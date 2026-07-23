using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Stainer.Web.Application.Devices.SoconBridge;

namespace Stainer.Web.Infrastructure.Devices.SoconBridge;

// Stainer.SoconBridge 命名管道的只读客户端实现（ISoconBridgeClient）。
//
// 连接生命周期（严格对应服务端 BridgeHost.Run：每个连接处理一个请求、返回一个响应后关闭）：
//   - 每次调用都新建一个连接（INamedPipeConnectionFactory.Create），发送一帧请求，
//     读取一帧响应，然后在 finally 中释放管道；绝不跨调用复用同一管道实例；
//   - 绝不自动启动 Bridge、绝不自动重试、绝不自动重连、绝不自动发送 Close；
//   - COM/波特率/sdkDirectory 等硬件配置由 Bridge 本机配置负责，请求体只发送 { requestId, command }；
//   - 每次请求生成唯一 requestId（32 位无连字符十六进制），并校验响应 requestId、限制读取长度。
//
// 超时与取消的严格区分（与 Dcr55SerialTransport.ReadFrameAsync 一致，不可回退）：
//   - ConnectTimeout / ResponseTimeout：由客户端超时（WaitAsync / deadline）判定；
//   - Canceled：仅由调用方 CancellationToken 触发。
// 绝不用内部 linked CTS 把超时误判成取消——WaitAsync(TimeSpan, CancellationToken) 与
// deadline + 每读一次检查 DateTimeOffset.UtcNow，二者都把超时表现为 TimeoutException，
// 把调用方取消表现为 OperationCanceledException，语义天然分离。
//
// 异常脱敏（不可回退）：所有捕获到的本地异常（IOException / UnauthorizedAccessException /
// DecoderFallbackException / JsonException 等）一律不得把 ex.Message / ex.ToString() / 堆栈 /
// 本机路径 / 管道内部信息写入对外 ErrorMessage。传输与协议失败统一返回“稳定错误码 +
// 固定安全消息”：稳定错误码即 SoconBridgeExchangeStatus / SoconBridgeProtocolErrorKind 枚举，
// 固定消息为不含任何运行期变量与原始异常文本的常量字符串。Bridge 正常返回的已脱敏
// Failure / Blocked 协议信息（result.Message / BlockReason / Details）按协议保留，与本地异常严格区分。
public sealed class NamedPipeSoconBridgeClient : ISoconBridgeClient
{
    // 严格 UTF-8 解码器：无效字节直接抛 DecoderFallbackException，以便把 InvalidUtf8
    // 与 InvalidJson 分开报告（先验 UTF-8、再交给 JSON 反序列化）。
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly SoconBridgeClientOptions options;
    private readonly INamedPipeConnectionFactory connectionFactory;

    // 生产构造：使用真实 NamedPipeClientStream。
    public NamedPipeSoconBridgeClient(SoconBridgeClientOptions options)
        : this(options, new NamedPipeConnectionFactory())
    {
    }

    // 测试 / 受控验证构造：可注入 INamedPipeConnectionFactory 实现以覆盖连接阶段的
    // 不可控行为（接入超时、端点不可用、接入即断开等）。
    internal NamedPipeSoconBridgeClient(
        SoconBridgeClientOptions options,
        INamedPipeConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        if (string.IsNullOrWhiteSpace(options.PipeName))
        {
            throw new ArgumentException("PipeName must be a non-whitespace string.", nameof(options));
        }

        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ConnectTimeout must be positive.");
        }

        if (options.ResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ResponseTimeout must be positive.");
        }

        if (options.MaxResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxResponseBytes must be positive.");
        }

