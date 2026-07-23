namespace Stainer.Web.Application.Devices.SoconBridge;

// 单次请求的传输/协议状态，独立于业务 Outcome。
// 覆盖只读客户端契约要求区分的每一种失败模式。
//
// 连接超时（ConnectTimeout）与请求取消（Canceled）必须可区分：
//   - ConnectTimeout / ResponseTimeout：由客户端超时截止时间判定；
//   - Canceled：由调用方 CancellationToken 触发。
// 客户端不得用内部 linked CTS 把超时误判成取消（见 Dcr55SerialTransport 的同一约束）。
public enum SoconBridgeExchangeStatus
{
    // 收到并解析了完整且 requestId 匹配的响应。此时应进一步查看 Outcome。
    Completed = 0,

    // 命名管道端点根本无法接入（访问被拒 / 硬性端点错误，而非超时）。
    PipeUnavailable = 1,

    // 连接（管道接入）在 ConnectTimeout 内未完成。
    ConnectTimeout = 2,

    // 响应在 ResponseTimeout 内未收到。
    ResponseTimeout = 3,

    // 调用方 CancellationToken 被取消（与任何内部超时不同）。
    Canceled = 4,

    // 服务端在交付完整响应前关闭了连接。
    Disconnected = 5,

    // 收到了响应，但违反了帧/编码/长度/requestId 规则。此时应查看 ProtocolError。
    ProtocolError = 6
}

// SoconBridgeExchangeStatus.ProtocolError 的细粒度原因。
public enum SoconBridgeProtocolErrorKind
{
    // 4 字节长度前缀缺失、为负或其它非法。
    InvalidLength = 0,

    // 负载不是合法 UTF-8。
    InvalidUtf8 = 1,

    // 负载是合法 UTF-8，但不是合法 JSON 或不符合响应结构。
    InvalidJson = 2,

    // 声明的响应长度超过 MaxResponseBytes。
    OversizeResponse = 3,

    // 响应 requestId 与请求 requestId 不匹配。
    RequestIdMismatch = 4
}
