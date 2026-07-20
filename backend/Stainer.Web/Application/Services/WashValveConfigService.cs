using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

// 清洗/电磁阀配置服务：单行(ScopeKey=sample-wash) 读取 / upsert 持久化。
// 镜像 SerialConnectionConfigService 的幂等 + 审计模式。仅持久化工程配置，不驱动真实硬件。
public sealed class WashValveConfigService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WashValveConfigResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.WashValveConfigProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ScopeKey == WashValveConfigScopeKeys.SampleWash, cancellationToken);
        return ToResponse(profile ?? CreateDefault());
    }

    public Task<WashValveConfigMutationResponse> SaveAsync(
        SaveWashValveConfigRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "wash_valve_config.save",
            request,
            actor,
            async () =>
            {
                RequireReason(request.Reason);
                var washTempC = ValidateTemp(request.WashTempC);

                var profile = await dbContext.WashValveConfigProfiles
                    .SingleOrDefaultAsync(x => x.ScopeKey == WashValveConfigScopeKeys.SampleWash, cancellationToken);
                object? before = profile is null ? null : ToAudit(profile);
                if (profile is null)
                {
                    profile = new WashValveConfigProfile
                    {
                        ScopeKey = WashValveConfigScopeKeys.SampleWash,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    dbContext.WashValveConfigProfiles.Add(profile);
                }

                if (washTempC.HasValue) profile.WashTempC = washTempC;
                if (request.SolenoidOpen.HasValue) profile.SolenoidOpen = request.SolenoidOpen.Value;
                profile.Enabled = true;
                profile.UpdatedAtUtc = DateTimeOffset.UtcNow;

                AddAudit(actor, "wash_valve_config.save", "WashValveConfigProfile", profile.Id, before, ToAudit(profile), request.Reason);

                return new CommandExecutionResult<WashValveConfigMutationResponse>(
                    new WashValveConfigMutationResponse(true, request.CommandId, false, "WashValveConfigProfile", profile.Id, "Wash/valve config saved."),
                    "WashValveConfigProfile",
                    profile.Id);
            },
            cancellationToken);
    }

    private static WashValveConfigProfile CreateDefault() => new()
    {
        ScopeKey = WashValveConfigScopeKeys.SampleWash,
        WashTempC = null,
        SolenoidOpen = false,
        Enabled = true
    };

    private static WashValveConfigResponse ToResponse(WashValveConfigProfile p) => new(
        p.ScopeKey, p.WashTempC, p.SolenoidOpen, p.Enabled, p.CreatedAtUtc, p.UpdatedAtUtc);

    private static object ToAudit(WashValveConfigProfile p) => new { p.ScopeKey, p.WashTempC, p.SolenoidOpen, p.Enabled };

    private void AddAudit(AuthenticatedUser actor, string action, string entityType, string entityId, object? before, object after, string reason)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(new { before, after, reason = reason.Trim(), actor = actor.Username }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static decimal? ValidateTemp(decimal? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < 0m || value.Value > 200m)
        {
            throw new BusinessRuleException("wash_temp_c_invalid", "washTempC must be between 0 and 200.", StatusCodes.Status400BadRequest);
        }

        return value;
    }

    private static void RequireReason(string? reason)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException("reason_required", "reason is required.", StatusCodes.Status400BadRequest);
        }

        if (normalized.Length > 2000)
        {
            throw new BusinessRuleException("reason_too_long", "reason must be at most 2000 characters.", StatusCodes.Status400BadRequest);
        }
    }
}
