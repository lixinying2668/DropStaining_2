namespace Stainer.Web.Application.ReadModels;

public sealed record TraceabilityListResponse<T>(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<T> Items);

public sealed record HistoryRunSummaryResponse(
    string MachineRunId,
    string RunCode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? RequestedBy,
    int ChannelBatchCount,
    int SlideTaskCount,
    int AlarmCount,
    string Channels,
    string WorkflowNames);

public sealed record HistoryRunDetailResponse(
    string MachineRunId,
    string RunCode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? RequestedBy,
    IReadOnlyList<HistoryChannelBatchResponse> ChannelBatches,
    IReadOnlyList<HistoryWorkflowExecutionResponse> WorkflowExecutions,
    IReadOnlyList<HistoryDeviceCommandResponse> DeviceCommands,
    IReadOnlyList<HistoryReagentConsumptionResponse> ReagentConsumptions,
    IReadOnlyList<HistoryDabUsageResponse> DabUsages,
    IReadOnlyList<TraceAlarmResponse> Alarms);

public sealed record HistoryChannelBatchResponse(
    string ChannelBatchId,
    string DrawerCode,
    string Status,
    string? ExperimentType,
    string? WorkflowVersionId,
    string? WorkflowCode,
    string? WorkflowName,
    string? WorkflowVersionLabel,
    string WorkflowSelectionStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string WorkflowSnapshotJson,
    IReadOnlyList<HistorySlideTaskResponse> Slides);

public sealed record HistorySlideTaskResponse(
    string SlideTaskId,
    string StainingTaskId,
    string TaskCode,
    string SlotCode,
    string TaskType,
    string Status,
    string? RawSampleCode,
    string? NormalizedSampleCode,
    string? PrimaryAntibodyCode,
    string? CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string WorkflowSnapshotJson);

public sealed record HistoryWorkflowExecutionResponse(
    string WorkflowExecutionId,
    string SlideTaskId,
    string WorkflowVersionId,
    string? WorkflowCode,
    string? WorkflowName,
    string? WorkflowVersionLabel,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<HistoryWorkflowStepExecutionResponse> Steps);

public sealed record HistoryWorkflowStepExecutionResponse(
    string WorkflowStepExecutionId,
    int StepNo,
    string MajorStepCode,
    string StepName,
    string ActionType,
    string? ReagentCode,
    int? VolumeUl,
    string Status,
    int RedoCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record HistoryDeviceCommandResponse(
    string DeviceCommandExecutionId,
    string MachineRunId,
    string? WorkflowStepExecutionId,
    string CommandType,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CommandSentAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record HistoryReagentConsumptionResponse(
    string ReagentConsumptionId,
    string MachineRunId,
    string WorkflowStepExecutionId,
    string ReagentBottleId,
    string ReagentCode,
    string? ReagentName,
    string? ProductionBatchNo,
    string? SerialNo,
    string? FullBarcode,
    int VolumeUl,
    DateTimeOffset CreatedAtUtc);

public sealed record HistoryDabUsageResponse(
    string DabBatchUsageId,
    string DabBatchId,
    string? MachineRunId,
    string? WorkflowStepExecutionId,
    string PositionCode,
    int VolumeUl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PreparedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record TraceAlarmResponse(
    string AlarmId,
    string? MachineRunId,
    string Code,
    string Severity,
    string Status,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClearedAtUtc,
    string? SourceChannels,
    string? AckBy,
    DateTimeOffset? AckAtUtc,
    string? AckReason,
    IReadOnlyList<TraceAlarmActionResponse> Actions);

public sealed record TraceAlarmActionResponse(
    string AlarmActionId,
    string Action,
    string Message,
    string? Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record AlarmMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string AlarmId,
    string Status,
    string Message);

public sealed record AuditLogResponse(
    string AuditLogId,
    string? ActorUserId,
    string? Actor,
    string Action,
    string EntityType,
    string? EntityId,
    string Message,
    string Summary,
    string? Reason,
    string? CommandId,
    string? CorrelationId,
    string? MachineRunId,
    string? TaskId,
    string? Channel,
    string? Slot,
    DateTimeOffset CreatedAtUtc);

public sealed record ExportDescriptorResponse(
    string FileName,
    string ContentType,
    int RowCount);
