using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class EngineeringSessionService(
    StainerDbContext dbContext,
    PasswordHashService passwordHashService,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(60);
    private static readonly string[] ActiveRunStatuses =
    [
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted
    ];

    public async Task<EngineeringSessionResponse?> GetCurrentAsync(AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        await ExpireOldSessionsAsync(actor.UserId, cancellationToken);
        var sessions = await dbContext.EngineeringSessions
            .AsNoTracking()
            .Where(x => x.UserId == actor.UserId && x.Status == EngineeringSessionStatus.Active)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = sessions
            .Where(x => x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefault();
        return session is null ? null : ToResponse(session, false, "Engineering session is active.");
    }

    public async Task<EngineeringSessionResponse> StartAsync(
        StartEngineeringSessionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        RequireEngineer(actor);
        var reason = RequireValue(request.Reason, "reason");
        var target = RequireValue(request.Target, "target");

        var user = await dbContext.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == actor.UserId, cancellationToken)
            ?? throw new BusinessRuleException("user_not_found", "Current user was not found.", StatusCodes.Status401Unauthorized);
        if (!user.IsEnabled || !passwordHashService.Verify(request.Password, user.PasswordHash))
        {
            throw new BusinessRuleException("engineering_second_auth_failed", "Engineering secondary authentication failed.", StatusCodes.Status401Unauthorized);
        }

        var roles = user.UserRoles
            .Select(x => x.Role?.Code)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();
        if (!roles.Contains("engineer", StringComparer.OrdinalIgnoreCase) && !roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("engineering_session_forbidden", "Engineering session requires engineer or admin role.", StatusCodes.Status403Forbidden);
        }

        return await idempotencyService.RunAsync(
            request.CommandId,
            "engineering.session.start",
            request with { Password = "***" },
            actor,
            async () =>
            {
                await ExpireOldSessionsAsync(actor.UserId, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var duration = request.DurationMinutes is null
                    ? DefaultDuration
                    : TimeSpan.FromMinutes(Math.Clamp(request.DurationMinutes.Value, 1, (int)MaxDuration.TotalMinutes));
                var session = new EngineeringSession
                {
                    CommandId = request.CommandId.Trim(),
                    UserId = actor.UserId,
                    Username = actor.Username,
                    Status = EngineeringSessionStatus.Active,
                    Reason = reason,
                    Target = target,
                    DangerousOperationConfirmed = request.DangerousOperationConfirmed,
                    AuthenticatedAtUtc = now,
                    ExpiresAtUtc = now.Add(duration),
                    CreatedAtUtc = now
                };
                dbContext.EngineeringSessions.Add(session);
                AddAudit(actor, "engineering.session.start", "EngineeringSession", session.Id, new
                {
                    request.CommandId,
                    reason,
                    target,
                    request.DangerousOperationConfirmed,
                    session.ExpiresAtUtc
                });
                return new CommandExecutionResult<EngineeringSessionResponse>(
                    ToResponse(session, false, "Engineering session started."),
                    "EngineeringSession",
                    session.Id);
            },
            cancellationToken);
    }

    public Task<EngineeringSessionResponse> RevokeAsync(
        EndEngineeringSessionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        RequireEngineer(actor);
        var reason = RequireValue(request.Reason, "reason");
        var target = RequireValue(request.Target, "target");
        return idempotencyService.RunAsync(
            request.CommandId,
            "engineering.session.revoke",
            request,
            actor,
            async () =>
            {
                var sessions = await dbContext.EngineeringSessions
                    .Where(x => x.UserId == actor.UserId && x.Status == EngineeringSessionStatus.Active)
                    .ToListAsync(cancellationToken);
                var session = sessions
                    .Where(x => x.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    .OrderByDescending(x => x.ExpiresAtUtc)
                    .FirstOrDefault()
                    ?? throw new BusinessRuleException("engineering_session_not_active", "No active engineering session was found.", StatusCodes.Status409Conflict);
                session.Status = EngineeringSessionStatus.Revoked;
                session.RevokedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "engineering.session.revoke", "EngineeringSession", session.Id, new
                {
                    request.CommandId,
                    reason,
                    target
                });
                return new CommandExecutionResult<EngineeringSessionResponse>(
                    ToResponse(session, false, "Engineering session revoked."),
                    "EngineeringSession",
                    session.Id);
            },
            cancellationToken);
    }

    public async Task RequireWriteSessionAsync(
        AuthenticatedUser actor,
        string commandId,
        string reason,
        string target,
        bool dangerousOperationConfirmed,
        CancellationToken cancellationToken = default)
    {
        RequireEngineer(actor);
        commandId = RequireValue(commandId, "commandId");
        reason = RequireValue(reason, "reason");
        target = RequireValue(target, "target");

        if (await dbContext.CommandReceipts.AsNoTracking().AnyAsync(x => x.CommandId == commandId, cancellationToken))
        {
            return;
        }

        await ExpireOldSessionsAsync(actor.UserId, cancellationToken);
        var sessions = await dbContext.EngineeringSessions
            .Where(x => x.UserId == actor.UserId && x.Status == EngineeringSessionStatus.Active)
            .ToListAsync(cancellationToken);
        var session = sessions
            .Where(x => x.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefault();
        if (session is null)
        {
            throw new BusinessRuleException("engineering_session_required", "An active engineering session with secondary authentication is required.", StatusCodes.Status403Forbidden);
        }

        var activeRuns = await dbContext.MachineRuns
            .AsNoTracking()
            .Where(x => ActiveRunStatuses.Contains(x.Status))
            .Select(x => new { x.Id, x.RunCode, x.Status })
            .ToListAsync(cancellationToken);
        var activeRun = activeRuns
            .OrderByDescending(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (activeRun is not null && !dangerousOperationConfirmed && !session.DangerousOperationConfirmed)
        {
            throw new BusinessRuleException(
                "engineering_write_blocked_while_running",
                $"Machine run {activeRun.RunCode} is {activeRun.Status}; engineering writes are read-only by default while a run is active.",
                StatusCodes.Status409Conflict);
        }

        AddAudit(actor, "engineering.command.authorized", "EngineeringCommand", commandId, new
        {
            commandId,
            reason,
            target,
            sessionId = session.Id,
            dangerousOperationConfirmed = dangerousOperationConfirmed || session.DangerousOperationConfirmed,
            activeRun
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ExpireOldSessionsAsync(string userId, CancellationToken cancellationToken)
    {
        var expired = await dbContext.EngineeringSessions
            .Where(x => x.UserId == userId && x.Status == EngineeringSessionStatus.Active)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expiredSessions = expired.Where(x => x.ExpiresAtUtc <= now).ToList();
        foreach (var session in expiredSessions)
        {
            session.Status = EngineeringSessionStatus.Expired;
        }

        if (expiredSessions.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityType, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static EngineeringSessionResponse ToResponse(EngineeringSession session, bool replayed, string message)
    {
        return new EngineeringSessionResponse(
            session.Status == EngineeringSessionStatus.Active,
            session.CommandId,
            replayed,
            session.Id,
            session.Status,
            session.UserId,
            session.Username,
            session.Reason,
            session.Target,
            session.DangerousOperationConfirmed,
            session.AuthenticatedAtUtc,
            session.ExpiresAtUtc,
            message);
    }

    private static void RequireEngineer(AuthenticatedUser actor)
    {
        if (!actor.HasRole("engineer") && !actor.HasRole("admin"))
        {
            throw new BusinessRuleException("engineering_session_forbidden", "Engineering session requires engineer or admin role.", StatusCodes.Status403Forbidden);
        }
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
}
