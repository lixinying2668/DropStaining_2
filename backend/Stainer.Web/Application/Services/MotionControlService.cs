using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.Devices;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MotionControlService(
    StainerDbContext dbContext,
    DeviceModeService deviceModeService,
    IConfiguration configuration,
    IHostEnvironment environment,
    IRobotMotionPrimitives motionPrimitives)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultPipetteAspirateVisibleMs = 2000;
    private const int DefaultPipetteWashVisibleMs = 2000;
    private readonly int pipetteAspirateVisibleMs = ResolveVisibleDelay(
        configuration,
        environment,
        "Motion:PipetteAspirateVisibleMilliseconds",
        DefaultPipetteAspirateVisibleMs);
    private readonly int pipetteWashVisibleMs = ResolveVisibleDelay(
        configuration,
        environment,
        "Motion:PipetteWashVisibleMilliseconds",
        DefaultPipetteWashVisibleMs);
    private const long MinXUm = -100_000;
    private const long MaxXUm = 600_000;
    private const long MinYUm = -100_000;
    private const long MaxYUm = 600_000;
    private const long MinZUm = -10_000;
    private const long MaxZUm = 200_000;

    public async Task<MotionDeviceResult> EnsureTravelSafeZAsync(RobotTravelSafeZRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await motionPrimitives.MoveZAsync(RobotZAxis.Z1, request.SafeZUm, cancellationToken);
            await motionPrimitives.MoveZAsync(RobotZAxis.Z2, request.SafeZUm, cancellationToken);
            return MotionDeviceResult.Succeeded("Robot Z axes moved to travel-safe height.", new Dictionary<string, object?>
            {
                ["safeZUm"] = request.SafeZUm
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MotionDeviceResult.Failed(
                "motion_travel_safe_z_failed",
                exception.Message,
                DeviceCommandStatuses.Failed,
                new Dictionary<string, object?> { ["safeZUm"] = request.SafeZUm });
        }
    }

    public async Task<MotionDeviceResult> MoveToXYAtSafeHeightAsync(RobotMoveToXYAtSafeHeightRequest request, CancellationToken cancellationToken = default)
    {
        var safeZ = await EnsureTravelSafeZAsync(new RobotTravelSafeZRequest(request.SafeZUm), cancellationToken);
        if (!safeZ.Ok)
        {
            return safeZ;
        }

        try
        {
            await motionPrimitives.MoveXYAsync(request.TargetXUm, request.TargetYUm, cancellationToken);
            return MotionDeviceResult.Succeeded("Robot moved to absolute XY at travel-safe height.", new Dictionary<string, object?>
            {
                ["pointCode"] = request.PointCode,
                ["targetXUm"] = request.TargetXUm,
                ["targetYUm"] = request.TargetYUm,
                ["safeZUm"] = request.SafeZUm
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MotionDeviceResult.Failed(
                "motion_move_xy_failed",
                exception.Message,
                DeviceCommandStatuses.Failed,
                new Dictionary<string, object?>
                {
                    ["pointCode"] = request.PointCode,
                    ["targetXUm"] = request.TargetXUm,
                    ["targetYUm"] = request.TargetYUm,
                    ["safeZUm"] = request.SafeZUm
                });
        }
    }

    public async Task<MotionDeviceResult> InitializeModuleAsync(string moduleCode, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            if (moduleCode == DeviceModules.RobotArm)
            {
                var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
                arm.IsHomed = true;
                arm.IsConnected = true;
                arm.Status = MotionStatuses.Idle;
                arm.LastErrorCode = null;
                arm.LastErrorMessage = null;
                arm.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return MotionDeviceResult.Succeeded("Robot arm is homed.", ArmData(arm));
            }

            if (moduleCode is DeviceModules.Needles or DeviceModules.Pipette or DeviceModules.NeedleWash)
            {
                var needles = await dbContext.NeedleStates.OrderBy(x => x.NeedleNo).ToListAsync(cancellationToken);
                foreach (var needle in needles)
                {
                    if (!needle.IsConnected || needle.Status is MotionStatuses.Unknown or MotionStatuses.TimedOut or MotionStatuses.Disconnected or MotionStatuses.Faulted)
                    {
                        return MotionDeviceResult.Failed(needle.LastErrorCode ?? "needle_not_ready", $"Needle {needle.NeedleCode} is not ready.", MapStatusToDeviceStatus(needle.Status), NeedlesData(needles));
                    }
                }

                return MotionDeviceResult.Succeeded("Needles and pipette are ready.", NeedlesData(needles));
            }

            return MotionDeviceResult.Failed("motion_module_invalid", "Unsupported motion module.", DeviceCommandStatuses.NotSupported);
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

    public async Task<MotionReadinessResult> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var arm = await dbContext.RobotArmStates.AsNoTracking().SingleAsync(cancellationToken);
            if (!CanArmBeReady(arm))
            {
                return new MotionReadinessResult(false, arm.LastErrorCode ?? "robot_arm_not_ready", $"Robot arm is {arm.Status}; homed={arm.IsHomed}.");
            }

            var needles = await dbContext.NeedleStates.AsNoTracking().OrderBy(x => x.NeedleNo).ToListAsync(cancellationToken);
            var needle = needles.FirstOrDefault(x => !CanNeedleBeReady(x));
            if (needle is not null)
            {
                return new MotionReadinessResult(false, needle.LastErrorCode ?? "needle_not_ready", $"Needle {needle.NeedleCode} is {needle.Status}; wash={needle.NeedsWash}; volume={needle.VolumeUl}.");
            }

            return new MotionReadinessResult(true, null, "Robot arm and needles are ready.");
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<MotionModuleState>> GetDeviceModuleStatesAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var arm = await dbContext.RobotArmStates.AsNoTracking().SingleAsync(cancellationToken);
            var needles = await dbContext.NeedleStates.AsNoTracking().OrderBy(x => x.NeedleNo).ToListAsync(cancellationToken);
            var needlesReady = needles.All(CanNeedleBeReady);
            return
            [
                BuildModuleState(DeviceModules.RobotArm, CanArmBeReady(arm), !arm.IsConnected, arm.LastErrorCode, arm.LastErrorMessage, ArmData(arm)),
                BuildModuleState(DeviceModules.Needles, needlesReady, needles.Any(x => !x.IsConnected), needles.FirstOrDefault(x => !CanNeedleBeReady(x))?.LastErrorCode, needles.FirstOrDefault(x => !CanNeedleBeReady(x))?.LastErrorMessage, NeedlesData(needles)),
                BuildModuleState(DeviceModules.Pipette, needlesReady, needles.Any(x => !x.IsConnected), needles.FirstOrDefault(x => !CanNeedleBeReady(x))?.LastErrorCode, needles.FirstOrDefault(x => !CanNeedleBeReady(x))?.LastErrorMessage, NeedlesData(needles)),
                BuildModuleState(DeviceModules.NeedleWash, needles.All(x => x.IsConnected && x.LastErrorCode is null), needles.Any(x => !x.IsConnected), needles.FirstOrDefault(x => x.LastErrorCode is not null)?.LastErrorCode, needles.FirstOrDefault(x => x.LastErrorCode is not null)?.LastErrorMessage, NeedlesData(needles))
            ];
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MotionDeviceResult> HomeFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureMockMode();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
            arm.IsHomed = true;
            arm.IsConnected = true;
            arm.Status = MotionStatuses.Idle;
            arm.CurrentTargetPointCode = "Home";
            arm.CurrentXUm = 0;
            arm.CurrentYUm = 0;
            arm.CurrentZUm = 0;
            ApplyContext(arm, request);
            AddOperation(PipettingOperationTypes.Home, DeviceCommandStatus.Completed, request, null, PipettingExecutionModes.Single, "Home", null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0);
            await dbContext.SaveChangesAsync(cancellationToken);
            return MotionDeviceResult.Succeeded("Robot arm homed.", ArmData(arm));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MotionDeviceResult> MoveFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureMockMode();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var targetCode = RequireParam(request, "targetPointCode");
            var snapshot = ParseCoordinateSnapshot(request);
            var target = RequireFrozenTarget(snapshot, targetCode);
            var softLimit = ValidateSoftLimits(target);
            if (!softLimit.Ok)
            {
                await RecordArmFaultAsync(request, softLimit.ErrorCode!, softLimit.Message, MotionStatuses.Faulted, cancellationToken);
                return MotionDeviceResult.Failed(softLimit.ErrorCode!, softLimit.Message, DeviceCommandStatuses.Failed, new Dictionary<string, object?> { ["targetPointCode"] = targetCode });
            }

            var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
            if (!CanArmOperate(arm))
            {
                return MotionDeviceResult.Failed(arm.LastErrorCode ?? "robot_arm_not_ready", arm.LastErrorMessage ?? "Robot arm is not ready.", MapStatusToDeviceStatus(arm.Status), ArmData(arm));
            }

            arm.Status = MotionStatuses.Idle;
            arm.CurrentTargetPointCode = target.PointCode;
            arm.CurrentXUm = target.CalibratedXUm;
            arm.CurrentYUm = target.CalibratedYUm;
            arm.CurrentZUm = target.SafeZUm ?? target.CalibratedZUm;
            arm.CoordinateProfileVersionId = snapshot.CoordinateProfileVersionId;
            arm.LastErrorCode = null;
            arm.LastErrorMessage = null;
            arm.IsConnected = true;
            ApplyContext(arm, request);
            AddOperation(PipettingOperationTypes.Move, DeviceCommandStatus.Completed, request, null, PipettingExecutionModes.Single, target.PointCode, null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0);
            await dbContext.SaveChangesAsync(cancellationToken);
            return MotionDeviceResult.Succeeded("Robot arm moved.", ArmData(arm));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MotionDeviceResult> PipetteFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureMockMode();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var snapshot = ParseCoordinateSnapshot(request);
            var reagentCode = Convert.ToString(request.Parameters.GetValueOrDefault("reagentCode"))?.Trim();
            var volume = Convert.ToInt32(request.Parameters.GetValueOrDefault("adjustedVolumeUl") ?? request.Parameters.GetValueOrDefault("volumeUl") ?? 0);
            if (string.IsNullOrWhiteSpace(reagentCode) || volume <= 0)
            {
                return MotionDeviceResult.Failed("pipette_request_invalid", "Pipette command requires reagentCode and positive volume.", DeviceCommandStatuses.Failed);
            }

            var targetPoint = Convert.ToString(request.Parameters.GetValueOrDefault("targetPointCode"));
            if (string.IsNullOrWhiteSpace(targetPoint))
            {
                targetPoint = Convert.ToString(request.Parameters.GetValueOrDefault("slotCode"));
            }

            if (string.IsNullOrWhiteSpace(targetPoint))
            {
                var drawer = Convert.ToString(request.Parameters.GetValueOrDefault("drawerCode"));
                var slotNo = Convert.ToInt32(request.Parameters.GetValueOrDefault("slotNo") ?? 0);
                targetPoint = !string.IsNullOrWhiteSpace(drawer) && slotNo > 0 ? $"{drawer!.Trim().ToUpperInvariant()}-{slotNo:00}" : null;
            }

            var target = RequireFrozenTarget(snapshot, targetPoint);
            var targetLimit = ValidateSoftLimits(target);
            if (!targetLimit.Ok)
            {
                await RecordArmFaultAsync(request, targetLimit.ErrorCode!, targetLimit.Message, MotionStatuses.Faulted, cancellationToken);
                return MotionDeviceResult.Failed(targetLimit.ErrorCode!, targetLimit.Message, DeviceCommandStatuses.Failed);
            }

            var needle = await SelectNeedleAsync(request, reagentCode, cancellationToken);
            var source = await ResolveSourceAsync(reagentCode, volume, request, cancellationToken);
            var sourcePoint = RequireFrozenTarget(snapshot, source.SourcePositionCode);
            var sourceLimit = ValidateSoftLimits(sourcePoint);
            if (!sourceLimit.Ok)
            {
                await RecordArmFaultAsync(request, sourceLimit.ErrorCode!, sourceLimit.Message, MotionStatuses.Faulted, cancellationToken);
                return MotionDeviceResult.Failed(sourceLimit.ErrorCode!, sourceLimit.Message, DeviceCommandStatuses.Failed);
            }

            var allowAutoWash = Convert.ToBoolean(request.Parameters.GetValueOrDefault("allowAutomaticWash") ?? false);
            if (!CanNeedleLoad(needle, source.SourceType, reagentCode, source.SourceId, allowAutoWash, out var blockMessage))
            {
                MarkNeedleFault(needle, "needle_reagent_switch_requires_wash", blockMessage, request, MotionStatuses.Faulted);
                AddOperation(PipettingOperationTypes.Aspirate, DeviceCommandStatus.Failed, request, needle.NeedleCode, PipettingExecutionModes.Single, target.PointCode, null, source.SourceType, reagentCode, source.ReagentBottleId, source.DabBatchId, source.SystemLiquidSourceType, source.SourcePositionCode, volume, "needle_reagent_switch_requires_wash", blockMessage);
                await dbContext.SaveChangesAsync(cancellationToken);
                return MotionDeviceResult.Failed("needle_reagent_switch_requires_wash", blockMessage, DeviceCommandStatuses.Failed, NeedleData(needle));
            }

            if (NeedsAutoWash(needle, source.SourceType, reagentCode, source.SourceId))
            {
                ClearNeedleLoad(needle);
                ApplyContext(needle, request);
                AddOperation(PipettingOperationTypes.WashNeedle, DeviceCommandStatus.Completed, request, needle.NeedleCode, PipettingExecutionModes.Single, "NeedleWash", null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0);
            }

            var executionMode = ResolveExecutionMode(snapshot, request, target.PointCode);
            var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
            if (!CanArmOperate(arm))
            {
                return MotionDeviceResult.Failed(arm.LastErrorCode ?? "robot_arm_not_ready", arm.LastErrorMessage ?? "Robot arm is not ready.", MapStatusToDeviceStatus(arm.Status), ArmData(arm));
            }

            // 洗针：每个加液步吸液前，先把机械臂移到洗针位并停留，让"排完液体→洗针→再吸液"过程可见。
            // 试剂位仍由 ResolveSourceAsync 解析（source.SourcePositionCode，与该步试剂一一对应），这里只增加洗针移动。
            var washPoint = FindWashStationPoint(snapshot);
            if (washPoint is not null)
            {
                arm.Status = MotionStatuses.Idle;
                arm.CurrentTargetPointCode = washPoint.PointCode;
                arm.CurrentXUm = washPoint.CalibratedXUm;
                arm.CurrentYUm = washPoint.CalibratedYUm;
                arm.CurrentZUm = washPoint.SafeZUm ?? washPoint.CalibratedZUm;
                ApplyContext(arm, request);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (pipetteWashVisibleMs > 0)
                {
                    await Task.Delay(pipetteWashVisibleMs, cancellationToken);
                }
            }

            // 吸液阶段：先把机械臂移到「该步骤对应试剂」源位并持久化一次，使"在试剂位吸液"成为可观测的中间状态（而非直接跳到玻片）。
            arm.Status = MotionStatuses.Idle;
            arm.CurrentTargetPointCode = source.SourcePositionCode;
            arm.CurrentXUm = sourcePoint.CalibratedXUm;
            arm.CurrentYUm = sourcePoint.CalibratedYUm;
            arm.CurrentZUm = sourcePoint.SafeZUm ?? sourcePoint.CalibratedZUm;
            arm.CoordinateProfileVersionId = snapshot.CoordinateProfileVersionId;
            ApplyContext(arm, request);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (pipetteAspirateVisibleMs > 0)
            {
                await Task.Delay(pipetteAspirateVisibleMs, cancellationToken); // 让“在试剂源位吸液”过程可见（不是一闪而过）
            }

            var secondaryTarget = Convert.ToString(request.Parameters.GetValueOrDefault("secondaryTargetPointCode"));
            AddOperation(PipettingOperationTypes.LiquidDetect, DeviceCommandStatus.Completed, request, needle.NeedleCode, executionMode, source.SourcePositionCode, null, source.SourceType, reagentCode, source.ReagentBottleId, source.DabBatchId, source.SystemLiquidSourceType, source.SourcePositionCode, volume);
            AddOperation(PipettingOperationTypes.Aspirate, DeviceCommandStatus.Completed, request, needle.NeedleCode, executionMode, source.SourcePositionCode, null, source.SourceType, reagentCode, source.ReagentBottleId, source.DabBatchId, source.SystemLiquidSourceType, source.SourcePositionCode, volume);
            ApplyNeedleLoadedThenDispensed(needle, source, reagentCode, volume, request);
            arm.IsConnected = true;
            arm.IsHomed = true;
            arm.Status = MotionStatuses.Idle;
            arm.CurrentTargetPointCode = target.PointCode;
            arm.CurrentXUm = target.CalibratedXUm;
            arm.CurrentYUm = target.CalibratedYUm;
            arm.CurrentZUm = target.DispenseZUm ?? target.CalibratedZUm ?? target.SafeZUm;
            arm.CoordinateProfileVersionId = snapshot.CoordinateProfileVersionId;
            arm.LastErrorCode = null;
            arm.LastErrorMessage = null;
            ApplyContext(arm, request);
            AddOperation(PipettingOperationTypes.Dispense, DeviceCommandStatus.Completed, request, needle.NeedleCode, executionMode, target.PointCode, secondaryTarget, source.SourceType, reagentCode, source.ReagentBottleId, source.DabBatchId, source.SystemLiquidSourceType, source.SourcePositionCode, volume);
            AddOperation(PipettingOperationTypes.Blowout, DeviceCommandStatus.Completed, request, needle.NeedleCode, executionMode, target.PointCode, secondaryTarget, source.SourceType, reagentCode, source.ReagentBottleId, source.DabBatchId, source.SystemLiquidSourceType, source.SourcePositionCode, volume);
            await dbContext.SaveChangesAsync(cancellationToken);
            var data = NeedleData(needle);
            data["executionMode"] = executionMode;
            data["sourcePositionCode"] = source.SourcePositionCode;
            data["targetPointCode"] = target.PointCode;
            data["coordinateProfileVersionId"] = snapshot.CoordinateProfileVersionId;
            return MotionDeviceResult.Succeeded("Pipette command completed.", data);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MotionDeviceResult> WashNeedlesFromDeviceAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureMockMode();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var requestedNeedle = NormalizeNeedleCode(Convert.ToString(request.Parameters.GetValueOrDefault("needleCode")), allowNull: true);
            var needles = await dbContext.NeedleStates.OrderBy(x => x.NeedleNo).ToListAsync(cancellationToken);
            foreach (var needle in needles.Where(x => requestedNeedle is null || x.NeedleCode == requestedNeedle))
            {
                ClearNeedleLoad(needle);
                ApplyContext(needle, request);
                AddOperation(PipettingOperationTypes.WashNeedle, DeviceCommandStatus.Completed, request, needle.NeedleCode, PipettingExecutionModes.Single, "NeedleWash", null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return MotionDeviceResult.Succeeded("Needle wash completed.", NeedlesData(needles));
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
        string? needleCode,
        string? machineRunId,
        string? workflowStepExecutionId,
        string? deviceCommandExecutionId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            var status = StatusForDeviceFailure(deviceStatus, errorCode);
            var finalErrorCode = string.IsNullOrWhiteSpace(errorCode) ? status : errorCode.Trim();
            if (moduleCode == DeviceModules.RobotArm)
            {
                var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
                arm.Status = status;
                arm.IsConnected = status != MotionStatuses.Disconnected;
                arm.LastErrorCode = finalErrorCode;
                arm.LastErrorMessage = message;
                arm.MachineRunId = machineRunId;
                arm.WorkflowStepExecutionId = workflowStepExecutionId;
                arm.DeviceCommandExecutionId = deviceCommandExecutionId;
                arm.CurrentCommandId = deviceCommandExecutionId;
                arm.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                var normalizedNeedle = NormalizeNeedleCode(needleCode, allowNull: true);
                var needles = await dbContext.NeedleStates.Where(x => normalizedNeedle == null || x.NeedleCode == normalizedNeedle).ToListAsync(cancellationToken);
                foreach (var needle in needles)
                {
                    needle.Status = status;
                    needle.IsConnected = status != MotionStatuses.Disconnected;
                    needle.LastErrorCode = finalErrorCode;
                    needle.LastErrorMessage = message;
                    needle.MachineRunId = machineRunId;
                    needle.WorkflowStepExecutionId = workflowStepExecutionId;
                    needle.DeviceCommandExecutionId = deviceCommandExecutionId;
                    needle.CurrentCommandId = deviceCommandExecutionId;
                    needle.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
            }

            AddOperation(PipettingOperationTypes.Dispense, deviceStatus is DeviceCommandStatuses.Unknown or DeviceCommandStatuses.TimedOut ? DeviceCommandStatus.Unknown : DeviceCommandStatus.Failed, null, needleCode, PipettingExecutionModes.Single, null, null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0, finalErrorCode, message, machineRunId, workflowStepExecutionId, deviceCommandExecutionId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RecordDabDispenseFromExecutorAsync(
        DabBatch batch,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        int volumeUl,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureSeededCoreAsync(cancellationToken);
            if (await dbContext.PipettingOperations.AnyAsync(x => x.DeviceCommandExecutionId == command.Id && x.SourceType == NeedleLoadSourceTypes.DabBatch, cancellationToken))
            {
                return;
            }

            var needle = await SelectNeedleAsync(step, "DAB", cancellationToken);
            if (NeedsAutoWash(needle, NeedleLoadSourceTypes.DabBatch, "DAB", batch.Id))
            {
                ClearNeedleLoad(needle);
                AddOperation(PipettingOperationTypes.WashNeedle, DeviceCommandStatus.Completed, null, needle.NeedleCode, PipettingExecutionModes.Single, "NeedleWash", null, NeedleLoadSourceTypes.Empty, null, null, null, null, null, 0, null, null, run.Id, step.Id, command.Id, command.LiquidClassVersionId, command.LiquidClassVersionNo, command.LiquidClassParametersJson);
            }

            ApplyNeedleLoadedThenDispensed(
                needle,
                new MotionSource(NeedleLoadSourceTypes.DabBatch, "DAB", null, batch.Id, null, batch.PositionCode, batch.PositionCode),
                "DAB",
                volumeUl,
                new DeviceOperationRequest(new DeviceCommandContext(command.Id, command.Id, "system", nameof(MachineExecutor)), DeviceModules.Pipette, "DabDispense", new Dictionary<string, object?>
                {
                    ["machineRunId"] = run.Id,
                    ["workflowStepExecutionId"] = step.Id,
                    ["liquidClassVersionId"] = command.LiquidClassVersionId,
                    ["liquidClassVersionNo"] = command.LiquidClassVersionNo,
                    ["liquidClassParametersJson"] = command.LiquidClassParametersJson
                }));
            AddOperation(PipettingOperationTypes.Dispense, DeviceCommandStatus.Completed, null, needle.NeedleCode, PipettingExecutionModes.Single, step.WorkflowExecution?.SlideTask?.SlotCode, null, NeedleLoadSourceTypes.DabBatch, "DAB", null, batch.Id, null, batch.PositionCode, volumeUl, null, null, run.Id, step.Id, command.Id, command.LiquidClassVersionId, command.LiquidClassVersionNo, command.LiquidClassParametersJson);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task EnsureSeededCoreAsync(CancellationToken cancellationToken)
    {
        if (!await dbContext.RobotArmStates.AnyAsync(cancellationToken))
        {
            dbContext.RobotArmStates.Add(new RobotArmState { IsHomed = false, Status = MotionStatuses.Idle, UpdatedAtUtc = DateTimeOffset.UtcNow });
        }

        if (!await dbContext.NeedleStates.AnyAsync(cancellationToken))
        {
            dbContext.NeedleStates.AddRange(
                new NeedleState { NeedleCode = NeedleCodes.Needle1, NeedleNo = 1, UpdatedAtUtc = DateTimeOffset.UtcNow },
                new NeedleState { NeedleCode = NeedleCodes.Needle2, NeedleNo = 2, UpdatedAtUtc = DateTimeOffset.UtcNow });
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<NeedleState> SelectNeedleAsync(DeviceOperationRequest request, string reagentCode, CancellationToken cancellationToken)
    {
        var requested = NormalizeNeedleCode(Convert.ToString(request.Parameters.GetValueOrDefault("needleCode")), allowNull: true);
        if (requested is not null)
        {
            return await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == requested, cancellationToken);
        }

        var slotNo = Convert.ToInt32(request.Parameters.GetValueOrDefault("slotNo") ?? 1);
        var preferred = slotNo % 2 == 0 ? NeedleCodes.Needle2 : NeedleCodes.Needle1;
        var needles = await dbContext.NeedleStates.OrderBy(x => x.NeedleCode == preferred ? 0 : 1).ThenBy(x => x.NeedleNo).ToListAsync(cancellationToken);
        return needles.FirstOrDefault(x => CanNeedleLoad(x, NeedleLoadSourceTypes.ReagentBottle, reagentCode, null, allowAutoWash: false, out _))
            ?? needles.First();
    }

    private async Task<NeedleState> SelectNeedleAsync(WorkflowStepExecution step, string reagentCode, CancellationToken cancellationToken)
    {
        var slotCode = step.WorkflowExecution?.SlideTask?.SlotCode;
        var slotNo = 1;
        if (!string.IsNullOrWhiteSpace(slotCode) && int.TryParse(slotCode.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(), out var parsed))
        {
            slotNo = parsed;
        }

        var preferred = slotNo % 2 == 0 ? NeedleCodes.Needle2 : NeedleCodes.Needle1;
        var needles = await dbContext.NeedleStates.OrderBy(x => x.NeedleCode == preferred ? 0 : 1).ThenBy(x => x.NeedleNo).ToListAsync(cancellationToken);
        return needles.FirstOrDefault(x => CanNeedleLoad(x, NeedleLoadSourceTypes.DabBatch, reagentCode, null, allowAutoWash: true, out _))
            ?? needles.First();
    }

    private async Task<MotionSource> ResolveSourceAsync(string reagentCode, int volumeUl, DeviceOperationRequest request, CancellationToken cancellationToken)
    {
        if (string.Equals(reagentCode, "DAB", StringComparison.OrdinalIgnoreCase))
        {
            var taskId = Convert.ToString(request.Parameters.GetValueOrDefault("stainingTaskId"));
            var batch = await dbContext.DabBatches
                .Where(x => x.Status == DabBatchStatus.Available && (string.IsNullOrWhiteSpace(taskId) || x.Tasks.Any(t => t.StainingTaskId == taskId)))
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new BusinessRuleException("dab_batch_not_available", "No available DAB batch is assigned.", StatusCodes.Status409Conflict);
            return new MotionSource(NeedleLoadSourceTypes.DabBatch, "DAB", null, batch.Id, null, batch.PositionCode, batch.PositionCode);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var placements = await dbContext.ReagentRackPlacements
            .Include(x => x.ReagentRackPosition)
            .Include(x => x.ReagentBottle)
            .Where(x => x.RemovedAtUtc == null
                && x.ReagentBottle!.ReagentCode == reagentCode
                && x.ReagentBottle.Status == "Available"
                && x.ReagentBottle.ExpirationDate >= today
                && x.ReagentBottle.RemainingVolumeUl > 0)
            .OrderBy(x => x.ReagentRackPosition!.PositionNo)
            .ToListAsync(cancellationToken);
        if (placements.Sum(x => x.ReagentBottle!.RemainingVolumeUl) < volumeUl)
        {
            throw new BusinessRuleException("reagent_insufficient", $"Reagent {reagentCode} is insufficient.", StatusCodes.Status409Conflict);
        }

        var placement = placements.First();
        return new MotionSource(NeedleLoadSourceTypes.ReagentBottle, reagentCode, placement.ReagentBottleId, null, null, placement.ReagentRackPosition!.Code, placement.ReagentRackPosition.Code);
    }

    private static bool CanNeedleLoad(NeedleState needle, string sourceType, string? reagentCode, string? sourceId, bool allowAutoWash, out string message)
    {
        message = string.Empty;
        if (!CanNeedleOperate(needle))
        {
            message = $"Needle {needle.NeedleCode} is not ready.";
            return false;
        }

        if (needle.LoadedSourceType == NeedleLoadSourceTypes.Empty && !needle.NeedsWash)
        {
            return true;
        }

        if (SameNeedleContent(needle, sourceType, reagentCode, sourceId))
        {
            return true;
        }

        if (allowAutoWash && needle.VolumeUl == 0)
        {
            return true;
        }

        message = $"Needle {needle.NeedleCode} must be emptied and washed before switching from {needle.LoadedReagentCode ?? needle.LoadedSourceType} to {reagentCode ?? sourceType}.";
        return false;
    }

    private static bool NeedsAutoWash(NeedleState needle, string sourceType, string? reagentCode, string? sourceId) =>
        needle.VolumeUl == 0
        && needle.NeedsWash
        && !SameNeedleContent(needle, sourceType, reagentCode, sourceId);

    private static bool SameNeedleContent(NeedleState needle, string sourceType, string? reagentCode, string? sourceId)
    {
        if (needle.LoadedSourceType == NeedleLoadSourceTypes.Empty)
        {
            return false;
        }

        if (!string.Equals(needle.LoadedSourceType, sourceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(reagentCode) && string.Equals(needle.LoadedReagentCode, reagentCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return sourceType switch
        {
            NeedleLoadSourceTypes.ReagentBottle => string.Equals(needle.SourceBottleId, sourceId, StringComparison.Ordinal),
            NeedleLoadSourceTypes.DabBatch => string.Equals(needle.DabBatchId, sourceId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static void ApplyNeedleLoadedThenDispensed(NeedleState needle, MotionSource source, string reagentCode, int volumeUl, DeviceOperationRequest request)
    {
        needle.LoadedSourceType = source.SourceType;
        needle.LoadedReagentCode = reagentCode;
        needle.SourceBottleId = source.ReagentBottleId;
        needle.DabBatchId = source.DabBatchId;
        needle.SystemLiquidSourceType = source.SystemLiquidSourceType;
        needle.SourcePositionCode = source.SourcePositionCode;
        needle.VolumeUl = 0;
        needle.LiquidClassVersionId = Convert.ToString(request.Parameters.GetValueOrDefault("liquidClassVersionId"));
        needle.LiquidClassVersionNo = ToNullableInt(request.Parameters.GetValueOrDefault("liquidClassVersionNo"));
        needle.LiquidClassParametersJson = Convert.ToString(request.Parameters.GetValueOrDefault("liquidClassParametersJson")) ?? "{}";
        needle.NeedsWash = true;
        needle.Status = MotionStatuses.Completed;
        needle.IsConnected = true;
        needle.LastErrorCode = null;
        needle.LastErrorMessage = null;
        ApplyContext(needle, request);
    }

    private static void ClearNeedleLoad(NeedleState needle)
    {
        needle.LoadedSourceType = NeedleLoadSourceTypes.Empty;
        needle.LoadedReagentCode = null;
        needle.SourceBottleId = null;
        needle.DabBatchId = null;
        needle.SystemLiquidSourceType = null;
        needle.SourcePositionCode = null;
        needle.VolumeUl = 0;
        needle.LiquidClassVersionId = null;
        needle.LiquidClassVersionNo = null;
        needle.LiquidClassParametersJson = "{}";
        needle.NeedsWash = false;
        needle.Status = MotionStatuses.Idle;
        needle.LastErrorCode = null;
        needle.LastErrorMessage = null;
        needle.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyContext(RobotArmState arm, DeviceOperationRequest request)
    {
        arm.CurrentCommandId = request.Context.CommandId;
        arm.MachineRunId = Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId"));
        arm.WorkflowStepExecutionId = Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId"));
        arm.DeviceCommandExecutionId = request.Context.CommandId;
        arm.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyContext(NeedleState needle, DeviceOperationRequest request)
    {
        needle.CurrentCommandId = request.Context.CommandId;
        needle.MachineRunId = Convert.ToString(request.Parameters.GetValueOrDefault("machineRunId"));
        needle.WorkflowStepExecutionId = Convert.ToString(request.Parameters.GetValueOrDefault("workflowStepExecutionId"));
        needle.DeviceCommandExecutionId = request.Context.CommandId;
        needle.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task RecordArmFaultAsync(DeviceOperationRequest request, string errorCode, string message, string status, CancellationToken cancellationToken)
    {
        var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
        arm.Status = status;
        arm.LastErrorCode = errorCode;
        arm.LastErrorMessage = message;
        ApplyContext(arm, request);
    }

    private static void MarkNeedleFault(NeedleState needle, string errorCode, string message, DeviceOperationRequest request, string status)
    {
        needle.Status = status;
        needle.LastErrorCode = errorCode;
        needle.LastErrorMessage = message;
        ApplyContext(needle, request);
    }

    private void AddOperation(
        string operationType,
        string status,
        DeviceOperationRequest? request,
        string? needleCode,
        string executionMode,
        string? targetPointCode,
        string? secondaryTargetPointCode,
        string sourceType,
        string? reagentCode,
        string? reagentBottleId,
        string? dabBatchId,
        string? systemLiquidSourceType,
        string? sourcePositionCode,
        int volumeUl,
        string? errorCode = null,
        string? errorMessage = null,
        string? machineRunId = null,
        string? workflowStepExecutionId = null,
        string? deviceCommandExecutionId = null,
        string? liquidClassVersionId = null,
        int? liquidClassVersionNo = null,
        string? liquidClassParametersJson = null)
    {
        var parameters = request?.Parameters;
        var operation = new PipettingOperation
        {
            OperationType = operationType,
            Status = status,
            NeedleCode = NormalizeNeedleCode(needleCode, allowNull: true),
            ExecutionMode = executionMode,
            TargetPointCode = targetPointCode,
            SecondaryTargetPointCode = secondaryTargetPointCode,
            CoordinateProfileVersionId = Convert.ToString(parameters?.GetValueOrDefault("coordinateProfileVersionId")),
            LiquidClassVersionId = liquidClassVersionId ?? Convert.ToString(parameters?.GetValueOrDefault("liquidClassVersionId")),
            LiquidClassVersionNo = liquidClassVersionNo ?? ToNullableInt(parameters?.GetValueOrDefault("liquidClassVersionNo")),
            LiquidClassParametersJson = liquidClassParametersJson ?? Convert.ToString(parameters?.GetValueOrDefault("liquidClassParametersJson")) ?? "{}",
            SourceType = sourceType,
            ReagentCode = reagentCode,
            ReagentBottleId = reagentBottleId,
            DabBatchId = dabBatchId,
            SystemLiquidSourceType = systemLiquidSourceType,
            SourcePositionCode = sourcePositionCode,
            VolumeUl = volumeUl,
            MachineRunId = machineRunId ?? Convert.ToString(parameters?.GetValueOrDefault("machineRunId")),
            WorkflowStepExecutionId = workflowStepExecutionId ?? Convert.ToString(parameters?.GetValueOrDefault("workflowStepExecutionId")),
            DeviceCommandExecutionId = deviceCommandExecutionId ?? request?.Context.CommandId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.PipettingOperations.Add(operation);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = $"motion.operation.{operationType.ToLowerInvariant()}",
            EntityType = "PipettingOperation",
            EntityId = operation.Id,
            Message = JsonSerializer.Serialize(new
            {
                operation.Status,
                operation.NeedleCode,
                operation.ExecutionMode,
                operation.SourceType,
                operation.ReagentCode,
                operation.SourcePositionCode,
                operation.TargetPointCode,
                operation.SecondaryTargetPointCode,
                operation.VolumeUl,
                operation.MachineRunId,
                operation.WorkflowStepExecutionId,
                operation.DeviceCommandExecutionId,
                operation.ErrorCode
            }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static CoordinateSnapshot ParseCoordinateSnapshot(DeviceOperationRequest request)
    {
        var json = Convert.ToString(request.Parameters.GetValueOrDefault("coordinateSnapshotJson"));
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            throw new BusinessRuleException("coordinate_snapshot_required", "Motion commands require a frozen coordinate snapshot.", StatusCodes.Status409Conflict);
        }

        try
        {
            return JsonSerializer.Deserialize<CoordinateSnapshot>(json, JsonOptions)
                ?? throw new BusinessRuleException("coordinate_snapshot_invalid", "Frozen coordinate snapshot could not be parsed.", StatusCodes.Status409Conflict);
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("coordinate_snapshot_invalid", "Frozen coordinate snapshot could not be parsed.", StatusCodes.Status409Conflict);
        }
    }

    private static CoordinateSnapshotPoint? FindWashStationPoint(CoordinateSnapshot snapshot)
    {
        // 优先内壁洗针位；找不到则任意 Wash* 位；再找不到返回 null（调用方跳过洗针移动，不崩）。
        return snapshot.TargetPoints.FirstOrDefault(x => string.Equals(x.PointCode, "WashInnerLeft", StringComparison.OrdinalIgnoreCase))
            ?? snapshot.TargetPoints.FirstOrDefault(x => x.PointCode is not null && x.PointCode.StartsWith("Wash", StringComparison.OrdinalIgnoreCase))
            ?? snapshot.TargetPoints.FirstOrDefault(x => string.Equals(x.PointCode, "NeedleWash", StringComparison.OrdinalIgnoreCase));
    }

    private static CoordinateSnapshotPoint RequireFrozenTarget(CoordinateSnapshot snapshot, string? pointCode)
    {
        if (string.IsNullOrWhiteSpace(snapshot.CoordinateProfileVersionId))
        {
            throw new BusinessRuleException("coordinate_snapshot_invalid", "Frozen coordinate snapshot does not include a version id.", StatusCodes.Status409Conflict);
        }

        var normalized = pointCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("target_point_required", "A target point code is required.", StatusCodes.Status400BadRequest);
        }

        var point = snapshot.TargetPoints.SingleOrDefault(x => string.Equals(x.PointCode, normalized, StringComparison.OrdinalIgnoreCase));
        if (point is null || !point.IsEnabled)
        {
            throw new BusinessRuleException("target_point_not_in_frozen_coordinates", $"Target point {normalized} is not present in the frozen coordinate snapshot.", StatusCodes.Status409Conflict);
        }

        return point;
    }

    private static (bool Ok, string? ErrorCode, string Message) ValidateSoftLimits(CoordinateSnapshotPoint point)
    {
        if (point.CalibratedXUm is { } x && (x < MinXUm || x > MaxXUm)) return (false, "motion_soft_limit_x", $"Target point {point.PointCode} exceeds X soft limit.");
        if (point.CalibratedYUm is { } y && (y < MinYUm || y > MaxYUm)) return (false, "motion_soft_limit_y", $"Target point {point.PointCode} exceeds Y soft limit.");
        var z = point.SafeZUm ?? point.CalibratedZUm ?? point.DispenseZUm ?? point.LiquidDetectZUm;
        if (z is { } value && (value < MinZUm || value > MaxZUm)) return (false, "motion_soft_limit_z", $"Target point {point.PointCode} exceeds Z soft limit.");
        return (true, null, string.Empty);
    }

    private static string ResolveExecutionMode(CoordinateSnapshot snapshot, DeviceOperationRequest request, string primaryTarget)
    {
        var useBoth = Convert.ToBoolean(request.Parameters.GetValueOrDefault("useBothNeedles") ?? false)
            || request.Action.Contains("dual", StringComparison.OrdinalIgnoreCase);
        if (!useBoth)
        {
            return PipettingExecutionModes.Single;
        }

        var secondary = Convert.ToString(request.Parameters.GetValueOrDefault("secondaryTargetPointCode"));
        if (string.IsNullOrWhiteSpace(secondary))
        {
            return PipettingExecutionModes.Sequential;
        }

        _ = RequireFrozenTarget(snapshot, primaryTarget);
        _ = RequireFrozenTarget(snapshot, secondary);
        return PipettingExecutionModes.Sequential;
    }

    private static int ResolveVisibleDelay(IConfiguration configuration, IHostEnvironment environment, string key, int defaultValue)
    {
        var configured = configuration[key];
        if (int.TryParse(configured, out var value))
        {
            return Math.Max(0, value);
        }

        return environment.IsEnvironment("Testing") ? 0 : defaultValue;
    }

    private static MotionModuleState BuildModuleState(string moduleCode, bool ready, bool disconnected, string? errorCode, string? errorMessage, object data) => new(
        moduleCode,
        ready ? DeviceConnectionStatuses.Connected : disconnected ? DeviceConnectionStatuses.Disconnected : DeviceConnectionStatuses.Faulted,
        ready ? "Idle" : "AttentionRequired",
        JsonSerializer.Serialize(data, JsonOptions),
        errorCode,
        errorMessage);

    private static Dictionary<string, object?> ArmData(RobotArmState arm) => new()
    {
        ["isHomed"] = arm.IsHomed,
        ["isConnected"] = arm.IsConnected,
        ["status"] = arm.Status,
        ["currentTargetPointCode"] = arm.CurrentTargetPointCode,
        ["currentXUm"] = arm.CurrentXUm,
        ["currentYUm"] = arm.CurrentYUm,
        ["currentZUm"] = arm.CurrentZUm,
        ["coordinateProfileVersionId"] = arm.CoordinateProfileVersionId,
        ["lastErrorCode"] = arm.LastErrorCode
    };

    private static Dictionary<string, object?> NeedlesData(IEnumerable<NeedleState> needles) => new()
    {
        ["needles"] = needles.Select(NeedleData).ToList()
    };

    private static Dictionary<string, object?> NeedleData(NeedleState needle) => new()
    {
        ["needleCode"] = needle.NeedleCode,
        ["needleNo"] = needle.NeedleNo,
        ["status"] = needle.Status,
        ["isConnected"] = needle.IsConnected,
        ["loadedSourceType"] = needle.LoadedSourceType,
        ["loadedReagentCode"] = needle.LoadedReagentCode,
        ["sourceBottleId"] = needle.SourceBottleId,
        ["dabBatchId"] = needle.DabBatchId,
        ["systemLiquidSourceType"] = needle.SystemLiquidSourceType,
        ["sourcePositionCode"] = needle.SourcePositionCode,
        ["volumeUl"] = needle.VolumeUl,
        ["liquidClassVersionId"] = needle.LiquidClassVersionId,
        ["liquidClassVersionNo"] = needle.LiquidClassVersionNo,
        ["needsWash"] = needle.NeedsWash,
        ["lastErrorCode"] = needle.LastErrorCode
    };

    private static bool CanArmOperate(RobotArmState arm) => arm.IsConnected && arm.LastErrorCode is null && arm.Status is not (MotionStatuses.Faulted or MotionStatuses.TimedOut or MotionStatuses.Unknown or MotionStatuses.Disconnected);
    private static bool CanArmBeReady(RobotArmState arm) => CanArmOperate(arm) && arm.IsHomed;
    private static bool CanNeedleOperate(NeedleState needle) => needle.IsConnected && needle.LastErrorCode is null && needle.Status is not (MotionStatuses.Faulted or MotionStatuses.TimedOut or MotionStatuses.Unknown or MotionStatuses.Disconnected);
    private static bool CanNeedleBeReady(NeedleState needle) => CanNeedleOperate(needle) && needle.VolumeUl == 0 && !needle.NeedsWash;

    private static string NormalizeNeedleCode(string? value, bool allowNull)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (allowNull) return null!;
            throw new BusinessRuleException("needle_code_required", "needleCode is required.", StatusCodes.Status400BadRequest);
        }

        if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle1;
        if (normalized.Equals("2", StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle2;
        var match = NeedleCodes.All.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessRuleException("needle_code_invalid", "needleCode must be Needle1 or Needle2.", StatusCodes.Status400BadRequest);
        }

        return match;
    }

    private static string RequireParam(DeviceOperationRequest request, string key)
    {
        var value = Convert.ToString(request.Parameters.GetValueOrDefault(key));
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleException($"{key}_required", $"{key} is required.", StatusCodes.Status400BadRequest);
        }

        return value.Trim();
    }

    private void EnsureMockMode()
    {
        if (!deviceModeService.IsMock)
        {
            throw new BusinessRuleException("motion_mock_not_available", "Motion Mock control is unavailable in Real mode.", StatusCodes.Status409Conflict);
        }
    }

    private static string MapStatusToDeviceStatus(string status) => status switch
    {
        MotionStatuses.TimedOut => DeviceCommandStatuses.TimedOut,
        MotionStatuses.Unknown => DeviceCommandStatuses.Unknown,
        _ => DeviceCommandStatuses.Failed
    };

    private static string StatusForDeviceFailure(string deviceStatus, string? errorCode)
    {
        if (deviceStatus == DeviceCommandStatuses.TimedOut) return MotionStatuses.TimedOut;
        if (deviceStatus == DeviceCommandStatuses.Unknown) return MotionStatuses.Unknown;
        if (!string.IsNullOrWhiteSpace(errorCode) && errorCode.Contains("disconnect", StringComparison.OrdinalIgnoreCase)) return MotionStatuses.Disconnected;
        return MotionStatuses.Faulted;
    }

    private static int? ToNullableInt(object? value)
    {
        if (value is null) return null;
        if (value is int i) return i;
        return int.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private sealed record MotionSource(
        string SourceType,
        string? ReagentCode,
        string? ReagentBottleId,
        string? DabBatchId,
        string? SystemLiquidSourceType,
        string SourcePositionCode,
        string SourceId);

    private sealed record CoordinateSnapshot(
        string? CoordinateProfileVersionId,
        IReadOnlyList<CoordinateSnapshotPoint> TargetPoints);

    private sealed record CoordinateSnapshotPoint(
        string PointCode,
        string PointType,
        long? CalibratedXUm,
        long? CalibratedYUm,
        long? CalibratedZUm,
        long? SafeZUm,
        long? LiquidDetectZUm,
        long? DispenseZUm,
        string ValidationStatus,
        bool RequiresCalibration,
        bool IsEnabled);
}

public sealed record MotionDeviceResult(
    bool Ok,
    string Status,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, object?> Data)
{
    public static MotionDeviceResult Succeeded(string message, IReadOnlyDictionary<string, object?>? data = null) =>
        new(true, DeviceCommandStatuses.Succeeded, null, message, data ?? new Dictionary<string, object?>());

    public static MotionDeviceResult Failed(string errorCode, string message, string status = DeviceCommandStatuses.Failed, IReadOnlyDictionary<string, object?>? data = null) =>
        new(false, status, errorCode, message, data ?? new Dictionary<string, object?>());
}

public sealed record MotionReadinessResult(bool Ok, string? ErrorCode, string Message);

public sealed record RobotTravelSafeZRequest(long SafeZUm);

public sealed record RobotMoveToXYAtSafeHeightRequest(
    string PointCode,
    long TargetXUm,
    long TargetYUm,
    long SafeZUm);

public sealed record MotionModuleState(
    string ModuleCode,
    string ConnectionStatus,
    string CurrentAction,
    string CurrentParametersJson,
    string? ErrorCode,
    string? ErrorMessage);
