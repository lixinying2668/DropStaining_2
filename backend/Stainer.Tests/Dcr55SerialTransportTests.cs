using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Devices;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

// DCR55-02：真实串口 Transport 的离线单元测试。
// 通过注入 ISerialPort 的可控假实现，验证“单次触发 + 接收 + 超时/断开/打开失败”等
// 关键路径，且不打开任何真实 COM 口。
public sealed class Dcr55SerialTransportTests
{
    private static readonly Dcr55ConnectionOptions Configured = new()
    {
        Port = "TEST-COM",
        BaudRate = 115200,
        DataBits = 8,
        Parity = Dcr55Parity.None,
        StopBits = Dcr55StopBits.One
    };

    [Fact]
    public void Transport_name_and_configuration_match_dcr55_connection_options()
    {
        var transport = CreateTransport(FakeSerialPort.FromText("X\r\n"));

        Assert.Equal("dcr55-serial", transport.Name);
        Assert.True(transport.IsConfigured);

        var unconfigured = new Dcr55SerialTransport(new Dcr55ConnectionOptions());
        Assert.False(unconfigured.IsConfigured);
    }

    [Fact]
    public async Task Receive_writes_single_trigger_command_and_returns_crlf_barcode()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        var transport = CreateTransport(port);

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55);

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        var rawText = Encoding.ASCII.GetString(result.ResponseChunks.SelectMany(c => c).ToArray());
        Assert.Equal("ABC123\r\n", rawText);

        // 必须写入单次触发命令（RDCMXEV1,P11,P20 + CR），且禁止连续触发。
        var written = Encoding.ASCII.GetString(port.WrittenBytes);
        Assert.Equal(Dcr55Protocol.SingleTriggerCommandText + "\r", written);
        Assert.DoesNotContain(Dcr55Protocol.ContinuousTriggerCommandText, written);
        // 触发次数必须为 1（禁止自动循环）。
        Assert.Single(port.WriteCalls);
        Assert.True(port.OpenCalled);
        Assert.True(port.CloseCalled);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Open_failure_returns_disconnected_without_sending_any_command()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        port.ThrowOnOpen = new IOException("COM not present");
        var transport = CreateTransport(port);

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55);

        Assert.Equal(DeviceByteTransportStatuses.Disconnected, result.Status);
        Assert.Equal("dcr55_serial_open_failed", result.ErrorCode);
        Assert.Empty(port.WriteCalls);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Read_timeout_returns_timed_out_status()
    {
        // 假串口持续抛出 TimeoutException，模拟设备无返回。
        var port = FakeSerialPort.AlwaysTimeout();
        var transport = CreateTransport(port, receiveTimeout: TimeSpan.FromMilliseconds(200));

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55);

        Assert.Equal(DeviceByteTransportStatuses.TimedOut, result.Status);
        Assert.Equal("dcr55_serial_read_timeout", result.ErrorCode);
        Assert.Single(port.WriteCalls);
    }

    [Fact]
    public async Task End_of_stream_during_read_returns_disconnected()
    {
        var port = FakeSerialPort.ReturningEndOfStream();
        var transport = CreateTransport(port, receiveTimeout: TimeSpan.FromMilliseconds(200));

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55);

        Assert.Equal(DeviceByteTransportStatuses.Disconnected, result.Status);
        Assert.Equal("dcr55_serial_disconnected", result.ErrorCode);
    }

    [Fact]
    public async Task Non_dcr55_endpoint_is_rejected_and_does_not_open_port()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        var transport = CreateTransport(port);

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.MainController);

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("dcr55_serial_endpoint_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    [Fact]
    public async Task Exchange_is_not_supported_and_does_not_touch_the_port()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(DeviceByteTransportEndpoints.Dcr55, "trigger", []));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("dcr55_serial_exchange_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Unconfigured_port_fails_closed_without_attempting_io()
    {
        var port = FakeSerialPort.FromText("ABC123\r\n");
        var transport = new Dcr55SerialTransport(
            new Dcr55ConnectionOptions(),
            () => port,
            TimeSpan.FromMilliseconds(200));

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55);

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("dcr55_serial_not_configured", result.ErrorCode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    private static Dcr55SerialTransport CreateTransport(FakeSerialPort port, TimeSpan? receiveTimeout = null) =>
        new(Configured, () => port, receiveTimeout ?? TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

    private sealed class FakeSerialPort : ISerialPort
    {
        // DCR55 真实语义：设备只有在“收到触发命令之后”才会输出条码。
        // 因此本假实现用 responseAfterWrite 控制可读性：
        // - Write() 被调用（即发出触发命令）后才允许 ReadByte() 取出预置响应；
        // - DiscardInBuffer() 模拟“清空触发前串口里的陈旧输入”，对本假实现中
        //   代表“触发后设备返回”的 response 队列无影响（保持为空操作）。
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

        // 模拟清空触发前的陈旧输入；本假实现中不触碰触发后的响应队列。
        public void DiscardInBuffer() { }

        public void Dispose() => Disposed = true;
    }
}