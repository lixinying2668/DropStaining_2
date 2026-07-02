using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class StartupRecoveryService(
    StainerDbContext dbContext,
    DeviceModeService deviceModeService,
    SafetyLogWriter safetyLogWriter)
{
    private static readonly string[] NonTerminalRunStatuses =
    [
        RuntimeLedgerStatus.Created,
        RuntimeLedgerStatus.Pending,
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted
    ];

    private static readonly string[] UncertainCommandStatuses =
    [
        DeviceCommandStatus.CommandSent,
        DeviceCommandStatus.Acknowledged,
        DeviceCommandStatus.DeviceAcknowledged
    ];

    public async Task<StartupRecoveryReportResponse> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var recoveredAtUtc = DateTimeOffset.UtcNow;
        var commands = await dbContext.DeviceCommandExecutions
            .Where(x => UncertainCommandStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        foreach (var command in commands)
        {
            command.Status = DeviceCommandStatus.Unknown;
            command.ResultJson = JsonSerializer.Serialize(new
            {
                recoveredAtUtc,
                reason = "Service restarted before command completion could be confirmed."
            });
        }
        var commandIds = commands.Select(x => x.Id).ToList();
        if (commandIds.Count > 0)
        {
            var leases = await dbContext.MachineResourceLeases
                .Where(x => commandIds.Contains(x.DeviceCommandExecutionId!) && x.Status == MachineResourceLeaseStatus.Acquired)
                .ToListAsync(cancellationToken);
            foreach (var lease in leases)
            {
                lease.Status = MachineResourceLeaseStatus.NeedsManualResolution;
                lease.WaitReason = "Startup recovery marked the owning command Unknown.";
            }
        }

        var stepIds = commands
            .Select(x => x.WorkflowStepExecutionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var steps = stepIds.Count == 0
            ? new List<WorkflowStepExecution>()
            : await dbContext.WorkflowStepExecutions
                .Where(x => stepIds.Contains(x.Id) || x.Status == RuntimeLedgerStatus.Running)
                .ToListAsync(cancellationToken);
        foreach (var step in steps.Where(x => x.Status is RuntimeLedgerStatus.Running or RuntimeLedgerStatus.Pending))
        {
            step.Status = RuntimeLedgerStatus.Unknown;
        }

        var activeRuns = await dbContext.MachineRuns
            .Include(x => x.ChannelBatches)
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.StepExecutions)
            .Where(x => NonTerminalRunStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);

        var commandsByRun = commands
            .GroupBy(x => x.MachineRunId)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
        var runsMarkedFaulted = 0;
        var alarmsCreated = 0;
        foreach (var run in activeRuns)
        {
            var hasUnknownCommand = commandsByRun.ContainsKey(run.Id)
                || run.WorkflowExecutions.SelectMany(x => x.StepExecutions).Any(x => x.Status == RuntimeLedgerStatus.Unknown);
            if (!hasUnknownCommand)
            {
                dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "startup.recovery.scan",
                    EntityType = "MachineRun",
                    EntityId = run.Id,
                    Message = JsonSerializer.Serialize(new { run.Status, deviceMode = deviceModeService.CurrentMode }),
                    CreatedAtUtc = recoveredAtUtc
                });
                continue;
            }

            run.Status = RuntimeLedgerStatus.Faulted;
            run.FaultMessage = "Startup recovery detected unknown device command state. Manual handling is required.";
            foreach (var batch in run.ChannelBatches)
            {
                if (batch.Status is RuntimeLedgerStatus.Running or RuntimeLedgerStatus.Pending or RuntimeLedgerStatus.Paused)
                {
                    batch.Status = RuntimeLedgerStatus.Faulted;
                }
            }

            if (!await dbContext.Alarms.AnyAsync(x => x.MachineRunId == run.Id && x.Code == "startup_recovery_unknown_command" && x.Status == "Active", cancellationToken))
            {
                dbContext.Alarms.Add(new Alarm
                {
                    MachineRunId = run.Id,
                    Code = "startup_recovery_unknown_command",
                    Severity = "Critical",
                    Message = "Service restarted with sent or acknowledged device commands that were not confirmed completed. Manual handling is required.",
                    Status = "Active",
                    CreatedAtUtc = recoveredAtUtc
                });
                alarmsCreated++;
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "startup.recovery.unknown_command",
                EntityType = "MachineRun",
                EntityId = run.Id,
                Message = JsonSerializer.Serialize(new
                {
                    commandsMarkedUnknown = commandsByRun.GetValueOrDefault(run.Id)?.Select(x => x.Id).ToArray() ?? [],
                    deviceMode = deviceModeService.CurrentMode,
                    recoveredAtUtc
                }),
                CreatedAtUtc = recoveredAtUtc
            });
            runsMarkedFaulted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var report = new StartupRecoveryReportResponse(
            activeRuns.Count,
            commands.Count,
            steps.Count(x => x.Status == RuntimeLedgerStatus.Unknown),
            runsMarkedFaulted,
            alarmsCreated,
            recoveredAtUtc,
            runsMarkedFaulted > 0
                ? "Unknown command state was detected. Runs require manual handling."
                : "Startup recovery completed.");
        await safetyLogWriter.WriteAsync(
            "runtime",
            runsMarkedFaulted > 0 ? "Warning" : "Information",
            report.Message,
            new SafetyLogContext(DeviceMode: deviceModeService.CurrentMode, Source: "StartupRecoveryService"),
            cancellationToken: cancellationToken);
        return report;
    }
}
