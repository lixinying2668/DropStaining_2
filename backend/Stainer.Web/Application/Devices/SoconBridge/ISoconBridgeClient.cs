namespace Stainer.Web.Application.Devices.SoconBridge;

// Stainer.SoconBridge IPC 进程（命名管道）的只读客户端契约。
//
// 连接生命周期（严格对应服务端 BridgeHost.Run：每个连接处理一个请求、返回一个响应后关闭）：
//   - 每次调用都新建一个连接，发送一帧请求，读取一帧响应，然后释放管道及相关资源；
//   - 客户端绝不自动启动 Bridge、绝不自动重试、绝不自动重连、绝不自动发送 Close；
//   - COM/波特率/sdkDirectory 等硬件配置仍由 Bridge 本机配置负责，客户端不发送；
//   - 每次请求都生成唯一 requestId，并校验响应 requestId、限制读取长度。
//
// 所有方法通过 SoconBridgeResponseResult 区分 Success/Failure/Blocked 业务结果以及传输/协议错误；
// 调用方取消以 ExchangeStatus.Canceled 暴露（与连接/响应超时不同）。
public interface ISoconBridgeClient
{
    Task<SoconBridgeResponseResult> OpenConfiguredReadOnlySessionAsync(CancellationToken cancellationToken);

    Task<SoconBridgeResponseResult> CloseConfiguredReadOnlySessionAsync(CancellationToken cancellationToken);

    Task<SoconBridgeResponseResult> GetBridgeStatusAsync(CancellationToken cancellationToken);

    Task<SoconBridgeResponseResult> PingAsync(CancellationToken cancellationToken);
}
