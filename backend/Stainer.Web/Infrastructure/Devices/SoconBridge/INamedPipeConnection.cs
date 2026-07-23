using System.IO.Pipes;

namespace Stainer.Web.Infrastructure.Devices.SoconBridge;

// Named pipe 客户端连接抽象。仅在本项目 (Infrastructure/Devices/SoconBridge) 内使用，
// Application 层永远不会直接接触 NamedPipeClientStream，也不会直接打开管道。
//
// 存在意义（对照 ISerialPort / SystemIoSerialPortAdapter 的同一手法）：让 NamedPipeSoconBridgeClient
// 的“连接建立”阶段可在离线单元测试中被假实现替换——真实管道服务端是否存在、是否及时应答
// 属于不可控环境因素，必须可注入。连接建立后的读写仍直接面向 Stream，由客户端自行控制
// 截止时间与取消语义。
internal interface INamedPipeConnection : IDisposable, IAsyncDisposable
{
    // 发起管道接入；成功返回后 DuplexStream 可用。
    //
    // 异常契约（严格对应 NamedPipeSoconBridgeClient.SendAsync 的状态映射）：
    //   - 超时（在 timeout 内未完成接入）-> TimeoutException；
    //   - 调用方 CancellationToken 触发 -> OperationCanceledException；
    //   - 端点硬性不可用（访问被拒 / 管道不存在或被关闭等）-> UnauthorizedAccessException / IOException。
    // 实现不得使用内部 linked CTS 把超时折叠成取消（见 Dcr55SerialTransport.ReadFrameAsync 的同一约束）。
    Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);

    // 仅用于诊断/旁路判定，不参与帧收发的主路径。
    bool IsConnected { get; }

    // 接入成功后用于帧读写的双向流；NamedPipeClientStream 本身即是 Stream。
    Stream DuplexStream { get; }
}

// 连接工厂抽象。每次请求由客户端显式 Create -> 用完即 DisposeAsync，对应服务端
// “一连接一请求一响应后关闭”的语义；工厂本身无状态、可跨调用复用。
internal interface INamedPipeConnectionFactory
{
    INamedPipeConnection Create(string serverName, string pipeName);
}

// 生产工厂：返回包装真实 NamedPipeClientStream 的连接。
internal sealed class NamedPipeConnectionFactory : INamedPipeConnectionFactory
{
    public INamedPipeConnection Create(string serverName, string pipeName) =>
        new NamedPipeClientConnection(serverName, pipeName);
}

// 真实 NamedPipeClientStream 的瘦封装。
//
// 超时与取消的严格区分（与 Dcr55SerialTransport 一致）：
//   - ConnectAsync(cancellationToken).WaitAsync(timeout, cancellationToken)
//     WaitAsync(TimeSpan, CancellationToken) 在 timeout 到期时抛 TimeoutException，
//     在调用方 token 取消时抛 OperationCanceledException，两者天然可区分；
//   - 绝不引入内部 linked CTS（其 CancellationTokenSource.CancelAfter 会把超时伪装成取消，
//     在负载下抖动并丢失 ConnectTimeout vs Canceled 的可区分性）。
internal sealed class NamedPipeClientConnection : INamedPipeConnection
{
    private readonly NamedPipeClientStream pipe;

    public NamedPipeClientConnection(string serverName, string pipeName)
    {
        // PipeDirection.InOut 与服务端 BridgeHost 一致；单实例本机管道。
        this.pipe = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut);
    }

    public bool IsConnected => pipe.IsConnected;

    public Stream DuplexStream => pipe;

    public Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        pipe.ConnectAsync(cancellationToken).WaitAsync(timeout, cancellationToken);

    public void Dispose() => pipe.Dispose();

    public ValueTask DisposeAsync() => pipe.DisposeAsync();
}
