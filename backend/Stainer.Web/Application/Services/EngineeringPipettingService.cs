using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class EngineeringPipettingService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    DeviceModeService deviceModeService,
    IRobotArmProcessActionService processActionService)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxManualVolumeUl = 5_000;

    public EngineeringPipettingTestCatalogResponse ListTestTypes()
    {
        return new EngineeringPipettingTestCatalogResponse(
            [
                new EngineeringPipettingTestTypeResponse(
                    PipettingOperationTypes.LiquidDetect,
                    "/api/engineering/pipetting-tests/liquid-detect",
                    true,
                    false,
                    false,
                    true,
                    "Manual liquid level detect at a configured coordinate point."),
                new EngineeringPipettingTestTypeResponse(
                    PipettingOperationTypes.Aspirate,
                    "/api/engineering/pipetting-tests/aspirate",
                    true,
                    true,
                    true,
                    true,
                    "Manual aspirate test using an existing liquid class version."),
                new EngineeringPipettingTestTypeResponse(
                    PipettingOperationTypes.Dispense,
                    "/api/engineering/pipetting-tests/dispense",
                    true,
                    true,
                    true,
                    true,
                    "Manual dispense test from the selected needle state."),
                new EngineeringPipettingTestTypeResponse(
                    PipettingOperationTypes.WashNeedle,
                    "/api/engineering/pipetting-tests/wash",
                    true,
                    false,
                    false,
                    true,
                    "Manual needle wash test using the existing wash operation model."),
                new EngineeringPipettingTestTypeResponse(
                    PipettingOperationTypes.WashNeedle,
                    "/api/engineering/pipetting-tests/flush",
                    true,
                    false,
                    false,
                    true,
                    "Flush is currently recorded as the existing WashNeedle domain operation.")
            ],
            ["Purge", "ClearChannel"],
            DateTimeOffset.UtcNow);
    }

    public Task<EngineeringPipettingTestResponse> LiquidDetectAsync(
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(PipettingOperationTypes.LiquidDetect, request, actor, cancellationToken);

    public Task<EngineeringPipettingTestResponse> AspirateAsync(
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(PipettingOperationTypes.Aspirate, request, actor, cancellationToken);

    public Task<EngineeringPipettingTestResponse> DispenseAsync(
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(PipettingOperationTypes.Dispense, request, actor, cancellationToken);

    public Task<EngineeringPipettingTestResponse> WashAsync(
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(PipettingOperationTypes.WashNeedle, request, actor, cancellationToken);

    public Task<EngineeringPipettingTestResponse> FlushAsync(
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(PipettingOperationTypes.WashNeedle, request, actor, cancellationToken, requestedOperation: "Flush");

    private Task<EngineeringPipettingTestResponse> ExecuteAsync(
        string operationType,
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken,
        string? requestedOperation = null)
    {
        var commandId = RequireValue(request.CommandId, "commandId");
        var operationKey = (requestedOperation ?? operationType).ToLowerInvariant();
        return idempotencyService.RunAsync(
            commandId,
            $"engineering.pipetting.{operationKey}",
            request,
            actor,
            async () =>
            {
                EnsureMockMode();
                await Gate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureSeededCoreAsync(cancellationToken);
                    var channel = NormalizeChannel(request.Channel);
                    var needleCode = NormalizeNeedleCode(
                        request.NeedleCode ?? ReadOperationString(request, "needleCode") ?? "Needle1");
                    var rawPosition = request.Position ?? request.CoordinatePointCode;
                    var position = NormalizePosition(FriendlyPointCodeResolver.Resolve(rawPosition) ?? rawPosition);
                    var volumeUl = ResolveVolumeUl(operationType, request);
                    var coordinatePoint = await RequireCoordinatePointAsync(position, request.CoordinateProfileVersionId, cancellationToken);
                    var liquidClass = await ResolveLiquidClassAsync(operationType, request, cancellationToken);
                    var now = DateTimeOffset.UtcNow;
                    var arm = await dbContext.RobotArmStates.SingleAsync(cancellationToken);
                    var needle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == needleCode, cancellationToken);
                    var failure = ValidateState(operationType, needle, volumeUl);
                    if (failure is not null)
                    {
                        var failedOperation = AddOperation(
                            operationType,
                            DeviceCommandStatus.Failed,
                            request,
                            actor,
                            channel,
                            needleCode,
                            position,
                            coordinatePoint,
                            liquidClass,
                            volumeUl,
                            failure.Value.ErrorCode,
                            failure.Value.Message,
                            requestedOperation);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        var failedResponse = BuildResponse(
                            failedOperation,
                            false,
                            commandId,
                            channel,
                            needle,
                            coordinatePoint,
                            failure.Value.Message,
                            failure.Value.ErrorCode);
                        return new CommandExecutionResult<EngineeringPipettingTestResponse>(
                            failedResponse,
                            "PipettingOperation",
                            failedOperation.Id);
                    }

                    var processFailure = await ExecuteProcessActionIfRequiredAsync(
                        operationType,
                        requestedOperation,
                        request,
                        commandId,
                        needleCode,
                        position,
                        coordinatePoint,
                        volumeUl,
                        cancellationToken);
                    if (processFailure is not null)
                    {
                        var failedOperation = AddOperation(
                            operationType,
                            DeviceCommandStatus.Failed,
                            request,
                            actor,
                            channel,
                            needleCode,
                            position,
                            coordinatePoint,
                            liquidClass,
                            volumeUl,
                            processFailure.Value.ErrorCode,
                            processFailure.Value.Message,
                            requestedOperation);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        var failedResponse = BuildResponse(
                            failedOperation,
                            false,
                            commandId,
                            channel,
                            needle,
                            coordinatePoint,
                            processFailure.Value.Message,
                            processFailure.Value.ErrorCode);
                        return new CommandExecutionResult<EngineeringPipettingTestResponse>(
                            failedResponse,
                            "PipettingOperation",
                            failedOperation.Id);
                    }

                    ApplyArmState(arm, operationType, coordinatePoint, commandId);
                    ApplyNeedleState(needle, operationType, request, liquidClass, position, volumeUl, commandId);
                    var operation = AddOperation(
                        operationType,
                        DeviceCommandStatus.Completed,
                        request,
                        actor,
                        channel,
                        needleCode,
                        position,
                        coordinatePoint,
                        liquidClass,
                        volumeUl,
                        null,
                        null,
                        requestedOperation,
                        now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    var response = BuildResponse(
                        operation,
                        true,
                        commandId,
                        channel,
                        needle,
                        coordinatePoint,
                        $"{operationType} engineering pipetting test completed.",
                        null);
                    return new CommandExecutionResult<EngineeringPipettingTestResponse>(
                        response,
                        "PipettingOperation",
                        operation.Id);
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

    private async Task<CoordinatePoint> RequireCoordinatePointAsync(
        string position,
        string? coordinateProfileVersionId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CoordinatePoints
            .Include(x => x.CoordinateProfileVersion)
            .Where(x => x.IsEnabled && x.PointCode == position);
        if (!string.IsNullOrWhiteSpace(coordinateProfileVersionId))
        {
            var versionId = coordinateProfileVersionId.Trim();
            query = query.Where(x => x.CoordinateProfileVersionId == versionId);
        }
        else
        {
            query = query.Where(x => x.CoordinateProfileVersion != null && x.CoordinateProfileVersion.IsActive);
        }

        var point = await query
            .OrderByDescending(x => x.CoordinateProfileVersion != null && x.CoordinateProfileVersion.IsActive)
            .ThenBy(x => x.PointCode)
            .FirstOrDefaultAsync(cancellationToken);
        return point
            ?? throw new BusinessRuleException(
                "engineering_pipetting_position_invalid",
                $"Position {position} is not an enabled coordinate point.",
                StatusCodes.Status400BadRequest);
    }

    private async Task<LiquidClassVersion?> ResolveLiquidClassAsync(
        string operationType,
        EngineeringPipettingTestRequest request,
        CancellationToken cancellationToken)
    {
        var requiresLiquidClass = operationType is PipettingOperationTypes.Aspirate or PipettingOperationTypes.Dispense;
        if (string.IsNullOrWhiteSpace(request.LiquidClassVersionId) && string.IsNullOrWhiteSpace(request.LiquidClassCode))
        {
            if (requiresLiquidClass)
            {
                throw new BusinessRuleException(
                    "engineering_pipetting_liquid_class_required",
                    "Aspirate and dispense tests require LiquidClassVersionId or LiquidClassCode.",
                    StatusCodes.Status400BadRequest);
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.LiquidClassVersionId))
        {
            var versionId = request.LiquidClassVersionId.Trim();
            return await dbContext.LiquidClassVersions
                .Include(x => x.LiquidClassProfile)
                .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
                ?? throw new BusinessRuleException(
                    "engineering_pipetting_liquid_class_not_found",
                    "Liquid class version was not found.",
                    StatusCodes.Status404NotFound);
        }

        var code = request.LiquidClassCode!.Trim();
        return await dbContext.LiquidClassVersions
            .Include(x => x.LiquidClassProfile)
            .Where(x => x.LiquidClassProfile != null
                && x.LiquidClassProfile.Code == code
                && x.LiquidClassProfile.IsEnabled
                && x.LiquidClassProfile.EnabledVersionId == x.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException(
                "engineering_pipetting_liquid_class_not_found",
                "Enabled liquid class was not found.",
                StatusCodes.Status404NotFound);
    }

    private (string ErrorCode, string Message)? ValidateState(string operationType, NeedleState needle, int volumeUl)
    {
        if (!CanNeedleOperate(needle))
        {
            return (needle.LastErrorCode ?? "needle_not_ready", needle.LastErrorMessage ?? $"Needle {needle.NeedleCode} is not ready.");
        }

        if (operationType == PipettingOperationTypes.Dispense && needle.VolumeUl < volumeUl)
        {
            return ("needle_volume_insufficient", $"Needle {needle.NeedleCode} contains {needle.VolumeUl} uL and cannot dispense {volumeUl} uL.");
        }

        return null;
    }

    private async Task<(string ErrorCode, string Message)?> ExecuteProcessActionIfRequiredAsync(
        string operationType,
        string? requestedOperation,
        EngineeringPipettingTestRequest request,
        string commandId,
        string needleCode,
        string position,
        CoordinatePoint point,
        int volumeUl,
        CancellationToken cancellationToken)
    {
        var processAction = ResolveProcessAction(operationType, requestedOperation, point);
        if (processAction is EngineeringProcessActionKind.None)
        {
            return null;
        }

        var coordinateFailure = ValidateProcessMoveTarget(point);
        if (coordinateFailure is not null)
        {
            return coordinateFailure;
        }

        var move = new RobotMoveToXYAtSafeHeightRequest(
            position,
            point.CalibratedXUm!.Value,
            point.CalibratedYUm!.Value,
            point.SafeZUm!.Value);

        RobotArmProcessActionResult result = processAction switch
        {
            EngineeringProcessActionKind.Aspirate => await processActionService.MoveToSourceAndTakeLiquidAsync(
                move,
                new TakeLiquidRequest(
                    commandId,
                    needleCode,
                    volumeUl,
                    request.Reason,
                    AspirateZUm: point.LiquidDetectZUm ?? point.CalibratedZUm ?? point.SafeZUm,
                    SafeZUm: point.SafeZUm),
                cancellationToken),
            EngineeringProcessActionKind.Dispense => await processActionService.MoveToTargetAndDispenseLiquidAsync(
                move,
                new DispenseLiquidRequest(
                    commandId,
                    needleCode,
                    volumeUl,
                    request.Reason,
                    DispenseZUm: point.DispenseZUm ?? point.CalibratedZUm ?? point.SafeZUm,
                    SafeZUm: point.SafeZUm),
                cancellationToken),
            EngineeringProcessActionKind.WashOuter => await processActionService.MoveToOuterWashAndWashOuterAsync(
                move,
                new WashOuterRequest(
                    commandId,
                    needleCode,
                    request.Reason,
                    WashOuterZUm: point.CalibratedZUm ?? point.SafeZUm,
                    SafeZUm: point.SafeZUm),
                cancellationToken),
            _ => RobotArmProcessActionResult.Succeeded()
        };

        if (result.Success)
        {
            return null;
        }

        return (
            result.FailureStage == RobotArmProcessFailureStage.Move
                ? "engineering_pipetting_move_failed"
                : "engineering_pipetting_action_failed",
            result.ErrorMessage ?? "Engineering pipetting process action failed.");
    }

    private static EngineeringProcessActionKind ResolveProcessAction(
        string operationType,
        string? requestedOperation,
        CoordinatePoint point)
    {
        if (operationType == PipettingOperationTypes.Aspirate)
        {
            return EngineeringProcessActionKind.Aspirate;
        }

        if (operationType == PipettingOperationTypes.Dispense)
        {
            return EngineeringProcessActionKind.Dispense;
        }

        if (operationType == PipettingOperationTypes.WashNeedle
            && requestedOperation is null
            && point.PointType.Equals("WashOuter", StringComparison.OrdinalIgnoreCase))
        {
            return EngineeringProcessActionKind.WashOuter;
        }

        return EngineeringProcessActionKind.None;
    }

    private static (string ErrorCode, string Message)? ValidateProcessMoveTarget(CoordinatePoint point)
    {
        if (point.CalibratedXUm is null)
        {
            return ("engineering_pipetting_coordinate_incomplete", $"Coordinate point {point.PointCode} is missing CalibratedXUm.");
        }

        if (point.CalibratedYUm is null)
        {
            return ("engineering_pipetting_coordinate_incomplete", $"Coordinate point {point.PointCode} is missing CalibratedYUm.");
        }

        if (point.SafeZUm is null)
        {
            return ("engineering_pipetting_coordinate_incomplete", $"Coordinate point {point.PointCode} is missing SafeZUm.");
        }

        return null;
    }

    private void ApplyArmState(RobotArmState arm, string operationType, CoordinatePoint point, string commandId)
    {
        arm.IsConnected = true;
        arm.IsHomed = true;
        arm.Status = MotionStatuses.Completed;
        arm.CurrentTargetPointCode = point.PointCode;
        arm.CurrentXUm = point.CalibratedXUm;
        arm.CurrentYUm = point.CalibratedYUm;
        arm.CurrentZUm = operationType switch
        {
            PipettingOperationTypes.LiquidDetect or PipettingOperationTypes.Aspirate => point.LiquidDetectZUm ?? point.CalibratedZUm ?? point.SafeZUm,
            PipettingOperationTypes.Dispense => point.DispenseZUm ?? point.CalibratedZUm ?? point.SafeZUm,
            _ => point.SafeZUm ?? point.CalibratedZUm
        };
        arm.CoordinateProfileVersionId = point.CoordinateProfileVersionId;
        arm.CurrentCommandId = commandId;
        arm.DeviceCommandExecutionId = commandId;
        arm.MachineRunId = null;
        arm.WorkflowStepExecutionId = null;
        arm.LastErrorCode = null;
        arm.LastErrorMessage = null;
        arm.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void ApplyNeedleState(
        NeedleState needle,
        string operationType,
        EngineeringPipettingTestRequest request,
        LiquidClassVersion? liquidClass,
        string position,
        int volumeUl,
        string commandId)
    {
        if (operationType == PipettingOperationTypes.Aspirate)
        {
            needle.LoadedSourceType = NormalizeSourceType(ReadOperationString(request, "sourceType")) ?? NeedleLoadSourceTypes.SystemLiquid;
            needle.LoadedReagentCode = ReadOperationString(request, "reagentCode");
            needle.SourceBottleId = ReadOperationString(request, "reagentBottleId");
            needle.DabBatchId = ReadOperationString(request, "dabBatchId");
            needle.SystemLiquidSourceType = ReadOperationString(request, "systemLiquidSourceType") ?? needle.LoadedSourceType;
            needle.SourcePositionCode = ReadOperationString(request, "sourcePositionCode") ?? position;
            needle.VolumeUl += volumeUl;
            needle.NeedsWash = true;
            ApplyLiquidClass(needle, liquidClass);
        }
        else if (operationType == PipettingOperationTypes.Dispense)
        {
            needle.VolumeUl -= volumeUl;
            needle.NeedsWash = true;
            ApplyLiquidClass(needle, liquidClass);
        }
        else if (operationType == PipettingOperationTypes.WashNeedle)
        {
            ClearNeedleLoad(needle);
        }

        if (operationType != PipettingOperationTypes.WashNeedle)
        {
            needle.Status = MotionStatuses.Completed;
        }

        needle.IsConnected = true;
        needle.CurrentCommandId = commandId;
        needle.DeviceCommandExecutionId = commandId;
        needle.MachineRunId = null;
        needle.WorkflowStepExecutionId = null;
        needle.LastErrorCode = null;
        needle.LastErrorMessage = null;
        needle.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyLiquidClass(NeedleState needle, LiquidClassVersion? liquidClass)
    {
        if (liquidClass is null)
        {
            return;
        }

        needle.LiquidClassVersionId = liquidClass.Id;
        needle.LiquidClassVersionNo = liquidClass.VersionNo;
        needle.LiquidClassParametersJson = LiquidClassParametersJson(liquidClass);
    }

    private PipettingOperation AddOperation(
        string operationType,
        string status,
        EngineeringPipettingTestRequest request,
        AuthenticatedUser actor,
        string channel,
        string needleCode,
        string position,
        CoordinatePoint point,
        LiquidClassVersion? liquidClass,
        int volumeUl,
        string? errorCode,
        string? errorMessage,
        string? requestedOperation,
        DateTimeOffset? createdAtUtc = null)
    {
        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        var sourceType = operationType == PipettingOperationTypes.Aspirate
            ? NormalizeSourceType(ReadOperationString(request, "sourceType")) ?? NeedleLoadSourceTypes.SystemLiquid
            : NeedleLoadSourceTypes.Empty;
        var operation = new PipettingOperation
        {
            OperationType = operationType,
            Status = status,
            ChannelCode = channel,
            NeedleCode = needleCode,
            ExecutionMode = NormalizeExecutionMode(ReadOperationString(request, "executionMode")),
            TargetPointCode = position,
            CoordinateProfileVersionId = point.CoordinateProfileVersionId,
            LiquidClassVersionId = liquidClass?.Id,
            LiquidClassVersionNo = liquidClass?.VersionNo,
            LiquidClassParametersJson = liquidClass is null ? "{}" : LiquidClassParametersJson(liquidClass),
            SourceType = sourceType,
            ReagentCode = ReadOperationString(request, "reagentCode"),
            ReagentBottleId = ReadOperationString(request, "reagentBottleId"),
            DabBatchId = ReadOperationString(request, "dabBatchId"),
            SystemLiquidSourceType = ReadOperationString(request, "systemLiquidSourceType"),
            SourcePositionCode = operationType == PipettingOperationTypes.Aspirate
                ? ReadOperationString(request, "sourcePositionCode") ?? position
                : null,
            VolumeUl = volumeUl,
            DeviceCommandExecutionId = request.CommandId.Trim(),
            ParametersJson = BuildParametersJson(operationType, request, point, requestedOperation),
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };
        dbContext.PipettingOperations.Add(operation);
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = operation.ActorUserId,
            Action = $"engineering.pipetting.{(requestedOperation ?? operationType).ToLowerInvariant()}",
            EntityType = "PipettingOperation",
            EntityId = operation.Id,
            Message = JsonSerializer.Serialize(new
            {
                operation.OperationType,
                requestedOperation,
                operation.Status,
                operation.ChannelCode,
                operation.NeedleCode,
                operation.TargetPointCode,
                operation.CoordinateProfileVersionId,
                operation.LiquidClassVersionId,
                operation.LiquidClassVersionNo,
                operation.VolumeUl,
                operation.ErrorCode,
                reason = request.Reason,
                actor = actor.Username
            }, JsonOptions),
            CreatedAtUtc = now
        });
        return operation;
    }

    private static EngineeringPipettingTestResponse BuildResponse(
        PipettingOperation operation,
        bool ok,
        string commandId,
        string channel,
        NeedleState needle,
        CoordinatePoint point,
        string message,
        string? errorCode)
    {
        return new EngineeringPipettingTestResponse(
            ok,
            commandId,
            false,
            operation.Id,
            operation.OperationType,
            operation.Status,
            channel,
            needle.NeedleCode,
            point.PointCode,
            operation.VolumeUl,
            operation.LiquidClassVersionId,
            operation.LiquidClassVersionNo,
            message,
            errorCode,
            new Dictionary<string, object?>
            {
                ["needleStatus"] = needle.Status,
                ["needleVolumeUl"] = needle.VolumeUl,
                ["needleNeedsWash"] = needle.NeedsWash,
                ["loadedSourceType"] = needle.LoadedSourceType,
                ["loadedReagentCode"] = needle.LoadedReagentCode,
                ["coordinateProfileVersionId"] = point.CoordinateProfileVersionId,
                ["pointType"] = point.PointType
            });
    }

    private void EnsureMockMode()
    {
        if (!deviceModeService.IsMock)
        {
            throw new BusinessRuleException(
                "engineering_pipetting_real_not_available",
                "Engineering pipetting tests are fail-closed outside Mock mode until real hardware validation is explicitly implemented.",
                StatusCodes.Status409Conflict);
        }
    }

    private static int ResolveVolumeUl(string operationType, EngineeringPipettingTestRequest request)
    {
        var value = request.VolumeUl ?? request.Volume ?? ReadOperationInt(request, "volumeUl") ?? 0;
        if (operationType is PipettingOperationTypes.Aspirate or PipettingOperationTypes.Dispense)
        {
            if (value is < 1 or > MaxManualVolumeUl)
            {
                throw new BusinessRuleException(
                    "engineering_pipetting_volume_invalid",
                    $"Volume must be between 1 and {MaxManualVolumeUl} uL.",
                    StatusCodes.Status400BadRequest);
            }
        }
        else if (value is < 0 or > MaxManualVolumeUl)
        {
            throw new BusinessRuleException(
                "engineering_pipetting_volume_invalid",
                $"Volume must be between 0 and {MaxManualVolumeUl} uL.",
                StatusCodes.Status400BadRequest);
        }

        return value;
    }

    private static string NormalizeChannel(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        normalized = normalized switch
        {
            "1" or "CHANNEL1" or "CHANNEL 1" => "A",
            "2" or "CHANNEL2" or "CHANNEL 2" => "B",
            "3" or "CHANNEL3" or "CHANNEL 3" => "C",
            "4" or "CHANNEL4" or "CHANNEL 4" => "D",
            _ => normalized
        };
        if (normalized is not ("A" or "B" or "C" or "D"))
        {
            throw new BusinessRuleException("engineering_pipetting_channel_invalid", "Channel must be A, B, C, or D.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeNeedleCode(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle1;
        if (normalized.Equals("2", StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle2;
        var match = NeedleCodes.All.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BusinessRuleException("engineering_pipetting_needle_invalid", "NeedleCode must be Needle1 or Needle2.", StatusCodes.Status400BadRequest);
        }

        return match;
    }

    private static string NormalizePosition(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("engineering_pipetting_position_required", "Position or CoordinatePointCode is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string? NormalizeSourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Equals(NeedleLoadSourceTypes.SystemLiquid, StringComparison.OrdinalIgnoreCase)) return NeedleLoadSourceTypes.SystemLiquid;
        if (normalized.Equals(NeedleLoadSourceTypes.ReagentBottle, StringComparison.OrdinalIgnoreCase)) return NeedleLoadSourceTypes.ReagentBottle;
        if (normalized.Equals(NeedleLoadSourceTypes.DabBatch, StringComparison.OrdinalIgnoreCase)) return NeedleLoadSourceTypes.DabBatch;
        throw new BusinessRuleException("engineering_pipetting_source_type_invalid", "SourceType must be SystemLiquid, ReagentBottle, or DabBatch.", StatusCodes.Status400BadRequest);
    }

    private static string NormalizeExecutionMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PipettingExecutionModes.Single;
        }

        var normalized = value.Trim();
        if (normalized.Equals(PipettingExecutionModes.Single, StringComparison.OrdinalIgnoreCase)) return PipettingExecutionModes.Single;
        if (normalized.Equals(PipettingExecutionModes.Sequential, StringComparison.OrdinalIgnoreCase)) return PipettingExecutionModes.Sequential;
        if (normalized.Equals(PipettingExecutionModes.Synchronized, StringComparison.OrdinalIgnoreCase)) return PipettingExecutionModes.Synchronized;
        throw new BusinessRuleException("engineering_pipetting_execution_mode_invalid", "ExecutionMode is not supported.", StatusCodes.Status400BadRequest);
    }

    private static bool CanNeedleOperate(NeedleState needle) =>
        needle.IsConnected
        && needle.LastErrorCode is null
        && needle.Status is not (MotionStatuses.Faulted or MotionStatuses.TimedOut or MotionStatuses.Unknown or MotionStatuses.Disconnected);

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
    }

    private static string LiquidClassParametersJson(LiquidClassVersion version) =>
        JsonSerializer.Serialize(new
        {
            version.LiquidDetectionEnabled,
            version.LiquidDetectionSensitivityPercent,
            version.LiquidDetectionSpeedUmPerSecond,
            version.AspirateSpeedUlPerSecond,
            version.AspirateDelayMs,
            version.DispenseSpeedUlPerSecond,
            version.DispenseDelayMs,
            version.LeadingAirGapUl,
            version.TrailingAirGapUl,
            version.BlowoutVolumeUl,
            version.BlowoutDelayMs,
            version.VolumeAdjustmentUl,
            version.PreWetCycles,
            version.MixCycles,
            version.LiquidFollowingDepthUm,
            version.RetractSpeedUmPerSecond,
            version.ConditioningVolumeUl,
            version.BreakoffSpeedUlPerSecond,
            version.PostDispenseAirGapUl
        }, JsonOptions);

    private static string BuildParametersJson(
        string operationType,
        EngineeringPipettingTestRequest request,
        CoordinatePoint point,
        string? requestedOperation)
    {
        var payload = new Dictionary<string, object?>
        {
            ["operationType"] = operationType,
            ["requestedOperation"] = requestedOperation,
            ["channel"] = request.Channel,
            ["needleCode"] = request.NeedleCode,
            ["position"] = request.Position,
            ["coordinatePointCode"] = request.CoordinatePointCode,
            ["coordinateProfileVersionId"] = point.CoordinateProfileVersionId,
            ["volumeUl"] = request.VolumeUl ?? request.Volume,
            ["liquidClassVersionId"] = request.LiquidClassVersionId,
            ["liquidClassCode"] = request.LiquidClassCode,
            ["reason"] = request.Reason,
            ["operationParameters"] = request.OperationParameters.HasValue && request.OperationParameters.Value.ValueKind != JsonValueKind.Undefined
                ? ToJsonValue(request.OperationParameters.Value)
                : null
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? ReadOperationString(EngineeringPipettingTestRequest request, string propertyName)
    {
        if (!request.OperationParameters.HasValue
            || request.OperationParameters.Value.ValueKind != JsonValueKind.Object
            || !request.OperationParameters.Value.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString().Trim();
    }

    private static int? ReadOperationInt(EngineeringPipettingTestRequest request, string propertyName)
    {
        if (!request.OperationParameters.HasValue
            || request.OperationParameters.Value.ValueKind != JsonValueKind.Object
            || !request.OperationParameters.Value.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static object? ToJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(x => x.Name, x => ToJsonValue(x.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ToJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static string RequireValue(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException($"{fieldName}_required", $"{fieldName} is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private enum EngineeringProcessActionKind
    {
        None,
        Aspirate,
        Dispense,
        WashOuter
    }
}
