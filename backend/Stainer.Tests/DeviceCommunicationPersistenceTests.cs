using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

public sealed class DeviceCommunicationPersistenceTests
{
    [Fact]
    public async Task Sqlite_lock_keeps_durable_pending_record_and_does_not_repeat_adapter_command()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stainer-device-communication-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "stainer.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=1";
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var dbContext = new StainerDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var stateStore = new MockDeviceStateStore();
        var adapter = new MockDeviceAdapter(stateStore);
        var persistence = new DeviceCommunicationPersistenceService(dbContext, adapter);
        var request = new DeviceOperationRequest(
            new DeviceCommandContext("cmd-communication-lock", "corr-communication-lock", "engineer", "DeviceCommunicationPersistenceTests"),
            DeviceModules.Controller,
            "locked-command",
            new Dictionary<string, object?> { ["target"] = "controller" });

        var record = persistence.Begin(request);
        await dbContext.SaveChangesAsync();
        var versionBeforeCommand = stateStore.Snapshot().Version;
        var result = await adapter.ExecuteWorkflowActionAsync(request);
        var versionAfterCommand = stateStore.Snapshot().Version;
        Assert.True(versionAfterCommand > versionBeforeCommand);

        await using (var lockConnection = new SqliteConnection(connectionString))
        {
            await lockConnection.OpenAsync();
            await using var lockCommand = lockConnection.CreateCommand();
            lockCommand.CommandText = "BEGIN EXCLUSIVE;";
            await lockCommand.ExecuteNonQueryAsync();

            Assert.False(await persistence.TryPersistCompletionAsync(record, result));

            await using var unlockCommand = lockConnection.CreateCommand();
            unlockCommand.CommandText = "ROLLBACK;";
            await unlockCommand.ExecuteNonQueryAsync();
        }

        await dbContext.SaveChangesAsync();
        Assert.Equal(versionAfterCommand, stateStore.Snapshot().Version);

        await using var verifyContext = new StainerDbContext(options);
        var persisted = await verifyContext.DeviceCommunicationRecords.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(DeviceCommunicationPersistenceStatus.Pending, persisted.PersistenceStatus);
        Assert.Equal("{}", persisted.ResponseJson);
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x =>
            x.Action == "device.communication.persistence_pending"
            && x.EntityId == record.Id
            && x.Message.Contains("SQLite lock")));
    }
}
