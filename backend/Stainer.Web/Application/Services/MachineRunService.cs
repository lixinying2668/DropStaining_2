using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MachineRunService(StainerDbContext dbContext, CommandIdempotencyService idempotencyService)
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

                var drawerGroups = tasks.GroupBy(x => x.PhysicalSlot!.Drawer!.Code).ToList();
                if (drawerGroups.Any(x => x.Count() is < 1 or > 3))
                {
                    throw new BusinessRuleException("drawer_batch_size_invalid", "Each drawer batch must contain 1 to 3 confirmed slides.", StatusCodes.Status409Conflict);
                }

                var run = new MachineRun
                {
                    RunCode = $"RUN-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
                    Status = RuntimeLedgerStatus.Created,
                    RequestedByUserId = actor.UserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.MachineRuns.Add(run);

                foreach (var drawerGroup in drawerGroups.OrderBy(x => x.Key))
                {
                    var firstTask = drawerGroup.First();
                    var batch = new ChannelBatch
                    {
                        MachineRun = run,
                        DrawerId = firstTask.PhysicalSlot!.DrawerId,
                        DrawerCode = drawerGroup.Key,
                        Status = RuntimeLedgerStatus.Pending,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    run.ChannelBatches.Add(batch);

                    foreach (var task in drawerGroup.OrderBy(x => x.PhysicalSlot!.SlotNo))
                    {
                        var slideTask = new SlideTask
                        {
                            ChannelBatch = batch,
                            StainingTaskId = task.Id,
                            PhysicalSlotId = task.PhysicalSlotId,
                            SlotCode = task.PhysicalSlot!.Code,
                            TaskType = task.TaskType,
                            Status = RuntimeLedgerStatus.Pending,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        };
                        batch.SlideTasks.Add(slideTask);

                        var workflowExecution = new WorkflowExecution
                        {
                            MachineRun = run,
                            SlideTask = slideTask,
                            WorkflowVersionId = task.WorkflowVersionId,
                            Status = RuntimeLedgerStatus.Pending,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        };
                        slideTask.WorkflowExecutions.Add(workflowExecution);
                        run.WorkflowExecutions.Add(workflowExecution);

                        foreach (var step in await BuildStepExecutionsAsync(task, workflowExecution, cancellationToken))
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

    private async Task<IReadOnlyList<WorkflowStepExecution>> BuildStepExecutionsAsync(
        StainingTask task,
        WorkflowExecution workflowExecution,
        CancellationToken cancellationToken)
    {
        var steps = await dbContext.WorkflowSteps
            .AsNoTracking()
            .Where(x => x.WorkflowVersionId == task.WorkflowVersionId)
            .OrderBy(x => x.StepNo)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            steps = task.TaskType == StainingTaskType.He
                ? SyntheticHeSteps(task.WorkflowVersionId)
                : SyntheticIhcSteps(task.WorkflowVersionId);
        }

        return steps.Select(x => new WorkflowStepExecution
        {
            WorkflowExecution = workflowExecution,
            StepNo = x.StepNo,
            MajorStepCode = NormalizeMajorStep(task.TaskType, x.MajorStepCode),
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
