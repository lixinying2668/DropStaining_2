using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class OperatorSnapshotQueryService(
    StainerDbContext dbContext,
    DeviceModeService deviceModeService,
    DeviceInitializationService deviceInitializationService,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService)
{
    private static readonly string[] TerminalBatchStatuses =
    [
        RuntimeLedgerStatus.Completed,
        RuntimeLedgerStatus.Stopped
    ];

    public async Task<OperatorSnapshotResponse> GetAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.MachineRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var run = runs.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();

        var initialization = await deviceInitializationService.GetLatestAsync(cancellationToken);
        var thermal = await thermalControlService.GetStateAsync(false, cancellationToken);
        var fluidics = await fluidicsControlService.GetStateAsync(cancellationToken);

        var drawers = await dbContext.Drawers
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
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
            .Where(x => drawerIds.Contains(x.DrawerId)
                && ((run != null && x.MachineRunId == run.Id)
                    || (x.MachineRunId == null && !TerminalBatchStatuses.Contains(x.Status))))
            .ToListAsync(cancellationToken);

        var executions = run is null
            ? []
            : await dbContext.WorkflowExecutions
                .AsNoTracking()
                .Include(x => x.StepExecutions)
                .Where(x => x.MachineRunId == run.Id)
                .ToListAsync(cancellationToken);
        var executionBySlide = executions
            .GroupBy(x => x.SlideTaskId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(e => e.CreatedAtUtc).First());

        var channels = drawers.Select((drawer, index) =>
        {
            var batch = batches
                .Where(x => x.DrawerId == drawer.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();
            return ToChannel(index + 1, drawer.Code, batch, executionBySlide);
        }).ToList();

        var alarmRows = await dbContext.Alarms
            .AsNoTracking()
            .Where(x => x.Status != "Resolved"
                && (run == null || x.MachineRunId == null || x.MachineRunId == run.Id))
            .ToListAsync(cancellationToken);
        var alarms = alarmRows.OrderByDescending(x => x.CreatedAtUtc).Take(30).ToList();

        var commandRows = run is null
            ? []
            : await dbContext.DeviceCommandExecutions
                .AsNoTracking()
                .Where(x => x.MachineRunId == run.Id)
                .ToListAsync(cancellationToken);
        var commands = commandRows
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(30)
            .Select(x => new OperatorDeviceCommandResponse(
                x.Id,
                x.CommandType,
                x.Status,
                x.WorkflowStepExecutionId,
                x.CreatedAtUtc,
                x.CommandSentAtUtc,
                x.AcknowledgedAtUtc,
                x.CompletedAtUtc))
            .ToList();

        var needleRows = await dbContext.NeedleStates
            .AsNoTracking()
            .OrderBy(x => x.NeedleNo)
            .ToListAsync(cancellationToken);
        var needles = NeedleCodes.All.Select(code =>
        {
            var state = needleRows.FirstOrDefault(x => x.NeedleCode == code);
            return state is null
                ? new OperatorNeedleResponse(
                    code,
                    MotionStatuses.Unknown,
                    false,
                    NeedleLoadSourceTypes.Empty,
                    null,
                    null,
                    null,
                    0,
                    false,
                    null,
                    "needle_state_unavailable",
                    "No formal needle state has been recorded.",
                    DateTimeOffset.MinValue)
                : new OperatorNeedleResponse(
                    state.NeedleCode,
                    state.Status,
                    state.IsConnected,
                    state.LoadedSourceType,
                    state.LoadedReagentCode,
                    state.SourcePositionCode,
                    state.DabBatchId,
                    state.VolumeUl,
                    state.NeedsWash,
                    state.CurrentCommandId,
                    state.LastErrorCode,
                    state.LastErrorMessage,
                    state.UpdatedAtUtc);
        }).ToList();

        var resourceLeaseRows = run is null
            ? []
            : await dbContext.MachineResourceLeases
                .AsNoTracking()
                .Where(x => x.MachineRunId == run.Id)
                .ToListAsync(cancellationToken);
        var resourceLeases = resourceLeaseRows
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(40)
            .Select(x => new OperatorResourceLeaseResponse(
                x.Id,
                x.ResourceCode,
                x.ResourceType,
                x.Status,
                x.MachineRunId,
                x.WorkflowStepExecutionId,
                x.DeviceCommandExecutionId,
                x.WaitReason,
                x.CreatedAtUtc,
                x.AcquiredAtUtc,
                x.ReleasedAtUtc))
            .ToList();

        var dabPositions = await GetDabPositionsAsync(cancellationToken);
        var recentEvents = await GetRecentEventsAsync(commands, cancellationToken);
        var currentStep = run?.CurrentMajorStepCode
            ?? executions.Select(CurrentStep).FirstOrDefault(x => x is not null)?.MajorStepCode;
        var slideCount = channels.Sum(x => x.Slides.Count);
        var status = run is null
            ? initialization.Ok ? (slideCount > 0 ? "ready" : "initialized") : "idle"
            : MapPageStatus(run.Status);

        var alarmDetails = alarms
            .Select(x => new AlarmResponse(
                x.Id,
                OperatorAlarmPresentation.Category(x.Code),
                x.Severity,
                OperatorAlarmPresentation.Summary(x.Code, x.Severity),
                x.Status))
            .ToList();
        var logs = recentEvents
            .Select(x => $"[{x.OccurredAtUtc.ToLocalTime():HH:mm:ss}] {x.Title} {x.Detail}")
            .ToList();

        return new OperatorSnapshotResponse(
            status,
            initialization.Ok,
            deviceModeService.CurrentMode,
            run?.Id,
            run?.RunCode,
            currentStep,
            run?.PauseRequested ?? false,
            run?.StopRequested ?? false,
            new OperatorUserResponse(actor.UserId, actor.Username, actor.DisplayName, actor.ActiveRole, actor.Roles),
            ToSystem(initialization, thermal, fluidics),
            channels,
            alarms.Select(x => OperatorAlarmPresentation.Summary(x.Code, x.Severity)).ToList(),
            alarmDetails,
            logs,
            recentEvents,
            initialization,
            thermal,
            fluidics,
            needles,
            resourceLeases,
            commands,
            dabPositions);
    }

    private async Task<IReadOnlyList<OperatorDabPositionResponse>> GetDabPositionsAsync(
        CancellationToken cancellationToken)
    {
        var positions = await dbContext.DabMixPositions
            .AsNoTracking()
            .OrderBy(x => x.PositionNo)
            .ToListAsync(cancellationToken);
        var batchIds = positions
            .Where(x => x.ActiveDabBatchId is not null)
            .Select(x => x.ActiveDabBatchId!)
            .ToList();
        var batches = await dbContext.DabBatches
            .AsNoTracking()
            .Include(x => x.DabAReagentBottle)
            .Include(x => x.DabBReagentBottle)
            .Where(x => batchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return positions.Select(position =>
        {
            batches.TryGetValue(position.ActiveDabBatchId ?? string.Empty, out var batch);
            return new OperatorDabPositionResponse(
                position.Id,
                position.Code,
                position.PositionNo,
                position.IsEnabled,
                position.Status,
                position.ActiveDabBatchId,
                batch?.Status,
                batch?.CleaningStatus,
                batch?.DabAReagentBottle?.FullBarcode,
                batch?.DabBReagentBottle?.FullBarcode,
                batch?.SlideCount,
                batch?.RemainingVolumeUl,
                batch?.PreparedAtUtc,
                batch?.ExpiresAtUtc,
                position.UpdatedAtUtc);
        }).ToList();
    }

    private async Task<IReadOnlyList<OperatorEventResponse>> GetRecentEventsAsync(
        IReadOnlyList<OperatorDeviceCommandResponse> commands,
        CancellationToken cancellationToken)
    {
        var auditRows = await dbContext.AuditLogs
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var audit = auditRows
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(16)
            .Select(x =>
            {
                var summary = OperatorAlarmPresentation.AuditSummary(x.Action);
                return new OperatorEventResponse(
                    x.Id,
                    "Audit",
                    summary.Title,
                    summary.Detail,
                    "Recorded",
                    x.CreatedAtUtc,
                    "/history");
            })
            .ToList();
        var commandEvents = commands.Select(x => new OperatorEventResponse(
            x.Id,
            "DeviceCommand",
            x.CommandType,
            $"命令阶段：{x.Status}",
            x.Status,
            x.CompletedAtUtc ?? x.AcknowledgedAtUtc ?? x.CommandSentAtUtc ?? x.CreatedAtUtc,
            "/run"));

        return audit
            .Concat(commandEvents)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(16)
            .ToList();
    }

    private static OperatorChannelResponse ToChannel(
        int index,
        string drawerCode,
        ChannelBatch? batch,
        IReadOnlyDictionary<string, WorkflowExecution> executionBySlide)
    {
        if (batch is null)
        {
            return new OperatorChannelResponse(
                index,
                drawerCode,
                null,
                "empty",
                0,
                "No active batch",
                null,
                null,
                null,
                null,
                null,
                WorkflowSelectionStatus.Unselected,
                false,
                true,
                false,
                []);
        }

        var locked = batch.WorkflowLockedAtUtc is not null
            || batch.StartedAtUtc is not null
            || !string.IsNullOrWhiteSpace(batch.MachineRunId)
            || batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Locked;
        var slides = batch.SlideTasks
            .OrderBy(x => x.PhysicalSlot?.SlotNo ?? ParseSlotNo(x.SlotCode))
            .Select(slide => ToSlide(batch, slide, executionBySlide.GetValueOrDefault(slide.Id)))
            .ToList();
        var progress = slides.Count == 0 ? 0 : (int)Math.Round(slides.Average(x => x.Progress));
        var currentStep = slides
            .FirstOrDefault(x => x.Status is "running" or "error" or "unknown")?.CurrentStep
            ?? (locked ? "Locked" : slides.Count == 0 ? "Waiting for slides" : "Waiting");

        return new OperatorChannelResponse(
            index,
            drawerCode,
            batch.Id,
            slides.Count == 0 && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Unselected
                ? "empty"
                : MapPageStatus(batch.Status),
            progress,
            currentStep,
            batch.ExperimentType,
            batch.SelectedWorkflowVersionId,
            batch.SelectedWorkflowVersion?.WorkflowDefinition?.Code,
            batch.SelectedWorkflowVersion?.WorkflowDefinition?.Name,
            batch.SelectedWorkflowVersion?.VersionLabel,
            batch.WorkflowSelectionStatus,
            locked,
            !locked
                && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Unselected
                && string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
                && slides.Count == 0,
            !locked
                && batch.WorkflowSelectionStatus == WorkflowSelectionStatus.Selected
                && !string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId),
            slides);
    }

    private static OperatorSlideResponse ToSlide(
        ChannelBatch batch,
        SlideTask slide,
        WorkflowExecution? execution)
    {
        var task = slide.StainingTask;
        var workflow = batch.SelectedWorkflowVersion;
        var step = CurrentStep(execution);
        var sampleIdentifier = task?.RawSampleCode
            ?? task?.NormalizedSampleCode
            ?? task?.RawCode
            ?? task?.TaskCode
            ?? slide.Id[^Math.Min(8, slide.Id.Length)..];
        var progress = execution?.StepExecutions.Count > 0
            ? Math.Clamp((int)Math.Round(execution.StepExecutions.Count(x => x.Status == RuntimeLedgerStatus.Completed)
                * 100.0 / execution.StepExecutions.Count), 0, 100)
            : slide.Status == RuntimeLedgerStatus.Completed ? 100 : 0;

        return new OperatorSlideResponse(
            slide.Id,
            task?.Id,
            ParseSlotNo(slide.SlotCode),
            slide.SlotCode,
            sampleIdentifier,
            sampleIdentifier,
            workflow?.WorkflowDefinition?.Code ?? batch.ExperimentType ?? slide.TaskType,
            workflow?.WorkflowDefinition?.Name,
            workflow?.VersionLabel,
            batch.SelectedWorkflowVersionId,
            task?.ConfirmedPrimaryAntibodyCode ?? task?.PrimaryAntibodyCode,
            task?.ConfirmedPrimaryAntibodyCode,
            task?.InputMode,
            task?.CompatibilityValidationStatus,
            task?.CompatibilityValidationMessage,
            MapPageStatus(slide.Status),
            step?.StepName ?? step?.MajorStepCode ?? MapSlideStep(slide.Status),
            progress);
    }

    private static WorkflowStepExecution? CurrentStep(WorkflowExecution? execution) =>
        execution?.StepExecutions
            .OrderBy(x => x.StepNo)
            .FirstOrDefault(x => x.Status is RuntimeLedgerStatus.Running or RuntimeLedgerStatus.Failed or RuntimeLedgerStatus.Unknown)
        ?? execution?.StepExecutions.OrderBy(x => x.StepNo).FirstOrDefault(x => x.Status == RuntimeLedgerStatus.Pending)
        ?? execution?.StepExecutions.OrderBy(x => x.StepNo).LastOrDefault();

    private static OperatorSystemResponse ToSystem(
        DeviceInitializationResponse initialization,
        ThermalStateResponse thermal,
        FluidicsStateResponse fluidics)
    {
        var checks = initialization.Checks
            .GroupBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var liquid = checks.GetValueOrDefault("liquid-level");
        var water = fluidics.LiquidLevels.FirstOrDefault(x => x.SourceType.Contains("Water", StringComparison.OrdinalIgnoreCase));
        var pbs = fluidics.LiquidLevels.FirstOrDefault(x => x.SourceType.Contains("PBS", StringComparison.OrdinalIgnoreCase));
        var waste = fluidics.LiquidLevels.FirstOrDefault(x => x.IsWaste && x.SourceType.Contains("Waste", StringComparison.OrdinalIgnoreCase));
        var toxic = fluidics.LiquidLevels.FirstOrDefault(x => x.IsWaste && !x.SourceType.Contains("Waste", StringComparison.OrdinalIgnoreCase));

        return new OperatorSystemResponse(
            IsSucceeded(checks.GetValueOrDefault("controller")),
            IsSucceeded(checks.GetValueOrDefault("robot-arm")) && ReadBool(checks.GetValueOrDefault("robot-arm"), "homed", true),
            IsSucceeded(checks.GetValueOrDefault("cooling")) && thermal.Cooling.IsConnected,
            IsSucceeded(checks.GetValueOrDefault("sample-scanner")),
            IsSucceeded(checks.GetValueOrDefault("reagent-scanner")),
            IsSucceeded(liquid) && fluidics.LiquidLevels.All(x => x.IsConnected),
            IsSucceeded(checks.GetValueOrDefault("needle-wash")),
            IsUsableSupply(water) && ReadBool(liquid, "pureWaterOk", true),
            IsUsableSupply(pbs) && ReadBool(liquid, "pbsOk", true),
            IsFullWaste(waste) || ReadBool(liquid, "wasteTankFull", false),
            IsFullWaste(toxic) || ReadBool(liquid, "toxicTankFull", false),
            thermal.Cooling.CurrentTemperatureDeciC / 10m);
    }

    private static bool IsSucceeded(DeviceInitializationCheckResponse? check) =>
        check?.Status == DeviceInitializationCheckStatus.Succeeded;

    private static bool IsUsableSupply(LiquidContainerResponse? container) =>
        container is null || (container.IsConnected && container.LevelStatus is not "Depleted" and not "Faulted" and not "Unknown");

    private static bool IsFullWaste(LiquidContainerResponse? container) =>
        container is not null && (!container.IsConnected || container.LevelStatus is "Full" or "Faulted" or "Unknown");

    private static bool ReadBool(DeviceInitializationCheckResponse? check, string key, bool fallback)
    {
        if (check is null || !check.Result.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        if (value is bool boolean) return boolean;
        if (value is JsonElement element && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static string MapPageStatus(string status) => status switch
    {
        RuntimeLedgerStatus.Created or RuntimeLedgerStatus.Pending => "configured",
        RuntimeLedgerStatus.Running => "running",
        RuntimeLedgerStatus.Paused => "paused",
        RuntimeLedgerStatus.Completed => "completed",
        RuntimeLedgerStatus.Stopped => "stopped",
        RuntimeLedgerStatus.Failed or RuntimeLedgerStatus.Faulted => "error",
        RuntimeLedgerStatus.Unknown => "unknown",
        RuntimeLedgerStatus.WaitingUnload => "waiting",
        _ => status.ToLowerInvariant()
    };

    private static string MapSlideStep(string status) => status switch
    {
        RuntimeLedgerStatus.Completed => "Completed",
        RuntimeLedgerStatus.Running => "Running",
        RuntimeLedgerStatus.Paused => "Paused",
        RuntimeLedgerStatus.Unknown => "Unknown - manual resolution required",
        RuntimeLedgerStatus.Failed or RuntimeLedgerStatus.Faulted => "Faulted",
        _ => "Waiting"
    };

    private static int ParseSlotNo(string slotCode)
    {
        var separator = slotCode.LastIndexOf('-');
        return separator >= 0 && int.TryParse(slotCode[(separator + 1)..], out var parsed) ? parsed : 0;
    }
}
