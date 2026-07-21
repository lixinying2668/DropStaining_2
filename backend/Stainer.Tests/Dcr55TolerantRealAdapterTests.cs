using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Devices;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

// Dcr55TolerantRealAdapter 的离线单元测试（【新增】文件，不改任何现有代码）。
// 通过注入 ISerialPort 的可控假实现，验证“单次触发 + 容错读帧（\r/\r\n/\n/无结束符）+
// 超时/断开/打开失败/未配置”等关键路径，且不打开任何真实 COM 口。
public sealed class Dcr55TolerantRealAdapterTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 7, 8, 1, 2, 3, TimeSpan.Zero);
    private static readonly Dcr55ConnectionOptions Configured = new()
    {
        Port = "TEST-COM",
        BaudRate = 115200,
        DataBits = 8,
        Parity = Dcr55Parity.None,
        StopBits = Dcr55StopBits.One
    };

    [Fact]
    public async Task Unconfigured_options_fail_closed_without_opening_a_port()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        var adapter = CreateAdapter(port, new Dcr55ConnectionOptions());

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.NotConfigured, result.Status);
        Assert.Null(result.Barcode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    [Fact]
    public async Task Cr_only_response_returns_success_barcode()
    {
        // 现场 DCR55 实测：触发后条码以单个 \r 结尾（现有 Dcr55RealAdapter 会超时，本类型应成功）。
        var port = FakeSerialPort.FromText("ABC123\r");
        var adapter = CreateAdapter(port);

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Success, result.Status);
        Assert.Equal("ABC123", result.Barcode);
        Assert.Single(port.WriteCalls);
        Assert.Equal(Dcr55Protocol.SingleTriggerCommandText + "\r", Encoding.ASCII.GetString(port.WrittenBytes));
    }

    [Theory]
    [InlineData("ABC123\r\n")]
    [InlineData("ABC123\r")]
    [InlineData("ABC123\n")]
    [InlineData("ABC123")]
    public async Task Tolerates_all_terminator_styles(string response)
    {
        var port = FakeSerialPort.FromText(response);
        var adapter = CreateAdapter(port);

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Success, result.Status);
        Assert.Equal("ABC123", result.Barcode);
    }

    [Fact]
    public async Task Writes_single_trigger_command_once_and_never_continuous()
    {
        var port = FakeSerialPort.FromText("ABC123\r");
        var adapter = CreateAdapter(port);

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Success, result.Status);
        var written = Encoding.ASCII.GetString(port.WrittenBytes);
        Assert.Equal(Dcr55Protocol.SingleTriggerCommandText + "\r", written);
        Assert.DoesNotContain(Dcr55Protocol.ContinuousTriggerCommandText, written);
        Assert.Single(port.WriteCalls);          // 触发次数必须为 1（禁止连续触发/自动循环）
        Assert.True(port.OpenCalled);
        Assert.True(port.CloseCalled);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Open_failure_returns_disconnected_without_sending_any_command()
    {
        var port = FakeSerialPort.FromText("ABC123\r");
        port.ThrowOnOpen = new IOException("COM not present");
        var adapter = CreateAdapter(port);

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Disconnected, result.Status);
        Assert.Empty(port.WriteCalls);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task No_data_within_receive_timeout_returns_timeout()
    {
        var port = FakeSerialPort.AlwaysTimeout();
        var adapter = CreateAdapter(port, receiveTimeout: TimeSpan.FromMilliseconds(200));

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Timeout, result.Status);
        Assert.Null(result.Barcode);
        Assert.Single(port.WriteCalls);          // 触发命令已发，但设备无返回
    }

    [Fact]
    public async Task End_of_stream_during_read_returns_disconnected()
    {
        var port = FakeSerialPort.ReturningEndOfStream();
        var adapter = CreateAdapter(port, receiveTimeout: TimeSpan.FromMilliseconds(200));

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.Disconnected, result.Status);
    }

    [Fact]
    public async Task Non_ascii_byte_in_response_is_invalid()
    {
        // "ABC" + 0x80（>0x7F）+ CR：应判为非法响应。
        var port = FakeSerialPort.FromBytes(0x41, 0x42, 0x43, 0x80, 0x0D);
        var adapter = CreateAdapter(port);

        var result = await adapter.ReceiveScanAsync();

        Assert.Equal(Dcr55ScanStatus.InvalidResponse, result.Status);
        Assert.Null(result.Barcode);
    }

    private static Dcr55TolerantRealAdapter CreateAdapter(
        FakeSerialPort port, Dcr55ConnectionOptions? options = null, TimeSpan? receiveTimeout = null) =>
        new(
            options ?? Configured,
            () => port,
            receiveTimeout ?? TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50),
            new FixedTimeProvider(ObservedAt));

    // 与 Dcr55SerialTransportTests 中的 FakeSerialPort 同语义：
    // 只有在 Write()（发出触发命令）后才允许读出预置响应，未触发即读按 ReadTimeout 抛 TimeoutException。
    private sealed class FakeSerialPort : ISerialPort
    {
        private readonly Queue<int> response = new();
        private readonly bool alwaysTimeout;
        private readonly bool endOfStream;
        private bool responseAfterWrite;

        private FakeSerialPort(IEnumerable<int> bytes, bool alwaysTimeout, bool endOfStream)
        {
            foreach (var b in bytes)
            {
                response.Enqueue(b);
            }

            this.alwaysTimeout = alwaysTimeout;
            this.endOfStream = endOfStream;
        }

        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public bool IsOpen { get; private set; }
        public int BytesToRead => response.Count;
        public List<byte[]> WriteCalls { get; } = [];
        public byte[] WrittenBytes => WriteCalls.SelectMany(c => c).ToArray();
        public bool OpenCalled { get; private set; }
        public bool CloseCalled { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? ThrowOnOpen { get; set; }

        public static FakeSerialPort FromText(string text) =>
            new(Encoding.ASCII.GetBytes(text).Select(b => (int)b), false, false);

        public static FakeSerialPort FromBytes(params int[] bytes) => new(bytes, false, false);

        public static FakeSerialPort AlwaysTimeout() => new([], true, false);

        public static FakeSerialPort ReturningEndOfStream() => new([], false, true);

        public void Open()
        {
            OpenCalled = true;
            if (ThrowOnOpen is not null)
            {
                throw ThrowOnOpen;
            }

            IsOpen = true;
        }

        public void Close()
        {
            CloseCalled = true;
            IsOpen = false;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            var chunk = new byte[count];
            Array.Copy(buffer, offset, chunk, 0, count);
            WriteCalls.Add(chunk);
            // 触发命令已发送：此后设备的响应字节可被读取。
            responseAfterWrite = true;
        }

        public int ReadByte()
        {
            // 未触发即读：设备尚未返回任何数据，与真实串口一致按 ReadTimeout 抛 TimeoutException。
            if (!responseAfterWrite || alwaysTimeout)
            {
                throw new TimeoutException();
            }

            if (response.Count > 0)
            {
                return response.Dequeue();
            }

            if (endOfStream)
            {
                return -1;
            }

            throw new TimeoutException();
        }

        public void DiscardInBuffer() { }

        public void Dispose() => Disposed = true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
