using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

// 试剂硬件副作用 dispatcher：单读 ReagentHardwareEventDecorator 的 hardwareChannel，
// 把试剂状态变更事件串行地经 IReagentHardwareSink 驱动到硬件（四步范式）。
// 范式参考 MachineExecutor（业务投递 + IServiceScopeFactory 后台串行消费）与 MachineEventSignalRDispatcher（单 channel reader）。
//
// 关键纪律（PROJECT_CONTEXT §9.2）：
//   - 后台异常绝不 rethrow，不让 webhost crash（仿 MachineExecutor.RecordExecutorExceptionAsync）；
//   - 事件在源事务 Commit 之前发布，故处理前做"提交确认"，回滚事件丢弃并审计；
//   - 幂等由 sink 端 DeriveCommandId + DeviceCommunicationRecords 预检保证。
public sealed class ReagentHardwareDispatcher(
    ReagentHardwareEventDecorator decorator,
    IServiceScopeFactory scopeFactory,
    ILogger<ReagentHardwareDispatcher> logger) : BackgroundService
{
    // 初始延迟让 CommandIdempotencyService 的事务在常态下完成 Commit（事件在业务闭包内、Commit 前发布）。
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in decorator.ReadAllReagentHardwareAsync(stoppingToken))
            {
                try
                {
                    await DispatchAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reagent hardware dispatch failed: {EventId} / {Type}", message.EventId, message.Type);
                    await RecordFailureAsync(message, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reagent hardware dispatcher stopped unexpectedly.");
            throw;
        }
    }

    private async Task DispatchAsync(MachineEventMessage message, CancellationToken cancellationToken)
    {
        await Task.Delay(InitialDelay, cancellationToken);

        var position = ReadString(message, "position");
        var scanSessionId = ReadString(message, "scanSessionId");

        // 仅对扫码期事件（含 position + scanSessionId）做提交确认；
        // ReagentBottleDepleted 由 MachineExecutor 发、payload 无 position，跳过验证直接发。
        var requiresVerification = !string.IsNullOrWhiteSpace(position) && !string.IsNullOrWhiteSpace(scanSessionId);
        if (requiresVerification
            && !await IsOriginatingScanCommittedAsync(scanSessionId!, position!, message.OccurredAtUtc, cancellationToken))
        {
            logger.LogWarning("Reagent hardware event {EventId} dropped: originating scan item not persisted within {Timeout} (transaction likely rolled back).",
                message.EventId, VerificationTimeout);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<IReagentHardwareSink>();
        await sink.NotifyReagentStateChangedAsync(ReagentHardwareEvent.FromMessage(message), cancellationToken);
    }

    // 开新 scope 用全新 StainerDbContext 查询：未提交的数据不可见，故查得到即事务已 Commit。
    private async Task<bool> IsOriginatingScanCommittedAsync(
        string scanSessionId, string position, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(VerificationTimeout);
        // 只把 string 等值比较交给数据库；CreatedAtUtc 的时间窗口检查在内存里做，
        // 避免 EF Core（SQLite）无法翻译 DateTimeOffset 的 <= 表达式。
        var cutoff = occurredAt.AddSeconds(30);
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();

            var positionId = await dbContext.ReagentRackPositions.AsNoTracking()
                .Where(x => x.Code == position)
                .Select(x => (string?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

            DateTimeOffset? itemCreatedAt = null;
            if (positionId is not null)
            {
                itemCreatedAt = await dbContext.ReagentScanItems.AsNoTracking()
                    .Where(x => x.ReagentScanSessionId == scanSessionId && x.ReagentRackPositionId == positionId)
                    .Select(x => (DateTimeOffset?)x.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (itemCreatedAt is not null && itemCreatedAt <= cutoff)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline) return false;
            await Task.Delay(RetryInterval, cancellationToken);
        }
    }

    private static string? ReadString(MachineEventMessage message, string key)
        => message.Payload.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private async Task RecordFailureAsync(MachineEventMessage message, Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "reagent.hardware.dispatch_failed",
                EntityType = "ReagentHardwareEvent",
                EntityId = message.EventId,
                Message = JsonSerializer.Serialize(new { message.EventId, message.Type, error = exception.Message }),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            // 用 None：失败记录不应被正在进行的取消令牌阻断，必须落库以便排查。
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // 记录失败本身不能再 crash host（仿 MachineExecutor.RecordExecutorExceptionAsync 末尾的 catch）。
        }
    }
}
