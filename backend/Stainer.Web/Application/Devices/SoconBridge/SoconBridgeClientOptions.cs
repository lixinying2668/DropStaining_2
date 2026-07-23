namespace Stainer.Web.Application.Devices.SoconBridge;

// 只读 SoconBridge 命名管道客户端的配置。
// 本轮只定义契约与 Infrastructure 实现；DI/API/Real 流程接入属于后续阶段，不在本次范围。
// 所有值都可在配置中覆盖，默认值与服务端常量一致。
public sealed class SoconBridgeClientOptions
{
    // 必须与 BridgeHost.DefaultPipeName 一致。
    public string PipeName { get; set; } = SoconBridgeTransport.DefaultPipeName;

    // "." 表示本机；Bridge 是单实例本机进程，不使用远程端点。
    public string ServerName { get; set; } = ".";

    // 独立的连接（管道接入）超时。
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // 独立的响应（读取）超时。
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // 单个响应帧的客户端防御性上限。
    public int MaxResponseBytes { get; set; } = SoconBridgeTransport.DefaultMaxResponseBytes;
}
