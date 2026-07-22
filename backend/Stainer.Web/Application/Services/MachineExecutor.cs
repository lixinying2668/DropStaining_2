using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.Devices;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MachineExecutor(IRuntimeEventPublisher eventPublisher, IDeviceAdapter deviceAdapter, IConfiguration configuration, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultStepVisibleDelayMs = 1500;
    private readonly int stepVisibleDelayMs = ResolveStepVisibleDelay(configuration, environment);
    private readonly Channel<MachineExecutorCommand> commands = Channel.CreateUnbounded<MachineExecutorCommand>();
    private readonly ConcurrentDictionary<string, ControlFlags> flags = new(StringComparer.Ordinal);
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
                var dabLifecycleService = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
                var fluidicsControlService = scope.ServiceProvider.GetRequiredService<FluidicsControlService>();
                var motionControlService = scope.ServiceProvider.GetRequiredService<MotionControlService>();
                var communicationPersistence = scope.ServiceProvider.GetRequiredService<DeviceCommunicationPersistenceService>();
                await ProcessCommandAsync(dbContext, dabLifecycleService, fluidicsControlService, motionControlService, communicationPersistence, command, stoppingToken);
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

    private async Task ProcessCommandAsync(
        StainerDbContext dbContext,
        DabLifecycleService dabLifecycleService,
        FluidicsControlService fluidicsControlService,
        MotionControlService motionControlService,
        DeviceCommunicationPersistenceService communicationPersistence,
        MachineExecutorCommand command,
        CancellationToken cancellationToken)
    {
        switch (command.Type)
        {
            case MachineExecutorCommandType.Start:
            case MachineExecutorCommandType.Resume:
                await ExecuteRunUntilBlockedAsync(dbContext, dabLifecycleService, fluidicsControlService, motionControlService, communicationPersistence, command.RunId, cancellationToken);
                break;
            case MachineExecutorCommandType.Redo:
                await RedoCurrentMajorStepAsync(dbContext, command.RunId, command.Payload ?? "Redo requested.", cancellationToken);
                await ExecuteRunUntilBlockedAsync(dbContext, dabLifecycleService, fluidicsControlService, motionControlService, communicationPersistence, command.RunId, cancellationToken);
                break;
        }
    }

    private async Task ExecuteRunUntilBlockedAsync(
        StainerDbContext dbContext,
        DabLifecycleService dabLifecycleService,
        FluidicsControlService fluidicsControlService,
        MotionControlService motionControlService,
        DeviceCommunicationPersistenceService communicationPersistence,
        string runId,
        CancellationToken cancellationToken)
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

        var now = DateTimeOffset.UtcNow;
        run.Status = RuntimeLedgerStatus.Running;
        run.StartedAtUtc ??= now;
        run.PauseRequested = false;
        run.StopRequested = false;
        foreach (var batch in run.ChannelBatches)
        {
            batch.Status = RuntimeLedgerStatus.Running;
            batch.StartedAtUtc ??= now;
            batch.WorkflowLockedAtUtc ??= now;
            batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Locked;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        PublishMachineState(run, "Run started or resumed.");
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DeviceConnectionChanged,
            run.Id,
            "MachineRun",
            run.Id,
            "admin",
            new Dictionary<string, object?>
            {
                ["connected"] = true,
                ["adapter"] = deviceAdapter.Name,
                ["mode"] = deviceAdapter.Mode,
                ["message"] = $"{deviceAdapter.Name} is selected."
            }));

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

            // Only expose the first pending step of each slide as executable.  This
            // preserves the configured Timeline within a slide and then advances the
            // same Timeline step across all slides in deterministic physical order.
            // Selecting from every pending step allowed a blocked mixer step to be
            // skipped in favour of a later step from the same slide.
            var pendingSteps = run.WorkflowExecutions
                .Select(x => x.StepExecutions
                    .Where(step => step.Status == RuntimeLedgerStatus.Pending)
                    .OrderBy(step => step.StepNo)
                    .ThenBy(step => step.CreatedAtUtc)
                    .FirstOrDefault())
                .Where(x => x is not null)
                .Select(x => x!)
                .OrderBy(x => x.StepNo)
                .ThenBy(x => x.WorkflowExecution!.SlideTask!.ChannelBatch!.DrawerCode)
                .ThenBy(x => ParseSlotNo(x.WorkflowExecution!.SlideTask!.SlotCode))
                .ThenBy(x => x.WorkflowExecution!.SlideTask!.SlotCode)
                .ToList();

            if (pendingSteps.Count == 0)
            {
                await CompleteRunAsync(dbContext, run, cancellationToken);
                return;
            }

            var step = SelectNextExecutableStep(run, pendingSteps);
            if (step is null)
            {
                var blocked = pendingSteps.First();
                await FaultRunAsync(
                    dbContext,
                    run.Id,
                    blocked.Id,
                    "Mixer step is waiting for all same-channel slides in this round to complete liquid addition.",
                    cancellationToken,
                    "mixer_prerequisite_not_met",
                    markUnknown: false);
                return;
            }

            var stepCompleted = await ExecuteStepAsync(dbContext, dabLifecycleService, fluidicsControlService, motionControlService, communicationPersistence, run, step, cancellationToken);
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

    private async Task<bool> ExecuteStepAsync(
        StainerDbContext dbContext,
        DabLifecycleService dabLifecycleService,
        FluidicsControlService fluidicsControlService,
        MotionControlService motionControlService,
        DeviceCommunicationPersistenceService communicationPersistence,
        MachineRun run,
        WorkflowStepExecution step,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        run.CurrentMajorStepCode = step.MajorStepCode;
        step.Status = RuntimeLedgerStatus.Running;
        step.StartedAtUtc ??= now;
        step.WorkflowExecution!.Status = RuntimeLedgerStatus.Running;
        step.WorkflowExecution.StartedAtUtc ??= now;
        step.WorkflowExecution.SlideTask!.Status = RuntimeLedgerStatus.Running;
        step.WorkflowExecution.SlideTask.ChannelBatch!.Status = RuntimeLedgerStatus.Running;

        var requiresLiquidClass = IsLiquidClassAction(step.ActionType)
            || (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0);
        var liquidClass = requiresLiquidClass
            ? LiquidClassSnapshotFactory.FindForCommand(step.WorkflowExecution.SlideTask.ChannelBatch.LiquidClassSnapshotJson, step.ReagentCode)
            : null;
        var liquidParametersJson = liquidClass is null ? "{}" : JsonSerializer.Serialize(liquidClass.Parameters, JsonOptions);

        var command = new DeviceCommandExecution
        {
            MachineRunId = run.Id,
            WorkflowStepExecutionId = step.Id,
            CommandType = step.ActionType,
            Status = DeviceCommandStatus.Planned,
            LiquidClassVersionId = liquidClass?.LiquidClassVersionId,
            LiquidClassVersionNo = liquidClass?.VersionNo,
            LiquidClassParametersJson = liquidParametersJson,
            LiquidClassSelectionStatus = requiresLiquidClass
                ? liquidClass is null ? LiquidClassSelectionStatus.NeedsManualResolution : LiquidClassSelectionStatus.Frozen
                : LiquidClassSelectionStatus.NotApplicable,
            PayloadJson = JsonSerializer.Serialize(new
            {
                step.StepNo,
                step.MajorStepCode,
                step.ReagentCode,
                step.VolumeUl,
                step.TargetTemperatureDeciC,
                liquidClassVersionId = liquidClass?.LiquidClassVersionId,
                liquidClassVersionNo = liquidClass?.VersionNo,
                liquidClassParameters = liquidClass?.Parameters,
                liquidOperations = liquidClass is null ? Array.Empty<string>() : new[] { "LiquidDetect", "Aspirate", "Dispense", "Blowout" }
            }),
            CreatedAtUtc = now
        };
        dbContext.DeviceCommandExecutions.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (requiresLiquidClass && liquidClass is null)
        {
            command.Status = DeviceCommandStatus.Failed;
            command.CompletedAtUtc = DateTimeOffset.UtcNow;
            command.ResultJson = JsonSerializer.Serialize(new { ok = false, errorCode = "liquid_class_snapshot_missing" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await FaultRunAsync(dbContext, run.Id, step.Id, "Frozen Liquid Class snapshot is missing for the pipetting command.", cancellationToken, "liquid_class_snapshot_missing");
            return false;
        }
        PublishSlideTaskState(run.Id, step.WorkflowExecution.SlideTask, step.StepName);
        PublishWorkflowStep(run.Id, step, MachineEventTypes.WorkflowStepStarted);

        var leaseResult = await TryAcquireResourcesAsync(dbContext, run, step, command, cancellationToken);
        if (!leaseResult.Ok)
        {
            command.Status = DeviceCommandStatus.Failed;
            command.CompletedAtUtc = DateTimeOffset.UtcNow;
            command.ResultJson = JsonSerializer.Serialize(new { ok = false, errorCode = leaseResult.ErrorCode, message = leaseResult.Message });
            step.Status = RuntimeLedgerStatus.Failed;
            step.CompletedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await FaultRunAsync(dbContext, run.Id, step.Id, leaseResult.Message, cancellationToken, leaseResult.ErrorCode ?? "resource_waiting", markUnknown: false);
            return false;
        }

        command.Status = DeviceCommandStatus.CommandSent;
        command.CommandSentAtUtc = DateTimeOffset.UtcNow;
        var operationRequest = BuildDeviceOperationRequest(run, step, command);
        var communicationRecord = communicationPersistence.Begin(operationRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        var deviceResult = await ExecuteDeviceActionAsync(step, operationRequest, cancellationToken);
        await communicationPersistence.TryPersistCompletionAsync(communicationRecord, deviceResult, cancellationToken);
        if (deviceResult.Acknowledged)
        {
            command.Status = DeviceCommandStatus.DeviceAcknowledged;
            command.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Mock/Twin 可视化：让每个步骤保持一段可见时长，前端机械臂动画才来得及展示“到试剂位吸液→排液”等过程。
        // Real 模式设备动作 fail-closed，不会执行到此处。值可调（毫秒）。
        if (deviceResult.Ok && stepVisibleDelayMs > 0)
        {
            await Task.Delay(stepVisibleDelayMs, cancellationToken);
        }

        var deviceOutcomeUnknown = deviceResult.Status is DeviceCommandStatuses.Unknown or DeviceCommandStatuses.TimedOut
            || (IsDabStep(step)
                && deviceResult.Data.TryGetValue("faultType", out var faultType)
                && string.Equals(Convert.ToString(faultType), DeviceFaultTypes.Disconnect, StringComparison.OrdinalIgnoreCase));
        await RecordFluidicsDeviceFailureIfNeededAsync(fluidicsControlService, run, step, command, deviceResult, cancellationToken);
        await RecordMotionDeviceFailureIfNeededAsync(motionControlService, run, step, command, deviceResult, cancellationToken);
        var businessOk = false;
        if (deviceResult.Ok)
        {
            businessOk = await ApplyBusinessEffectsAsync(dbContext, dabLifecycleService, motionControlService, run, step, command, deviceResult, cancellationToken);
        }
        else if (IsDabStep(step))
        {
            var dabFailure = await dabLifecycleService.HandlePreparationNotCompletedFromDeviceAsync(
                run,
                step,
                deviceResult,
                deviceOutcomeUnknown,
                cancellationToken);
            if (!dabFailure.Ok)
            {
                await AddAlarmAsync(dbContext, run.Id, dabFailure.ErrorCode!, "Critical", dabFailure.Message, cancellationToken);
                if (dabFailure.Batch is not null)
                {
                    PublishDabBatchChanged(run.Id, dabFailure.Batch, deviceOutcomeUnknown ? "unknown" : "failed");
                }
            }
        }

        var ok = deviceResult.Ok && businessOk;
        command.Status = ok
            ? DeviceCommandStatus.Completed
            : deviceOutcomeUnknown ? DeviceCommandStatus.Unknown : DeviceCommandStatus.Failed;
        command.CompletedAtUtc = DateTimeOffset.UtcNow;
        command.ResultJson = JsonSerializer.Serialize(new
        {
            ok,
            adapter = deviceAdapter.Name,
            mode = deviceAdapter.Mode,
            deviceResult.Status,
            deviceResult.ErrorCode,
            deviceResult.Message,
            deviceResult.StartedAtUtc,
            deviceResult.CompletedAtUtc,
            deviceResult.Data
        });

        step.Status = ok
            ? RuntimeLedgerStatus.Completed
            : deviceOutcomeUnknown ? RuntimeLedgerStatus.Unknown : RuntimeLedgerStatus.Failed;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        if (command.Status == DeviceCommandStatus.Unknown)
        {
            MarkResourcesUnknown(dbContext, command.Id, deviceResult.Message);
        }
        else
        {
            ReleaseResources(dbContext, command.Id);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        PublishWorkflowStep(run.Id, step, MachineEventTypes.WorkflowStepCompleted);
        PublishSlideTaskState(run.Id, step.WorkflowExecution.SlideTask, step.StepName);

        if (!ok)
        {
            await FaultRunAsync(
                dbContext,
                run.Id,
                step.Id,
                deviceResult.Message,
                cancellationToken,
                deviceOutcomeUnknown ? "device_command_unknown" : "device_command_failed",
                deviceOutcomeUnknown);
            return false;
        }

        return true;
    }

    private async Task<bool> ApplyBusinessEffectsAsync(
        StainerDbContext dbContext,
        DabLifecycleService dabLifecycleService,
        MotionControlService motionControlService,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        DeviceCommandResult deviceResult,
        CancellationToken cancellationToken)
    {
        if (IsDabStep(step))
        {
            return await ApplyDabAsync(dbContext, dabLifecycleService, motionControlService, run, step, command, deviceResult, cancellationToken);
        }

        if (IsTemperatureStep(step))
        {
            eventPublisher.Publish(MachineEventMessage.Create(
                MachineEventTypes.TemperatureChanged,
                run.Id,
                "WorkflowStepExecution",
                step.Id,
                null,
                new Dictionary<string, object?>
                {
                    ["workflowStepExecutionId"] = step.Id,
                    ["slideTaskId"] = step.WorkflowExecution?.SlideTaskId,
                    ["majorStepCode"] = step.MajorStepCode,
                    ["currentTemperatureDeciC"] = deviceResult.Data.GetValueOrDefault("currentTemperatureDeciC") ?? 420,
                    ["targetTemperatureDeciC"] = deviceResult.Data.GetValueOrDefault("targetTemperatureDeciC") ?? step.TargetTemperatureDeciC ?? 420,
                    ["thermalStatus"] = deviceResult.Data.GetValueOrDefault("status"),
                    ["adapter"] = deviceAdapter.Name,
                    ["message"] = deviceResult.Message
                }));
        }

        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0)
        {
            return await ConsumeReagentAsync(dbContext, run, step, command, step.ReagentCode!, step.VolumeUl!.Value, cancellationToken);
        }

        return true;
    }

    private static DeviceOperationRequest BuildDeviceOperationRequest(
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command)
    {
        var moduleCode = ResolveDeviceModule(step);
        return new DeviceOperationRequest(
            new DeviceCommandContext(command.Id, command.Id, "system", nameof(MachineExecutor)),
            moduleCode,
            step.ActionType,
            new Dictionary<string, object?>
            {
                ["machineRunId"] = command.MachineRunId,
                ["workflowStepExecutionId"] = step.Id,
                ["stepNo"] = step.StepNo,
                ["majorStepCode"] = step.MajorStepCode,
                ["reagentCode"] = step.ReagentCode,
                ["volumeUl"] = step.VolumeUl,
                ["adjustedVolumeUl"] = step.VolumeUl + (command.LiquidClassVersionId is null
                    ? 0
                    : LiquidClassSnapshotFactory.FindForCommand(step.WorkflowExecution!.SlideTask!.ChannelBatch!.LiquidClassSnapshotJson, step.ReagentCode)?.Parameters.VolumeAdjustmentUl ?? 0),
                ["liquidClassVersionId"] = command.LiquidClassVersionId,
                ["liquidClassVersionNo"] = command.LiquidClassVersionNo,
                ["liquidClassParametersJson"] = command.LiquidClassParametersJson,
                ["drawerCode"] = step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode,
                ["slotNo"] = ParseSlotNo(step.WorkflowExecution?.SlideTask?.SlotCode),
                ["slotCode"] = step.WorkflowExecution?.SlideTask?.SlotCode,
                ["stainingTaskId"] = step.WorkflowExecution?.SlideTask?.StainingTaskId,
                ["targetTemperatureDeciC"] = step.TargetTemperatureDeciC ?? 420,
                ["pwmChannelCode"] = DrawerToPwm(step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode),
                ["speedPercent"] = DefaultPumpSpeed(step.ActionType),
                ["durationMs"] = 25,
                ["targetPointCode"] = ResolveTargetPoint(step),
                ["coordinateProfileVersionId"] = run.CoordinateProfileVersionId ?? step.WorkflowExecution?.SlideTask?.ChannelBatch?.CoordinateProfileVersionId,
                ["coordinateSnapshotJson"] = string.IsNullOrWhiteSpace(run.CoordinateSnapshotJson) || run.CoordinateSnapshotJson == "{}"
                    ? step.WorkflowExecution?.SlideTask?.ChannelBatch?.CoordinateSnapshotJson
                    : run.CoordinateSnapshotJson,
                ["needleCode"] = PreferredNeedleForSlot(step),
                ["allowAutomaticWash"] = true,
                ["roundKey"] = $"{command.MachineRunId}:{step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode}:{step.MajorStepCode}"
            });
    }

    private async Task<DeviceCommandResult> ExecuteDeviceActionAsync(
        WorkflowStepExecution step,
        DeviceOperationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (IsDabStep(step))
            {
                return await deviceAdapter.PrepareDabAsync(request, cancellationToken);
            }

            if (IsTemperatureStep(step))
            {
                return await deviceAdapter.SetTemperatureAsync(request, cancellationToken);
            }

            var action = step.ActionType.ToLowerInvariant();
            if (action.Contains("level"))
            {
                return await deviceAdapter.ReadLiquidLevelsAsync(request, cancellationToken);
            }

            if (action.Contains("needle") && action.Contains("wash"))
            {
                return await deviceAdapter.WashNeedlesAsync(request, cancellationToken);
            }

            if (action.Contains("wash"))
            {
                return await deviceAdapter.RunPumpAsync(request, cancellationToken);
            }

            if (action.Contains("mix"))
            {
                return await deviceAdapter.MixAsync(request, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0)
            {
                return await deviceAdapter.PipetteAsync(request, cancellationToken);
            }

            return await deviceAdapter.ExecuteWorkflowActionAsync(request, cancellationToken);
        }
        catch (BusinessRuleException ex)
        {
            var now = DateTimeOffset.UtcNow;
            return new DeviceCommandResult(
                false,
                DeviceCommandStatuses.Failed,
                request.ModuleCode,
                step.ActionType,
                ex.Code,
                ex.Message,
                now,
                now,
                true,
                new Dictionary<string, object?> { ["businessRule"] = ex.Code });
        }
    }

    private static async Task RecordFluidicsDeviceFailureIfNeededAsync(
        FluidicsControlService fluidicsControlService,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        DeviceCommandResult deviceResult,
        CancellationToken cancellationToken)
    {
        if (deviceResult.Ok)
        {
            return;
        }

        var moduleCode = ResolveDeviceModule(step);
        if (!IsFluidicsModule(moduleCode) || !ShouldRecordFluidicsDeviceFailure(deviceResult))
        {
            return;
        }

        var drawerCode = step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode;
        await fluidicsControlService.RecordDeviceFailureFromExecutorAsync(
            moduleCode,
            deviceResult.Status,
            deviceResult.ErrorCode,
            deviceResult.Message,
            DrawerToPwm(drawerCode),
            drawerCode,
            ResolveLiquidSourceType(deviceResult),
            run.Id,
            step.Id,
            command.Id,
            cancellationToken);
    }

    private static async Task RecordMotionDeviceFailureIfNeededAsync(
        MotionControlService motionControlService,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        DeviceCommandResult deviceResult,
        CancellationToken cancellationToken)
    {
        if (deviceResult.Ok)
        {
            return;
        }

        var moduleCode = ResolveDeviceModule(step);
        if (!IsMotionModule(moduleCode) || !ShouldRecordMotionDeviceFailure(deviceResult))
        {
            return;
        }

        await motionControlService.RecordDeviceFailureFromExecutorAsync(
            moduleCode,
            deviceResult.Status,
            deviceResult.ErrorCode,
            deviceResult.Message,
            PreferredNeedleForSlot(step),
            run.Id,
            step.Id,
            command.Id,
            cancellationToken);
    }

    private async Task<ResourceLeaseResult> TryAcquireResourcesAsync(
        StainerDbContext dbContext,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        CancellationToken cancellationToken)
    {
        var resources = ResolveRequiredResources(step)
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        if (resources.Count == 0)
        {
            return ResourceLeaseResult.Succeeded();
        }

        foreach (var resource in resources)
        {
            var existing = await dbContext.MachineResourceLeases
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ResourceCode == resource.Code && x.Status == MachineResourceLeaseStatus.Acquired, cancellationToken);
            if (existing is not null && existing.DeviceCommandExecutionId != command.Id)
            {
                var reason = $"Resource {resource.Code} is held by command {existing.DeviceCommandExecutionId}.";
                dbContext.MachineResourceLeases.Add(new MachineResourceLease
                {
                    ResourceCode = resource.Code,
                    ResourceType = resource.Type,
                    Status = MachineResourceLeaseStatus.Waiting,
                    MachineRunId = run.Id,
                    WorkflowStepExecutionId = step.Id,
                    DeviceCommandExecutionId = command.Id,
                    CommandType = command.CommandType,
                    WaitReason = reason,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "resource.waiting",
                    EntityType = "MachineResourceLease",
                    EntityId = command.Id,
                    Message = JsonSerializer.Serialize(new { runId = run.Id, stepId = step.Id, commandId = command.Id, resource.Code, resource.Type, reason }),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                return ResourceLeaseResult.Failed("resource_waiting", reason);
            }
        }

        foreach (var resource in resources)
        {
            var alreadyHeld = await dbContext.MachineResourceLeases
                .AnyAsync(x => x.ResourceCode == resource.Code && x.Status == MachineResourceLeaseStatus.Acquired && x.DeviceCommandExecutionId == command.Id, cancellationToken);
            if (alreadyHeld)
            {
                continue;
            }

            dbContext.MachineResourceLeases.Add(new MachineResourceLease
            {
                ResourceCode = resource.Code,
                ResourceType = resource.Type,
                Status = MachineResourceLeaseStatus.Acquired,
                MachineRunId = run.Id,
                WorkflowStepExecutionId = step.Id,
                DeviceCommandExecutionId = command.Id,
                CommandType = command.CommandType,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                AcquiredAtUtc = DateTimeOffset.UtcNow
            });
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "resource.acquired",
            EntityType = "DeviceCommandExecution",
            EntityId = command.Id,
            Message = JsonSerializer.Serialize(new { runId = run.Id, stepId = step.Id, commandId = command.Id, resources = resources.Select(x => x.Code).ToArray() }),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ResourceLeaseResult.Succeeded();
    }

    private static void ReleaseResources(StainerDbContext dbContext, string commandId)
    {
        var now = DateTimeOffset.UtcNow;
        var leases = dbContext.MachineResourceLeases.Local
            .Where(x => x.DeviceCommandExecutionId == commandId && x.Status == MachineResourceLeaseStatus.Acquired)
            .ToList();
        foreach (var lease in leases)
        {
            lease.Status = MachineResourceLeaseStatus.Released;
            lease.ReleasedAtUtc = now;
        }

        if (leases.Count > 0)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "resource.released",
                EntityType = "DeviceCommandExecution",
                EntityId = commandId,
                Message = JsonSerializer.Serialize(new { commandId, resources = leases.Select(x => x.ResourceCode).ToArray() }),
                CreatedAtUtc = now
            });
        }
    }

    private static void MarkResourcesUnknown(StainerDbContext dbContext, string commandId, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var leases = dbContext.MachineResourceLeases.Local
            .Where(x => x.DeviceCommandExecutionId == commandId && x.Status == MachineResourceLeaseStatus.Acquired)
            .ToList();
        foreach (var lease in leases)
        {
            lease.Status = MachineResourceLeaseStatus.NeedsManualResolution;
            lease.WaitReason = reason;
        }

        if (leases.Count > 0)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "resource.needs_manual_resolution",
                EntityType = "DeviceCommandExecution",
                EntityId = commandId,
                Message = JsonSerializer.Serialize(new { commandId, resources = leases.Select(x => x.ResourceCode).ToArray(), reason }),
                CreatedAtUtc = now
            });
        }
    }

    private static IReadOnlyList<ResourceRequest> ResolveRequiredResources(WorkflowStepExecution step)
    {
        var moduleCode = ResolveDeviceModule(step);
        var resources = new List<ResourceRequest>();
        if (moduleCode is DeviceModules.RobotArm or DeviceModules.Pipette or DeviceModules.NeedleWash or DeviceModules.Dab)
        {
            resources.Add(new ResourceRequest(MachineResourceTypes.Platform, "Platform:RobotArm"));
        }

        if (moduleCode == DeviceModules.Pipette)
        {
            var needle = PreferredNeedleForSlot(step) ?? NeedleCodes.Needle1;
            resources.Add(new ResourceRequest(MachineResourceTypes.Needle, $"Needle:{needle}"));
            resources.Add(new ResourceRequest(MachineResourceTypes.WashStation, "WashStation:NeedleWash"));
            if (!string.IsNullOrWhiteSpace(step.ReagentCode))
            {
                resources.Add(new ResourceRequest(MachineResourceTypes.Source, $"Source:{step.ReagentCode.Trim().ToUpperInvariant()}"));
            }
        }
        else if (moduleCode == DeviceModules.NeedleWash)
        {
            resources.Add(new ResourceRequest(MachineResourceTypes.WashStation, "WashStation:NeedleWash"));
            resources.Add(new ResourceRequest(MachineResourceTypes.Needle, $"Needle:{NeedleCodes.Needle1}"));
            resources.Add(new ResourceRequest(MachineResourceTypes.Needle, $"Needle:{NeedleCodes.Needle2}"));
        }
        else if (moduleCode == DeviceModules.Pump)
        {
            resources.Add(new ResourceRequest(MachineResourceTypes.Pump, $"Pump:{DrawerToPwm(step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode) ?? "Any"}"));
        }
        else if (moduleCode == DeviceModules.Mixer)
        {
            resources.Add(new ResourceRequest(MachineResourceTypes.Mixer, $"Mixer:{step.WorkflowExecution?.SlideTask?.ChannelBatch?.DrawerCode ?? "Any"}"));
        }
        else if (moduleCode == DeviceModules.Dab)
        {
            resources.Add(new ResourceRequest(MachineResourceTypes.DabPosition, "DabPosition:DAB"));
        }

        return resources;
    }

    private static string ResolveDeviceModule(WorkflowStepExecution step)
    {
        if (IsDabStep(step)) return DeviceModules.Dab;
        if (IsTemperatureStep(step)) return DeviceModules.Temperature;
        var action = step.ActionType.ToLowerInvariant();
        if (action.Contains("level")) return DeviceModules.LiquidLevel;
        if (action.Contains("needle")) return DeviceModules.NeedleWash;
        if (action.Contains("wash")) return DeviceModules.Pump;
        if (action.Contains("mix")) return DeviceModules.Mixer;
        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0) return DeviceModules.Pipette;
        return DeviceModules.Workflow;
    }

    private static bool IsFluidicsModule(string moduleCode) =>
        string.Equals(moduleCode, DeviceModules.Pump, StringComparison.Ordinal)
        || string.Equals(moduleCode, DeviceModules.Mixer, StringComparison.Ordinal)
        || string.Equals(moduleCode, DeviceModules.LiquidLevel, StringComparison.Ordinal);

    private static bool IsMotionModule(string moduleCode) =>
        string.Equals(moduleCode, DeviceModules.RobotArm, StringComparison.Ordinal)
        || string.Equals(moduleCode, DeviceModules.Needles, StringComparison.Ordinal)
        || string.Equals(moduleCode, DeviceModules.Pipette, StringComparison.Ordinal)
        || string.Equals(moduleCode, DeviceModules.NeedleWash, StringComparison.Ordinal);

    private static bool ShouldRecordFluidicsDeviceFailure(DeviceCommandResult deviceResult) =>
        deviceResult.Status is DeviceCommandStatuses.Unknown or DeviceCommandStatuses.TimedOut
        || deviceResult.Data.ContainsKey("faultPlanId");

    private static bool ShouldRecordMotionDeviceFailure(DeviceCommandResult deviceResult) =>
        deviceResult.Status is DeviceCommandStatuses.Unknown or DeviceCommandStatuses.TimedOut
        || deviceResult.Data.ContainsKey("faultPlanId")
        || !string.IsNullOrWhiteSpace(deviceResult.ErrorCode);

    private static string? ResolveLiquidSourceType(DeviceCommandResult deviceResult)
    {
        if (deviceResult.Data.TryGetValue("sourceType", out var sourceType) && !string.IsNullOrWhiteSpace(Convert.ToString(sourceType)))
        {
            return Convert.ToString(sourceType);
        }

        if (deviceResult.Data.TryGetValue("liquidSourceType", out var liquidSourceType) && !string.IsNullOrWhiteSpace(Convert.ToString(liquidSourceType)))
        {
            return Convert.ToString(liquidSourceType);
        }

        return null;
    }

    private static WorkflowStepExecution? SelectNextExecutableStep(MachineRun run, IReadOnlyList<WorkflowStepExecution> pendingSteps)
    {
        foreach (var step in pendingSteps)
        {
            if (!IsMixStep(step) || MixerPrerequisitesSatisfied(run, step))
            {
                return step;
            }
        }

        return null;
    }

    private static bool MixerPrerequisitesSatisfied(MachineRun run, WorkflowStepExecution mixStep)
    {
        var channelBatchId = mixStep.WorkflowExecution?.SlideTask?.ChannelBatchId;
        if (string.IsNullOrWhiteSpace(channelBatchId))
        {
            return false;
        }

        var participatingWorkflows = run.WorkflowExecutions
            .Where(x => x.SlideTask?.ChannelBatchId == channelBatchId
                && x.StepExecutions.Any(step => step.MajorStepCode == mixStep.MajorStepCode && IsMixStep(step)))
            .ToList();
        if (participatingWorkflows.Count == 0)
        {
            return false;
        }

        foreach (var workflow in participatingWorkflows)
        {
            var additions = workflow.StepExecutions
                .Where(step => step.MajorStepCode == mixStep.MajorStepCode
                    && step.StepNo < mixStep.StepNo
                    && IsLiquidAdditionStep(step))
                .ToList();
            if (additions.Count > 0 && additions.Any(step => step.Status != RuntimeLedgerStatus.Completed))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMixStep(WorkflowStepExecution step) =>
        step.ActionType.Contains("mix", StringComparison.OrdinalIgnoreCase);

    private static bool IsLiquidAdditionStep(WorkflowStepExecution step)
    {
        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0)
        {
            return true;
        }

        var action = step.ActionType.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return action.Contains("dispens", StringComparison.OrdinalIgnoreCase)
            || action.Contains("pipett", StringComparison.OrdinalIgnoreCase)
            || action.Contains("addliquid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiquidClassAction(string actionType)
    {
        var normalized = actionType.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Contains("aspirat", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dispens", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("liquiddetect", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("blowout", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("pipett", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DrawerToPwm(string? drawerCode)
    {
        return (drawerCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "A" => "PWM0",
            "B" => "PWM1",
            "C" => "PWM2",
            "D" => "PWM3",
            _ => null
        };
    }

    private static int DefaultPumpSpeed(string actionType) =>
        actionType.Contains("reverse", StringComparison.OrdinalIgnoreCase) ? -60 : 60;

    private static string? ResolveWashTargetPoint(string actionType)
    {
        if (actionType.Contains("outer", StringComparison.OrdinalIgnoreCase))
        {
            return "WashOuterLeft";
        }

        if (actionType.Contains("inner", StringComparison.OrdinalIgnoreCase))
        {
            return "WashInnerLeft";
        }

        return null;
    }

    private static string? ResolveTargetPoint(WorkflowStepExecution step)
    {
        var washTarget = ResolveWashTargetPoint(step.ActionType);
        if (!string.IsNullOrWhiteSpace(washTarget))
        {
            return washTarget;
        }

        if (step.ActionType.Contains("wash", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(step.ReagentCode) && (step.VolumeUl ?? 0) > 0)
        {
            return step.WorkflowExecution?.SlideTask?.SlotCode;
        }

        return null;
    }

    private static string? PreferredNeedleForSlot(WorkflowStepExecution step)
    {
        var slotNo = ParseSlotNo(step.WorkflowExecution?.SlideTask?.SlotCode);
        if (slotNo <= 0)
        {
            return null;
        }

        return slotNo % 2 == 0 ? NeedleCodes.Needle2 : NeedleCodes.Needle1;
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
        if (await dbContext.ReagentConsumptions.AnyAsync(x => x.DeviceCommandExecutionId == command.Id, cancellationToken))
        {
            return true;
        }

        var reservations = await dbContext.ReagentReservations
            .Include(x => x.ReagentBottle)
            .Where(x => x.MachineRunId == run.Id
                && x.ReagentCode == reagentCode
                && x.ReservationKind == ReagentReservationKind.MachineRun
                && x.Status == ReagentReservationStatus.Reserved
                && x.ReagentBottleId != null
                && x.ReservedVolumeUl > 0)
            .ToListAsync(cancellationToken);
        reservations = reservations.OrderBy(x => x.CreatedAtUtc).ToList();
        var reservedBottleIds = reservations.Select(x => x.ReagentBottleId!).ToList();
        var reservedPlacements = await dbContext.ReagentRackPlacements
            .AsNoTracking()
            .Include(x => x.ReagentRackPosition)
            .Where(x => x.RemovedAtUtc == null && reservedBottleIds.Contains(x.ReagentBottleId))
            .ToDictionaryAsync(x => x.ReagentBottleId, x => x.ReagentRackPosition!.Code, cancellationToken);

        var sources = reservations
            .Where(x => x.ReagentBottle is not null)
            .Select(x => new ReagentSourceAllocation(
                x.ReagentBottle!,
                Math.Min(x.ReservedVolumeUl, x.ReagentBottle!.RemainingVolumeUl),
                reservedPlacements.GetValueOrDefault(x.ReagentBottleId!),
                x))
            .ToList();

        if (sources.Count == 0)
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
            sources = placements
                .Select(x => new ReagentSourceAllocation(x.ReagentBottle!, x.ReagentBottle!.RemainingVolumeUl, x.ReagentRackPosition?.Code, null))
                .ToList();
        }

        var available = sources.Sum(x => x.AvailableVolumeUl);
        if (available < volumeUl)
        {
            await AddAlarmAsync(dbContext, run.Id, "reagent_insufficient", "Critical", $"Reagent {reagentCode} is insufficient. Required {volumeUl} ul, available {available} ul.", cancellationToken);
            return false;
        }

        var remaining = volumeUl;
        foreach (var source in sources.OrderBy(x => x.AvailableVolumeUl))
        {
            if (remaining <= 0)
            {
                break;
            }

            var bottle = source.Bottle;
            var used = Math.Min(remaining, source.AvailableVolumeUl);
            bottle.RemainingVolumeUl -= used;
            bottle.UpdatedAtUtc = DateTimeOffset.UtcNow;
            remaining -= used;
            if (source.Reservation is not null)
            {
                source.Reservation.ReservedVolumeUl -= used;
                source.Reservation.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (source.Reservation.ReservedVolumeUl == 0)
                {
                    source.Reservation.Status = ReagentReservationStatus.Consumed;
                }
            }

            dbContext.ReagentConsumptions.Add(new ReagentConsumption
            {
                MachineRunId = run.Id,
                WorkflowStepExecutionId = step.Id,
                DeviceCommandExecutionId = command.Id,
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
                SourcePositionCode = source.PositionCode,
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
                eventPublisher.Publish(MachineEventMessage.Create(
                    MachineEventTypes.ReagentBottleDepleted,
                    run.Id,
                    "ReagentBottle",
                    bottle.Id,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["reagentBottleId"] = bottle.Id,
                        ["fullBarcode"] = bottle.FullBarcode,
                        ["reagentCode"] = reagentCode,
                        ["remainingVolumeUl"] = bottle.RemainingVolumeUl,
                        ["message"] = $"Bottle {bottle.FullBarcode} for {reagentCode} is depleted."
                    }));
            }
        }

        return true;
    }

    private sealed record ReagentSourceAllocation(
        ReagentBottle Bottle,
        int AvailableVolumeUl,
        string? PositionCode,
        ReagentReservation? Reservation);

    private async Task<bool> ApplyDabAsync(
        StainerDbContext dbContext,
        DabLifecycleService dabLifecycleService,
        MotionControlService motionControlService,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        DeviceCommandResult deviceResult,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dabBatch = await FindDabBatchForStepAsync(dbContext, step, cancellationToken);
        if (dabBatch is null)
        {
            await AddAlarmAsync(dbContext, run.Id, "dab_batch_not_prepared", "Critical", "No formal DAB batch is assigned to this DAB workflow step.", cancellationToken);
            return false;
        }

        if (dabBatch.Status is DabBatchStatus.PendingPreparation or DabBatchStatus.Preparing)
        {
            var preparation = await dabLifecycleService.CompletePreparationFromDeviceAsync(
                dabBatch.Id,
                run,
                step,
                command,
                deviceResult,
                cancellationToken);
            if (!preparation.Ok)
            {
                await AddAlarmAsync(dbContext, run.Id, preparation.ErrorCode!, "Critical", preparation.Message, cancellationToken);
                return false;
            }

            PublishDabBatchChanged(run.Id, preparation.Batch!, "prepared");
            return true;
        }

        if (dabBatch.Status == DabBatchStatus.Available)
        {
            return await ConsumeAvailableDabBatchAsync(dbContext, motionControlService, run, step, command, dabBatch, now, cancellationToken);
        }

        await AddAlarmAsync(dbContext, run.Id, "dab_batch_unavailable", "Critical", $"DAB batch {dabBatch.Id} is {dabBatch.Status} and cannot be used.", cancellationToken);
        return false;
    }

    private async Task<DabBatch?> FindDabBatchForStepAsync(
        StainerDbContext dbContext,
        WorkflowStepExecution step,
        CancellationToken cancellationToken)
    {
        var stainingTaskId = step.WorkflowExecution?.SlideTask?.StainingTaskId;
        if (string.IsNullOrWhiteSpace(stainingTaskId))
        {
            return null;
        }

        var candidates = await dbContext.DabBatches
            .Include(x => x.DabMixPosition)
            .Include(x => x.Tasks)
            .Include(x => x.ReagentReservations)
            .ThenInclude(x => x.ReagentBottle)
            .Where(x => x.Tasks.Any(task => task.StainingTaskId == stainingTaskId)
                && x.Status != DabBatchStatus.Cleaned
                && x.Status != DabBatchStatus.LegacyUnverified)
            .ToListAsync(cancellationToken);
        return candidates
            .OrderByDescending(x => x.Status == DabBatchStatus.Preparing)
            .ThenByDescending(x => x.Status == DabBatchStatus.PendingPreparation)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    private async Task<bool> ConsumeAvailableDabBatchAsync(
        StainerDbContext dbContext,
        MotionControlService motionControlService,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        DabBatch batch,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batch.ExpiresAtUtc <= now)
        {
            batch.Status = DabBatchStatus.Expired;
            batch.CleaningStatus = DabCleaningStatus.Required;
            batch.DabMixPosition!.Status = DabMixPositionStatus.AwaitingCleaning;
            batch.DabMixPosition.UpdatedAtUtc = now;
            PublishDabBatchChanged(run.Id, batch, "expired");
            await AddAlarmAsync(dbContext, run.Id, "dab_expired", "Critical", "A DAB batch is expired. Clean DAB mix positions before continuing.", cancellationToken);
            return false;
        }

        if (await dbContext.DabBatchUsages.AnyAsync(x => x.CommandId == command.Id, cancellationToken))
        {
            return true;
        }

        var volume = Math.Max(step.VolumeUl ?? DabFormula.VolumePerSlideUl, DabFormula.VolumePerSlideUl);
        if (batch.RemainingVolumeUl < volume)
        {
            await AddAlarmAsync(dbContext, run.Id, "dab_batch_insufficient", "Critical", $"DAB batch {batch.Id} does not have enough remaining volume.", cancellationToken);
            return false;
        }

        batch.RemainingVolumeUl -= volume;
        batch.UsedVolumeUl += volume;
        batch.UpdatedAtUtc = now;
        if (batch.RemainingVolumeUl == 0)
        {
            batch.Status = DabBatchStatus.Depleted;
            batch.CleaningStatus = DabCleaningStatus.Required;
            batch.DabMixPosition!.Status = DabMixPositionStatus.AwaitingCleaning;
            batch.DabMixPosition.UpdatedAtUtc = now;
        }

        PublishDabBatchChanged(run.Id, batch, "consumed");
        dbContext.DabBatchUsages.Add(new DabBatchUsage
        {
            DabBatch = batch,
            MachineRunId = run.Id,
            WorkflowStepExecutionId = step.Id,
            CommandId = command.Id,
            VolumeUl = volume,
            CreatedAtUtc = now
        });
        dbContext.DispenseExecutions.Add(new DispenseExecution
        {
            DeviceCommandExecutionId = command.Id,
            ReagentCode = "DAB",
            VolumeUl = volume,
            SourcePositionCode = batch.PositionCode,
            TargetSlotCode = step.WorkflowExecution!.SlideTask!.SlotCode,
            Status = DeviceCommandStatus.Completed,
            CreatedAtUtc = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "run.dab_consumption",
            EntityType = "DabBatch",
            EntityId = batch.Id,
            Message = JsonSerializer.Serialize(new { runId = run.Id, volumeUl = volume, remainingUl = batch.RemainingVolumeUl }),
            CreatedAtUtc = now
        });
        await motionControlService.RecordDabDispenseFromExecutorAsync(batch, run, step, command, volume, cancellationToken);
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
        PublishMachineState(run, $"Redo major step {major}.");
    }

    private async Task<bool> ValidateMockDeviceStateAsync(StainerDbContext dbContext, MachineRun run, CancellationToken cancellationToken)
    {
        var hasActiveDeviceProfile = await dbContext.DeviceProfiles
            .AsNoTracking()
            .AnyAsync(x => x.IsActive, cancellationToken);
        var hasActiveCoordinateProfile = await dbContext.CoordinateProfileVersions
            .AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Status == CoordinateProfileVersionStatus.Active, cancellationToken);
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
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Status = RuntimeLedgerStatus.Paused;
        run.PauseRequested = true;
        foreach (var batch in run.ChannelBatches)
        {
            batch.Status = RuntimeLedgerStatus.Paused;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        PublishMachineState(run, "Run paused after current atomic action.");
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
        var now = DateTimeOffset.UtcNow;
        run.CompletedAtUtc = now;
        foreach (var batch in run.ChannelBatches)
        {
            batch.Status = RuntimeLedgerStatus.Stopped;
            batch.CompletedAtUtc ??= now;
        }

        foreach (var slideTask in run.WorkflowExecutions.Select(x => x.SlideTask).Where(x => x is not null).Cast<SlideTask>())
        {
            if (slideTask.Status is RuntimeLedgerStatus.Pending or RuntimeLedgerStatus.Running or RuntimeLedgerStatus.Paused)
            {
                slideTask.Status = RuntimeLedgerStatus.Stopped;
            }
        }

        foreach (var step in run.WorkflowExecutions.SelectMany(x => x.StepExecutions).Where(x => x.Status == RuntimeLedgerStatus.Pending))
        {
            step.Status = RuntimeLedgerStatus.Stopped;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        PublishMachineState(run, "Run stopped after current atomic action.");
    }

    private async Task FaultRunAsync(
        StainerDbContext dbContext,
        string runId,
        string stepId,
        string message,
        CancellationToken cancellationToken,
        string alarmCode = "mock_fault",
        bool markUnknown = true)
    {
        var run = await LoadRunAsync(dbContext, runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        var step = run.WorkflowExecutions.SelectMany(x => x.StepExecutions).FirstOrDefault(x => x.Id == stepId);
        if (step is not null)
        {
            step.Status = markUnknown ? RuntimeLedgerStatus.Unknown : RuntimeLedgerStatus.Failed;
        }

        run.Status = RuntimeLedgerStatus.Faulted;
        run.FaultMessage = message;
        foreach (var batch in run.ChannelBatches)
        {
            batch.Status = RuntimeLedgerStatus.Faulted;
        }

        await AddAlarmAsync(dbContext, runId, alarmCode, "Critical", message, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        PublishMachineState(run, message);
    }

    private async Task CompleteRunAsync(StainerDbContext dbContext, MachineRun run, CancellationToken cancellationToken)
    {
        run.Status = RuntimeLedgerStatus.Completed;
        var now = DateTimeOffset.UtcNow;
        run.CompletedAtUtc = now;
        foreach (var workflow in run.WorkflowExecutions)
        {
            workflow.Status = RuntimeLedgerStatus.Completed;
            workflow.CompletedAtUtc = now;
            workflow.SlideTask!.Status = RuntimeLedgerStatus.WaitingUnload;
            workflow.SlideTask.ChannelBatch!.Status = RuntimeLedgerStatus.Completed;
            workflow.SlideTask.ChannelBatch.CompletedAtUtc ??= now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        PublishMachineState(run, "Run completed and slides are waiting unload.");
    }

    private async Task AddAlarmAsync(StainerDbContext dbContext, string runId, string code, string severity, string message, CancellationToken cancellationToken)
    {
        if (!await dbContext.Alarms.AnyAsync(x => x.MachineRunId == runId && x.Code == code && x.Status == "Active", cancellationToken))
        {
            var alarm = new Alarm
            {
                MachineRunId = runId,
                Code = code,
                Severity = severity,
                Message = message,
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Alarms.Add(alarm);
            eventPublisher.Publish(MachineEventMessage.Create(
                MachineEventTypes.AlarmRaised,
                runId,
                "Alarm",
                alarm.Id,
                null,
                new Dictionary<string, object?>
                {
                    ["alarmId"] = alarm.Id,
                    ["code"] = code,
                    ["severity"] = severity,
                    ["status"] = alarm.Status,
                    ["message"] = message
                }));
        }
    }

    private void PublishMachineState(MachineRun run, string message)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.MachineStateChanged,
            run.Id,
            "MachineRun",
            run.Id,
            null,
            new Dictionary<string, object?>
            {
                ["runId"] = run.Id,
                ["runCode"] = run.RunCode,
                ["status"] = run.Status,
                ["currentMajorStepCode"] = run.CurrentMajorStepCode,
                ["faultMessage"] = run.FaultMessage,
                ["message"] = message
            }));
    }

    private void PublishSlideTaskState(string runId, SlideTask slideTask, string? currentStep)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.SlideTaskStateChanged,
            runId,
            "SlideTask",
            slideTask.Id,
            null,
            new Dictionary<string, object?>
            {
                ["slideTaskId"] = slideTask.Id,
                ["slotCode"] = slideTask.SlotCode,
                ["taskType"] = slideTask.TaskType,
                ["status"] = slideTask.Status,
                ["currentStep"] = currentStep
            }));
    }

    private void PublishWorkflowStep(string runId, WorkflowStepExecution step, string eventType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            eventType,
            runId,
            "WorkflowStepExecution",
            step.Id,
            null,
            new Dictionary<string, object?>
            {
                ["workflowStepExecutionId"] = step.Id,
                ["workflowExecutionId"] = step.WorkflowExecutionId,
                ["slideTaskId"] = step.WorkflowExecution?.SlideTaskId,
                ["stepNo"] = step.StepNo,
                ["majorStepCode"] = step.MajorStepCode,
                ["stepName"] = step.StepName,
                ["actionType"] = step.ActionType,
                ["reagentCode"] = step.ReagentCode,
                ["volumeUl"] = step.VolumeUl,
                ["status"] = step.Status,
                ["redoCount"] = step.RedoCount
            }));
    }

    private void PublishDabBatchChanged(string runId, DabBatch batch, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DabBatchChanged,
            runId,
            "DabBatch",
            batch.Id,
            null,
            new Dictionary<string, object?>
            {
                ["dabBatchId"] = batch.Id,
                ["positionCode"] = batch.PositionCode,
                ["status"] = batch.Status,
                ["changeType"] = changeType,
                ["remainingVolumeUl"] = batch.RemainingVolumeUl,
                ["preparedAtUtc"] = batch.PreparedAtUtc,
                ["expiresAtUtc"] = batch.ExpiresAtUtc
            }));
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

    private static bool IsTemperatureStep(WorkflowStepExecution step)
    {
        return step.MajorStepCode.Contains("HEAT", StringComparison.OrdinalIgnoreCase)
            || step.MajorStepCode.Contains("TEMP", StringComparison.OrdinalIgnoreCase)
            || step.ActionType.Contains("HEAT", StringComparison.OrdinalIgnoreCase)
            || step.ActionType.Contains("TEMP", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseSlotNo(string? slotCode)
    {
        var value = slotCode?.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(value, out var slotNo) ? slotNo : 0;
    }

    private static int ResolveStepVisibleDelay(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["MachineExecutor:StepVisibleDelayMilliseconds"];
        if (int.TryParse(configured, out var value))
        {
            return Math.Max(0, value);
        }

        return environment.IsEnvironment("Testing") ? 0 : DefaultStepVisibleDelayMs;
    }

    private sealed record MachineExecutorCommand(string RunId, string Type, string? Payload);

    private sealed record ResourceRequest(string Type, string Code);

    private sealed record ResourceLeaseResult(bool Ok, string? ErrorCode, string Message)
    {
        public static ResourceLeaseResult Succeeded() => new(true, null, "Resources acquired.");

        public static ResourceLeaseResult Failed(string errorCode, string message) => new(false, errorCode, message);
    }

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

public sealed class MachineExecutorHostedService(
    MachineExecutor executor,
    IServiceScopeFactory scopeFactory,
    MachineExecutorLeaseService leaseService,
    SafetyLogWriter safetyLogWriter) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!leaseService.TryAcquire())
        {
            await safetyLogWriter.WriteAsync(
                "runtime",
                "Error",
                "MachineExecutor lease is unavailable. This instance is read-only for execution.",
                new SafetyLogContext(Source: "MachineExecutorHostedService"),
                cancellationToken: stoppingToken);
            return;
        }

        executor.Attach(scopeFactory);
        try
        {
            await safetyLogWriter.WriteAsync(
                "runtime",
                "Information",
                "MachineExecutor lease acquired.",
                new SafetyLogContext(Source: "MachineExecutorHostedService"),
                cancellationToken: stoppingToken);
            await executor.RunAsync(stoppingToken);
        }
        finally
        {
            leaseService.Release();
        }
    }
}
