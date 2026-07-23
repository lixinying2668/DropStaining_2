using System.Text;
using System.Text.Json;
using Stainer.Web.Application.Devices.SoconBridge;
using Stainer.Web.Infrastructure.Devices.SoconBridge;

namespace Stainer.Tests.SoconBridge;

/// <summary>
/// 真实 NamedPipeSoconBridgeClient 对 FakeSoconBridgePipeServer 的往返集成测试。
/// 不 mock 客户端接口，全部通过真实命名管道 I/O 完成。
/// </summary>
public class SoconBridgeClientRoundTripTests
{
    // ---------------------------------------------------------------
    // A. 请求格式验证：客户端发送的帧内容
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(SoconBridgeCommands.OpenConfiguredReadOnlySession)]
    [InlineData(SoconBridgeCommands.CloseConfiguredReadOnlySession)]
    [InlineData(SoconBridgeCommands.Ping)]
    [InlineData(SoconBridgeCommands.GetBridgeStatus)]
    public async Task 每条命令_发送正确的command和requestId_且不含硬件配置(string expectedCommand)
    {
        // Arrange
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        SoconBridgeClientRequest? captured = null;
        var serveTask = server.ServeAsync(req =>
        {
            captured = req;
            return SoconBridgeServerAction.RespondJson(
                SoconBridgeFrame.ResponseJson(req.RequestId!, req.Command!, true, "Idle", "OK"));
        }, cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);

        // Act — 根据命令选择对应方法
        _ = expectedCommand switch
        {
            SoconBridgeCommands.OpenConfiguredReadOnlySession => await client.OpenConfiguredReadOnlySessionAsync(cts.Token),
            SoconBridgeCommands.CloseConfiguredReadOnlySession => await client.CloseConfiguredReadOnlySessionAsync(cts.Token),
            SoconBridgeCommands.Ping => await client.PingAsync(cts.Token),
            SoconBridgeCommands.GetBridgeStatus => await client.GetBridgeStatusAsync(cts.Token),
            _ => throw new ArgumentOutOfRangeException()
        };

        await serveTask;

        // Assert — command 正确
        Assert.NotNull(captured);
        Assert.Equal(expectedCommand, captured.Command);

        // requestId 非空
        Assert.False(string.IsNullOrEmpty(captured.RequestId));

        // 帧长度前缀与实际载荷字节数一致
        Assert.Equal(captured.RawPayload.Length, captured.DeclaredLength);
        Assert.Equal(Encoding.UTF8.GetByteCount(captured.Json), captured.DeclaredLength);

        // 解析 JSON，确认只含 requestId 和 command
        using var doc = JsonDocument.Parse(captured.Json);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetPropertyCount());
        Assert.True(root.TryGetProperty("requestId", out _));
        Assert.True(root.TryGetProperty("command", out _));

        // 硬件配置字段绝不能出现
        Assert.False(root.TryGetProperty("axis", out _));
        Assert.False(root.TryGetProperty("com", out _));
        Assert.False(root.TryGetProperty("baud", out _));
        Assert.False(root.TryGetProperty("portNumber", out _));
        Assert.False(root.TryGetProperty("sdkDirectory", out _));
    }

    [Fact]
    public async Task 两次连续请求_生成不同的requestId()
    {
        // 第一轮
        string? firstRequestId = null;
        await using (var server1 = FakeSoconBridgePipeServer.Start())
        {
            var options = new SoconBridgeClientOptions
            {
                PipeName = server1.PipeName,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                ResponseTimeout = TimeSpan.FromSeconds(3)
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var serveTask = server1.ServeAsync(req =>
            {
                firstRequestId = req.RequestId;
                return SoconBridgeServerAction.RespondJson(
                    SoconBridgeFrame.Pong(req.RequestId!));
            }, cts.Token);

            var client = new NamedPipeSoconBridgeClient(options);
            await client.PingAsync(cts.Token);
            await serveTask;
        }

        // 第二轮（同一个 client 实例，连不同的 server）
        string? secondRequestId = null;
        await using var server2 = FakeSoconBridgePipeServer.Start();
        var options2 = new SoconBridgeClientOptions
        {
            PipeName = server2.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serveTask2 = server2.ServeAsync(req =>
        {
            secondRequestId = req.RequestId;
            return SoconBridgeServerAction.RespondJson(
                SoconBridgeFrame.Pong(req.RequestId!));
        }, cts2.Token);

        var client2 = new NamedPipeSoconBridgeClient(options2);
        await client2.PingAsync(cts2.Token);
        await serveTask2;

        // Assert
        Assert.NotNull(firstRequestId);
        Assert.NotNull(secondRequestId);
        Assert.NotEqual(firstRequestId, secondRequestId);
    }

    // ---------------------------------------------------------------
    // B. 结果解析：Success / Failure / Blocked 不混淆
    // ---------------------------------------------------------------

    [Fact]
    public async Task OpenConfiguredReadOnlySession_success_true_解析为IsSuccess()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(
            req => SoconBridgeServerAction.RespondJson(SoconBridgeFrame.SuccessSessionOpen(req.RequestId!)),
            cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        SoconBridgeResponseResult result = await client.OpenConfiguredReadOnlySessionAsync(cts.Token);

        await serveTask;

        Assert.True(result.IsSuccess);
        Assert.Equal(SoconBridgeExchangeStatus.Completed, result.Status);
        Assert.Equal(SoconBridgeOutcome.Success, result.Outcome);
        Assert.False(result.IsFailure);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task Ping_success_true_解析为IsSuccess()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(
            req => SoconBridgeServerAction.RespondJson(SoconBridgeFrame.Pong(req.RequestId!)),
            cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        SoconBridgeResponseResult result = await client.PingAsync(cts.Token);

        await serveTask;

        Assert.True(result.IsSuccess);
        Assert.Equal(SoconBridgeExchangeStatus.Completed, result.Status);
        Assert.Equal(SoconBridgeOutcome.Success, result.Outcome);
        Assert.False(result.IsFailure);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task success_false_message_BLOCKED_且details_blockReason_解析为IsBlocked()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(req =>
        {
            var json = SoconBridgeFrame.ResponseJson(
                req.RequestId!,
                req.Command!,
                success: false,
                bridgeStatus: "Busy",
                message: "BLOCKED",
                detailsJson: "{\"blockReason\":\"RealReadOnlyNotEnabled\"}",
                warningsJson: "[]");
            return SoconBridgeServerAction.RespondJson(json);
        }, cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        var result = await client.OpenConfiguredReadOnlySessionAsync(cts.Token);
        await serveTask;

        Assert.True(result.IsBlocked);
        Assert.Equal(SoconBridgeExchangeStatus.Completed, result.Status);
        Assert.Equal(SoconBridgeOutcome.Blocked, result.Outcome);
        Assert.Equal("RealReadOnlyNotEnabled", result.BlockReason);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Theory]
    [InlineData("NotSupported", "Error")]
    [InlineData("RealReadOnlyNotEnabled", "Error")]
    [InlineData("SessionAlreadyOpen", "SessionOpen")]
    [InlineData("DeploymentNotValidated", "NotValidated")]
    public async Task success_false_非BLOCKED消息_解析为IsFailure(string message, string bridgeStatus)
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(req =>
        {
            var json = SoconBridgeFrame.ResponseJson(
                req.RequestId!,
                req.Command!,
                success: false,
                bridgeStatus: bridgeStatus,
                message: message);
            return SoconBridgeServerAction.RespondJson(json);
        }, cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        var result = await client.GetBridgeStatusAsync(cts.Token);
        await serveTask;

        Assert.True(result.IsFailure);
        Assert.Equal(SoconBridgeExchangeStatus.Completed, result.Status);
        Assert.Equal(SoconBridgeOutcome.Failure, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task Details投影_SessionOpen响应_字段正确解析()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        const string detailsJson =
            "{\"sessionState\":\"Open\",\"sessionOpen\":true,\"cacheValid\":false}";
        var serveTask = server.ServeAsync(req =>
        {
            var json = SoconBridgeFrame.ResponseJson(
                req.RequestId!,
                "OpenConfiguredReadOnlySession",
                success: true,
                bridgeStatus: "SessionOpen",
                message: "会话已打开",
                detailsJson: detailsJson);
            return SoconBridgeServerAction.RespondJson(json);
        }, cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        var result = await client.OpenConfiguredReadOnlySessionAsync(cts.Token);
        await serveTask;

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Details);
        Assert.Equal("Open", result.Details.SessionState);
        Assert.True(result.Details.SessionOpen);
        Assert.False(result.Details.CacheValid);
        Assert.Equal("SessionOpen", result.BridgeStatus);
        Assert.Equal("会话已打开", result.Message);
    }

    // ---------------------------------------------------------------
    // C. requestId 语义：匹配与不匹配
    // ---------------------------------------------------------------

    [Fact]
    public async Task 响应requestId与请求一致_解析为Completed()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serveTask = server.ServeAsync(req =>
            SoconBridgeServerAction.RespondJson(
                SoconBridgeFrame.Pong(req.RequestId!)), cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.Completed, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task 响应requestId不匹配_解析为ProtocolError_RequestIdMismatch()
    {
        await using var server = FakeSoconBridgePipeServer.Start();
        var options = new SoconBridgeClientOptions
        {
            PipeName = server.PipeName,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ResponseTimeout = TimeSpan.FromSeconds(3)
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        const string tamperedId = "ffffffffffffffffffffffffffffffff";
        var serveTask = server.ServeAsync(req =>
            SoconBridgeServerAction.RespondJson(
                SoconBridgeFrame.Pong(tamperedId)), cts.Token);

        var client = new NamedPipeSoconBridgeClient(options);
        var result = await client.PingAsync(cts.Token);
        await serveTask;

        Assert.Equal(SoconBridgeExchangeStatus.ProtocolError, result.Status);
        Assert.Equal(SoconBridgeProtocolErrorKind.RequestIdMismatch, result.ProtocolError);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsBlocked);
        Assert.False(result.IsFailure);
        // result.RequestId 仍为客户端生成的原始值
        Assert.NotEqual(tamperedId, result.RequestId);
        Assert.Equal(32, result.RequestId.Length);
    }
}
