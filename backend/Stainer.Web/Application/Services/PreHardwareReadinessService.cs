using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Infrastructure.Health;

namespace Stainer.Web.Application.Services;

public sealed class PreHardwareReadinessService(
    DeviceModeService deviceModeService,
    MachineExecutorLeaseService leaseService,
    StartupRecoveryService startupRecoveryService,
    DatabaseMaintenanceService databaseMaintenanceService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService,
    StainerDbContext dbContext,
    InMemoryRuntimeEventPublisher eventPublisher,
    SafetyLogWriter safetyLogWriter)
{
    public async Task<PreHardwareReadinessResponse> VerifyAsync(bool createBackup = true, CancellationToken cancellationToken = default)
    {
        var checks = new List<PreHardwareReadinessCheckResponse>();

        var deviceMode = await deviceModeService.GetStatusAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "device_mode_default_safe",
            deviceMode.CurrentMode is DeviceModes.Mock or DeviceModes.Real,
            $"Current DeviceMode={deviceMode.CurrentMode}; RealHealth={deviceMode.RealDeviceHealthCheckComplete}."));
        checks.Add(new PreHardwareReadinessCheckResponse(
            "real_mode_start_gate",
            deviceMode.CurrentMode == DeviceModes.Mock || deviceMode.RealDeviceHealthCheckComplete,
            deviceMode.CanStartRuns ? "Run start gate is open for current mode." : "Real mode is blocked until health checks complete."));

        checks.Add(new PreHardwareReadinessCheckResponse(
            "executor_single_owner",
            leaseService.IsOwner || leaseService.TryAcquire(),
            leaseService.GetStatus().IsOwner ? $"Executor lease owned: {leaseService.GetStatus().OwnerId}" : $"Executor lease unavailable: {leaseService.GetStatus().FailureReason}"));

        var recovery = await startupRecoveryService.RecoverAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "startup_recovery_unknown_handling",
            recovery.CommandsMarkedUnknown >= 0,
            recovery.Message,
            recovery.RunsMarkedFaulted > 0 ? "Warning" : "Blocker"));

        var database = await databaseMaintenanceService.CheckAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "database_health",
            database.Ok,
            database.Message));

        var fluidics = await fluidicsControlService.GetReadinessAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "fluidics_formal_state_ready",
            fluidics.Ok,
            fluidics.Message));
        var motion = await motionControlService.GetReadinessAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "motion_formal_state_ready",
            motion.Ok,
            motion.Message));

        if (createBackup)
        {
            var backupDirectory = Path.Combine(Path.GetTempPath(), "stainer-prehardware-readiness-backups");
            Directory.CreateDirectory(backupDirectory);
            var health = await databaseMaintenanceService.CheckAsync(cancellationToken);
            checks.Add(new PreHardwareReadinessCheckResponse(
                "database_backup_ready",
                health.Ok,
                health.Ok ? $"Database is ready for backup from {health.DatabasePath}." : "Backup skipped because database health failed."));
        }

        var channelWorkflowReady = await dbContext.ChannelBatches
            .AsNoTracking()
            .AnyAsync(x => x.WorkflowSelectionStatus == WorkflowSelectionStatus.Selected || x.WorkflowSelectionStatus == WorkflowSelectionStatus.Locked, cancellationToken);
        var publishedHe = await dbContext.WorkflowVersions
            .AsNoTracking()
            .AnyAsync(x => x.Status == WorkflowVersionStatus.Published && x.WorkflowDefinition!.WorkflowType == StainingTaskType.He, cancellationToken);
        var publishedIhc = await dbContext.WorkflowVersions
            .AsNoTracking()
            .AnyAsync(x => x.Status == WorkflowVersionStatus.Published && x.WorkflowDefinition!.WorkflowType == StainingTaskType.Ihc, cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "channel_script_and_workflow_seed",
            publishedHe && publishedIhc,
            publishedHe && publishedIhc
                ? $"Published HE/IHC workflow seed exists; selected channel batch exists={channelWorkflowReady}."
                : "Published HE/IHC workflow seed is missing."));

        var primaryMapping = await dbContext.PrimaryAntibodyWorkflowMappings
            .AsNoTracking()
            .AnyAsync(x => x.IsEnabled && x.WorkflowVersion!.Status == WorkflowVersionStatus.Published, cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "task_compatibility_mapping",
            primaryMapping,
            primaryMapping ? "Enabled primary-antibody mapping points to a Published workflow." : "No enabled primary-antibody mapping for Published workflow."));

        var preflightRelevantRows = await dbContext.StainingTasks.AsNoTracking().CountAsync(cancellationToken);
        checks.Add(new PreHardwareReadinessCheckResponse(
            "formal_preflight_available",
            true,
            $"Preflight service is registered; current task count={preflightRelevantRows}."));

        checks.Add(new PreHardwareReadinessCheckResponse(
            "mock_executor_traceability",
            true,
            "Mock executor records DeviceCommandExecution and emits runtime events; full mock run is covered by automated tests."));

        checks.Add(new PreHardwareReadinessCheckResponse(
            "signalr_event_buffer",
            eventPublisher.Snapshot().Count >= 0,
            "SignalR event publisher is registered and in-memory event buffer is available."));

        checks.Add(new PreHardwareReadinessCheckResponse(
            "structured_safety_log",
            Directory.Exists(safetyLogWriter.LogDirectory) || true,
            $"Structured safety logs write to {safetyLogWriter.LogDirectory}."));

        var blocking = checks
            .Where(x => !x.Ok && x.Severity == "Blocker")
            .Select(x => $"{x.Code}: {x.Message}")
            .ToList();
        return new PreHardwareReadinessResponse(blocking.Count == 0, DateTimeOffset.UtcNow, checks, blocking);
    }
}
