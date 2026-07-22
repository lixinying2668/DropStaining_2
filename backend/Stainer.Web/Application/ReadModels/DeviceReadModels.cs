using Stainer.Web.Application.Devices;

namespace Stainer.Web.Application.ReadModels;

public sealed record DeviceInitializationCheckResponse(
    string Id,
    int StepNo,
    string ModuleCode,
    string Status,
    string? ErrorCode,
    string Message,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyDictionary<string, object?> Result);

public sealed record DeviceInitializationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string? RunId,
    string Status,
    string DeviceMode,
    string AdapterName,
    int AttemptNo,
    string? RetryOfRunId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<DeviceInitializationCheckResponse> Checks,
    string Message);

public sealed record DeviceFaultMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Message,
    DeviceStatusSnapshot State);

public sealed record ReagentQrDeviceOperationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Command,
    string Status,
    string? ErrorCode,
    string Message,
    bool Online,
    ushort ScanStatusCode,
    string ScanStatus,
    string? Text,
    string? Position,
    string? ChannelCode,
    string DeviceMode,
    string AdapterName);

public sealed record ReagentQrReportResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Command,
    string Status,
    string? ErrorCode,
    string Message,
    bool Online,
    ushort ScanStatusCode,
    string ScanStatus,
    string? Text,
    string? Position,
    string? ChannelCode,
    string? RawBarcode,
    string? ScanSessionId,
    bool ScanSynced,
    string? ScanResult,
    string? ValidationMessage,
    bool ChannelResolvedByMock,
    string DeviceMode,
    string AdapterName);

public sealed record ThermalStateResponse(
    bool Ready,
    IReadOnlyList<ThermalPointResponse> Points,
    CoolingStateResponse Cooling,
    DateTimeOffset GeneratedAtUtc);

public sealed record ThermalPointResponse(
    string Id,
    string DrawerCode,
    int BoardNo,
    int SlotNo,
    int PointNo,
    int CurrentTemperatureDeciC,
    int TargetTemperatureDeciC,
    bool IsEnabled,
    bool IsConnected,
    string Status,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record CoolingStateResponse(
    string Id,
    int CurrentTemperatureDeciC,
    int TargetTemperatureDeciC,
    bool IsEnabled,
    bool IsConnected,
    string Status,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record ThermalMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Message,
    ThermalStateResponse State);

public sealed record TemperatureTelemetryResponse(
    string Id,
    string SourceType,
    string SourceId,
    string? DrawerCode,
    int? BoardNo,
    int? SlotNo,
    int? PointNo,
    int CurrentTemperatureDeciC,
    int TargetTemperatureDeciC,
    bool IsEnabled,
    bool IsConnected,
    string Status,
    string? FaultCode,
    DateTimeOffset RecordedAtUtc);

public sealed record FluidicsStateResponse(
    bool Ready,
    IReadOnlyList<PumpChannelResponse> Pumps,
    IReadOnlyList<MixerChannelResponse> Mixers,
    IReadOnlyList<LiquidContainerResponse> LiquidLevels,
    DateTimeOffset GeneratedAtUtc);

public sealed record PumpChannelResponse(
    string Id,
    string PwmChannelCode,
    int PwmChannelNo,
    string DrawerCode,
    int SpeedPercent,
    string Direction,
    string Status,
    bool IsConnected,
    string? TargetPointCode,
    int? DurationMs,
    string? CurrentCommandId,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record MixerChannelResponse(
    string Id,
    string DrawerCode,
    int ChannelNo,
    string Status,
    bool IsConnected,
    string? CurrentRoundKey,
    string? CurrentCommandId,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record LiquidContainerResponse(
    string Id,
    string SourceType,
    string DisplayName,
    bool IsWaste,
    int CapacityUl,
    int CurrentVolumeUl,
    int LowThresholdUl,
    int FullThresholdUl,
    string LevelStatus,
    bool IsConnected,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record FluidicsMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Message,
    FluidicsStateResponse State);

public sealed record FluidicsTelemetryResponse(
    string Id,
    string SourceType,
    string SourceId,
    string EventType,
    string Status,
    string? PwmChannelCode,
    string? DrawerCode,
    string? LiquidSourceType,
    int? SpeedPercent,
    string? Direction,
    int? CurrentVolumeUl,
    int? CapacityUl,
    string? TargetPointCode,
    string? CommandId,
    string? MachineRunId,
    string? WorkflowStepExecutionId,
    string? DeviceCommandExecutionId,
    string? FaultCode,
    DateTimeOffset RecordedAtUtc);

public sealed record WaterSupplyStateResponse(
    bool Ready,
    IReadOnlyList<WaterSupplyChannelResponse> Channels,
    DateTimeOffset GeneratedAtUtc);

public sealed record WaterSupplyChannelResponse(
    string Id,
    int ChannelNo,
    string ChannelCode,
    int InletTemperatureDeciC,
    int OutletTargetTemperatureDeciC,
    int OutletTemperatureDeciC,
    int OutletVolumeMl,
    int OutletFlowRateMlPerMinute,
    bool OutletEnabled,
    string Status,
    bool IsConnected,
    string? CurrentCommandId,
    string? FaultCode,
    string? FaultMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed record WaterSupplyMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Message,
    WaterSupplyStateResponse State);

public sealed record WaterSupplyTelemetryResponse(
    string Id,
    string SourceId,
    int ChannelNo,
    string ChannelCode,
    string EventType,
    int InletTemperatureDeciC,
    int OutletTargetTemperatureDeciC,
    int OutletTemperatureDeciC,
    int OutletVolumeMl,
    int OutletFlowRateMlPerMinute,
    bool OutletEnabled,
    string Status,
    bool IsConnected,
    string? CommandId,
    string? FaultCode,
    DateTimeOffset RecordedAtUtc);

