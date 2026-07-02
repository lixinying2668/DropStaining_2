using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Health;

public sealed class DatabaseMaintenanceService(
    StainerDbContext dbContext,
    DatabaseHealthChecker healthChecker,
    CommandIdempotencyService idempotencyService,
    SafetyLogWriter safetyLogWriter,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DatabaseMaintenanceReportResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var health = await healthChecker.CheckAsync(cancellationToken);
        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).Count();
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        var ok = health.ForeignKeysEnabled
            && string.Equals(health.JournalMode, "wal", StringComparison.OrdinalIgnoreCase)
            && health.BusyTimeoutMilliseconds >= DatabaseInitializer.MinimumBusyTimeoutMilliseconds
            && health.CanReadWrite
            && health.IntegrityOk
            && pending == 0;
        return new DatabaseMaintenanceReportResponse(
            ok,
            health.DatabasePath,
            health.SqliteVersion,
            health.ForeignKeysEnabled,
            health.JournalMode,
            health.BusyTimeoutMilliseconds,
            health.CanReadWrite,
            health.IntegrityOk,
            applied,
            pending,
            ok ? "Database health checks passed." : "Database health checks failed or migrations are pending.");
    }

    public async Task<DatabaseBackupResponse> BackupAsync(DatabaseBackupRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        var commandId = RequireCommandId(request.CommandId);
        const string operation = "database.backup";
        var requestHash = HashRequest(operation, request);
        var existing = await dbContext.CommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Operation != operation || existing.RequestHash != requestHash)
            {
                throw new BusinessRuleException("command_conflict", "commandId already exists for a different request.", StatusCodes.Status409Conflict);
            }

            var existingResponse = JsonSerializer.Deserialize<DatabaseBackupResponse>(existing.ResponseJson, JsonOptions);
            if (existingResponse is null)
            {
                throw new BusinessRuleException("command_replay_failed", "Stored command response could not be replayed.", StatusCodes.Status409Conflict);
            }

            return existingResponse with { Replayed = true };
        }

        var before = await CheckAsync(cancellationToken);
        if (!before.IntegrityOk || !before.CanReadWrite)
        {
            await SaveBackupAlarmAsync(
                "database_backup_health_failed",
                "Database backup was blocked because health or integrity checks failed.",
                cancellationToken);
            throw new BusinessRuleException("database_health_failed", "Database backup requires passing health and integrity checks.", StatusCodes.Status409Conflict);
        }

        var backupDirectory = ResolveBackupDirectory(request.OutputDirectory);
        Directory.CreateDirectory(backupDirectory);
        var backupStem = $"stainer-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z-{Guid.NewGuid():N}";
        var backupPath = Path.Combine(backupDirectory, $"{backupStem}.db");
        var attemptDirectory = ResolveAttemptDirectory(backupStem);
        BackupSqliteResult backup;
        bool afterIntegrityOk;
        try
        {
            backup = await BackupSqliteAsync(before.DatabasePath, backupPath, attemptDirectory, cancellationToken);
            backupPath = backup.BackupPath;
            afterIntegrityOk = await CheckBackupIntegrityAsync(backupPath, cancellationToken);
        }
        catch (Exception primaryEx) when (primaryEx is SqliteException or IOException or UnauthorizedAccessException)
        {
            var failureMessage = $"Database backup failed. Source={before.DatabasePath}; Target={backupPath}; Reason={primaryEx.Message}";
            await SaveBackupAlarmAsync(
                "database_backup_failed",
                failureMessage,
                cancellationToken);
            throw new BusinessRuleException("database_backup_failed", failureMessage, StatusCodes.Status500InternalServerError);
        }

        if (!afterIntegrityOk)
        {
            await SaveBackupAlarmAsync(
                "database_backup_integrity_failed",
                "Database backup was created but failed integrity check.",
                cancellationToken);
            throw new BusinessRuleException("database_backup_integrity_failed", "Database backup failed integrity check.", StatusCodes.Status500InternalServerError);
        }

        var completionMessage = backup.AttemptMessages.Count == 0
            ? "Database backup completed."
            : $"Database backup completed via {backup.Method} after fallback. FinalBackup={backupPath}. {string.Join(" | ", backup.AttemptMessages)}";

        var response = new DatabaseBackupResponse(
            true,
            commandId,
            false,
            backupPath,
            before.IntegrityOk,
            afterIntegrityOk,
            DateTimeOffset.UtcNow,
            completionMessage);

        var now = DateTimeOffset.UtcNow;
        var actorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.CommandReceipts.Add(new CommandReceipt
        {
            CommandId = commandId,
            Operation = operation,
            RequestHash = requestHash,
            Status = "Completed",
            ResponseJson = JsonSerializer.Serialize(response, JsonOptions),
            ActorUserId = actorUserId,
            EntityType = "DatabaseBackup",
            EntityId = backupPath,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = "database.backup",
            EntityType = "DatabaseBackup",
            EntityId = backupPath,
            Message = JsonSerializer.Serialize(new { commandId, backupPath, before.DatabasePath, backup.Method, backup.AttemptMessages, attemptDirectory = backup.AttemptDirectory, backup.AttemptCleanup }, JsonOptions),
            CreatedAtUtc = now
        });
        if (backup.AttemptMessages.Count > 0)
        {
            dbContext.Alarms.Add(new Alarm
            {
                Code = "database_backup_degraded",
                Severity = "Warning",
                Message = $"Database backup completed via {backup.Method} after earlier methods failed. FinalBackup={backupPath}; FailedAttempts={string.Join(" | ", backup.AttemptMessages)}",
                Status = "Active",
                CreatedAtUtc = now
            });
            dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                Action = "database.backup_degraded",
                EntityType = "DatabaseBackup",
                EntityId = backupPath,
                Message = JsonSerializer.Serialize(new { commandId, backupPath, backup.Method, backup.AttemptMessages }, JsonOptions),
                CreatedAtUtc = now
            });
        }

        if (!backup.AttemptCleanup.Succeeded)
        {
            dbContext.Alarms.Add(new Alarm
            {
                Code = "database_backup_attempt_cleanup_failed",
                Severity = "Warning",
                Message = $"Database backup succeeded but attempt cleanup needs maintenance. AttemptDirectory={backup.AttemptDirectory}; Reason={backup.AttemptCleanup.Message}; FinalBackup={backupPath}",
                Status = "Active",
                CreatedAtUtc = now
            });
            dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                Action = "database.backup_attempt_cleanup_failed",
                EntityType = "DatabaseBackup",
                EntityId = backupPath,
                Message = JsonSerializer.Serialize(new { commandId, backupPath, backup.AttemptDirectory, backup.AttemptCleanup }, JsonOptions),
                CreatedAtUtc = now
            });
        }

        await ResolveHistoricalBackupFailureAlarmsAsync(commandId, backupPath, actorUserId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await safetyLogWriter.WriteAsync(
            "runtime",
            backup.AttemptMessages.Count == 0 ? "Information" : "Warning",
            completionMessage,
            new SafetyLogContext(CommandId: commandId, DeviceMode: "Mock", Actor: actor.Username, Source: "DatabaseMaintenanceService"),
            cancellationToken: cancellationToken);

        return response;
    }

    public Task<DatabaseRestoreResponse> RequestRestoreAsync(DatabaseRestoreRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "database.restore_request",
            request,
            actor,
            async () =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    throw new BusinessRuleException("reason_required", "Database restore requires a reason.", StatusCodes.Status400BadRequest);
                }

                var backupPath = Path.GetFullPath(request.BackupPath);
                if (!File.Exists(backupPath))
                {
                    throw new BusinessRuleException("backup_not_found", "Backup file was not found.", StatusCodes.Status404NotFound);
                }

                var integrityOk = await CheckBackupIntegrityAsync(backupPath, cancellationToken);
                if (!integrityOk)
                {
                    throw new BusinessRuleException("backup_integrity_failed", "Backup integrity check failed.", StatusCodes.Status409Conflict);
                }

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                    Action = "database.restore_requested",
                    EntityType = "DatabaseBackup",
                    EntityId = backupPath,
                    Message = JsonSerializer.Serialize(new { commandId = request.CommandId, backupPath, request.Reason, restartRequired = true }, JsonOptions),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await safetyLogWriter.WriteAsync(
                    "runtime",
                    "Warning",
                    "Database restore requested. Stop the service and restore offline before restart.",
                    new SafetyLogContext(CommandId: request.CommandId, Actor: actor.Username, Source: "DatabaseMaintenanceService"),
                    cancellationToken: cancellationToken);
                return new CommandExecutionResult<DatabaseRestoreResponse>(
                    new DatabaseRestoreResponse(
                        true,
                        request.CommandId,
                        false,
                        backupPath,
                        true,
                        true,
                        "Restore request was audited. Stop the service and restore the verified backup offline before restart."),
                    "DatabaseBackup",
                    backupPath);
            },
            cancellationToken);
    }

    private string ResolveBackupDirectory(string? requested)
    {
        var configured = requested ?? configuration["Database:BackupDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "data", "backups"));
    }

    private async Task SaveBackupAlarmAsync(string code, string message, CancellationToken cancellationToken, string severity = "Critical")
    {
        dbContext.Alarms.Add(new Alarm
        {
            Code = code,
            Severity = severity,
            Message = message,
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ResolveHistoricalBackupFailureAlarmsAsync(
        string commandId,
        string backupPath,
        string? actorUserId,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        var alarms = await dbContext.Alarms
            .Include(x => x.Actions)
            .Where(x => x.Code == "database_backup_failed" && x.Status != "Resolved")
            .ToListAsync(cancellationToken);

        foreach (var alarm in alarms)
        {
            var previousStatus = alarm.Status;
            if (alarm.Status == "Active")
            {
                alarm.Actions.Add(new AlarmAction
                {
                    ActorUserId = actorUserId,
                    Action = "Acknowledged",
                    Message = $"Verified backup succeeded before closure. CommandId={commandId}; BackupPath={backupPath}",
                    CreatedAtUtc = resolvedAt
                });
            }

            alarm.Status = "Resolved";
            alarm.ClearedAtUtc = resolvedAt;
            alarm.Actions.Add(new AlarmAction
            {
                ActorUserId = actorUserId,
                Action = "Resolved",
                Message = $"Closed after verified backup succeeded. CommandId={commandId}; BackupPath={backupPath}",
                CreatedAtUtc = resolvedAt
            });
            dbContext.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                Action = "alarm.resolve",
                EntityType = "Alarm",
                EntityId = alarm.Id,
                Message = JsonSerializer.Serialize(new { commandId, backupPath, alarm.Code, previousStatus }, JsonOptions),
                CreatedAtUtc = resolvedAt
            });
        }

        return alarms.Count;
    }

    private static string ResolveAttemptDirectory(string backupStem)
    {
        var safeStem = string.Concat(backupStem.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "stainer-backup-attempts", safeStem));
    }

    private static async Task<BackupSqliteResult> BackupSqliteAsync(string databasePath, string backupPath, string attemptDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(attemptDirectory);
        var failedCandidates = new List<string>();
        var attemptMessages = new List<string>();
        var attemptBackupPath = Path.Combine(attemptDirectory, Path.GetFileName(backupPath));
        try
        {
            await BackupSqliteWithBackupApiAsync(databasePath, attemptBackupPath, cancellationToken);
            PromoteBackup(attemptBackupPath, backupPath);
            var cleanup = await CleanupAttemptDirectoryWithRetryAsync(attemptDirectory, cancellationToken);
            return new BackupSqliteResult(backupPath, "sqlite-backup-api", attemptMessages, attemptDirectory, cleanup);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            failedCandidates.Add(attemptBackupPath);
            attemptMessages.Add($"sqlite-backup-api failed for {attemptBackupPath}: {ex.Message}");
            TryCleanupPartialBackup(attemptBackupPath);
            var vacuumAttemptPath = Path.Combine(
                attemptDirectory,
                $"{Path.GetFileNameWithoutExtension(backupPath)}-vacuum-{Guid.NewGuid():N}.db");
            var vacuumBackupPath = Path.Combine(
                Path.GetDirectoryName(backupPath) ?? ".",
                Path.GetFileName(vacuumAttemptPath));
            try
            {
                await BackupSqliteWithVacuumIntoAsync(databasePath, vacuumAttemptPath, cancellationToken);
                PromoteBackup(vacuumAttemptPath, vacuumBackupPath);
                var cleanup = await CleanupAttemptDirectoryWithRetryAsync(attemptDirectory, cancellationToken);
                return new BackupSqliteResult(vacuumBackupPath, "vacuum-into", attemptMessages, attemptDirectory, cleanup);
            }
            catch (Exception vacuumEx) when (vacuumEx is SqliteException or IOException or UnauthorizedAccessException)
            {
                failedCandidates.Add(vacuumAttemptPath);
                attemptMessages.Add($"vacuum-into failed for {vacuumAttemptPath}: {vacuumEx.Message}");
                TryCleanupPartialBackup(vacuumAttemptPath);
                var copyAttemptPath = Path.Combine(
                    attemptDirectory,
                    $"{Path.GetFileNameWithoutExtension(backupPath)}-copy-{Guid.NewGuid():N}.db");
                var copyPath = Path.Combine(
                    Path.GetDirectoryName(backupPath) ?? ".",
                    Path.GetFileName(copyAttemptPath));
                await CheckpointSqliteAsync(databasePath, cancellationToken);
                await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
                await using (var destination = new FileStream(copyAttemptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                PromoteBackup(copyAttemptPath, copyPath);
                var cleanup = await CleanupAttemptDirectoryWithRetryAsync(attemptDirectory, cancellationToken);
                return new BackupSqliteResult(copyPath, "checkpoint-copy", attemptMessages, attemptDirectory, cleanup);
            }
        }
    }

    private static void PromoteBackup(string attemptPath, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? ".");
        File.Move(attemptPath, backupPath);
    }

    private static async Task BackupSqliteWithBackupApiAsync(string databasePath, string backupPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(BuildSqliteConnectionString(databasePath));
        await using var destination = new SqliteConnection(BuildSqliteConnectionString(backupPath));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task BackupSqliteWithVacuumIntoAsync(string databasePath, string backupPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(BuildSqliteConnectionString(databasePath));
        await source.OpenAsync(cancellationToken);
        await using (var timeout = source.CreateCommand())
        {
            timeout.CommandText = $"PRAGMA busy_timeout = {DatabaseInitializer.MinimumBusyTimeoutMilliseconds};";
            await timeout.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = source.CreateCommand();
        command.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}';";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CheckpointSqliteAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(BuildSqliteConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);
        await using var timeout = connection.CreateCommand();
        timeout.CommandText = $"PRAGMA busy_timeout = {DatabaseInitializer.MinimumBusyTimeoutMilliseconds};";
        await timeout.ExecuteNonQueryAsync(cancellationToken);
        await using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpoint.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryCleanupPartialBackup(string backupPath)
    {
        try
        {
            foreach (var path in new[] { backupPath, $"{backupPath}-journal", $"{backupPath}-wal", $"{backupPath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task CleanupPartialBackupsWithRetryAsync(IEnumerable<string> backupPaths, CancellationToken cancellationToken)
    {
        foreach (var path in backupPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (TryCleanupPartialBackup(path))
                {
                    break;
                }

                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static async Task<BackupAttemptCleanupResult> CleanupAttemptDirectoryWithRetryAsync(string attemptDirectory, CancellationToken cancellationToken)
    {
        var fullAttemptDirectory = Path.GetFullPath(attemptDirectory);
        var attemptRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "stainer-backup-attempts"));
        if (!IsWithinDirectory(fullAttemptDirectory, attemptRoot))
        {
            return new BackupAttemptCleanupResult(false, $"Refused to clean attempt directory outside temp root: {fullAttemptDirectory}");
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!Directory.Exists(fullAttemptDirectory))
                {
                    return new BackupAttemptCleanupResult(true, "Attempt directory was already absent.");
                }

                Directory.Delete(fullAttemptDirectory, recursive: true);
                return new BackupAttemptCleanupResult(true, "Attempt directory cleaned.");
            }
            catch (IOException ex)
            {
                if (attempt == 9)
                {
                    return new BackupAttemptCleanupResult(false, ex.Message);
                }

                await Task.Delay(250, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                if (attempt == 9)
                {
                    return new BackupAttemptCleanupResult(false, ex.Message);
                }

                await Task.Delay(250, cancellationToken);
            }
        }

        return new BackupAttemptCleanupResult(false, "Attempt directory cleanup did not complete.");
    }

    private static bool IsWithinDirectory(string childPath, string parentPath)
    {
        var normalizedParent = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedChild = childPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckBackupIntegrityAsync(string backupPath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(BuildSqliteConnectionString(backupPath, readOnly: true));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(Convert.ToString(result), "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSqliteConnectionString(string databasePath, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        };
        if (readOnly)
        {
            builder.Mode = SqliteOpenMode.ReadOnly;
        }

        return builder.ToString();
    }

    private static string RequireCommandId(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new BusinessRuleException("command_id_required", "commandId is required.", StatusCodes.Status400BadRequest);
        }

        return commandId.Trim();
    }

    private static string HashRequest(string operation, object request)
    {
        var json = JsonSerializer.Serialize(new { operation, request }, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private sealed record BackupSqliteResult(
        string BackupPath,
        string Method,
        IReadOnlyList<string> AttemptMessages,
        string AttemptDirectory,
        BackupAttemptCleanupResult AttemptCleanup);

    private sealed record BackupAttemptCleanupResult(
        bool Succeeded,
        string Message);
}
