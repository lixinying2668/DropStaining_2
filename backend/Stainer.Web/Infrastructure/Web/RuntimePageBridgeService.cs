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
    RunControlService runControlService,
    DeviceModeService deviceModeService,
    DeviceInitializationService deviceInitializationService)
{
    private static readonly string[] ActiveBatchStatuses =
    [
        RuntimeLedgerStatus.Pending,
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted
    ];

    public async Task<MockRuntimeState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var run = await machineRunQueryService.GetCurrentAsync(cancellationToken);
        var state = run is null ? store.GetState() : ToPageState(store.GetState(), run);
        state.DeviceMode = deviceModeService.CurrentMode;
        return await ApplyFormalStateAsync(state, cancellationToken);
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
            var state = createdRun is null ? store.RunAction("start") : ToPageState(store.GetState(), createdRun);
            state.DeviceMode = deviceModeService.CurrentMode;
            return await ApplyFormalStateAsync(state, cancellationToken);
        }

        _ = normalizedAction switch
        {
            "start" => await runControlService.StartAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-start")), actor, cancellationToken),
            "pause" => await runControlService.PauseAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-pause")), actor, cancellationToken),
            "resume" => await runControlService.ResumeAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-resume")), actor, cancellationToken),
            "stop" => await runControlService.StopAsync(run.Id, new RunCommandRequest(NewCommandId("ui-run-stop")), actor, cancellationToken),
            _ => throw new BusinessRuleException("run_action_unknown", $"未知运行操作：{action}", StatusCodes.Status400BadRequest)
        };

        var updated = await machineRunQueryService.GetAsync(run.Id, cancellationToken);
        var updatedState = updated is null ? store.GetState() : ToPageState(store.GetState(), updated);
        updatedState.DeviceMode = deviceModeService.CurrentMode;
        return await ApplyFormalStateAsync(updatedState, cancellationToken);
    }

    private async Task<MockRuntimeState> ApplyFormalStateAsync(MockRuntimeState state, CancellationToken cancellationToken)
    {
        state = await OverlayDatabaseChannelsAsync(state, cancellationToken);
        var initialization = await deviceInitializationService.GetLatestAsync(cancellationToken);
        state.Initialized = initialization.Ok;
        if (initialization.Ok && state.Status == "idle")
        {
            state.Status = state.Channels.SelectMany(x => x.Slides).Any() ? "ready" : "initialized";
        }

        state.System = ToSystemCheck(initialization);
        if (!string.IsNullOrWhiteSpace(initialization.Message))
        {
            state.Logs = new[] { $"[初始化/{TranslateStatus(initialization.Status)}] {initialization.Message}" }
                .Concat(state.Logs)
                .Take(80)
                .ToList();
        }

        return state;
    }

    private static MockSystemCheck ToSystemCheck(DeviceInitializationResponse initialization)
    {
        var checks = initialization.Checks
            .GroupBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var controller = checks.GetValueOrDefault("controller");
        var cooling = checks.GetValueOrDefault("cooling");
        var sampleScanner = checks.GetValueOrDefault("sample-scanner");
        var reagentScanner = checks.GetValueOrDefault("reagent-scanner");
        var robot = checks.GetValueOrDefault("robot-arm");
        var liquid = checks.GetValueOrDefault("liquid-level");
        var wash = checks.GetValueOrDefault("needle-wash");
        return new MockSystemCheck
        {
            ControllerOnline = IsSucceeded(controller),
            RoboticArmHome = IsSucceeded(robot) && ReadBool(robot, "homed", true),
            ReagentCooling = IsSucceeded(cooling),
            SampleScannerOnline = IsSucceeded(sampleScanner),
            ReagentScannerOnline = IsSucceeded(reagentScanner),
            ScannerOnline = IsSucceeded(sampleScanner) && IsSucceeded(reagentScanner),
            LiquidSensor = IsSucceeded(liquid),
            NeedleWash = IsSucceeded(wash),
            PureWaterOk = IsSucceeded(liquid) && ReadBool(liquid, "pureWaterOk", true),
            PbsOk = IsSucceeded(liquid) && ReadBool(liquid, "pbsOk", true),
            WasteTankFull = ReadBool(liquid, "wasteTankFull", false),
            ToxicTankFull = ReadBool(liquid, "toxicTankFull", false),
            ReagentTemperatureC = ReadDecimal(cooling, "currentTemperatureDeciC", 80m) / 10m
        };
    }

    private static bool IsSucceeded(DeviceInitializationCheckResponse? check) =>
        check?.Status == DeviceInitializationCheckStatus.Succeeded;

    private static bool ReadBool(DeviceInitializationCheckResponse? check, string key, bool fallback)
    {
        if (check is null || !check.Result.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        if (value is bool boolean) return boolean;
        if (value is System.Text.Json.JsonElement element && element.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static decimal ReadDecimal(DeviceInitializationCheckResponse? check, string key, decimal fallback)
    {
        if (check is null || !check.Result.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        if (value is decimal number) return number;
        if (value is System.Text.Json.JsonElement element && element.TryGetDecimal(out var parsedElement)) return parsedElement;
        return decimal.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private async Task<MockRuntimeState> OverlayDatabaseChannelsAsync(MockRuntimeState state, CancellationToken cancellationToken)
    {
        var drawers = await dbContext.Drawers
            .AsNoTracking()
            .Include(x => x.PhysicalSlots)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
        if (drawers.Count == 0)
        {
            return state;
        }

        var drawerIds = drawers.Select(x => x.Id).ToList();
        var batches = await dbContext.ChannelBatches
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.SelectedWorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .Include(x => x.SlideTasks)
            .ThenInclude(x => x.StainingTask)
            .Include(x => x.SlideTasks)
            .ThenInclude(x => x.PhysicalSlot)
            .Where(x => drawerIds.Contains(x.DrawerId) && ActiveBatchStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        if (batches.Count == 0)
        {
            return state;
        }

        var batchByDrawer = batches
            .GroupBy(x => x.DrawerId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(b => b.CreatedAtUtc).First());
        state.Channels = drawers.Select((drawer, index) =>
        {
            batchByDrawer.TryGetValue(drawer.Id, out var batch);
            return ToPageChannelFromDatabase(drawer, batch, index);
        }).ToList();
        return state;
    }

    private static MockChannel ToPageChannelFromDatabase(Drawer drawer, ChannelBatch? batch, int index)
    {
        if (batch is null)
        {
            return new MockChannel
            {
                Id = index + 1,
                Name = $"Channel{index + 1}",
                DrawerCode = drawer.Code,
                Status = "empty",
                CurrentStep = "无活动批次",
                WorkflowSelectionStatus = WorkflowSelectionStatus.Unselected,
                CanSelectWorkflow = true,
                CanChangeWorkflow = false
            };
        }

        var slides = batch.SlideTasks
            .OrderBy(x => x.PhysicalSlot?.SlotNo ?? ParseSlotNo(x.SlotCode))
            .Select(slide => ToPageSlideFromDatabase(batch, slide))
            .ToList();
        var locked = batch.WorkflowLockedAtUtc is not null
            || batch.StartedAtUtc is not null
            || !string.IsNullOrWhiteSpace(batch.MachineRunId)
            || batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Locked;
        var status = slides.Count == 0 && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Unselected
            ? "empty"
            : MapPageStatus(batch.Status);
        return new MockChannel
        {
            Id = index + 1,
            Name = $"Channel{index + 1}",
            DrawerCode = drawer.Code,
            ChannelBatchId = batch.Id,
            Status = status,
            Progress = slides.Count == 0 ? 0 : (int)Math.Round(slides.Average(x => x.Progress)),
            CurrentStep = locked ? "已锁定" : slides.Count == 0 ? "等待放入玻片" : "等待中",
            ExperimentType = batch.ExperimentType,
            WorkflowVersionId = batch.SelectedWorkflowVersionId,
            WorkflowCode = batch.SelectedWorkflowVersion?.WorkflowDefinition?.Code,
            WorkflowName = batch.SelectedWorkflowVersion?.WorkflowDefinition?.Name,
            WorkflowVersionLabel = batch.SelectedWorkflowVersion?.VersionLabel,
            WorkflowSelectionStatus = batch.WorkflowSelectionStatus,
            WorkflowLockedAtUtc = batch.WorkflowLockedAtUtc,
            WorkflowLocked = locked,
            CanSelectWorkflow = !locked
                && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Unselected
                && string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
                && slides.Count == 0,
            CanChangeWorkflow = !locked
                && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Selected
                && !string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId),
            Slides = slides
        };
    }

    private static MockSlide ToPageSlideFromDatabase(ChannelBatch batch, SlideTask slide)
    {
        var task = slide.StainingTask;
        var workflow = batch.SelectedWorkflowVersion;
        var workflowDefinition = workflow?.WorkflowDefinition;
        var sampleIdentifier = task?.RawSampleCode
            ?? task?.NormalizedSampleCode
            ?? task?.RawCode
            ?? task?.TaskCode
            ?? slide.Id[^Math.Min(8, slide.Id.Length)..];
        return new MockSlide
        {
            Id = slide.Id,
            StainingTaskId = task?.Id,
            Channel = Math.Clamp((batch.DrawerCode.FirstOrDefault() - 'A') + 1, 1, 4),
            Slot = ParseSlotNo(slide.SlotCode),
            Barcode = sampleIdentifier,
            SampleIdentifier = sampleIdentifier,
            ProtocolCode = workflowDefinition?.Code ?? batch.ExperimentType ?? slide.TaskType,
            WorkflowName = workflowDefinition?.Name,
            WorkflowVersionLabel = workflow?.VersionLabel,
            WorkflowVersionId = batch.SelectedWorkflowVersionId,
            AntibodyCode = task?.ConfirmedPrimaryAntibodyCode ?? task?.PrimaryAntibodyCode ?? string.Empty,
            Status = MapPageStatus(slide.Status),
            CurrentStep = MapSlideStep(slide.Status),
            Progress = slide.Status == RuntimeLedgerStatus.Completed ? 100 : 0
        };
    }

    private static MockRuntimeState ToPageState(MockRuntimeState fallback, MachineRunDetailResponse run)
    {
        var workflowsBySlide = run.WorkflowExecutions.ToDictionary(x => x.SlideTaskId, StringComparer.Ordinal);
        var channels = Enumerable.Range(0, 4)
            .Select(index => new MockChannel
            {
            Id = index + 1,
            Name = $"Channel{index + 1}",
            DrawerCode = ((char)('A' + index)).ToString(),
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
            Logs = run.Alarms.Select(x => $"[{TranslateStatus(x.Severity)}] {x.Code}: {x.Message}")
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
            SampleIdentifier = slide.Id[^Math.Min(8, slide.Id.Length)..],
            ProtocolCode = slide.TaskType,
            Status = MapPageStatus(slide.Status),
            CurrentStep = step?.StepName ?? step?.MajorStepCode ?? "等待中",
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

        return batch.Status == RuntimeLedgerStatus.Completed ? "已完成" : "等待中";
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

    private static string MapSlideStep(string status)
    {
        return status switch
        {
            RuntimeLedgerStatus.Pending => "等待中",
            RuntimeLedgerStatus.Running => "运行中",
            RuntimeLedgerStatus.Paused => "已暂停",
            RuntimeLedgerStatus.Completed => "已完成",
            RuntimeLedgerStatus.WaitingUnload => "等待卸载",
            RuntimeLedgerStatus.Faulted or RuntimeLedgerStatus.Failed => "故障",
            _ => status
        };
    }

    private static string TranslateStatus(string? status)
    {
        return status switch
        {
            "Active" => "活动中",
            "Acknowledged" => "已确认",
            "Resolved" => "已处理",
            "Warning" => "警告",
            "Error" => "错误",
            "Critical" => "严重",
            "Information" => "信息",
            "Succeeded" => "成功",
            "Failed" => "失败",
            "Running" => "运行中",
            "Pending" => "等待中",
            _ => status ?? "--"
        };
    }

    private static string NewCommandId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
