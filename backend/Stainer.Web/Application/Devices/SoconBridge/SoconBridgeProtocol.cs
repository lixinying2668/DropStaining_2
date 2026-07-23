namespace Stainer.Web.Application.Devices.SoconBridge;

// Stainer.SoconBridge 服务端常量的客户端镜像（BridgeHost / BridgeRequestProcessor）。
// 服务端源码是最终依据，这些值必须与服务端保持一致；不得仅凭文档推断。
//
// 传输约定（与 BridgeHost.Run 一致）：
//   - 仅 Windows Named Pipe，PipeDirection.InOut，单实例服务端；
//   - 每个连接处理一个请求、返回一个响应后关闭（客户端每次请求新建连接）；
//   - 帧格式 = 4 字节小端有符号长度前缀 + UTF-8 JSON 负载；
//   - 请求体上限 64 KB；服务端不对响应体声明上限，故客户端自行防御性限制。
public static class SoconBridgeTransport
{
    // 对应 BridgeHost.DefaultPipeName。
    public const string DefaultPipeName = "Stainer.SoconBridge";

    // 对应 BridgeHost.MaxRequestBytes。客户端请求负载始终很小，但必须遵守同一上限。
    public const int MaxRequestBytes = 64 * 1024;

    // 客户端防御性响应帧上限。服务端不声明响应上限，客户端必须自行限制读取长度，
    // 避免被异常或恶意服务端的超大长度前缀拖入无界内存分配。
    public const int DefaultMaxResponseBytes = 1024 * 1024;
}

// IPC 命令字符串（对应 BridgeRequestProcessor 内的命令常量）。
// 本轮只读客户端只暴露只读子集；不得新增或修改服务端命令。
// Open 请求只发送协议要求的内容（requestId + command），COM/波特率/sdkDirectory 等
// 硬件配置由 Bridge 本机配置负责，客户端不复制、不发送。
public static class SoconBridgeCommands
{
    public const string Ping = "Ping";
    public const string GetBridgeStatus = "GetBridgeStatus";
    public const string OpenConfiguredReadOnlySession = "OpenConfiguredReadOnlySession";
    public const string CloseConfiguredReadOnlySession = "CloseConfiguredReadOnlySession";
}
