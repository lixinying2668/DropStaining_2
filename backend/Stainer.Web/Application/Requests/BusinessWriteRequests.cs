using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stainer.Web.Application.Requests;

public sealed record CreateUserRequest(
    string CommandId,
    string Username,
    string DisplayName,
    string Password,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserDisplayNameRequest(
    string CommandId,
    string DisplayName);

public sealed record SetUserEnabledRequest(
    string CommandId,
    bool Enabled);

public sealed record ResetUserPasswordRequest(
    string CommandId,
    string NewPassword);

public sealed record SetUserRolesRequest(
    string CommandId,
    IReadOnlyList<string> Roles);

public sealed record CreateWorkflowDraftRequest(
    string CommandId,
    string? SourceWorkflowId,
    string? Code,
    string? Name,
    string? WorkflowType,
    string? Description,
    string? VersionLabel,
    string? ChangeNote);

public sealed record CreateWorkflowRequest(
    string CommandId,
    string Code,
    string Name,
    string WorkflowType,
    string? Description,
    string? VersionLabel,
    string? ChangeNote);

public sealed record CreateWorkflowVersionRequest(
    string CommandId,
    string? VersionLabel,
    string? ChangeNote);

public sealed record CopyWorkflowVersionDraftRequest(
    string CommandId,
    string? VersionLabel,
    string? ChangeNote);

public sealed record UpdateWorkflowVersionRequest(
    string CommandId,
    string? Name,
    string? Description,
    bool? IsEnabled,
    string? VersionLabel,
    string? ChangeNote,
    string? PlanningRulesJson = null);

public sealed record SaveWorkflowStepRequest(
    string CommandId,
    int? StepNo,
    string? MajorStepCode,
    string StepName,
    string ActionType,
    string? ReagentCode,
    int? VolumeUl,
    int? DurationSeconds,
    int? TargetTemperatureDeciC,
    string? MixParametersJson,
    string? WashParametersJson,
    string? LegacyParametersJson,
    string? FailureStrategy);

public sealed record SaveWorkflowReagentRequirementRequest(
    string CommandId,
    string ReagentCode,
    int? RequiredVolumeUl,
    bool IsRequired);

public sealed record PublishWorkflowVersionRequest(string CommandId);

public sealed record RetireWorkflowVersionRequest(string CommandId, string Reason);

public sealed record SetDefaultWorkflowVersionRequest(string CommandId, string ExperimentType);

public sealed record CreatePrimaryAntibodyMappingRequest(
    string CommandId,
    string PrimaryAntibodyCode,
    string WorkflowVersionId);

public sealed record ChangePrimaryAntibodyMappingStateRequest(
    string CommandId,
    string? Reason);

public sealed record AcknowledgeAlarmRequest(
    string CommandId,
    string? Reason);

public sealed record SelectChannelWorkflowRequest(
    string CommandId,
    string? ChannelBatchId,
    string? DrawerCode,
    string ExperimentType,
    string WorkflowVersionId);

public sealed record SelectChannelExperimentTypeRequest(
    string CommandId,
    string? ChannelBatchId,
    string? DrawerCode,
    string ExperimentType,
    string? Reason)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record EnsureChannelBatchRequest(
    string CommandId,
    string DrawerCode);

public sealed record CreateHeTaskRequest(
    string CommandId,
    string SlotCode,
    string? DrawerCode = null,
    string? ChannelBatchId = null,
    string? WorkflowVersionId = null);

/// <summary>
/// 创建 IHC 任务。一抗由通道所选已发布染色流程的「一抗孵育」步骤决定，
/// 客户端不再提交一抗；加玻片只需提供槽位与通道信息。
/// </summary>
/// <remarks>
/// 下列字段仅为兼容旧接口保留，不再参与一抗选择或流程匹配：
/// <list type="bullet">
/// <item><c>InputMode</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// <item><c>RawCode</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// <item><c>SelectedPrimaryAntibodyCode</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// <item><c>SelectedWorkflowVersionId</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// <item><c>WorkflowVersionId</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// <item><c>LisQueryLogId</c> — 已弃用：一抗由流程决定，本字段忽略其选抗作用。</item>
/// </list>
/// 以上字段全部可选；新前端可不发送。后端会以通道已选的已发布 IHC WorkflowVersion 为准。
/// </remarks>
public sealed record CreateIhcTaskRequest(
    string CommandId,
    string? InputMode = null,
    string? RawCode = null,
    string SlotCode = "",
    string? DrawerCode = null,
    string? ChannelBatchId = null,
    string? SelectedPrimaryAntibodyCode = null,
    string? SelectedWorkflowVersionId = null,
    string? WorkflowVersionId = null,
    string? LisQueryLogId = null);

public sealed record MockSampleScanRequest(
    string CommandId,
    int Count = 1,
    string? Scenario = null,
    string? RawCode = null,
    string? SlotCode = null);

public sealed record MockReagentScanRequest(
    string CommandId,
    string? Scope = null,
    string? Position = null,
    int? ColumnNo = null,
    string? Scenario = null,
    IReadOnlyDictionary<string, string?>? BarcodesByPosition = null,
    string? ScanSessionId = null);

public sealed record MockLisQueryRequest(
    string CommandId,
    string RawCode);

public sealed record ResetMockDemoDataRequest(
    string CommandId,
    string Confirmation);

public sealed record ReagentScanInputItem(
    string Position,
    string ScanResult,
    string? RawBarcode,
    string? LocatorCode,
    DateOnly? ExpirationDate,
    string? ErrorReason = null);

public sealed record ConfirmReagentScanRequest(
    string CommandId,
    IReadOnlyList<ReagentScanInputItem> Items,
    string? ScanSessionId = null);

public sealed record StartReagentScanSessionRequest(
    string CommandId);

public sealed record CompleteReagentScanSessionRequest(
    string CommandId);

public sealed record CalibrateCoordinatePointRequest(
    string CommandId,
    string ProfileCode,
    string PointCode,
    long? CalibratedXUm,
    long? CalibratedYUm,
    long? CalibratedZUm,
    long? SafeZUm,
    long? AspirateZUm,
    long? AspirateEndZUm,
    long? DispenseZUm,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record CoordinateTargetPointInput(
    string PointCode,
    string? PointType,
    long? XUm,
    long? YUm,
    long? ZUm,
    long? SafeZUm,
    long? LiquidDetectZUm,
    long? AspirateEndZUm,
    long? DispenseZUm,
    long? ActionOffsetXUm,
    long? ActionOffsetYUm,
    long? ActionOffsetZUm,
    bool? IsEnabled);

public sealed record CreateCoordinateProfileVersionRequest(
    string CommandId,
    string ProfileCode,
    string? SourceVersionId,
    string VersionLabel,
    string Reason,
    IReadOnlyList<CoordinateTargetPointInput> TargetPoints,
    string? ValidationResultJson = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record PublishCoordinateProfileVersionRequest(
    string CommandId,
    string Reason,
    string ValidationResultJson,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record ActivateCoordinateProfileVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record SaveLiquidClassRequest(
    string CommandId,
    string Code,
    string Name,
    int? AspirateSpeedUlPerSecond,
    int? DispenseSpeedUlPerSecond,
    int? LeadingAirGapUl,
    int? TrailingAirGapUl,
    int? ExcessVolumeUl,
    int? PreWetCycles,
    int? MixCycles,
    bool IsEnabled,
    string Reason,
    string? SourceVersionId = null,
    string? VersionLabel = null,
    bool? LiquidDetectionEnabled = null,
    int? LiquidDetectionSensitivityPercent = null,
    int? LiquidDetectionSpeedUmPerSecond = null,
    int? AspirateDelayMs = null,
    int? DispenseDelayMs = null,
    int? BlowoutVolumeUl = null,
    int? BlowoutDelayMs = null,
    int? VolumeAdjustmentUl = null,
    int? LiquidFollowingDepthUm = null,
    int? RetractSpeedUmPerSecond = null,
    int? ConditioningVolumeUl = null,
    int? BreakoffSpeedUlPerSecond = null,
    int? PostDispenseAirGapUl = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record PublishLiquidClassVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record EnableLiquidClassVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record SaveDeviceProfileRequest(
    string CommandId,
    string Code,
    string Name,
    bool IsActive,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);
