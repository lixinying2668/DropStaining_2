using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class RunControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    MachineExecutor executor,
    PreflightValidationService preflightValidationService,
    DeviceInitializationService deviceInitializationService,
    DeviceModeService deviceModeService,
    CoordinateProfileLifecycleService coordinateProfileLifecycleService,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService,
    MachineExecutorLeaseService leaseService,
    SafetyLogWriter safetyLogWriter)
{
    public Task<RunCommandResponse> StartAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(
            runId,
            request.CommandId,
            "run.start",
            request,
            actor,
            async () =>
            {
                await coordinateProfileLifecycleService.EnsureRunCoordinateUsableAsync(runId, deviceModeService.CurrentMode, cancellationToken);
                deviceModeService.EnsureRunStartAllowed();
                await deviceInitializationService.EnsureReadyAsync(cancellationToken);
                await thermalControlService.EnsureReadyForRunAsync(cancellationToken);
                await fluidicsControlService.EnsureReadyForRunAsync(cancellationToken);
                await motionControlService.EnsureReadyForRunAsync(cancellationToken);
                leaseService.EnsureOwner();
                var preflight = string.IsNullOrWhiteSpace(request.PreflightStateHash)
                    ? null
                    : await preflightValidationService.ValidateAsync(cancellationToken);
                if (preflight is not null && (!preflight.Ok || !string.Equals(preflight.StateHash, request.PreflightStateHash, StringComparison.Ordinal)))
                {
                    throw new BusinessRuleException("preflight_invalid", "Latest preflight validation is missing, failed, or stale. Re-run preflight before starting.", StatusCodes.Status409Conflict);
                }

                await LockChannelBatchesForStartAsync(runId, request.CommandId, actor, cancellationToken);
                await safetyLogWriter.WriteAsync(
                    "device",
                    "Information",
                    "Run start command queued.",
                    new SafetyLogContext(
                        CorrelationId: request.CommandId,
                        CommandId: request.CommandId,
                        MachineRunId: runId,
                        DeviceMode: deviceModeService.CurrentMode,
                        Actor: actor.Username,
                        Source: "RunControlService"),
                    cancellationToken: cancellationToken);
                await executor.EnqueueStartAsync(runId, cancellationToken);
            },
            "Start command queued.",
            cancellationToken);
    }

    public Task<RunCommandResponse> PauseAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.pause", request, actor, () => { executor.RequestPause(runId); return Task.CompletedTask; }, "Pause requested.", cancellationToken);
    }

    public Task<RunCommandResponse> ResumeAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.resume", request, actor, async () => await executor.EnqueueResumeAsync(runId, cancellationToken), "Resume command queued.", cancellationToken);
    }

    public Task<RunCommandResponse> StopAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.stop", request, actor, () => { executor.RequestStop(runId); return Task.CompletedTask; }, "Stop requested.", cancellationToken);
    }

    public Task<RunCommandResponse> InjectFaultAsync(string runId, InjectFaultRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.inject_fault", request, actor, () => { executor.RequestFault(runId, request.Message); return Task.CompletedTask; }, "Fault injection requested.", cancellationToken);
    }

    public Task<RunCommandResponse> RedoCurrentMajorStepAsync(string runId, RedoMajorStepRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.redo_current_major_step", request, actor, async () => await executor.EnqueueRedoAsync(runId, request.Reason, cancellationToken), "Redo command queued.", cancellationToken);
    }

    private Task<RunCommandResponse> EnqueueAsync(
        string runId,
        string commandId,
        string operation,
        object request,
        AuthenticatedUser actor,
        Func<Task> enqueue,
        string message,
        CancellationToken cancellationToken)
    {
        return idempotencyService.RunAsync(
            commandId,
            operation,
            new { runId, request },
            actor,
            async () =>
            {
                var run = await dbContext.MachineRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
                if (run is null)
                {
                    throw new BusinessRuleException("run_not_found", "Run was not found.", StatusCodes.Status404NotFound);
                }

                leaseService.EnsureOwner();
                await enqueue();
                return new CommandExecutionResult<RunCommandResponse>(
                    new RunCommandResponse(true, commandId, false, runId, run.Status, message),
                    "MachineRun",
                    runId);
            },
            cancellationToken);
    }

    private async Task LockChannelBatchesForStartAsync(
        string runId,
        string commandId,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.MachineRuns
            .Include(x => x.ChannelBatches)
            .SingleAsync(x => x.Id == runId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var batch in run.ChannelBatches)
        {
            if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
            {
                throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution before it can start.", StatusCodes.Status409Conflict);
            }

            if (string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
                || string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson)
                || batch.WorkflowSnapshotJson == "{}")
            {
                throw new BusinessRuleException("channel_workflow_required", "Each channel batch must have a selected workflow before start.", StatusCodes.Status409Conflict);
            }

            if (batch.CoordinateSelectionStatus == CoordinateSelectionStatus.NeedsManualResolution)
            {
                throw new BusinessRuleException("channel_coordinate_needs_manual_resolution", "Channel batch needs manual coordinate resolution before it can start.", StatusCodes.Status409Conflict);
            }

            if (batch.CoordinateSelectionStatus != CoordinateSelectionStatus.Frozen
                || string.IsNullOrWhiteSpace(batch.CoordinateProfileVersionId)
                || string.IsNullOrWhiteSpace(batch.CoordinateSnapshotJson)
                || batch.CoordinateSnapshotJson == "{}")
            {
                throw new BusinessRuleException("channel_coordinate_required", "Each channel batch must have a frozen coordinate version before start.", StatusCodes.Status409Conflict);
            }

            if (batch.LiquidClassSelectionStatus == LiquidClassSelectionStatus.NeedsManualResolution)
            {
                throw new BusinessRuleException("channel_liquid_class_needs_manual_resolution", "Channel batch needs manual Liquid Class resolution before start.", StatusCodes.Status409Conflict);
            }

            if (batch.LiquidClassSelectionStatus != LiquidClassSelectionStatus.Frozen
                || string.IsNullOrWhiteSpace(batch.LiquidClassSnapshotJson)
                || batch.LiquidClassSnapshotJson == "{}")
            {
                throw new BusinessRuleException("channel_liquid_class_required", "Each channel batch must have frozen Liquid Class versions before start.", StatusCodes.Status409Conflict);
            }

            if (batch.WorkflowLockedAtUtc is null)
            {
                dbContext.WorkflowAssignmentHistory.Add(new WorkflowAssignmentHistory
                {
                    ChannelBatch = batch,
                    OldExperimentType = batch.ExperimentType,
                    OldWorkflowVersionId = batch.SelectedWorkflowVersionId,
                    OldWorkflowSnapshotJson = batch.WorkflowSnapshotJson,
                    NewExperimentType = batch.ExperimentType,
                    NewWorkflowVersionId = batch.SelectedWorkflowVersionId,
                    NewWorkflowSnapshotJson = batch.WorkflowSnapshotJson,
                    ActionType = WorkflowAssignmentAction.Lock,
                    ActorUserId = actor.UserId,
                    CreatedAtUtc = now,
                    Reason = "Run start locked channel workflow.",
                    CommandId = commandId
                });
            }

            batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Locked;
            batch.WorkflowLockedAtUtc ??= now;
        }

        var coordinateVersionIds = run.ChannelBatches
            .Select(x => x.CoordinateProfileVersionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (coordinateVersionIds.Count != 1)
        {
            throw new BusinessRuleException("coordinate_version_mismatch", "All channel batches in one run must use the same frozen coordinate version.", StatusCodes.Status409Conflict);
        }

        run.CoordinateProfileVersionId ??= coordinateVersionIds[0];
        run.CoordinateSnapshotJson = run.ChannelBatches
            .Select(x => x.CoordinateSnapshotJson)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "{}") ?? "{}";
    }
}
