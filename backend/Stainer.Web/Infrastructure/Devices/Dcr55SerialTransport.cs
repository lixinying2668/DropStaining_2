using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Devices;

namespace Stainer.Web.Infrastructure.Devices;

// DCR55 真实串口 Transport。
//
// 设计约束（DCR55-02）：
// - SerialPort 仅存在于本 Transport 层；Application 层（Dcr55RealAdapter 等）只通过
//   IDeviceByteTransport 的 ReceiveAsync(endpoint) 获取结果，永远不会直接接触串口。
// - 本阶段只允许“单次触发”：在 ReceiveAsync(Dcr55) 内完成“打开→发送单次触发命令→
//   读取返回→关闭”，一次调用对应一次扫码。禁止连续触发 / 自动循环 / 自动重试。
// - COM 口由 Dcr55ConnectionOptions 配置提供，绝不硬编码、绝不自动扫描枚举。
public sealed class Dcr55SerialTransport : IDeviceByteTransport
{
    private const string Cr = "\r";          // RDCMXEV1 命令终止符
    private const string Crlf = "\r\n";      // 条码返回帧终止符

    private readonly Dcr55ConnectionOptions configuration;
    private readonly Func<ISerialPort> portFactory;
    private readonly TimeSpan receiveTimeout;
    private readonly TimeSpan frameSilence;

    public string Name => "dcr55-serial";

    public bool IsConfigured => configuration.IsConfigured;

    // 生产构造：使用真实 System.IO.Ports.SerialPort。
    public Dcr55SerialTransport(
        Dcr55ConnectionOptions configuration,
        TimeSpan? receiveTimeout = null,
        TimeSpan? frameSilence = null)
        : this(configuration, CreateDefaultPort, receiveTimeout, frameSilence)
    {
    }

    // 测试 / 受控验证构造：可注入 ISerialPort 实现。
    internal Dcr55SerialTransport(
        Dcr55ConnectionOptions configuration,
        Func<ISerialPort> portFactory,
        TimeSpan? receiveTimeout = null,
        TimeSpan? frameSilence = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(portFactory);
        this.configuration = configuration;
        this.portFactory = portFactory;
        this.receiveTimeout = receiveTimeout ?? TimeSpan.FromSeconds(5);
        this.frameSilence = frameSilence ?? TimeSpan.FromMilliseconds(150);
    }

    public Task<DeviceByteTransportResult> ExchangeAsync(
        DeviceByteTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        // DCR55 单次扫码语义为“触发后接收”，统一走 ReceiveAsync。
        // 显式拒绝 Exchange，避免被误用为命令通道。
        _ = cancellationToken;
        return Task.FromResult(new DeviceByteTransportResult(
            DeviceByteTransportStatuses.Failed,
            [],
            "dcr55_serial_exchange_not_supported",
            $"DCR55 serial transport only supports ReceiveAsync; endpoint '{request.Endpoint}' exchange is not supported."));
    }

