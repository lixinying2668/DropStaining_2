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
    string? ChangeNote);

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

public sealed record CreateIhcTaskRequest(
    string CommandId,
    string InputMode,
    string RawCode,
    string SlotCode,
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
