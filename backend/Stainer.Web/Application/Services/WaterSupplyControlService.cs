using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

// 供水孔 / 供水模块控制（water_supply_channel_states）。
// 每个染色通道 1..4 拥有独立的进/出水温度、出水量、流速与开关状态。
// 业务语义与 /api/fluidics/*（液路/泵/清洗/混匀/液位）不同——不得把供水孔出水映射到 fluidics.wash。
// Mock 模式可执行；Real 模式 fail-closed（409 water_supply_mock_not_available），绝不回退 Mock。
public sealed class WaterSupplyControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    IDeviceAdapter deviceAdapter)
{
    private const int ChannelCount = 4;
    private const int AmbientInletTemperatureDeciC = 250;   // 25.0 ℃
    private const int DefaultOutletTemperatureDeciC = 250;  // 25.0 ℃
    private const int DefaultTargetTemperatureDeciC = 450;  // 45.0 ℃
    private const int DefaultFlowRateMlPerMinute = 250;
    private const int DefaultOutletDurationMs = 10_000;     // 打开出水时按流速折算水量的默认时长
    private const int MillilitersToMicroliters = 1000;
    private const int MillisecondsPerMinute = 60_000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<int, string> ChannelCodes =
        new Dictionary<int, string> { [1] = "CH1", [2] = "CH2", [3] = "CH3", [4] = "CH4" };