    public async Task<DeviceByteTransportResult> ReceiveAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(endpoint, DeviceByteTransportEndpoints.Dcr55, StringComparison.Ordinal))
        {
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.Failed,
                [],
                "dcr55_serial_endpoint_not_supported",
                $"DCR55 serial transport does not serve endpoint '{endpoint}'.");
        }

        if (!configuration.IsConfigured)
        {
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.Failed,
                [],
                "dcr55_serial_not_configured",
                "DCR55 serial port is not configured.");
        }

        // 串联化单次触发：整个流程在一个临时串口实例内完成并关闭。
        // 严禁并发触发与自动重试。
        ISerialPort port;
        try
        {
            port = portFactory();
            ConfigurePort(port);
        }
        catch (Exception ex)
        {
            return Failure("dcr55_serial_port_factory_failed", "Failed to create serial port: " + ex.Message);
        }

        try
        {
            await OpenAsync(port, cancellationToken);
        }
        catch (Exception)
        {
            port.Dispose();
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.Disconnected,
                [],
                "dcr55_serial_open_failed",
                $"Failed to open COM port '{configuration.Port}'. The port may not exist or is in use.");
        }

        try
        {
            port.DiscardInBuffer();

            // 单次触发命令（RDCMXEV1,P11,P20 + CR）。禁止连续触发命令。
            var command = Encoding.ASCII.GetBytes(Dcr55Protocol.SingleTriggerCommandText + Cr);
            port.Write(command, 0, command.Length);

            var rawText = await ReadFrameAsync(port, cancellationToken);
            var chunks = new List<byte[]> { Encoding.ASCII.GetBytes(rawText) };

            return new DeviceByteTransportResult(DeviceByteTransportStatuses.Succeeded, chunks);
        }
        catch (OperationCanceledException)
        {
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.TimedOut,
                [],
                "dcr55_serial_canceled",
                "DCR55 single scan was canceled.");
        }
        catch (TimeoutException)
        {
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.TimedOut,
                [],
                "dcr55_serial_read_timeout",
                "DCR55 did not return a barcode within the receive timeout.");
        }
        catch (InvalidOperationException ex)
        {
            // 串口在读写过程中被断开（例如 USB 拔出）。
            return new DeviceByteTransportResult(
                DeviceByteTransportStatuses.Disconnected,
                [],
                "dcr55_serial_disconnected",
                "DCR55 serial port disconnected during I/O: " + ex.Message);
        }
        finally
        {
            try { port.Close(); }
            catch { /* 关闭失败不影响已读结果 */ }
            port.Dispose();
        }
    }

    private void ConfigurePort(ISerialPort port)
    {
        port.PortName = configuration.Port!;
        port.BaudRate = configuration.BaudRate;
        port.DataBits = configuration.DataBits;
        port.Parity = MapParity(configuration.Parity);
        port.StopBits = MapStopBits(configuration.StopBits);
        // 单次读取的最小阻塞时间，用于帧间静默判定。
        port.ReadTimeout = Math.Max((int)frameSilence.TotalMilliseconds, 50);
    }

    private static Parity MapParity(Dcr55Parity parity) => parity switch
    {
        Dcr55Parity.None => Parity.None,
        Dcr55Parity.Odd => Parity.Odd,
        Dcr55Parity.Even => Parity.Even,
        Dcr55Parity.Mark => Parity.Mark,
        Dcr55Parity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(Dcr55StopBits stopBits) => stopBits switch
    {
        Dcr55StopBits.One => StopBits.One,
        Dcr55StopBits.OnePointFive => StopBits.OnePointFive,
        Dcr55StopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    private static Task OpenAsync(ISerialPort port, CancellationToken cancellationToken)
    {
        // System.IO.Ports 的 Open 是同步 API；在后台线程执行以免阻塞调用方的事件循环，
        // 并尊重取消。打开本身不允许自动重试。
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            port.Open();
        }, cancellationToken);
    }

    // 读取一帧条码：持续读取字节直到出现 CRLF，并在 CRLF 后进入“帧间静默”确认无后续数据；
    // 整体受 receiveTimeout 与 cancellationToken 双重约束，绝不无限等待。
    private async Task<string> ReadFrameAsync(ISerialPort port, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var deadline = DateTimeOffset.UtcNow + receiveTimeout;
        var sawCrlf = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(receiveTimeout);

        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();

            int value;
            try
            {
                value = await Task.Run(() => port.ReadByte(), cts.Token);
            }
            catch (TimeoutException) when (sawCrlf)
            {
                // 已收到完整 CRLF，且在 frameSilence 内没有后续数据：帧结束。
                return builder.ToString();
            }
            catch (TimeoutException) when (DateTimeOffset.UtcNow >= deadline)
            {
                throw;
            }
            catch (TimeoutException)
            {
                // 帧内字符间静默，但还没到总超时：继续等待。
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw;
                }

                continue;
            }

            if (value < 0)
            {
                // 串口关闭 / EOF：视为断开。
                throw new InvalidOperationException("Serial port returned end-of-stream.");
            }

            builder.Append((char)value);

            if (!sawCrlf && builder.ToString().EndsWith(Crlf, StringComparison.Ordinal))
            {
                sawCrlf = true;
            }
        }
    }

    private static DeviceByteTransportResult Failure(string code, string message) =>
        new(DeviceByteTransportStatuses.Failed, [], code, message);

    private static ISerialPort CreateDefaultPort() => new SystemIoSerialPortAdapter();
}