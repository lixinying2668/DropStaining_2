using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MachineExecutor(IRuntimeEventPublisher eventPublisher)
{
    private readonly Channel<MachineExecutorCommand> commands = Channel.CreateUnbounded<MachineExecutorCommand>();
    private readonly ConcurrentDictionary<string, ControlFlags> flags = new(StringComparer.Ordinal);
    private readonly TimeSpan mockDelay = TimeSpan.FromMilliseconds(35);
    private IServiceScopeFactory? scopeFactory;

    public void Attach(IServiceScopeFactory serviceScopeFactory)
    {
        scopeFactory = serviceScopeFactory;
    }

    public ValueTask EnqueueStartAsync(string runId, CancellationToken cancellationToken = default)
    {
        return commands.Writer.WriteAsync(new MachineExecutorCommand(runId, MachineExecutorCommandType.Start, null), cancellationToken);
    }

    public ValueTask EnqueueResumeAsync(string runId, CancellationToken cancellationToken = default)
    {
        var control = flags.GetOrAdd(runId, _ => new ControlFlags());
        control.PauseRequested = false;
        return commands.Writer.WriteAsync(new MachineExecutorCommand(runId, MachineExecutorCommandType.Resume, null), cancellationToken);
    }

    public ValueTask EnqueueRedoAsync(string runId, string reason, CancellationToken cancellationToken = default)
    {
        var control = flags.GetOrAdd(runId, _ => new ControlFlags());
        control.FaultMessage = null;
        return commands.Writer.WriteAsync(new MachineExecutorCommand(runId, MachineExecutorCommandType.Redo, reason), cancellationToken);
    }

    public void RequestPause(string runId)
    {
        flags.GetOrAdd(runId, _ => new ControlFlags()).PauseRequested = true;
    }

    public void RequestStop(string runId)
    {
        flags.GetOrAdd(runId, _ => new ControlFlags()).StopRequested = true;
    }

    public void RequestFault(string runId, string message)
    {
        flags.GetOrAdd(runId, _ => new ControlFlags()).FaultMessage = string.IsNullOrWhiteSpace(message) ? "Injected fault." : message.Trim();
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (scopeFactory is null)
        {
            throw new InvalidOperationException("MachineExecutor scope factory was not attached.");
        }

        await foreach (var command in commands.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
                await ProcessCommandAsync(dbContext, command, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await RecordExecutorExceptionAsync(command.RunId, ex);
            }
        }
    }

    private async Task RecordExecutorExceptionAsync(string runId, Exception exception)
    {
        if (scopeFactory is null)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var run = await LoadRunAsync(dbContext, runId, CancellationToken.None);
            if (run is null)
            {
                return;
            }

            run.Status = RuntimeLedgerStatus.Faulted;
            run.FaultMessage = exception.Message;
            var currentStep = run.WorkflowExecutions
                .SelectMany(x => x.StepExecutions)
                .FirstOrDefault(x => x.Status == RuntimeLedgerStatus.Running)
                ?? run.WorkflowExecutions
                    .SelectMany(x => x.StepExecutions)
                    .FirstOrDefault(x => x.Status == RuntimeLedgerStatus.Pending);
            if (currentStep is not null)
            {
                currentStep.Status = RuntimeLedgerStatus.Unknown;
            }

            await AddAlarmAsync(dbContext, runId, "executor_exception", "Critical", exception.Message, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            eventPublisher.Publish(runId, "run.executor_exception", exception.Message);
        }
        catch
        {
            // The executor must not crash the web host while trying to record its own failure.
        }
    }

    private async Task ProcessCommandAsync(StainerDbContext dbContext, MachineExecutorCommand command, CancellationToken cancellationToken)
    {
        switch (command.Type)
        {
            case MachineExecutorCommandType.Start:
            case MachineExecutorCommandType.Resume:
                await ExecuteRunUntilBlockedAsync(dbContext, command.RunId, cancellationToken);
                break;
            case MachineExecutorCommandType.Redo:
                await RedoCurrentMajorStepAsync(dbContext, command.RunId, command.Payload ?? "Redo requested.", cancellationToken);
                await ExecuteRunUntilBlockedAsync(dbContext, command.RunId, cancellationToken);
                break;
        }
    }

    private async Task ExecuteRunUntilBlockedAsync(StainerDbContext dbContext, string runId, CancellationToken cancellationToken)
    {
        var control = flags.GetOrAdd(runId, _ => new ControlFlags());
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null
            || run.Status == RuntimeLedgerStatus.Completed
            || run.Status == RuntimeLedgerStatus.Stopped
            || run.Status == RuntimeLedgerStatus.Faulted)
        {
            return;
        }

        run.Status = RuntimeLedgerStatus.Running;
        run.StartedAtUtc ??= DateTimeOffset.UtcNow;
        run.PauseRequested = false;
        run.StopRequested = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(runId, "run.running", "Run started or resumed.");

        while (!cancellationToken.IsCancellationRequested)
        {
            run = await LoadRunAsync(dbContext, runId, cancellationToken);
            if (run is null)
            {
                return;
            }

            if (run.Status == RuntimeLedgerStatus.Faulted)
            {
                return;
            }

            var step = run.WorkflowExecutions
                .SelectMany(x => x.StepExecutions)
                .Where(x => x.Status == RuntimeLedgerStatus.Pending)
                .OrderBy(x => x.WorkflowExecution!.SlideTask!.SlotCode)
                .ThenBy(x => x.StepNo)
                .FirstOrDefault();

            if (step is null)
            {
                await CompleteRunAsync(dbContext, run, cancellationToken);
                return;
            }

            var stepCompleted = await ExecuteStepAsync(dbContext, run, step, cancellationToken);
            if (!stepCompleted)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(control.FaultMessage))
            {
                await FaultRunAsync(dbContext, run.Id, step.Id, control.FaultMessage, cancellationToken);
                control.FaultMessage = null;
                return;
            }

            if (control.StopRequested)
            {
                await StopRunAsync(dbContext, run.Id, cancellationToken);
                control.StopRequested = false;
                return;
            }

            if (control.PauseRequested)
            {
                await PauseRunAsync(dbContext, run.Id, cancellationToken);
                return;
            }
        }
    }

    private async Task<bool> ExecuteStepAsync(StainerDbContext dbContext, MachineRun run, WorkflowStepExecution step, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        run.CurrentMajorStepCode = step.MajorStepCode;
        step.Status = RuntimeLedgerStatus.Running;
        step.StartedAtUtc ??= now;
        step.WorkflowExecution!.Status = RuntimeLedgerStatus.Running;
        step.WorkflowExecution.StartedAtUtc ??= now;
        step.WorkflowExecution.SlideTask!.Status = RuntimeLedgerStatus.Running;
        step.WorkflowExecution.SlideTask.ChannelBatch!.Status = RuntimeLedgerStatus.Running;

        var command = new DeviceCommandExecution
        {
            MachineRunId = run.Id,
            WorkflowStepExecutionId = step.Id,
            CommandType = step.ActionType,
            Status = DeviceCommandStatus.Planned,
            PayloadJson = JsonSerializer.Serialize(new
            {
                step.StepNo,
                step.MajorStepCode,
                step.ReagentCode,
                step.VolumeUl
            }),
            CreatedAtUtc = now
        };
        dbContext.DeviceCommandExecutions.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);

        command.Status = DeviceCommandStatus.CommandSent;
        command.CommandSentAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await Task.Delay(mockDelay, cancellationToken);

        command.Status = DeviceCommandStatus.Acknowledged;
        command.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var ok = await ApplyMockActionAsync(dbContext, run, step, command, cancellationToken);
        command.Status = ok ? DeviceCommandStatus.Completed : DeviceCommandStatus.Failed;
        command.CompletedAtUtc = DateTimeOffset.UtcNow;
        command.ResultJson = JsonSerializer.Serialize(new { ok });

        step.Status = ok ? RuntimeLedgerStatus.Completed : RuntimeLedgerStatus.Failed;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(run.Id, ok ? "step.completed" : "step.failed", $"{step.MajorStepCode}/{step.StepName}");

        if (!ok)
        {
            await FaultRunAsync(dbContext, run.Id, step.Id, "Mock step failed.", cancellationToken);
            return false;
        }

        return true;
    }

    private async Task<bool> ApplyMockActionAsync(
        StainerDbContext dbContext,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        CancellationToken cancellationToken)
    {
        if (IsDabStep(step))
        {
            return await ApplyDabAsync(dbContext, run, step, command, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0)
        {
            return await ConsumeReagentAsync(dbContext, run, step, command, step.ReagentCode!, step.VolumeUl!.Value, cancellationToken);
        }

        return true;
    }

    private async Task<bool> ConsumeReagentAsync(
        StainerDbContext dbContext,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        string reagentCode,
        int volumeUl,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var placements = await dbContext.ReagentRackPlacements
            .Where(x => x.RemovedAtUtc == null)
            .Include(x => x.ReagentRackPosition)
            .Include(x => x.ReagentBottle)
            .Where(x => x.ReagentBottle!.ReagentCode == reagentCode
                && x.ReagentBottle.Status == "Available"
                && x.ReagentBottle.ExpirationDate >= today
                && x.ReagentBottle.RemainingVolumeUl > 0)
            .ToListAsync(cancellationToken);

        var available = placements.Sum(x => x.ReagentBottle!.RemainingVolumeUl);
        if (available < volumeUl)
        {
            await AddAlarmAsync(dbContext, run.Id, "reagent_insufficient", "Critical", $"Reagent {reagentCode} is insufficient. Required {volumeUl} ul, available {available} ul.", cancellationToken);
            return false;
        }

        var remaining = volumeUl;
        foreach (var placement in placements.OrderBy(x => x.ReagentBottle!.RemainingVolumeUl))
        {
            if (remaining <= 0)
            {
                break;
            }

            var bottle = placement.ReagentBottle!;
            var used = Math.Min(remaining, bottle.RemainingVolumeUl);
            bottle.RemainingVolumeUl -= used;
            bottle.UpdatedAtUtc = DateTimeOffset.UtcNow;
            remaining -= used;

            dbContext.ReagentConsumptions.Add(new ReagentConsumption
            {
                MachineRunId = run.Id,
                WorkflowStepExecutionId = step.Id,
                ReagentBottleId = bottle.Id,
                ReagentCode = reagentCode,
                VolumeUl = used,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.DispenseExecutions.Add(new DispenseExecution
            {
                DeviceCommandExecutionId = command.Id,
                ReagentBottleId = bottle.Id,
                ReagentCode = reagentCode,
                VolumeUl = used,
                SourcePositionCode = placement.ReagentRackPosition?.Code,
                TargetSlotCode = step.WorkflowExecution!.SlideTask!.SlotCode,
                Status = DeviceCommandStatus.Completed,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "run.reagent_consumption",
                EntityType = "ReagentBottle",
                EntityId = bottle.Id,
                Message = JsonSerializer.Serialize(new { runId = run.Id, reagentCode, volumeUl = used, remainingUl = bottle.RemainingVolumeUl }),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            if (bottle.RemainingVolumeUl == 0)
            {
                await AddAlarmAsync(dbContext, run.Id, "reagent_depleted", "Warning", $"Bottle {bottle.FullBarcode} for {reagentCode} is depleted.", cancellationToken);
            }
        }

        return true;
    }

    private async Task<bool> ApplyDabAsync(
        StainerDbContext dbContext,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var availableBatches = await dbContext.DabBatches
            .Where(x => x.Status == RuntimeLedgerStatus.Available)
            .ToListAsync(cancellationToken);
        var expired = availableBatches
            .Where(x => x.ExpiresAtUtc <= now)
            .ToList();
        if (expired.Count > 0)
        {
            foreach (var batch in expired)
            {
                batch.Status = RuntimeLedgerStatus.Expired;
            }

            await AddAlarmAsync(dbContext, run.Id, "dab_expired", "Critical", "A DAB batch is expired. Clean DAB mix positions before continuing.", cancellationToken);
            return false;
        }

        var volume = Math.Max(step.VolumeUl ?? 100, 100);
        var dabBatch = availableBatches
            .Where(x => x.ExpiresAtUtc > now && x.RemainingVolumeUl >= volume)
            .OrderBy(x => x.ExpiresAtUtc)
            .FirstOrDefault();

        if (dabBatch is null)
        {
            var occupiedCodes = await dbContext.DabBatches
                .Where(x => x.Status == RuntimeLedgerStatus.Available)
                .Select(x => x.PositionCode)
                .ToListAsync(cancellationToken);
            var position = await dbContext.DabMixPositions
                .OrderBy(x => x.PositionNo)
                .FirstOrDefaultAsync(x => !occupiedCodes.Contains(x.Code), cancellationToken);
            if (position is null)
            {
                await AddAlarmAsync(dbContext, run.Id, "dab_positions_full", "Critical", "All DAB mix positions M1-M8 are occupied. Cleaning is required.", cancellationToken);
                return false;
            }

            dabBatch = new DabBatch
            {
                DabMixPositionId = position.Id,
                PositionCode = position.Code,
                Status = RuntimeLedgerStatus.Available,
                RemainingVolumeUl = 1000,
                PreparedAtUtc = now,
                ExpiresAtUtc = now.AddHours(3),
                CreatedAtUtc = now
            };
            dbContext.DabBatches.Add(dabBatch);
            dbContext.DeviceCommandExecutions.Add(new DeviceCommandExecution
            {
                MachineRunId = run.Id,
                WorkflowStepExecutionId = step.Id,
                CommandType = "MockDabPrepare",
                Status = DeviceCommandStatus.Completed,
                PayloadJson = JsonSerializer.Serialize(new { position.Code, volumeUl = dabBatch.RemainingVolumeUl }),
                ResultJson = "{\"ok\":true}",
                CreatedAtUtc = now,
                CommandSentAtUtc = now,
                AcknowledgedAtUtc = now,
                CompletedAtUtc = now
            });
        }

        dabBatch.RemainingVolumeUl -= volume;
        dbContext.DabBatchUsages.Add(new DabBatchUsage
        {
            DabBatch = dabBatch,
            MachineRunId = run.Id,
            WorkflowStepExecutionId = step.Id,
            VolumeUl = volume,
            CreatedAtUtc = now
        });
        dbContext.DispenseExecutions.Add(new DispenseExecution
        {
            DeviceCommandExecutionId = command.Id,
            ReagentCode = "DAB",
            VolumeUl = volume,
            SourcePositionCode = dabBatch.PositionCode,
            TargetSlotCode = step.WorkflowExecution!.SlideTask!.SlotCode,
            Status = DeviceCommandStatus.Completed,
            CreatedAtUtc = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "run.dab_consumption",
            EntityType = "DabBatch",
            EntityId = dabBatch.Id,
            Message = JsonSerializer.Serialize(new { runId = run.Id, volumeUl = volume, remainingUl = dabBatch.RemainingVolumeUl }),
            CreatedAtUtc = now
        });
        return true;
    }

    private async Task RedoCurrentMajorStepAsync(StainerDbContext dbContext, string runId, string reason, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null || run.Status != RuntimeLedgerStatus.Faulted)
        {
            return;
        }

        var targetStep = run.WorkflowExecutions
            .SelectMany(x => x.StepExecutions)
            .Where(x => x.Status is RuntimeLedgerStatus.Unknown or RuntimeLedgerStatus.Failed)
            .OrderByDescending(x => x.StartedAtUtc ?? x.CreatedAtUtc)
            .FirstOrDefault();
        if (targetStep is null)
        {
            return;
        }

        var major = targetStep.MajorStepCode;
        var majorSteps = run.WorkflowExecutions
            .SelectMany(x => x.StepExecutions)
            .Where(x => x.WorkflowExecutionId == targetStep.WorkflowExecutionId && x.MajorStepCode == major)
            .ToList();

        if (!await ValidateMockDeviceStateAsync(dbContext, run, cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var step in majorSteps)
        {
            if (!await ValidateStepResourcesAsync(dbContext, run, step, cancellationToken))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            step.Status = RuntimeLedgerStatus.Pending;
            step.StartedAtUtc = null;
            step.CompletedAtUtc = null;
            step.RedoCount++;
        }

        run.Status = RuntimeLedgerStatus.Running;
        run.FaultMessage = null;
        run.CurrentMajorStepCode = major;
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "run.redo_major_step",
            EntityType = "MachineRun",
            EntityId = run.Id,
            Message = JsonSerializer.Serialize(new { majorStepCode = major, reason }),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(runId, "run.redo", $"Redo major step {major}.");
    }

    private async Task<bool> ValidateMockDeviceStateAsync(StainerDbContext dbContext, MachineRun run, CancellationToken cancellationToken)
    {
        var hasActiveDeviceProfile = await dbContext.DeviceProfiles
            .AsNoTracking()
            .AnyAsync(x => x.IsActive, cancellationToken);
        var hasActiveCoordinateProfile = await dbContext.CoordinateProfiles
            .AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Status == "Active", cancellationToken);
        if (hasActiveDeviceProfile && hasActiveCoordinateProfile)
        {
            return true;
        }

        await AddAlarmAsync(
            dbContext,
            run.Id,
            "redo_device_not_ready",
            "Critical",
            "Mock device state is not ready before redo. Active device and coordinate profiles are required.",
            cancellationToken);
        return false;
    }

    private async Task<bool> ValidateStepResourcesAsync(StainerDbContext dbContext, MachineRun run, WorkflowStepExecution step, CancellationToken cancellationToken)
    {
        if (IsDabStep(step))
        {
            var availableBatches = await dbContext.DabBatches
                .Where(x => x.Status == RuntimeLedgerStatus.Available)
                .ToListAsync(cancellationToken);
            var expired = availableBatches.Any(x => x.ExpiresAtUtc <= DateTimeOffset.UtcNow);
            if (expired)
            {
                await AddAlarmAsync(dbContext, run.Id, "redo_dab_expired", "Critical", "DAB is expired before redo.", cancellationToken);
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0 && !IsDabStep(step))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var available = await dbContext.ReagentRackPlacements
                .Where(x => x.RemovedAtUtc == null)
                .Include(x => x.ReagentBottle)
                .Where(x => x.ReagentBottle!.ReagentCode == step.ReagentCode
                    && x.ReagentBottle.Status == "Available"
                    && x.ReagentBottle.ExpirationDate >= today)
                .SumAsync(x => x.ReagentBottle!.RemainingVolumeUl, cancellationToken);
            if (available < step.VolumeUl)
            {
                await AddAlarmAsync(dbContext, run.Id, "redo_reagent_insufficient", "Critical", $"Reagent {step.ReagentCode} is insufficient before redo.", cancellationToken);
                return false;
            }
        }

        return true;
    }

    private async Task PauseRunAsync(StainerDbContext dbContext, string runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.MachineRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        run.Status = RuntimeLedgerStatus.Paused;
        run.PauseRequested = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(runId, "run.paused", "Run paused after current atomic action.");
    }

    private async Task StopRunAsync(StainerDbContext dbContext, string runId, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Status = RuntimeLedgerStatus.Stopped;
        run.StopRequested = true;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        foreach (var step in run.WorkflowExecutions.SelectMany(x => x.StepExecutions).Where(x => x.Status == RuntimeLedgerStatus.Pending))
        {
            step.Status = RuntimeLedgerStatus.Stopped;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(runId, "run.stopped", "Run stopped after current atomic action.");
    }

    private async Task FaultRunAsync(StainerDbContext dbContext, string runId, string stepId, string message, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        var step = run.WorkflowExecutions.SelectMany(x => x.StepExecutions).FirstOrDefault(x => x.Id == stepId);
        if (step is not null)
        {
            step.Status = RuntimeLedgerStatus.Unknown;
        }

        run.Status = RuntimeLedgerStatus.Faulted;
        run.FaultMessage = message;
        await AddAlarmAsync(dbContext, runId, "mock_fault", "Critical", message, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(runId, "run.faulted", message);
    }

    private async Task CompleteRunAsync(StainerDbContext dbContext, MachineRun run, CancellationToken cancellationToken)
    {
        run.Status = RuntimeLedgerStatus.Completed;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        foreach (var workflow in run.WorkflowExecutions)
        {
            workflow.Status = RuntimeLedgerStatus.Completed;
            workflow.CompletedAtUtc = DateTimeOffset.UtcNow;
            workflow.SlideTask!.Status = RuntimeLedgerStatus.WaitingUnload;
            workflow.SlideTask.ChannelBatch!.Status = RuntimeLedgerStatus.Completed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        eventPublisher.Publish(run.Id, "run.completed", "Run completed and slides are waiting unload.");
    }

    private async Task AddAlarmAsync(StainerDbContext dbContext, string runId, string code, string severity, string message, CancellationToken cancellationToken)
    {
        if (!await dbContext.Alarms.AnyAsync(x => x.MachineRunId == runId && x.Code == code && x.Status == "Active", cancellationToken))
        {
            dbContext.Alarms.Add(new Alarm
            {
                MachineRunId = runId,
                Code = code,
                Severity = severity,
                Message = message,
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task<MachineRun?> LoadRunAsync(StainerDbContext dbContext, string runId, CancellationToken cancellationToken)
    {
        return await dbContext.MachineRuns
            .Include(x => x.ChannelBatches)
            .ThenInclude(x => x.SlideTasks)
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.SlideTask)
            .ThenInclude(x => x!.ChannelBatch)
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.StepExecutions)
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
    }

    private static bool IsDabStep(WorkflowStepExecution step)
    {
        return step.MajorStepCode.Contains("DAB", StringComparison.OrdinalIgnoreCase)
            || step.ActionType.Contains("DAB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(step.ReagentCode, "DAB", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MachineExecutorCommand(string RunId, string Type, string? Payload);

    private static class MachineExecutorCommandType
    {
        public const string Start = "Start";
        public const string Resume = "Resume";
        public const string Redo = "Redo";
    }

    private sealed class ControlFlags
    {
        public volatile bool PauseRequested;
        public volatile bool StopRequested;
        public string? FaultMessage;
    }
}

public sealed class MachineExecutorHostedService(MachineExecutor executor, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        executor.Attach(scopeFactory);
        await executor.RunAsync(stoppingToken);
    }
}
