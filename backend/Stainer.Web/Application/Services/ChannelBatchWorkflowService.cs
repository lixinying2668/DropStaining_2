using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class ChannelBatchWorkflowService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    CoordinateProfileLifecycleService coordinateProfileLifecycleService,
    LiquidClassSnapshotFactory liquidClassSnapshotFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveBatchStatuses =
    [
        RuntimeLedgerStatus.Pending,
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted
    ];

    public Task<ChannelBatchWorkflowResponse> SelectInitialWorkflowAsync(
        SelectChannelWorkflowRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "channel.workflow.initial_select",
            request,
            actor,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var experimentType = NormalizeExperimentType(request.ExperimentType);
                var version = await LoadPublishedWorkflowVersionAsync(request.WorkflowVersionId, experimentType, cancellationToken);
                var snapshot = JsonSerializer.Serialize(WorkflowSnapshotFactory.Create(version), JsonOptions);
                var coordinate = await coordinateProfileLifecycleService.FreezeCurrentActiveVersionAsync(cancellationToken);

                var batch = await LoadTargetBatchAsync(request, cancellationToken);
                EnsureBatchCanInitialSelect(batch);
                var liquidClassSnapshot = batch.LiquidClassSelectionStatus == LiquidClassSelectionStatus.Frozen && batch.LiquidClassSnapshotJson != "{}"
                    ? LiquidClassSnapshotFactory.ValidateFrozenForWorkflow(version, batch.LiquidClassSnapshotJson)
                    : await liquidClassSnapshotFactory.FreezeForWorkflowAsync(version, cancellationToken);
                AddInitialSelectionHistory(batch, actor, request.CommandId, experimentType, version.Id, snapshot, now);

                batch.ExperimentType = experimentType;
                batch.SelectedWorkflowVersionId = version.Id;
                batch.WorkflowSnapshotJson = snapshot;
                batch.CoordinateProfileVersionId = coordinate.VersionId;
                batch.CoordinateSnapshotJson = coordinate.SnapshotJson;
                batch.CoordinateSelectionStatus = CoordinateSelectionStatus.Frozen;
                batch.LiquidClassSnapshotJson = liquidClassSnapshot;
                batch.LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen;
                batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Selected;
                batch.WorkflowSelectedAtUtc = now;
                batch.WorkflowSelectedByUserId = actor.UserId;

                AddAudit(actor, "channel.workflow.select", batch.Id, new
                {
                    batch.DrawerCode,
                    batch.ExperimentType,
                    workflowVersionId = version.Id,
                    coordinateProfileVersionId = coordinate.VersionId,
                    commandId = request.CommandId,
                    correlationId = request.CommandId
                });
                PublishChannelBatchChanged(batch, "workflowSelected");

                return new CommandExecutionResult<ChannelBatchWorkflowResponse>(
                    new ChannelBatchWorkflowResponse(
                        true,
                        request.CommandId,
                        false,
                        batch.Id,
                        batch.DrawerCode,
                        experimentType,
                        version.Id,
                        batch.WorkflowSelectionStatus,
                        batch.WorkflowSelectedAtUtc,
                        "Channel workflow selected."),
                    "ChannelBatch",
                    batch.Id);
            },
            cancellationToken);
    }

    public Task<ChannelBatchWorkflowResponse> SelectExperimentTypeAsync(
        SelectChannelExperimentTypeRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if (request.AdditionalProperties?.Keys.Any(x => x.Equals("workflowVersionId", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new BusinessRuleException(
                "workflow_version_not_allowed",
                "workflowVersionId is not accepted. The server binds the current default Published workflow.",
                StatusCodes.Status400BadRequest);
        }

        return idempotencyService.RunAsync(
            request.CommandId,
            "channel.experiment_type.select",
            request,
            actor,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var experimentType = NormalizeExperimentType(request.ExperimentType);
                var version = await LoadDefaultPublishedWorkflowVersionAsync(experimentType, cancellationToken);
                var snapshot = JsonSerializer.Serialize(WorkflowSnapshotFactory.Create(version), JsonOptions);
                var batch = await LoadTargetBatchAsync(request.ChannelBatchId, request.DrawerCode, cancellationToken);
                var isInitialSelection = IsUnselected(batch);
                var coordinate = isInitialSelection
                    ? await coordinateProfileLifecycleService.FreezeCurrentActiveVersionAsync(cancellationToken)
                    : (VersionId: batch.CoordinateProfileVersionId ?? string.Empty, SnapshotJson: batch.CoordinateSnapshotJson);

                if (isInitialSelection)
                {
                    EnsureBatchCanInitialSelect(batch);
                    AddAssignmentHistory(
                        batch,
                        actor,
                        request.CommandId,
                        WorkflowAssignmentAction.InitialSelection,
                        "Initial channel experiment type selection.",
                        experimentType,
                        version.Id,
                        snapshot,
                        now);
                }
                else
                {
                    var reason = RequireChangeReason(request.Reason);
                    await EnsureBatchCanChangeAsync(batch, experimentType, version.Id, cancellationToken);
                    AddAssignmentHistory(
                        batch,
                        actor,
                        request.CommandId,
                        WorkflowAssignmentAction.PreStartChange,
                        reason,
                        experimentType,
                        version.Id,
                        snapshot,
                        now);
                }

                var liquidClassSnapshot = batch.LiquidClassSelectionStatus == LiquidClassSelectionStatus.Frozen && batch.LiquidClassSnapshotJson != "{}"
                    ? LiquidClassSnapshotFactory.ValidateFrozenForWorkflow(version, batch.LiquidClassSnapshotJson)
                    : await liquidClassSnapshotFactory.FreezeForWorkflowAsync(version, cancellationToken);

                var oldExperimentType = batch.ExperimentType;
                var oldWorkflowVersionId = batch.SelectedWorkflowVersionId;
                batch.ExperimentType = experimentType;
                batch.SelectedWorkflowVersionId = version.Id;
                batch.WorkflowSnapshotJson = snapshot;
                batch.LiquidClassSnapshotJson = liquidClassSnapshot;
                batch.LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen;
                if (isInitialSelection)
                {
                    batch.CoordinateProfileVersionId = coordinate.VersionId;
                    batch.CoordinateSnapshotJson = coordinate.SnapshotJson;
                    batch.CoordinateSelectionStatus = CoordinateSelectionStatus.Frozen;
                }
                batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Selected;
                batch.WorkflowSelectedAtUtc = now;
                batch.WorkflowSelectedByUserId = actor.UserId;

                AddAudit(actor, isInitialSelection ? "channel.experiment_type.select" : "channel.experiment_type.change", batch.Id, new
                {
                    batch.DrawerCode,
                    oldExperimentType,
                    oldWorkflowVersionId,
                    experimentType,
                    workflowVersionId = version.Id,
                    coordinateProfileVersionId = batch.CoordinateProfileVersionId,
                    workflowName = version.WorkflowDefinition!.Name,
                    reason = isInitialSelection ? null : request.Reason?.Trim(),
                    preflightInvalidated = !isInitialSelection,
                    commandId = request.CommandId,
                    correlationId = request.CommandId
                });
                PublishChannelBatchChanged(batch, isInitialSelection ? "experimentTypeSelected" : "experimentTypeChanged");

                return new CommandExecutionResult<ChannelBatchWorkflowResponse>(
                    new ChannelBatchWorkflowResponse(
                        true,
                        request.CommandId,
                        false,
                        batch.Id,
                        batch.DrawerCode,
                        experimentType,
                        version.Id,
                        batch.WorkflowSelectionStatus,
                        batch.WorkflowSelectedAtUtc,
                        isInitialSelection ? "Channel experiment type selected." : "Channel experiment type changed.",
                        version.WorkflowDefinition.Name,
                        version.VersionLabel),
                    "ChannelBatch",
                    batch.Id);
            },
            cancellationToken);
    }

    public Task<ChannelBatchActivationResponse> EnsureActiveBatchAsync(
        EnsureChannelBatchRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "channel_batch.ensure_active",
            request,
            actor,
            async () =>
            {
                var drawerCode = NormalizeDrawerCode(request.DrawerCode);
                var drawer = await dbContext.Drawers.SingleOrDefaultAsync(x => x.Code == drawerCode, cancellationToken);
                if (drawer is null)
                {
                    throw new BusinessRuleException("drawer_not_found", "Drawer was not found.", StatusCodes.Status404NotFound);
                }

                var activeBatches = await dbContext.ChannelBatches
                    .Include(x => x.SlideTasks)
                    .Where(x => x.DrawerId == drawer.Id && ActiveBatchStatuses.Contains(x.Status))
                    .ToListAsync(cancellationToken);
                var existing = activeBatches
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .FirstOrDefault();
                var liquidClassCatalogSnapshot = existing is null
                    ? await liquidClassSnapshotFactory.FreezeCatalogAsync(cancellationToken)
                    : existing.LiquidClassSnapshotJson;
                var batch = existing ?? new ChannelBatch
                {
                    DrawerId = drawer.Id,
                    DrawerCode = drawer.Code,
                    Status = RuntimeLedgerStatus.Pending,
                    WorkflowSnapshotJson = "{}",
                    WorkflowSelectionStatus = WorkflowSelectionStatus.Unselected,
                    LiquidClassSnapshotJson = liquidClassCatalogSnapshot,
                    LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                if (existing is null)
                {
                    dbContext.ChannelBatches.Add(batch);
                    AddAudit(actor, "channel_batch.ensure_active", batch.Id, new
                    {
                        batch.DrawerCode,
                        commandId = request.CommandId
                    });
                    PublishChannelBatchChanged(batch, "batchCreated");
                }

                return new CommandExecutionResult<ChannelBatchActivationResponse>(
                    new ChannelBatchActivationResponse(
                        true,
                        request.CommandId,
                        false,
                        batch.Id,
                        batch.DrawerCode,
                        batch.Status,
                        batch.WorkflowSelectionStatus,
                        existing is null ? "Channel batch created." : "Active channel batch exists.",
                        batch.ExperimentType,
                        batch.SlideTasks
                            .OrderBy(x => x.SlotCode)
                            .Select(x => x.SlotCode)
                            .ToList()),
                    "ChannelBatch",
                    batch.Id);
            },
            cancellationToken);
    }

    public Task<ChannelBatchWorkflowResponse> SelectWorkflowAsync(
        SelectChannelWorkflowRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return SelectInitialWorkflowAsync(request, actor, cancellationToken);
    }

    public async Task<ChannelBatch?> GetActiveBatchAsync(string drawerCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDrawerCode(drawerCode);
        var batches = await dbContext.ChannelBatches
            .Include(x => x.SlideTasks)
            .ThenInclude(x => x.StainingTask)
            .Include(x => x.SelectedWorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .Where(x => x.DrawerCode == normalized && ActiveBatchStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        return batches.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
    }

    public async Task<ChannelBatch> RequireSelectedActiveBatchAsync(string drawerCode, CancellationToken cancellationToken = default)
    {
        var batch = await GetActiveBatchAsync(drawerCode, cancellationToken);
        if (batch is null || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId))
        {
            throw new BusinessRuleException("channel_workflow_required", "Select a channel workflow before adding slides.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
        {
            throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution before it can be used.", StatusCodes.Status409Conflict);
        }

        return batch;
    }

    public static bool IsActiveStatus(string status)
    {
        return ActiveBatchStatuses.Contains(status);
    }

    private async Task<ChannelBatch> LoadTargetBatchAsync(SelectChannelWorkflowRequest request, CancellationToken cancellationToken)
    {
        return await LoadTargetBatchAsync(request.ChannelBatchId, request.DrawerCode, cancellationToken);
    }

    private async Task<ChannelBatch> LoadTargetBatchAsync(string? channelBatchId, string? drawerCodeValue, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(channelBatchId))
        {
            var batch = await dbContext.ChannelBatches
                .Include(x => x.SlideTasks)
                .ThenInclude(x => x.StainingTask)
                .SingleOrDefaultAsync(x => x.Id == channelBatchId.Trim(), cancellationToken);
            return batch ?? throw new BusinessRuleException("channel_batch_not_found", "Channel batch was not found.", StatusCodes.Status404NotFound);
        }

        var drawerCode = NormalizeDrawerCode(drawerCodeValue);
        var batches = await dbContext.ChannelBatches
            .Include(x => x.SlideTasks)
            .ThenInclude(x => x.StainingTask)
            .Where(x => x.DrawerCode == drawerCode && ActiveBatchStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        var activeBatch = batches.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (activeBatch is null)
        {
            throw new BusinessRuleException("channel_batch_not_found", "Channel batch was not found.", StatusCodes.Status404NotFound);
        }

        return activeBatch;
    }

    private static bool IsUnselected(ChannelBatch batch)
    {
        return batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Unselected
            && string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
            && string.IsNullOrWhiteSpace(batch.ExperimentType)
            && (string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson) || batch.WorkflowSnapshotJson == "{}");
    }

    private static void EnsureBatchCanInitialSelect(ChannelBatch batch)
    {
        if (batch.NeedsManualResolution || batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
        {
            throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowLockedAtUtc is not null || batch.StartedAtUtc is not null || !string.IsNullOrWhiteSpace(batch.MachineRunId))
        {
            throw new BusinessRuleException("channel_workflow_locked", "Channel workflow is locked after run start.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Unselected
            || !string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
            || !string.IsNullOrWhiteSpace(batch.ExperimentType)
            || (!string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson) && batch.WorkflowSnapshotJson != "{}"))
        {
            throw new BusinessRuleException("channel_workflow_already_selected", "Channel workflow has already been selected.", StatusCodes.Status409Conflict);
        }

        if (batch.SlideTasks.Count > 0)
        {
            throw new BusinessRuleException("channel_batch_not_empty", "Initial workflow selection is only allowed for an empty channel batch.", StatusCodes.Status409Conflict);
        }
    }

    private async Task<WorkflowVersion> LoadPublishedWorkflowVersionAsync(string workflowVersionId, string experimentType, CancellationToken cancellationToken)
    {
        var normalized = (workflowVersionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("workflow_version_required", "workflowVersionId is required.", StatusCodes.Status400BadRequest);
        }

        var version = await dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.Steps)
            .Include(x => x.ReagentRequirements)
            .SingleOrDefaultAsync(x => x.Id == normalized, cancellationToken);
        if (version is null)
        {
            throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        }

        if (version.Status != WorkflowVersionStatus.Published || version.WorkflowDefinition is null)
        {
            throw new BusinessRuleException("workflow_version_not_published", "Selected workflow version must be published.", StatusCodes.Status409Conflict);
        }

        if (version.WorkflowDefinition.WorkflowType is not (StainingTaskType.He or StainingTaskType.Ihc))
        {
            throw new BusinessRuleException("workflow_type_not_supported", "Only HE and IHC workflows are supported.", StatusCodes.Status409Conflict);
        }

        if (version.WorkflowDefinition.WorkflowType != experimentType)
        {
            throw new BusinessRuleException("workflow_type_mismatch", "Workflow experiment type does not match the requested experiment type.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task<WorkflowVersion> LoadDefaultPublishedWorkflowVersionAsync(string experimentType, CancellationToken cancellationToken)
    {
        var defaults = await dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.Steps)
            .Include(x => x.ReagentRequirements)
            .Where(x => x.DefaultExperimentType == experimentType)
            .ToListAsync(cancellationToken);
        var version = defaults.SingleOrDefault();
        if (version is null)
        {
            throw new BusinessRuleException(
                "default_workflow_not_configured",
                $"No default {experimentType} workflow is configured. Ask an administrator to set one on the workflow configuration page.",
                StatusCodes.Status409Conflict);
        }

        if (version.Status != WorkflowVersionStatus.Published
            || version.WorkflowDefinition is null
            || !version.WorkflowDefinition.IsEnabled
            || version.WorkflowDefinition.WorkflowType != experimentType)
        {
            throw new BusinessRuleException(
                "default_workflow_invalid",
                $"The configured default {experimentType} workflow is not an enabled Published {experimentType} workflow.",
                StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task EnsureBatchCanChangeAsync(
        ChannelBatch batch,
        string newExperimentType,
        string newWorkflowVersionId,
        CancellationToken cancellationToken)
    {
        if (batch.NeedsManualResolution || batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
        {
            throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowLockedAtUtc is not null || batch.StartedAtUtc is not null || !string.IsNullOrWhiteSpace(batch.MachineRunId))
        {
            throw new BusinessRuleException("channel_workflow_locked", "Channel experiment type is locked after run start.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Selected || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId))
        {
            throw new BusinessRuleException("channel_workflow_state_invalid", "Channel workflow selection state is invalid.", StatusCodes.Status409Conflict);
        }

        if (batch.ExperimentType == newExperimentType && batch.SelectedWorkflowVersionId == newWorkflowVersionId)
        {
            throw new BusinessRuleException("channel_default_workflow_unchanged", "This channel already uses the current default workflow for the selected experiment type.", StatusCodes.Status409Conflict);
        }

        var tasks = batch.SlideTasks.Select(x => x.StainingTask).Where(x => x is not null).ToList();
        if (tasks.Any(x => x!.TaskType != newExperimentType))
        {
            throw new BusinessRuleException(
                "channel_experiment_type_incompatible",
                "Existing slides are not compatible with the requested experiment type.",
                StatusCodes.Status409Conflict);
        }

        if (newExperimentType != StainingTaskType.Ihc || tasks.Count == 0)
        {
            return;
        }

        var taskAntibodyCodes = tasks
            .Select(x => x!.ConfirmedPrimaryAntibodyCode ?? x.PrimaryAntibodyCode)
            .ToList();
        if (taskAntibodyCodes.Any(string.IsNullOrWhiteSpace))
        {
            throw new BusinessRuleException(
                "channel_ihc_antibody_missing",
                "Every existing IHC slide must have a confirmed primary antibody code before changing the channel default workflow.",
                StatusCodes.Status409Conflict);
        }

        var antibodyCodes = taskAntibodyCodes
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mappedCodes = await dbContext.PrimaryAntibodyWorkflowMappings
            .Where(x => x.IsEnabled
                && x.WorkflowVersionId == newWorkflowVersionId
                && antibodyCodes.Contains(x.PrimaryAntibodyCode))
            .Select(x => x.PrimaryAntibodyCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (mappedCodes.Count != antibodyCodes.Count)
        {
            throw new BusinessRuleException(
                "channel_ihc_workflow_incompatible",
                "One or more existing IHC primary antibody codes are not mapped to the current default IHC workflow.",
                StatusCodes.Status409Conflict);
        }
    }

    private void AddInitialSelectionHistory(
        ChannelBatch batch,
        AuthenticatedUser actor,
        string commandId,
        string newExperimentType,
        string newWorkflowVersionId,
        string newSnapshot,
        DateTimeOffset now)
    {
        AddAssignmentHistory(
            batch,
            actor,
            commandId,
            WorkflowAssignmentAction.InitialSelection,
            "Initial channel workflow selection.",
            newExperimentType,
            newWorkflowVersionId,
            newSnapshot,
            now);
    }

    private void AddAssignmentHistory(
        ChannelBatch batch,
        AuthenticatedUser actor,
        string commandId,
        string actionType,
        string reason,
        string newExperimentType,
        string newWorkflowVersionId,
        string newSnapshot,
        DateTimeOffset now)
    {
        dbContext.WorkflowAssignmentHistory.Add(new WorkflowAssignmentHistory
        {
            ChannelBatch = batch,
            OldExperimentType = batch.ExperimentType,
            OldWorkflowVersionId = batch.SelectedWorkflowVersionId,
            OldWorkflowSnapshotJson = batch.WorkflowSnapshotJson,
            NewExperimentType = newExperimentType,
            NewWorkflowVersionId = newWorkflowVersionId,
            NewWorkflowSnapshotJson = newSnapshot,
            ActionType = actionType,
            ActorUserId = actor.UserId,
            OperatorUserId = actor.UserId,
            CreatedAtUtc = now,
            Reason = reason,
            CommandId = commandId,
            CorrelationId = commandId
        });
    }

    private static string RequireChangeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("workflow_change_reason_required", "Changing the channel experiment type requires a reason.", StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = "ChannelBatch",
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void PublishChannelBatchChanged(ChannelBatch batch, string reason)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.ChannelBatchChanged,
            batch.MachineRunId,
            "ChannelBatch",
            batch.Id,
            null,
            new Dictionary<string, object?>
            {
                ["channelBatchId"] = batch.Id,
                ["drawerCode"] = batch.DrawerCode,
                ["workflowSelectionStatus"] = batch.WorkflowSelectionStatus,
                ["experimentType"] = batch.ExperimentType,
                ["workflowVersionId"] = batch.SelectedWorkflowVersionId,
                ["reason"] = reason
            }));
    }

    private static string NormalizeDrawerCode(string? drawerCode)
    {
        var normalized = (drawerCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("drawer_code_required", "drawerCode is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeExperimentType(string? experimentType)
    {
        var normalized = (experimentType ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not (StainingTaskType.He or StainingTaskType.Ihc))
        {
            throw new BusinessRuleException("experiment_type_invalid", "ExperimentType must be HE or IHC.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }
}
