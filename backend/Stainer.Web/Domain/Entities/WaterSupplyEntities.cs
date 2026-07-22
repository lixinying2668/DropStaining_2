namespace Stainer.Web.Domain.Entities;

// 供水孔 / 供水模块：每个染色通道（1..4）对应一个独立供水孔，拥有自己的进/出水温度、
// 出水水量、流速与开关状态。与 /api/fluidics/*（液路/泵/清洗/混匀/液位）业务语义不同，
// 不得把供水孔出水动作映射到 fluidics.wash。
public sealed class WaterSupplyChannelState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int ChannelNo { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public int InletTemperatureDeciC { get; set; } = 250;
    public int OutletTargetTemperatureDeciC { get; set; } = 450;
    public int OutletTemperatureDeciC { get; set; } = 250;
    public int OutletVolumeMl { get; set; }
    public int OutletFlowRateMlPerMinute { get; set; } = 250;
    public bool OutletEnabled { get; set; }
    public string Status { get; set; } = WaterSupplyStatuses.Idle;
    public bool IsConnected { get; set; } = true;
    public string? CurrentCommandId { get; set; }
    public string? FaultCode { get; set; }
    public string? FaultMessage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

// 供水孔状态变化遥测：温度/水量/流速/开关/commandId/时间。
public sealed class WaterSupplyTelemetry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceId { get; set; } = string.Empty;
    public int ChannelNo { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int InletTemperatureDeciC { get; set; }
    public int OutletTargetTemperatureDeciC { get; set; }
    public int OutletTemperatureDeciC { get; set; }
    public int OutletVolumeMl { get; set; }
    public int OutletFlowRateMlPerMinute { get; set; }
    public bool OutletEnabled { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string? CommandId { get; set; }
    public string? FaultCode { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class WaterSupplyStatuses
{
    public const string Idle = "Idle";
    public const string Running = "Running";
    public const string Stopped = "Stopped";
    public const string Faulted = "Faulted";
    public const string Disconnected = "Disconnected";
    public const string Unknown = "Unknown";
    public const string TimedOut = "TimedOut";
}

public static class WaterSupplyFaultTypes
{
    public const string Failure = "Failure";
    public const string Timeout = "Timeout";
    public const string SensorFailure = "SensorFailure";
    public const string Disconnected = "Disconnected";
    public const string Unknown = "Unknown";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Failure,
        Timeout,
        SensorFailure,
        Disconnected,
        Unknown
    };
}

public static class WaterSupplyTelemetryEventTypes
{
    public const string ChannelChanged = "ChannelChanged";
    public const string OutletOpened = "OutletOpened";
    public const string OutletClosed = "OutletClosed";
    public const string FaultConfigured = "FaultConfigured";
    public const string FaultCleared = "FaultCleared";
}
