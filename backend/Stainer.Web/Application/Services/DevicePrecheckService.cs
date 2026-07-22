using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

/// <summary>
/// 数字孪生“检测”的真实预检服务：11 项独立检查，每项映射到真实数据源。
/// 一键检测（RunAll）与单项检测（RunOne）都会真正执行；只读评估（EvaluateReadOnly）供
/// GET /api/run/preflight 复用，绝不触发回零/洗针/重连等硬件动作。
/// 只有 Passed 算通过；缺数据、缺返回项、未知状态、断连、故障或请求失败绝不能默认通过。
/// 一键检测结果会被持久化为 DevicePrecheckRun，preflight 从“最近一次完整预检报告（主）/
/// 最近一次设备初始化（备）”读取探测型检查，形成同一可信链路。
/// </summary>
public sealed class DevicePrecheckService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    DeviceInitializationService deviceInitializationService,
    FluidicsControlService fluidicsControlService,
    IDeviceAdapter deviceAdapter,
    IRealDeviceReadAdapter realDeviceReadAdapter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>预检报告有效期：超过则探测型检查（主控/扫码器）视为失效，需重新检测。</summary>
    private static readonly TimeSpan MaxReportAge = TimeSpan.FromMinutes(30);

    /// <summary>液位传感器读数新鲜度上限：超过则视为数据过期。</summary>
    private static readonly TimeSpan MaxLiquidDataAge = TimeSpan.FromHours(24);

    /// <summary>Mock 回零坐标允许误差：100 μm（0.1 mm）。</summary>
    private const long HomeCoordinateToleranceUm = 100;

    /// <summary>“活动运行”状态集合（与 EngineeringConfigService/StartupRecoveryService 一致）。
    /// 处于这些状态时禁止回零/洗针等危险动作。</summary>
    private static readonly string[] ActiveRunStatuses =
    [
        RuntimeLedgerStatus.Created,
        RuntimeLedgerStatus.Pending,
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted,
        RuntimeLedgerStatus.WaitingUnload
    ];

    /// <summary>液位/液量校验要求齐全的容器。</summary>
    private static readonly string[] RequiredLiquidSources =
    [
        LiquidSourceTypes.SystemWater, LiquidSourceTypes.Pbs, LiquidSourceTypes.Waste, LiquidSourceTypes.ToxicWaste
    ];

    private sealed record CheckDefinition(string CheckId, string Label, string Category, int StepIndex, bool IsAction, bool IsLiquid);
    private sealed record ReportCheckEntry(string CheckId, string Module, string Status, DateTimeOffset CheckedAtUtc, string? ErrorCode, string? ErrorMessage);
    private sealed record ProbeContext(Dictionary<string, string>? ReportMap, DeviceInitializationRun? LatestInitRun, string Mode);

    private static readonly CheckDefinition[] Checks =
    [
        new("device.controller.connected", "主控连接", PrecheckCategories.Device, 0, false, false),
        new("motion.arm.home", "机械臂回零", PrecheckCategories.Motion, 1, true, false),
        new("thermal.cooling.connected", "制冷连接", PrecheckCategories.Thermal, 2, false, false),
        new("scanner.sample.online", "样本扫码器在线", PrecheckCategories.Scanner, 3, false, false),
        new("scanner.reagent.online", "试剂扫码器在线", PrecheckCategories.Scanner, 4, false, false),
        new("fluidics.level-sensors.readable", "液位/传感器读取", PrecheckCategories.Fluidics, 5, false, false),
        new("motion.needle-wash.ready", "洗针准备", PrecheckCategories.Motion, 6, true, false),
        new("fluidics.system-water.available", "纯水可用", PrecheckCategories.Fluidics, 7, false, true),
        new("fluidics.pbs.available", "PBS可用", PrecheckCategories.Fluidics, 8, false, true),
        new("fluidics.waste.not-full", "废液未满", PrecheckCategories.Fluidics, 9, false, true),
        new("fluidics.toxic-waste.not-full", "排毒桶未满", PrecheckCategories.Fluidics, 10, false, true)
    ];

    private static readonly IReadOnlyDictionary<string, CheckDefinition> ChecksById =
        Checks.ToDictionary(c => c.CheckId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> KnownCheckIds => Checks.Select(c => c.CheckId).ToList();

    /// <summary>POST /api/prechecks：主动执行全部 11 项真实检查，返回 11 项独立结果，并持久化为最近一次完整报告。</summary>
    public Task<PrecheckReportResponse> RunAllAsync(PrecheckRunRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "precheck.run",
            request,
            actor,
            async () =>
            {
                var runMode = ValidateRunMode(request.RunMode);
                var checks = new List<PrecheckCheckResponse>();
                foreach (var definition in Checks)
                {
                    checks.Add(runMode == RuntimeModes.Twin
                        ? await ExecuteActiveAsync(definition, request.CommandId, actor, cancellationToken)
                        : await ExecuteRealActiveAsync(definition, cancellationToken));
                }

                var reportId = Guid.NewGuid().ToString("N");
                await PersistReportAsync(reportId, request.CommandId, runMode, checks, cancellationToken);
                var report = BuildReport(reportId, request.CommandId, runMode, checks);
                return new CommandExecutionResult<PrecheckReportResponse>(report, "PrecheckReport", report.ReportId);
            },
            cancellationToken);
    }

    /// <summary>POST /api/prechecks/{checkId}：只执行并返回指定项；未知 checkId 抛出明确业务错误。
    /// 单项检测不写完整报告，故单项通过不会让 preflight 把其余项判绿。</summary>
    public Task<PrecheckReportResponse> RunOneAsync(string checkId, PrecheckRunRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        var definition = Resolve(checkId);
        return idempotencyService.RunAsync(
            request.CommandId,
            $"precheck.check.{definition.CheckId}",
            new { checkId = definition.CheckId, request },
            actor,
            async () =>
            {
                var runMode = ValidateRunMode(request.RunMode);
                // 单项检测会使本模式下的旧全量报告失效：单项结果（无论通过与否）都不代表整体就绪，
                // 必须重新“一键检测”才能形成可启动的全量报告。避免旧报告掩盖单项复测失败。
                await InvalidateFullReportsAsync(DeviceModeFor(runMode), cancellationToken);
                var check = runMode == RuntimeModes.Twin
                    ? await ExecuteActiveAsync(definition, request.CommandId, actor, cancellationToken)
                    : await ExecuteRealActiveAsync(definition, cancellationToken);
                var report = BuildReport(Guid.NewGuid().ToString("N"), request.CommandId, runMode, [check]);
                return new CommandExecutionResult<PrecheckReportResponse>(report, "PrecheckReport", report.ReportId);
            },
            cancellationToken);
    }

    /// <summary>
    /// 只读评估全部 11 项（供 preflight）。只读取数据库/实时状态，绝不触发回零、洗针、重连等动作。
    /// 探测型检查（主控/扫码器）不允许在只读路径做通信探测，改由“最近一次完整预检报告（有效期内的，主）
    /// / 最近一次 Ready 的设备初始化（备）”读取真实探测结果；两者皆无则 Unavailable（Mock 亦不默认通过）。
    /// </summary>
    public async Task<IReadOnlyList<PrecheckCheckResponse>> EvaluateReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        var context = await LoadProbeContextAsync(cancellationToken);
        var results = new List<PrecheckCheckResponse>();
        foreach (var definition in Checks)
        {
            results.Add(await EvaluateReadOnlyAsync(definition, context, cancellationToken));
        }

        return results;
    }

    private async Task<PrecheckCheckResponse> ExecuteRealActiveAsync(CheckDefinition definition, CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            return definition.CheckId switch
            {
                "device.controller.connected" => FromRealRead(definition, await realDeviceReadAdapter.ReadControllerWorkStatusAsync(cancellationToken), checkedAt),
                "motion.arm.home" => FromRealRead(definition, await realDeviceReadAdapter.ReadControllerNodeStatusesAsync(cancellationToken), checkedAt),
                "thermal.cooling.connected" => FromRealRead(definition, await realDeviceReadAdapter.ReadCoolingSnapshotAsync(cancellationToken), checkedAt),
                "scanner.sample.online" => FromRealRead(definition, await realDeviceReadAdapter.ReceiveDcr55ResultAsync(cancellationToken), checkedAt),
                "scanner.reagent.online" => FromRealRead(definition, await realDeviceReadAdapter.ReadQrScanStatusAsync(cancellationToken), checkedAt),
                "fluidics.level-sensors.readable" => FromRealRead(definition, await realDeviceReadAdapter.ReceiveLiquidLevelStatusAsync(cancellationToken), checkedAt),
                "motion.needle-wash.ready" => FromRealRead(definition, await realDeviceReadAdapter.ReadControllerNodeStatusesAsync(cancellationToken), checkedAt),
                _ => FromRealRead(definition, await realDeviceReadAdapter.ReceiveLiquidLevelStatusAsync(cancellationToken), checkedAt)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return FromException(definition, ex, checkedAt);
        }
    }

    private static PrecheckCheckResponse FromRealRead<T>(CheckDefinition definition, RealDeviceReadResult<T> result, DateTimeOffset checkedAt)
    {
        var data = new Dictionary<string, object?>
        {
            ["source"] = "Real",
            ["hardwareAccessAttempted"] = true,
            ["hardwareAccessed"] = result.ResponseBytes.Length > 0,
            ["transportStatus"] = result.Status,
            ["requestBytes"] = Convert.ToHexString(result.RequestBytes),
            ["responseBytes"] = Convert.ToHexString(result.ResponseBytes)
        };
        return result.Ok
            ? Passed(definition, data, checkedAt)
            : Failed(
                definition,
                result.ErrorCode ?? DefaultCode(definition),
                string.IsNullOrWhiteSpace(result.Message) ? $"{definition.Label} real-device check failed." : result.Message,
                checkedAt,
                data,
                result.Status is DeviceByteTransportStatuses.NotConnected or DeviceByteTransportStatuses.Disconnected
                    ? PrecheckStatuses.Unavailable
                    : PrecheckStatuses.Failed);
    }

    private async Task<PrecheckCheckResponse> ExecuteActiveAsync(CheckDefinition definition, string commandId, AuthenticatedUser actor, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (definition.IsLiquid)
            {
                return await EvaluateLiquidCheckAsync(definition, startedAt, cancellationToken);
            }

            // 液位传感器：执行设备初始化的读传感器步骤后，仍以严格只读校验确认（容器齐全/容量/读数区间/新鲜度）。
            if (definition.CheckId == "fluidics.level-sensors.readable")
            {
                var levelStep = await deviceInitializationService.ExecuteStartupStepAsync(definition.StepIndex, $"{commandId}#{definition.CheckId}", actor, cancellationToken);
                if (!string.Equals(levelStep.Status, DeviceCommandStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
                {
                    return ToCheckResponse(definition, levelStep, startedAt);
                }

                return await EvaluateLevelSensorsReadOnlyAsync(definition, DateTimeOffset.UtcNow, cancellationToken);
            }

            if (definition.IsAction)
            {
                // 机械臂回零 / 洗针是真实动作：活动运行互锁 + commandId 幂等（由外层 idempotency 保证不重复执行）。
                await EnsureNoActiveRunAsync(cancellationToken);
                var stepCommandId = $"{commandId}#{definition.CheckId}";
                var stepResult = await deviceInitializationService.ExecuteStartupStepAsync(definition.StepIndex, stepCommandId, actor, cancellationToken);
                if (!string.Equals(stepResult.Status, DeviceCommandStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
                {
                    return ToCheckResponse(definition, stepResult, startedAt);
                }

                // 动作成功后，仍以真实状态校验通过条件（针/臂必须连接正常、无错误、满足 VolumeUl=0/NeedsWash=false/IsHomed）。
                return definition.CheckId == "motion.arm.home"
                    ? await EvaluateArmReadOnlyAsync(definition, DateTimeOffset.UtcNow, cancellationToken)
                    : await EvaluateNeedleWashReadOnlyAsync(definition, DateTimeOffset.UtcNow, cancellationToken);
            }

            var result = await deviceInitializationService.ExecuteStartupStepAsync(definition.StepIndex, $"{commandId}#{definition.CheckId}", actor, cancellationToken);
            return ToCheckResponse(definition, result, startedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return FromException(definition, ex, startedAt);
        }
    }

    private async Task<PrecheckCheckResponse> EvaluateReadOnlyAsync(CheckDefinition definition, ProbeContext context, CancellationToken cancellationToken)
    {
        var at = DateTimeOffset.UtcNow;
        try
        {
            return definition.CheckId switch
            {
                "device.controller.connected" => EvaluateProbedCheck(definition, context, DeviceModules.Controller, at, "device_controller_not_probed", "主控尚无有效的预检/初始化探测记录（只读校验）。"),
                "scanner.sample.online" => EvaluateProbedCheck(definition, context, DeviceModules.SampleScanner, at, "scanner_sample_not_probed", "样本扫码器尚无有效的预检/初始化探测记录（只读校验）。"),
                "scanner.reagent.online" => EvaluateProbedCheck(definition, context, DeviceModules.ReagentScanner, at, "scanner_reagent_not_probed", "试剂扫码器尚无有效的预检/初始化探测记录（只读校验）。"),
                "motion.arm.home" => await EvaluateArmReadOnlyAsync(definition, at, cancellationToken),
                "thermal.cooling.connected" => await EvaluateCoolingReadOnlyAsync(definition, at, cancellationToken),
                "fluidics.level-sensors.readable" => await EvaluateLevelSensorsReadOnlyAsync(definition, at, cancellationToken),
                "motion.needle-wash.ready" => await EvaluateNeedleWashReadOnlyAsync(definition, at, cancellationToken),
                _ => await EvaluateLiquidCheckAsync(definition, at, cancellationToken)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return FromException(definition, ex, at);
        }
    }

    // 探测型检查（主控/扫码器）：优先取有效期内的最近一次完整预检报告；回退到最近一次 Ready 设备初始化的对应步骤；
    // 都没有则 Unavailable。绝不因 Mock 模式无条件判通过。
    private PrecheckCheckResponse EvaluateProbedCheck(CheckDefinition definition, ProbeContext context, string moduleCode, DateTimeOffset at, string missingCode, string missingMessage)
    {
        var data = new Dictionary<string, object?> { ["moduleCode"] = moduleCode, ["source"] = (string?)null };
        if (context.ReportMap is not null && context.ReportMap.TryGetValue(definition.CheckId, out var reportedStatus))
        {
            data["source"] = "precheckReport";
            data["reportedStatus"] = reportedStatus;
            return reportedStatus switch
            {
                PrecheckStatuses.Passed => Passed(definition, data, at),
                PrecheckStatuses.Unavailable => Failed(definition, DefaultCode(definition), $"{definition.Label} 最近一次预检为不可用。", at, data, PrecheckStatuses.Unavailable),
                _ => Failed(definition, DefaultCode(definition), $"{definition.Label} 最近一次预检未通过。", at, data)
            };
        }

        if (context.LatestInitRun is not null && string.Equals(context.LatestInitRun.DeviceMode, context.Mode, StringComparison.OrdinalIgnoreCase))
        {
            var initReady = context.LatestInitRun.Status == DeviceInitializationStatus.Ready;
            var stepSucceeded = context.LatestInitRun.Checks.Any(x => string.Equals(x.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Status, DeviceInitializationCheckStatus.Succeeded, StringComparison.OrdinalIgnoreCase));
            data["source"] = "deviceInitialization";
            data["latestInitStatus"] = context.LatestInitRun.Status;
            if (initReady && stepSucceeded)
            {
                return Passed(definition, data, at);
            }
        }

        return Failed(definition, missingCode, missingMessage, at, data, PrecheckStatuses.Unavailable);
    }

    // 机械臂：RobotArmStates，要求连接、已回零、位于 Home 坐标容差内、无错误，且状态可确认（只读，不回零）。
    private async Task<PrecheckCheckResponse> EvaluateArmReadOnlyAsync(CheckDefinition definition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var arm = await dbContext.RobotArmStates.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (arm is null)
        {
            return Failed(definition, "robot_arm_not_found", "机械臂状态记录不存在。", at, null, PrecheckStatuses.Unavailable);
        }

        var coordinatesAtHome = IsHomeCoordinate(arm.CurrentXUm)
            && IsHomeCoordinate(arm.CurrentYUm)
            && IsHomeCoordinate(arm.CurrentZUm);
        var targetAtHome = string.Equals(arm.CurrentTargetPointCode, "Home", StringComparison.OrdinalIgnoreCase);
        var data = ArmData(arm, coordinatesAtHome, targetAtHome);
        var ok = arm.IsConnected
            && arm.IsHomed
            && coordinatesAtHome
            && targetAtHome
            && string.IsNullOrEmpty(arm.LastErrorCode)
            && !IsUncertainMotionStatus(arm.Status);
        var failureCode = arm.LastErrorCode
            ?? (!coordinatesAtHome || !targetAtHome ? "robot_arm_home_coordinate_invalid" : "robot_arm_not_ready");
        return ok
            ? Passed(definition, data, at)
            : Failed(definition, failureCode,
                $"机械臂未在回零位置：homed={arm.IsHomed}, connected={arm.IsConnected}, target={arm.CurrentTargetPointCode ?? "—"}, " +
                $"xyz=({arm.CurrentXUm?.ToString() ?? "—"},{arm.CurrentYUm?.ToString() ?? "—"},{arm.CurrentZUm?.ToString() ?? "—"})μm, status={arm.Status}.",
                at, data);
    }

    // 制冷：ThermalControlService 数据源（CoolingUnitState），要求在线、无故障且状态可确认（只读）。
    private async Task<PrecheckCheckResponse> EvaluateCoolingReadOnlyAsync(CheckDefinition definition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var cooling = await dbContext.CoolingUnitStates.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cooling is null)
        {
            return Failed(definition, "cooling_not_found", "制冷单元状态记录不存在。", at, null, PrecheckStatuses.Unavailable);
        }

        var data = CoolingData(cooling);
        var ok = cooling.IsConnected
            && string.IsNullOrEmpty(cooling.FaultCode)
            && !string.Equals(cooling.Status, ThermalStatuses.Faulted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cooling.Status, ThermalStatuses.Unknown, StringComparison.OrdinalIgnoreCase);
        return ok
            ? Passed(definition, data, at)
            : Failed(definition, cooling.FaultCode ?? "cooling_not_ready", $"制冷单元未就绪：connected={cooling.IsConnected}, status={cooling.Status}.", at, data);
    }

    // 液位传感器：LiquidContainerStates，要求四个必需容器齐全、连接正常、无故障、读数有效（容量>0、0≤当前≤容量）、数据未过期（只读）。
    private async Task<PrecheckCheckResponse> EvaluateLevelSensorsReadOnlyAsync(CheckDefinition definition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var liquids = await dbContext.LiquidContainerStates.AsNoTracking().ToListAsync(cancellationToken);
        var bySource = liquids.ToDictionary(x => x.SourceType, StringComparer.OrdinalIgnoreCase);
        var data = new Dictionary<string, object?>
        {
            ["containers"] = liquids.Select(LiquidData).ToList(),
            ["requiredSources"] = RequiredLiquidSources.ToArray()
        };

        var missing = RequiredLiquidSources.Where(s => !bySource.ContainsKey(s)).ToList();
        if (missing.Count > 0)
        {
            return Failed(definition, "liquid_containers_missing", $"必需液位容器缺失：{string.Join(", ", missing)}。", at, data, PrecheckStatuses.Unavailable);
        }

        var staleCutoff = DateTimeOffset.UtcNow - MaxLiquidDataAge;
        foreach (var source in RequiredLiquidSources)
        {
            var liquid = bySource[source];
            if (!liquid.IsConnected || !string.IsNullOrEmpty(liquid.FaultCode)
                || string.Equals(liquid.LevelStatus, LiquidLevelStatuses.SensorFault, StringComparison.OrdinalIgnoreCase)
                || string.Equals(liquid.LevelStatus, LiquidLevelStatuses.Disconnected, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(definition, $"{liquid.SourceType}_sensor_fault", $"液位传感器 {liquid.SourceType} 不可用：status={liquid.LevelStatus}, connected={liquid.IsConnected}, fault={liquid.FaultCode}.", at, data);
            }

            if (liquid.CapacityUl <= 0)
            {
                return Failed(definition, $"{liquid.SourceType}_invalid_capacity", $"液位传感器 {liquid.SourceType} 容量无效：{liquid.CapacityUl}.", at, data);
            }

            if (liquid.CurrentVolumeUl < 0 || liquid.CurrentVolumeUl > liquid.CapacityUl)
            {
                return Failed(definition, $"{liquid.SourceType}_reading_out_of_range", $"液位传感器 {liquid.SourceType} 读数越界：{liquid.CurrentVolumeUl} ∉ [0, {liquid.CapacityUl}].", at, data);
            }

            if (liquid.UpdatedAtUtc < staleCutoff)
            {
                return Failed(definition, $"{liquid.SourceType}_data_stale", $"液位传感器 {liquid.SourceType} 数据过期。", at, data);
            }
        }

        return Passed(definition, data, at);
    }

    // 洗针：NeedleStates，要求针存在、连接正常、无错误、VolumeUl=0、NeedsWash=false，且状态可确认（只读，不洗针）。
    private async Task<PrecheckCheckResponse> EvaluateNeedleWashReadOnlyAsync(CheckDefinition definition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var needles = await dbContext.NeedleStates.AsNoTracking().OrderBy(x => x.NeedleNo).ToListAsync(cancellationToken);
        var data = new Dictionary<string, object?> { ["needles"] = needles.Select(NeedleData).ToList() };
        if (needles.Count == 0)
        {
            return Failed(definition, "needle_not_found", "针状态记录不存在。", at, data, PrecheckStatuses.Unavailable);
        }

        var bad = needles.FirstOrDefault(x => !x.IsConnected
            || !string.IsNullOrEmpty(x.LastErrorCode)
            || x.VolumeUl != 0
            || x.NeedsWash
            || IsUncertainMotionStatus(x.Status));
        return bad is null
            ? Passed(definition, data, at)
            : Failed(definition, bad.LastErrorCode ?? "needle_not_ready", $"针 {bad.NeedleCode} 未就绪：volume={bad.VolumeUl}, needsWash={bad.NeedsWash}, connected={bad.IsConnected}, status={bad.Status}.", at, data);
    }

    // 纯水/PBS/废液/排毒桶：FluidicsControlService + LiquidContainerStates，按阈值边界判定（只读与主动共用）。
    // CurrentVolumeUl <= LowThresholdUl 视为不足；CurrentVolumeUl >= FullThresholdUl 视为已满（边界值按失败处理）。
    private async Task<PrecheckCheckResponse> EvaluateLiquidCheckAsync(CheckDefinition definition, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var (sourceType, isWaste) = definition.CheckId switch
        {
            "fluidics.system-water.available" => (LiquidSourceTypes.SystemWater, false),
            "fluidics.pbs.available" => (LiquidSourceTypes.Pbs, false),
            "fluidics.waste.not-full" => (LiquidSourceTypes.Waste, true),
            "fluidics.toxic-waste.not-full" => (LiquidSourceTypes.ToxicWaste, true),
            _ => (string.Empty, false)
        };

        var state = await fluidicsControlService.GetStateAsync(cancellationToken);
        var liquid = state.LiquidLevels.SingleOrDefault(x => string.Equals(x.SourceType, sourceType, StringComparison.OrdinalIgnoreCase));
        var data = new Dictionary<string, object?>
        {
            ["sourceType"] = sourceType,
            ["isWaste"] = isWaste,
            ["currentVolumeUl"] = liquid?.CurrentVolumeUl,
            ["lowThresholdUl"] = liquid?.LowThresholdUl,
            ["fullThresholdUl"] = liquid?.FullThresholdUl,
            ["capacityUl"] = liquid?.CapacityUl,
            ["isConnected"] = liquid?.IsConnected,
            ["faultCode"] = liquid?.FaultCode,
            ["levelStatus"] = liquid?.LevelStatus
        };

        if (liquid is null)
        {
            return Failed(definition, $"{sourceType}_not_found", $"液源 {sourceType} 记录不存在。", at, data, PrecheckStatuses.Unavailable);
        }

        if (!liquid.IsConnected || !string.IsNullOrEmpty(liquid.FaultCode))
        {
            return Failed(definition, $"{sourceType}_sensor_fault", $"液源 {sourceType} 传感器不可用：connected={liquid.IsConnected}, fault={liquid.FaultCode}.", at, data);
        }

        if (!isWaste)
        {
            if (liquid.CurrentVolumeUl <= liquid.LowThresholdUl)
            {
                return Failed(definition, $"{sourceType}_low", $"液源 {sourceType} 余量不足（{liquid.CurrentVolumeUl} ≤ 阈值 {liquid.LowThresholdUl}）。", at, data);
            }
        }
        else
        {
            if (liquid.CurrentVolumeUl >= liquid.FullThresholdUl)
            {
                return Failed(definition, $"{sourceType}_full", $"液源 {sourceType} 已满（{liquid.CurrentVolumeUl} ≥ 满阈值 {liquid.FullThresholdUl}）。", at, data);
            }
        }

        return Passed(definition, data, at);
    }

    private async Task PersistReportAsync(string reportId, string commandId, string runMode, IReadOnlyList<PrecheckCheckResponse> checks, CancellationToken cancellationToken)
    {
        dbContext.DevicePrecheckRuns.Add(new DevicePrecheckRun
        {
            Id = reportId,
            CommandId = commandId,
            DeviceMode = DeviceModeFor(runMode),
            RunMode = runMode,
            Ok = checks.Count > 0 && checks.All(x => PrecheckStatuses.IsPassing(x.Status)),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ChecksJson = JsonSerializer.Serialize(checks.Select(c => new ReportCheckEntry(
                c.CheckId, c.Category, c.Status, c.CheckedAtUtc, c.Code,
                PrecheckStatuses.IsPassing(c.Status) ? null : c.Message)), JsonOptions)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task InvalidateFullReportsAsync(string mode, CancellationToken cancellationToken)
    {
        var existing = await dbContext.DevicePrecheckRuns.Where(x => x.DeviceMode == mode).ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            dbContext.DevicePrecheckRuns.RemoveRange(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    // SQLite 不支持以 DateTimeOffset 列做 SQL ORDER BY，故先物化再在内存中排序（与 DeviceInitializationService.GetLatestAsync 一致）。
    private async Task<ProbeContext> LoadProbeContextAsync(CancellationToken cancellationToken)
    {
        var mode = deviceAdapter.Mode;
        Dictionary<string, string>? reportMap = null;
        var cutoff = DateTimeOffset.UtcNow - MaxReportAge;
        var reportRuns = await dbContext.DevicePrecheckRuns.AsNoTracking().Where(x => x.DeviceMode == mode).ToListAsync(cancellationToken);
        var latestReport = reportRuns.OrderByDescending(x => x.GeneratedAtUtc).FirstOrDefault();
        if (latestReport is not null && latestReport.GeneratedAtUtc >= cutoff)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<ReportCheckEntry>>(latestReport.ChecksJson, JsonOptions);
                reportMap = entries?.ToDictionary(e => e.CheckId, e => e.Status, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                reportMap = null;
            }
        }

        var initRuns = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .Include(x => x.Checks)
            .ToListAsync(cancellationToken);
        var latestInitRun = initRuns
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.AttemptNo)
            .FirstOrDefault();

        return new ProbeContext(reportMap, latestInitRun, mode);
    }

    // 不可确认的运动状态（Faulted/TimedOut/Unknown/Disconnected）一律视为未就绪。
    private static bool IsUncertainMotionStatus(string? status) =>
        string.Equals(status, MotionStatuses.Faulted, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MotionStatuses.TimedOut, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MotionStatuses.Unknown, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MotionStatuses.Disconnected, StringComparison.OrdinalIgnoreCase);

    private static PrecheckCheckResponse ToCheckResponse(CheckDefinition definition, DeviceCommandResult result, DateTimeOffset startedAt)
    {
        var status = MapStatus(result.Status);
        var passed = PrecheckStatuses.IsPassing(status);
        var code = passed ? null : (result.ErrorCode ?? DefaultCode(definition));
        var message = passed
            ? $"{definition.Label} 通过。"
            : (string.IsNullOrWhiteSpace(result.Message) ? $"{definition.Label} 未通过。" : result.Message);
        var checkedAt = result.CompletedAtUtc == default ? DateTimeOffset.UtcNow : result.CompletedAtUtc;
        return new PrecheckCheckResponse(definition.CheckId, definition.Label, definition.Category, status, true, code, message, checkedAt, result.Data);
    }

    private static string MapStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return PrecheckStatuses.Failed;
        }

        return status switch
        {
            DeviceCommandStatuses.Succeeded => PrecheckStatuses.Passed,
            DeviceCommandStatuses.NotSupported or DeviceCommandStatuses.Offline or DeviceCommandStatuses.NotConfigured => PrecheckStatuses.Unavailable,
            _ => PrecheckStatuses.Failed
        };
    }

    private static PrecheckCheckResponse Passed(CheckDefinition definition, IReadOnlyDictionary<string, object?> data, DateTimeOffset at) =>
        new(definition.CheckId, definition.Label, definition.Category, PrecheckStatuses.Passed, true, null, $"{definition.Label} 通过。", at, data);

    private static PrecheckCheckResponse Failed(CheckDefinition definition, string code, string message, DateTimeOffset at, IReadOnlyDictionary<string, object?>? data, string status = PrecheckStatuses.Failed) =>
        new(definition.CheckId, definition.Label, definition.Category, status, true, code, message, at, data ?? new Dictionary<string, object?>());

    private static PrecheckCheckResponse FromException(CheckDefinition definition, Exception exception, DateTimeOffset at)
    {
        var code = exception is BusinessRuleException business ? business.Code : "precheck_check_failed";
        return new PrecheckCheckResponse(definition.CheckId, definition.Label, definition.Category, PrecheckStatuses.Failed, true, code, exception.Message, at, new Dictionary<string, object?> { ["exceptionType"] = exception.GetType().Name });
    }

    private PrecheckReportResponse BuildReport(string reportId, string commandId, string runMode, IReadOnlyList<PrecheckCheckResponse> checks)
    {
        var ok = checks.Count > 0 && checks.All(x => PrecheckStatuses.IsPassing(x.Status));
        return new PrecheckReportResponse(ok, reportId, commandId, checks, DateTimeOffset.UtcNow, runMode, DeviceModeFor(runMode));
    }

    private string ValidateRunMode(string? requested)
    {
        return RuntimeModes.Normalize(requested, deviceAdapter.Mode);
    }

    private static string DeviceModeFor(string runMode) =>
        runMode == RuntimeModes.Twin ? DeviceModes.Mock : DeviceModes.Real;

    private static CheckDefinition Resolve(string checkId)
    {
        if (string.IsNullOrWhiteSpace(checkId) || !ChecksById.TryGetValue(checkId, out var definition))
        {
            throw new BusinessRuleException("precheck_check_unknown", $"Unknown precheck checkId '{checkId}'.", StatusCodes.Status400BadRequest);
        }

        return definition;
    }

    private async Task EnsureNoActiveRunAsync(CancellationToken cancellationToken)
    {
        var active = await dbContext.MachineRuns.AsNoTracking()
            .AnyAsync(x => ActiveRunStatuses.Contains(x.Status), cancellationToken);
        if (active)
        {
            throw new BusinessRuleException("precheck_action_blocked_run_active", "无法执行机械臂回零/洗针：当前存在活动染色流程（运行/暂停/故障等）。", StatusCodes.Status409Conflict);
        }
    }

    private static string DefaultCode(CheckDefinition definition) => definition.CheckId.Replace('.', '_') + "_failed";

    private static bool IsHomeCoordinate(long? coordinateUm) =>
        coordinateUm.HasValue && coordinateUm.Value >= -HomeCoordinateToleranceUm && coordinateUm.Value <= HomeCoordinateToleranceUm;

    private static Dictionary<string, object?> ArmData(RobotArmState arm, bool coordinatesAtHome, bool targetAtHome) => new()
    {
        ["isHomed"] = arm.IsHomed,
        ["isConnected"] = arm.IsConnected,
        ["status"] = arm.Status,
        ["lastErrorCode"] = arm.LastErrorCode,
        ["currentTargetPointCode"] = arm.CurrentTargetPointCode,
        ["currentXUm"] = arm.CurrentXUm,
        ["currentYUm"] = arm.CurrentYUm,
        ["currentZUm"] = arm.CurrentZUm,
        ["homeCoordinateToleranceUm"] = HomeCoordinateToleranceUm,
        ["coordinatesAtHome"] = coordinatesAtHome,
        ["targetAtHome"] = targetAtHome
    };

    private static Dictionary<string, object?> CoolingData(CoolingUnitState cooling) => new()
    {
        ["isConnected"] = cooling.IsConnected,
        ["status"] = cooling.Status,
        ["currentTemperatureDeciC"] = cooling.CurrentTemperatureDeciC,
        ["targetTemperatureDeciC"] = cooling.TargetTemperatureDeciC,
        ["faultCode"] = cooling.FaultCode
    };

    private static Dictionary<string, object?> NeedleData(NeedleState needle) => new()
    {
        ["needleCode"] = needle.NeedleCode,
        ["isConnected"] = needle.IsConnected,
        ["status"] = needle.Status,
        ["volumeUl"] = needle.VolumeUl,
        ["needsWash"] = needle.NeedsWash,
        ["lastErrorCode"] = needle.LastErrorCode
    };

    private static Dictionary<string, object?> LiquidData(LiquidContainerState liquid) => new()
    {
        ["sourceType"] = liquid.SourceType,
        ["currentVolumeUl"] = liquid.CurrentVolumeUl,
        ["lowThresholdUl"] = liquid.LowThresholdUl,
        ["fullThresholdUl"] = liquid.FullThresholdUl,
        ["levelStatus"] = liquid.LevelStatus,
        ["isConnected"] = liquid.IsConnected,
        ["faultCode"] = liquid.FaultCode,
        ["updatedAtUtc"] = liquid.UpdatedAtUtc
    };
}
