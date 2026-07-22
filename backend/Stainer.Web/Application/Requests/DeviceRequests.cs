namespace Stainer.Web.Application.Requests;

public sealed record StartDeviceInitializationRequest(string CommandId);

public sealed record PrecheckRunRequest(string CommandId, string? RunMode = null);

public sealed record RetryDeviceInitializationRequest(
    string CommandId,
    string Reason);

public sealed record ConfigureMockDeviceFaultRequest(
    string CommandId,
    string ModuleCode,
    string FaultType,
    string Reason,
    string? ErrorCode = null,
    string? Message = null);

public sealed record ClearMockDeviceFaultRequest(
    string CommandId,
    string Reason,
    string? ModuleCode = null);

public sealed record ReagentQrCommandRequest(
    string CommandId,
    string? Position = null,
    string? RawBarcode = null,
    string? Text = null,
    string? ScanSessionId = null);

public sealed record ReportReagentQrTextRequest(
    string CommandId,
    string? Text = null,
    string? Position = null,
    string? ScanSessionId = null,
    DateOnly? ExpirationDate = null);

public sealed record SetThermalPointRequest(
    string CommandId,
    int TargetTemperatureDeciC,
    bool IsEnabled);

public sealed record SetThermalBoardRequest(
    string CommandId,
    int TargetTemperatureDeciC,
    bool IsEnabled);

public sealed record SetCoolingRequest(
    string CommandId,
    int TargetTemperatureDeciC,
    bool IsEnabled);

public sealed record ConfigureThermalFaultRequest(
    string CommandId,
    string TargetType,
    string FaultType,
    string Reason,
    string? DrawerCode = null,
    int? SlotNo = null);

public sealed record ClearThermalFaultRequest(
    string CommandId,
    string TargetType,
    string Reason,
    string? DrawerCode = null,
    int? SlotNo = null);

public sealed record RunPumpRequest(
    string CommandId,
    string PwmChannelCode,
    int SpeedPercent,
    int? DurationMs = null,
    string? TargetPointCode = null,
    string? Reason = null);

public sealed record StopPumpRequest(
    string CommandId,
    string PwmChannelCode,
    string? Reason = null);

public sealed record WashTargetRequest(
    string CommandId,
    string TargetPointCode,
    int SpeedPercent,
    int? DurationMs = null,
    string? Reason = null);

public sealed record StopWashRequest(
    string CommandId,
    string? Reason = null);

public sealed record MixerCommandRequest(
    string CommandId,
    string? RoundKey = null,
    string? Reason = null);

public sealed record SetLiquidLevelRequest(
    string CommandId,
    string SourceType,
    string LevelStatus,
    int? CurrentVolumeUl = null,
    string? Reason = null);

public sealed record SetLiquidThresholdRequest(
    string CommandId,
    string SourceType,
    int? LowThresholdUl = null,
    int? FullThresholdUl = null,
    string? Reason = null);

public sealed record ConfigureFluidicsFaultRequest(
    string CommandId,
    string TargetType,
    string FaultType,
    string Reason,
    string? PwmChannelCode = null,
    string? DrawerCode = null,
    string? SourceType = null,
    string? ErrorCode = null,
    string? Message = null);

public sealed record ClearFluidicsFaultRequest(
    string CommandId,
    string TargetType,
    string Reason,
    string? PwmChannelCode = null,
    string? DrawerCode = null,
    string? SourceType = null);

// 供水孔 / 供水模块（water_supply_channel_states）：与 /api/fluidics/* 语义不同，独立温/流/量/开关。
public sealed record SetWaterSupplyTargetTemperatureRequest(
    string CommandId,
    int TargetTemperatureDeciC,
    string Reason);

public sealed record SetWaterSupplyFlowRequest(
    string CommandId,
    int FlowRateMlPerMinute,
    string Reason);

public sealed record SetWaterSupplyOutletRequest(
    string CommandId,
    bool Enabled,
    string Reason,
    int? DurationMs = null);

public sealed record ConfigureWaterSupplyFaultRequest(
    string CommandId,
    int ChannelNo,
    string FaultType,
    string Reason,
    string? ErrorCode = null,
    string? Message = null);

public sealed record ClearWaterSupplyFaultRequest(
    string CommandId,
    int ChannelNo,
    string Reason);

