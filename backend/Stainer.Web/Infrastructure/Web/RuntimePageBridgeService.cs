using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Web;

public sealed class RuntimePageBridgeService(
    MockRuntimeStore store,
    StainerDbContext dbContext,
    MachineRunService machineRunService,
    MachineRunQueryService machineRunQueryService,
    RunControlService runControlService)
{
    public async Task<MockRuntimeState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var run = await machineRunQueryService.GetCurrentAsync(cancellationToken);
        return run is null ? store.GetState() : ToPageState(store.GetState(), run);
    }

    public async Task<MockRuntimeState> RunActionAsync(string action, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        var run = await machineRunQueryService.GetCurrentAsync(cancellationToken);
        if (run is null || IsTerminal(run.Status))
        {
            if (normalizedAction != "start")
            {
                return store.RunAction(normalizedAction);
            }

            var taskIds = await dbContext.StainingTasks
                .AsNoTracking()
                .Where(x => x.Status == StainingTaskStatus.Confirmed)
                .OrderBy(x => x.TaskCode)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (taskIds.Count == 0)
            {
                return store.RunAction("start");
            }

            var created = await machineRunService.CreateRunAsync(
                new CreateMachineRunRequest(NewCommandId("ui-run-create"), taskIds),
                actor,
                cancellationToken);
            await runControlService.StartAsync(
                created.RunId,
                new RunCommandRequest(NewCommandId("ui-run-start")),
                actor,
                cancellationToken);
            var createdRun = await machineRunQueryService.GetAsync(created.RunId, cancellationToken);
            return createdRun is null ? store.RunAction("start") : ToPageState(store.GetState(), createdRun);
        }

        _ = normalizedAction switch
        {
            "start" => await runControlService.StartAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-start")), actor, cancellationToken),
            "pause" => await runControlService.PauseAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-pause")), actor, cancellationToken),
            "resume" => await runControlService.ResumeAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-resume")), actor, cancellationToken),
            "stop" => await runControlService.StopAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-stop")), actor, cancellationToken),
            _ => throw new BusinessRuleException("run_action_unknown", $"Unknown run action: {action}", StatusCodes.Status400BadRequest)
        };

        var updated = await machineRunQueryService.GetAsync(run.Id, cancellationToken);
        return updated is null ? store.GetState() : ToPageState(store.GetState(), updated);
    }

    private static MockRuntimeState ToPageState(MockRuntimeState fallback, MachineRunDetailResponse run)
    {
        var workflowsBySlide = run.WorkflowExecutions.ToDictionary(x => x.SlideTaskId, StringComparer.Ordinal);
        var channels = Enumerable.Range(0, 4)
            .Select(index => new MockChannel
            {
                Id = index + 1,
                Name = $"Channel{index + 1}",
                Status = "empty",
                CurrentStep = "Empty"
            })
            .ToList();

        foreach (var batch in run.ChannelBatches)
        {
            var channelIndex = Math.Clamp(batch.DrawerCode[0] - 'A', 0, 3);
            var channel = channels[channelIndex];
            channel.Status = MapPageStatus(batch.Status);
            channel.Progress = CalculateBatchProgress(batch, workflowsBySlide);
            channel.CurrentStep = CurrentBatchStep(batch, workflowsBySlide);
            channel.Slides = batch.Slides
                .Select(slide => ToPageSlide(batch.DrawerCode, slide, workflowsBySlide.GetValueOrDefault(slide.Id)))
                .ToList();
        }

        return new MockRuntimeState
        {
            RunId = run.Id,
            Status = MapPageStatus(run.Status),
            Initialized = fallback.Initialized,
            System = fallback.System,
            Channels = channels,
            Reagents = fallback.Reagents,
            ActiveUser = fallback.ActiveUser,
            Alarms = run.Alarms.Select(x => $"{x.Code}: {x.Message}").ToList(),
            Logs = run.Alarms.Select(x => $"[{x.Severity}] {x.Code}: {x.Message}")
                .Concat(fallback.Logs)
                .Take(80)
                .ToList()
        };
    }

    private static MockSlide ToPageSlide(string drawerCode, SlideTaskResponse slide, WorkflowExecutionResponse? workflow)
    {
        var step = CurrentStep(workflow);
        return new MockSlide
        {
            Id = slide.Id,
            Channel = Math.Clamp(drawerCode[0] - 'A' + 1, 1, 4),
            Slot = ParseSlotNo(slide.SlotCode),
            Barcode = slide.Id[^Math.Min(8, slide.Id.Length)..],
            ProtocolCode = slide.TaskType,
            Status = MapPageStatus(slide.Status),
            CurrentStep = step?.StepName ?? step?.MajorStepCode ?? "Waiting",
            Progress = CalculateProgress(workflow)
        };
    }

    private static WorkflowStepExecutionResponse? CurrentStep(WorkflowExecutionResponse? workflow)
    {
        if (workflow is null)
        {
            return null;
        }

        return workflow.Steps.FirstOrDefault(x => x.Status is RuntimeLedgerStatus.Running or RuntimeLedgerStatus.Unknown or RuntimeLedgerStatus.Failed)
            ?? workflow.Steps.FirstOrDefault(x => x.Status == RuntimeLedgerStatus.Pending)
            ?? workflow.Steps.LastOrDefault();
    }

    private static int CalculateBatchProgress(ChannelBatchResponse batch, IReadOnlyDictionary<string, WorkflowExecutionResponse> workflowsBySlide)
    {
        var workflows = batch.Slides
            .Select(x => workflowsBySlide.GetValueOrDefault(x.Id))
            .Where(x => x is not null)
            .Cast<WorkflowExecutionResponse>()
            .ToList();
        if (workflows.Count == 0)
        {
            return 0;
        }

        return (int)Math.Round(workflows.Average(CalculateProgress));
    }

    private static int CalculateProgress(WorkflowExecutionResponse? workflow)
    {
        if (workflow is null || workflow.Steps.Count == 0)
        {
            return 0;
        }

        var completed = workflow.Steps.Count(x => x.Status == RuntimeLedgerStatus.Completed);
        return Math.Clamp((int)Math.Round(completed * 100.0 / workflow.Steps.Count), 0, 100);
    }

    private static string CurrentBatchStep(ChannelBatchResponse batch, IReadOnlyDictionary<string, WorkflowExecutionResponse> workflowsBySlide)
    {
        foreach (var slide in batch.Slides)
        {
            var step = CurrentStep(workflowsBySlide.GetValueOrDefault(slide.Id));
            if (step is not null && step.Status != RuntimeLedgerStatus.Completed)
            {
                return step.StepName;
            }
        }

        return batch.Status == RuntimeLedgerStatus.Completed ? "Completed" : "Waiting";
    }

    private static int ParseSlotNo(string slotCode)
    {
        var suffix = slotCode.Split('-').LastOrDefault();
        return int.TryParse(suffix, out var slotNo) ? slotNo : 1;
    }

    private static bool IsTerminal(string status)
    {
        return status is RuntimeLedgerStatus.Completed or RuntimeLedgerStatus.Stopped;
    }

    private static string MapPageStatus(string status)
    {
        return status switch
        {
            RuntimeLedgerStatus.Created or RuntimeLedgerStatus.Pending => "configured",
            RuntimeLedgerStatus.Running => "running",
            RuntimeLedgerStatus.Paused => "paused",
            RuntimeLedgerStatus.Stopped => "stopped",
            RuntimeLedgerStatus.Completed => "completed",
            RuntimeLedgerStatus.WaitingUnload => "waiting",
            RuntimeLedgerStatus.Failed or RuntimeLedgerStatus.Faulted or RuntimeLedgerStatus.Unknown => "error",
            _ => status.ToLowerInvariant()
        };
    }

    private static string NewCommandId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
