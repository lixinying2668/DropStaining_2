using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;

namespace Stainer.Web.Infrastructure.Devices;

// 调试用真实 COM 字节收发服务：打开指定 COM 口 → 发测试字节 → 读返回 → 关闭。
// 原始字节，不经主控协议/白名单；System.IO.Ports 仅在本层（Transport/Devices 边界）。
// 读用「总 deadline + 短 ReadTimeout 循环 ReadByte」：环回下收完即停，串口助手异步发也能在窗口内收。
public sealed class SerialDebugService
{
    private const int ReadChunkTimeoutMs = 150;

    private readonly Func<ISerialPort> portFactory;

    public SerialDebugService() : this(() => new SystemIoSerialPortAdapter()) { }

    // 测试入口：注入 fake ISerialPort 工厂（ISerialPort 为 internal，Stainer.Tests 经 InternalsVisibleTo 可访问）。
    internal SerialDebugService(Func<ISerialPort> portFactory) => this.portFactory = portFactory;

    public Task<SerialDebugExchangeResponse> ExchangeAsync(SerialDebugExchangeRequest request, CancellationToken cancellationToken = default)
        => Task.Run(() => Exchange(request, cancellationToken), cancellationToken);

    private SerialDebugExchangeResponse Exchange(SerialDebugExchangeRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var portName = (request.PortName ?? string.Empty).Trim();
        var text = request.Text ?? string.Empty;
        var sentBytes = Encoding.UTF8.GetBytes(text);
        var received = new List<byte>();

        if (string.IsNullOrWhiteSpace(portName))
        {
            return Fail("serial_port_name_required", "PortName is required.", request, startedAt, sentBytes, received);
        }

        var baudRate = request.BaudRate is > 0 and <= 1_000_000 ? request.BaudRate.Value : 115200;
        var dataBits = request.DataBits is >= 5 and <= 8 ? request.DataBits.Value : 8;
        var parity = MapParity(request.Parity);
        var stopBits = MapStopBits(request.StopBits);
        var timeoutMs = request.TimeoutMs is >= 50 and <= 10_000 ? request.TimeoutMs.Value : 1000;

        ISerialPort? port = null;
        try
        {
            port = portFactory();
            port.PortName = portName;
            port.BaudRate = baudRate;
            port.DataBits = dataBits;
            port.Parity = parity;
            port.StopBits = stopBits;
            port.ReadTimeout = ReadChunkTimeoutMs;
            port.WriteTimeout = 1000;
            port.Open();
            port.DiscardInBuffer();
            port.Write(sentBytes, 0, sentBytes.Length);

            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    received.Add((byte)port.ReadByte());
                }
                catch (TimeoutException)
                {
                    // 短读超时：环回下"收完即停"；串口助手模式下空等继续直到总 deadline。
                    if (received.Count > 0)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(
                "serial_exchange_failed",
                $"COM {portName} 通信失败：{ex.GetType().Name}: {ex.Message}",
                request,
                startedAt,
                sentBytes,
                received);
        }
        finally
        {
            try
            {
                if (port is not null)
                {
                    if (port.IsOpen)
                    {
                        port.Close();
                    }
                    port.Dispose();
                }
            }
            catch
            {
                // 忽略清理异常，不影响结果返回。
            }
        }

        var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var receivedArray = received.ToArray();
        return new SerialDebugExchangeResponse(
            true,
            "Succeeded",
            null,
            $"已向 {portName} 发送 {sentBytes.Length} 字节，接收 {receivedArray.Length} 字节。",
            text,
            Convert.ToHexString(sentBytes),
            Encoding.UTF8.GetString(receivedArray),
            Convert.ToHexString(receivedArray),
            durationMs);
    }

    private static SerialDebugExchangeResponse Fail(
        string errorCode, string message, SerialDebugExchangeRequest request, DateTimeOffset startedAt, byte[] sentBytes, List<byte> received)
    {
        var receivedArray = received.ToArray();
        return new SerialDebugExchangeResponse(
            false,
            "Failed",
            errorCode,
            message,
            request.Text ?? string.Empty,
            Convert.ToHexString(sentBytes),
            Encoding.UTF8.GetString(receivedArray),
            Convert.ToHexString(receivedArray),
            (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
    }

    private static Parity MapParity(string? value) => (value ?? "None").Trim().ToLowerInvariant() switch
    {
        "odd" => Parity.Odd,
        "even" => Parity.Even,
        "mark" => Parity.Mark,
        "space" => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(string? value) => (value ?? "One").Trim().ToLowerInvariant() switch
    {
        "two" => StopBits.Two,
        "onepointfive" => StopBits.OnePointFive,
        _ => StopBits.One
    };
}
