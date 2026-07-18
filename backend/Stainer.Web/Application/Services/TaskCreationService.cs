using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class TaskCreationService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    ChannelBatchWorkflowService channelBatchWorkflowService,
    WorkflowPrimaryAntibodyResolver workflowPrimaryAntibodyResolver,
    IRuntimeEventPublisher eventPublisher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CompatibilityCompatible = "Compatible";
    private const string CompatibilityMessage = "一抗由所选染色流程确定。";

    public Task<TaskCreationResponse> CreateHeTaskAsync(
        CreateHeTaskRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "task.create_he",
            request,
            actor,
            async () =>
            {
                var (slot, batch) = await LoadSlotAndBatchAsync(
                    request.SlotCode,
                    request.DrawerCode,
                    request.ChannelBatchId,
                    cancellationToken);
                var legacyWorkflowVersionId = NormalizeOptional(request.WorkflowVersionId);
                EnsureCanAddSlideToBatch(batch, StainingTaskType.He, legacyWorkflowVersionId);
                var version = await LoadChannelWorkflowVersionAsync(batch, StainingTaskType.He, cancellationToken);
                var task = CreateTask(
                    request.CommandId,
                    StainingTaskType.He,
                    slot,
                    batch,
                    version,
                    actor,
                    inputMode: "ManualHE",
                    rawCode: null,
                    normalizedCode: null,
                    primaryAntibodyCode: null,
                    candidateResults: Array.Empty<object>(),
                    rawSampleCode: null,
                    normalizedSampleCode: null,
                    lisQueryLogId: null,
                    lisCandidatePrimaryAntibodyCodes: [],
                    confirmedPrimaryAntibodyCode: null,
                    compatibilityValidationStatus: null,
                    compatibilityValidationMessage: null);
                dbContext.StainingTasks.Add(task);
                var slideTask = AddSlideTask(batch, task, slot, StainingTaskType.He);
                AddAudit(actor, "task.create_he", "StainingTask", task.Id, new
                {
                    task.TaskCode,
                    slot = slot.Code,
                    channelBatchId = batch.Id,
                    drawerCode = batch.DrawerCode,
                    inheritedWorkflowVersionId = batch.SelectedWorkflowVersionId,
                    legacyWorkflowVersionCompatibilityField = legacyWorkflowVersionId is not null,
                    legacyWorkflowVersionId
                });
                PublishSlideTaskCreated(batch, task, slot, slideTask);
                return new CommandExecutionResult<TaskCreationResponse>(
                    CreatedResponse(request.CommandId, false, task, batch, "HE task created."),
                    "StainingTask",
                    task.Id);
            },
            cancellationToken);
    }

    public async Task<TaskCreationResponse> CreateIhcTaskAsync(
        CreateIhcTaskRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var legacyWorkflowVersionId = GetLegacyWorkflowVersionId(request);
        return await idempotencyService.RunAsync(
            request.CommandId,
            "task.create_ihc",
            request,
            actor,
            async () =>
            {
                var (slot, batch) = await LoadSlotAndBatchAsync(
                    request.SlotCode,
                    request.DrawerCode,
                    request.ChannelBatchId,
                    cancellationToken);
                // 一抗由所选染色流程决定：忽略客户端 legacy 选抗字段对流程选择的影响。
                EnsureCanAddSlideToBatch(batch, StainingTaskType.Ihc, requestedWorkflowVersionId: null);
                var version = await LoadChannelWorkflowVersionAsync(batch, StainingTaskType.Ihc, cancellationToken);
                var resolution = workflowPrimaryAntibodyResolver.Resolve(version);
                if (resolution.Status != PrimaryAntibodyResolutionStatus.Resolved || string.IsNullOrWhiteSpace(resolution.Code))
                {
                    throw resolution.Status switch
                    {
                        PrimaryAntibodyResolutionStatus.StepMissing => new BusinessRuleException(
                            "ihc_workflow_primary_antibody_step_missing",
                            "所选染色流程缺少一抗孵育步骤，无法确定一抗。",
                            StatusCodes.Status409Conflict),
                        PrimaryAntibodyResolutionStatus.CodeEmpty => new BusinessRuleException(
                            "ihc_workflow_primary_antibody_code_empty",
                            "所选染色流程的一抗孵育步骤未配置一抗编码（reagentCode 为空）。",
                            StatusCodes.Status409Conflict),
                        PrimaryAntibodyResolutionStatus.Conflict => new BusinessRuleException(
                            "ihc_workflow_primary_antibody_code_conflict",
                            "所选染色流程存在多个不一致的一抗编码，无法唯一确定一抗。",
                            StatusCodes.Status409Conflict),
                        _ => new BusinessRuleException(
                            "ihc_workflow_primary_antibody_step_missing",
                            "所选染色流程缺少一抗孵育步骤，无法确定一抗。",
                            StatusCodes.Status409Conflict),
                    };
                }

                var primaryAntibodyCode = resolution.Code;
                var rawCode = NormalizeOptional(request.RawCode);
                var inputMode = NormalizeOptional(request.InputMode);
                var lisQueryLogId = NormalizeOptional(request.LisQueryLogId);

                var task = CreateTask(
                    request.CommandId,
                    StainingTaskType.Ihc,
                    slot,
                    batch,
                    version,
                    actor,
                    inputMode,
                    rawCode,
                    rawCode,
                    primaryAntibodyCode,
                    new[]
                    {
                        new
                        {
                            source = "workflow",
                            confirmedPrimaryAntibodyCode = primaryAntibodyCode,
                            inheritedWorkflowVersionId = version.Id,
                            compatibilityValidationStatus = CompatibilityCompatible,
                            compatibilityValidationMessage = CompatibilityMessage
                        }
                    },
                    rawSampleCode: rawCode,
                    normalizedSampleCode: rawCode,
                    lisQueryLogId,
                    lisCandidatePrimaryAntibodyCodes: Array.Empty<string>(),
                    confirmedPrimaryAntibodyCode: primaryAntibodyCode,
                    compatibilityValidationStatus: CompatibilityCompatible,
                    compatibilityValidationMessage: CompatibilityMessage);
                dbContext.StainingTasks.Add(task);
                var slideTask = AddSlideTask(batch, task, slot, StainingTaskType.Ihc);
                AddAudit(actor, "task.create_ihc", "StainingTask", task.Id, new
                {
                    task.TaskCode,
                    slot = slot.Code,
                    channelBatchId = batch.Id,
                    drawerCode = batch.DrawerCode,
                    task.RawSampleCode,
                    task.NormalizedSampleCode,
                    task.LisQueryLogId,
                    task.ConfirmedPrimaryAntibodyCode,
                    primaryAntibodySource = "workflow",
                    inheritedWorkflowVersionId = batch.SelectedWorkflowVersionId,
                    task.CompatibilityValidationStatus,
                    task.CompatibilityValidationMessage,
                    legacyWorkflowVersionCompatibilityField = legacyWorkflowVersionId is not null,
                    legacyWorkflowVersionId
                });
                PublishSlideTaskCreated(batch, task, slot, slideTask);
                return new CommandExecutionResult<TaskCreationResponse>(
                    CreatedResponse(request.CommandId, false, task, batch, "IHC task created."),
                    "StainingTask",
                    task.Id);
            },
            cancellationToken);
    }

    private async Task<PhysicalSlot> LoadIdleSlotAsync(string slotCode, CancellationToken cancellationToken)
    {
        var normalized = (slotCode ?? string.Empty).Trim();
        var slot = await dbContext.PhysicalSlots
            .Include(x => x.Drawer)
            .SingleOrDefaultAsync(x => x.Code == normalized, cancellationToken);
        if (slot is null)
        {
            throw new BusinessRuleException("slot_not_found", "Physical slot was not found.", StatusCodes.Status404NotFound);
        }

        var occupied = await dbContext.StainingTasks.AnyAsync(
            x => x.PhysicalSlotId == slot.Id && x.Status == StainingTaskStatus.Confirmed,
            cancellationToken);
        if (occupied)
        {
            throw new BusinessRuleException("slot_not_idle", "Selected slot is not idle.", StatusCodes.Status409Conflict);
        }

        return slot;
    }

    private async Task<(PhysicalSlot Slot, ChannelBatch Batch)> LoadSlotAndBatchAsync(
        string slotCode,
        string? drawerCode,
        string? channelBatchId,
        CancellationToken cancellationToken)
    {
        var slot = await LoadIdleSlotAsync(slotCode, cancellationToken);
        var normalizedDrawerCode = NormalizeOptional(drawerCode)?.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(channelBatchId))
        {
            var batch = await LoadSelectedBatchByIdAsync(channelBatchId.Trim(), cancellationToken);
            if (!string.Equals(batch.DrawerId, slot.DrawerId, StringComparison.Ordinal))
            {
                throw new BusinessRuleException("channel_slot_mismatch", "Physical slot must belong to the selected channel.", StatusCodes.Status409Conflict);
            }

            if (normalizedDrawerCode is not null && !string.Equals(batch.DrawerCode, normalizedDrawerCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("channel_slot_mismatch", "drawerCode does not match the selected channel batch.", StatusCodes.Status409Conflict);
            }

            return (slot, batch);
        }

        if (normalizedDrawerCode is null)
        {
            throw new BusinessRuleException("channel_required", "drawerCode or channelBatchId is required.", StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(slot.Drawer?.Code, normalizedDrawerCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("channel_slot_mismatch", "Physical slot must belong to the requested drawer.", StatusCodes.Status409Conflict);
        }

        var activeBatch = await channelBatchWorkflowService.RequireSelectedActiveBatchAsync(normalizedDrawerCode, cancellationToken);
        return (slot, activeBatch);
    }

    private async Task<ChannelBatch> LoadSelectedBatchByIdAsync(string channelBatchId, CancellationToken cancellationToken)
    {
        var batch = await dbContext.ChannelBatches
            .Include(x => x.SlideTasks)
            .ThenInclude(x => x.StainingTask)
            .Include(x => x.SelectedWorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .SingleOrDefaultAsync(x => x.Id == channelBatchId, cancellationToken);
        if (batch is null)
        {
            throw new BusinessRuleException("channel_batch_not_found", "Channel batch was not found.", StatusCodes.Status404NotFound);
        }

        if (!ChannelBatchWorkflowService.IsActiveStatus(batch.Status))
        {
            throw new BusinessRuleException("channel_batch_not_active", "Channel batch is not active.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution || batch.NeedsManualResolution)
        {
            throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution before it can be used.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Selected || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId))
        {
            throw new BusinessRuleException("channel_workflow_required", "Select a channel workflow before adding slides.", StatusCodes.Status409Conflict);
        }

        return batch;
    }

    private async Task<WorkflowVersion> LoadPublishedWorkflowVersionAsync(
        string workflowVersionId,
        string workflowType,
        CancellationToken cancellationToken)
    {
        var version = await dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.Steps)
            .Include(x => x.ReagentRequirements)
            .SingleOrDefaultAsync(x => x.Id == workflowVersionId, cancellationToken);
        if (version is null)
        {
            throw new BusinessRuleException("workflow_version_not_found", "Workflow version was not found.", StatusCodes.Status404NotFound);
        }

        if (version.Status != WorkflowVersionStatus.Published || version.WorkflowDefinition?.WorkflowType != workflowType)
        {
            throw new BusinessRuleException("workflow_version_not_published", "Selected workflow version must be a published workflow of the requested type.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task<WorkflowVersion> LoadChannelWorkflowVersionAsync(
        ChannelBatch batch,
        string workflowType,
        CancellationToken cancellationToken)
    {
        if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Selected
            || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
            || string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson)
            || batch.WorkflowSnapshotJson == "{}")
        {
            throw new BusinessRuleException("channel_workflow_required", "Select a channel workflow before adding slides.", StatusCodes.Status409Conflict);
        }

        var version = await LoadPublishedWorkflowVersionAsync(batch.SelectedWorkflowVersionId, workflowType, cancellationToken);
        if (batch.ExperimentType != workflowType)
        {
            throw new BusinessRuleException("channel_experiment_type_mismatch", "Task type must match the selected channel workflow.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private static StainingTask CreateTask(
        string commandId,
        string taskType,
        PhysicalSlot slot,
        ChannelBatch batch,
        WorkflowVersion version,
        AuthenticatedUser actor,
        string? inputMode,
        string? rawCode,
        string? normalizedCode,
        string? primaryAntibodyCode,
        object candidateResults,
        string? rawSampleCode,
        string? normalizedSampleCode,
        string? lisQueryLogId,
        IReadOnlyList<string> lisCandidatePrimaryAntibodyCodes,
        string? confirmedPrimaryAntibodyCode,
        string? compatibilityValidationStatus,
        string? compatibilityValidationMessage)
    {
        return new StainingTask
        {
            TaskCode = $"{taskType}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28],
            TaskType = taskType,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = slot.Id,
            WorkflowDefinitionId = version.WorkflowDefinitionId,
            WorkflowVersionId = batch.SelectedWorkflowVersionId!,
            WorkflowSnapshotJson = batch.WorkflowSnapshotJson,
            InputMode = inputMode,
            RawCode = rawCode,
            NormalizedCode = normalizedCode,
            PrimaryAntibodyCode = primaryAntibodyCode,
            CandidateResultsJson = JsonSerializer.Serialize(candidateResults, JsonOptions),
            RawSampleCode = rawSampleCode,
            NormalizedSampleCode = normalizedSampleCode,
            LisQueryLogId = lisQueryLogId,
            LisCandidatePrimaryAntibodyCodesJson = lisCandidatePrimaryAntibodyCodes.Count == 0
                ? null
                : JsonSerializer.Serialize(lisCandidatePrimaryAntibodyCodes, JsonOptions),
            ConfirmedPrimaryAntibodyCode = confirmedPrimaryAntibodyCode,
            CompatibilityValidationStatus = compatibilityValidationStatus,
            CompatibilityValidationMessage = compatibilityValidationMessage,
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private SlideTask AddSlideTask(ChannelBatch batch, StainingTask task, PhysicalSlot slot, string taskType)
    {
        var slideTask = new SlideTask
        {
            ChannelBatch = batch,
            StainingTask = task,
            PhysicalSlotId = slot.Id,
            SlotCode = slot.Code,
            TaskType = taskType,
            Status = RuntimeLedgerStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.SlideTasks.Add(slideTask);
        return slideTask;
    }

    private static void EnsureCanAddSlideToBatch(ChannelBatch batch, string taskType, string? requestedWorkflowVersionId)
    {
        if (batch.WorkflowLockedAtUtc is not null || batch.StartedAtUtc is not null || !string.IsNullOrWhiteSpace(batch.MachineRunId))
        {
            throw new BusinessRuleException("channel_batch_locked", "Cannot add slides after the channel batch has started.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
        {
            throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution.", StatusCodes.Status409Conflict);
        }

        if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Selected
            || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
            || string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson)
            || batch.WorkflowSnapshotJson == "{}")
        {
            throw new BusinessRuleException("channel_workflow_required", "Select a channel workflow before adding slides.", StatusCodes.Status409Conflict);
        }

        if (batch.ExperimentType != taskType)
        {
            throw new BusinessRuleException("channel_experiment_type_mismatch", "All slides in a channel must share the selected experiment type.", StatusCodes.Status409Conflict);
        }

        if (requestedWorkflowVersionId is not null && requestedWorkflowVersionId != batch.SelectedWorkflowVersionId)
        {
            throw new BusinessRuleException("channel_workflow_mismatch", "Slide workflow must match the selected channel workflow.", StatusCodes.Status409Conflict);
        }

        if (batch.SlideTasks.Count >= 4)
        {
            throw new BusinessRuleException("channel_batch_full", "A channel batch can contain at most 4 slides.", StatusCodes.Status409Conflict);
        }
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityType, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void PublishSlideTaskCreated(ChannelBatch batch, StainingTask task, PhysicalSlot slot, SlideTask slideTask)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.SlideTaskCreated,
            batch.MachineRunId,
            "SlideTask",
            slideTask.Id,
            null,
            new Dictionary<string, object?>
            {
                ["channelBatchId"] = batch.Id,
                ["drawerCode"] = batch.DrawerCode,
                ["slotCode"] = slot.Code,
                ["slideTaskId"] = slideTask.Id,
                ["stainingTaskId"] = task.Id,
                ["taskType"] = task.TaskType,
                ["workflowVersionId"] = batch.SelectedWorkflowVersionId
            }));
    }

    private static TaskCreationResponse CreatedResponse(string commandId, bool replayed, StainingTask task, ChannelBatch batch, string message)
    {
        return new TaskCreationResponse(
            true,
            commandId,
            replayed,
            false,
            message,
            task.Id,
            task.TaskCode,
            [],
            [],
            batch.Id,
            batch.DrawerCode,
            batch.ExperimentType,
            batch.SelectedWorkflowVersionId,
            batch.WorkflowSelectionStatus,
            task.CompatibilityValidationStatus,
            task.CompatibilityValidationMessage);
    }

    private static string? GetLegacyWorkflowVersionId(CreateIhcTaskRequest request)
    {
        var selectedWorkflowVersionId = NormalizeOptional(request.SelectedWorkflowVersionId);
        var workflowVersionId = NormalizeOptional(request.WorkflowVersionId);
        if (selectedWorkflowVersionId is not null
            && workflowVersionId is not null
            && selectedWorkflowVersionId != workflowVersionId)
        {
            throw new BusinessRuleException("legacy_workflow_version_conflict", "Legacy workflow version fields must match when both are provided.", StatusCodes.Status409Conflict);
        }

        return selectedWorkflowVersionId ?? workflowVersionId;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
