namespace Stainer.Web.Application.Requests;

// 调试栏「通信测试」请求：真实打开指定 COM 口收发测试字节（原始字节，不经主控协议/白名单）。
// 量程校验在 SerialDebugService 内完成；非法值回退安全默认（115200 / 8 / None / One / 1000ms）。
public sealed record SerialDebugExchangeRequest(
    string CommandId,
    string? PortName,
    int? BaudRate,
    int? DataBits,
    string? Parity,
    string? StopBits,
    string? Text,
    int? TimeoutMs,
    string Reason);
