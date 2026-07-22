using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Devices;

// 试剂区硬件通信 Sink 实现：复用"设备命令四步范式"把试剂状态变更驱动到 IDeviceAdapter。
//
// 生命周期：Scoped（同 ReagentQrScannerDeviceOperationService）。注入项均兼容 Scoped：
//   IDeviceAdapter(Singleton，可注入 Scoped)、StainerDbContext(Scoped)、DeviceCommunicationPersistenceService(Scoped)。
//   ReagentHardwareDispatcher 必须从 IServiceScopeFactory 每事件新建 scope 解析本类，
//   不得由 Singleton 直接 capture（否则跨事件复用 DbContext 造成并发污染/use-after-dispose）。
//
// 一期实现说明：用语义化 action 走 IDeviceAdapter.ScanReagentAsync（复用完整四步范式 + Mock/Real 自动切换）。
//   - Mock: MockDeviceAdapter.ScanReagentAsync 对未知 action 走默认分支安全返回，用于验证全链路。
//   - Real: UnavailableRealDeviceAdapter.ScanReagentAsync 直接 reject → fail-closed，记录 Pending→Failed，不发真实字节。
//   - 真实 adapter 实装后：在其 action 路由表识别下列 action 并映射到主控/DCR55 字节；
//     或二期在 IDeviceAdapter 加 NotifyReagentStateAsync 专用方法（接口加方法对调用方向后兼容）。
//   不绕过 adapter 直接调 IDeviceByteTransport，以保留 Real 模式 fail-closed 门禁（PROJECT_CONTEXT §7.7/§13）。
public sealed class ReagentHardwareSink(
    IDeviceAdapter deviceAdapter,
    StainerDbContext dbContext,
    DeviceCommunicationPersistenceService communicationPersistence,
    ILogger<ReagentHardwareSink> logger) : IReagentHardwareSink
{
    public const string ActionReagentStateChanged = "reagent.stateChanged";
    public const string ActionReagentBottleChanged = "reagent.bottleChanged";
    public const string ActionReagentBottleDepleted = "reagent.bottleDepleted";

    public async Task<ReagentHardwareResult> NotifyReagentStateChangedAsync(
        ReagentHardwareEvent evt, CancellationToken cancellationToken = default)
    {
        var commandId = evt.DeriveCommandId();
        var action = MapAction(evt.EventType);

        // 幂等预检：同 commandId 已成功完成则跳过，防止 rescan/dispatcher 重启造成真实动作重复发送
        // （PROJECT_CONTEXT §9.2.5：数据库锁、网络问题或异常不得造成真实动作重复发送）。
        var replayed = await dbContext.DeviceCommunicationRecords.AsNoTracking()
            .AnyAsync(x => x.CommandId == commandId
                && x.Ok
                && x.PersistenceStatus == DeviceCommunicationPersistenceStatus.Complete, cancellationToken);
        if (replayed)
        {
            return new ReagentHardwareResult(
                true,
                DeviceCommandStatuses.Succeeded,
                null,
                "Reagent hardware notification already completed; skipped (idempotent).",
                deviceAdapter.Mode,
                deviceAdapter.Name,
                commandId,
                true);
        }

        // 四步范式（与 ReagentQrScannerDeviceOperationService.ExecuteDeviceCommandAsync 一致）。
        var operationRequest = new DeviceOperationRequest(
            new DeviceCommandContext(commandId, evt.ScanSessionId, "system", nameof(ReagentHardwareSink)),
            DeviceModules.ReagentScanner,
            action,
            BuildParameters(evt));

        var record = communicationPersistence.Begin(operationRequest);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSqliteLock(ex))
        {
            // Pending 可能已被并发 writer 落库；继续调 adapter，让 TryPersistCompletionAsync 自身的 SQLite 锁降级处理。
            logger.LogWarning(ex, "SQLite lock when persisting Pending for {CommandId}; continuing to adapter.", commandId);
        }

        var deviceResult = await deviceAdapter.ScanReagentAsync(operationRequest, cancellationToken);
        await communicationPersistence.TryPersistCompletionAsync(record, deviceResult, cancellationToken);

        return new ReagentHardwareResult(
            deviceResult.Ok,
            deviceResult.Status,
            deviceResult.ErrorCode,
            deviceResult.Message,
            deviceAdapter.Mode,
            deviceAdapter.Name,
            commandId,
            false);
    }

    public Task<ReagentHardwareSensorReadout> ReadReagentSensorsAsync(string rackCode, CancellationToken cancellationToken = default)
    {
        // 二期：经 IRealDeviceReadAdapter（UnavailableRealDeviceAdapter 已实现该接口）做真实只读，
        // 可能需扩 MainControllerSerialTransport 的只读白名单放行光耦/IO 读取。一期不发任何字节。
        return Task.FromResult(new ReagentHardwareSensorReadout(
            false,
            DeviceCommandStatuses.NotSupported,
            "reagent_sensor_read_not_implemented",
            "Reagent sensor reads are reserved for phase 2; no hardware command was sent.",
            new Dictionary<string, object?> { ["rackCode"] = rackCode }));
    }

    private static string MapAction(string eventType) => eventType switch
    {
        MachineEventTypes.ReagentChanged => ActionReagentStateChanged,
        MachineEventTypes.ReagentBottleChanged => ActionReagentBottleChanged,
        MachineEventTypes.ReagentBottleDepleted => ActionReagentBottleDepleted,
        _ => "reagent.unknown"
    };

    private static IReadOnlyDictionary<string, object?> BuildParameters(ReagentHardwareEvent evt)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["protocol"] = "IceImmunoReagentStateEvent",
            ["source"] = nameof(ReagentHardwareSink),
            ["eventType"] = evt.EventType,
            ["message"] = evt.Message,
            ["occurredAtUtc"] = evt.OccurredAtUtc
        };
        if (!string.IsNullOrWhiteSpace(evt.ScanSessionId)) parameters["scanSessionId"] = evt.ScanSessionId;
        if (!string.IsNullOrWhiteSpace(evt.Position)) parameters["position"] = evt.Position;
        if (!string.IsNullOrWhiteSpace(evt.ReagentCode)) parameters["reagentCode"] = evt.ReagentCode;
        if (!string.IsNullOrWhiteSpace(evt.ReagentBottleId)) parameters["reagentBottleId"] = evt.ReagentBottleId;
        if (!string.IsNullOrWhiteSpace(evt.ScanResult)) parameters["scanResult"] = evt.ScanResult;
        if (evt.RemainingVolumeUl is int volume) parameters["remainingVolumeUl"] = volume;
        return parameters;
    }

    private static bool IsSqliteLock(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
            || message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase);
    }
}
