using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

// 试剂位对象配置服务：按 rackCode(R1-R40) 读取 / upsert 持久化。
// 镜像 PrecisionCalibrationConfigService 的幂等 + 审计模式。简单单行配置，不走 coordinate 版本治理。
public sealed class ReagentPositionConfigService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReagentPositionConfigResponse> GetAsync(string rackCode, CancellationToken cancellationToken = default)
    {
        var key = NormalizeRackCode(rackCode);
        var cfg = await dbContext.ReagentPositionConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RackCode == key, cancellationToken);
        return ToResponse(cfg ?? CreateDefault(key));
    }

    public Task<ReagentPositionConfigMutationResponse> SaveAsync(
        string rackCode,
        SaveReagentPositionConfigRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        rackCode = NormalizeRackCode(rackCode);
        return idempotencyService.RunAsync(
            request.CommandId,
            "reagent_position_config.save",
            new { rackCode, request },
            actor,
            async () =>
            {
                RequireReason(request.Reason);
                var x = ValidateRange(request.CalibratedXMm, "calibratedXMm", -1000m, 1000m);
                var y = ValidateRange(request.CalibratedYMm, "calibratedYMm", -1000m, 1000m);
                var safeZ = ValidateRange(request.SafeZMm, "safeZMm", 0m, 200m);
                var liquidDetectZ = ValidateRange(request.LiquidDetectZMm, "liquidDetectZMm", 0m, 200m);
                var aspirateEndZ = ValidateRange(request.AspirateEndZMm, "aspirateEndZMm", 0m, 200m);
                var dispenseZ = ValidateRange(request.DispenseZMm, "dispenseZMm", 0m, 200m);

                var cfg = await dbContext.ReagentPositionConfigs
                    .SingleOrDefaultAsync(x => x.RackCode == rackCode, cancellationToken);
                object? before = cfg is null ? null : ToAudit(cfg);
                if (cfg is null)
                {
                    cfg = new ReagentPositionConfig { RackCode = rackCode, CreatedAtUtc = DateTimeOffset.UtcNow };
                    dbContext.ReagentPositionConfigs.Add(cfg);
                }

                if (x.HasValue) cfg.CalibratedXMm = x;
                if (y.HasValue) cfg.CalibratedYMm = y;
                if (safeZ.HasValue) cfg.SafeZMm = safeZ;
                if (liquidDetectZ.HasValue) cfg.LiquidDetectZMm = liquidDetectZ;
                if (aspirateEndZ.HasValue) cfg.AspirateEndZMm = aspirateEndZ;
                if (dispenseZ.HasValue) cfg.DispenseZMm = dispenseZ;
                if (request.RoiLeft.HasValue) cfg.RoiLeft = request.RoiLeft;
                if (request.RoiTop.HasValue) cfg.RoiTop = request.RoiTop;
                if (request.RoiWidth.HasValue) cfg.RoiWidth = request.RoiWidth;
                if (request.RoiHeight.HasValue) cfg.RoiHeight = request.RoiHeight;
                if (request.PipetteVolumeUl.HasValue) cfg.PipetteVolumeUl = request.PipetteVolumeUl;
                if (!string.IsNullOrWhiteSpace(request.PipetteNeedleCode)) cfg.PipetteNeedleCode = request.PipetteNeedleCode.Trim();
                if (!string.IsNullOrWhiteSpace(request.PipetteLiquidClassCode)) cfg.PipetteLiquidClassCode = request.PipetteLiquidClassCode.Trim();
                cfg.Enabled = true;
                cfg.UpdatedAtUtc = DateTimeOffset.UtcNow;

                AddAudit(actor, cfg.Id, before, ToAudit(cfg), request.Reason);

                return new CommandExecutionResult<ReagentPositionConfigMutationResponse>(
                    new ReagentPositionConfigMutationResponse(true, request.CommandId, false, "ReagentPositionConfig", cfg.Id, "Reagent position config saved."),
                    "ReagentPositionConfig",
                    cfg.Id);
            },
            cancellationToken);
    }

    private static ReagentPositionConfig CreateDefault(string rackCode) => new() { RackCode = rackCode, Enabled = true };

    private static ReagentPositionConfigResponse ToResponse(ReagentPositionConfig c) => new(
        c.RackCode, c.CalibratedXMm, c.CalibratedYMm, c.SafeZMm, c.LiquidDetectZMm, c.AspirateEndZMm, c.DispenseZMm,
        c.RoiLeft, c.RoiTop, c.RoiWidth, c.RoiHeight,
        c.PipetteVolumeUl, c.PipetteNeedleCode, c.PipetteLiquidClassCode,
        c.Enabled, c.CreatedAtUtc, c.UpdatedAtUtc);

    private static object ToAudit(ReagentPositionConfig c) => new
    {
        c.RackCode, c.CalibratedXMm, c.CalibratedYMm, c.SafeZMm, c.LiquidDetectZMm, c.AspirateEndZMm, c.DispenseZMm,
        c.RoiLeft, c.RoiTop, c.RoiWidth, c.RoiHeight, c.Enabled
    };

    private void AddAudit(AuthenticatedUser actor, string entityId, object? before, object after, string reason)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = "reagent_position_config.save",
            EntityType = "ReagentPositionConfig",
            EntityId = entityId,
            Message = JsonSerializer.Serialize(new { before, after, reason = reason.Trim(), actor = actor.Username }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string NormalizeRackCode(string? rackCode)
    {
        var normalized = (rackCode ?? string.Empty).Trim().ToUpperInvariant();
        // 接受试剂位 R1-R40 和玻片位 A-01 ~ D-04
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^R([1-9]|[1-3]\d|40)$")
            || System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[A-D]-0[1-4]$"))
            return normalized;
        throw new BusinessRuleException("rack_code_invalid", "rackCode must be R1-R40 or A-01~D-04.", StatusCodes.Status400BadRequest);
    }

    private static decimal? ValidateRange(decimal? value, string fieldName, decimal minimum, decimal maximum)
    {
        if (!value.HasValue) return null;
        if (value.Value < minimum || value.Value > maximum)
            throw new BusinessRuleException($"{ToCode(fieldName)}_invalid", $"{fieldName} must be between {minimum} and {maximum}.", StatusCodes.Status400BadRequest);
        return value;
    }

    private static void RequireReason(string? reason)
    {
        var n = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(n)) throw new BusinessRuleException("reason_required", "reason is required.", StatusCodes.Status400BadRequest);
        if (n.Length > 2000) throw new BusinessRuleException("reason_too_long", "reason must be at most 2000 characters.", StatusCodes.Status400BadRequest);
    }

    private static string ToCode(string fieldName) => string.Concat(fieldName.Select((ch, i) =>
        char.IsUpper(ch) && i > 0 ? $"_{char.ToLowerInvariant(ch)}" : char.ToLowerInvariant(ch).ToString()));
}
