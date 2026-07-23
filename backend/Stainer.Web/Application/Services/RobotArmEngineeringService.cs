using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

/// <summary>
/// 机械臂工程隔离测试入口：把 <see cref="MotionControlService"/>（XY 安全平移）和
/// <see cref="IRobotArmAtomicActionService"/>（工艺原子动作）包上幂等 + 审计外壳，
/// 供工程端点在不必跑完整染色工作流的前提下隔离验证双针运动原语。
/// 模式无关：Mock 模式驱动 MockRobotMotionPrimitives（验证编排）；Real 模式驱动
/// SoconRobotMotionPrimitives（未启用时原语 fail-closed 409）。三重门禁（admin + 工程写会话 + 危险确认）在端点层。
/// </summary>
public sealed class RobotArmEngineeringService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    MotionControlService motionControlService,
    IRobotArmAtomicActionService atomicActionService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<RobotArmHardwareActionResponse> MoveAsync(
        MoveRobotArmHardwareRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "robot_arm.hardware_move",
            new { request },
            actor,
            async () =>
            {
                var result = await motionControlService.MoveToXYAtSafeHeightAsync(
                    new RobotMoveToXYAtSafeHeightRequest(
                        request.PointCode ?? string.Empty,
                        request.TargetXUm,
                        request.TargetYUm,
                        request.SafeZUm),
                    cancellationToken);
                if (!result.Ok)
                {
                    throw new BusinessRuleException(
                        result.ErrorCode ?? "robot_arm_hardware_move_failed",
                        result.Message,
                        StatusCodes.Status503ServiceUnavailable);
                }

                await WriteAuditAsync(actor, request.CommandId, "robot_arm.hardware_move", "MoveXY", request.PointCode, request.Reason, new
                {
                    request.PointCode,
                    request.TargetXUm,
                    request.TargetYUm,
                    request.SafeZUm
                }, cancellationToken);

                return new CommandExecutionResult<RobotArmHardwareActionResponse>(
                    new RobotArmHardwareActionResponse(
                        true,
                        request.CommandId,
                        false,
                        "MoveXY",
                        ["EnsureTravelSafeZ", "MoveXY"],
                        result.Message),
                    "RobotArm",
                    request.PointCode);
            },
            cancellationToken);
    }

    public Task<RobotArmHardwareActionResponse> RunAtomicActionAsync(
        RunRobotArmAtomicHardwareRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "robot_arm.atomic_action",
            new { request },
            actor,
            async () =>
            {
                var action = NormalizeAction(request.Action);
                var result = await DispatchAtomicActionAsync(action, request, cancellationToken);
                if (!result.Ok)
                {
                    throw new BusinessRuleException(
                        result.ErrorCode ?? "robot_arm_atomic_action_failed",
                        result.Message,
                        StatusCodes.Status503ServiceUnavailable);
                }

                var steps = result.Steps
                    .Select(step => string.IsNullOrWhiteSpace(step.Detail) ? step.Name : $"{step.Name}({step.Detail})")
                    .ToList();
                await WriteAuditAsync(actor, request.CommandId, "robot_arm.atomic_action", action, request.NeedleCode, request.Reason, new
                {
                    action,
                    request.NeedleCode,
                    request.VolumeUl,
                    request.WashVolumeUl,
                    request.WasteVolumeUl,
                    steps
                }, cancellationToken);

                return new CommandExecutionResult<RobotArmHardwareActionResponse>(
                    new RobotArmHardwareActionResponse(
                        true,
                        request.CommandId,
                        false,
                        action,
                        steps,
                        result.Message),
                    "RobotArm",
                    action);
            },
            cancellationToken);
    }

    private Task<RobotArmAtomicActionResult> DispatchAtomicActionAsync(
        string action,
        RunRobotArmAtomicHardwareRequest request,
        CancellationToken cancellationToken)
    {
        return action switch
        {
            RobotAtomicActions.TakeLiquid => atomicActionService.TakeLiquidAsync(
                new TakeLiquidRequest(request.CommandId, request.NeedleCode, RequireVolume(request, nameof(request.VolumeUl)), request.Reason, request.AspirateZUm, request.SafeZUm), cancellationToken),
            RobotAtomicActions.PrepareMix => atomicActionService.PrepareMixAsync(
                new PrepareMixRequest(request.CommandId, request.NeedleCode, RequireVolume(request, nameof(request.VolumeUl)), request.Reason, request.MixZUm, request.SafeZUm), cancellationToken),
            RobotAtomicActions.DispenseLiquid => atomicActionService.DispenseLiquidAsync(
                new DispenseLiquidRequest(request.CommandId, request.NeedleCode, RequireVolume(request, nameof(request.VolumeUl)), request.Reason, request.DispenseZUm, request.SafeZUm), cancellationToken),
            RobotAtomicActions.WashInner => atomicActionService.WashInnerAsync(
                new WashInnerRequest(request.CommandId, request.NeedleCode, RequireVolumeField(request.WashVolumeUl, nameof(request.WashVolumeUl)), RequireVolumeField(request.WasteVolumeUl, nameof(request.WasteVolumeUl)), request.Reason, request.WashInnerZUm, request.SafeZUm), cancellationToken),
            RobotAtomicActions.WashOuter => atomicActionService.WashOuterAsync(
                new WashOuterRequest(request.CommandId, request.NeedleCode, request.Reason, request.WashOuterZUm, request.SafeZUm), cancellationToken),
            _ => throw new BusinessRuleException(
                "atomic_action_invalid",
                $"action must be one of {RobotAtomicActions.TakeLiquid}, {RobotAtomicActions.PrepareMix}, {RobotAtomicActions.DispenseLiquid}, {RobotAtomicActions.WashInner}, {RobotAtomicActions.WashOuter}.",
                StatusCodes.Status400BadRequest)
        };
    }

    private static string NormalizeAction(string? action)
    {
        var normalized = action?.Trim() ?? string.Empty;
        return normalized switch
        {
            RobotAtomicActions.TakeLiquid or RobotAtomicActions.PrepareMix or RobotAtomicActions.DispenseLiquid
                or RobotAtomicActions.WashInner or RobotAtomicActions.WashOuter => normalized,
            _ => throw new BusinessRuleException(
                "atomic_action_invalid",
                $"action must be one of {RobotAtomicActions.TakeLiquid}, {RobotAtomicActions.PrepareMix}, {RobotAtomicActions.DispenseLiquid}, {RobotAtomicActions.WashInner}, {RobotAtomicActions.WashOuter}.",
                StatusCodes.Status400BadRequest)
        };
    }

    private static int RequireVolume(RunRobotArmAtomicHardwareRequest request, string fieldName) =>
        RequireVolumeField(request.VolumeUl, fieldName);

    private static int RequireVolumeField(int? volumeUl, string fieldName)
    {
        if (!volumeUl.HasValue || volumeUl.Value <= 0)
        {
            throw new BusinessRuleException("atomic_action_volume_invalid", $"{fieldName} must be a positive volume in uL.", StatusCodes.Status400BadRequest);
        }

        return volumeUl.Value;
    }

    private async Task WriteAuditAsync(
        AuthenticatedUser actor,
        string commandId,
        string auditAction,
        string action,
        string? detail,
        string? reason,
        object payload,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = auditAction,
            EntityType = "RobotArm",
            EntityId = action,
            Message = JsonSerializer.Serialize(new
            {
                commandId,
                action,
                detail,
                reason = reason?.Trim() ?? string.Empty,
                payload
            }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
