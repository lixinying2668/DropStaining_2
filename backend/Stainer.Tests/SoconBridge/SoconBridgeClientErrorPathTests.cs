using System.Buffers.Binary;
using System.Text;
using Stainer.Web.Application.Devices.SoconBridge;
using Stainer.Web.Infrastructure.Devices.SoconBridge;

namespace Stainer.Tests.SoconBridge;

/// <summary>
/// NamedPipeSoconBridgeClient 的传输/协议错误路径与连接生命周期测试。
/// 读/解析类错误使用真实 FakeSoconBridgePipeServer（真实命名管道 I/O）；
/// 连接阶段不可控场景（端点不可用 / 接入超时 / 敏感异常脱敏）通过 internal
/// INamedPipeConnectionFactory 缝隙注入假实现，保证确定性和可重复。
/// 全程不启动真实 Bridge、不加载 SDK、不打开 COM、不连接机械臂。
/// </summary>
public class SoconBridgeClientErrorPathTests
{
    // ---------------------------------------------------------------
    // 连接阶段（注入假工厂，确定性）
    // ---------------------------------------------------------------

    [Fact]
    public async Task 连接被拒_UnauthorizedAccessException_解析为PipeUnavailable()
    {
        var client = NewClientWithConnectFactory(
            new FakeThrowingConnectionFactory(new UnauthorizedAccessException("ignored")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await client.PingAsync(cts.Token);

        Assert.Equal(SoconBridgeExchangeStatus.PipeUnavailable, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Outcome);
        // 固定安全消息，不携带任何运行期变量或异常文本。
        Assert.Equal("Named pipe endpoint is unavailable.", result.ErrorMessage);
    }

    [Fact]
    public async Task 连接IOException_同样解析为PipeUnavailable()
    {
        var client = NewClientWithConnectFactory(
            new FakeThrowingConnectionFactory(new IOException("ignored")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await client.PingAsync(cts.Token);

        Assert.Equal(SoconBridgeExchangeStatus.PipeUnavailable, result.Status);
        Assert.Equal("Named pipe endpoint is unavailable.", result.ErrorMessage);
    }

    [Fact]
    public async Task 接入挂起_在ConnectTimeout后解析为ConnectTimeout()
    {
        // 假连接复用真实连接的 ConnectAsync 超时机制：Task.Delay(Infinite).WaitAsync(timeout, ct)，
        // 在客户端配置的短 ConnectTimeout 后抛 TimeoutException。
        var client = new NamedPipeSoconBridgeClient(
            new SoconBridgeClientOptions
            {
                PipeName = "Stainer.SoconBridge.Tests.hanging",
                ConnectTimeout = TimeSpan.FromMilliseconds(200),
                ResponseTimeout = TimeSpan.FromSeconds(5)
            },
            new FakeHangingConnectionFactory());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await client.PingAsync(cts.Token);

        Assert.Equal(SoconBridgeExchangeStatus.ConnectTimeout, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Named pipe connect did not complete within the configured connect timeout.",
            result.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public async Task 本地异常不泄漏_ErrorMessage不含路径与原始异常文本(Type exceptionType)
    {
        // 构造包含“敏感路径 + 管道内部细节”的本地异常，确认对外 ErrorMessage 不泄漏。
        const string SensitivePath = @"C:\Users\secret\SoconBridge.config.local.json";
        const string SensitiveDetail = "pipe internal handle 0x42 / sessionKey=ABCDEF";
        var rawException = CreateException(exceptionType, $"Access to '{SensitivePath}' denied: {SensitiveDetail}");

        var client = NewClientWithConnectFactory(new FakeThrowingConnectionFactory(rawException));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await client.PingAsync(cts.Token);

        Assert.Equal(SoconBridgeExchangeStatus.PipeUnavailable, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(SensitivePath, result.ErrorMessage);
        Assert.DoesNotContain("secret", result.ErrorMessage);
        Assert.DoesNotContain("config.local.json", result.ErrorMessage);
        Assert.DoesNotContain(SensitiveDetail, result.ErrorMessage);
        Assert.DoesNotContain("0x42", result.ErrorMessage);
        // 整条原始异常消息不得原样出现。
        Assert.DoesNotContain(rawException.Message, result.ErrorMessage);
        // 仍给出稳定错误码与固定安全消息。
        Assert.Equal("Named pipe endpoint is unavailable.", result.ErrorMessage);
    }

    // ---------------------------------------------------------------
    // 读/解析阶段（真实 FakeSoconBridgePipeServer）
    // ---------------------------------------------------------------

    [Fact]
    public async Task 服务端Hold_在ResponseTimeout后解析为ResponseTimeout()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = new NamedPipeSoconBridgeClient(new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromMilliseconds(400)
        });
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.Hold(), serverCts.Token);
        var result = await client.PingAsync(CancellationToken.None); // 由客户端内部 ResponseTimeout 终结
        serverCts.Cancel(); // 解除服务端 Hold
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ResponseTimeout, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Response was not received within the configured response timeout.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task 调用方取消_解析为Canceled_且与ResponseTimeout可区分()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        // 故意把内部 ResponseTimeout 设得很长，确保只有调用方取消才会终结请求。
        var client = new NamedPipeSoconBridgeClient(new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(30)
        });
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));

        // 同一个 cts 同时驱动客户端与服务端 Hold：取消时两端一起结束，不挂起。
        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.Hold(), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.Canceled, result.Status);
        Assert.NotEqual(SoconBridgeExchangeStatus.ResponseTimeout, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("Response read was canceled by the caller.", result.ErrorMessage);
    }

    [Fact]
    public async Task 服务端响应前断开_解析为Disconnected()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.Disconnect(), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.Disconnected, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task 服务端帧中段断开_解析为Disconnected()
    {
        // 服务端写 4 字节长度前缀（声明 100 字节）后立即断开，不发送载荷：
        // 客户端读到合法长度，随后读载荷遇到 EOF -> Disconnected。
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.RespondOversized(100), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.Disconnected, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task 长度前缀非正_解析为InvalidLength(int declaredLength)
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var rawPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(rawPrefix, declaredLength);

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.RespondRaw(rawPrefix), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ProtocolError, result.Status);
        Assert.Equal(SoconBridgeProtocolErrorKind.InvalidLength, result.ProtocolError);
        Assert.False(result.IsSuccess);
        Assert.Equal("Response length prefix is invalid.", result.ErrorMessage);
    }

