using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class FluidicsControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    IDeviceAdapter deviceAdapter)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, (int No, string Drawer)> PwmMap =
        new Dictionary<string, (int No, string Drawer)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PWM0"] = (0, "A"),
            ["PWM1"] = (1, "B"),
            ["PWM2"] = (2, "C"),
            ["PWM3"] = (3, "D")
        };

    public async Task<FluidicsStateResponse> GetStateAsync(CancellationToken cancellationToken = default)
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

    // 启动期公开入口：仅幂等补齐泵/混匀/液位容器基线（供 SystemWater 等容器在首次 API 调用前就存在），
    // 不推进模拟。供 Program.cs 启动初始化调用，避免空库首次操作（如供水孔扣减 SystemWater）时容器缺失。
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

    public async Task<IReadOnlyList<FluidicsTelemetryResponse>> ListTelemetryAsync(int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var rows = await dbContext.FluidicsTelemetry.AsNoTracking().ToListAsync(cancellationToken);
        return rows
            .OrderByDescending(x => x.RecordedAtUtc)
            .Take(take)
            .Select(ToTelemetryResponse)
            .ToList();
    }

    public Task<FluidicsMutationResponse> RunPumpAsync(
        RunPumpRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.pump.run",
            request,
            actor,
            async () =>
            {
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    return await RunPumpViaMainControllerAsync(request, actor, cancellationToken);
                }

                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var pump = await RequirePumpAsync(request.PwmChannelCode, cancellationToken);
                    EnsurePumpCanOperate(pump);
                    await ApplyPumpRunAsync(
                        pump,
                        request.CommandId,
                        request.SpeedPercent,
                        request.DurationMs,
                        request.TargetPointCode,
                        null,
                        null,
                        null,
                        cancellationToken);
                    AddAudit(actor, "fluidics.pump.run", "PumpChannelState", pump.Id, request.CommandId, new
                    {
                        pump.PwmChannelCode,
                        pump.DrawerCode,
                        request.SpeedPercent,
                        request.DurationMs,
                        request.TargetPointCode,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Pump command completed.", await BuildResponseAsync(cancellationToken)),
                        "PumpChannelState",
                        pump.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> StopPumpAsync(
        StopPumpRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.pump.stop",
            request,
            actor,
            async () =>
            {
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    return await StopPumpViaMainControllerAsync(request, actor, cancellationToken);
                }

                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var pump = await RequirePumpAsync(request.PwmChannelCode, cancellationToken);
                    if (!pump.IsConnected)
                    {
                        throw new BusinessRuleException("pump_disconnected", "Disconnected pump channel cannot accept a stop command.", StatusCodes.Status409Conflict);
                    }

                    ApplyPumpStop(pump, request.CommandId, FluidicsStatuses.Stopped, null, null, null);
                    AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
                    PublishPump(pump, "stopped");
                    AddAudit(actor, "fluidics.pump.stop", "PumpChannelState", pump.Id, request.CommandId, new { pump.PwmChannelCode, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Pump stopped.", await BuildResponseAsync(cancellationToken)),
                        "PumpChannelState",
                        pump.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> WashAsync(
        WashTargetRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.wash",
            request,
            actor,
            async () =>
            {
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessRuleException(
                        "wash_target_real_not_implemented",
                        "Wash target control is not a PWM pump path; Real mode target wash control is not implemented yet.",
                        StatusCodes.Status409Conflict);
                }

                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var wash = await RequireWashTargetAsync(request.TargetPointCode, cancellationToken);
                    var pump = await RequirePumpAsync("PWM0", cancellationToken);
                    EnsurePumpCanOperate(pump);
                    await ApplyPumpRunAsync(
                        pump,
                        request.CommandId,
                        request.SpeedPercent,
                        request.DurationMs ?? 50,
                        wash.Code,
                        null,
                        null,
                        null,
                        cancellationToken);
                    AddAudit(actor, $"fluidics.wash.{wash.WashType.ToLowerInvariant()}", "WashPosition", wash.Id, request.CommandId, new
                    {
                        wash.Code,
                        wash.WashType,
                        request.SpeedPercent,
                        request.DurationMs,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, $"Wash {wash.WashType} command completed.", await BuildResponseAsync(cancellationToken)),
                        "WashPosition",
                        wash.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> StopWashAsync(
        StopWashRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.wash.stop",
            request,
            actor,
            async () =>
            {
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessRuleException(
                        "wash_target_real_not_implemented",
                        "Wash target control is not a PWM pump path; Real mode target wash control is not implemented yet.",
                        StatusCodes.Status409Conflict);
                }

                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    // wash 由 PWM0 驱动；wash-stop 是独立业务命令（非裸 PWM=0），停止 wash 泵。
                    var pump = await RequirePumpAsync("PWM0", cancellationToken);
                    ApplyPumpStop(pump, request.CommandId, FluidicsStatuses.Stopped, null, null, null);
                    AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
                    PublishPump(pump, "stopped");
                    AddAudit(actor, "fluidics.wash.stop", "PumpChannelState", pump.Id, request.CommandId, new { pump.PwmChannelCode, pump.TargetPointCode, request.Reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Sample wash stopped.", await BuildResponseAsync(cancellationToken)),
                        "PumpChannelState",
                        pump.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    private Task<CommandExecutionResult<FluidicsMutationResponse>> RunPumpViaMainControllerAsync(
        RunPumpRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        return RunRealPumpCommandAsync(
            request.CommandId,
            actor,
            request.PwmChannelCode,
            request.SpeedPercent,
            request.DurationMs,
            request.TargetPointCode,
            "fluidics.pump.run",
            new { request.PwmChannelCode, request.SpeedPercent, request.DurationMs, request.TargetPointCode, request.Reason },
            cancellationToken);
    }

    private Task<CommandExecutionResult<FluidicsMutationResponse>> StopPumpViaMainControllerAsync(
        StopPumpRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        return StopRealPumpCommandAsync(
            request.CommandId,
            actor,
            request.PwmChannelCode,
            "fluidics.pump.stop",
            new { request.PwmChannelCode, request.Reason },
            cancellationToken);
    }

    private async Task<CommandExecutionResult<FluidicsMutationResponse>> RunRealPumpCommandAsync(
        string commandId,
        AuthenticatedUser actor,
        string pwmChannelCode,
        int speedPercent,
        int? durationMs,
        string? targetPointCode,
        string auditAction,
        object auditDetails,
        CancellationToken cancellationToken)
    {
        ValidateSpeed(speedPercent);
        var normalizedPwm = NormalizePwm(pwmChannelCode);
        await EnsureRealPumpReadyAsync(normalizedPwm, targetPointCode, cancellationToken);

        var startResult = await SendRealPumpPwmAsync(commandId, actor, normalizedPwm, speedPercent, cancellationToken);
        if (!startResult.Ok)
        {
            return await FailRealPumpCommandAsync(normalizedPwm, commandId, actor, auditAction, auditDetails, startResult, cancellationToken);
        }

        var started = await ApplyRealPumpStartedAsync(normalizedPwm, commandId, actor, speedPercent, durationMs, targetPointCode, auditAction, auditDetails, cancellationToken);
        if (speedPercent == 0 || !durationMs.HasValue)
        {
            return started;
        }

        await Task.Delay(Math.Max(1, durationMs.Value), cancellationToken);
        var stopResult = await SendRealPumpPwmAsync($"{commandId}:auto-stop", actor, normalizedPwm, 0, cancellationToken);
        if (!stopResult.Ok)
        {
            return await FailRealPumpCommandAsync(normalizedPwm, commandId, actor, auditAction, auditDetails, stopResult, cancellationToken);
        }

        return await ApplyRealPumpStoppedAsync(normalizedPwm, commandId, actor, FluidicsStatuses.Completed, auditAction, auditDetails, "Pump command completed via main controller.", cancellationToken);
    }

    private async Task<CommandExecutionResult<FluidicsMutationResponse>> StopRealPumpCommandAsync(
        string commandId,
        AuthenticatedUser actor,
        string pwmChannelCode,
        string auditAction,
        object auditDetails,
        CancellationToken cancellationToken)
    {
        var normalizedPwm = NormalizePwm(pwmChannelCode);
        await EnsureRealPumpReadyAsync(normalizedPwm, null, cancellationToken);
        var stopResult = await SendRealPumpPwmAsync(commandId, actor, normalizedPwm, 0, cancellationToken);
        if (!stopResult.Ok)
        {
            return await FailRealPumpCommandAsync(normalizedPwm, commandId, actor, auditAction, auditDetails, stopResult, cancellationToken);
        }

        return await ApplyRealPumpStoppedAsync(normalizedPwm, commandId, actor, FluidicsStatuses.Stopped, auditAction, auditDetails, "Pump stopped via main controller.", cancellationToken);
    }

    private async Task EnsureRealPumpReadyAsync(string normalizedPwm, string? targetPointCode, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pump = await RequirePumpAsync(normalizedPwm, cancellationToken);
            EnsurePumpCanOperate(pump);
            if (!string.IsNullOrWhiteSpace(targetPointCode))
            {
                _ = await RequireWashTargetAsync(targetPointCode, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<DeviceCommandResult> SendRealPumpPwmAsync(
        string commandId,
        AuthenticatedUser actor,
        string normalizedPwm,
        int value,
        CancellationToken cancellationToken)
    {
        var deviceRequest = new DeviceOperationRequest(
            new DeviceCommandContext(commandId, null, actor.UserId, nameof(FluidicsControlService)),
            DeviceModules.Pump,
            "set-pwm",
            new Dictionary<string, object?>
            {
                ["pwmId"] = PwmMap[normalizedPwm].No,
                ["pwmChannelCode"] = normalizedPwm,
                ["value"] = value,
                ["speedPercent"] = value
            });

        return await deviceAdapter.RunPumpAsync(deviceRequest, cancellationToken);
    }

    private async Task<CommandExecutionResult<FluidicsMutationResponse>> ApplyRealPumpStartedAsync(
        string normalizedPwm,
        string commandId,
        AuthenticatedUser actor,
        int speedPercent,
        int? durationMs,
        string? targetPointCode,
        string auditAction,
        object auditDetails,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pump = await RequirePumpAsync(normalizedPwm, cancellationToken);
            await ApplyPumpRunAsync(pump, commandId, speedPercent, durationMs, targetPointCode, null, null, null, cancellationToken, autoCompleteAfterDuration: false);
            AddAudit(actor, auditAction, "PumpChannelState", pump.Id, commandId, new { source = "MainController", ok = true, details = auditDetails });
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CommandExecutionResult<FluidicsMutationResponse>(
                new FluidicsMutationResponse(true, commandId, false, "Pump command applied via main controller.", await BuildResponseAsync(cancellationToken)),
                "PumpChannelState",
                pump.Id);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<CommandExecutionResult<FluidicsMutationResponse>> ApplyRealPumpStoppedAsync(
        string normalizedPwm,
        string commandId,
        AuthenticatedUser actor,
        string status,
        string auditAction,
        object auditDetails,
        string message,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pump = await RequirePumpAsync(normalizedPwm, cancellationToken);
            ApplyPumpStop(pump, commandId, status, null, null, null);
            AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
            PublishPump(pump, status == FluidicsStatuses.Completed ? "completed" : "stopped");
            AddAudit(actor, auditAction, "PumpChannelState", pump.Id, commandId, new { source = "MainController", ok = true, stopped = true, details = auditDetails });
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CommandExecutionResult<FluidicsMutationResponse>(
                new FluidicsMutationResponse(true, commandId, false, message, await BuildResponseAsync(cancellationToken)),
                "PumpChannelState",
                pump.Id);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<CommandExecutionResult<FluidicsMutationResponse>> FailRealPumpCommandAsync(
        string normalizedPwm,
        string commandId,
        AuthenticatedUser actor,
        string auditAction,
        object auditDetails,
        DeviceCommandResult deviceResult,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pump = await RequirePumpAsync(normalizedPwm, cancellationToken);
            pump.Status = deviceResult.Status == DeviceCommandStatuses.TimedOut ? FluidicsStatuses.TimedOut : FluidicsStatuses.Unknown;
            pump.FaultCode = deviceResult.ErrorCode ?? "wash_pwm_command_failed";
            pump.FaultMessage = deviceResult.Message;
            pump.CurrentCommandId = commandId;
            pump.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
            AddFluidicsAlarm($"fluidics_pump_{pump.PwmChannelCode}_{pump.FaultCode}", pump.FaultMessage, null);
            PublishPump(pump, "mainControllerFailed");
            AddAudit(actor, auditAction, "PumpChannelState", pump.Id, commandId, new { source = "MainController", ok = false, deviceResult.ErrorCode, details = auditDetails });
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException(
                deviceResult.ErrorCode ?? "wash_pwm_command_failed",
                deviceResult.Message,
                StatusCodes.Status409Conflict);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<FluidicsMutationResponse> StartMixerAsync(
        string drawerCode,
        MixerCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ChangeMixerAsync(drawerCode, request, actor, "fluidics.mixer.start", FluidicsStatuses.Running, "Mixer started.", cancellationToken);
    }

    public Task<FluidicsMutationResponse> CompleteMixerAsync(
        string drawerCode,
        MixerCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ChangeMixerAsync(drawerCode, request, actor, "fluidics.mixer.complete", FluidicsStatuses.Completed, "Mixer completed.", cancellationToken);
    }

    public Task<FluidicsMutationResponse> StopMixerAsync(
        string drawerCode,
        MixerCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ChangeMixerAsync(drawerCode, request, actor, "fluidics.mixer.stop", FluidicsStatuses.Stopped, "Mixer stopped.", cancellationToken);
    }

    public Task<FluidicsMutationResponse> SetLiquidLevelAsync(
        SetLiquidLevelRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.liquid_level.set",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var liquid = await RequireLiquidAsync(request.SourceType, cancellationToken);
                    var status = NormalizeLiquidLevelStatus(request.LevelStatus);
                    liquid.LevelStatus = status;
                    liquid.IsConnected = status != LiquidLevelStatuses.Disconnected;
                    liquid.FaultCode = status is LiquidLevelStatuses.SensorFault or LiquidLevelStatuses.Disconnected ? status : null;
                    liquid.FaultMessage = status is LiquidLevelStatuses.SensorFault or LiquidLevelStatuses.Disconnected ? RequireReason(request.Reason) : null;
                    if (request.CurrentVolumeUl.HasValue)
                    {
                        liquid.CurrentVolumeUl = Math.Clamp(request.CurrentVolumeUl.Value, 0, liquid.CapacityUl);
                    }
                    else
                    {
                        ApplyRepresentativeVolume(liquid, status);
                    }

                    liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.LiquidLevelChanged, request.CommandId, null, null, null);
                    AddLiquidAlarmIfNeeded(liquid, null);
                    PublishLiquid(liquid, "levelChanged", request.CommandId);
                    if (!IsLiquidReady(liquid))
                    {
                        await MarkActiveRunsFaultedAsync($"Liquid level {liquid.SourceType} is {liquid.LevelStatus}.", $"liquid_{liquid.SourceType}_{liquid.LevelStatus}", cancellationToken);
                    }

                    AddAudit(actor, "fluidics.liquid_level.set", "LiquidContainerState", liquid.Id, request.CommandId, new
                    {
                        liquid.SourceType,
                        liquid.LevelStatus,
                        liquid.CurrentVolumeUl,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Liquid level updated.", await BuildResponseAsync(cancellationToken)),
                        "LiquidContainerState",
                        liquid.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> SetLiquidThresholdAsync(
        SetLiquidThresholdRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.threshold.set",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var liquid = await RequireLiquidAsync(request.SourceType, cancellationToken);
                    _ = RequireReason(request.Reason);
                    if(request.LowThresholdUl.HasValue) liquid.LowThresholdUl = Math.Clamp(request.LowThresholdUl.Value, 0, liquid.CapacityUl);
                    if(request.FullThresholdUl.HasValue) liquid.FullThresholdUl = Math.Clamp(request.FullThresholdUl.Value, 0, liquid.CapacityUl);
                    liquid.LevelStatus = CalculateLiquidStatus(liquid);
                    liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddAudit(actor, "fluidics.threshold.set", "LiquidContainerState", liquid.Id, request.CommandId, new
                    {
                        liquid.SourceType,
                        liquid.LowThresholdUl,
                        liquid.FullThresholdUl,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Liquid thresholds updated.", await BuildResponseAsync(cancellationToken)),
                        "LiquidContainerState",
                        liquid.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> ConfigureFaultAsync(
        ConfigureFluidicsFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.fault.configure",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var targetId = await ApplyFaultAsync(request, cancellationToken);
                    AddAudit(actor, "fluidics.fault.configured", "FluidicsState", targetId, request.CommandId, new
                    {
                        request.TargetType,
                        request.PwmChannelCode,
                        request.DrawerCode,
                        request.SourceType,
                        request.FaultType,
                        request.ErrorCode,
                        request.Message,
                        request.Reason
                    });
                    await MarkActiveRunsFaultedAsync(request.Message ?? request.Reason, "fluidics_fault", cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Fluidics fault configured.", await BuildResponseAsync(cancellationToken)),
                        "FluidicsState",
                        targetId);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<FluidicsMutationResponse> ClearFaultAsync(
        ClearFluidicsFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "fluidics.fault.clear",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var targetId = await ClearFaultCoreAsync(request, cancellationToken);
                    var alarms = await dbContext.Alarms.Where(x => x.Status == "Active" && x.Code.StartsWith("fluidics_")).ToListAsync(cancellationToken);
                    foreach (var alarm in alarms)
                    {
                        alarm.Status = "Cleared";
                        alarm.ClearedAtUtc = DateTimeOffset.UtcNow;
                    }

                    AddAudit(actor, "fluidics.fault.cleared", "FluidicsState", targetId, request.CommandId, new
                    {
                        request.TargetType,
                        request.PwmChannelCode,
                        request.DrawerCode,
                        request.SourceType,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, "Fluidics fault cleared.", await BuildResponseAsync(cancellationToken)),
                        "FluidicsState",
                        targetId);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public async Task<FluidicsDeviceResult> InitializeModuleAsync(string moduleCode, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            return moduleCode switch
            {
                DeviceModules.Pump => PumpReadinessResult(await dbContext.PumpChannelStates.AsNoTracking().ToListAsync(cancellationToken)),
                DeviceModules.Mixer => MixerReadinessResult(await dbContext.MixerChannelStates.AsNoTracking().ToListAsync(cancellationToken)),
                DeviceModules.LiquidLevel => LiquidReadinessResult(await dbContext.LiquidContainerStates.AsNoTracking().ToListAsync(cancellationToken)),
                _ => FluidicsDeviceResult.Failed("fluidics_module_invalid", "Unsupported fluidics module.", DeviceCommandStatuses.NotSupported)
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<FluidicsDeviceResult> RunPumpFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var speed = Convert.ToInt32(request.Parameters.GetValueOrDefault("speedPercent")
                ?? request.Parameters.GetValueOrDefault("pwm")
                ?? DefaultSpeedForAction(request.Action));
            var duration = ReadNullableInt(request.Parameters, "durationMs")
                ?? ReadNullableInt(request.Parameters, "durationSeconds") * 1000
                ?? 25;
            var pwm = Convert.ToString(request.Parameters.GetValueOrDefault("pwmChannelCode")) ?? DrawerToPwm(Convert.ToString(request.Parameters.GetValueOrDefault("drawerCode")));
            var targetPoint = Convert.ToString(request.Parameters.GetValueOrDefault("targetPointCode"));
            if (string.IsNullOrWhiteSpace(targetPoint) && IsWashAction(request.Action))
            {
                targetPoint = ResolveDefaultWashTarget(request.Action);
            }

            var pump = await RequirePumpAsync(pwm, cancellationToken);
            if (!CanPumpOperate(pump))
            {
                return FluidicsDeviceResult.Failed(pump.FaultCode ?? "pump_not_ready", pump.FaultMessage ?? "Pump channel is not ready.", MapStatusToDeviceStatus(pump.Status), PumpData(pump));
            }

            await ApplyPumpRunAsync(
                pump,
                request.Context.CommandId,
                speed,
                duration,
                targetPoint,
                Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId")),
                Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId")),
                request.Context.CommandId,
                cancellationToken);
            AddAudit(null, $"device.pump.{request.Action}", "PumpChannelState", pump.Id, request.Context.CommandId, new { request.ModuleCode, request.Action, request.Parameters });
            await dbContext.SaveChangesAsync(cancellationToken);
            return FluidicsDeviceResult.Succeeded("Pump command completed.", PumpData(pump));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<FluidicsDeviceResult> ReadLiquidLevelsFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var liquids = await dbContext.LiquidContainerStates.ToListAsync(cancellationToken);
            var sampledAtUtc = DateTimeOffset.UtcNow;
            foreach (var liquid in liquids)
            {
                // A successful Mock sensor read represents a new sample of the current
                // simulated values. Keep the values/faults intact, but refresh their age
                // so the subsequent strict precheck validates this read, not seed age.
                liquid.UpdatedAtUtc = sampledAtUtc;
                AddLiquidTelemetry(
                    liquid,
                    FluidicsTelemetryEventTypes.LiquidLevelChanged,
                    request.Context.CommandId,
                    Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId")),
                    Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId")),
                    request.Context.CommandId);
                AddLiquidAlarmIfNeeded(liquid, Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId")));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var bad = liquids.FirstOrDefault(x => !IsLiquidReady(x));
            return bad is null
                ? FluidicsDeviceResult.Succeeded("Liquid levels are ready.", LiquidLevelsData(liquids))
                : FluidicsDeviceResult.Failed(bad.FaultCode ?? $"liquid_{bad.LevelStatus}", $"Liquid level {bad.SourceType} is {bad.LevelStatus}.", DeviceCommandStatuses.Failed, LiquidData(bad));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<FluidicsDeviceResult> MixFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var drawerCode = NormalizeDrawer(Convert.ToString(request.Parameters.GetValueOrDefault("drawerCode")));
            var mixer = await RequireMixerAsync(drawerCode, cancellationToken);
            if (!CanMixerOperate(mixer))
            {
                return FluidicsDeviceResult.Failed(mixer.FaultCode ?? "mixer_not_ready", mixer.FaultMessage ?? "Mixer channel is not ready.", MapStatusToDeviceStatus(mixer.Status), MixerData(mixer));
            }

            var roundKey = Convert.ToString(request.Parameters.GetValueOrDefault("roundKey"))
                ?? $"{request.Parameters.GetValueOrDefault("machineRunId")}:{drawerCode}:{request.Parameters.GetValueOrDefault("majorStepCode")}";
            ApplyMixerState(
                mixer,
                FluidicsStatuses.Running,
                request.Context.CommandId,
                roundKey,
                Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId")),
                Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId")),
                request.Context.CommandId);
            AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.MixerChanged);
            PublishMixer(mixer, "started");
            await Task.Delay(5, cancellationToken);
            ApplyMixerState(
                mixer,
                FluidicsStatuses.Completed,
                request.Context.CommandId,
                roundKey,
                Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId")),
                Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId")),
                request.Context.CommandId);
            AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.MixerChanged);
            PublishMixer(mixer, "completed");
            AddAudit(null, $"device.mixer.{request.Action}", "MixerChannelState", mixer.Id, request.Context.CommandId, new { request.ModuleCode, request.Action, request.Parameters });
            await dbContext.SaveChangesAsync(cancellationToken);
            return FluidicsDeviceResult.Succeeded("Mixer command completed.", MixerData(mixer));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<FluidicsDeviceResult> ConsumeSystemLiquidFromRunAsync(
        string sourceType,
        int volumeUl,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var liquid = await RequireLiquidAsync(sourceType, cancellationToken);
            if (liquid.IsWaste)
            {
                return FluidicsDeviceResult.Failed("liquid_source_invalid", "Waste containers cannot be consumed as a source.", DeviceCommandStatuses.Failed, LiquidData(liquid));
            }

            if (!IsLiquidReady(liquid) || liquid.CurrentVolumeUl < volumeUl)
            {
                liquid.LevelStatus = liquid.CurrentVolumeUl <= 0 ? LiquidLevelStatuses.Empty : LiquidLevelStatuses.Low;
                liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.LiquidLevelChanged, command.Id, run.Id, step.Id, command.Id);
                AddLiquidAlarmIfNeeded(liquid, run.Id);
                await dbContext.SaveChangesAsync(cancellationToken);
                return FluidicsDeviceResult.Failed("liquid_source_insufficient", $"Liquid source {sourceType} is insufficient.", DeviceCommandStatuses.Failed, LiquidData(liquid));
            }

            liquid.CurrentVolumeUl -= volumeUl;
            liquid.LevelStatus = CalculateLiquidStatus(liquid);
            liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.LiquidLevelChanged, command.Id, run.Id, step.Id, command.Id);
            AddLiquidAlarmIfNeeded(liquid, run.Id);
            PublishLiquid(liquid, "consumed", command.Id);
            AddAudit(null, "run.system_liquid_consumption", "LiquidContainerState", liquid.Id, command.Id, new
            {
                runId = run.Id,
                workflowStepExecutionId = step.Id,
                deviceCommandExecutionId = command.Id,
                sourceType,
                volumeUl,
                liquid.CurrentVolumeUl,
                liquid.LevelStatus
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return FluidicsDeviceResult.Succeeded("System liquid consumed.", LiquidData(liquid));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RecordAdapterFaultAsync(
        string moduleCode,
        DeviceFaultPlanSnapshot fault,
        DeviceOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            if (moduleCode == DeviceModules.Pump)
            {
                var pump = await RequirePumpAsync(Convert.ToString(request.Parameters.GetValueOrDefault("pwmChannelCode")) ?? DrawerToPwm(Convert.ToString(request.Parameters.GetValueOrDefault("drawerCode"))), cancellationToken);
                ApplyFault(pump, MapGenericFault(fault.FaultType), fault.ErrorCode ?? "mock_fault", fault.Message, request.Context.CommandId);
                AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.FaultConfigured);
                PublishPump(pump, "faultConfigured");
            }
            else if (moduleCode == DeviceModules.Mixer)
            {
                var mixer = await RequireMixerAsync(Convert.ToString(request.Parameters.GetValueOrDefault("drawerCode")), cancellationToken);
                ApplyFault(mixer, MapGenericFault(fault.FaultType), fault.ErrorCode ?? "mock_fault", fault.Message, request.Context.CommandId);
                AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.FaultConfigured);
                PublishMixer(mixer, "faultConfigured");
            }
            else if (moduleCode == DeviceModules.LiquidLevel)
            {
                var sourceType = Convert.ToString(request.Parameters.GetValueOrDefault("sourceType"));
                var liquid = string.IsNullOrWhiteSpace(sourceType)
                    ? (await dbContext.LiquidContainerStates.OrderBy(x => x.SourceType).FirstAsync(cancellationToken))
                    : await RequireLiquidAsync(sourceType, cancellationToken);
                ApplyFault(liquid, MapGenericFault(fault.FaultType), fault.ErrorCode ?? "mock_fault", fault.Message, request.Context.CommandId);
                AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.FaultConfigured, request.Context.CommandId, null, null, request.Context.CommandId);
                PublishLiquid(liquid, "faultConfigured", request.Context.CommandId);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RecordDeviceFailureFromExecutorAsync(
        string moduleCode,
        string deviceStatus,
        string? errorCode,
        string message,
        string? pwmChannelCode,
        string? drawerCode,
        string? sourceType,
        string machineRunId,
        string workflowStepExecutionId,
        string deviceCommandExecutionId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var faultType = FaultTypeFromDeviceStatus(deviceStatus, errorCode);
            var finalErrorCode = string.IsNullOrWhiteSpace(errorCode) ? faultType : errorCode.Trim();
            if (moduleCode == DeviceModules.Pump)
            {
                var pump = await RequirePumpAsync(pwmChannelCode ?? DrawerToPwm(drawerCode), cancellationToken);
                ApplyFault(pump, faultType, finalErrorCode, message, deviceCommandExecutionId);
                pump.MachineRunId = machineRunId;
                pump.WorkflowStepExecutionId = workflowStepExecutionId;
                pump.DeviceCommandExecutionId = deviceCommandExecutionId;
                AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.FaultConfigured);
                PublishPump(pump, "deviceFailure");
                AddFluidicsAlarm($"fluidics_pump_{pump.PwmChannelCode}_{faultType}", message, machineRunId);
            }
            else if (moduleCode == DeviceModules.Mixer)
            {
                var mixer = await RequireMixerAsync(drawerCode, cancellationToken);
                ApplyFault(mixer, faultType, finalErrorCode, message, deviceCommandExecutionId);
                mixer.MachineRunId = machineRunId;
                mixer.WorkflowStepExecutionId = workflowStepExecutionId;
                mixer.DeviceCommandExecutionId = deviceCommandExecutionId;
                AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.FaultConfigured);
                PublishMixer(mixer, "deviceFailure");
                AddFluidicsAlarm($"fluidics_mixer_{mixer.DrawerCode}_{faultType}", message, machineRunId);
            }
            else if (moduleCode == DeviceModules.LiquidLevel)
            {
                var liquid = string.IsNullOrWhiteSpace(sourceType)
                    ? await dbContext.LiquidContainerStates.OrderBy(x => x.SourceType).FirstAsync(cancellationToken)
                    : await RequireLiquidAsync(sourceType, cancellationToken);
                ApplyFault(liquid, faultType, finalErrorCode, message, deviceCommandExecutionId);
                AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.FaultConfigured, deviceCommandExecutionId, machineRunId, workflowStepExecutionId, deviceCommandExecutionId);
                PublishLiquid(liquid, "deviceFailure", deviceCommandExecutionId);
                AddLiquidAlarmIfNeeded(liquid, machineRunId);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<FluidicsReadinessResult> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pumps = await dbContext.PumpChannelStates.AsNoTracking().ToListAsync(cancellationToken);
            var pump = pumps.FirstOrDefault(x => !CanPumpBeReady(x));
            if (pump is not null)
            {
                return new FluidicsReadinessResult(false, "pump_not_ready", $"Pump {pump.PwmChannelCode}/{pump.DrawerCode} is {pump.Status}; fault={pump.FaultCode ?? "none"}.");
            }

            var mixers = await dbContext.MixerChannelStates.AsNoTracking().ToListAsync(cancellationToken);
            var mixer = mixers.FirstOrDefault(x => !CanMixerBeReady(x));
            if (mixer is not null)
            {
                return new FluidicsReadinessResult(false, "mixer_not_ready", $"Mixer {mixer.DrawerCode} is {mixer.Status}; fault={mixer.FaultCode ?? "none"}.");
            }

            var liquids = await dbContext.LiquidContainerStates.AsNoTracking().ToListAsync(cancellationToken);
            var liquid = liquids.FirstOrDefault(x => !IsLiquidReady(x));
            if (liquid is not null)
            {
                return new FluidicsReadinessResult(false, "liquid_level_not_ready", $"Liquid level {liquid.SourceType} is {liquid.LevelStatus}; fault={liquid.FaultCode ?? "none"}.");
            }

            return new FluidicsReadinessResult(true, null, "Pumps, mixers and liquid levels are ready.");
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task EnsureReadyForRunAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetReadinessAsync(cancellationToken);
        if (!result.Ok)
        {
            throw new BusinessRuleException(result.ErrorCode!, result.Message, StatusCodes.Status409Conflict);
        }
    }

    public async Task<IReadOnlyList<FluidicsModuleState>> GetDeviceModuleStatesAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var pumps = await dbContext.PumpChannelStates.AsNoTracking().OrderBy(x => x.PwmChannelNo).ToListAsync(cancellationToken);
            var mixers = await dbContext.MixerChannelStates.AsNoTracking().OrderBy(x => x.ChannelNo).ToListAsync(cancellationToken);
            var liquids = await dbContext.LiquidContainerStates.AsNoTracking().OrderBy(x => x.SourceType).ToListAsync(cancellationToken);
            return
            [
                BuildModuleState(DeviceModules.Pump, pumps.All(CanPumpBeReady), pumps.Any(x => !x.IsConnected), pumps.FirstOrDefault(x => x.FaultCode is not null)?.FaultCode, pumps.FirstOrDefault(x => x.FaultCode is not null)?.FaultMessage, pumps.Select(x => PumpData(x)).ToList()),
                BuildModuleState(DeviceModules.Mixer, mixers.All(CanMixerBeReady), mixers.Any(x => !x.IsConnected), mixers.FirstOrDefault(x => x.FaultCode is not null)?.FaultCode, mixers.FirstOrDefault(x => x.FaultCode is not null)?.FaultMessage, mixers.Select(x => MixerData(x)).ToList()),
                BuildModuleState(DeviceModules.LiquidLevel, liquids.All(IsLiquidReady), liquids.Any(x => !x.IsConnected), liquids.FirstOrDefault(x => !IsLiquidReady(x))?.FaultCode ?? liquids.FirstOrDefault(x => !IsLiquidReady(x))?.LevelStatus, liquids.FirstOrDefault(x => !IsLiquidReady(x))?.FaultMessage, liquids.Select(x => LiquidData(x)).ToList())
            ];
        }
        finally
        {
            Gate.Release();
        }
    }

    private Task<FluidicsMutationResponse> ChangeMixerAsync(
        string drawerCode,
        MixerCommandRequest request,
        AuthenticatedUser actor,
        string operation,
        string targetStatus,
        string message,
        CancellationToken cancellationToken)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            operation,
            new { drawerCode, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var mixer = await RequireMixerAsync(drawerCode, cancellationToken);
                    if (targetStatus == FluidicsStatuses.Running)
                    {
                        EnsureMixerCanOperate(mixer);
                    }
                    else if (!mixer.IsConnected)
                    {
                        throw new BusinessRuleException("mixer_disconnected", "Disconnected mixer channel cannot accept a command.", StatusCodes.Status409Conflict);
                    }

                    ApplyMixerState(mixer, targetStatus, request.CommandId, request.RoundKey, null, null, null);
                    AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.MixerChanged);
                    PublishMixer(mixer, targetStatus.ToLowerInvariant());
                    AddAudit(actor, operation, "MixerChannelState", mixer.Id, request.CommandId, new
                    {
                        mixer.DrawerCode,
                        targetStatus,
                        request.RoundKey,
                        request.Reason
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<FluidicsMutationResponse>(
                        new FluidicsMutationResponse(true, request.CommandId, false, message, await BuildResponseAsync(cancellationToken)),
                        "MixerChannelState",
                        mixer.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    private async Task EnsureSeededCoreAsync(CancellationToken cancellationToken)
    {
        if (!await dbContext.PumpChannelStates.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (code, map) in PwmMap.OrderBy(x => x.Value.No))
            {
                dbContext.PumpChannelStates.Add(new PumpChannelState
                {
                    PwmChannelCode = code,
                    PwmChannelNo = map.No,
                    DrawerCode = map.Drawer,
                    UpdatedAtUtc = now
                });
            }
        }

        if (!await dbContext.MixerChannelStates.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (drawer, index) in new[] { "A", "B", "C", "D" }.Select((value, index) => (value, index)))
            {
                dbContext.MixerChannelStates.Add(new MixerChannelState
                {
                    DrawerCode = drawer,
                    ChannelNo = index,
                    UpdatedAtUtc = now
                });
            }
        }

        if (!await dbContext.LiquidContainerStates.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            dbContext.LiquidContainerStates.AddRange(
                NewLiquid(LiquidSourceTypes.SystemWater, "Water", false, 1_000_000, 850_000, 100_000, 900_000, now),
                NewLiquid(LiquidSourceTypes.Pbs, "PBS", false, 1_000_000, 850_000, 100_000, 900_000, now),
                NewLiquid(LiquidSourceTypes.Waste, "Waste", true, 1_000_000, 100_000, 100_000, 900_000, now),
                NewLiquid(LiquidSourceTypes.ToxicWaste, "Toxic waste", true, 500_000, 50_000, 50_000, 450_000, now));
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static LiquidContainerState NewLiquid(string sourceType, string displayName, bool isWaste, int capacity, int current, int low, int full, DateTimeOffset now) => new()
    {
        SourceType = sourceType,
        DisplayName = displayName,
        IsWaste = isWaste,
        CapacityUl = capacity,
        CurrentVolumeUl = current,
        LowThresholdUl = low,
        FullThresholdUl = full,
        LevelStatus = LiquidLevelStatuses.Normal,
        UpdatedAtUtc = now
    };

    private async Task ApplyPumpRunAsync(
        PumpChannelState pump,
        string commandId,
        int speedPercent,
        int? durationMs,
        string? targetPointCode,
        string? machineRunId,
        string? workflowStepExecutionId,
        string? deviceCommandExecutionId,
        CancellationToken cancellationToken,
        bool autoCompleteAfterDuration = true)
    {
        ValidateSpeed(speedPercent);
        if (!string.IsNullOrWhiteSpace(targetPointCode))
        {
            _ = await RequireWashTargetAsync(targetPointCode, cancellationToken);
        }

        var finalTargetPoint = string.IsNullOrWhiteSpace(targetPointCode) ? null : targetPointCode.Trim();
        pump.SpeedPercent = speedPercent;
        pump.Direction = DirectionFor(speedPercent);
        pump.Status = speedPercent == 0 ? FluidicsStatuses.Stopped : FluidicsStatuses.Running;
        pump.TargetPointCode = finalTargetPoint;
        pump.DurationMs = durationMs;
        pump.CurrentCommandId = commandId;
        pump.MachineRunId = string.IsNullOrWhiteSpace(machineRunId) ? null : machineRunId;
        pump.WorkflowStepExecutionId = string.IsNullOrWhiteSpace(workflowStepExecutionId) ? null : workflowStepExecutionId;
        pump.DeviceCommandExecutionId = string.IsNullOrWhiteSpace(deviceCommandExecutionId) ? null : deviceCommandExecutionId;
        pump.FaultCode = null;
        pump.FaultMessage = null;
        pump.IsConnected = true;
        pump.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
        PublishPump(pump, "started");

        if (autoCompleteAfterDuration && speedPercent != 0 && durationMs.HasValue)
        {
            await Task.Delay(Math.Clamp(durationMs.Value, 1, 25), cancellationToken);
            ApplyPumpStop(pump, commandId, FluidicsStatuses.Completed, machineRunId, workflowStepExecutionId, deviceCommandExecutionId);
            AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.PumpChanged);
            PublishPump(pump, "completed");
        }
    }

    private static void ApplyPumpStop(PumpChannelState pump, string commandId, string status, string? machineRunId, string? workflowStepExecutionId, string? deviceCommandExecutionId)
    {
        pump.SpeedPercent = 0;
        pump.Direction = PumpDirections.Stopped;
        pump.Status = status;
        pump.CurrentCommandId = commandId;
        pump.MachineRunId = string.IsNullOrWhiteSpace(machineRunId) ? pump.MachineRunId : machineRunId;
        pump.WorkflowStepExecutionId = string.IsNullOrWhiteSpace(workflowStepExecutionId) ? pump.WorkflowStepExecutionId : workflowStepExecutionId;
        pump.DeviceCommandExecutionId = string.IsNullOrWhiteSpace(deviceCommandExecutionId) ? pump.DeviceCommandExecutionId : deviceCommandExecutionId;
        pump.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyMixerState(
        MixerChannelState mixer,
        string status,
        string commandId,
        string? roundKey,
        string? machineRunId,
        string? workflowStepExecutionId,
        string? deviceCommandExecutionId)
    {
        mixer.Status = status;
        mixer.CurrentCommandId = commandId;
        mixer.CurrentRoundKey = string.IsNullOrWhiteSpace(roundKey) ? mixer.CurrentRoundKey : roundKey;
        mixer.MachineRunId = string.IsNullOrWhiteSpace(machineRunId) ? mixer.MachineRunId : machineRunId;
        mixer.WorkflowStepExecutionId = string.IsNullOrWhiteSpace(workflowStepExecutionId) ? mixer.WorkflowStepExecutionId : workflowStepExecutionId;
        mixer.DeviceCommandExecutionId = string.IsNullOrWhiteSpace(deviceCommandExecutionId) ? mixer.DeviceCommandExecutionId : deviceCommandExecutionId;
        if (status is FluidicsStatuses.Running or FluidicsStatuses.Completed or FluidicsStatuses.Stopped)
        {
            mixer.FaultCode = null;
            mixer.FaultMessage = null;
            mixer.IsConnected = true;
        }

        mixer.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<string> ApplyFaultAsync(ConfigureFluidicsFaultRequest request, CancellationToken cancellationToken)
    {
        var targetType = NormalizeTargetType(request.TargetType);
        var faultType = NormalizeFaultType(request.FaultType);
        var reason = RequireReason(request.Reason);
        var errorCode = string.IsNullOrWhiteSpace(request.ErrorCode) ? faultType : request.ErrorCode.Trim();
        var message = string.IsNullOrWhiteSpace(request.Message) ? reason : request.Message.Trim();
        if (targetType == "Pump")
        {
            var pump = await RequirePumpAsync(request.PwmChannelCode, cancellationToken);
            ApplyFault(pump, faultType, errorCode, message, request.CommandId);
            AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.FaultConfigured);
            PublishPump(pump, "faultConfigured");
            AddFluidicsAlarm($"fluidics_pump_{pump.PwmChannelCode}_{faultType}", message, null);
            return pump.Id;
        }

        if (targetType == "Mixer")
        {
            var mixer = await RequireMixerAsync(request.DrawerCode, cancellationToken);
            ApplyFault(mixer, faultType, errorCode, message, request.CommandId);
            AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.FaultConfigured);
            PublishMixer(mixer, "faultConfigured");
            AddFluidicsAlarm($"fluidics_mixer_{mixer.DrawerCode}_{faultType}", message, null);
            return mixer.Id;
        }

        var liquid = await RequireLiquidAsync(request.SourceType, cancellationToken);
        ApplyFault(liquid, faultType, errorCode, message, request.CommandId);
        AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.FaultConfigured, request.CommandId, null, null, null);
        PublishLiquid(liquid, "faultConfigured", request.CommandId);
        AddLiquidAlarmIfNeeded(liquid, null);
        return liquid.Id;
    }

    private async Task<string> ClearFaultCoreAsync(ClearFluidicsFaultRequest request, CancellationToken cancellationToken)
    {
        var targetType = NormalizeTargetType(request.TargetType);
        _ = RequireReason(request.Reason);
        if (targetType == "Pump")
        {
            var pump = await RequirePumpAsync(request.PwmChannelCode, cancellationToken);
            pump.FaultCode = null;
            pump.FaultMessage = null;
            pump.IsConnected = true;
            pump.Status = FluidicsStatuses.Idle;
            pump.SpeedPercent = 0;
            pump.Direction = PumpDirections.Stopped;
            pump.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddPumpTelemetry(pump, FluidicsTelemetryEventTypes.FaultCleared);
            PublishPump(pump, "faultCleared");
            return pump.Id;
        }

        if (targetType == "Mixer")
        {
            var mixer = await RequireMixerAsync(request.DrawerCode, cancellationToken);
            mixer.FaultCode = null;
            mixer.FaultMessage = null;
            mixer.IsConnected = true;
            mixer.Status = FluidicsStatuses.Idle;
            mixer.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddMixerTelemetry(mixer, FluidicsTelemetryEventTypes.FaultCleared);
            PublishMixer(mixer, "faultCleared");
            return mixer.Id;
        }

        var liquid = await RequireLiquidAsync(request.SourceType, cancellationToken);
        liquid.FaultCode = null;
        liquid.FaultMessage = null;
        liquid.IsConnected = true;
        liquid.LevelStatus = CalculateLiquidStatus(liquid);
        liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddLiquidTelemetry(liquid, FluidicsTelemetryEventTypes.FaultCleared, request.CommandId, null, null, null);
        PublishLiquid(liquid, "faultCleared", request.CommandId);
        return liquid.Id;
    }

    private static void ApplyFault(PumpChannelState pump, string faultType, string errorCode, string message, string commandId)
    {
        pump.FaultCode = errorCode;
        pump.FaultMessage = message;
        pump.IsConnected = faultType != FluidicsFaultTypes.Disconnected;
        pump.Status = StatusForFault(faultType);
        pump.SpeedPercent = 0;
        pump.Direction = PumpDirections.Stopped;
        pump.CurrentCommandId = commandId;
        pump.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyFault(MixerChannelState mixer, string faultType, string errorCode, string message, string commandId)
    {
        mixer.FaultCode = errorCode;
        mixer.FaultMessage = message;
        mixer.IsConnected = faultType != FluidicsFaultTypes.Disconnected;
        mixer.Status = StatusForFault(faultType);
        mixer.CurrentCommandId = commandId;
        mixer.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyFault(LiquidContainerState liquid, string faultType, string errorCode, string message, string commandId)
    {
        liquid.FaultCode = errorCode;
        liquid.FaultMessage = message;
        liquid.IsConnected = faultType != FluidicsFaultTypes.Disconnected;
        liquid.LevelStatus = faultType == FluidicsFaultTypes.Disconnected
            ? LiquidLevelStatuses.Disconnected
            : LiquidLevelStatuses.SensorFault;
        liquid.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task MarkActiveRunsFaultedAsync(string message, string alarmCode, CancellationToken cancellationToken)
    {
        var activeRuns = await dbContext.MachineRuns
            .Include(x => x.ChannelBatches)
            .Where(x => x.Status == RuntimeLedgerStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var run in activeRuns)
        {
            run.Status = RuntimeLedgerStatus.Faulted;
            run.FaultMessage = message;
            foreach (var batch in run.ChannelBatches)
            {
                batch.Status = RuntimeLedgerStatus.Faulted;
            }

            AddFluidicsAlarm($"fluidics_{alarmCode}", message, run.Id);
            eventPublisher.Publish(MachineEventMessage.Create(
                MachineEventTypes.MachineStateChanged,
                run.Id,
                "MachineRun",
                run.Id,
                null,
                new Dictionary<string, object?>
                {
                    ["runId"] = run.Id,
                    ["status"] = run.Status,
                    ["faultMessage"] = run.FaultMessage,
                    ["message"] = message
                }));
        }
    }

    private void AddPumpTelemetry(PumpChannelState pump, string eventType)
    {
        dbContext.FluidicsTelemetry.Add(new FluidicsTelemetry
        {
            SourceType = FluidicsTelemetrySourceTypes.Pump,
            SourceId = pump.Id,
            EventType = eventType,
            Status = pump.Status,
            PwmChannelCode = pump.PwmChannelCode,
            DrawerCode = pump.DrawerCode,
            SpeedPercent = pump.SpeedPercent,
            Direction = pump.Direction,
            TargetPointCode = pump.TargetPointCode,
            CommandId = pump.CurrentCommandId,
            MachineRunId = pump.MachineRunId,
            WorkflowStepExecutionId = pump.WorkflowStepExecutionId,
            DeviceCommandExecutionId = pump.DeviceCommandExecutionId,
            FaultCode = pump.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddMixerTelemetry(MixerChannelState mixer, string eventType)
    {
        dbContext.FluidicsTelemetry.Add(new FluidicsTelemetry
        {
            SourceType = FluidicsTelemetrySourceTypes.Mixer,
            SourceId = mixer.Id,
            EventType = eventType,
            Status = mixer.Status,
            DrawerCode = mixer.DrawerCode,
            CommandId = mixer.CurrentCommandId,
            MachineRunId = mixer.MachineRunId,
            WorkflowStepExecutionId = mixer.WorkflowStepExecutionId,
            DeviceCommandExecutionId = mixer.DeviceCommandExecutionId,
            FaultCode = mixer.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddLiquidTelemetry(LiquidContainerState liquid, string eventType, string? commandId, string? machineRunId, string? workflowStepExecutionId, string? deviceCommandExecutionId)
    {
        dbContext.FluidicsTelemetry.Add(new FluidicsTelemetry
        {
            SourceType = FluidicsTelemetrySourceTypes.LiquidLevel,
            SourceId = liquid.Id,
            EventType = eventType,
            Status = liquid.LevelStatus,
            LiquidSourceType = liquid.SourceType,
            CurrentVolumeUl = liquid.CurrentVolumeUl,
            CapacityUl = liquid.CapacityUl,
            CommandId = commandId,
            MachineRunId = machineRunId,
            WorkflowStepExecutionId = workflowStepExecutionId,
            DeviceCommandExecutionId = deviceCommandExecutionId,
            FaultCode = liquid.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddAudit(AuthenticatedUser? actor, string action, string entityType, string entityId, string commandId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor?.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(new { commandId, details }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddLiquidAlarmIfNeeded(LiquidContainerState liquid, string? runId)
    {
        if (IsLiquidReady(liquid))
        {
            return;
        }

        AddFluidicsAlarm($"fluidics_liquid_{liquid.SourceType}_{liquid.LevelStatus}", $"Liquid level {liquid.SourceType} is {liquid.LevelStatus}.", runId);
    }

    private void AddFluidicsAlarm(string code, string message, string? runId)
    {
        if (!dbContext.Alarms.Local.Any(x => x.Code == code && x.MachineRunId == runId && x.Status == "Active"))
        {
            dbContext.Alarms.Add(new Alarm
            {
                MachineRunId = runId,
                Code = code,
                Severity = "Critical",
                Message = message,
                Status = "Active",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private void PublishPump(PumpChannelState pump, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.PumpChanged,
            pump.MachineRunId,
            "PumpChannelState",
            pump.Id,
            null,
            PumpData(pump, changeType)));
    }

    private void PublishMixer(MixerChannelState mixer, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.MixerChanged,
            mixer.MachineRunId,
            "MixerChannelState",
            mixer.Id,
            null,
            MixerData(mixer, changeType)));
    }

    private void PublishLiquid(LiquidContainerState liquid, string changeType, string? commandId)
    {
        var data = LiquidData(liquid, changeType);
        data["commandId"] = commandId;
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.LiquidLevelChanged,
            null,
            "LiquidContainerState",
            liquid.Id,
            null,
            data));
    }

    private async Task<FluidicsStateResponse> BuildResponseAsync(CancellationToken cancellationToken)
    {
        var pumps = await dbContext.PumpChannelStates.AsNoTracking().OrderBy(x => x.PwmChannelNo).ToListAsync(cancellationToken);
        var mixers = await dbContext.MixerChannelStates.AsNoTracking().OrderBy(x => x.ChannelNo).ToListAsync(cancellationToken);
        var liquids = await dbContext.LiquidContainerStates.AsNoTracking().OrderBy(x => x.SourceType).ToListAsync(cancellationToken);
        return new FluidicsStateResponse(
            pumps.All(CanPumpBeReady) && mixers.All(CanMixerBeReady) && liquids.All(IsLiquidReady),
            pumps.Select(ToResponse).ToList(),
            mixers.Select(ToResponse).ToList(),
            liquids.Select(ToResponse).ToList(),
            DateTimeOffset.UtcNow);
    }

    private async Task<PumpChannelState> RequirePumpAsync(string? pwmChannelCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizePwm(pwmChannelCode);
        return await dbContext.PumpChannelStates.SingleOrDefaultAsync(x => x.PwmChannelCode == normalized, cancellationToken)
            ?? throw new BusinessRuleException("pump_channel_not_found", "Pump channel was not found.", StatusCodes.Status404NotFound);
    }

    private async Task<MixerChannelState> RequireMixerAsync(string? drawerCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeDrawer(drawerCode);
        return await dbContext.MixerChannelStates.SingleOrDefaultAsync(x => x.DrawerCode == normalized, cancellationToken)
            ?? throw new BusinessRuleException("mixer_channel_not_found", "Mixer channel was not found.", StatusCodes.Status404NotFound);
    }

    private async Task<LiquidContainerState> RequireLiquidAsync(string? sourceType, CancellationToken cancellationToken)
    {
        var normalized = NormalizeSourceType(sourceType);
        return await dbContext.LiquidContainerStates.SingleOrDefaultAsync(x => x.SourceType == normalized, cancellationToken)
            ?? throw new BusinessRuleException("liquid_source_not_found", "Liquid source was not found.", StatusCodes.Status404NotFound);
    }

    private async Task<WashPosition> RequireWashTargetAsync(string? targetPointCode, CancellationToken cancellationToken)
    {
        var code = targetPointCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BusinessRuleException("wash_target_required", "Wash target point is required.", StatusCodes.Status400BadRequest);
        }

        return await dbContext.WashPositions.SingleOrDefaultAsync(x => x.Code == code && x.IsEnabled, cancellationToken)
            ?? throw new BusinessRuleException("wash_target_not_found", "Wash target point was not found.", StatusCodes.Status404NotFound);
    }

    private static void EnsurePumpCanOperate(PumpChannelState pump)
    {
        if (!CanPumpOperate(pump))
        {
            throw new BusinessRuleException("pump_not_ready", "Pump channel is faulted, unknown, timed out, or disconnected.", StatusCodes.Status409Conflict);
        }
    }

    private static void EnsureMixerCanOperate(MixerChannelState mixer)
    {
        if (!CanMixerOperate(mixer))
        {
            throw new BusinessRuleException("mixer_not_ready", "Mixer channel is faulted, unknown, timed out, or disconnected.", StatusCodes.Status409Conflict);
        }
    }

    private static bool CanPumpOperate(PumpChannelState pump) => pump.IsConnected && pump.FaultCode is null && pump.Status is not (FluidicsStatuses.Faulted or FluidicsStatuses.Disconnected or FluidicsStatuses.TimedOut or FluidicsStatuses.Unknown);
    private static bool CanMixerOperate(MixerChannelState mixer) => mixer.IsConnected && mixer.FaultCode is null && mixer.Status is not (FluidicsStatuses.Faulted or FluidicsStatuses.Disconnected or FluidicsStatuses.TimedOut or FluidicsStatuses.Unknown);
    private static bool CanPumpBeReady(PumpChannelState pump) => CanPumpOperate(pump) && pump.Status != FluidicsStatuses.Running;
    private static bool CanMixerBeReady(MixerChannelState mixer) => CanMixerOperate(mixer) && mixer.Status != FluidicsStatuses.Running;

    private static bool IsLiquidReady(LiquidContainerState liquid) =>
        liquid.IsConnected
        && liquid.FaultCode is null
        && liquid.LevelStatus == LiquidLevelStatuses.Normal;

    private static FluidicsDeviceResult PumpReadinessResult(IReadOnlyList<PumpChannelState> pumps)
    {
        var bad = pumps.FirstOrDefault(x => !CanPumpBeReady(x));
        return bad is null
            ? FluidicsDeviceResult.Succeeded("All pump channels are ready.", new Dictionary<string, object?> { ["channels"] = pumps.Select(x => PumpData(x)).ToList() })
            : FluidicsDeviceResult.Failed(bad.FaultCode ?? "pump_not_ready", $"Pump {bad.PwmChannelCode} is {bad.Status}.", MapStatusToDeviceStatus(bad.Status), PumpData(bad));
    }

    private static FluidicsDeviceResult MixerReadinessResult(IReadOnlyList<MixerChannelState> mixers)
    {
        var bad = mixers.FirstOrDefault(x => !CanMixerBeReady(x));
        return bad is null
            ? FluidicsDeviceResult.Succeeded("All mixer channels are ready.", new Dictionary<string, object?> { ["channels"] = mixers.Select(x => MixerData(x)).ToList() })
            : FluidicsDeviceResult.Failed(bad.FaultCode ?? "mixer_not_ready", $"Mixer {bad.DrawerCode} is {bad.Status}.", MapStatusToDeviceStatus(bad.Status), MixerData(bad));
    }

    private static FluidicsDeviceResult LiquidReadinessResult(IReadOnlyList<LiquidContainerState> liquids)
    {
        var bad = liquids.FirstOrDefault(x => !IsLiquidReady(x));
        return bad is null
            ? FluidicsDeviceResult.Succeeded("Liquid levels are ready.", LiquidLevelsData(liquids))
            : FluidicsDeviceResult.Failed(bad.FaultCode ?? $"liquid_{bad.LevelStatus}", $"Liquid level {bad.SourceType} is {bad.LevelStatus}.", DeviceCommandStatuses.Failed, LiquidData(bad));
    }

    private static FluidicsModuleState BuildModuleState(string moduleCode, bool ready, bool disconnected, string? errorCode, string? errorMessage, object data) => new(
        moduleCode,
        ready ? DeviceConnectionStatuses.Connected : disconnected ? DeviceConnectionStatuses.Disconnected : DeviceConnectionStatuses.Faulted,
        ready ? "Idle" : "AttentionRequired",
        JsonSerializer.Serialize(data, JsonOptions),
        errorCode,
        errorMessage);

    private static Dictionary<string, object?> PumpData(PumpChannelState pump, string? changeType = null) => new()
    {
        ["changeType"] = changeType,
        ["pwmChannelCode"] = pump.PwmChannelCode,
        ["pwmChannelNo"] = pump.PwmChannelNo,
        ["drawerCode"] = pump.DrawerCode,
        ["speedPercent"] = pump.SpeedPercent,
        ["direction"] = pump.Direction,
        ["status"] = pump.Status,
        ["isConnected"] = pump.IsConnected,
        ["targetPointCode"] = pump.TargetPointCode,
        ["durationMs"] = pump.DurationMs,
        ["commandId"] = pump.CurrentCommandId,
        ["faultCode"] = pump.FaultCode
    };

    private static Dictionary<string, object?> MixerData(MixerChannelState mixer, string? changeType = null) => new()
    {
        ["changeType"] = changeType,
        ["drawerCode"] = mixer.DrawerCode,
        ["channelNo"] = mixer.ChannelNo,
        ["status"] = mixer.Status,
        ["isConnected"] = mixer.IsConnected,
        ["roundKey"] = mixer.CurrentRoundKey,
        ["commandId"] = mixer.CurrentCommandId,
        ["faultCode"] = mixer.FaultCode
    };

    private static Dictionary<string, object?> LiquidData(LiquidContainerState liquid, string? changeType = null) => new()
    {
        ["changeType"] = changeType,
        ["sourceType"] = liquid.SourceType,
        ["displayName"] = liquid.DisplayName,
        ["isWaste"] = liquid.IsWaste,
        ["capacityUl"] = liquid.CapacityUl,
        ["currentVolumeUl"] = liquid.CurrentVolumeUl,
        ["lowThresholdUl"] = liquid.LowThresholdUl,
        ["fullThresholdUl"] = liquid.FullThresholdUl,
        ["levelStatus"] = liquid.LevelStatus,
        ["isConnected"] = liquid.IsConnected,
        ["faultCode"] = liquid.FaultCode
    };

    private static Dictionary<string, object?> LiquidLevelsData(IEnumerable<LiquidContainerState> liquids) => new()
    {
        ["levels"] = liquids.Select(x => LiquidData(x)).ToList()
    };

    private static PumpChannelResponse ToResponse(PumpChannelState x) => new(
        x.Id,
        x.PwmChannelCode,
        x.PwmChannelNo,
        x.DrawerCode,
        x.SpeedPercent,
        x.Direction,
        x.Status,
        x.IsConnected,
        x.TargetPointCode,
        x.DurationMs,
        x.CurrentCommandId,
        x.FaultCode,
        x.FaultMessage,
        x.UpdatedAtUtc);

    private static MixerChannelResponse ToResponse(MixerChannelState x) => new(
        x.Id,
        x.DrawerCode,
        x.ChannelNo,
        x.Status,
        x.IsConnected,
        x.CurrentRoundKey,
        x.CurrentCommandId,
        x.FaultCode,
        x.FaultMessage,
        x.UpdatedAtUtc);

    private static LiquidContainerResponse ToResponse(LiquidContainerState x) => new(
        x.Id,
        x.SourceType,
        x.DisplayName,
        x.IsWaste,
        x.CapacityUl,
        x.CurrentVolumeUl,
        x.LowThresholdUl,
        x.FullThresholdUl,
        x.LevelStatus,
        x.IsConnected,
        x.FaultCode,
        x.FaultMessage,
        x.UpdatedAtUtc);

    private static FluidicsTelemetryResponse ToTelemetryResponse(FluidicsTelemetry x) => new(
        x.Id,
        x.SourceType,
        x.SourceId,
        x.EventType,
        x.Status,
        x.PwmChannelCode,
        x.DrawerCode,
        x.LiquidSourceType,
        x.SpeedPercent,
        x.Direction,
        x.CurrentVolumeUl,
        x.CapacityUl,
        x.TargetPointCode,
        x.CommandId,
        x.MachineRunId,
        x.WorkflowStepExecutionId,
        x.DeviceCommandExecutionId,
        x.FaultCode,
        x.RecordedAtUtc);

    private static string NormalizePwm(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 1 && int.TryParse(normalized, out var no))
        {
            normalized = $"PWM{no}";
        }

        if (!PwmMap.ContainsKey(normalized))
        {
            throw new BusinessRuleException("pump_pwm_channel_invalid", "PwmChannelCode must be PWM0, PWM1, PWM2, or PWM3.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string DrawerToPwm(string? drawerCode)
    {
        var drawer = NormalizeDrawer(drawerCode);
        return PwmMap.Single(x => x.Value.Drawer == drawer).Key;
    }

    private static string NormalizeDrawer(string? drawerCode)
    {
        var normalized = (drawerCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("A" or "B" or "C" or "D"))
        {
            throw new BusinessRuleException("drawer_code_invalid", "DrawerCode must be A, B, C, or D.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeSourceType(string? sourceType)
    {
        var normalized = (sourceType ?? string.Empty).Trim();
        var match = LiquidSourceTypes.All.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessRuleException("liquid_source_type_invalid", "SourceType must be SystemWater, PBS, Waste, or ToxicWaste.", StatusCodes.Status400BadRequest);
        }

        return match;
    }

    private static string NormalizeLiquidLevelStatus(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var match = LiquidLevelStatuses.All.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessRuleException("liquid_level_status_invalid", "Unknown liquid level status.", StatusCodes.Status400BadRequest);
        }

        return match;
    }

    private static string NormalizeFaultType(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var match = FluidicsFaultTypes.All.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessRuleException("fluidics_fault_type_invalid", "Unknown fluidics fault type.", StatusCodes.Status400BadRequest);
        }

        return match;
    }

    private static string NormalizeTargetType(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Equals("Pump", StringComparison.OrdinalIgnoreCase)) return "Pump";
        if (normalized.Equals("Mixer", StringComparison.OrdinalIgnoreCase)) return "Mixer";
        if (normalized.Equals("LiquidLevel", StringComparison.OrdinalIgnoreCase)) return "LiquidLevel";
        throw new BusinessRuleException("fluidics_target_type_invalid", "TargetType must be Pump, Mixer, or LiquidLevel.", StatusCodes.Status400BadRequest);
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("reason_required", "A reason is required.", StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static void ValidateSpeed(int value)
    {
        if (value is < -100 or > 100)
        {
            throw new BusinessRuleException("pump_speed_invalid", "SpeedPercent must be between -100 and 100.", StatusCodes.Status400BadRequest);
        }
    }

    private static string DirectionFor(int speedPercent) =>
        speedPercent > 0 ? PumpDirections.Forward : speedPercent < 0 ? PumpDirections.Reverse : PumpDirections.Stopped;

    private static string CalculateLiquidStatus(LiquidContainerState liquid)
    {
        if (!liquid.IsConnected) return LiquidLevelStatuses.Disconnected;
        if (liquid.IsWaste) return liquid.CurrentVolumeUl >= liquid.FullThresholdUl ? LiquidLevelStatuses.Full : LiquidLevelStatuses.Normal;
        if (liquid.CurrentVolumeUl <= 0) return LiquidLevelStatuses.Empty;
        return liquid.CurrentVolumeUl <= liquid.LowThresholdUl ? LiquidLevelStatuses.Low : LiquidLevelStatuses.Normal;
    }

    private static void ApplyRepresentativeVolume(LiquidContainerState liquid, string status)
    {
        liquid.CurrentVolumeUl = status switch
        {
            LiquidLevelStatuses.Low => liquid.IsWaste ? Math.Max(0, liquid.FullThresholdUl - 1) : Math.Max(1, liquid.LowThresholdUl / 2),
            LiquidLevelStatuses.Empty => 0,
            LiquidLevelStatuses.Full => liquid.CapacityUl,
            _ => liquid.IsWaste ? Math.Max(0, liquid.LowThresholdUl) : Math.Min(liquid.CapacityUl, liquid.LowThresholdUl + 100_000)
        };
    }

    private static string StatusForFault(string faultType) => faultType switch
    {
        FluidicsFaultTypes.Timeout => FluidicsStatuses.TimedOut,
        FluidicsFaultTypes.Disconnected => FluidicsStatuses.Disconnected,
        FluidicsFaultTypes.Unknown => FluidicsStatuses.Unknown,
        _ => FluidicsStatuses.Faulted
    };

    private static string MapStatusToDeviceStatus(string status) => status switch
    {
        FluidicsStatuses.TimedOut => DeviceCommandStatuses.TimedOut,
        FluidicsStatuses.Unknown => DeviceCommandStatuses.Unknown,
        _ => DeviceCommandStatuses.Failed
    };

    private static string MapGenericFault(string faultType) => faultType switch
    {
        DeviceFaultTypes.TimeoutNextCommand => FluidicsFaultTypes.Timeout,
        DeviceFaultTypes.Disconnect => FluidicsFaultTypes.Disconnected,
        DeviceFaultTypes.ReturnUnknown => FluidicsFaultTypes.Unknown,
        _ => FluidicsFaultTypes.Failure
    };

    private static string FaultTypeFromDeviceStatus(string status, string? errorCode)
    {
        if (status == DeviceCommandStatuses.TimedOut) return FluidicsFaultTypes.Timeout;
        if (status == DeviceCommandStatuses.Unknown) return FluidicsFaultTypes.Unknown;
        if (!string.IsNullOrWhiteSpace(errorCode) && errorCode.Contains("disconnect", StringComparison.OrdinalIgnoreCase)) return FluidicsFaultTypes.Disconnected;
        return FluidicsFaultTypes.Failure;
    }

    private static int DefaultSpeedForAction(string action) => IsWashAction(action) ? 60 : 50;
    private static bool IsWashAction(string action) => action.Contains("wash", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDefaultWashTarget(string action)
    {
        if (action.Contains("outer", StringComparison.OrdinalIgnoreCase)) return "WashOuterLeft";
        if (action.Contains("inner", StringComparison.OrdinalIgnoreCase)) return "WashInnerLeft";
        return null;
    }

    private static int? ReadNullableInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToInt32(value);
    }

    private void EnsureMockMode()
    {
        if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("fluidics_mock_not_available", "Fluidics Mock control is unavailable in Real mode.", StatusCodes.Status409Conflict);
        }
    }
}

public sealed record FluidicsReadinessResult(bool Ok, string? ErrorCode, string Message);

public sealed record FluidicsModuleState(
    string ModuleCode,
    string ConnectionStatus,
    string CurrentAction,
    string CurrentParametersJson,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record FluidicsDeviceResult(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, object?> Data)
{
    public static FluidicsDeviceResult Succeeded(string message, IReadOnlyDictionary<string, object?> data) =>
        new(true, DeviceCommandStatuses.Succeeded, null, message, data);

    public static FluidicsDeviceResult Failed(string errorCode, string message, string status = DeviceCommandStatuses.Failed, IReadOnlyDictionary<string, object?>? data = null) =>
        new(false, status, errorCode, message, data ?? new Dictionary<string, object?>());
}
