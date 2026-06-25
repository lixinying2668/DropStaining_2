using Microsoft.EntityFrameworkCore;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class WorkflowAssignmentHistoryTests
{
    [Fact]
    public async Task Can_record_initial_selection()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var (batch, user) = await CreateBatchAndUserAsync(dbContext, "A");

        var history = NewHistory(batch, user, WorkflowAssignmentAction.InitialSelection, "cmd-initial", "corr-initial");
        history.NewExperimentType = StainingTaskType.He;
        history.NewWorkflowVersionId = "workflow-he-v1";
        history.NewWorkflowSnapshotJson = "{\"version\":\"he-v1\"}";
        dbContext.WorkflowAssignmentHistory.Add(history);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.WorkflowAssignmentHistory.SingleAsync(x => x.CommandId == "cmd-initial");
        Assert.Equal(batch.Id, persisted.ChannelBatchId);
        Assert.Equal(user.Id, persisted.OperatorUserId);
        Assert.Equal(WorkflowAssignmentAction.InitialSelection, persisted.ActionType);
        Assert.Equal("corr-initial", persisted.CorrelationId);
    }

    [Fact]
    public async Task Can_record_pre_start_change()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var (batch, user) = await CreateBatchAndUserAsync(dbContext, "B");

        var history = NewHistory(batch, user, WorkflowAssignmentAction.PreStartChange, "cmd-change", "corr-change");
        history.OldExperimentType = StainingTaskType.Ihc;
        history.OldWorkflowVersionId = "workflow-ihc-v1";
        history.OldWorkflowSnapshotJson = "{\"version\":\"ihc-v1\"}";
        history.NewExperimentType = StainingTaskType.Ihc;
        history.NewWorkflowVersionId = "workflow-ihc-v2";
        history.NewWorkflowSnapshotJson = "{\"version\":\"ihc-v2\"}";
        history.Reason = "operator changed script before start";
        dbContext.WorkflowAssignmentHistory.Add(history);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.WorkflowAssignmentHistory.SingleAsync(x => x.CommandId == "cmd-change");
        Assert.Equal(WorkflowAssignmentAction.PreStartChange, persisted.ActionType);
        Assert.Equal("workflow-ihc-v1", persisted.OldWorkflowVersionId);
        Assert.Equal("workflow-ihc-v2", persisted.NewWorkflowVersionId);
        Assert.Contains("before start", persisted.Reason);
    }

    [Fact]
    public async Task Can_record_locked_action()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var (batch, user) = await CreateBatchAndUserAsync(dbContext, "C");

        dbContext.WorkflowAssignmentHistory.Add(NewHistory(batch, user, WorkflowAssignmentAction.Locked, "cmd-locked", "corr-locked"));
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.WorkflowAssignmentHistory.SingleAsync(x => x.CommandId == "cmd-locked");
        Assert.Equal(WorkflowAssignmentAction.Locked, persisted.ActionType);
        Assert.Equal("Locked", persisted.ActionType);
    }

    [Fact]
    public async Task Channel_batch_history_can_be_read_in_time_order()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var (batch, user) = await CreateBatchAndUserAsync(dbContext, "D");
        var baseTime = DateTimeOffset.UtcNow;
        var locked = NewHistory(batch, user, WorkflowAssignmentAction.Locked, "cmd-3", "corr-order");
        locked.CreatedAtUtc = baseTime.AddMinutes(3);
        var initial = NewHistory(batch, user, WorkflowAssignmentAction.InitialSelection, "cmd-1", "corr-order");
        initial.CreatedAtUtc = baseTime.AddMinutes(1);
        var change = NewHistory(batch, user, WorkflowAssignmentAction.PreStartChange, "cmd-2", "corr-order");
        change.CreatedAtUtc = baseTime.AddMinutes(2);

        dbContext.WorkflowAssignmentHistory.AddRange(locked, initial, change);
        await dbContext.SaveChangesAsync();

        var ordered = (await dbContext.WorkflowAssignmentHistory
                .Where(x => x.ChannelBatchId == batch.Id)
                .ToListAsync())
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.ActionType)
            .ToList();

        Assert.Equal(
            [WorkflowAssignmentAction.InitialSelection, WorkflowAssignmentAction.PreStartChange, WorkflowAssignmentAction.Locked],
            ordered);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var user = await dbContext.Users.SingleAsync(x => x.Username == "operator");

        dbContext.WorkflowAssignmentHistory.Add(new WorkflowAssignmentHistory
        {
            ChannelBatchId = "missing-channel-batch",
            ActionType = WorkflowAssignmentAction.InitialSelection,
            OperatorUserId = user.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Reason = "invalid channel batch",
            CommandId = "cmd-invalid-fk",
            CorrelationId = "corr-invalid-fk"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task History_records_are_append_only()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var (batch, user) = await CreateBatchAndUserAsync(dbContext, "A");
        var history = NewHistory(batch, user, WorkflowAssignmentAction.InitialSelection, "cmd-append-only", "corr-append-only");
        dbContext.WorkflowAssignmentHistory.Add(history);
        await dbContext.SaveChangesAsync();

        history.Reason = "attempted update";
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());

        dbContext.Entry(history).State = EntityState.Unchanged;
        dbContext.WorkflowAssignmentHistory.Remove(history);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    private static WorkflowAssignmentHistory NewHistory(
        ChannelBatch batch,
        User user,
        string actionType,
        string commandId,
        string correlationId)
    {
        return new WorkflowAssignmentHistory
        {
            ChannelBatchId = batch.Id,
            ActionType = actionType,
            OperatorUserId = user.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Reason = "test history record",
            CommandId = commandId,
            CorrelationId = correlationId
        };
    }

    private static async Task<(ChannelBatch Batch, User User)> CreateBatchAndUserAsync(StainerDbContext dbContext, string drawerCode)
    {
        var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == drawerCode);
        var batch = new ChannelBatch
        {
            DrawerId = drawer.Id,
            DrawerCode = drawer.Code,
            Status = RuntimeLedgerStatus.Pending,
            WorkflowSnapshotJson = "{}",
            WorkflowSelectionStatus = WorkflowSelectionStatus.Unselected,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ChannelBatches.Add(batch);
        await dbContext.SaveChangesAsync();

        var user = await dbContext.Users.SingleAsync(x => x.Username == "operator");
        return (batch, user);
    }

    private static async Task<StainerDbContext> CreateMigratedContextAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-workflow-history-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var connectionString = $"Data Source={databasePath}";
        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor())
            .Options;
        var dbContext = new StainerDbContext(options);
        await DatabaseInitializer.InitializeAsync(dbContext);
        await dbContext.Database.MigrateAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();
        return dbContext;
    }
}
