using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Stainer.Tests.SoconBridge;

/// <summary>
/// 客户端请求——由 <see cref="FakeSoconBridgePipeServer"/> 解析后传入 handler。
/// 不依赖任何 Infrastructure DTO，仅携带原始字节与宽松解析的字段。
/// </summary>
public sealed class SoconBridgeClientRequest
{
    /// <summary>客户端发送的原始载荷字节。</summary>
    public required byte[] RawPayload { get; init; }

    /// <summary>UTF-8 解码后的 JSON 字符串。</summary>
    public required string Json { get; init; }

    /// <summary>帧头声明的载荷长度。</summary>
    public required int DeclaredLength { get; init; }

    /// <summary>宽松解析的 requestId（可能为 null）。</summary>
    public string? RequestId { get; init; }

    /// <summary>宽松解析的 command（可能为 null）。</summary>
    public string? Command { get; init; }
}

/// <summary>
/// 服务器对单个连接的操作指令。
/// 通过静态工厂方法构造，handler 返回后由 <see cref="FakeSoconBridgePipeServer"/> 执行。
/// </summary>
public sealed class SoconBridgeServerAction
{
    private readonly ActionKind _kind;
    private readonly string? _json;
    private readonly byte[]? _rawBytes;
    private readonly int _oversizedDeclaredLength;

    private SoconBridgeServerAction(ActionKind kind, string? json = null, byte[]? rawBytes = null, int oversizedDeclaredLength = 0)
    {
        _kind = kind;
        _json = json;
        _rawBytes = rawBytes;
        _oversizedDeclaredLength = oversizedDeclaredLength;
    }

    /// <summary>正常 JSON 帧响应。</summary>
    public static SoconBridgeServerAction RespondJson(string json) => new(ActionKind.RespondJson, json: json);

    /// <summary>写入任意原始字节（无长度帧头），用于无效 JSON /畸形帧测试。</summary>
    public static SoconBridgeServerAction RespondRaw(byte[] rawBytes) => new(ActionKind.RespondRaw, rawBytes: rawBytes);

    /// <summary>
    /// 写入 4 字节 LE 声明长度但实际发送更少（或零）字节——用于超长/长度不匹配测试。
    /// </summary>
    public static SoconBridgeServerAction RespondOversized(int declaredLength) =>
        new(ActionKind.RespondOversized, oversizedDeclaredLength: declaredLength);

    /// <summary>不发送任何响应直接关闭连接——用于服务器断连测试。</summary>
    public static SoconBridgeServerAction Disconnect() => new(ActionKind.Disconnect);

    /// <summary>
    /// 永不响应、永不关闭——用于客户端响应超时测试。
    /// 测试通过取消 CancellationToken 或客户端内部超时来终结。
    /// </summary>
    public static SoconBridgeServerAction Hold() => new(ActionKind.Hold);

    internal async Task ExecuteAsync(Stream stream, CancellationToken cancellationToken)
    {
        switch (_kind)
        {
            case ActionKind.RespondJson:
                var frame = SoconBridgeFrame.WriteFrame(_json!);
                await stream.WriteAsync(frame, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                break;

            case ActionKind.RespondRaw:
                await stream.WriteAsync(_rawBytes!, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                break;

            case ActionKind.RespondOversized:
                // 写入声明长度但不写满载荷
                var header = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, _oversizedDeclaredLength);
                await stream.WriteAsync(header, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                break;

            case ActionKind.Disconnect:
                break; // 直接关闭

            case ActionKind.Hold:
                // 永远阻塞直到取消
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 预期取消
                }
                break;
        }
    }

    private enum ActionKind
    {
        RespondJson,
        RespondRaw,
        RespondOversized,
        Disconnect,
        Hold
    }
}

/// <summary>
/// 进程内假 SoconBridge 有名管道服务器。
/// 每个实例使用唯一管道名，每次 <see cref="ServeAsync"/> 处理一个连接（一问一答后关闭），
/// 与真实 Bridge 的单连接语义一致。
/// </summary>
public sealed class FakeSoconBridgePipeServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _served;

    private FakeSoconBridgePipeServer(string pipeName)
    {
        PipeName = pipeName;
        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte);
    }

    /// <summary>本实例监听的有名管道名。</summary>
    public string PipeName { get; }

    /// <summary>
    /// 创建并启动一个假服务器，使用唯一管道名。
    /// </summary>
    public static FakeSoconBridgePipeServer Start() =>
        new("Stainer.SoconBridge.Tests." + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 接受一个连接，读取一帧请求，调用 handler 获取操作指令，执行后关闭连接。
    /// 典型用法：在后台 Task 中调用，测试主线程同时 await 客户端发送。
    /// </summary>
    public async Task ServeAsync(
        Func<SoconBridgeClientRequest, SoconBridgeServerAction> handler,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);
        var ct = linkedCts.Token;

        await _pipe.WaitForConnectionAsync(ct);

        try
        {
            var (length, payload) = await SoconBridgeFrame.ReadFrameAsync(_pipe, ct);
            var json = Encoding.UTF8.GetString(payload);

            string? requestId = null;
            string? command = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("requestId", out var ridProp))
                    requestId = ridProp.GetString();
                if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                    command = cmdProp.GetString();
            }
            catch (JsonException)
            {
                // 宽松解析：无效 JSON 时 requestId/command 保持 null
            }

            var request = new SoconBridgeClientRequest
            {
                RawPayload = payload,
                Json = json,
                DeclaredLength = length,
                RequestId = requestId,
                Command = command
            };

            var action = handler(request);
            await action.ExecuteAsync(_pipe, ct);
        }
        finally
        {
            Interlocked.Exchange(ref _served, 1);
            _pipe.Disconnect();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await _pipe.DisposeAsync();
    }
}