    public async Task<WaterSupplyStateResponse> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            return await BuildResponseAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<WaterSupplyChannelResponse> GetChannelAsync(int channelNo, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var channel = await RequireChannelAsync(channelNo, cancellationToken);
            return ToResponse(channel);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<WaterSupplyMutationResponse> SetTargetTemperatureAsync(
        int channelNo,
        SetWaterSupplyTargetTemperatureRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "water_supply.target_temperature.set",
            new { channelNo, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = await RequireChannelAsync(channelNo, cancellationToken);
                    EnsureCanOperate(channel);
                    ValidateTemperature(request.TargetTemperatureDeciC);
                    _ = RequireReason(request.Reason);
                    channel.OutletTargetTemperatureDeciC = request.TargetTemperatureDeciC;
                    channel.CurrentCommandId = request.CommandId;
                    channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddTelemetry(channel, WaterSupplyTelemetryEventTypes.ChannelChanged);
                    PublishChannel(channel, "targetChanged");
                    AddAudit(actor, "water_supply.target_temperature.set", channel.Id, request.CommandId, new { channel.ChannelNo, channel.ChannelCode, request.TargetTemperatureDeciC, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<WaterSupplyMutationResponse>(
                        new WaterSupplyMutationResponse(true, request.CommandId, false, "Water supply target temperature updated.", await BuildResponseAsync(cancellationToken)),
                        "WaterSupplyChannelState",
                        channel.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<WaterSupplyMutationResponse> SetFlowAsync(
        int channelNo,
        SetWaterSupplyFlowRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "water_supply.flow.set",
            new { channelNo, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = await RequireChannelAsync(channelNo, cancellationToken);
                    EnsureCanOperate(channel);
                    ValidateFlowRate(request.FlowRateMlPerMinute);
                    _ = RequireReason(request.Reason);
                    channel.OutletFlowRateMlPerMinute = request.FlowRateMlPerMinute;
                    channel.CurrentCommandId = request.CommandId;
                    channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddTelemetry(channel, WaterSupplyTelemetryEventTypes.ChannelChanged);
                    PublishChannel(channel, "flowChanged");
                    AddAudit(actor, "water_supply.flow.set", channel.Id, request.CommandId, new { channel.ChannelNo, channel.ChannelCode, request.FlowRateMlPerMinute, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<WaterSupplyMutationResponse>(
                        new WaterSupplyMutationResponse(true, request.CommandId, false, "Water supply flow rate updated.", await BuildResponseAsync(cancellationToken)),
                        "WaterSupplyChannelState",
                        channel.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<WaterSupplyMutationResponse> SetOutletAsync(
        int channelNo,
        SetWaterSupplyOutletRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "water_supply.outlet.set",
            new { channelNo, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = await RequireChannelAsync(channelNo, cancellationToken);
                    EnsureCanOperate(channel);
                    _ = RequireReason(request.Reason);
                    if (request.Enabled)
                    {
                        await OpenOutletAsync(channel, request.CommandId, request.DurationMs ?? DefaultOutletDurationMs, cancellationToken);
                    }
                    else
                    {
                        CloseOutlet(channel, request.CommandId);
                    }

                    AddTelemetry(channel, request.Enabled ? WaterSupplyTelemetryEventTypes.OutletOpened : WaterSupplyTelemetryEventTypes.OutletClosed);
                    PublishChannel(channel, request.Enabled ? "outletOpened" : "outletClosed");
                    AddAudit(actor, "water_supply.outlet.set", channel.Id, request.CommandId, new
                    {
                        channel.ChannelNo,
                        channel.ChannelCode,
                        enabled = request.Enabled,
                        request.DurationMs,
                        request.Reason,
                        channel.OutletVolumeMl,
                        channel.OutletFlowRateMlPerMinute
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<WaterSupplyMutationResponse>(
                        new WaterSupplyMutationResponse(true, request.CommandId, false, request.Enabled ? "Water supply outlet opened." : "Water supply outlet closed.", await BuildResponseAsync(cancellationToken)),
                        "WaterSupplyChannelState",
                        channel.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<WaterSupplyMutationResponse> ConfigureFaultAsync(
        ConfigureWaterSupplyFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "water_supply.fault.configure",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = await RequireChannelAsync(request.ChannelNo, cancellationToken);
                    var faultType = NormalizeFaultType(request.FaultType);
                    var reason = RequireReason(request.Reason);
                    var errorCode = string.IsNullOrWhiteSpace(request.ErrorCode) ? faultType : request.ErrorCode.Trim();
                    var message = string.IsNullOrWhiteSpace(request.Message) ? reason : request.Message.Trim();
                    ApplyFault(channel, faultType, errorCode, message, request.CommandId);
                    AddTelemetry(channel, WaterSupplyTelemetryEventTypes.FaultConfigured);
                    PublishChannel(channel, "faultConfigured");
                    await AddFaultAlarmAsync(channel, faultType, message, cancellationToken);
                    AddAudit(actor, "water_supply.fault.configured", channel.Id, request.CommandId, new { channel.ChannelNo, channel.ChannelCode, faultType, errorCode, message, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<WaterSupplyMutationResponse>(
                        new WaterSupplyMutationResponse(true, request.CommandId, false, "Water supply fault configured.", await BuildResponseAsync(cancellationToken)),
                        "WaterSupplyChannelState",
                        channel.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<WaterSupplyMutationResponse> ClearFaultAsync(
        ClearWaterSupplyFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "water_supply.fault.clear",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = await RequireChannelAsync(request.ChannelNo, cancellationToken);
                    _ = RequireReason(request.Reason);
                    var alarms = await dbContext.Alarms
                        .Where(x => x.Status == "Active" && x.Code.StartsWith($"water_supply_{channel.ChannelCode}_"))
                        .ToListAsync(cancellationToken);
                    foreach (var alarm in alarms)
                    {
                        alarm.Status = "Cleared";
                        alarm.ClearedAtUtc = DateTimeOffset.UtcNow;
                    }

                    channel.FaultCode = null;
                    channel.FaultMessage = null;
                    channel.IsConnected = true;
                    channel.Status = channel.OutletEnabled ? WaterSupplyStatuses.Running : WaterSupplyStatuses.Idle;
                    channel.CurrentCommandId = request.CommandId;
                    channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddTelemetry(channel, WaterSupplyTelemetryEventTypes.FaultCleared);
                    PublishChannel(channel, "faultCleared");
                    AddAudit(actor, "water_supply.fault.cleared", channel.Id, request.CommandId, new { channel.ChannelNo, channel.ChannelCode, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<WaterSupplyMutationResponse>(
                        new WaterSupplyMutationResponse(true, request.CommandId, false, "Water supply fault cleared.", await BuildResponseAsync(cancellationToken)),
                        "WaterSupplyChannelState",
                        channel.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    // 启动期公开入口：仅幂等补齐 4 个供水通道，不推进模拟。供 Program.cs 启动初始化调用。
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task EnsureSeededCoreAsync(CancellationToken cancellationToken)
    {
        // 幂等补齐缺失的通道（1..4）；已有行（含被业务设过的温度/流速）不动。
        var present = (await dbContext.WaterSupplyChannelStates
                .Select(x => new { x.ChannelNo })
                .ToListAsync(cancellationToken))
            .Select(x => x.ChannelNo)
            .ToHashSet();
        var now = DateTimeOffset.UtcNow;
        for (var channelNo = 1; channelNo <= ChannelCount; channelNo++)
        {
            if (present.Contains(channelNo))
            {
                continue;
            }

            dbContext.WaterSupplyChannelStates.Add(new WaterSupplyChannelState
            {
                ChannelNo = channelNo,
                ChannelCode = ChannelCodes[channelNo],
                InletTemperatureDeciC = AmbientInletTemperatureDeciC,
                OutletTargetTemperatureDeciC = DefaultTargetTemperatureDeciC,
                OutletTemperatureDeciC = DefaultOutletTemperatureDeciC,
                OutletVolumeMl = 0,
                OutletFlowRateMlPerMinute = DefaultFlowRateMlPerMinute,
                OutletEnabled = false,
                Status = WaterSupplyStatuses.Idle,
                IsConnected = true,
                UpdatedAtUtc = now
            });
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task OpenOutletAsync(WaterSupplyChannelState channel, string commandId, int durationMs, CancellationToken cancellationToken)
    {
        channel.OutletEnabled = true;
        channel.Status = WaterSupplyStatuses.Running;
        // Mock：打开出水即认为出水温度已达目标温度。
        channel.OutletTemperatureDeciC = channel.OutletTargetTemperatureDeciC;
        channel.CurrentCommandId = commandId;
        channel.FaultCode = null;
        channel.FaultMessage = null;
        channel.IsConnected = true;

        // 按流速与时长折算本次出水量（ml），累加到通道累计出水量。
        var durationMinutes = Math.Max(0, durationMs) / (double)MillisecondsPerMinute;
        var dispensedMl = (int)Math.Round(channel.OutletFlowRateMlPerMinute * durationMinutes);
        if (dispensedMl > 0)
        {
            channel.OutletVolumeMl += dispensedMl;
            await DeductSystemWaterAsync(dispensedMl, cancellationToken);
        }

        channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void CloseOutlet(WaterSupplyChannelState channel, string commandId)
    {
        channel.OutletEnabled = false;
        channel.Status = WaterSupplyStatuses.Stopped;
        channel.CurrentCommandId = commandId;
        channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    // 同步扣减 SystemWater（系统纯水）容器：1 ml = 1000 uL，不低于 0。容器不存在（尚未 seed）时跳过。
    private async Task DeductSystemWaterAsync(int dispensedMl, CancellationToken cancellationToken)
    {
        var systemWater = await dbContext.LiquidContainerStates
            .SingleOrDefaultAsync(x => x.SourceType == LiquidSourceTypes.SystemWater, cancellationToken);
        if (systemWater is null || systemWater.IsWaste)
        {
            return;
        }

        var deductUl = dispensedMl * MillilitersToMicroliters;
        systemWater.CurrentVolumeUl = Math.Max(0, systemWater.CurrentVolumeUl - deductUl);
        systemWater.LevelStatus = CalculateLiquidStatus(systemWater);
        systemWater.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string CalculateLiquidStatus(LiquidContainerState liquid)
    {
        if (!liquid.IsConnected) return LiquidLevelStatuses.Disconnected;
        if (liquid.IsWaste) return liquid.CurrentVolumeUl >= liquid.FullThresholdUl ? LiquidLevelStatuses.Full : LiquidLevelStatuses.Normal;
        if (liquid.CurrentVolumeUl <= 0) return LiquidLevelStatuses.Empty;
        return liquid.CurrentVolumeUl <= liquid.LowThresholdUl ? LiquidLevelStatuses.Low : LiquidLevelStatuses.Normal;
    }

    private static void ApplyFault(WaterSupplyChannelState channel, string faultType, string errorCode, string message, string commandId)
    {
        channel.FaultCode = errorCode;
        channel.FaultMessage = message;
        channel.IsConnected = faultType != WaterSupplyFaultTypes.Disconnected;
        channel.Status = StatusForFault(faultType);
        channel.OutletEnabled = false;
        channel.CurrentCommandId = commandId;
        channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string StatusForFault(string faultType) => faultType switch
    {
        WaterSupplyFaultTypes.Timeout => WaterSupplyStatuses.TimedOut,
        WaterSupplyFaultTypes.Disconnected => WaterSupplyStatuses.Disconnected,
        WaterSupplyFaultTypes.Unknown => WaterSupplyStatuses.Unknown,
        _ => WaterSupplyStatuses.Faulted
    };

    private async Task AddFaultAlarmAsync(WaterSupplyChannelState channel, string faultType, string message, CancellationToken cancellationToken)
    {
        var code = $"water_supply_{channel.ChannelCode}_{faultType}";
        // 去重必须查数据库：不同请求/不同 commandId 走独立 DbContext，本地缓存 (.Local) 为空，
        // 仅查 Local 会让重复配置同一通道同类故障时插入多条相同 Active 告警。Local 兜底同事务内未提交的行。
        var alreadyActive = dbContext.Alarms.Local.Any(x => x.Code == code && x.Status == "Active")
            || await dbContext.Alarms.AnyAsync(x => x.Code == code && x.Status == "Active", cancellationToken);
        if (!alreadyActive)
        {
            dbContext.Alarms.Add(new Alarm
            {
                Code = code,
                Severity = "Error",
                Message = message,
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task<WaterSupplyChannelState> RequireChannelAsync(int channelNo, CancellationToken cancellationToken)
    {
        if (channelNo is < 1 or > ChannelCount)
        {
            throw new BusinessRuleException("water_supply_channel_invalid", "ChannelNo must be between 1 and 4.", StatusCodes.Status400BadRequest);
        }

        return await dbContext.WaterSupplyChannelStates.SingleOrDefaultAsync(x => x.ChannelNo == channelNo, cancellationToken)
            ?? throw new BusinessRuleException("water_supply_channel_not_found", "Water supply channel was not found.", StatusCodes.Status404NotFound);
    }

    private static void EnsureCanOperate(WaterSupplyChannelState channel)
    {
        if (!CanOperate(channel))
        {
            throw new BusinessRuleException(
                channel.FaultCode ?? "water_supply_not_ready",
                "Water supply channel is faulted, disconnected, unknown, or timed out.",
                StatusCodes.Status409Conflict);
        }
    }

    private static bool CanOperate(WaterSupplyChannelState channel) =>
        channel.IsConnected
        && channel.FaultCode is null
        && channel.Status is not (WaterSupplyStatuses.Faulted or WaterSupplyStatuses.Disconnected or WaterSupplyStatuses.Unknown or WaterSupplyStatuses.TimedOut);

    private async Task<WaterSupplyStateResponse> BuildResponseAsync(CancellationToken cancellationToken)
    {
        var channels = await dbContext.WaterSupplyChannelStates.AsNoTracking().OrderBy(x => x.ChannelNo).ToListAsync(cancellationToken);
        var ready = channels.All(CanOperate) && channels.Count == ChannelCount;
        return new WaterSupplyStateResponse(ready, channels.Select(ToResponse).ToList(), DateTimeOffset.UtcNow);
    }

    private void AddTelemetry(WaterSupplyChannelState channel, string eventType)
    {
        dbContext.WaterSupplyTelemetry.Add(new WaterSupplyTelemetry
        {
            SourceId = channel.Id,
            ChannelNo = channel.ChannelNo,
            ChannelCode = channel.ChannelCode,
            EventType = eventType,
            InletTemperatureDeciC = channel.InletTemperatureDeciC,
            OutletTargetTemperatureDeciC = channel.OutletTargetTemperatureDeciC,
            OutletTemperatureDeciC = channel.OutletTemperatureDeciC,
            OutletVolumeMl = channel.OutletVolumeMl,
            OutletFlowRateMlPerMinute = channel.OutletFlowRateMlPerMinute,
            OutletEnabled = channel.OutletEnabled,
            Status = channel.Status,
            IsConnected = channel.IsConnected,
            CommandId = channel.CurrentCommandId,
            FaultCode = channel.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityId, string commandId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = action,
            EntityType = "WaterSupplyChannelState",
            EntityId = entityId,
            Message = JsonSerializer.Serialize(new { commandId, details }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void PublishChannel(WaterSupplyChannelState channel, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.WaterSupplyChanged,
            null,
            "WaterSupplyChannelState",
            channel.Id,
            null,
            ChannelData(channel, changeType)));
    }

    private static IReadOnlyDictionary<string, object?> ChannelData(WaterSupplyChannelState channel, string? changeType = null) => new Dictionary<string, object?>
    {
        ["changeType"] = changeType,
        ["channelNo"] = channel.ChannelNo,
        ["channelCode"] = channel.ChannelCode,
        ["inletTemperatureDeciC"] = channel.InletTemperatureDeciC,
        ["outletTargetTemperatureDeciC"] = channel.OutletTargetTemperatureDeciC,
        ["outletTemperatureDeciC"] = channel.OutletTemperatureDeciC,
        ["outletVolumeMl"] = channel.OutletVolumeMl,
        ["outletFlowRateMlPerMinute"] = channel.OutletFlowRateMlPerMinute,
        ["outletEnabled"] = channel.OutletEnabled,
        ["status"] = channel.Status,
        ["isConnected"] = channel.IsConnected,
        ["commandId"] = channel.CurrentCommandId,
        ["faultCode"] = channel.FaultCode
    };

    private static WaterSupplyChannelResponse ToResponse(WaterSupplyChannelState x) => new(
        x.Id,
        x.ChannelNo,
        x.ChannelCode,
        x.InletTemperatureDeciC,
        x.OutletTargetTemperatureDeciC,
        x.OutletTemperatureDeciC,
        x.OutletVolumeMl,
        x.OutletFlowRateMlPerMinute,
        x.OutletEnabled,
        x.Status,
        x.IsConnected,
        x.CurrentCommandId,
        x.FaultCode,
        x.FaultMessage,
        x.UpdatedAtUtc);

    private static string NormalizeFaultType(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (!WaterSupplyFaultTypes.All.Contains(normalized))
        {
            throw new BusinessRuleException("water_supply_fault_type_invalid", "Unknown water supply fault type.", StatusCodes.Status400BadRequest);
        }

        return WaterSupplyFaultTypes.All.First(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("reason_required", "A reason is required.", StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static void ValidateTemperature(int value)
    {
        if (value is < 0 or > 1000)
        {
            throw new BusinessRuleException("water_supply_target_temperature_invalid", "Temperature must be between 0 and 1000 deci-Celsius.", StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateFlowRate(int value)
    {
        if (value is < 0 or > 5000)
        {
            throw new BusinessRuleException("water_supply_flow_rate_invalid", "FlowRateMlPerMinute must be between 0 and 5000.", StatusCodes.Status400BadRequest);
        }
    }

    private void EnsureMockMode()
    {
        if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("water_supply_mock_not_available", "Water supply Mock control is unavailable in Real mode.", StatusCodes.Status409Conflict);
        }
    }
}
