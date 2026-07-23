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
/// 通用坐标点（玻片/A-B液/配液/清洗位等）到 SOCON 真实运动的独立工程入口。
/// 与 <see cref="ReagentPositionHardwareService"/> 同源：复用已审核的 SOCON Bridge MoveX/Y/Z 路径，
/// 仅把数据源从试剂位表（ReagentPositionConfigs，mm）换成通用坐标点表（CoordinatePoints，µm）。
/// 保存坐标校准仍由 CoordinateProfileLifecycleService 负责；本服务只做显式危险确认后的现场移动。
/// Real-only：Mock 模式抛 409，绝不回退 Mock。现场移动后停在目标 Z（不自动回 SafeZ），便于现场观测；
/// 观测结束后调用方应显式再发一次 TargetZ=safe 抬回安全高度。
/// </summary>
public sealed class CoordinatePointHardwareService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    DeviceModeService deviceModeService,
    IReagentHardwareActionClient hardwareActionClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CoordinatePointHardwareActionResponse> MoveAsync(
        string pointCode,
        MoveCoordinatePointHardwareRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var normalizedPointCode = NormalizePointCode(pointCode);
        return idempotencyService.RunAsync(
            request.CommandId,
            "coordinate_point.hardware_move",
            new { pointCode = normalizedPointCode, request },
            actor,
            async () =>
            {
                if (deviceModeService.IsMock)
                {
                    throw new BusinessRuleException(
                        "coordinate_point_hardware_real_required",
                        "Coordinate point hardware movement requires Real device mode.",
                        StatusCodes.Status409Conflict);
                }

                var point = await RequireCoordinatePointAsync(normalizedPointCode, request.CoordinateProfileVersionId, cancellationToken);

                if (!point.CalibratedXUm.HasValue || !point.CalibratedYUm.HasValue || !point.SafeZUm.HasValue)
                {
                    throw new BusinessRuleException(
                        "coordinate_point_xy_safe_z_required",
                        "Calibrated X/Y and safe Z are required for real movement.",
                        StatusCodes.Status409Conflict);
                }

                var targetZName = NormalizeTargetZ(request.TargetZ);
                var targetZ = ResolveTargetZ(point, targetZName)
                    ?? throw new BusinessRuleException(
                        "coordinate_point_target_z_missing",
                        $"Configured {targetZName} Z value is required.",
                        StatusCodes.Status409Conflict);
                var needleCode = NormalizeNeedleCode(request.NeedleCode);
                var action = await hardwareActionClient.ExecuteAsync(
                    new ReagentHardwareActionRequest(
                        ReagentHardwareActionOperations.Move,
                        needleCode == NeedleCodes.Needle2 ? "z2" : "z1",
                        point.CalibratedXUm.Value,
                        point.CalibratedYUm.Value,
                        point.SafeZUm.Value,
                        targetZ),
                    cancellationToken);
                if (!action.Ok)
                {
                    throw new BusinessRuleException(
                        action.ErrorCode ?? "coordinate_point_hardware_move_failed",
                        action.Message,
                        StatusCodes.Status503ServiceUnavailable);
                }

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                    Action = "coordinate_point.hardware_move",
                    EntityType = "CoordinatePoint",
                    EntityId = point.Id,
                    Message = JsonSerializer.Serialize(new
                    {
                        pointCode = normalizedPointCode,
                        pointType = point.PointType,
                        needleCode,
                        targetZ = targetZName,
                        reason = request.Reason.Trim(),
                        action.CompletedSteps
                    }, JsonOptions),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });

                return new CommandExecutionResult<CoordinatePointHardwareActionResponse>(
                    new CoordinatePointHardwareActionResponse(
                        true,
                        request.CommandId,
                        false,
                        normalizedPointCode,
                        needleCode,
                        targetZName,
                        action.CompletedSteps,
                        "Coordinate point hardware movement completed."),
                    "CoordinatePoint",
                    point.Id);
            },
            cancellationToken);
    }

    // 通用坐标点解析：pointType 无关，与 EngineeringPipettingService.RequireCoordinatePointAsync 同源。
    // 不抽共享 helper 以避免跨服务耦合（查询逻辑稳定，复制成本低于耦合成本）。
    private async Task<CoordinatePoint> RequireCoordinatePointAsync(
        string pointCode,
        string? coordinateProfileVersionId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CoordinatePoints
            .Include(x => x.CoordinateProfileVersion)
            .Where(x => x.IsEnabled && x.PointCode == pointCode);
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
                "coordinate_point_not_found",
                $"Point {pointCode} is not an enabled coordinate point.",
                StatusCodes.Status404NotFound);
    }

    private static string NormalizePointCode(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("point_code_required", "pointCode is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string NormalizeNeedleCode(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.Equals(normalized, NeedleCodes.Needle1, StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle1;
        if (string.Equals(normalized, NeedleCodes.Needle2, StringComparison.OrdinalIgnoreCase)) return NeedleCodes.Needle2;
        throw new BusinessRuleException("needle_code_invalid", "needleCode must be Needle1 or Needle2.", StatusCodes.Status400BadRequest);
    }

    private static string NormalizeTargetZ(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "safe" => "safe",
            "liquid-detect" => "liquid-detect",
            "aspirate-end" => "aspirate-end",
            "dispense" => "dispense",
            _ => throw new BusinessRuleException(
                "target_z_invalid",
                "targetZ must be safe, liquid-detect, aspirate-end or dispense.",
                StatusCodes.Status400BadRequest)
        };
    }

    private static long? ResolveTargetZ(CoordinatePoint point, string targetZ) => targetZ switch
    {
        "safe" => point.SafeZUm,
        "liquid-detect" => point.LiquidDetectZUm,
        "aspirate-end" => point.AspirateEndZUm,
        "dispense" => point.DispenseZUm,
        _ => null
    };
}
