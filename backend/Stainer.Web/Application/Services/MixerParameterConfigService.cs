using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

// 混匀参数配置服务：按 drawerCode(A-D) 读取 / upsert 持久化。
// 镜像 PrecisionCalibrationConfigService 的幂等 + 审计模式。仅持久化工程配置，不触发真实动作。
public sealed class MixerParameterConfigService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MixerParameterResponse> GetAsync(string drawerCode, CancellationToken cancellationToken = default)
    {
        var key = NormalizeDrawerCode(drawerCode);
        var profile = await dbContext.MixerParameterProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DrawerCode == key, cancellationToken);
        return ToResponse(profile ?? CreateDefault(key));
    }

    public Task<MixerParameterMutationResponse> SaveAsync(
        string drawerCode,
        SaveMixerParameterRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        drawerCode = NormalizeDrawerCode(drawerCode);
        return idempotencyService.RunAsync(
            request.CommandId,
            "mixer_parameter.save",
            new { drawerCode, request },
            actor,
            async () =>
            {
                RequireReason(request.Reason);
                var origin = NormalizeOptionalValue(request.Origin, "origin", 64);
                var startStroke = ValidateIntRange(request.StartStroke, "startStroke", 0, 1000);
                var totalStroke = ValidateIntRange(request.TotalStroke, "totalStroke", 0, 5000);
                var topDwell = ValidateIntRange(request.TopDwellMs, "topDwellMs", 0, 10000);
                var bottomDwell = ValidateIntRange(request.BottomDwellMs, "bottomDwellMs", 0, 10000);
                var forwardSpeed = ValidateIntRange(request.ForwardSpeed, "forwardSpeed", 0, 10000);
                var reverseSpeed = ValidateIntRange(request.ReverseSpeed, "reverseSpeed", 0, 10000);
                var targetCycles = ValidateIntRange(request.TargetCycles, "targetCycles", 0, 1000);
                var remainingCycles = ValidateIntRange(request.RemainingCycles, "remainingCycles", 0, 1000);

                var profile = await dbContext.MixerParameterProfiles
                    .SingleOrDefaultAsync(x => x.DrawerCode == drawerCode, cancellationToken);
                object? before = profile is null ? null : ToAudit(profile);
                if (profile is null)
                {
                    profile = new MixerParameterProfile
                    {
                        DrawerCode = drawerCode,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    dbContext.MixerParameterProfiles.Add(profile);
                }

                if (!string.IsNullOrWhiteSpace(request.Origin)) profile.Origin = origin;
                if (startStroke.HasValue) profile.StartStroke = startStroke;
                if (totalStroke.HasValue) profile.TotalStroke = totalStroke;
                if (topDwell.HasValue) profile.TopDwellMs = topDwell;
                if (bottomDwell.HasValue) profile.BottomDwellMs = bottomDwell;
                if (forwardSpeed.HasValue) profile.ForwardSpeed = forwardSpeed;
                if (reverseSpeed.HasValue) profile.ReverseSpeed = reverseSpeed;
                if (targetCycles.HasValue) profile.TargetCycles = targetCycles;
                if (remainingCycles.HasValue) profile.RemainingCycles = remainingCycles;
                profile.Enabled = true;
                profile.UpdatedAtUtc = DateTimeOffset.UtcNow;

                AddAudit(actor, "mixer_parameter.save", "MixerParameterProfile", profile.Id, before, ToAudit(profile), request.Reason);

                return new CommandExecutionResult<MixerParameterMutationResponse>(
                    new MixerParameterMutationResponse(true, request.CommandId, false, "MixerParameterProfile", profile.Id, "Mixer parameter config saved."),
                    "MixerParameterProfile",
                    profile.Id);
            },
            cancellationToken);
    }

    private static MixerParameterProfile CreateDefault(string drawerCode) => new()
    {
        DrawerCode = drawerCode,
        Origin = null,
        StartStroke = 0,
        TotalStroke = 0,
        TopDwellMs = 0,
        BottomDwellMs = 0,
        ForwardSpeed = 0,
        ReverseSpeed = 0,
        TargetCycles = 0,
        RemainingCycles = 0,
        Enabled = true
    };

    private static MixerParameterResponse ToResponse(MixerParameterProfile p) => new(
        p.DrawerCode, p.Origin, p.StartStroke, p.TotalStroke, p.TopDwellMs, p.BottomDwellMs,
        p.ForwardSpeed, p.ReverseSpeed, p.TargetCycles, p.RemainingCycles, p.Enabled, p.CreatedAtUtc, p.UpdatedAtUtc);

    private static object ToAudit(MixerParameterProfile p) => new
    {
        p.DrawerCode, p.Origin, p.StartStroke, p.TotalStroke, p.TopDwellMs, p.BottomDwellMs,
        p.ForwardSpeed, p.ReverseSpeed, p.TargetCycles, p.RemainingCycles, p.Enabled
    };

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

    private static string NormalizeDrawerCode(string? drawerCode)
    {
        var normalized = (drawerCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return MixerDrawerCodes.A;
        }

        return Array.Exists(MixerDrawerCodes.All, x => string.Equals(x, normalized, StringComparison.Ordinal))
            ? normalized
            : throw new BusinessRuleException("drawer_code_invalid", $"drawerCode must be one of: {string.Join(", ", MixerDrawerCodes.All)}.", StatusCodes.Status400BadRequest);
    }

    private static int? ValidateIntRange(int? value, string fieldName, int minimum, int maximum)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var v = value.Value;
        if (v < minimum)
        {
            throw new BusinessRuleException($"{ToCode(fieldName)}_invalid", $"{fieldName} must be at least {minimum}.", StatusCodes.Status400BadRequest);
        }

        if (v > maximum)
        {
            throw new BusinessRuleException($"{ToCode(fieldName)}_invalid", $"{fieldName} must be at most {maximum}.", StatusCodes.Status400BadRequest);
        }

        return v;
    }

    private static string? NormalizeOptionalValue(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new BusinessRuleException($"{ToCode(fieldName)}_too_long", $"{fieldName} must be at most {maxLength} characters.", StatusCodes.Status400BadRequest);
        }

        return normalized;
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

    private static string ToCode(string fieldName)
    {
        return string.Concat(fieldName.Select((ch, index) =>
            char.IsUpper(ch) && index > 0 ? $"_{char.ToLowerInvariant(ch)}" : char.ToLowerInvariant(ch).ToString()));
    }
}
