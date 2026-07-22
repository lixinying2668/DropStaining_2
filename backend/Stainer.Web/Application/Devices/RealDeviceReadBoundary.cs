namespace Stainer.Web.Application.Devices;

public interface IDeviceByteTransport
{
    string Name { get; }

    bool IsConfigured { get; }

    Task<DeviceByteTransportResult> ExchangeAsync(
        DeviceByteTransportRequest request,
        CancellationToken cancellationToken = default);

    Task<DeviceByteTransportResult> ReceiveAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceByteTransportRequest(
    string Endpoint,
    string Operation,
    byte[] RequestBytes);

public sealed record DeviceByteTransportResult(
    string Status,
    IReadOnlyList<byte[]> ResponseChunks,
    string? ErrorCode = null,
    string? Message = null);

public static class DeviceByteTransportStatuses
{
    public const string Succeeded = "Succeeded";
    public const string TimedOut = "TimedOut";
    public const string Disconnected = "Disconnected";
    public const string Failed = "Failed";
    // 任务 P1-03-01：更细粒度的串口 Transport 状态，用于区分错误类型。
    // InvalidFrame：CRC 校验失败 / 非法帧 / 数据不足。
    // CommunicationError：串口打开失败 / I/O 异常 / 工厂异常。
    // NotConnected：Transport 未配置（PortName 缺失）或未连接。
    public const string InvalidFrame = "InvalidFrame";
    public const string CommunicationError = "CommunicationError";
    public const string NotConnected = "NotConnected";
}

public static class DeviceByteTransportEndpoints
{
    public const string MainController = "main-controller-v1.0.4";
    public const string Dcr55 = "dcr55-sample-scanner";
    public const string StandaloneCooling = "standalone-cooling-v1.0";
}

public interface IRealDeviceReadAdapter
{
    Task<RealDeviceReadResult<MainControllerWorkStatus>> ReadControllerWorkStatusAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerNodeStatuses>> ReadControllerNodeStatusesAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerRunTime>> ReadControllerRunTimeAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerTemperatureBoard>> ReadTemperaturesAsync(byte boardId, CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerTemperatureBoard>> ReadTargetTemperaturesAsync(byte boardId, CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerSwitchBoard>> ReadTemperatureSwitchesAsync(byte boardId, CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerOptocouplerStatus>> ReceiveLiquidLevelStatusAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerPwmSpeeds>> ReadPwmSpeedsAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerMixerValue>> ReadMixerOriginAsync(byte boardId, CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerMixerValue>> ReadMixerRemainingCountAsync(byte boardId, CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerQrScanStatus>> ReadQrScanStatusAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerQrText>> ReadQrTextAsync(CancellationToken cancellationToken = default);
    Dcr55TriggerPreparation PrepareDcr55Trigger(Dcr55TriggerMode mode, byte[]? configuredTerminator);
    Task<RealDeviceReadResult<Dcr55ScanResult>> ReceiveDcr55ResultAsync(CancellationToken cancellationToken = default);

    // 制冷读取已统一走主控 0x03，不再使用 StandaloneCooling 通道。
    // 单项读取返回主控解析模型（整摄氏度）；聚合读取返回 deci-C 快照，供 ThermalControlService / DevicePrecheckService 直接消费。
    Task<RealDeviceReadResult<MainControllerCoolingConnectionStatus>> ReadCoolingConnectionStatusAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerCoolingTemperature>> ReadCoolingCurrentTemperatureAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerCoolingTemperature>> ReadCoolingTargetTemperatureAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerCoolingSwitchState>> ReadCoolingSwitchStateAsync(CancellationToken cancellationToken = default);
    Task<RealDeviceReadResult<MainControllerCoolingSnapshot>> ReadCoolingSnapshotAsync(CancellationToken cancellationToken = default);
}

// 制冷聚合快照：主控 0x03 多条只读命令的合并结果。温度已从协议“整摄氏度”×10 换算为 deci-C。
// 不含 Status 字符串——协议不返回状态文本，CoolingUnitState.Status 由 ThermalControlService 按 (current,target,enabled) 推导。
public sealed record MainControllerCoolingSnapshot(
    bool IsConnected,
    int CurrentTemperatureDeciC,
    int TargetTemperatureDeciC,
    bool IsEnabled);

public sealed record RealDeviceReadResult<T>(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    T? Value,
    byte[] RequestBytes,
    byte[] ResponseBytes,
    IReadOnlyList<MainControllerAck> Acknowledgements,
    IReadOnlyList<MainControllerPutReport> PutReports);

public sealed record MainControllerPutReport(
    byte ParentClass,
    byte SubClass,
    object Value);

public sealed record Dcr55TriggerPreparation(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    byte[] CommandBytes,
    bool Sent);
