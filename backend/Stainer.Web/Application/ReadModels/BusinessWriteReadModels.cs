namespace Stainer.Web.Application.ReadModels;

public sealed record CommandResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Message);

public sealed record UserMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string UserId,
    string Username,
    string DisplayName,
    bool Enabled,
    IReadOnlyList<string> Roles,
    string Message);

public sealed record WorkflowDraftMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string WorkflowDefinitionId,
    string WorkflowVersionId,
    string Code,
    string Name,
    int VersionNo,
    string VersionLabel,
    string Status,
    string Message);

/// <summary>
/// 通道批次选择/变更工作流响应。PrimaryAntibodyCode 为所选染色流程由一抗孵育步骤
/// 解析出的一抗编码；未选或解析失败为 null（仅展示，不抛错）。
/// </summary>
public sealed record ChannelBatchWorkflowResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string ChannelBatchId,
    string DrawerCode,
    string ExperimentType,
    string WorkflowVersionId,
    string WorkflowSelectionStatus,
    DateTimeOffset? WorkflowSelectedAtUtc,
    string Message,
    string? WorkflowName = null,
    string? WorkflowVersionLabel = null,
    string? PrimaryAntibodyCode = null);

public sealed record DefaultWorkflowVersionResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string ExperimentType,
    string WorkflowDefinitionId,
    string WorkflowVersionId,
    string WorkflowCode,
    string WorkflowName,
    string WorkflowVersionLabel,
    string Message);

/// <summary>
/// 通道批次激活/查询响应。SelectedWorkflow* 与 PrimaryAntibodyCode 仅在通道已选流程时回填；
/// 未选或流程一抗解析失败时为 null（仅展示，不抛错）。
/// </summary>
public sealed record ChannelBatchActivationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string ChannelBatchId,
    string DrawerCode,
    string Status,
    string WorkflowSelectionStatus,
    string Message,
    string? ExperimentType = null,
    IReadOnlyList<string>? OccupiedSlotCodes = null,
    string? SelectedWorkflowVersionId = null,
    string? SelectedWorkflowName = null,
    string? SelectedWorkflowVersionLabel = null,
    string? PrimaryAntibodyCode = null);

public sealed record TaskWorkflowCandidateResponse(
    string WorkflowVersionId,
    string WorkflowDefinitionId,
    string WorkflowCode,
    string WorkflowName,
    string VersionLabel);

public sealed record TaskCreationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    bool RequiresSelection,
    string Message,
    string? TaskId,
    string? TaskCode,
    IReadOnlyList<string> CandidatePrimaryAntibodyCodes,
    IReadOnlyList<TaskWorkflowCandidateResponse> CandidateWorkflows,
    string? ChannelBatchId = null,
    string? DrawerCode = null,
    string? ExperimentType = null,
    string? WorkflowVersionId = null,
    string? WorkflowSelectionStatus = null,
    string? CompatibilityValidationStatus = null,
    string? CompatibilityValidationMessage = null);

public sealed record SampleScanItemResponse(
    string Id,
    string? SlotCode,
    string ScanKind,
    string ScanStatus,
    string? RawCode,
    string? NormalizedCode,
    string? PrimaryAntibodyCode,
    string? ErrorReason,
    string DeviceStatus,
    DateTimeOffset ScannedAtUtc);

public sealed record SampleScanSessionResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string SessionId,
    string SessionCode,
    string Status,
    int ValidCount,
    int EmptyCount,
    int InvalidCount,
    int TimedOutCount,
    int DeviceDisconnectedCount,
    IReadOnlyList<SampleScanItemResponse> Items,
    string Message);

public sealed record MockLisQueryResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string LisQueryLogId,
    string Status,
    string RawCode,
    string NormalizedCode,
    IReadOnlyList<string> CandidatePrimaryAntibodyCodes,
    string? ErrorCode,
    string? ErrorMessage,
    string Message);

public sealed record MockDemoDataResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    int CreatedCount,
    int UpdatedCount,
    int DeletedCount,
    int SkippedCount,
    string Message);

public sealed record MockReagentScanResponse(
    bool Ok,
    string CommandId,
    string SessionId,
    int PositionCount,
    IReadOnlyList<ReagentScanConfirmationResponse> Results,
    ReagentScanSessionSummaryResponse Session,
    string Message);

public sealed record ReagentScanConfirmationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string SessionId,
    int EmptyCount,
    int ValidCount,
    int InvalidCount,
    string Message,
    string? Position = null,
    string? ScanResult = null,
    string? ValidationMessage = null);

public sealed record ReagentScanSessionMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    ReagentScanSessionSummaryResponse Session,
    string Message);

public sealed record EngineeringWriteResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string EntityId,
    string Message);

public sealed record PreflightValidationReportResponse(
    bool Ok,
    int TaskCount,
    int IssueCount,
    IReadOnlyList<PreflightValidationIssueResponse> Issues,
    DateTimeOffset? GeneratedAtUtc = null,
    string? ReportId = null,
    bool CanStart = false,
    int WarningCount = 0,
    string? StateHash = null,
    IReadOnlyList<PrecheckCheckResponse>? Checks = null);

public sealed record PreflightValidationIssueResponse(
    string Area,
    string Code,
    string Message,
    string Severity = "Fail");
