using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Stainer.Tests.SoconBridge;

/// <summary>
/// Stainer.SoconBridge 有名管道帧编解码工具。
/// 帧格式：4 字节小端 int32 长度前缀 + UTF-8 JSON 载荷。
/// 纯静态工具，不涉及管道 I/O。
/// </summary>
public static class SoconBridgeFrame
{
    /// <summary>
    /// 将 JSON 字符串编码为带长度前缀的帧字节。
    /// </summary>
    public static byte[] WriteFrame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        return WriteFrame(payload);
    }

    /// <summary>
    /// 将原始载荷编码为带 4 字节小端长度前缀的帧字节。
    /// </summary>
    public static byte[] WriteFrame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>
    /// 从流中读取一帧：先读 4 字节小端 int32 长度，再读对应字节数的载荷。
    /// </summary>
    /// <exception cref="InvalidOperationException">长度小于等于 0 时抛出。</exception>
    public static async Task<(int length, byte[] payload)> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        await FillBufferAsync(stream, header, 4, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0)
            throw new InvalidOperationException($"帧长度无效: {length}（期望 > 0）。");

        var payload = new byte[length];
        await FillBufferAsync(stream, payload, length, cancellationToken);
        return (length, payload);
    }

    /// <summary>
    /// 构造规范响应 JSON 字符串。
    /// </summary>
    public static string ResponseJson(
        string requestId,
        string command,
        bool success,
        string bridgeStatus,
        string message,
        string? detailsJson = null,
        string? warningsJson = null)
    {
        detailsJson ??= "{}";
        warningsJson ??= "[]";
        return
            $"{{\"requestId\":{JsonSerializer.Serialize(requestId)},"
            + $"\"command\":{JsonSerializer.Serialize(command)},"
            + $"\"success\":{(success ? "true" : "false")},"
            + $"\"bridgeStatus\":{JsonSerializer.Serialize(bridgeStatus)},"
            + $"\"message\":{JsonSerializer.Serialize(message)},"
            + $"\"details\":{detailsJson},"
            + $"\"warnings\":{warningsJson}}}";
    }

    /// <summary>
    /// 会话打开成功响应。
    /// </summary>
    public static string SuccessSessionOpen(string requestId, string? detailsJson = null) =>
        ResponseJson(requestId, "OpenConfiguredReadOnlySession", true, "SessionOpen", "会话已打开", detailsJson);

    /// <summary>
    /// Bridge 忙碌/阻塞响应。
    /// </summary>
    public static string Blocked(string requestId, string command, string message = "Bridge 忙碌") =>
        ResponseJson(requestId, command, false, "Busy", message);

    /// <summary>
    /// 通用失败响应。
    /// </summary>
    public static string Failure(string requestId, string command, string bridgeStatus = "Error", string message = "操作失败") =>
        ResponseJson(requestId, command, false, bridgeStatus, message);

    /// <summary>
    /// Ping/Pong 响应。
    /// </summary>
    public static string Pong(string requestId) =>
        ResponseJson(requestId, "Ping", true, "Idle", "Pong");

    private static async Task FillBufferAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken);
            if (read == 0)
                throw new EndOfStreamException($"流意外结束：已读 {offset}/{count} 字节。");
            offset += read;
        }
    }
}
