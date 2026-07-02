using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DeviceCommunicationPersistenceService(
    StainerDbContext dbContext,
    IDeviceAdapter deviceAdapter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DeviceCommunicationRecord Begin(DeviceOperationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new DeviceCommunicationRecord
        {
            DeviceMode = deviceAdapter.Mode,
            AdapterName = deviceAdapter.Name,
            ModuleCode = request.ModuleCode,
            Action = request.Action,
            CommandId = request.Context.CommandId,
            CorrelationId = request.Context.CorrelationId,
            Actor = request.Context.Actor,
            Source = request.Context.Source,
            Status = DeviceCommunicationPersistenceStatus.Pending,
            Message = "Structured adapter result is pending persistence.",
            RequestJson = Truncate(JsonSerializer.Serialize(request.Parameters, JsonOptions), 16000),
            ResponseJson = "{}",
            PersistenceStatus = DeviceCommunicationPersistenceStatus.Pending,
            PersistenceFailureReason = "Awaiting the caller to persist the structured adapter result.",
            PersistenceLastAttemptAtUtc = now,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            CreatedAtUtc = now
        };
        dbContext.DeviceCommunicationRecords.Add(record);
        return record;
    }

    public void Complete(DeviceCommunicationRecord record, DeviceCommandResult result)
    {
        record.Status = result.Status;
        record.Ok = result.Ok;
        record.Acknowledged = result.Acknowledged;
        record.ErrorCode = result.ErrorCode;
        record.Message = Truncate(result.Message, 2000);
        record.ResponseJson = Truncate(JsonSerializer.Serialize(result.Data, JsonOptions), 16000);
        record.StartedAtUtc = result.StartedAtUtc;
        record.CompletedAtUtc = result.CompletedAtUtc;
        record.PersistenceStatus = DeviceCommunicationPersistenceStatus.Complete;
        record.PersistenceFailureReason = null;
        record.PersistenceAttemptCount++;
        record.PersistenceLastAttemptAtUtc = DateTimeOffset.UtcNow;
        record.PersistenceCompletedAtUtc = record.PersistenceLastAttemptAtUtc;
    }

    public async Task<bool> TryPersistCompletionAsync(
        DeviceCommunicationRecord record,
        DeviceCommandResult result,
        CancellationToken cancellationToken = default)
    {
        Complete(record, result);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsSqliteLock(ex))
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            dbContext.Entry(record).State = EntityState.Unchanged;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "device.communication.persistence_pending",
                EntityType = nameof(DeviceCommunicationRecord),
                EntityId = record.Id,
                Message = JsonSerializer.Serialize(new
                {
                    record.CommandId,
                    record.ModuleCode,
                    record.Action,
                    persistenceStatus = DeviceCommunicationPersistenceStatus.Pending,
                    reason = "SQLite lock prevented completion of the communication record; the durable pending record was retained.",
                    failedAtUtc
                }, JsonOptions),
                CreatedAtUtc = failedAtUtc
            });
            return false;
        }
    }

    private static bool IsSqliteLock(DbUpdateException exception)
    {
        if (exception.InnerException is SqliteException sqliteException)
        {
            return sqliteException.SqliteErrorCode is 5 or 6;
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
            || message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string? value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
