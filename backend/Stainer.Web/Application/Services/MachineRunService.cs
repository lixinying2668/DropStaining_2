using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MachineRunService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    PreflightValidationService preflightValidationService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<MachineRunResponse> CreateRunAsync(
        CreateMachineRunRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "run.create",
            request,
            actor,
            async () =>
            {
                if (request.StainingTaskIds.Count == 0)
                {
                    throw new BusinessRuleException("tasks_required", "At least one confirmed task is required.");
                }

                var preflight = string.IsNullOrWhiteSpace(request.PreflightStateHash)
                    ? null
                    : await preflightValidationService.ValidateAsync(cancellationToken);
                if (preflight is not null && (!preflight.Ok || !string.Equals(preflight.StateHash, request.PreflightStateHash, StringComparison.Ordinal)))
                {
                    throw new BusinessRuleException("preflight_invalid", "Latest preflight validation is missing, failed, or stale. Re-run preflight before creating a run.", StatusCodes.Status409Conflict);
                }

                var activeRun = await dbContext.MachineRuns.AnyAsync(
                    x => x.Status == RuntimeLedgerStatus.Created
                        || x.Status == RuntimeLedgerStatus.Running
                        || x.Status == RuntimeLedgerStatus.Paused
                        || x.Status == RuntimeLedgerStatus.Faulted,
                    cancellationToken);
                if (activeRun)
                {
                    throw new BusinessRuleException("active_run_exists", "An active run already exists.", StatusCodes.Status409Conflict);
                }

                var tasks = await dbContext.StainingTasks
                    .Include(x => x.PhysicalSlot)
                    .ThenInclude(x => x!.Drawer)
                    .Where(x => request.StainingTaskIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                if (tasks.Count != request.StainingTaskIds.Distinct().Count())
                {
                    throw new BusinessRuleException("task_not_found", "One or more tasks were not found.", StatusCodes.Status404NotFound);
                }

                if (tasks.Any(x => x.Status != StainingTaskStatus.Confirmed))
                {
                    throw new BusinessRuleException("task_not_confirmed", "All tasks must be confirmed before creating a run.", StatusCodes.Status409Conflict);
                }

                var slideTasks = await dbContext.SlideTasks
                    .Include(x => x.StainingTask)
                    .Include(x => x.PhysicalSlot)
                    .ThenInclude(x => x!.Drawer)
                    .Include(x => x.ChannelBatch)
                    .ThenInclude(x => x!.SelectedWorkflowVersion)
                    .ThenInclude(x => x!.WorkflowDefinition)
                    .Where(x => request.StainingTaskIds.Contains(x.StainingTaskId))
                    .ToListAsync(cancellationToken);
                if (slideTasks.Count != tasks.Count)
                {
                    throw new BusinessRuleException("channel_batch_required", "All tasks must belong to a selected channel batch before creating a run.", StatusCodes.Status409Conflict);
                }

                var batchIds = slideTasks.Select(x => x.ChannelBatchId).Distinct().ToList();
                var batches = await dbContext.ChannelBatches
                    .Include(x => x.SlideTasks)
                    .ThenInclude(x => x.StainingTask)
                    .Include(x => x.SelectedWorkflowVersion)
                    .ThenInclude(x => x!.WorkflowDefinition)
                    .Where(x => batchIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                ValidateBatchesForRun(request.StainingTaskIds, batches);

                var now = DateTimeOffset.UtcNow;
                var run = new MachineRun
                {
                    RunCode = $"RUN-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
                    Status = RuntimeLedgerStatus.Created,
                    RequestedByUserId = actor.UserId,
                    CreatedAtUtc = now
                };
                dbContext.MachineRuns.Add(run);

                foreach (var batch in batches.OrderBy(x => x.DrawerCode))
                {
                    batch.MachineRun = run;
                    batch.Status = RuntimeLedgerStatus.Pending;
                    run.ChannelBatches.Add(batch);

                    var selectedWorkflowVersionId = batch.SelectedWorkflowVersionId!;
                    foreach (var slideTask in batch.SlideTasks.OrderBy(x => x.PhysicalSlot?.SlotNo ?? int.MaxValue).ThenBy(x => x.SlotCode))
                    {
                        slideTask.Status = RuntimeLedgerStatus.Pending;

                        var workflowExecution = new WorkflowExecution
                        {
                            MachineRun = run,
                            SlideTask = slideTask,
                            WorkflowVersionId = selectedWorkflowVersionId,
                            Status = RuntimeLedgerStatus.Pending,
                            CreatedAtUtc = now
                        };
                        slideTask.WorkflowExecutions.Add(workflowExecution);
                        run.WorkflowExecutions.Add(workflowExecution);

                        foreach (var step in await BuildStepExecutionsAsync(batch, slideTask, workflowExecution, cancellationToken))
                        {
                            workflowExecution.StepExecutions.Add(step);
                        }
                    }
                }

                await AddReservationsAsync(run, cancellationToken);
                AddAudit(actor, "run.create", run.Id, new { run.RunCode, taskIds = tasks.Select(x => x.Id).ToArray() });

                return new CommandExecutionResult<MachineRunResponse>(
                    new MachineRunResponse(true, request.CommandId, false, run.Id, run.RunCode, run.Status, "Run created."),
                    "MachineRun",
                    run.Id);
            },
            cancellationToken);
    }

    private static void ValidateBatchesForRun(IReadOnlyList<string> requestedTaskIds, IReadOnlyList<ChannelBatch> batches)
    {
        var requested = requestedTaskIds.ToHashSet(StringComparer.Ordinal);
        foreach (var batch in batches)
        {
            if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
            {
                throw new BusinessRuleException("channel_batch_needs_manual_resolution", "Channel batch needs manual workflow resolution before it can start.", StatusCodes.Status409Conflict);
            }

            if (batch.WorkflowSelectionStatus != WorkflowSelectionStatus.Selected
                || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
                || string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson)
                || batch.WorkflowSnapshotJson == "{}")
            {
                throw new BusinessRuleException("channel_workflow_required", "Each channel batch must have a selected workflow before creating a run.", StatusCodes.Status409Conflict);
            }

            if (batch.WorkflowLockedAtUtc is not null || batch.StartedAtUtc is not null || !string.IsNullOrWhiteSpace(batch.MachineRunId))
            {
                throw new BusinessRuleException("channel_batch_locked", "Channel batch is already assigned to a run.", StatusCodes.Status409Conflict);
            }

            if (batch.SlideTasks.Count is < 1 or > 4)
            {
                throw new BusinessRuleException("drawer_batch_size_invalid", "Each drawer batch must contain 1 to 4 confirmed slides.", StatusCodes.Status409Conflict);
            }

            var batchTaskIds = batch.SlideTasks.Select(x => x.StainingTaskId).ToHashSet(StringComparer.Ordinal);
            if (!batchTaskIds.IsSubsetOf(requested) || !batchTaskIds.SetEquals(requested.Intersect(batchTaskIds, StringComparer.Ordinal)))
            {
                throw new BusinessRuleException("channel_batch_incomplete", "A run must include every slide in each selected channel batch.", StatusCodes.Status409Conflict);
            }

            foreach (var slideTask in batch.SlideTasks)
            {
                if (slideTask.StainingTask is null || slideTask.StainingTask.Status != StainingTaskStatus.Confirmed)
                {
                    throw new BusinessRuleException("task_not_confirmed", "All slides in the channel batch must be confirmed before creating a run.", StatusCodes.Status409Conflict);
                }

                if (slideTask.TaskType != batch.ExperimentType || slideTask.StainingTask.TaskType != batch.ExperimentType)
                {
                    throw new BusinessRuleException("channel_experiment_type_mismatch", "All slides in a channel must match the selected experiment type.", StatusCodes.Status409Conflict);
                }

                if (slideTask.StainingTask.WorkflowVersionId != batch.SelectedWorkflowVersionId)
                {
                    throw new BusinessRuleException("channel_workflow_mismatch", "Slide workflow must match the selected channel workflow.", StatusCodes.Status409Conflict);
                }
            }
        }
    }

    private async Task<IReadOnlyList<WorkflowStepExecution>> BuildStepExecutionsAsync(
        ChannelBatch batch,
        SlideTask slideTask,
        WorkflowExecution workflowExecution,
        CancellationToken cancellationToken)
    {
        var workflowVersionId = batch.SelectedWorkflowVersionId!;
        var steps = await dbContext.WorkflowSteps
            .AsNoTracking()
            .Where(x => x.WorkflowVersionId == workflowVersionId)
            .OrderBy(x => x.StepNo)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            steps = batch.ExperimentType == StainingTaskType.He
                ? SyntheticHeSteps(workflowVersionId)
                : SyntheticIhcSteps(workflowVersionId);
        }

        return steps.Select(x => new WorkflowStepExecution
        {
            WorkflowExecution = workflowExecution,
            StepNo = x.StepNo,
            MajorStepCode = NormalizeMajorStep(batch.ExperimentType ?? slideTask.TaskType, x.MajorStepCode),
            StepName = string.IsNullOrWhiteSpace(x.StepName) ? x.MajorStepCode : x.StepName,
            ActionType = x.ActionType,
            ReagentCode = x.ReagentCode,
            VolumeUl = x.VolumeUl,
            Status = RuntimeLedgerStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }).ToList();
    }

    private async Task AddReservationsAsync(MachineRun run, CancellationToken cancellationToken)
    {
        var workflowVersionIds = run.WorkflowExecutions.Select(x => x.WorkflowVersionId).Distinct().ToList();
        var requirements = await dbContext.WorkflowReagentRequirements
            .AsNoTracking()
            .Where(x => workflowVersionIds.Contains(x.WorkflowVersionId) && x.IsRequired)
            .GroupBy(x => x.ReagentCode)
            .Select(x => new { ReagentCode = x.Key, RequiredVolumeUl = x.Sum(y => y.RequiredVolumeUl ?? 0) })
            .ToListAsync(cancellationToken);

        foreach (var requirement in requirements)
        {
            dbContext.ReagentReservations.Add(new ReagentReservation
            {
                MachineRun = run,
                ReagentCode = requirement.ReagentCode,
                RequiredVolumeUl = requirement.RequiredVolumeUl,
                ReservedVolumeUl = requirement.RequiredVolumeUl,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = "MachineRun",
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string NormalizeMajorStep(string taskType, string majorStepCode)
    {
        if (!string.IsNullOrWhiteSpace(majorStepCode) && majorStepCode != "STEP")
        {
            return majorStepCode;
        }

        return taskType == StainingTaskType.He ? "HEMATOXYLIN" : "PRIMARY_ANTIBODY";
    }

    private static List<WorkflowStep> SyntheticHeSteps(string workflowVersionId)
    {
        return
        [
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 1, MajorStepCode = "HEMATOXYLIN", StepName = "Hematoxylin", ActionType = "Dispense", ReagentCode = "HEM", VolumeUl = 200 },
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 2, MajorStepCode = "TERMINAL_WASH", StepName = "Terminal wash", ActionType = "Wash" }
        ];
    }

    private static List<WorkflowStep> SyntheticIhcSteps(string workflowVersionId)
    {
        return
        [
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 1, MajorStepCode = "PRETREATMENT", StepName = "Pretreatment", ActionType = "Heat" },
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 2, MajorStepCode = "PRIMARY_ANTIBODY", StepName = "Primary antibody", ActionType = "Dispense", ReagentCode = "PAB", VolumeUl = 100 },
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 3, MajorStepCode = "SECONDARY_ANTIBODY", StepName = "Secondary antibody", ActionType = "Dispense", ReagentCode = "SEC", VolumeUl = 100 },
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 4, MajorStepCode = "DAB", StepName = "DAB", ActionType = "Dab", ReagentCode = "DAB", VolumeUl = 100 },
            new WorkflowStep { WorkflowVersionId = workflowVersionId, StepNo = 5, MajorStepCode = "HEMATOXYLIN", StepName = "Hematoxylin", ActionType = "Dispense", ReagentCode = "HEM", VolumeUl = 100 }
        ];
    }
}
