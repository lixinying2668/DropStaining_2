using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Devices;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class ThermalControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    IDeviceAdapter deviceAdapter)
{
    private const int AmbientTemperatureDeciC = 250;
    private const int PointStepDeciC = 50;
    private const int CoolingStepDeciC = 10;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ThermalStateResponse> GetStateAsync(bool advance = true, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            if (string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
            {
                // Mock 模式：推进温度模拟。
                if (advance)
                {
                    await AdvanceAllCoreAsync(cancellationToken);
                }
            }
            else if (advance)
            {
                // Real 模式：绝不推进模拟温度。UI 查询（advance=true）时读取主控实时制冷状态刷新 CoolingUnitState；
                // advance=false 的快照/诊断轮询不复读主控，避免高频刷串口。读取失败按 fail-closed 标记 fault，不伪造温度。
                await RefreshCoolingFromMainControllerCoreAsync(cancellationToken);
            }

            return await BuildResponseAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    // Real 模式下用主控 0x03 只读结果刷新 CoolingUnitState；失败则标记 fault，不静默伪造模拟温度。
    private async Task RefreshCoolingFromMainControllerCoreAsync(CancellationToken cancellationToken)
    {
        if (deviceAdapter is not IRealDeviceReadAdapter realRead)
        {
            return;
        }

        var snapshot = await realRead.ReadCoolingSnapshotAsync(cancellationToken);
        var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
        if (!snapshot.Ok)
        {
            cooling.FaultCode = "cooling_read_failed";
            cooling.FaultMessage = snapshot.Message;
            cooling.Status = ThermalStatuses.Unknown;
            cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddCoolingTelemetry(cooling);
            PublishCooling(cooling, null, "realReadFailed");
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var value = snapshot.Value!;
        cooling.CurrentTemperatureDeciC = value.CurrentTemperatureDeciC;
        cooling.TargetTemperatureDeciC = value.TargetTemperatureDeciC;
        cooling.IsEnabled = value.IsEnabled;
        cooling.IsConnected = value.IsConnected;
        cooling.FaultCode = null;
        cooling.FaultMessage = null;
        cooling.Status = NextStatus(cooling.CurrentTemperatureDeciC, cooling.TargetTemperatureDeciC, cooling.IsEnabled, true);
        cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddCoolingTelemetry(cooling);
        PublishCooling(cooling, null, "realRead");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ThermalMutationResponse> SetPointAsync(
        string drawerCode,
        int slotNo,
        SetThermalPointRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "thermal.point.set",
            new { drawerCode, slotNo, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var point = await RequirePointAsync(drawerCode, slotNo, cancellationToken);
                    SetPointTarget(point, request.TargetTemperatureDeciC, request.IsEnabled);
                    AddPointTelemetry(point);
                    AddAudit(actor, "thermal.point.set", point.Id, request.CommandId, new { point.DrawerCode, point.SlotNo, request.TargetTemperatureDeciC, request.IsEnabled });
                    PublishPoint(point, request.CommandId, "targetChanged");
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<ThermalMutationResponse>(
                        new ThermalMutationResponse(true, request.CommandId, false, "Thermal point target updated.", await BuildResponseAsync(cancellationToken)),
                        "ThermalPointState",
                        point.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<ThermalMutationResponse> SetBoardAsync(
        string drawerCode,
        SetThermalBoardRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "thermal.board.set",
            new { drawerCode, request },
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    ValidateTemperature(request.TargetTemperatureDeciC);
                    var normalized = NormalizeDrawer(drawerCode);
                    var points = await dbContext.ThermalPointStates.Where(x => x.DrawerCode == normalized).OrderBy(x => x.SlotNo).ToListAsync(cancellationToken);
                    if (points.Count != 4)
                    {
                        throw new BusinessRuleException("thermal_board_not_found", "Thermal board was not found.", StatusCodes.Status404NotFound);
                    }

                    foreach (var point in points)
                    {
                        SetPointTarget(point, request.TargetTemperatureDeciC, request.IsEnabled);
                        AddPointTelemetry(point);
                        PublishPoint(point, request.CommandId, "boardTargetChanged");
                    }

                    AddAudit(actor, "thermal.board.set", normalized, request.CommandId, new { drawerCode = normalized, request.TargetTemperatureDeciC, request.IsEnabled });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<ThermalMutationResponse>(
                        new ThermalMutationResponse(true, request.CommandId, false, "Thermal board targets updated.", await BuildResponseAsync(cancellationToken)),
                        "ThermalBoard",
                        normalized);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<ThermalMutationResponse> SetCoolingAsync(
        SetCoolingRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "thermal.cooling.set",
            request,
            actor,
            async () =>
            {
                // Real 模式：制冷由主控处理，走 IDeviceAdapter.SetCoolingAsync（主控 0x03/0x04、0x03/0x06），fail closed 不回退 Mock。
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    return await SetCoolingViaMainControllerAsync(request, actor, cancellationToken);
                }

                // Mock 模式：保留现有模拟逻辑
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    ValidateCoolingTemperature(request.TargetTemperatureDeciC);
                    var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
                    cooling.TargetTemperatureDeciC = request.TargetTemperatureDeciC;
                    cooling.IsEnabled = request.IsEnabled;
                    cooling.Status = NextStatus(cooling.CurrentTemperatureDeciC, cooling.TargetTemperatureDeciC, request.IsEnabled, true);
                    cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    AddCoolingTelemetry(cooling);
                    AddAudit(actor, "thermal.cooling.set", cooling.Id, request.CommandId, new { request.TargetTemperatureDeciC, request.IsEnabled });
                    PublishCooling(cooling, request.CommandId, "targetChanged");
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<ThermalMutationResponse>(
                        new ThermalMutationResponse(true, request.CommandId, false, "Cooling target updated.", await BuildResponseAsync(cancellationToken)),
                        "CoolingUnitState",
                        cooling.Id);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    // Real 模式制冷下发：调用主控适配器写入目标温度与开关，然后用真实回读结果刷新 CoolingUnitState / telemetry / event。
    // 适配器内部已 fail closed（任一步失败即返回 Ok=false）；此处再把失败转成 BusinessRuleException，绝不伪造成功状态。
    private async Task<CommandExecutionResult<ThermalMutationResponse>> SetCoolingViaMainControllerAsync(
        SetCoolingRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        var deviceRequest = new DeviceOperationRequest(
            new DeviceCommandContext(request.CommandId, null, actor.UserId, "ThermalControlService"),
            Application.Devices.DeviceModules.Cooling,
            "set-cooling",
            new Dictionary<string, object?>
            {
                ["targetTemperatureDeciC"] = request.TargetTemperatureDeciC,
                ["isEnabled"] = request.IsEnabled
            });

        var deviceResult = await deviceAdapter.SetCoolingAsync(deviceRequest, cancellationToken);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
            if (!deviceResult.Ok)
            {
                // fail closed：保留旧状态，记录 fault 与审计，向前端返回明确错误（409）。
                cooling.FaultCode = deviceResult.ErrorCode ?? "cooling_command_failed";
                cooling.FaultMessage = deviceResult.Message;
                cooling.Status = ThermalStatuses.Unknown;
                cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
                AddCoolingTelemetry(cooling);
                AddAudit(actor, "thermal.cooling.set", cooling.Id, request.CommandId, new { request.TargetTemperatureDeciC, request.IsEnabled, source = "MainController", ok = false, deviceResult.ErrorCode });
                PublishCooling(cooling, request.CommandId, "mainControllerFailed");
                await dbContext.SaveChangesAsync(cancellationToken);
                throw new BusinessRuleException(
                    deviceResult.ErrorCode ?? "cooling_command_failed",
                    deviceResult.Message,
                    StatusCodes.Status409Conflict);
            }

            cooling.CurrentTemperatureDeciC = DictionaryInt(deviceResult.Data, "currentTemperatureDeciC", cooling.CurrentTemperatureDeciC);
            cooling.TargetTemperatureDeciC = DictionaryInt(deviceResult.Data, "targetTemperatureDeciC", request.TargetTemperatureDeciC);
            cooling.IsEnabled = DictionaryBool(deviceResult.Data, "isEnabled", request.IsEnabled);
            cooling.IsConnected = DictionaryBool(deviceResult.Data, "isConnected", cooling.IsConnected);
            cooling.FaultCode = null;
            cooling.FaultMessage = null;
            cooling.Status = NextStatus(cooling.CurrentTemperatureDeciC, cooling.TargetTemperatureDeciC, cooling.IsEnabled, true);
            cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddCoolingTelemetry(cooling);
            AddAudit(actor, "thermal.cooling.set", cooling.Id, request.CommandId, new { request.TargetTemperatureDeciC, request.IsEnabled, source = "MainController", ok = true });
            PublishCooling(cooling, request.CommandId, "mainControllerApplied");
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CommandExecutionResult<ThermalMutationResponse>(
                new ThermalMutationResponse(true, request.CommandId, false, "Cooling applied via main controller.", await BuildResponseAsync(cancellationToken)),
                "CoolingUnitState",
                cooling.Id);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static int DictionaryInt(IReadOnlyDictionary<string, object?> data, string key, int fallback)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
        {
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        return fallback;
    }

    private static bool DictionaryBool(IReadOnlyDictionary<string, object?> data, string key, bool fallback)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
        {
            try { return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        return fallback;
    }

    public Task<ThermalMutationResponse> ConfigureFaultAsync(
        ConfigureThermalFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "thermal.fault.configure",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var faultType = NormalizeFaultType(request.FaultType);
                    var reason = RequireReason(request.Reason);
                    var targetId = await ApplyFaultAsync(request.TargetType, request.DrawerCode, request.SlotNo, faultType, reason, request.CommandId, cancellationToken);
                    AddAudit(actor, "thermal.fault.configured", targetId, request.CommandId, new { request.TargetType, request.DrawerCode, request.SlotNo, faultType, reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<ThermalMutationResponse>(
                        new ThermalMutationResponse(true, request.CommandId, false, "Thermal fault configured.", await BuildResponseAsync(cancellationToken)),
                        "ThermalState",
                        targetId);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public Task<ThermalMutationResponse> ClearFaultAsync(
        ClearThermalFaultRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "thermal.fault.clear",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var reason = RequireReason(request.Reason);
                    var targetId = await ClearFaultCoreAsync(request.TargetType, request.DrawerCode, request.SlotNo, request.CommandId, cancellationToken);
                    var alarms = await dbContext.Alarms.Where(x => x.Status == "Active" && x.Code.StartsWith($"thermal_{targetId}_")).ToListAsync(cancellationToken);
                    foreach (var alarm in alarms)
                    {
                        alarm.Status = "Cleared";
                        alarm.ClearedAtUtc = DateTimeOffset.UtcNow;
                    }

                    AddAudit(actor, "thermal.fault.cleared", targetId, request.CommandId, new { request.TargetType, request.DrawerCode, request.SlotNo, reason });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new CommandExecutionResult<ThermalMutationResponse>(
                        new ThermalMutationResponse(true, request.CommandId, false, "Thermal fault cleared; recovery is in progress.", await BuildResponseAsync(cancellationToken)),
                        "ThermalState",
                        targetId);
                }
                finally
                {
                    Gate.Release();
                }
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<TemperatureTelemetryResponse>> ListTelemetryAsync(int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var rows = await dbContext.TemperatureTelemetry.AsNoTracking().ToListAsync(cancellationToken);
        return rows
            .OrderByDescending(x => x.RecordedAtUtc)
            .Take(take)
            .Select(x => new TemperatureTelemetryResponse(
                x.Id, x.SourceType, x.SourceId, x.DrawerCode, x.BoardNo, x.SlotNo, x.PointNo,
                x.CurrentTemperatureDeciC, x.TargetTemperatureDeciC, x.IsEnabled, x.IsConnected,
                x.Status, x.FaultCode, x.RecordedAtUtc))
            .ToList();
    }

    public async Task<ThermalReadinessResult> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var points = await dbContext.ThermalPointStates.AsNoTracking().ToListAsync(cancellationToken);
            var cooling = await dbContext.CoolingUnitStates.AsNoTracking().SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
            var badPoint = points.FirstOrDefault(x => !x.IsConnected || x.FaultCode != null || x.Status is ThermalStatuses.Faulted or ThermalStatuses.Unknown or ThermalStatuses.Heating or ThermalStatuses.Cooling or ThermalStatuses.Returning);
            if (badPoint is not null)
            {
                return new ThermalReadinessResult(false, "thermal_point_not_ready", $"Thermal {badPoint.DrawerCode}-{badPoint.SlotNo} is {badPoint.Status}; fault={badPoint.FaultCode ?? "none"}.");
            }

            if (!cooling.IsConnected || cooling.FaultCode != null || cooling.Status is ThermalStatuses.Faulted or ThermalStatuses.Unknown or ThermalStatuses.Heating or ThermalStatuses.Cooling or ThermalStatuses.Returning)
            {
                return new ThermalReadinessResult(false, "cooling_not_ready", $"Cooling is {cooling.Status}; fault={cooling.FaultCode ?? "none"}.");
            }

            return new ThermalReadinessResult(true, null, "Thermal control and cooling are ready.");
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

    public async Task<ThermalDeviceResult> InitializeModuleAsync(string moduleCode, CancellationToken cancellationToken = default)
    {
        // Real 模式制冷初始化：通过主控读取真实连接/温度/开关状态刷新 CoolingUnitState，再判定就绪。
        if (moduleCode == Application.Devices.DeviceModules.Cooling
            && !string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshCoolingFromMainControllerCoreAsync(cancellationToken);
        }

        var state = await GetStateAsync(false, cancellationToken);
        if (moduleCode == Application.Devices.DeviceModules.Temperature)
        {
            var failed = state.Points.FirstOrDefault(x => !x.IsConnected || x.FaultCode != null || x.Status == ThermalStatuses.Unknown);
            return failed is null
                ? new ThermalDeviceResult(true, Application.Devices.DeviceCommandStatuses.Succeeded, null, "All 16 thermal points are connected.", new Dictionary<string, object?> { ["pointCount"] = 16, ["connected"] = true })
                : FaultResult(failed.FaultCode ?? "temperature_not_ready", $"Thermal point {failed.DrawerCode}-{failed.SlotNo} is not ready.");
        }

        var cooling = state.Cooling;
        return cooling.IsConnected && cooling.FaultCode is null && cooling.Status != ThermalStatuses.Unknown
            ? new ThermalDeviceResult(true, Application.Devices.DeviceCommandStatuses.Succeeded, null, "Cooling is connected.", new Dictionary<string, object?>
            {
                ["connected"] = cooling.IsConnected,
                ["currentTemperatureDeciC"] = cooling.CurrentTemperatureDeciC,
                ["targetTemperatureDeciC"] = cooling.TargetTemperatureDeciC,
                ["isEnabled"] = cooling.IsEnabled,
                ["status"] = cooling.Status
            })
            : FaultResult(cooling.FaultCode ?? "cooling_not_ready", "Cooling is not ready.");
    }

    public async Task<ThermalDeviceResult> SetPointFromDeviceAsync(
        string drawerCode,
        int slotNo,
        int targetTemperatureDeciC,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var point = await RequirePointAsync(drawerCode, slotNo, cancellationToken);
            if (!CanOperate(point.IsConnected, point.FaultCode, point.Status))
            {
                return FaultResult(point.FaultCode ?? "temperature_not_ready", point.FaultMessage ?? "Thermal point is not ready.", point.Status);
            }

            SetPointTarget(point, targetTemperatureDeciC, true);
            AddPointTelemetry(point);
            PublishPoint(point, null, "executorTargetChanged");
            await dbContext.SaveChangesAsync(cancellationToken);
            for (var i = 0; i < 25 && point.Status != ThermalStatuses.Stable; i++)
            {
                AdvancePoint(point);
                AddPointTelemetry(point);
                PublishPoint(point, null, "executorProgress");
                await dbContext.SaveChangesAsync(cancellationToken);
                await Task.Delay(5, cancellationToken);
            }

            return point.Status == ThermalStatuses.Stable
                ? new ThermalDeviceResult(true, Application.Devices.DeviceCommandStatuses.Succeeded, null, "Thermal point reached target.", PointData(point))
                : FaultResult(point.FaultCode ?? "heating_timeout", point.FaultMessage ?? "Thermal point did not stabilize.", point.Status);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<ThermalDeviceResult> SetCoolingFromDeviceAsync(int targetTemperatureDeciC, bool enabled, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            ValidateCoolingTemperature(targetTemperatureDeciC);
            var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
            if (!CanOperate(cooling.IsConnected, cooling.FaultCode, cooling.Status))
            {
                return FaultResult(cooling.FaultCode ?? "cooling_not_ready", cooling.FaultMessage ?? "Cooling is not ready.", cooling.Status);
            }

            cooling.TargetTemperatureDeciC = targetTemperatureDeciC;
            cooling.IsEnabled = enabled;
            cooling.Status = NextStatus(cooling.CurrentTemperatureDeciC, cooling.TargetTemperatureDeciC, enabled, true);
            cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddCoolingTelemetry(cooling);
            PublishCooling(cooling, null, "adapterTargetChanged");
            await dbContext.SaveChangesAsync(cancellationToken);
            for (var i = 0; i < 30 && cooling.Status is not (ThermalStatuses.Stable or ThermalStatuses.Off); i++)
            {
                AdvanceCooling(cooling);
                AddCoolingTelemetry(cooling);
                PublishCooling(cooling, null, "adapterProgress");
                await dbContext.SaveChangesAsync(cancellationToken);
                await Task.Delay(5, cancellationToken);
            }

            return cooling.Status is ThermalStatuses.Stable or ThermalStatuses.Off
                ? new ThermalDeviceResult(true, Application.Devices.DeviceCommandStatuses.Succeeded, null, "Cooling reached target state.", CoolingData(cooling))
                : FaultResult(cooling.FaultCode ?? "cooling_timeout", cooling.FaultMessage ?? "Cooling did not stabilize.", cooling.Status);
        }
        finally
        {
            Gate.Release();
        }
    }

    // 启动期公开入口：仅保证 16 个温控点 + 冷却单元已写入数据库，
    // 不推进温度模拟。供 Program.cs 启动初始化调用，使 /api/twin/snapshot 在空库下也能稳定返回温控点。
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
        // 温控点：按 (DrawerCode, SlotNo) 幂等补齐到 16 行（A-D × 1-4）。
        // 不用 AnyAsync 守卫——那会让 1~15 行的残缺状态原样保留，导致 snapshot 返回缺数、
        // 单抽屉逻辑（下方 points.Count != 4）抛 thermal_board_not_found。此处只补缺失项，已有行（含被业务设过的温度）不动。
        var present = (await dbContext.ThermalPointStates
                .Select(x => new { x.DrawerCode, x.SlotNo })
                .ToListAsync(cancellationToken))
            .Select(x => (x.DrawerCode.ToUpperInvariant(), x.SlotNo))
            .ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var (drawer, board) in new[] { ("A", 0), ("B", 1), ("C", 2), ("D", 3) })
        {
            for (var slot = 1; slot <= 4; slot++)
            {
                if (present.Contains((drawer, slot)))
                {
                    continue;
                }

                dbContext.ThermalPointStates.Add(new ThermalPointState
                {
                    DrawerCode = drawer,
                    BoardNo = board,
                    SlotNo = slot,
                    PointNo = slot - 1,
                    UpdatedAtUtc = now
                });
            }
        }

        if (!await dbContext.CoolingUnitStates.AnyAsync(cancellationToken))
        {
            dbContext.CoolingUnitStates.Add(new CoolingUnitState());
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task AdvanceAllCoreAsync(CancellationToken cancellationToken)
    {
        var points = await dbContext.ThermalPointStates.ToListAsync(cancellationToken);
        var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
        var changed = false;
        foreach (var point in points.Where(x => x.FaultCode is null && x.IsConnected && x.Status is not (ThermalStatuses.Stable or ThermalStatuses.Off or ThermalStatuses.Unknown or ThermalStatuses.Faulted)))
        {
            AdvancePoint(point);
            AddPointTelemetry(point);
            PublishPoint(point, null, "simulationTick");
            changed = true;
        }

        if (cooling.FaultCode is null && cooling.IsConnected && cooling.Status is not (ThermalStatuses.Stable or ThermalStatuses.Off or ThermalStatuses.Unknown or ThermalStatuses.Faulted))
        {
            AdvanceCooling(cooling);
            AddCoolingTelemetry(cooling);
            PublishCooling(cooling, null, "simulationTick");
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static void AdvancePoint(ThermalPointState point)
    {
        var target = point.IsEnabled ? point.TargetTemperatureDeciC : AmbientTemperatureDeciC;
        point.CurrentTemperatureDeciC = Step(point.CurrentTemperatureDeciC, target, PointStepDeciC);
        point.Status = point.CurrentTemperatureDeciC == target
            ? point.IsEnabled ? ThermalStatuses.Stable : ThermalStatuses.Off
            : point.IsEnabled ? NextStatus(point.CurrentTemperatureDeciC, target, true, false) : ThermalStatuses.Returning;
        point.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void AdvanceCooling(CoolingUnitState cooling)
    {
        var target = cooling.IsEnabled ? cooling.TargetTemperatureDeciC : AmbientTemperatureDeciC;
        cooling.CurrentTemperatureDeciC = Step(cooling.CurrentTemperatureDeciC, target, CoolingStepDeciC);
        cooling.Status = cooling.CurrentTemperatureDeciC == target
            ? cooling.IsEnabled ? ThermalStatuses.Stable : ThermalStatuses.Off
            : cooling.IsEnabled ? NextStatus(cooling.CurrentTemperatureDeciC, target, true, true) : ThermalStatuses.Returning;
        cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static int Step(int current, int target, int amount) => current < target ? Math.Min(target, current + amount) : Math.Max(target, current - amount);

    private static string NextStatus(int current, int target, bool enabled, bool coolingUnit)
    {
        if (!enabled) return ThermalStatuses.Returning;
        if (current == target) return ThermalStatuses.Stable;
        if (coolingUnit && target > current) return ThermalStatuses.Returning;
        return current < target ? ThermalStatuses.Heating : ThermalStatuses.Cooling;
    }

    private static void SetPointTarget(ThermalPointState point, int target, bool enabled)
    {
        ValidateTemperature(target);
        if (!point.IsConnected || point.FaultCode is not null || point.Status == ThermalStatuses.Unknown)
        {
            throw new BusinessRuleException("thermal_point_not_ready", "Faulted, Unknown, or disconnected thermal point cannot accept a target.", StatusCodes.Status409Conflict);
        }

        point.TargetTemperatureDeciC = target;
        point.IsEnabled = enabled;
        point.Status = NextStatus(point.CurrentTemperatureDeciC, target, enabled, false);
        point.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<string> ApplyFaultAsync(string targetType, string? drawerCode, int? slotNo, string faultType, string reason, string? commandId, CancellationToken cancellationToken)
    {
        if (IsCoolingTarget(targetType))
        {
            var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
            ApplyFault(cooling, faultType, reason);
            AddCoolingTelemetry(cooling);
            PublishCooling(cooling, commandId, "faultConfigured");
            AddFaultAlarm(cooling.Id, faultType, reason);
            return cooling.Id;
        }

        var point = await RequirePointAsync(drawerCode, slotNo ?? 0, cancellationToken);
        ApplyFault(point, faultType, reason);
        AddPointTelemetry(point);
        PublishPoint(point, commandId, "faultConfigured");
        AddFaultAlarm(point.Id, faultType, reason);
        return point.Id;
    }

    private async Task<string> ClearFaultCoreAsync(string targetType, string? drawerCode, int? slotNo, string? commandId, CancellationToken cancellationToken)
    {
        if (IsCoolingTarget(targetType))
        {
            var cooling = await dbContext.CoolingUnitStates.SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
            cooling.FaultCode = null;
            cooling.FaultMessage = null;
            cooling.IsConnected = true;
            cooling.Status = NextStatus(cooling.CurrentTemperatureDeciC, cooling.TargetTemperatureDeciC, cooling.IsEnabled, true);
            cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddCoolingTelemetry(cooling);
            PublishCooling(cooling, commandId, "faultCleared");
            return cooling.Id;
        }

        var point = await RequirePointAsync(drawerCode, slotNo ?? 0, cancellationToken);
        point.FaultCode = null;
        point.FaultMessage = null;
        point.IsConnected = true;
        point.Status = NextStatus(point.CurrentTemperatureDeciC, point.TargetTemperatureDeciC, point.IsEnabled, false);
        point.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddPointTelemetry(point);
        PublishPoint(point, commandId, "faultCleared");
        return point.Id;
    }

    private static void ApplyFault(ThermalPointState point, string faultType, string reason)
    {
        point.FaultCode = faultType;
        point.FaultMessage = reason;
        point.IsConnected = faultType != ThermalFaultTypes.Disconnected;
        point.Status = faultType == ThermalFaultTypes.Unknown ? ThermalStatuses.Unknown : ThermalStatuses.Faulted;
        if (faultType == ThermalFaultTypes.OverTemperature) point.CurrentTemperatureDeciC = Math.Min(1200, point.TargetTemperatureDeciC + 150);
        if (faultType == ThermalFaultTypes.TemperatureDeviation) point.CurrentTemperatureDeciC = Math.Min(1200, point.TargetTemperatureDeciC + 100);
        point.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyFault(CoolingUnitState cooling, string faultType, string reason)
    {
        cooling.FaultCode = faultType;
        cooling.FaultMessage = reason;
        cooling.IsConnected = faultType != ThermalFaultTypes.Disconnected;
        cooling.Status = faultType == ThermalFaultTypes.Unknown ? ThermalStatuses.Unknown : ThermalStatuses.Faulted;
        if (faultType == ThermalFaultTypes.OverTemperature) cooling.CurrentTemperatureDeciC = Math.Min(1200, cooling.TargetTemperatureDeciC + 150);
        if (faultType == ThermalFaultTypes.TemperatureDeviation) cooling.CurrentTemperatureDeciC = Math.Min(1200, cooling.TargetTemperatureDeciC + 100);
        cooling.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<ThermalPointState> RequirePointAsync(string? drawerCode, int slotNo, CancellationToken cancellationToken)
    {
        var normalized = NormalizeDrawer(drawerCode);
        if (slotNo is < 1 or > 4)
        {
            throw new BusinessRuleException("thermal_slot_invalid", "SlotNo must be between 1 and 4.", StatusCodes.Status400BadRequest);
        }

        return await dbContext.ThermalPointStates.SingleOrDefaultAsync(x => x.DrawerCode == normalized && x.SlotNo == slotNo, cancellationToken)
            ?? throw new BusinessRuleException("thermal_point_not_found", "Thermal point was not found.", StatusCodes.Status404NotFound);
    }

    private async Task<ThermalStateResponse> BuildResponseAsync(CancellationToken cancellationToken)
    {
        var points = await dbContext.ThermalPointStates.AsNoTracking().OrderBy(x => x.BoardNo).ThenBy(x => x.PointNo).ToListAsync(cancellationToken);
        var cooling = await dbContext.CoolingUnitStates.AsNoTracking().SingleAsync(x => x.Id == CoolingUnitState.SingletonId, cancellationToken);
        var ready = points.All(x => x.IsConnected && x.FaultCode is null && x.Status is not (ThermalStatuses.Faulted or ThermalStatuses.Unknown or ThermalStatuses.Heating or ThermalStatuses.Cooling or ThermalStatuses.Returning))
            && cooling.IsConnected && cooling.FaultCode is null && cooling.Status is not (ThermalStatuses.Faulted or ThermalStatuses.Unknown or ThermalStatuses.Heating or ThermalStatuses.Cooling or ThermalStatuses.Returning);
        return new ThermalStateResponse(
            ready,
            points.Select(ToResponse).ToList(),
            ToResponse(cooling),
            DateTimeOffset.UtcNow);
    }

    private void AddPointTelemetry(ThermalPointState point)
    {
        dbContext.TemperatureTelemetry.Add(new TemperatureTelemetry
        {
            SourceType = ThermalTelemetrySourceTypes.Point,
            SourceId = point.Id,
            DrawerCode = point.DrawerCode,
            BoardNo = point.BoardNo,
            SlotNo = point.SlotNo,
            PointNo = point.PointNo,
            CurrentTemperatureDeciC = point.CurrentTemperatureDeciC,
            TargetTemperatureDeciC = point.TargetTemperatureDeciC,
            IsEnabled = point.IsEnabled,
            IsConnected = point.IsConnected,
            Status = point.Status,
            FaultCode = point.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddCoolingTelemetry(CoolingUnitState cooling)
    {
        dbContext.TemperatureTelemetry.Add(new TemperatureTelemetry
        {
            SourceType = ThermalTelemetrySourceTypes.Cooling,
            SourceId = cooling.Id,
            CurrentTemperatureDeciC = cooling.CurrentTemperatureDeciC,
            TargetTemperatureDeciC = cooling.TargetTemperatureDeciC,
            IsEnabled = cooling.IsEnabled,
            IsConnected = cooling.IsConnected,
            Status = cooling.Status,
            FaultCode = cooling.FaultCode,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void AddFaultAlarm(string targetId, string faultType, string reason)
    {
        var code = $"thermal_{targetId}_{faultType}";
        if (!dbContext.Alarms.Local.Any(x => x.Code == code && x.Status == "Active"))
        {
            dbContext.Alarms.Add(new Alarm { Code = code, Severity = "Error", Message = reason, Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow });
        }
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityId, string commandId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = "ThermalState",
            EntityId = entityId,
            Message = JsonSerializer.Serialize(new { commandId, details }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private void PublishPoint(ThermalPointState point, string? commandId, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.TemperatureChanged,
            null,
            "ThermalPointState",
            point.Id,
            null,
            PointData(point, commandId, changeType)));
    }

    private void PublishCooling(CoolingUnitState cooling, string? commandId, string changeType)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.CoolingChanged,
            null,
            "CoolingUnitState",
            cooling.Id,
            null,
            CoolingData(cooling, commandId, changeType)));
    }

    private static IReadOnlyDictionary<string, object?> PointData(ThermalPointState point, string? commandId = null, string? changeType = null) => new Dictionary<string, object?>
    {
        ["commandId"] = commandId,
        ["changeType"] = changeType,
        ["drawerCode"] = point.DrawerCode,
        ["boardNo"] = point.BoardNo,
        ["slotNo"] = point.SlotNo,
        ["pointNo"] = point.PointNo,
        ["currentTemperatureDeciC"] = point.CurrentTemperatureDeciC,
        ["targetTemperatureDeciC"] = point.TargetTemperatureDeciC,
        ["isEnabled"] = point.IsEnabled,
        ["isConnected"] = point.IsConnected,
        ["status"] = point.Status,
        ["faultCode"] = point.FaultCode
    };

    private static IReadOnlyDictionary<string, object?> CoolingData(CoolingUnitState cooling, string? commandId = null, string? changeType = null) => new Dictionary<string, object?>
    {
        ["commandId"] = commandId,
        ["changeType"] = changeType,
        ["currentTemperatureDeciC"] = cooling.CurrentTemperatureDeciC,
        ["targetTemperatureDeciC"] = cooling.TargetTemperatureDeciC,
        ["isEnabled"] = cooling.IsEnabled,
        ["isConnected"] = cooling.IsConnected,
        ["status"] = cooling.Status,
        ["faultCode"] = cooling.FaultCode
    };

    private static ThermalDeviceResult FaultResult(string code, string message, string status = ThermalStatuses.Faulted) => new(
        false,
        status == ThermalStatuses.Unknown ? Application.Devices.DeviceCommandStatuses.Unknown : Application.Devices.DeviceCommandStatuses.Failed,
        code,
        message,
        new Dictionary<string, object?> { ["status"] = status, ["faultCode"] = code });

    private static bool CanOperate(bool connected, string? faultCode, string status) => connected && faultCode is null && status is not (ThermalStatuses.Faulted or ThermalStatuses.Unknown);

    private static ThermalPointResponse ToResponse(ThermalPointState x) => new(
        x.Id, x.DrawerCode, x.BoardNo, x.SlotNo, x.PointNo, x.CurrentTemperatureDeciC,
        x.TargetTemperatureDeciC, x.IsEnabled, x.IsConnected, x.Status, x.FaultCode, x.FaultMessage, x.UpdatedAtUtc);

    private static CoolingStateResponse ToResponse(CoolingUnitState x) => new(
        x.Id, x.CurrentTemperatureDeciC, x.TargetTemperatureDeciC, x.IsEnabled, x.IsConnected,
        x.Status, x.FaultCode, x.FaultMessage, x.UpdatedAtUtc);

    private static string NormalizeDrawer(string? drawerCode)
    {
        var normalized = (drawerCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("A" or "B" or "C" or "D"))
        {
            throw new BusinessRuleException("thermal_drawer_invalid", "DrawerCode must be A, B, C, or D.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeFaultType(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (!ThermalFaultTypes.All.Contains(normalized))
        {
            throw new BusinessRuleException("thermal_fault_type_invalid", "Unknown thermal fault type.", StatusCodes.Status400BadRequest);
        }

        return ThermalFaultTypes.All.First(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("reason_required", "A reason is required.", StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static bool IsCoolingTarget(string? targetType)
    {
        var normalized = (targetType ?? string.Empty).Trim();
        if (normalized.Equals("Cooling", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.Equals("Point", StringComparison.OrdinalIgnoreCase)) return false;
        throw new BusinessRuleException("thermal_target_type_invalid", "TargetType must be Point or Cooling.", StatusCodes.Status400BadRequest);
    }

    private static void ValidateTemperature(int value)
    {
        if (value is < 0 or > 1000)
        {
            throw new BusinessRuleException("target_temperature_invalid", "Temperature must be between 0 and 1000 deci-Celsius.", StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateCoolingTemperature(int value)
    {
        if (value is < -200 or > 400)
        {
            throw new BusinessRuleException("cooling_target_temperature_invalid", "Cooling temperature must be between -200 and 400 deci-Celsius.", StatusCodes.Status400BadRequest);
        }
    }

    private void EnsureMockMode()
    {
        if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("thermal_mock_not_available", "Thermal Mock control is unavailable in Real mode.", StatusCodes.Status409Conflict);
        }
    }
}

public sealed record ThermalReadinessResult(bool Ok, string? ErrorCode, string Message);

public sealed record ThermalDeviceResult(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, object?> Data);