    [Fact]
    public async Task 响应超过MaxResponseBytes_解析为OversizeResponse()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        // 把客户端上限设得很小；服务端用 RespondOversized 只写 4 字节长度前缀、声明一个超限帧，
        // 不写载荷——避免“客户端读完前缀即关闭、服务端写整帧撞上 broken pipe”的竞态。
        var client = new NamedPipeSoconBridgeClient(new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            MaxResponseBytes = 8
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(
            _ => SoconBridgeServerAction.RespondOversized(1000),
            cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ProtocolError, result.Status);
        Assert.Equal(SoconBridgeProtocolErrorKind.OversizeResponse, result.ProtocolError);
        Assert.Equal("Response exceeds the maximum allowed response size.", result.ErrorMessage);
    }

    [Fact]
    public async Task 载荷非UTF8_解析为InvalidUtf8()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // 合法长度前缀 + 非法 UTF-8 载荷。
        var badPayload = new byte[] { 0xFF, 0xFE, 0xFD, 0x00 };
        var framed = SoconBridgeFrame.WriteFrame(badPayload);

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.RespondRaw(framed), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ProtocolError, result.Status);
        Assert.Equal(SoconBridgeProtocolErrorKind.InvalidUtf8, result.ProtocolError);
        Assert.Equal("Response payload is not valid UTF-8.", result.ErrorMessage);
    }

    [Fact]
    public async Task 载荷非合法JSON_解析为InvalidJson()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // 合法 UTF-8 但非 JSON：写入带长度前缀的帧。
        var framed = SoconBridgeFrame.WriteFrame(Encoding.UTF8.GetBytes("{not valid json"));

        var serveTask = server.ServeAsync(_ => SoconBridgeServerAction.RespondRaw(framed), cts.Token);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ProtocolError, result.Status);
        Assert.Equal(SoconBridgeProtocolErrorKind.InvalidJson, result.ProtocolError);
        Assert.Equal("Response payload is not valid JSON.", result.ErrorMessage);
    }

    // ---------------------------------------------------------------
    // 生命周期：资源释放后可再次发起请求
    // ---------------------------------------------------------------

    [Fact]
    public async Task 失败后释放资源_同一客户端可再次发起请求并成功()
    {
        // 真实管道：同一个 FakeSoconBridgePipeServer 在 ServeAsync 的 finally 中 Disconnect，
        // 之后可再次 WaitForConnection 接受第二个连接——对应真实 Bridge“一连接一请求”循环。
        await using var server = FakeSoconBridgePipeServer.Start();
        var client = NewClient(server.PipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 第一轮：服务端响应前断开 -> Disconnected。
        var serve1 = server.ServeAsync(_ => SoconBridgeServerAction.Disconnect(), cts.Token);
        var first = await client.PingAsync(cts.Token);
        await serve1;
        Assert.Equal(SoconBridgeExchangeStatus.Disconnected, first.Status);

        // 第二轮：同一客户端、同一管道名，服务端接受第二个连接并正常响应 -> 成功。
        var serve2 = server.ServeAsync(
            req => SoconBridgeServerAction.RespondJson(SoconBridgeFrame.Pong(req.RequestId!)),
            cts.Token);
        var second = await client.PingAsync(cts.Token);
        await serve2;

        Assert.True(second.IsSuccess);
        Assert.Equal(SoconBridgeExchangeStatus.Completed, second.Status);
    }

    // ---------------------------------------------------------------
    // 辅助
    // ---------------------------------------------------------------

    private static NamedPipeSoconBridgeClient NewClient(string pipeName) => new(new SoconBridgeClientOptions
    {
        PipeName = pipeName,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        ResponseTimeout = TimeSpan.FromSeconds(5)
    });

    private static NamedPipeSoconBridgeClient NewClientWithConnectFactory(INamedPipeConnectionFactory factory) =>
        new(new SoconBridgeClientOptions
        {
            PipeName = "Stainer.SoconBridge.Tests.fake",
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(5)
        }, factory);

    private static Exception CreateException(Type type, string message) =>
        type == typeof(UnauthorizedAccessException)
            ? new UnauthorizedAccessException(message)
            : new IOException(message);

    // 连接即抛出固定异常（端点不可用 / 敏感异常脱敏测试）。
    private sealed class FakeThrowingConnectionFactory : INamedPipeConnectionFactory
    {
        private readonly Exception _exception;
        public FakeThrowingConnectionFactory(Exception exception) => _exception = exception;
        public INamedPipeConnection Create(string serverName, string pipeName) =>
            new FakeThrowingConnection(_exception);
    }

    private sealed class FakeThrowingConnection : INamedPipeConnection
    {
        private readonly Exception _exception;
        public FakeThrowingConnection(Exception exception) => _exception = exception;
        public Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromException(_exception);
        public bool IsConnected => false;
        public Stream DuplexStream => throw new InvalidOperationException("Connection never established.");
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // 连接永远挂起，直到客户端 ConnectTimeout（经 WaitAsync）触发 TimeoutException。
    private sealed class FakeHangingConnectionFactory : INamedPipeConnectionFactory
    {
        public INamedPipeConnection Create(string serverName, string pipeName) => new FakeHangingConnection();
    }

    private sealed class FakeHangingConnection : INamedPipeConnection
    {
        public Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken).WaitAsync(timeout, cancellationToken);
        public bool IsConnected => false;
        public Stream DuplexStream => throw new InvalidOperationException("Connection never established.");
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