        this.options = options;
        this.connectionFactory = connectionFactory;
    }

    public Task<SoconBridgeResponseResult> PingAsync(CancellationToken cancellationToken) =>
        SendAsync(SoconBridgeCommands.Ping, cancellationToken);

    public Task<SoconBridgeResponseResult> GetBridgeStatusAsync(CancellationToken cancellationToken) =>
        SendAsync(SoconBridgeCommands.GetBridgeStatus, cancellationToken);

    public Task<SoconBridgeResponseResult> OpenConfiguredReadOnlySessionAsync(CancellationToken cancellationToken) =>
        SendAsync(SoconBridgeCommands.OpenConfiguredReadOnlySession, cancellationToken);

    public Task<SoconBridgeResponseResult> CloseConfiguredReadOnlySessionAsync(CancellationToken cancellationToken) =>
        SendAsync(SoconBridgeCommands.CloseConfiguredReadOnlySession, cancellationToken);

    // 单次请求的核心实现：一连接、一请求、一响应、一释放。
    private async Task<SoconBridgeResponseResult> SendAsync(string command, CancellationToken cancellationToken)
    {
        // 每次请求生成唯一 requestId；即便传输失败也带回，便于关联日志。
        var requestId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var wireRequest = new SoconBridgeWireRequest { RequestId = requestId, Command = command };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wireRequest, JsonOptions);

        // 防御性校验：请求负载绝不得超过服务端上限（本客户端只发两个小字段，恒为真，
        // 但仍显式校验以免未来扩展 DTO 时越界）。固定安全消息，不写入任何变量或异常文本。
        if (payload.Length > SoconBridgeTransport.MaxRequestBytes)
        {
            return SoconBridgeResponseResult.ForProtocolError(
                SoconBridgeProtocolErrorKind.OversizeResponse,
                requestId,
                "Client request payload exceeds the maximum allowed request size.");
        }

        // 每次调用新建连接；无论成功失败都在 finally 中释放，确保后续请求可继续发起
        // （依赖“失败后仍可复用客户端”的测试用例正是建立在这一保证之上）。
        var connection = connectionFactory.Create(options.ServerName, options.PipeName);
        try
        {
            // ---- 连接阶段 ----
            try
            {
                await connection.ConnectAsync(options.ConnectTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.ConnectTimeout,
                    requestId,
                    "Named pipe connect did not complete within the configured connect timeout.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.Canceled,
                    requestId,
                    "Named pipe connect was canceled by the caller.");
            }
            catch (UnauthorizedAccessException)
            {
                // 访问被拒（ACL / 端点不可达）：固定安全消息，不暴露 ex.Message / 路径 / 管道内部信息。
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.PipeUnavailable,
                    requestId,
                    "Named pipe endpoint is unavailable.");
            }
            catch (IOException)
            {
                // 端点硬性不可用（管道不存在 / 被关闭 / I/O 错误）：固定安全消息，不暴露 ex.Message。
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.PipeUnavailable,
                    requestId,
                    "Named pipe endpoint is unavailable.");
            }

            // ---- 帧组装：4 字节小端有符号长度前缀 + UTF-8 JSON 负载 ----
            var stream = connection.DuplexStream;
            var frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);

            // 读写阶段：deadline 仅约束读取，调用方 token 约束取消；二者各司其职。
            var deadline = DateTimeOffset.UtcNow + options.ResponseTimeout;

            byte[]? payloadBytes;
            try
            {
                // 写入请求帧。命名管道写一般由 OS 缓冲即完成；仍捕获 IOException
                // 以便在写阶段断管时归并为 Disconnected，避免向上抛未处理异常。
                try
                {
                    await stream.WriteAsync(frame, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return SoconBridgeResponseResult.ForStatus(
                        SoconBridgeExchangeStatus.Canceled,
                        requestId,
                        "Request frame write was canceled by the caller.");
                }
                catch (IOException)
                {
                    // 固定安全消息，不写入 ex.Message。
                    return SoconBridgeResponseResult.ForStatus(
                        SoconBridgeExchangeStatus.Disconnected,
                        requestId,
                        "Named pipe disconnected while writing the request frame.");
                }

                // ---- 读取 4 字节长度前缀 ----
                var lengthBytes = await ReadExactAsync(stream, 4, deadline, cancellationToken);
                if (lengthBytes is null)
                {
                    return SoconBridgeResponseResult.ForStatus(
                        SoconBridgeExchangeStatus.Disconnected,
                        requestId,
                        "Server closed the connection before sending the 4-byte length prefix.");
                }

                var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length <= 0)
                {
                    // 协议错误：固定安全消息，不写入收到的具体长度值。
                    return SoconBridgeResponseResult.ForProtocolError(
                        SoconBridgeProtocolErrorKind.InvalidLength,
                        requestId,
                        "Response length prefix is invalid.");
                }

                if (length > options.MaxResponseBytes)
                {
                    // 协议错误：固定安全消息，不写入声明长度或上限值。
                    return SoconBridgeResponseResult.ForProtocolError(
                        SoconBridgeProtocolErrorKind.OversizeResponse,
                        requestId,
                        "Response exceeds the maximum allowed response size.");
                }

                // ---- 读取 length 字节负载 ----
                payloadBytes = await ReadExactAsync(stream, length, deadline, cancellationToken);
                if (payloadBytes is null)
                {
                    return SoconBridgeResponseResult.ForStatus(
                        SoconBridgeExchangeStatus.Disconnected,
                        requestId,
                        "Server closed the connection before the response payload completed.");
                }
            }
            catch (TimeoutException)
            {
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.ResponseTimeout,
                    requestId,
                    "Response was not received within the configured response timeout.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.Canceled,
                    requestId,
                    "Response read was canceled by the caller.");
            }
            catch (IOException)
            {
                // 固定安全消息，不写入 ex.Message。
                return SoconBridgeResponseResult.ForStatus(
                    SoconBridgeExchangeStatus.Disconnected,
                    requestId,
                    "Named pipe disconnected while reading the response frame.");
            }

            // ---- 严格 UTF-8 解码：与 JSON 解析分开报告 ----
            string text;
            try
            {
                text = StrictUtf8.GetString(payloadBytes);
            }
            catch (DecoderFallbackException)
            {
                // 协议错误：固定安全消息，不写入 ex.Message。
                return SoconBridgeResponseResult.ForProtocolError(
                    SoconBridgeProtocolErrorKind.InvalidUtf8,
                    requestId,
                    "Response payload is not valid UTF-8.");
            }

            // ---- JSON 反序列化 ----
            SoconBridgeWireResponse? wire;
            try
            {
                wire = JsonSerializer.Deserialize<SoconBridgeWireResponse>(text, JsonOptions);
            }
            catch (JsonException)
            {
                // 协议错误：固定安全消息，不写入 ex.Message。
                return SoconBridgeResponseResult.ForProtocolError(
                    SoconBridgeProtocolErrorKind.InvalidJson,
                    requestId,
                    "Response payload is not valid JSON.");
            }

            if (wire is null)
            {
                return SoconBridgeResponseResult.ForProtocolError(
                    SoconBridgeProtocolErrorKind.InvalidJson,
                    requestId,
                    "Response payload is not valid JSON.");
            }

            // ---- requestId 一致性校验（Ordinal）----
            // 固定安全消息：不写入请求/响应 requestId 原值（客户端生成的 requestId 仍可通过 result.RequestId 关联）。
            if (wire.RequestId is null ||
                !string.Equals(wire.RequestId, requestId, StringComparison.Ordinal))
            {
                return SoconBridgeResponseResult.ForProtocolError(
                    SoconBridgeProtocolErrorKind.RequestIdMismatch,
                    requestId,
                    "Response requestId does not match the request requestId.");
            }

            // ---- 构造最终结果：command 优先取服务端回显，缺失则回退请求 command ----
            var resolvedCommand = wire.Command ?? command;
            var details = SoconBridgeWireMapper.ToPublic(wire.Details);

            return SoconBridgeResponseResult.Completed(
                requestId,
                resolvedCommand,
                wire.Success,
                wire.BridgeStatus,
                wire.Message ?? string.Empty,
                wire.Details?.BlockReason,
                wire.Warnings ?? [],
                details);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    // 从流中精确读取 count 字节。使用 deadline + 调用方 token 双约束：
    //   - 每次循环先检查 deadline（到点抛 TimeoutException）与调用方取消（抛 OperationCanceledException）；
    //   - 每次单读 stream.ReadAsync(buf, ct).WaitAsync(timeRemaining, ct) —— WaitAsync 把超时表现为
    //     TimeoutException、把调用方取消表现为 OperationCanceledException，二者天然分离；
    //   - 绝不使用内部 linked CTS（会把超时伪装成取消）。
    // 返回 null 表示流在凑齐 count 字节前结束（EOF / 服务端关闭）。
    private static async Task<byte[]?> ReadExactAsync(
        Stream stream,
        int count,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException();
            }

            // 仅响应调用方取消；超时由上方 deadline 判定并抛 TimeoutException。
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = deadline - DateTimeOffset.UtcNow;
            int read;
            try
            {
                // 使用 byte[] 重载以获得 Task<int>（Memory<> 重载返回 ValueTask<int>，无 WaitAsync 扩展）；
                // 再以 (remaining, cancellationToken) 套 WaitAsync，把超时表现为 TimeoutException、
                // 把调用方取消表现为 OperationCanceledException。
                read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken)
                    .WaitAsync(remaining, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (read == 0)
            {
                // 流在凑齐前结束：EOF / 服务端关闭连接。
                return null;
            }

            offset += read;
        }

        return buffer;
    }
}
