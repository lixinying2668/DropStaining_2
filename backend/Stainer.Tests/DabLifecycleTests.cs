using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class DabLifecycleTests
{
    [Fact]
    public void Formula_uses_integer_microliters_and_fixed_ratio()
    {
        var formula = DabFormula.CalculateRequired(2);

        Assert.Equal(800, formula.TotalVolumeUl);
        Assert.Equal(40, formula.DabAVolumeUl);
        Assert.Equal(40, formula.DabBVolumeUl);
        Assert.Equal(720, formula.WaterVolumeUl);
        Assert.Throws<ArgumentOutOfRangeException>(() => DabFormula.CalculateRequired(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DabFormula.Calculate(801));
    }

    [Fact]
    public async Task Service_persists_formula_sources_lifecycle_usage_and_cleaning()
    {
        await using var factory = CreateFactory();
        var seed = await SeedDabInputsAsync(factory, 4);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var preferredDabA = await dbContext.ReagentBottles.SingleAsync(x => x.Id == seed.DabABottleId);
        preferredDabA.RemainingVolumeUl = 20;
        var overflowDabA = CreateBottle(preferredDabA.ReagentDefinitionId, "DAB-A");
        overflowDabA.RemainingVolumeUl = 20;
        overflowDabA.InitialVolumeUl = 20;
        dbContext.ReagentBottles.Add(overflowDabA);
        await dbContext.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
        var actor = new AuthenticatedUser(seed.OperatorUserId, "operator", "Operator", "operator", ["operator"]);

        var created = await service.CreateBatchAsync(new CreateDabBatchRequest(
            "cmd-dab-service-create",
            seed.TaskIds.Take(2).ToList(),
            seed.DabABottleId,
            seed.DabBBottleId,
            "M1"), actor);

        Assert.Equal(DabBatchStatus.PendingPreparation, created.Status);
        Assert.Equal(DabMixPositionStatus.Occupied, (await service.ListPositionsAsync()).Single(x => x.Code == "M1").Status);
        Assert.Equal(800, created.TotalRequiredVolumeUl);
        Assert.Equal((1, 1, 18), (created.DabARatioParts, created.DabBRatioParts, created.WaterRatioParts));
        Assert.Null(created.PreparedAtUtc);
        Assert.Equal(4, created.Reservations.Count);
        Assert.Equal(40, created.Reservations.Where(x => x.SourceRole == "DabA").Sum(x => x.ReservedVolumeUl));
        Assert.Equal(2, created.Reservations.Count(x => x.SourceRole == "DabA"));
        Assert.Equal(40, created.Reservations.Single(x => x.SourceRole == "DabB").ReservedVolumeUl);
        Assert.Equal(720, created.Reservations.Single(x => x.SourceRole == "Water").ReservedVolumeUl);

        var replayed = await service.CreateBatchAsync(new CreateDabBatchRequest(
            "cmd-dab-service-create",
            seed.TaskIds.Take(2).ToList(),
            seed.DabABottleId,
            seed.DabBBottleId,
            "M1"), actor);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.BatchId, replayed.BatchId);

        var preparing = await service.StartPreparationAsync(
            created.BatchId,
            new DabBatchCommandRequest("cmd-dab-service-start"),
            actor);
        Assert.Equal(DabBatchStatus.Preparing, preparing.Status);

        var completed = await service.CompletePreparationAsync(
            created.BatchId,
            new CompleteDabPreparationRequest("cmd-dab-service-complete", 800),
            actor);
        Assert.Equal(DabBatchStatus.Available, completed.Status);
        Assert.Equal(40, completed.DabAVolumeUl);
        Assert.Equal(40, completed.DabBVolumeUl);
        Assert.Equal(720, completed.WaterVolumeUl);
        Assert.Equal(completed.PreparedAtUtc!.Value.AddHours(3), completed.ExpiresAtUtc);
        Assert.Equal(TimeSpan.Zero, completed.PreparedAtUtc.Value.Offset);

        var partiallyUsed = await service.ConsumeAsync(
            created.BatchId,
            new ConsumeDabBatchRequest("cmd-dab-service-use-1", 200, seed.TaskIds[0]),
            actor);
        Assert.Equal(200, partiallyUsed.UsedVolumeUl);
        Assert.Equal(600, partiallyUsed.RemainingVolumeUl);

        var depleted = await service.ConsumeAsync(
            created.BatchId,
            new ConsumeDabBatchRequest("cmd-dab-service-use-2", 600),
            actor);
        Assert.Equal(DabBatchStatus.Depleted, depleted.Status);
        Assert.Equal(DabCleaningStatus.Required, depleted.CleaningStatus);
        Assert.Equal(DabMixPositionStatus.AwaitingCleaning, (await service.ListPositionsAsync()).Single(x => x.Code == "M1").Status);

        var cleaning = await service.StartCleaningAsync(
            created.BatchId,
            new DabBatchCommandRequest("cmd-dab-service-clean-start"),
            actor);
        Assert.Equal(DabBatchStatus.AwaitingCleaning, cleaning.Status);

        var cleaned = await service.ConfirmCleaningAsync(
            created.BatchId,
            new DabBatchCommandRequest("cmd-dab-service-clean-confirm"),
            actor);
        Assert.Equal(DabBatchStatus.Cleaned, cleaned.Status);
        Assert.Equal(DabCleaningStatus.Confirmed, cleaned.CleaningStatus);
        var released = (await service.ListPositionsAsync()).Single(x => x.Code == "M1");
        Assert.Equal(DabMixPositionStatus.Available, released.Status);
        Assert.Null(released.ActiveDabBatchId);

        var persisted = await dbContext.DabBatches
            .AsNoTracking()
            .Include(x => x.Tasks)
            .SingleAsync(x => x.Id == created.BatchId);
        Assert.Equal(2, persisted.Tasks.Count);
        Assert.Equal(800, persisted.ActualPreparedVolumeUl);
        Assert.Equal(800, persisted.UsedVolumeUl);
        Assert.Equal(2, await dbContext.DabBatchUsages.CountAsync(x => x.DabBatchId == created.BatchId));
        Assert.Equal(7, await dbContext.CommandReceipts.CountAsync(x => x.EntityId == created.BatchId));
        Assert.Equal(7, await dbContext.AuditLogs.CountAsync(x => x.EntityId == created.BatchId && x.EntityType == "DabBatch"));
        Assert.Equal(4, await dbContext.ReagentReservations.CountAsync(x => x.DabBatchId == created.BatchId));
        Assert.Equal(800, await dbContext.ReagentReservations.Where(x => x.DabBatchId == created.BatchId).SumAsync(x => x.ReservedVolumeUl));
        Assert.False(await dbContext.ReagentConsumptions.AnyAsync(x => x.ReagentCode == "DAB-A" || x.ReagentCode == "DAB-B"));
        Assert.Equal(20, (await dbContext.ReagentBottles.SingleAsync(x => x.Id == seed.DabABottleId)).RemainingVolumeUl);
        Assert.Equal(20, (await dbContext.ReagentBottles.SingleAsync(x => x.Id == overflowDabA.Id)).RemainingVolumeUl);
        Assert.Equal(100_000, (await dbContext.ReagentBottles.SingleAsync(x => x.Id == seed.DabBBottleId)).RemainingVolumeUl);
    }

    [Fact]
    public async Task Api_rejects_duplicate_or_exhausted_positions_and_invalid_volumes()
    {
        await using var factory = CreateFactory();
        var seed = await SeedDabInputsAsync(factory, 10);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dab/positions")).StatusCode);

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient);
        var positions = await operatorClient.GetFromJsonAsync<IReadOnlyList<DabMixPositionResponse>>("/api/dab/positions");
        Assert.NotNull(positions);
        Assert.Equal(8, positions!.Count);
        Assert.All(positions, x => Assert.Equal(DabMixPositionStatus.Available, x.Status));

        var operatorCreate = await operatorClient.PostAsJsonAsync("/api/dab/batches", new
        {
            commandId = "cmd-dab-api-operator-create",
            taskIds = new[] { seed.TaskIds[0] },
            dabAReagentBottleId = seed.DabABottleId,
            dabBReagentBottleId = seed.DabBBottleId,
            positionCode = "M1"
        });
        Assert.Equal(HttpStatusCode.Forbidden, operatorCreate.StatusCode);

        using var client = factory.CreateClient();
        await LoginAsync(client, "engineer");
        var first = await PostAsync<DabBatchResponse>(client, "/api/dab/batches", new
        {
            commandId = "cmd-dab-api-create-1",
            taskIds = new[] { seed.TaskIds[0] },
            dabAReagentBottleId = seed.DabABottleId,
            dabBReagentBottleId = seed.DabBBottleId,
            positionCode = "M1"
        });
        Assert.Equal(600, first.TotalRequiredVolumeUl);
        var loaded = await client.GetFromJsonAsync<DabBatchResponse>($"/api/dab/batches/{first.BatchId}");
        Assert.NotNull(loaded);
        Assert.Equal(seed.DabABottleId, loaded!.DabAReagentBottleId);
        Assert.Equal(seed.DabBBottleId, loaded.DabBReagentBottleId);
        Assert.Equal(new[] { seed.TaskIds[0] }, loaded.TaskIds);
        Assert.Equal(600, loaded.Reservations.Sum(x => x.ReservedVolumeUl));

        var duplicate = await client.PostAsJsonAsync("/api/dab/batches", new
        {
            commandId = "cmd-dab-api-duplicate-position",
            taskIds = new[] { seed.TaskIds[1] },
            dabAReagentBottleId = seed.DabABottleId,
            dabBReagentBottleId = seed.DabBBottleId,
            positionCode = "M1"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("dab_position_occupied", (await duplicate.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);

        _ = await PostAsync<DabBatchResponse>(client, $"/api/dab/batches/{first.BatchId}/preparation/start", new
        {
            commandId = "cmd-dab-api-start"
        });
        var invalidPrepared = await client.PostAsJsonAsync($"/api/dab/batches/{first.BatchId}/preparation/complete", new
        {
            commandId = "cmd-dab-api-invalid-prepared",
            actualPreparedVolumeUl = 599
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPrepared.StatusCode);
        Assert.Equal("dab_prepared_volume_mismatch", (await invalidPrepared.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);

        _ = await PostAsync<DabBatchResponse>(client, $"/api/dab/batches/{first.BatchId}/preparation/complete", new
        {
            commandId = "cmd-dab-api-complete",
            actualPreparedVolumeUl = 600
        });
        var invalidUsage = await client.PostAsJsonAsync($"/api/dab/batches/{first.BatchId}/consume", new
        {
            commandId = "cmd-dab-api-invalid-use",
            volumeUl = 601
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUsage.StatusCode);
        Assert.Equal("dab_usage_volume_invalid", (await invalidUsage.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);

        for (var index = 1; index < 8; index++)
        {
            var allocated = await PostAsync<DabBatchResponse>(client, "/api/dab/batches", new
            {
                commandId = $"cmd-dab-api-fill-{index}",
                taskIds = new[] { seed.TaskIds[index] },
                dabAReagentBottleId = seed.DabABottleId,
                dabBReagentBottleId = seed.DabBBottleId
            });
            Assert.Equal($"M{index + 1}", allocated.PositionCode);
        }

        var exhausted = await client.PostAsJsonAsync("/api/dab/batches", new
        {
            commandId = "cmd-dab-api-all-full",
            taskIds = new[] { seed.TaskIds[8] },
            dabAReagentBottleId = seed.DabABottleId,
            dabBReagentBottleId = seed.DabBBottleId
        });
        Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);
        Assert.Equal("dab_positions_unavailable", (await exhausted.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-dab-api-duplicate-position"));
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-dab-api-invalid-prepared"));
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-dab-api-invalid-use"));
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-dab-api-all-full"));
        Assert.Equal(8, await dbContext.DabBatches.CountAsync());
    }

    [Fact]
    public async Task Service_enforces_expiry_and_failure_transitions()
    {
        await using var factory = CreateFactory();
        var seed = await SeedDabInputsAsync(factory, 2);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var actor = new AuthenticatedUser(seed.OperatorUserId, "operator", "Operator", "operator", ["operator"]);

        var expiring = await service.CreateBatchAsync(new CreateDabBatchRequest(
            "cmd-dab-expire-create",
            [seed.TaskIds[0]],
            seed.DabABottleId,
            seed.DabBBottleId,
            "M1"), actor);
        _ = await service.StartPreparationAsync(expiring.BatchId, new DabBatchCommandRequest("cmd-dab-expire-start"), actor);
        _ = await service.CompletePreparationAsync(expiring.BatchId, new CompleteDabPreparationRequest("cmd-dab-expire-complete", 600), actor);

        var early = await Assert.ThrowsAsync<BusinessRuleException>(() => service.MarkExpiredAsync(
            expiring.BatchId,
            new DabBatchCommandRequest("cmd-dab-expire-early"),
            actor));
        Assert.Equal("dab_batch_not_expired", early.Code);
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-dab-expire-early"));

        dbContext.ChangeTracker.Clear();
        var tracked = await dbContext.DabBatches.SingleAsync(x => x.Id == expiring.BatchId);
        tracked.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();
        var expired = await service.MarkExpiredAsync(
            expiring.BatchId,
            new DabBatchCommandRequest("cmd-dab-expire-mark"),
            actor);
        Assert.Equal(DabBatchStatus.Expired, expired.Status);
        Assert.Equal(DabCleaningStatus.Required, expired.CleaningStatus);

        var failedBatch = await service.CreateBatchAsync(new CreateDabBatchRequest(
            "cmd-dab-fail-create",
            [seed.TaskIds[1]],
            seed.DabABottleId,
            seed.DabBBottleId,
            "M2"), actor);
        var failed = await service.FailAsync(
            failedBatch.BatchId,
            new FailDabBatchRequest("cmd-dab-fail", "mixing verification failed"),
            actor);
        Assert.Equal(DabBatchStatus.Failed, failed.Status);
        Assert.Equal(DabCleaningStatus.NeedsManualResolution, failed.CleaningStatus);
        Assert.Equal(DabMixPositionStatus.NeedsManualResolution, (await service.ListPositionsAsync()).Single(x => x.Code == "M2").Status);
        Assert.All(failed.Reservations, x => Assert.Equal(ReagentReservationStatus.Released, x.Status));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), "stainer-dab-lifecycle-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "stainer.db");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = Path.Combine(root, "machine-executor.lock"),
            ["Safety:LogDirectory"] = Path.Combine(root, "logs"),
            ["Device:Mode"] = DeviceModes.Mock,
            ["Device:RealHealthCheckComplete"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        };
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                foreach (var setting in settings)
                {
                    builder.UseSetting(setting.Key, setting.Value);
                }
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            });
    }

    private static async Task<DabSeed> SeedDabInputsAsync(WebApplicationFactory<Program> factory, int taskCount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var workflow = await dbContext.WorkflowVersions
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.Steps)
            .SingleAsync(x => x.DefaultExperimentType == StainingTaskType.Ihc && x.Status == WorkflowVersionStatus.Published);
        Assert.Contains(workflow.Steps, x => x.ActionType == "Dab");
        var slots = await dbContext.PhysicalSlots.OrderBy(x => x.Code).Take(taskCount).ToListAsync();
        Assert.Equal(taskCount, slots.Count);

        var tasks = Enumerable.Range(0, taskCount).Select(index => new StainingTask
        {
            TaskCode = $"DAB-TASK-{index + 1:00}-{Guid.NewGuid():N}",
            TaskType = StainingTaskType.Ihc,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = slots[index].Id,
            WorkflowDefinitionId = workflow.WorkflowDefinitionId,
            WorkflowVersionId = workflow.Id,
            WorkflowSnapshotJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        }).ToList();
        dbContext.StainingTasks.AddRange(tasks);

        var definition = await dbContext.ReagentDefinitions.SingleAsync(x => x.ReagentCode == "DAB");
        var dabA = CreateBottle(definition.Id, "DAB-A");
        var dabB = CreateBottle(definition.Id, "DAB-B");
        dbContext.ReagentBottles.AddRange(dabA, dabB);
        await dbContext.SaveChangesAsync();

        var operatorUserId = await dbContext.Users.Where(x => x.Username == "operator").Select(x => x.Id).SingleAsync();
        return new DabSeed(tasks.Select(x => x.Id).ToList(), dabA.Id, dabB.Id, operatorUserId);
    }

    private static ReagentBottle CreateBottle(string definitionId, string code)
    {
        return new ReagentBottle
        {
            ReagentDefinitionId = definitionId,
            FullBarcode = $"{code}-{Guid.NewGuid():N}",
            ReagentCode = code,
            ProductionBatchNo = "DAB-TEST",
            SerialNo = Guid.NewGuid().ToString("N"),
            InitialVolumeUl = 100_000,
            RemainingVolumeUl = 100_000,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = "Available",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task LoginAsync(HttpClient client, string role = "operator")
    {
        var response = await client.PostAsJsonAsync("/api/login", new
        {
            username = role,
            password = "123456",
            role
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result!;
    }

    private sealed record DabSeed(
        IReadOnlyList<string> TaskIds,
        string DabABottleId,
        string DabBBottleId,
        string OperatorUserId);

    private sealed record ErrorResponse(string Code, string Detail);
}
