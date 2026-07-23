using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Requests;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

// SerialDebugService 用 fake ISerialPort 验证收发逻辑（不打开真实 COM 口）。
// ISerialPort 为 internal，Stainer.Tests 经 InternalsVisibleTo 可访问。
public sealed class SerialDebugServiceTests
{
    [Fact]
    public async Task Exchange_loops_back_sent_bytes_and_succeeds()
    {
        var fake = new FakeSerialPort();
        var service = new SerialDebugService(() => fake);

        var result = await service.ExchangeAsync(
            new SerialDebugExchangeRequest("cmd-loopback", "COM8", 115200, 8, "None", "One", "PING", 500, "test"),
            CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Equal("Succeeded", result.Status);
        Assert.Equal("PING", result.SentText);
        Assert.Equal("PING", result.ReceivedText);
        Assert.Equal<byte>(Encoding.UTF8.GetBytes("PING"), fake.Written);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task Exchange_fails_closed_when_port_open_fails()
    {
        var fake = new FakeSerialPort { OpenThrows = true };
        var service = new SerialDebugService(() => fake);

        var result = await service.ExchangeAsync(
            new SerialDebugExchangeRequest("cmd-open-fail", "COM99", null, null, null, null, "PING", null, "test"),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("serial_exchange_failed", result.ErrorCode);
        Assert.Contains("COM99", result.Message);
    }

    [Fact]
    public async Task Exchange_requires_port_name()
    {
        var service = new SerialDebugService(() => new FakeSerialPort());

        var result = await service.ExchangeAsync(
            new SerialDebugExchangeRequest("cmd-no-port", "   ", null, null, null, null, "PING", null, "test"),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("serial_port_name_required", result.ErrorCode);
    }

    [Fact]
    public async Task Exchange_accepts_empty_text_and_loops_back_nothing()
    {
        var fake = new FakeSerialPort();
        var service = new SerialDebugService(() => fake);

        var result = await service.ExchangeAsync(
            new SerialDebugExchangeRequest("cmd-empty", "COM8", null, null, null, null, "", 200, "test"),
            CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Equal("", result.ReceivedText);
        Assert.Empty(fake.Written);
    }

    // 环回 fake：Write 记录并入队，ReadByte 回放出队字节，队列空抛 TimeoutException（模拟短读超时）。
    private sealed class FakeSerialPort : ISerialPort
    {
        private readonly Queue<byte> echoQueue = new();

        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public bool IsOpen { get; private set; }
        public int BytesToRead => echoQueue.Count;
        public List<byte> Written { get; } = [];
        public bool OpenThrows { get; set; }

        public void Open()
        {
            if (OpenThrows)
            {
                throw new IOException("open failed");
            }
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public void Write(byte[] buffer, int offset, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var value = buffer[offset + index];
                Written.Add(value);
                echoQueue.Enqueue(value);
            }
        }

        public int ReadByte()
        {
            if (echoQueue.Count == 0)
            {
                throw new TimeoutException();
            }
            return echoQueue.Dequeue();
        }

        public void DiscardInBuffer() => echoQueue.Clear();

        public void Dispose() { }
    }
}
