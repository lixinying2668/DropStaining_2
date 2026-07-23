namespace Stainer.Web.Application.ReadModels;

// 调试栏「通信测试」响应：回显发送/接收字节（文本 + hex）与耗时，便于判断 COM 通路是否通畅。
public sealed record SerialDebugExchangeResponse(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    string SentText,
    string SentHex,
    string ReceivedText,
    string ReceivedHex,
    int DurationMs);
