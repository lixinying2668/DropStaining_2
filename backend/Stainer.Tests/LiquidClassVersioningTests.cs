using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class LiquidClassVersioningTests
{
    [Fact]
    public async Task Draft_publish_enable_records_validation_differences_audit_and_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var profiles = await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes");
        var baseline = Assert.Single(profiles!, x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId is not null);

        var request = new
        {
            commandId = "cmd-liquid-draft-001",
            code = baseline.Code,
            name = "Adjusted general liquid class",
            aspirateSpeedUlPerSecond = 120,
            dispenseSpeedUlPerSecond = 130,
            leadingAirGapUl = 6,
            trailingAirGapUl = 7,
            excessVolumeUl = 2,
            preWetCycles = 2,
            mixCycles = 1,
            isEnabled = false,
            reason = "verified engineering adjustment",
            sourceVersionId = baseline.EnabledVersionId,
            versionLabel = "2",
            liquidDetectionEnabled = true,
            liquidDetectionSensitivityPercent = 60,
            liquidDetectionSpeedUmPerSecond = 1200,
            aspirateDelayMs = 150,
            dispenseDelayMs = 160,
            blowoutVolumeUl = 12,
            blowoutDelayMs = 170,
            volumeAdjustmentUl = 2
        };
        var draft = await PostAsync<LiquidClassVersionMutationResponse>(client, "/api/engineering/liquid-classes", request);
        var replay = await PostAsync<LiquidClassVersionMutationResponse>(client, "/api/engineering/liquid-classes", request);
        Assert.Equal(LiquidClassVersionStatus.Draft, draft.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(draft.LiquidClassVersionId, replay.LiquidClassVersionId);

        var published = await PostAsync<LiquidClassVersionMutationResponse>(client, $"/api/engineering/liquid-class-versions/{draft.LiquidClassVersionId}/publish", new
        {
            commandId = "cmd-liquid-publish-001",
            reason = "bench validation passed"
        });
        Assert.Equal(LiquidClassVersionStatus.Published, published.Status);

        var enabled = await PostAsync<LiquidClassVersionMutationResponse>(client, $"/api/engineering/liquid-class-versions/{draft.LiquidClassVersionId}/enable", new
        {
            commandId = "cmd-liquid-enable-001",
            reason = "approved for operation"
        });
        Assert.Equal(LiquidClassVersionStatus.Enabled, enabled.Status);
        Assert.True(enabled.IsReferenceable);

        profiles = await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes");
        var updated = profiles!.Single(x => x.Id == baseline.Id);
        var version = updated.Versions.Single(x => x.Id == draft.LiquidClassVersionId);
        Assert.Equal(draft.LiquidClassVersionId, updated.EnabledVersionId);
        Assert.Contains(version.Differences, x => x.ParameterName == nameof(LiquidClassVersion.AspirateSpeedUlPerSecond) && x.Unit == "uL/s");
        Assert.Equal(3, version.ValidationRecords.Count);
        Assert.All(version.ValidationRecords, x => Assert.True(x.IsValid));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "engineering.liquid_class.version.enable" && x.EntityId == draft.LiquidClassVersionId));
        var persisted = await dbContext.LiquidClassVersions.SingleAsync(x => x.Id == draft.LiquidClassVersionId);
        persisted.AspirateSpeedUlPerSecond++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Channel_and_run_keep_frozen_versions_after_a_new_version_is_enabled()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var baseline = Assert.Single((await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes"))!, x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId is not null);
        var firstBatch = await CreateAndSelectBatchAsync(client, "A", "liquid-freeze-a");

        var draft = await PostAsync<LiquidClassVersionMutationResponse>(client, "/api/engineering/liquid-classes", new
        {
            commandId = "cmd-liquid-freeze-draft",
            code = baseline.Code,
            name = "Freeze test liquid class",
            aspirateSpeedUlPerSecond = 140,
            dispenseSpeedUlPerSecond = 150,
            leadingAirGapUl = 8,
            trailingAirGapUl = 9,
            excessVolumeUl = 3,
            preWetCycles = 2,
            mixCycles = 1,
            isEnabled = false,
            reason = "freeze behavior test",
            sourceVersionId = baseline.EnabledVersionId,
            versionLabel = "freeze-2"
        });
        _ = await PostAsync<LiquidClassVersionMutationResponse>(client, $"/api/engineering/liquid-class-versions/{draft.LiquidClassVersionId}/publish", new { commandId = "cmd-liquid-freeze-publish", reason = "validated" });
        _ = await PostAsync<LiquidClassVersionMutationResponse>(client, $"/api/engineering/liquid-class-versions/{draft.LiquidClassVersionId}/enable", new { commandId = "cmd-liquid-freeze-enable", reason = "activate" });

        var secondBatch = await CreateAndSelectBatchAsync(client, "B", "liquid-freeze-b");
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-liquid-freeze-task",
            workflowVersionId = secondBatch.WorkflowVersionId,
            drawerCode = "B",
            slotCode = "B-01"
        });
        var run = await PostAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-liquid-freeze-run",
            stainingTaskIds = new[] { task.TaskId }
        });
        var runDetail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{run.RunId}");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batchA = await dbContext.ChannelBatches.AsNoTracking().SingleAsync(x => x.Id == firstBatch.ChannelBatchId);
        var batchB = await dbContext.ChannelBatches.AsNoTracking().SingleAsync(x => x.Id == secondBatch.ChannelBatchId);
        Assert.Equal(LiquidClassSelectionStatus.Frozen, batchA.LiquidClassSelectionStatus);
        Assert.Contains(baseline.EnabledVersionId!, batchA.LiquidClassSnapshotJson);
        Assert.DoesNotContain(draft.LiquidClassVersionId, batchA.LiquidClassSnapshotJson);
        Assert.Contains(draft.LiquidClassVersionId, batchB.LiquidClassSnapshotJson);
        Assert.Equal(LiquidClassSelectionStatus.Frozen, runDetail!.LiquidClassSelectionStatus);
        Assert.Contains(draft.LiquidClassVersionId, runDetail.LiquidClassSnapshotJson);
    }

    [Fact]
    public async Task Concurrent_enable_keeps_single_enabled_version_and_active_pointer()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var baseline = Assert.Single((await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes"))!, x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId is not null);
        var draftA = await CreatePublishedDraftAsync(client, baseline, "concurrent-a", 151);
        var draftB = await CreatePublishedDraftAsync(client, baseline, "concurrent-b", 161);

        var enableA = client.PostAsJsonAsync($"/api/engineering/liquid-class-versions/{draftA.LiquidClassVersionId}/enable", new
        {
            commandId = "cmd-liquid-concurrent-a-enable",
            reason = "concurrent enable A"
        });
        var enableB = client.PostAsJsonAsync($"/api/engineering/liquid-class-versions/{draftB.LiquidClassVersionId}/enable", new
        {
            commandId = "cmd-liquid-concurrent-b-enable",
            reason = "concurrent enable B"
        });
        var responses = await Task.WhenAll(enableA, enableB);
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, x => Assert.True(x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected status {x.StatusCode}: {x.Content.ReadAsStringAsync().Result}"));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var profile = await dbContext.LiquidClassProfiles
            .Include(x => x.Versions)
            .SingleAsync(x => x.Id == baseline.Id);
        var enabledVersions = profile.Versions.Where(x => x.Status == LiquidClassVersionStatus.Enabled).ToList();
        var enabled = Assert.Single(enabledVersions);
        Assert.Equal(profile.EnabledVersionId, enabled.Id);
        Assert.Contains(enabled.Id, new[] { draftA.LiquidClassVersionId, draftB.LiquidClassVersionId });
    }

    [Fact]
    public async Task Invalid_units_or_ranges_are_rejected_without_a_command_receipt()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);
        var response = await client.PostAsJsonAsync("/api/engineering/liquid-classes", new
        {
            commandId = "cmd-liquid-invalid",
            code = "INVALID-LC",
            name = "Invalid",
            aspirateSpeedUlPerSecond = 0,
            dispenseSpeedUlPerSecond = 100,
            leadingAirGapUl = 0,
            trailingAirGapUl = 0,
            excessVolumeUl = 0,
            preWetCycles = 0,
            mixCycles = 0,
            isEnabled = false,
            reason = "range validation test"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-liquid-invalid"));
        Assert.False(await dbContext.LiquidClassProfiles.AnyAsync(x => x.Code == "INVALID-LC"));
    }

    [Fact]
    public async Task Migration_marks_legacy_batches_runs_and_commands_for_manual_resolution()
    {
        var directory = Path.Combine(TestPaths.TempRoot, "stainer-liquid-class-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "stainer.db")}")
            .Options;
        await using var dbContext = new StainerDbContext(options);
        var migrator = dbContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260702002713_CoordinateActiveUniquenessAndRealVerification");
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO drawers (id, code, name, sort_order, heat_board_id, is_enabled, created_at_utc)
            VALUES ('drawer-legacy', 'Z', 'Legacy', 99, 99, 1, {{now}});
            INSERT INTO machine_runs (id, run_code, status, pause_requested, stop_requested, coordinate_snapshot_json, created_at_utc)
            VALUES ('run-legacy', 'RUN-LEGACY-LC', 'Completed', 0, 0, '{}', {{now}});
            INSERT INTO channel_batches (
                id, machine_run_id, drawer_id, drawer_code, status, workflow_snapshot_json,
                coordinate_snapshot_json, coordinate_selection_status, workflow_selection_status,
                needs_manual_resolution, manual_resolution_reason, created_at_utc)
            VALUES (
                'batch-legacy', 'run-legacy', 'drawer-legacy', 'Z', 'Completed', '{}',
                '{}', 'Frozen', 'Selected', 0, '', {{now}});
            INSERT INTO device_command_executions (
                id, machine_run_id, command_type, status, payload_json, result_json, created_at_utc)
            VALUES ('command-legacy', 'run-legacy', 'Dispense', 'Completed', '{}', '{}', {{now}});
            """);

        await migrator.MigrateAsync();
        dbContext.ChangeTracker.Clear();
        var batch = await dbContext.ChannelBatches.SingleAsync(x => x.Id == "batch-legacy");
        var run = await dbContext.MachineRuns.SingleAsync(x => x.Id == "run-legacy");
        var command = await dbContext.DeviceCommandExecutions.SingleAsync(x => x.Id == "command-legacy");
        Assert.True(batch.NeedsManualResolution);
        Assert.Equal(LiquidClassSelectionStatus.NeedsManualResolution, batch.LiquidClassSelectionStatus);
        Assert.Contains("no frozen Liquid Class version", batch.ManualResolutionReason);
        Assert.Equal(LiquidClassSelectionStatus.NeedsManualResolution, run.LiquidClassSelectionStatus);
        Assert.Equal(LiquidClassSelectionStatus.NeedsManualResolution, command.LiquidClassSelectionStatus);
        Assert.Null(command.LiquidClassVersionId);
    }

    private static async Task<ChannelBatchWorkflowResponse> CreateAndSelectBatchAsync(HttpClient client, string drawerCode, string suffix)
    {
        _ = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new { commandId = $"cmd-{suffix}-batch", drawerCode });
        return await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = $"cmd-{suffix}-select",
            drawerCode,
            experimentType = StainingTaskType.He
        });
    }

    private static async Task<LiquidClassVersionMutationResponse> CreatePublishedDraftAsync(
        HttpClient client,
        LiquidClassResponse baseline,
        string suffix,
        int aspirateSpeed)
    {
        var draft = await PostAsync<LiquidClassVersionMutationResponse>(client, "/api/engineering/liquid-classes", new
        {
            commandId = $"cmd-liquid-{suffix}-draft",
            code = baseline.Code,
            name = $"Concurrent {suffix}",
            aspirateSpeedUlPerSecond = aspirateSpeed,
            dispenseSpeedUlPerSecond = aspirateSpeed + 5,
            leadingAirGapUl = 8,
            trailingAirGapUl = 9,
            excessVolumeUl = 3,
            preWetCycles = 2,
            mixCycles = 1,
            isEnabled = false,
            reason = $"concurrent draft {suffix}",
            sourceVersionId = baseline.EnabledVersionId,
            versionLabel = suffix
        });
        return await PostAsync<LiquidClassVersionMutationResponse>(client, $"/api/engineering/liquid-class-versions/{draft.LiquidClassVersionId}/publish", new
        {
            commandId = $"cmd-liquid-{suffix}-publish",
            reason = $"publish {suffix}"
        });
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-liquid-class-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
            }));
        });
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username = "admin", password = "123456", role = "admin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var engineeringSession = await client.PostAsJsonAsync("/api/engineering/session", new
        {
            commandId = $"cmd-liquid-engineering-session-{Guid.NewGuid():N}",
            password = "123456",
            reason = "liquid class versioning test",
            target = "liquid-class"
        });
        Assert.Equal(HttpStatusCode.OK, engineeringSession.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
