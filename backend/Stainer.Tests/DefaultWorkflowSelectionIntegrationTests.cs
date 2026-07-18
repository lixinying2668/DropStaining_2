using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class DefaultWorkflowSelectionIntegrationTests
{
    [Fact]
    public async Task Admin_can_replace_defaults_without_changing_existing_batches_and_invalid_defaults_are_rejected()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        using var operatorClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");
        await LoginAsync(operatorClient, "operator", "operator");

        string originalDefaultHeId;
        string replacementHeId;
        string draftHeId;
        string retiredHeId;
        string disabledHeId;
        string ihcId;
        string existingBatchId;
        const string frozenSnapshot = "{\"workflow\":\"frozen-before-default-change\"}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            originalDefaultHeId = (await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.He)).Id;
            replacementHeId = (await CreateWorkflowVersionAsync(db, "DEFAULT-HE-REPLACEMENT", StainingTaskType.He, WorkflowVersionStatus.Published)).Id;
            draftHeId = (await CreateWorkflowVersionAsync(db, "DEFAULT-HE-DRAFT", StainingTaskType.He, WorkflowVersionStatus.Draft)).Id;
            retiredHeId = (await CreateWorkflowVersionAsync(db, "DEFAULT-HE-RETIRED", StainingTaskType.He, WorkflowVersionStatus.Retired)).Id;
            disabledHeId = (await CreateWorkflowVersionAsync(db, "DEFAULT-HE-DISABLED", StainingTaskType.He, WorkflowVersionStatus.Published, false)).Id;
            ihcId = (await CreateWorkflowVersionAsync(db, "DEFAULT-IHC-MISMATCH", StainingTaskType.Ihc, WorkflowVersionStatus.Published)).Id;

            var drawer = await db.Drawers.SingleAsync(x => x.Code == "A");
            var batch = new ChannelBatch
            {
                DrawerId = drawer.Id,
                DrawerCode = drawer.Code,
                Status = RuntimeLedgerStatus.Pending,
                ExperimentType = StainingTaskType.He,
                SelectedWorkflowVersionId = originalDefaultHeId,
                WorkflowSnapshotJson = frozenSnapshot,
                WorkflowSelectionStatus = WorkflowSelectionStatus.Selected,
                WorkflowSelectedAtUtc = DateTimeOffset.UtcNow
            };
            db.ChannelBatches.Add(batch);
            await db.SaveChangesAsync();
            existingBatchId = batch.Id;
        }

        var setRequest = new { commandId = "cmd-default-he-replace", experimentType = "HE" };
        var replacement = await PostAsync<DefaultWorkflowVersionResponse>(
            adminClient,
            $"/api/workflow-versions/{replacementHeId}/set-default",
            setRequest,
            HttpStatusCode.OK);
        Assert.Equal(replacementHeId, replacement!.WorkflowVersionId);
        Assert.Equal(StainingTaskType.He, replacement.ExperimentType);

        var replay = await PostAsync<DefaultWorkflowVersionResponse>(
            adminClient,
            $"/api/workflow-versions/{replacementHeId}/set-default",
            setRequest,
            HttpStatusCode.OK);
        Assert.True(replay!.Replayed);

        await AssertStatusAsync(adminClient, $"/api/workflow-versions/{draftHeId}/set-default", new { commandId = "cmd-default-draft", experimentType = "HE" }, HttpStatusCode.Conflict);
        await AssertStatusAsync(adminClient, $"/api/workflow-versions/{retiredHeId}/set-default", new { commandId = "cmd-default-retired", experimentType = "HE" }, HttpStatusCode.Conflict);
        await AssertStatusAsync(adminClient, $"/api/workflow-versions/{disabledHeId}/set-default", new { commandId = "cmd-default-disabled", experimentType = "HE" }, HttpStatusCode.Conflict);
        await AssertStatusAsync(adminClient, $"/api/workflow-versions/{ihcId}/set-default", new { commandId = "cmd-default-mismatch", experimentType = "HE" }, HttpStatusCode.Conflict);
        await AssertStatusAsync(operatorClient, $"/api/workflow-versions/{replacementHeId}/set-default", new { commandId = "cmd-default-forbidden", experimentType = "HE" }, HttpStatusCode.Forbidden);
        await AssertStatusAsync(adminClient, $"/api/workflow-versions/{replacementHeId}/retire", new { commandId = "cmd-retire-default", reason = "test" }, HttpStatusCode.Conflict);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Null((await verifyDb.WorkflowVersions.SingleAsync(x => x.Id == originalDefaultHeId)).DefaultExperimentType);
        Assert.Equal(StainingTaskType.He, (await verifyDb.WorkflowVersions.SingleAsync(x => x.Id == replacementHeId)).DefaultExperimentType);
        var existingBatch = await verifyDb.ChannelBatches.SingleAsync(x => x.Id == existingBatchId);
        Assert.Equal(originalDefaultHeId, existingBatch.SelectedWorkflowVersionId);
        Assert.Equal(frozenSnapshot, existingBatch.WorkflowSnapshotJson);
        Assert.Equal(1, await verifyDb.AuditLogs.CountAsync(x => x.Action == "workflow.default.set" && x.EntityId == replacementHeId));
        Assert.Equal(1, await verifyDb.AuditLogs.CountAsync(x => x.Action == "workflow.default.unset" && x.EntityId == originalDefaultHeId));
        var oldDefault = await verifyDb.WorkflowVersions.SingleAsync(x => x.Id == originalDefaultHeId);
        oldDefault.DefaultExperimentType = StainingTaskType.He;
        oldDefault.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<DbUpdateException>(() => verifyDb.SaveChangesAsync());
    }

    [Fact]
    public async Task Experiment_type_selection_uses_defaults_rejects_client_workflow_id_and_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string defaultHeId;
        string defaultIhcId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            defaultHeId = (await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.He)).Id;
            defaultIhcId = (await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.Ihc)).Id;
        }

        var batchA = await EnsureBatchAsync(client, "A", "cmd-batch-default-a");
        var heRequest = new
        {
            commandId = "cmd-select-default-he",
            channelBatchId = batchA.ChannelBatchId,
            drawerCode = "A",
            experimentType = "HE",
            reason = (string?)null
        };
        var he = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", heRequest, HttpStatusCode.OK);
        Assert.Equal(defaultHeId, he!.WorkflowVersionId);
        Assert.NotEmpty(he.WorkflowName!);
        Assert.NotEmpty(he.WorkflowVersionLabel!);

        var replay = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", heRequest, HttpStatusCode.OK);
        Assert.True(replay!.Replayed);

        var batchB = await EnsureBatchAsync(client, "B", "cmd-batch-default-b");
        await AssertStatusAsync(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-client-workflow-id-rejected",
            channelBatchId = batchB.ChannelBatchId,
            drawerCode = "B",
            experimentType = "IHC",
            workflowVersionId = defaultHeId
        }, HttpStatusCode.BadRequest);

        var ihc = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-select-default-ihc",
            channelBatchId = batchB.ChannelBatchId,
            drawerCode = "B",
            experimentType = "IHC",
            reason = (string?)null
        }, HttpStatusCode.OK);
        Assert.Equal(defaultIhcId, ihc!.WorkflowVersionId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var batch = await db.ChannelBatches.SingleAsync(x => x.Id == batchA.ChannelBatchId);
            batch.StartedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await AssertStatusAsync(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-locked-channel",
            channelBatchId = batchA.ChannelBatchId,
            drawerCode = "A",
            experimentType = "IHC",
            reason = "locked change test"
        }, HttpStatusCode.Conflict);

        var batchC = await EnsureBatchAsync(client, "C", "cmd-batch-default-c");
        var batchD = await EnsureBatchAsync(client, "D", "cmd-batch-default-d");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var defaultHe = await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.He);
            var defaultIhc = await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.Ihc);
            defaultHe.DefaultExperimentType = null;
            defaultHe.UpdatedAtUtc = DateTimeOffset.UtcNow;
            defaultIhc.DefaultExperimentType = null;
            defaultIhc.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var missingDefault = await client.PostAsJsonAsync("/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-missing-default-he",
            channelBatchId = batchC.ChannelBatchId,
            drawerCode = "C",
            experimentType = "HE",
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, missingDefault.StatusCode);
        Assert.Contains("No default HE workflow", await missingDefault.Content.ReadAsStringAsync());
        var missingIhcDefault = await client.PostAsJsonAsync("/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-missing-default-ihc",
            channelBatchId = batchD.ChannelBatchId,
            drawerCode = "D",
            experimentType = "IHC",
            reason = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, missingIhcDefault.StatusCode);
        Assert.Contains("No default IHC workflow", await missingIhcDefault.Content.ReadAsStringAsync());

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var selectedA = await verifyDb.ChannelBatches.SingleAsync(x => x.Id == batchA.ChannelBatchId);
        Assert.Equal(defaultHeId, selectedA.SelectedWorkflowVersionId);
        Assert.Contains("workflowDefinitionId", selectedA.WorkflowSnapshotJson);
        Assert.Equal(1, await verifyDb.WorkflowAssignmentHistory.CountAsync(x => x.ChannelBatchId == batchA.ChannelBatchId));
        Assert.Equal(1, await verifyDb.AuditLogs.CountAsync(x => x.Action == "channel.experiment_type.select" && x.EntityId == batchA.ChannelBatchId));
    }

    [Fact]
    public async Task Prestart_reselection_requires_reason_rebinds_to_new_default_and_rejects_task_type_mismatch()
    {
        await using var factory = CreateFactory();
        using var operatorClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");
        await LoginAsync(adminClient, "admin", "admin");

        string originalDefaultHeId;
        string replacementHeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            originalDefaultHeId = (await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.He)).Id;
            replacementHeId = (await CreateWorkflowVersionAsync(db, "DEFAULT-HE-RESELECT", StainingTaskType.He, WorkflowVersionStatus.Published)).Id;
        }

        var batchA = await EnsureBatchAsync(operatorClient, "A", "cmd-change-batch-a");
        await PostAsync<ChannelBatchWorkflowResponse>(operatorClient, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-select-he-a",
            channelBatchId = batchA.ChannelBatchId,
            drawerCode = "A",
            experimentType = "HE",
            reason = (string?)null
        }, HttpStatusCode.OK);
        await AssertStatusAsync(operatorClient, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-no-reason",
            channelBatchId = batchA.ChannelBatchId,
            drawerCode = "A",
            experimentType = "IHC",
            reason = ""
        }, HttpStatusCode.BadRequest);
        await PostAsync<TaskCreationResponse>(operatorClient, "/api/tasks/he", new
        {
            commandId = "cmd-change-he-task",
            slotCode = "A-01",
            drawerCode = "A",
            channelBatchId = batchA.ChannelBatchId
        }, HttpStatusCode.OK);
        await AssertStatusAsync(operatorClient, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-type-mismatch",
            channelBatchId = batchA.ChannelBatchId,
            drawerCode = "A",
            experimentType = "IHC",
            reason = "try incompatible type"
        }, HttpStatusCode.Conflict);

        var batchB = await EnsureBatchAsync(operatorClient, "B", "cmd-change-batch-b");
        await PostAsync<ChannelBatchWorkflowResponse>(operatorClient, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-select-he-b",
            channelBatchId = batchB.ChannelBatchId,
            drawerCode = "B",
            experimentType = "HE",
            reason = (string?)null
        }, HttpStatusCode.OK);
        await PostAsync<DefaultWorkflowVersionResponse>(adminClient, $"/api/workflow-versions/{replacementHeId}/set-default", new
        {
            commandId = "cmd-change-default-he",
            experimentType = "HE"
        }, HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.Equal(originalDefaultHeId, (await db.ChannelBatches.SingleAsync(x => x.Id == batchB.ChannelBatchId)).SelectedWorkflowVersionId);
        }

        var changed = await PostAsync<ChannelBatchWorkflowResponse>(operatorClient, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-change-reselect-he-b",
            channelBatchId = batchB.ChannelBatchId,
            drawerCode = "B",
            experimentType = "HE",
            reason = "adopt newly approved default"
        }, HttpStatusCode.OK);
        Assert.Equal(replacementHeId, changed!.WorkflowVersionId);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(2, await verifyDb.WorkflowAssignmentHistory.CountAsync(x => x.ChannelBatchId == batchB.ChannelBatchId));
        Assert.Equal(1, await verifyDb.WorkflowAssignmentHistory.CountAsync(x => x.ChannelBatchId == batchB.ChannelBatchId && x.ActionType == WorkflowAssignmentAction.PreStartChange));
        Assert.True(await verifyDb.AuditLogs.AnyAsync(x => x.Action == "channel.experiment_type.change" && x.EntityId == batchB.ChannelBatchId));
    }

    [Fact]
    public async Task Default_ihc_channel_uses_workflow_driven_primary_antibody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string defaultIhcId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            defaultIhcId = (await db.WorkflowVersions.SingleAsync(x => x.DefaultExperimentType == StainingTaskType.Ihc)).Id;
            db.PrimaryAntibodyWorkflowMappings.Add(new PrimaryAntibodyWorkflowMapping
            {
                PrimaryAntibodyCode = "002",
                WorkflowVersionId = defaultIhcId,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var batch = await EnsureBatchAsync(client, "A", "cmd-batch-ihc-multi");
        await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-select-ihc-multi",
            channelBatchId = batch.ChannelBatchId,
            drawerCode = "A",
            experimentType = "IHC",
            reason = (string?)null
        }, HttpStatusCode.OK);

        // Client submits legacy inputMode/rawCode but primary antibody is determined by workflow step.
        await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-001",
            slotCode = "A-01",
            drawerCode = "A",
            channelBatchId = batch.ChannelBatchId
        }, HttpStatusCode.OK);
        await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-002",
            slotCode = "A-02",
            drawerCode = "A",
            channelBatchId = batch.ChannelBatchId
        }, HttpStatusCode.OK);
        // Previously unmapped codes were rejected; now all succeed since antibody is from workflow.
        await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-003",
            slotCode = "A-03",
            drawerCode = "A",
            channelBatchId = batch.ChannelBatchId
        }, HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var tasks = await verifyDb.SlideTasks
            .Where(x => x.ChannelBatchId == batch.ChannelBatchId)
            .Include(x => x.StainingTask)
            .Select(x => x.StainingTask!)
            .ToListAsync();
        Assert.Equal(3, tasks.Count);
        // All tasks have ConfirmedPrimaryAntibodyCode = "P01" (seeded IHC workflow PRIMARY step ReagentCode).
        Assert.All(tasks, x =>
        {
            Assert.Equal("P01", x.ConfirmedPrimaryAntibodyCode);
            Assert.Equal(defaultIhcId, x.WorkflowVersionId);
        });
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-default-workflow-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
                }));
            });
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username, password = "123456", role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<ChannelBatchActivationResponse> EnsureBatchAsync(HttpClient client, string drawerCode, string commandId)
    {
        return (await PostAsync<ChannelBatchActivationResponse>(
            client,
            "/api/channel-batches/active",
            new { commandId, drawerCode },
            HttpStatusCode.OK))!;
    }

    private static async Task<T?> PostAsync<T>(HttpClient client, string url, object body, HttpStatusCode expectedStatus)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task AssertStatusAsync(HttpClient client, string url, object body, HttpStatusCode expectedStatus)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private static async Task<WorkflowVersion> CreateWorkflowVersionAsync(
        StainerDbContext db,
        string code,
        string workflowType,
        string status,
        bool enabled = true)
    {
        var definition = new WorkflowDefinition
        {
            Code = code,
            Name = code,
            WorkflowType = workflowType,
            Description = "Default workflow integration test.",
            IsEnabled = enabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var version = new WorkflowVersion
        {
            WorkflowDefinition = definition,
            VersionNo = 1,
            VersionLabel = "1",
            Status = status,
            ChangeNote = "Default workflow integration test.",
            PublishedAtUtc = status == WorkflowVersionStatus.Published ? DateTimeOffset.UtcNow : null,
            RetiredAtUtc = status == WorkflowVersionStatus.Retired ? DateTimeOffset.UtcNow : null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        version.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "TEST",
            StepName = "Test step",
            ActionType = "Manual",
            DurationSeconds = 3,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }
}
