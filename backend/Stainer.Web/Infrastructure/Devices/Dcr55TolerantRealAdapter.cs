using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Devices;

namespace Stainer.Web.Infrastructure.Devices;

// DCR55 容错 Real Adapter（与现有 Dcr55RealAdapter 并列的【新增】类型，不修改任何现有代码）。
//
// 背景：现场实测 DCR55 触发后返回的条码以单个 \r（0x0D）结尾，而现有 Dcr55SerialTransport/
// Dcr55RealAdapter 只认 \r\n 作为帧结束，会一直等到总超时（表现即“扫不到文本”）。
// 本类型提供等价的“单次触发 + 容错读帧”扫码入口：
// - 触发命令仍取自产品协议常量 Dcr55Protocol.SingleTriggerCommandText（RDCMXEV1,P11,P20\r）；
// - 帧读取兼容 \r / \r\n / \n / 无结束符，靠“收到数据后的帧间静默”收尾；
// - 解析走 Dcr55TolerantProtocol.ParseBarcodeResult。
// 一次 ReceiveScanAsync 内完成“打开→发一次触发→读帧→关闭”，无自动重连/重试。
//
// 实现 IDcr55Adapter，可在需要时作为 Dcr55RealAdapter 的替代。将其注册到 DI 属于对
// 现有 ServiceCollectionExtensions 的修改，按“不改现有代码”的约定不在本类型中完成。
public sealed class Dcr55TolerantRealAdapter : IDcr55Adapter
{
    private const string Cr = "\r";          // RDCMXEV1 命令终止符
    private static readonly Encoding Ascii = Encoding.ASCII;

    private readonly Dcr55ConnectionOptions configuration;
    private readonly Func<ISerialPort> portFactory;
    private readonly TimeSpan receiveTimeout;
    private readonly TimeSpan frameSilence;
    private readonly TimeProvider clock;

    // 生产构造：使用真实 System.IO.Ports.SerialPort。
    public Dcr55TolerantRealAdapter(
        Dcr55ConnectionOptions configuration,
        TimeSpan? receiveTimeout = null,
        TimeSpan? frameSilence = null,
        TimeProvider? timeProvider = null)
        : this(configuration, CreateDefaultPort, receiveTimeout, frameSilence, timeProvider)
    {
    }

    // 测试 / 受控验证构造：可注入 ISerialPort 实现。
    internal Dcr55TolerantRealAdapter(
        Dcr55ConnectionOptions configuration,
        Func<ISerialPort> portFactory,
        TimeSpan? receiveTimeout = null,
        TimeSpan? frameSilence = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(portFactory);
        this.configuration = configuration;
        this.portFactory = portFactory;
        this.receiveTimeout = receiveTimeout ?? TimeSpan.FromSeconds(5);
        this.frameSilence = frameSilence ?? TimeSpan.FromMilliseconds(150);
        this.clock = timeProvider ?? TimeProvider.System;
    }

    public async Task<Dcr55ScanResult> ReceiveScanAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = clock.GetUtcNow();
        if (!configuration.IsConfigured)
        {
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.NotConfigured, string.Empty, timestamp);
        }

        ISerialPort port;
        try
        {
            port = portFactory();
            ConfigurePort(port);
        }
        catch (Exception)
        {
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Disconnected, string.Empty, timestamp);
        }

        try
        {
            await OpenAsync(port, cancellationToken);
        }
        catch (Exception)
        {
            port.Dispose();
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Disconnected, string.Empty, timestamp);
        }

        string rawText;
        try
        {
            port.DiscardInBuffer();

            // 单次触发命令（RDCMXEV1,P11,P20 + CR）。禁止连续触发命令。
            var command = Ascii.GetBytes(Dcr55Protocol.SingleTriggerCommandText + Cr);
            port.Write(command, 0, command.Length);

            rawText = await ReadTolerantFrameAsync(port, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Timeout, string.Empty, timestamp);
        }
        catch (TimeoutException)
        {
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Timeout, string.Empty, timestamp);
        }
        catch (InvalidOperationException)
        {
            // 串口在读写过程中被断开（例如 USB 拔出）。
            return Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Disconnected, string.Empty, timestamp);
        }
        finally
        {
            try { port.Close(); }
            catch { /* 关闭失败不影响已读结果 */ }
            port.Dispose();
        }

        return Dcr55TolerantProtocol.ParseBarcodeResult(rawText, timestamp);
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

    // 容错读帧：持续读取字节，“已收到数据后的帧间静默”即视为帧结束，
    // 兼容 \r / \r\n / \n / 无结束符；整体受 receiveTimeout 与 cancellationToken 双重约束，
    // 绝不无限等待。始终无数据则在总超时后由调用方判为 Timeout。
    private async Task<string> ReadTolerantFrameAsync(ISerialPort port, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var deadline = DateTimeOffset.UtcNow + receiveTimeout;

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
            catch (TimeoutException) when (builder.Length > 0)
            {
                // 已收到数据后的静默即帧结束：兼容 \r / \r\n / \n / 无结束符。
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
        }
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

    private static ISerialPort CreateDefaultPort() => new SystemIoSerialPortAdapter();
}
