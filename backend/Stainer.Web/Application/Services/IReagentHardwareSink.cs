namespace Stainer.Web.Application.Services;

// 试剂区硬件通信 Sink：把"试剂状态变更"转成设备命令（写方向，一期），
// 并预留"从硬件读传感器状态"（读方向，二期占位）。
// 实现为 Scoped，由 ReagentHardwareDispatcher 每事件新建 scope 解析（不可被 Singleton 直接 capture）。
public interface IReagentHardwareSink
{
    // 一期·写方向：试剂状态变更 → 经 IDeviceAdapter 四步范式通知硬件。
    Task<ReagentHardwareResult> NotifyReagentStateChangedAsync(
        ReagentHardwareEvent evt, CancellationToken cancellationToken = default);

    // 二期·读方向占位：从硬件传感器读取试剂通道到位/入口感应等状态。一期返回 NotSupported，不发任何字节。
    Task<ReagentHardwareSensorReadout> ReadReagentSensorsAsync(
        string rackCode, CancellationToken cancellationToken = default);
}

// 从 MachineEventMessage 投影出的试剂硬件事件。
public sealed record ReagentHardwareEvent(
    string EventId,
    string EventType,
    string? ScanSessionId,
    string? Position,
    string? ReagentBottleId,
    string? ReagentCode,
    string? ScanResult,
    int? RemainingVolumeUl,
    string? Message,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, object?> Payload)
{
    public static ReagentHardwareEvent FromMessage(MachineEventMessage message) => new(
        message.EventId,
        message.Type,
        ReadString(message, "scanSessionId"),
        ReadString(message, "position"),
        ReadString(message, "reagentBottleId"),
        ReadString(message, "reagentCode"),
        ReadString(message, "scanResult"),
        ReadInt(message, "remainingVolumeUl"),
        ReadString(message, "message"),
        message.OccurredAtUtc,
        message.Payload);

    // 派生稳定的硬件命令 id：用业务键而非 EventId(Guid.NewGuid())，
    // 保证 rescan（同一 scanSessionId+position 重复确认）或 dispatcher 重启时幂等去重。
    public string DeriveCommandId()
    {
        var key = !string.IsNullOrWhiteSpace(Position)
            ? $"{EventType}:{ScanSessionId}:{Position}"
            : !string.IsNullOrWhiteSpace(ReagentBottleId)
                ? $"{EventType}:{ReagentCode}:{ReagentBottleId}"
                : $"{EventType}:{EventId}";
        return $"reagent-hardware:{key}";
    }

    private static string? ReadString(MachineEventMessage message, string key)
        => message.Payload.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static int? ReadInt(MachineEventMessage message, string key)
        => message.Payload.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : null;
}

public sealed record ReagentHardwareResult(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    string AdapterMode,
    string AdapterName,
    string CommandId,
    bool Replayed);

public sealed record ReagentHardwareSensorReadout(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, object?> Data);
