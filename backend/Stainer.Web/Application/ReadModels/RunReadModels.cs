namespace Stainer.Web.Application.ReadModels;

public sealed record MachineRunResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string RunId,
    string RunCode,
    string Status,
    string Message);

public sealed record RunCommandResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string RunId,
    string Status,
    string Message);

public sealed record MachineRunDetailResponse(
    string Id,
    string RunCode,
    string Status,
    string? CurrentMajorStepCode,
    IReadOnlyList<ChannelBatchResponse> ChannelBatches,
    IReadOnlyList<WorkflowExecutionResponse> WorkflowExecutions,
    IReadOnlyList<AlarmResponse> Alarms);

public sealed record ChannelBatchResponse(
    string Id,
    string DrawerCode,
    string Status,
    IReadOnlyList<SlideTaskResponse> Slides);

public sealed record SlideTaskResponse(
    string Id,
    string SlotCode,
    string TaskType,
    string Status);

public sealed record WorkflowExecutionResponse(
    string Id,
    string SlideTaskId,
    string Status,
    IReadOnlyList<WorkflowStepExecutionResponse> Steps);

public sealed record WorkflowStepExecutionResponse(
    string Id,
    int StepNo,
    string MajorStepCode,
    string StepName,
    string ActionType,
    string? ReagentCode,
    int? VolumeUl,
    string Status,
    int RedoCount);

public sealed record AlarmResponse(
    string Id,
    string Code,
    string Severity,
    string Message,
    string Status);
