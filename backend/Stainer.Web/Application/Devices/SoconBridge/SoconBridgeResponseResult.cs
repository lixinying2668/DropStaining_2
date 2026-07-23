namespace Stainer.Web.Application.Devices.SoconBridge;

// 单次 Bridge IPC 请求的不可变结果。只能由 Infrastructure 实现构造。
//
// 可区分性契约（任务要求的每一类都可通过本对象观测）：
//   - Completed + Outcome ∈ {Success, Failure, Blocked}：收到并解析了合法响应；按 Outcome 分支。
//   - PipeUnavailable / ConnectTimeout / ResponseTimeout / Canceled / Disconnected / ProtocolError：
//     传输/协议失败；此时 Outcome 为 null，IsSuccess / IsBlocked / IsFailure 均为 false。
//
// Blocked 与业务 Failure 永远不会被报告为 Success，也不会被折叠成同一种异常；
// 传输/协议失败同样以可区分的 Status 暴露，而非统一的异常类型。
public sealed class SoconBridgeResponseResult
{
    private SoconBridgeResponseResult(
        SoconBridgeExchangeStatus status,
        string requestId,
        SoconBridgeOutcome? outcome,
        string? command,
        string? bridgeStatus,
        string? message,
        string? blockReason,
        IReadOnlyList<string> warnings,
        SoconBridgeResponseDetails? details,
        SoconBridgeProtocolErrorKind? protocolError,
        string? errorMessage)
    {
        Status = status;
        RequestId = requestId;
        Outcome = outcome;
        Command = command;
        BridgeStatus = bridgeStatus;
        Message = message;
        BlockReason = blockReason;
        Warnings = warnings;
        Details = details;
        ProtocolError = protocolError;
        ErrorMessage = errorMessage;
    }

    public SoconBridgeExchangeStatus Status { get; }

    // 每次请求由客户端生成的唯一 requestId；即便传输失败也保留，便于关联日志。
    public string RequestId { get; }

    // 仅当 Status == Completed 时非空。
    public SoconBridgeOutcome? Outcome { get; }
    public string? Command { get; }
    public string? BridgeStatus { get; }
    public string? Message { get; }
    public string? BlockReason { get; }
    public IReadOnlyList<string> Warnings { get; }
    public SoconBridgeResponseDetails? Details { get; }

    // 仅当 Status == ProtocolError 时非空。
    public SoconBridgeProtocolErrorKind? ProtocolError { get; }

    // 传输/协议失败时的人类可读说明（非 Completed 时使用）。
    public string? ErrorMessage { get; }

    public bool IsSuccess => Status == SoconBridgeExchangeStatus.Completed && Outcome == SoconBridgeOutcome.Success;
    public bool IsFailure => Status == SoconBridgeExchangeStatus.Completed && Outcome == SoconBridgeOutcome.Failure;
    public bool IsBlocked => Status == SoconBridgeExchangeStatus.Completed && Outcome == SoconBridgeOutcome.Blocked;

    // 由实现调用：收到合法响应后构造。
    internal static SoconBridgeResponseResult Completed(
        string requestId,
        string command,
        bool success,
        string? bridgeStatus,
        string message,
        string? blockReason,
        IReadOnlyList<string> warnings,
        SoconBridgeResponseDetails? details)
    {
        var outcome = success
            ? SoconBridgeOutcome.Success
            : IsBlockedMessage(message)
                ? SoconBridgeOutcome.Blocked
                : SoconBridgeOutcome.Failure;

        return new SoconBridgeResponseResult(
            SoconBridgeExchangeStatus.Completed,
            requestId,
            outcome,
            command,
            bridgeStatus,
            message,
            blockReason,
            warnings,
            details,
            protocolError: null,
            errorMessage: null);
    }

    // 由实现调用：传输/协议失败（非 ProtocolError 的子类）。
    internal static SoconBridgeResponseResult ForStatus(
        SoconBridgeExchangeStatus status,
        string requestId,
        string errorMessage) =>
        new(status, requestId, outcome: null, command: null, bridgeStatus: null, message: null,
            blockReason: null, warnings: [], details: null, protocolError: null, errorMessage);

    // 由实现调用：ProtocolError 子类（帧/编码/长度/requestId 违约）。
    internal static SoconBridgeResponseResult ForProtocolError(
        SoconBridgeProtocolErrorKind kind,
        string requestId,
        string errorMessage) =>
        new(SoconBridgeExchangeStatus.ProtocolError, requestId, outcome: null, command: null,
            bridgeStatus: null, message: null, blockReason: null, warnings: [], details: null,
            kind, errorMessage);

    // 对应 BridgeRequestProcessor.CreateBlockedResponse —— 该方法固定写入 message = "BLOCKED"。
    internal static bool IsBlockedMessage(string? message) =>
        string.Equals(message, "BLOCKED", StringComparison.Ordinal);
}
