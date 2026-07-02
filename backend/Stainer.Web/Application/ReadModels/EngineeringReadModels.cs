namespace Stainer.Web.Application.ReadModels;

public sealed record EngineeringLayoutResponse(
    IReadOnlyList<DrawerLayoutResponse> Drawers,
    IReadOnlyList<ReagentRackLayoutResponse> ReagentRackPositions,
    IReadOnlyList<NamedPositionResponse> DabMixPositions,
    IReadOnlyList<NamedPositionResponse> WashPositions);

public sealed record DrawerLayoutResponse(
    string Id,
    string Code,
    string Name,
    int SortOrder,
    int HeatBoardId,
    bool IsEnabled,
    IReadOnlyList<PhysicalSlotLayoutResponse> Slots);

public sealed record PhysicalSlotLayoutResponse(
    string Id,
    string Code,
    int SlotNo,
    int VerticalOrderFromBottom,
    int HeatPointId,
    bool IsEnabled);

public sealed record ReagentRackLayoutResponse(
    string Id,
    string Code,
    int PositionNo,
    int ColumnNo,
    int RowNo,
    int ScannerChannelNo,
    string ScannerChannelCode,
    bool IsEnabled);

public sealed record NamedPositionResponse(
    string Id,
    string Code,
    int? PositionNo,
    string? Type,
    bool IsEnabled);

public sealed record CoordinateProfileResponse(
    string Id,
    string Code,
    string Name,
    string Status,
    string OriginDefinition,
    bool IsActive,
    string? ActiveVersionId,
    IReadOnlyList<CoordinateProfileVersionResponse> Versions,
    IReadOnlyList<CoordinatePointResponse> Points);

public sealed record CoordinateProfileVersionResponse(
    string Id,
    string CoordinateProfileId,
    int VersionNo,
    string VersionLabel,
    string Status,
    bool IsActive,
    string UsageScope,
    string VerificationStatus,
    string? SourceVersionId,
    string ChangeReason,
    string ChangeSummaryJson,
    string ValidationResultJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    IReadOnlyList<CoordinatePointResponse> TargetPoints);

public sealed record CoordinatePointResponse(
    string Id,
    string PointCode,
    string PointType,
    long? PresetXUm,
    long? PresetYUm,
    long? CalibratedXUm,
    long? CalibratedYUm,
    long? CalibratedZUm,
    long? SafeZUm,
    long? AspirateZUm,
    long? DispenseZUm,
    long? ActionOffsetXUm,
    long? ActionOffsetYUm,
    long? ActionOffsetZUm,
    string ValidationStatus,
    string ValidationMessage,
    bool RequiresCalibration,
    bool IsEnabled);

public sealed record CoordinateProfileVersionMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string CoordinateProfileId,
    string CoordinateProfileVersionId,
    int VersionNo,
    string VersionLabel,
    string Status,
    bool IsActive,
    string UsageScope,
    string VerificationStatus,
    string Message);

public sealed record LiquidClassResponse(
    string Id,
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
    string? EnabledVersionId,
    IReadOnlyList<LiquidClassVersionResponse> Versions);

public sealed record LiquidClassVersionResponse(
    string Id,
    int VersionNo,
    string VersionLabel,
    string Name,
    string Status,
    bool IsReferenceable,
    string? SourceVersionId,
    string ChangeReason,
    string ChangeSummaryJson,
    bool LiquidDetectionEnabled,
    int LiquidDetectionSensitivityPercent,
    int LiquidDetectionSpeedUmPerSecond,
    int AspirateSpeedUlPerSecond,
    int AspirateDelayMs,
    int DispenseSpeedUlPerSecond,
    int DispenseDelayMs,
    int LeadingAirGapUl,
    int TrailingAirGapUl,
    int BlowoutVolumeUl,
    int BlowoutDelayMs,
    int VolumeAdjustmentUl,
    int PreWetCycles,
    int MixCycles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? EnabledAtUtc,
    IReadOnlyList<LiquidClassDifferenceResponse> Differences,
    IReadOnlyList<LiquidClassValidationResponse> ValidationRecords);

public sealed record LiquidClassDifferenceResponse(
    string ParameterName,
    string? PreviousValue,
    string? NewValue,
    string Unit);

public sealed record LiquidClassValidationResponse(
    string Stage,
    bool IsValid,
    string ResultJson,
    string? ValidatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record LiquidClassVersionMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string LiquidClassId,
    string LiquidClassVersionId,
    int VersionNo,
    string VersionLabel,
    string Status,
    bool IsReferenceable,
    string Message);

public sealed record EngineeringSessionResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string SessionId,
    string Status,
    string UserId,
    string Username,
    string Reason,
    string Target,
    bool DangerousOperationConfirmed,
    DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Message);

public sealed record EngineeringCommandLogResponse(
    string DeviceCommandExecutionId,
    string MachineRunId,
    string? WorkflowStepExecutionId,
    string CommandType,
    string Status,
    string PayloadJson,
    string ResultJson,
    string? LiquidClassVersionId,
    int? LiquidClassVersionNo,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CommandSentAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record EngineeringErrorCodeResponse(
    string SourceType,
    string SourceId,
    string Code,
    string Status,
    string Message,
    string? ModuleCode,
    string? MachineRunId,
    DateTimeOffset CreatedAtUtc);

public sealed record EngineeringMockCommunicationResponse(
    string Id,
    string DeviceMode,
    string AdapterName,
    string ModuleCode,
    string Action,
    string CommandId,
    string? CorrelationId,
    string? Actor,
    string Source,
    string Status,
    bool Ok,
    bool Acknowledged,
    string? ErrorCode,
    string Message,
    string RequestJson,
    string ResponseJson,
    string PersistenceStatus,
    string? PersistenceFailureReason,
    int PersistenceAttemptCount,
    DateTimeOffset PersistenceLastAttemptAtUtc,
    DateTimeOffset? PersistenceCompletedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record EngineeringDeviceDiagnosticsResponse(
    object Thermal,
    object Fluidics,
    IReadOnlyList<object> MotionModules,
    DateTimeOffset GeneratedAtUtc);

public sealed record EngineeringConfigExportResponse(
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<CoordinateProfileResponse> CoordinateProfiles,
    IReadOnlyList<LiquidClassResponse> LiquidClasses);

public sealed record EngineeringConfigImportPreviewResponse(
    bool Ok,
    string ConfigType,
    string TargetCode,
    string? SourceVersionId,
    string VersionLabel,
    bool HasChanges,
    IReadOnlyList<EngineeringConfigDiffItemResponse> Differences,
    IReadOnlyList<string> Errors);

public sealed record EngineeringConfigDiffItemResponse(
    string Path,
    string? Before,
    string? After,
    string ChangeType);
