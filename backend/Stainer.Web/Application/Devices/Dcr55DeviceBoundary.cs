namespace Stainer.Web.Application.Devices;

public interface IDcr55Adapter
{
    Task<Dcr55ScanResult> ReceiveScanAsync(CancellationToken cancellationToken = default);
}

public sealed record Dcr55ConnectionOptions
{
    public string? Port { get; init; }

    public int BaudRate { get; init; } = 115200;

    public int DataBits { get; init; } = 8;

    public Dcr55Parity Parity { get; init; } = Dcr55Parity.None;

    public Dcr55StopBits StopBits { get; init; } = Dcr55StopBits.One;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Port);
}

public enum Dcr55Parity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum Dcr55StopBits
{
    One,
    OnePointFive,
    Two
}

public enum Dcr55ScanStatus
{
    Success,
    Timeout,
    Disconnected,
    InvalidResponse,
    NotConfigured
}

// 主控（Main Controller）串口连接配置。对应任务 P1-03-01 的通讯参数要求。
// 仅描述连接参数，不包含业务逻辑；串口对象只存在于 Infrastructure 层 Transport。
public sealed record MainControllerConnectionOptions
{
    public string? PortName { get; init; }

    public int BaudRate { get; init; } = 115200;

    public int DataBits { get; init; } = 8;

    public MainControllerParity Parity { get; init; } = MainControllerParity.None;

    public MainControllerStopBits StopBits { get; init; } = MainControllerStopBits.One;

    public MainControllerHandshake Handshake { get; init; } = MainControllerHandshake.None;

    public int ReadTimeoutMilliseconds { get; init; } = 2000;

    public int WriteTimeoutMilliseconds { get; init; } = 2000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PortName);
}

public enum MainControllerParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum MainControllerStopBits
{
    One,
    OnePointFive,
    Two
}

public enum MainControllerHandshake
{
    None,
    RequestToSend,
    RequestToSendXOnXOff,
    XOnXOff
}

public sealed record Dcr55ScanResult(
    string? Barcode,
    string RawText,
    Dcr55ScanStatus Status,
    DateTimeOffset Timestamp);
