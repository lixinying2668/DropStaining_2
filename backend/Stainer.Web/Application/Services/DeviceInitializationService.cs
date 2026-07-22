using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DeviceInitializationService(
    IDeviceAdapter deviceAdapter,
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    SafetyLogWriter safetyLogWriter,
    DeviceCommunicationPersistenceService communicationPersistence,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService)
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<InitializationStep> Steps =
    [
        new(DeviceModules.Controller, "connect", "SCDevice.Connect / OpenPort / ConnectEthernet", "SOCON.API V3.1", "第10页（Connect）"),
        new(DeviceModules.RobotArm, "home", "InitX / InitY / InitZ / InitDevice / CheckHome", "SOCON.API V3.1", "第19-22页"),
        new(DeviceModules.Cooling, "connect", "主控 TL_COOL_GET_MODULE_CONNECT 0x03/0x01（+ 当前/目标温度 0x03/0x02、0x03/0x03，开关 0x03/0x05）", "冰免通讯协议 ver1.0.6", "制冷模块（主控 0x03）"),
        new(DeviceModules.SampleScanner, "check-online", "IO状态 / DCR55通信状态检测", "DCR55说明书", "第13-15页"),
        new(DeviceModules.ReagentScanner, "check-online", "IO状态 / 扫码心跳 / 通信状态", "DCR55说明书", "第13-15页"),
        new(DeviceModules.LiquidLevel, "read-sensors", "LiqDet / GetIOState / 光耦读取", "SOCON.API V3.1", "第16-25页"),
        new(DeviceModules.NeedleWash, "prepare", "SOPAReset / SuckMix / Aspirate / Dispense", "SOCON.API V3.1", "第34-35页"),
        new(DeviceModules.LiquidLevel, "check-system-water", "GetIOState / LiqDet / 光耦输入", "SOCON.API V3.1", "第16页"),
        new(DeviceModules.LiquidLevel, "check-pbs", "GetIOState / LiqDet / IO液位检测", "SOCON.API V3.1", "第16页"),
        new(DeviceModules.LiquidLevel, "check-waste-not-full", "GetIOState / IO液位开关", "SOCON.API V3.1", "第16页"),
        new(DeviceModules.LiquidLevel, "check-toxic-waste-not-full", "GetIOState / IO输入检测", "SOCON.API V3.1", "第16页")
    ];

    public async Task<DeviceInitializationResponse> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Checks)
            .ToListAsync(cancellationToken);
        var run = runs
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.AttemptNo)
            .FirstOrDefault();
        return run is null
            ? new DeviceInitializationResponse(
                false,
                string.Empty,
                false,
                null,
                "NotStarted",
                deviceAdapter.Mode,
                deviceAdapter.Name,
                0,
                null,
                null,
                null,
                [],
                "Device initialization has not started.")
            : ToResponse(run, false);
    }

    /// <summary>
    /// 启动期 11 个生命周期步骤的数量（与 <see cref="ExecuteStartupStepAsync"/> 的 stepIndex 一一对应，0 基）。
    /// 供 DevicePrecheckService 复用同一套步骤逻辑，避免在外部复制近似实现。
    /// </summary>
    public int StartupStepCount => Steps.Count;

    /// <summary>
    /// 执行单个启动期步骤，复用与完整设备初始化完全相同的 <see cref="ExecuteStepAsync"/> 分发逻辑
    /// （含机械臂回零、洗针、制冷连接、扫码器在线探测、液位读取、液量校验等），并写入通信持久化。
    /// 不创建 DeviceInitializationRun；只返回该步骤的设备命令结果，供预检按项映射。
    /// </summary>
    public async Task<DeviceCommandResult> ExecuteStartupStepAsync(
        int stepIndex,
        string commandId,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if ((uint)stepIndex >= (uint)Steps.Count)
        {
            throw new BusinessRuleException(
                "device_initialization_step_invalid",
                $"Startup step index {stepIndex} is out of range.",
                StatusCodes.Status400BadRequest);
        }

        var step = Steps[stepIndex];
        var startedAtUtc = DateTimeOffset.UtcNow;
        var operationRequest = new DeviceOperationRequest(
            new DeviceCommandContext($"{commandId}:step-{stepIndex + 1:00}:{step.ModuleCode}", commandId, actor.Username, nameof(DeviceInitializationService)),
            step.ModuleCode,
            step.Action,
            CreateStepParameters(step));
        var communicationRecord = communicationPersistence.Begin(operationRequest);
        DeviceCommandResult result;
        try
        {
            result = await ExecuteStepAsync(step, operationRequest, startedAtUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = ExceptionResult(operationRequest, startedAtUtc, ex);
        }

        result = result with { Data = Merge(operationRequest.Parameters, result.Data) };
        communicationPersistence.Complete(communicationRecord, result);
        return result;
    }

    public Task<DeviceInitializationResponse> InitializeAsync(
        StartDeviceInitializationRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteSerializedAsync(request.CommandId, null, null, request, actor, cancellationToken);
    }

    public async Task<DeviceInitializationResponse> RetryAsync(
        string initializationRunId,
        RetryDeviceInitializationRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == initializationRunId, cancellationToken)
            ?? throw new BusinessRuleException("device_initialization_not_found", "Device initialization run was not found.", StatusCodes.Status404NotFound);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("reason_required", "Retrying device initialization requires a reason.", StatusCodes.Status400BadRequest);
        }

        return await ExecuteSerializedAsync(request.CommandId, source, reason, request, actor, cancellationToken);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var latest = runs.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (latest is null
            || latest.Status != DeviceInitializationStatus.Ready
            || !string.Equals(latest.DeviceMode, deviceAdapter.Mode, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "device_initialization_required",
                "A successful device initialization for the current DeviceMode is required before run start.",
                StatusCodes.Status409Conflict);
        }


        await thermalControlService.EnsureReadyForRunAsync(cancellationToken);
        await fluidicsControlService.EnsureReadyForRunAsync(cancellationToken);
        await motionControlService.EnsureReadyForRunAsync(cancellationToken);
    }

    private async Task<DeviceInitializationResponse> ExecuteSerializedAsync(
        string commandId,
        DeviceInitializationRun? retryOf,
        string? retryReason,
        object request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        await InitializationGate.WaitAsync(cancellationToken);
        try
        {
            return await idempotencyService.RunAsync(
                commandId,
                retryOf is null ? "device.initialization.start" : "device.initialization.retry",
                retryOf is null ? request : new { retryOfRunId = retryOf.Id, request },
                actor,
                async () =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var run = new DeviceInitializationRun
                    {
                        CommandId = commandId,
                        Status = DeviceInitializationStatus.Running,
                        DeviceMode = deviceAdapter.Mode,
                        AdapterName = deviceAdapter.Name,
                        AttemptNo = retryOf is null ? 1 : retryOf.AttemptNo + 1,
                        RetryOfRunId = retryOf?.Id,
                        RequestedByUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                        StartedAtUtc = now,
                        CreatedAtUtc = now
                    };
                    foreach (var (step, index) in Steps.Select((value, index) => (value, index)))
                    {
                        run.Checks.Add(new DeviceInitializationCheck
                        {
                            StepNo = index + 1,
                            ModuleCode = step.ModuleCode,
                            Status = DeviceInitializationCheckStatus.Pending
                        });
                    }

                    dbContext.DeviceInitializationRuns.Add(run);
                    foreach (var check in run.Checks.OrderBy(x => x.StepNo))
                    {
                        var step = Steps[check.StepNo - 1];
                        check.Status = DeviceInitializationCheckStatus.Running;
                        check.StartedAtUtc = DateTimeOffset.UtcNow;
                        PublishProgress(run, check, commandId);

                        var operationRequest = new DeviceOperationRequest(
                            new DeviceCommandContext($"{commandId}:step-{check.StepNo:00}:{check.ModuleCode}", commandId, actor.Username, nameof(DeviceInitializationService)),
                            check.ModuleCode,
                            step.Action,
                            CreateStepParameters(step));
                        var communicationRecord = communicationPersistence.Begin(operationRequest);
                        DeviceCommandResult result;
                        try
                        {
                            result = await ExecuteStepAsync(step, operationRequest, check.StartedAtUtc.Value, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        {
                            result = ExceptionResult(operationRequest, check.StartedAtUtc.Value, ex);
                        }

                        result = result with { Data = Merge(operationRequest.Parameters, result.Data) };
                        communicationPersistence.Complete(communicationRecord, result);
                        check.Status = MapCheckStatus(result.Status);
                        check.ErrorCode = result.ErrorCode;
                        check.Message = result.Message;
                        check.ResultJson = JsonSerializer.Serialize(result.Data, JsonOptions);
                        check.CompletedAtUtc = result.CompletedAtUtc;
                        PublishProgress(run, check, commandId);
                    }

                    await AppendReadinessFailuresAsync(run, commandId, cancellationToken);

                    var failed = run.Checks.FirstOrDefault(x => x.Status != DeviceInitializationCheckStatus.Succeeded);
                    run.CompletedAtUtc = DateTimeOffset.UtcNow;
                    run.Status = failed is null ? DeviceInitializationStatus.Ready : DeviceInitializationStatus.Failed;
                    run.FailureCode = failed?.ErrorCode;
                    run.Message = failed is null
                        ? "Device initialization completed successfully."
                        : $"Device initialization failed at {failed.ModuleCode}: {failed.Message}";
                    if (failed is not null)
                    {
                        dbContext.Alarms.Add(new Alarm
                        {
                            Code = "device_initialization_failed",
                            Severity = "Error",
                            Message = $"InitializationRun={run.Id}; Module={failed.ModuleCode}; Error={failed.ErrorCode}; {failed.Message}",
                            Status = "Active",
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        });
                    }

                    dbContext.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                        Action = failed is null ? "device.initialization.completed" : "device.initialization.failed",
                        EntityType = "DeviceInitializationRun",
                        EntityId = run.Id,
                        Message = JsonSerializer.Serialize(new
                        {
                            commandId,
                            run.DeviceMode,
                            run.AdapterName,
                            run.AttemptNo,
                            run.RetryOfRunId,
                            retryReason,
                            run.Status,
                            run.FailureCode,
                            run.Message
                        }, JsonOptions),
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                    await safetyLogWriter.WriteAsync(
                        "device",
                        failed is null ? "Information" : "Error",
                        run.Message,
                        new SafetyLogContext(
                            CorrelationId: commandId,
                            CommandId: commandId,
                            DeviceMode: deviceAdapter.Mode,
                            Actor: actor.Username,
                            Source: nameof(DeviceInitializationService)),
                        cancellationToken: cancellationToken);
                    PublishCompleted(run, commandId);
                    return new CommandExecutionResult<DeviceInitializationResponse>(
                        ToResponse(run, false),
                        "DeviceInitializationRun",
                        run.Id);
                },
                cancellationToken);
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private async Task<DeviceCommandResult> ExecuteStepAsync(
        InitializationStep step,
        DeviceOperationRequest request,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (deviceAdapter.Mode == DeviceModes.Real)
        {
            return await deviceAdapter.InitializeModuleAsync(request, cancellationToken);
        }

        if (step.ModuleCode == DeviceModules.RobotArm)
        {
            var motionResult = await motionControlService.HomeFromDeviceAsync(request, cancellationToken);
            if (!motionResult.Ok)
            {
                return FromMotionResult(request, motionResult, startedAtUtc);
            }

            return await deviceAdapter.InitializeModuleAsync(
                request with { Parameters = Merge(request.Parameters, motionResult.Data, new Dictionary<string, object?> { ["motionStateValidated"] = true }) },
                cancellationToken);
        }

        if (step.ModuleCode == DeviceModules.NeedleWash)
        {
            var motionResult = await motionControlService.WashNeedlesFromDeviceAsync(request, cancellationToken);
            if (!motionResult.Ok)
            {
                return FromMotionResult(request, motionResult, startedAtUtc);
            }

            return await deviceAdapter.InitializeModuleAsync(
                request with { Parameters = Merge(request.Parameters, motionResult.Data, new Dictionary<string, object?> { ["motionStateValidated"] = true }) },
                cancellationToken);
        }

        if (step.ModuleCode == DeviceModules.Cooling)
        {
            var thermalResult = await thermalControlService.InitializeModuleAsync(DeviceModules.Cooling, cancellationToken);
            var data = Merge(thermalResult.Data, MockCoolingDefaults());
            if (!thermalResult.Ok)
            {
                return FromThermalResult(request, thermalResult with { Data = data }, startedAtUtc);
            }

            return await deviceAdapter.InitializeModuleAsync(
                request with { Parameters = Merge(request.Parameters, data, new Dictionary<string, object?> { ["thermalStateValidated"] = true }) },
                cancellationToken);
        }

        if (step.ModuleCode == DeviceModules.LiquidLevel && step.Action == "read-sensors")
        {
            var fluidicsResult = await fluidicsControlService.ReadLiquidLevelsFromDeviceAsync(request, cancellationToken);
            if (!fluidicsResult.Ok)
            {
                return FromFluidicsResult(request, fluidicsResult, startedAtUtc);
            }

            return await deviceAdapter.InitializeModuleAsync(
                request with { Parameters = Merge(request.Parameters, fluidicsResult.Data, new Dictionary<string, object?> { ["fluidicsStateValidated"] = true }) },
                cancellationToken);
        }

        if (step.ModuleCode == DeviceModules.LiquidLevel)
        {
            return await ExecuteLiquidAvailabilityCheckAsync(step, request, startedAtUtc, cancellationToken);
        }

        return await deviceAdapter.InitializeModuleAsync(request, cancellationToken);
    }

    private async Task AppendReadinessFailuresAsync(
        DeviceInitializationRun run,
        string commandId,
        CancellationToken cancellationToken)
    {
        if (deviceAdapter.Mode != DeviceModes.Mock)
        {
            return;
        }

        var thermal = await thermalControlService.GetReadinessAsync(cancellationToken);
        if (!thermal.Ok)
        {
            AppendSupplementalFailure(
                run,
                commandId,
                ModuleForThermalReadiness(thermal),
                thermal.ErrorCode ?? "thermal_not_ready",
                thermal.Message);
        }

        var fluidics = await fluidicsControlService.GetReadinessAsync(cancellationToken);
        if (!fluidics.Ok)
        {
            AppendSupplementalFailure(
                run,
                commandId,
                ModuleForFluidicsReadiness(fluidics),
                fluidics.ErrorCode ?? "fluidics_not_ready",
                fluidics.Message);
        }
    }

    private void AppendSupplementalFailure(
        DeviceInitializationRun run,
        string commandId,
        string moduleCode,
        string errorCode,
        string message)
    {
        if (run.Checks.Any(x =>
                string.Equals(x.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase)
                && x.Status != DeviceInitializationCheckStatus.Succeeded))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var check = new DeviceInitializationCheck
        {
            DeviceInitializationRun = run,
            DeviceInitializationRunId = run.Id,
            StepNo = run.Checks.Count == 0 ? 1 : run.Checks.Max(x => x.StepNo) + 1,
            ModuleCode = moduleCode,
            Status = DeviceInitializationCheckStatus.Failed,
            ErrorCode = errorCode,
            Message = message,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            ResultJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["startupReadinessGate"] = true,
                ["ready"] = false,
                ["errorCode"] = errorCode,
                ["message"] = message
            }, JsonOptions)
        };
        run.Checks.Add(check);
        PublishProgress(run, check, commandId);
    }

    private static string ModuleForThermalReadiness(ThermalReadinessResult result) =>
        string.Equals(result.ErrorCode, "cooling_not_ready", StringComparison.OrdinalIgnoreCase)
            ? DeviceModules.Cooling
            : DeviceModules.Temperature;

    private static string ModuleForFluidicsReadiness(FluidicsReadinessResult result) =>
        result.ErrorCode switch
        {
            "pump_not_ready" => DeviceModules.Pump,
            "mixer_not_ready" => DeviceModules.Mixer,
            _ => DeviceModules.LiquidLevel
        };

    private async Task<DeviceCommandResult> ExecuteLiquidAvailabilityCheckAsync(
        InitializationStep step,
        DeviceOperationRequest request,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        var state = await fluidicsControlService.GetStateAsync(cancellationToken);
        var (sourceType, resultKey, ok, errorCode, message) = step.Action switch
        {
            "check-system-water" => CheckSourceAvailable(state, LiquidSourceTypes.SystemWater, "systemWaterAvailable", "pure_water_unavailable", "System water is unavailable."),
            "check-pbs" => CheckSourceAvailable(state, LiquidSourceTypes.Pbs, "pbsAvailable", "pbs_unavailable", "PBS is unavailable."),
            "check-waste-not-full" => CheckWasteNotFull(state, LiquidSourceTypes.Waste, "wasteNotFull", "waste_full", "Waste tank is full."),
            "check-toxic-waste-not-full" => CheckWasteNotFull(state, LiquidSourceTypes.ToxicWaste, "toxicWasteNotFull", "toxic_waste_full", "Toxic waste tank is full."),
            _ => (string.Empty, "ok", false, "liquid_check_invalid", "Unsupported liquid availability check.")
        };
        var data = new Dictionary<string, object?>
        {
            ["sourceType"] = sourceType,
            [resultKey] = ok,
            ["ioStateNormal"] = ok,
            ["sensorInputsNormal"] = ok,
            ["containers"] = state.LiquidLevels.Select(x => new Dictionary<string, object?>
            {
                ["sourceType"] = x.SourceType,
                ["levelStatus"] = x.LevelStatus,
                ["isConnected"] = x.IsConnected,
                ["faultCode"] = x.FaultCode,
                ["currentVolumeUl"] = x.CurrentVolumeUl,
                ["capacityUl"] = x.CapacityUl
            }).ToList()
        };

        if (!ok)
        {
            return new DeviceCommandResult(
                false,
                DeviceCommandStatuses.Failed,
                request.ModuleCode,
                request.Action,
                errorCode,
                message,
                startedAtUtc,
                DateTimeOffset.UtcNow,
                true,
                data);
        }

        return new DeviceCommandResult(
            true,
            DeviceCommandStatuses.Succeeded,
            request.ModuleCode,
            request.Action,
            null,
            $"{sourceType} availability check passed.",
            startedAtUtc,
            DateTimeOffset.UtcNow,
            true,
            Merge(request.Parameters, data, new Dictionary<string, object?> { ["fluidicsStateValidated"] = true }));
    }

    private static (string SourceType, string ResultKey, bool Ok, string ErrorCode, string Message) CheckSourceAvailable(
        FluidicsStateResponse state,
        string sourceType,
        string resultKey,
        string errorCode,
        string message)
    {
        var source = state.LiquidLevels.SingleOrDefault(x => string.Equals(x.SourceType, sourceType, StringComparison.OrdinalIgnoreCase));
        var ok = source is not null
            && source.IsConnected
            && source.FaultCode is null
            && source.LevelStatus == LiquidLevelStatuses.Normal;
        return (sourceType, resultKey, ok, errorCode, source is null ? $"{sourceType} level source was not found." : message);
    }

    private static (string SourceType, string ResultKey, bool Ok, string ErrorCode, string Message) CheckWasteNotFull(
        FluidicsStateResponse state,
        string sourceType,
        string resultKey,
        string errorCode,
        string message)
    {
        var source = state.LiquidLevels.SingleOrDefault(x => string.Equals(x.SourceType, sourceType, StringComparison.OrdinalIgnoreCase));
        var ok = source is not null
            && source.IsConnected
            && source.FaultCode is null
            && source.LevelStatus != LiquidLevelStatuses.Full
            && source.LevelStatus != LiquidLevelStatuses.SensorFault
            && source.LevelStatus != LiquidLevelStatuses.Disconnected;
        return (sourceType, resultKey, ok, errorCode, source is null ? $"{sourceType} level source was not found." : message);
    }

    private static DeviceCommandResult FromThermalResult(DeviceOperationRequest request, ThermalDeviceResult result, DateTimeOffset startedAtUtc) =>
        new(
            result.Ok,
            result.Status,
            request.ModuleCode,
            request.Action,
            result.ErrorCode,
            result.Message,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            result.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
            result.Data);

    private static DeviceCommandResult FromFluidicsResult(DeviceOperationRequest request, FluidicsDeviceResult result, DateTimeOffset startedAtUtc) =>
        new(
            result.Ok,
            result.Status,
            request.ModuleCode,
            request.Action,
            result.ErrorCode,
            result.Message,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            result.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
            result.Data);

    private static DeviceCommandResult FromMotionResult(DeviceOperationRequest request, MotionDeviceResult result, DateTimeOffset startedAtUtc) =>
        new(
            result.Ok,
            result.Status,
            request.ModuleCode,
            request.Action,
            result.ErrorCode,
            result.Message,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            result.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
            result.Data);

    private static DeviceCommandResult ExceptionResult(DeviceOperationRequest request, DateTimeOffset startedAtUtc, Exception exception)
    {
        var code = exception is BusinessRuleException businessRule ? businessRule.Code : "device_initialization_step_exception";
        return new DeviceCommandResult(
            false,
            DeviceCommandStatuses.Failed,
            request.ModuleCode,
            request.Action,
            code,
            exception.Message,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            true,
            new Dictionary<string, object?>
            {
                ["exceptionType"] = exception.GetType().Name
            });
    }

    private static IReadOnlyDictionary<string, object?> CreateStepParameters(InitializationStep step)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["startupLifecycleStep"] = true,
            ["requiredInterface"] = step.Interface,
            ["documentSource"] = step.DocumentSource,
            ["pageReference"] = step.PageReference
        };

        if (step.ModuleCode == DeviceModules.SampleScanner || step.ModuleCode == DeviceModules.ReagentScanner)
        {
            parameters["online"] = true;
            parameters["communicationOk"] = true;
        }

        if (step.ModuleCode == DeviceModules.LiquidLevel)
        {
            parameters["ioStateNormal"] = true;
            parameters["sensorInputsNormal"] = true;
        }

        return parameters;
    }

    private static IReadOnlyDictionary<string, object?> MockCoolingDefaults() =>
        new Dictionary<string, object?>
        {
            ["mockFixedTemperatureC"] = 5,
            ["currentTemperatureDeciC"] = 50,
            ["currentTemperatureC"] = 5,
            // 真实制冷路径已统一为主控 0x03（连接 0x03/0x01，温度 0x03/0x02~0x03，开关 0x03/0x05~0x06）。
            // 此处仅保留 Mock 默认值；Real 模式由 ThermalControlService 通过主控回读真实状态。
            ["coolingSerialCommand"] = "主控 0x03：连接 0x03/0x01，温度 0x03/0x02、0x03/0x03，开关 0x03/0x05、0x03/0x06"
        };

    private static IReadOnlyDictionary<string, object?> Merge(params IReadOnlyDictionary<string, object?>[] dictionaries)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var dictionary in dictionaries)
        {
            foreach (var pair in dictionary)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static string MapCheckStatus(string adapterStatus)
    {
        return adapterStatus switch
        {
            DeviceCommandStatuses.Succeeded => DeviceInitializationCheckStatus.Succeeded,
            DeviceCommandStatuses.TimedOut => DeviceInitializationCheckStatus.TimedOut,
            DeviceCommandStatuses.Unknown => DeviceInitializationCheckStatus.Unknown,
            _ => DeviceInitializationCheckStatus.Failed
        };
    }

    private static DeviceInitializationResponse ToResponse(DeviceInitializationRun run, bool replayed)
    {
        return new DeviceInitializationResponse(
            run.Status == DeviceInitializationStatus.Ready,
            run.CommandId,
            replayed,
            run.Id,
            run.Status,
            run.DeviceMode,
            run.AdapterName,
            run.AttemptNo,
            run.RetryOfRunId,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.Checks.OrderBy(x => x.StepNo).Select(ToCheckResponse).ToList(),
            run.Message ?? string.Empty);
    }

    private static DeviceInitializationCheckResponse ToCheckResponse(DeviceInitializationCheck check)
    {
        IReadOnlyDictionary<string, object?> result;
        try
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object?>>(check.ResultJson, JsonOptions)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            result = new Dictionary<string, object?>();
        }

        return new DeviceInitializationCheckResponse(
            check.Id,
            check.StepNo,
            check.ModuleCode,
            check.Status,
            check.ErrorCode,
            check.Message,
            check.StartedAtUtc,
            check.CompletedAtUtc,
            result);
    }

    private void PublishProgress(DeviceInitializationRun run, DeviceInitializationCheck check, string commandId)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DeviceInitializationChanged,
            null,
            "DeviceInitializationRun",
            run.Id,
            null,
            new Dictionary<string, object?>
            {
                ["commandId"] = commandId,
                ["runId"] = run.Id,
                ["runStatus"] = run.Status,
                ["moduleCode"] = check.ModuleCode,
                ["checkStatus"] = check.Status,
                ["errorCode"] = check.ErrorCode,
                ["message"] = check.Message
            }));
    }

    private void PublishCompleted(DeviceInitializationRun run, string commandId)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DeviceInitializationChanged,
            null,
            "DeviceInitializationRun",
            run.Id,
            null,
            new Dictionary<string, object?>
            {
                ["commandId"] = commandId,
                ["runId"] = run.Id,
                ["runStatus"] = run.Status,
                ["deviceMode"] = run.DeviceMode,
                ["adapterName"] = run.AdapterName,
                ["attemptNo"] = run.AttemptNo,
                ["message"] = run.Message
            }));
    }

    private sealed record InitializationStep(
        string ModuleCode,
        string Action,
        string Interface,
        string DocumentSource,
        string PageReference);
}
