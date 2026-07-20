using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

/// <summary>
/// Comprehensive integration tests for the IHC workflow-driven primary antibody model.
/// Covers design §5 scenarios: antibody from workflow step, client fields ignored,
/// missing/empty/conflicting PRIMARY steps, unpublished/wrong-type/unselected workflows,
/// HE unaffected, preflight checks, and response field verification.
/// </summary>
public sealed class IhcWorkflowDrivenAntibodyTests
{
    // §5 scenario 1: Workflow PRIMARY step ReagentCode="001" → ConfirmedPrimaryAntibodyCode="001"
    [Fact]
    public async Task Workflow_with_primary_step_reagent_001_sets_confirmed_antibody_to_001()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-001-TEST", "001")).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-001-test",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var persisted = await db2.StainingTasks.SingleAsync(x => x.Id == task.TaskId);
        Assert.Equal("001", persisted.ConfirmedPrimaryAntibodyCode);
        Assert.Equal("001", persisted.PrimaryAntibodyCode);
    }

    // §5 scenario 2: Client submits different rawCode/selectedPrimaryAntibodyCode → antibody NOT overridden
    [Fact]
    public async Task Client_submitted_antibody_fields_are_ignored_workflow_value_used_instead()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-IGNORE-CLIENT", "P99")).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);

        // Submit with various legacy fields -- all ignored
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-ignore-client",
            drawerCode = "A",
            slotCode = "A-01",
            inputMode = "DirectPrimaryAntibody",
            rawCode = "OTHER_CODE",
            selectedPrimaryAntibodyCode = "YET_ANOTHER",
            lisQueryLogId = "lis-ignored-001"
        });
        Assert.True(task.Ok);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var persisted = await db2.StainingTasks.SingleAsync(x => x.Id == task.TaskId);
        Assert.Equal("P99", persisted.ConfirmedPrimaryAntibodyCode);
        Assert.Equal("P99", persisted.PrimaryAntibodyCode);
    }

    // §5 scenario 3: Missing PRIMARY step → ihc_workflow_primary_antibody_step_missing
    [Fact]
    public async Task Missing_primary_step_throws_step_missing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowWithStepsAsync(db, "IHC-NO-PRIMARY", [
                ("FINAL_WASH", "Wash", "WAS")
            ])).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-no-primary",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ihc_workflow_primary_antibody_step_missing", body.GetProperty("code").GetString());
    }

    // §5 scenario 4: PRIMARY step with empty ReagentCode → ihc_workflow_primary_antibody_code_empty
    [Fact]
    public async Task Primary_step_with_empty_reagent_code_throws_code_empty()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowWithStepsAsync(db, "IHC-EMPTY-REAGENT", [
                ("PRIMARY_ANTIBODY", "Primary", "")
            ])).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-empty-reagent",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ihc_workflow_primary_antibody_code_empty", body.GetProperty("code").GetString());
    }

    // §5 scenario 4b: A valid PRIMARY step alongside one with empty ReagentCode → code_empty (empty is NOT ignored)
    [Fact]
    public async Task Primary_step_with_valid_and_empty_reagent_codes_throws_code_empty()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowWithStepsAsync(db, "IHC-VALID-AND-EMPTY", [
                ("PRIMARY_ANTIBODY", "Primary 1", "001"),
                ("PRIMARY_ANTIBODY_2", "Primary 2", "")
            ])).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-valid-and-empty",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ihc_workflow_primary_antibody_code_empty", body.GetProperty("code").GetString());
    }

    // §5 scenario 5: Multiple PRIMARY steps with conflicting ReagentCodes → ihc_workflow_primary_antibody_code_conflict
    [Fact]
    public async Task Conflicting_primary_step_reagent_codes_throws_conflict()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowWithStepsAsync(db, "IHC-CONFLICT", [
                ("PRIMARY_ANTIBODY", "Primary 1", "PA1"),
                ("PRIMARY_ANTIBODY_2", "Primary 2", "PA2")
            ])).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-conflict",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ihc_workflow_primary_antibody_code_conflict", body.GetProperty("code").GetString());
    }

    // §5 scenario 6: Unpublished workflow → workflow_version_not_published
    [Fact]
    public async Task Unpublished_workflow_throws_workflow_version_not_published()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string draftVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            draftVersionId = (await CreateIhcWorkflowAsync(db, "IHC-DRAFT-TEST", "P01", WorkflowVersionStatus.Draft)).Id;
        }

        var batchId = await CreateEmptyChannelBatchAsync(factory, "A");
        var response = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-select-draft",
            channelBatchId = batchId,
            drawerCode = "A",
            experimentType = "IHC",
            workflowVersionId = draftVersionId
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("workflow_version_not_published", body.GetProperty("code").GetString());
    }

    // §5 scenario 7: Non-IHC (HE) workflow for IHC task → channel_experiment_type_mismatch
    [Fact]
    public async Task He_workflow_for_ihc_task_throws_experiment_type_mismatch()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            heVersionId = (await CreateHeWorkflowAsync(db, "HE-TYPE-MISMATCH", "HEM")).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.He, heVersionId);
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-on-he-channel",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("channel_experiment_type_mismatch", body.GetProperty("code").GetString());
    }

    // §5 scenario 8: No workflow selected → channel_workflow_required
    [Fact]
    public async Task No_workflow_selected_throws_channel_workflow_required()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        _ = await CreateEmptyChannelBatchAsync(factory, "A");
        var response = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-no-workflow",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("channel_workflow_required", body.GetProperty("code").GetString());
    }

    // §5 scenario 9: HE tasks unaffected (no primary antibody)
    [Fact]
    public async Task He_task_creation_unaffected_no_primary_antibody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            heVersionId = (await CreateHeWorkflowAsync(db, "HE-UNAFFECTED", "HEM")).Id;
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.He, heVersionId);
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-he-unaffected",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var persisted = await db2.StainingTasks.SingleAsync(x => x.Id == task.TaskId);
        Assert.Null(persisted.ConfirmedPrimaryAntibodyCode);
        Assert.Null(persisted.PrimaryAntibodyCode);
        Assert.Null(persisted.CompatibilityValidationStatus);
    }

    // §5 scenario 10: Preflight — task frozen antibody != workflow antibody → channel_workflow_incompatible
    [Fact]
    public async Task Preflight_detects_incompatible_and_missing_antibody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-PREFLIGHT", "P01")).Id;

            // Create a batch and select the workflow first.
            var batchId = await CreateEmptyChannelBatchAsync(factory, "A");
            var batch = await db.ChannelBatches.SingleAsync(x => x.Id == batchId);
            var version = await db.WorkflowVersions
                .Include(x => x.WorkflowDefinition)
                .Include(x => x.Steps)
                .SingleAsync(x => x.Id == ihcVersionId);
            batch.ExperimentType = StainingTaskType.Ihc;
            batch.SelectedWorkflowVersionId = ihcVersionId;
            batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Selected;
            batch.WorkflowSelectedAtUtc = DateTimeOffset.UtcNow;
            batch.WorkflowSnapshotJson = System.Text.Json.JsonSerializer.Serialize(
                new { workflowDefinitionId = version.WorkflowDefinitionId, workflowVersionId = version.Id });
            await db.SaveChangesAsync();

            // Insert a dirty IHC task with mismatched ConfirmedPrimaryAntibodyCode.
            var slot = await db.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
            var userId = await db.Users.Where(x => x.Username == "operator").Select(x => x.Id).SingleAsync();
            var dirtyTask = new StainingTask
            {
                TaskCode = "IHC-dirty-001",
                TaskType = StainingTaskType.Ihc,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlotId = slot.Id,
                WorkflowDefinitionId = version.WorkflowDefinitionId,
                WorkflowVersionId = ihcVersionId,
                WorkflowSnapshotJson = batch.WorkflowSnapshotJson,
                PrimaryAntibodyCode = "WRONG_ANTIBODY",
                ConfirmedPrimaryAntibodyCode = "WRONG_ANTIBODY",
                CompatibilityValidationStatus = "Compatible",
                CompatibilityValidationMessage = "dirty data",
                CreatedByUserId = userId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.StainingTasks.Add(dirtyTask);
            await db.SaveChangesAsync();
            db.SlideTasks.Add(new SlideTask
            {
                ChannelBatchId = batch.Id,
                StainingTaskId = dirtyTask.Id,
                PhysicalSlotId = slot.Id,
                SlotCode = slot.Code,
                TaskType = StainingTaskType.Ihc,
                Status = RuntimeLedgerStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.Contains(preflight.Issues, x => x.Code == "channel_workflow_incompatible");
    }

    [Fact]
    public async Task Preflight_detects_missing_antibody_on_ihc_task()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-PREFLIGHT-EMPTY", "P01")).Id;

            // Create a batch and select the workflow first.
            var batchId = await CreateEmptyChannelBatchAsync(factory, "B");
            var batch = await db.ChannelBatches.SingleAsync(x => x.Id == batchId);
            batch.ExperimentType = StainingTaskType.Ihc;
            batch.SelectedWorkflowVersionId = ihcVersionId;
            batch.WorkflowSelectionStatus = WorkflowSelectionStatus.Selected;
            batch.WorkflowSelectedAtUtc = DateTimeOffset.UtcNow;
            var version = await db.WorkflowVersions
                .Include(x => x.WorkflowDefinition)
                .Include(x => x.Steps)
                .SingleAsync(x => x.Id == ihcVersionId);
            batch.WorkflowSnapshotJson = System.Text.Json.JsonSerializer.Serialize(
                new { workflowDefinitionId = version.WorkflowDefinitionId, workflowVersionId = version.Id });
            await db.SaveChangesAsync();

            // Insert an IHC task with null ConfirmedPrimaryAntibodyCode.
            var slot = await db.PhysicalSlots.SingleAsync(x => x.Code == "B-01");
            var userId = await db.Users.Where(x => x.Username == "operator").Select(x => x.Id).SingleAsync();
            var emptyTask = new StainingTask
            {
                TaskCode = "IHC-empty-001",
                TaskType = StainingTaskType.Ihc,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlotId = slot.Id,
                WorkflowDefinitionId = version.WorkflowDefinitionId,
                WorkflowVersionId = ihcVersionId,
                WorkflowSnapshotJson = batch.WorkflowSnapshotJson,
                PrimaryAntibodyCode = null,
                ConfirmedPrimaryAntibodyCode = null,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.StainingTasks.Add(emptyTask);
            await db.SaveChangesAsync();
            db.SlideTasks.Add(new SlideTask
            {
                ChannelBatchId = batch.Id,
                StainingTaskId = emptyTask.Id,
                PhysicalSlotId = slot.Id,
                SlotCode = slot.Code,
                TaskType = StainingTaskType.Ihc,
                Status = RuntimeLedgerStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.Contains(preflight.Issues, x => x.Code == "primary_antibody_required");
    }

    // §5 scenario 10b: Preflight must NOT block when the workflow antibody is absent from
    // primary_antibody_workflow_mappings — mappings are query/compat only, not a startup gate.
    [Fact]
    public async Task Preflight_does_not_block_when_antibody_absent_from_mapping_table()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-PREFLIGHT-NOMAP", "P01")).Id;
            // 显式确认：该流程在映射表中没有任何条目。
            Assert.False(await db.PrimaryAntibodyWorkflowMappings.AnyAsync(x => x.WorkflowVersionId == ihcVersionId));
        }

        _ = await CreateBatchAndSelectAsync(factory, client, "A", StainingTaskType.Ihc, ihcVersionId);
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-preflight-nomap",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);

        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.DoesNotContain(preflight.Issues, x => x.Code == "channel_workflow_incompatible");
        Assert.DoesNotContain(preflight.Issues, x => x.Code == "primary_antibody_required");
    }

    // §5 scenario 11: Response fields carry primaryAntibodyCode and workflow metadata
    [Fact]
    public async Task Active_batch_and_workflow_selection_responses_include_primary_antibody_code()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreateIhcWorkflowAsync(db, "IHC-RESPONSE-FIELDS", "P01")).Id;
        }

        var batchId = await CreateEmptyChannelBatchAsync(factory, "A");

        // Before selection: active batch has no primary antibody.
        var activeBefore = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-active-before-select",
            drawerCode = "A"
        });
        Assert.Null(activeBefore.PrimaryAntibodyCode);

        // Select workflow.
        var selected = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-select-response-fields",
            channelBatchId = batchId,
            drawerCode = "A",
            experimentType = "IHC",
            workflowVersionId = ihcVersionId
        });
        Assert.Equal("P01", selected.PrimaryAntibodyCode);
        Assert.NotNull(selected.WorkflowName);
        Assert.NotNull(selected.WorkflowVersionLabel);

        // After selection: active batch includes primary antibody and workflow metadata.
        var activeAfter = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-active-after-select",
            drawerCode = "A"
        });
        Assert.Equal("P01", activeAfter.PrimaryAntibodyCode);
        Assert.Equal(ihcVersionId, activeAfter.SelectedWorkflowVersionId);
        Assert.NotNull(activeAfter.SelectedWorkflowName);
        Assert.NotNull(activeAfter.SelectedWorkflowVersionLabel);

        // Create a task and verify response compatibility status.
        var task = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-response-field-task",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);
        Assert.Equal("Compatible", task.CompatibilityValidationStatus);
    }

    // Bonus: experiment-type-selection response also carries primaryAntibodyCode
    [Fact]
    public async Task Experiment_type_selection_response_includes_primary_antibody_code()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        // Use seeded default IHC workflow (has PRIMARY step with ReagentCode=P01).
        var batch = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-et-active-b",
            drawerCode = "B"
        });
        var selected = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-et-select-ihc",
            channelBatchId = batch.ChannelBatchId,
            drawerCode = "B",
            experimentType = "IHC",
            reason = (string?)null
        });
        Assert.Equal("P01", selected.PrimaryAntibodyCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-ihc-workflow-tests", Guid.NewGuid().ToString("N"), "stainer.db");
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

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<string> CreateEmptyChannelBatchAsync(WebApplicationFactory<Program> factory, string drawerCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
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
        return batch.Id;
    }

    private static async Task<string> CreateBatchAndSelectAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string drawerCode,
        string experimentType,
        string workflowVersionId)
    {
        var batchId = await CreateEmptyChannelBatchAsync(factory, drawerCode);
        _ = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = $"cmd-select-{drawerCode}",
            channelBatchId = batchId,
            drawerCode,
            experimentType,
            workflowVersionId
        });
        return batchId;
    }

    private static async Task<WorkflowVersion> CreateIhcWorkflowAsync(
        StainerDbContext db,
        string code,
        string primaryAntibodyReagentCode,
        string status = WorkflowVersionStatus.Published)
    {
        return await CreateIhcWorkflowWithStepsAsync(db, code, [
            ("PRIMARY_ANTIBODY", "Primary antibody", primaryAntibodyReagentCode)
        ], status);
    }

    private static async Task<WorkflowVersion> CreateIhcWorkflowWithStepsAsync(
        StainerDbContext db,
        string code,
        (string MajorStepCode, string StepName, string ReagentCode)[] steps,
        string status = WorkflowVersionStatus.Published)
    {
        var liquidClassProfileId = await db.LiquidClassProfiles
            .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
            .Select(x => x.Id)
            .SingleAsync();

        // Ensure reagent definitions exist.
        foreach (var step in steps.Where(x => !string.IsNullOrWhiteSpace(x.ReagentCode)))
        {
            var exists = await db.ReagentDefinitions.AnyAsync(x => x.ReagentCode == step.ReagentCode);
            if (!exists)
            {
                db.ReagentDefinitions.Add(new ReagentDefinition
                {
                    ReagentCode = step.ReagentCode,
                    Name = $"Reagent {step.ReagentCode}",
                    ReagentType = "test",
                    LiquidClassProfileId = liquidClassProfileId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        var definition = new WorkflowDefinition
        {
            Code = code,
            Name = code,
            WorkflowType = StainingTaskType.Ihc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var version = new WorkflowVersion
        {
            WorkflowDefinition = definition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = status,
            ChangeNote = "Test IHC workflow.",
            PublishedAtUtc = status == WorkflowVersionStatus.Published ? DateTimeOffset.UtcNow : null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        for (var i = 0; i < steps.Length; i++)
        {
            version.Steps.Add(new WorkflowStep
            {
                StepNo = i + 1,
                MajorStepCode = steps[i].MajorStepCode,
                StepName = steps[i].StepName,
                ActionType = string.IsNullOrWhiteSpace(steps[i].ReagentCode) ? "Manual" : "Dispense",
                ReagentCode = string.IsNullOrWhiteSpace(steps[i].ReagentCode) ? null : steps[i].ReagentCode,
                VolumeUl = 100,
                DurationSeconds = 60,
                TargetTemperatureDeciC = 250,
                FailureStrategy = "Stop",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            if (!string.IsNullOrWhiteSpace(steps[i].ReagentCode))
            {
                version.ReagentRequirements.Add(new WorkflowReagentRequirement
                {
                    ReagentCode = steps[i].ReagentCode,
                    RequiredVolumeUl = 100,
                    IsRequired = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }

    private static async Task<WorkflowVersion> CreateHeWorkflowAsync(
        StainerDbContext db,
        string code,
        string reagentCode,
        string status = WorkflowVersionStatus.Published)
    {
        var liquidClassProfileId = await db.LiquidClassProfiles
            .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
            .Select(x => x.Id)
            .SingleAsync();
        var reagentDef = await db.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == reagentCode);
        if (reagentDef is null)
        {
            reagentDef = new ReagentDefinition
            {
                ReagentCode = reagentCode,
                Name = $"Reagent {reagentCode}",
                ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ReagentDefinitions.Add(reagentDef);
        }

        var definition = new WorkflowDefinition
        {
            Code = code,
            Name = code,
            WorkflowType = StainingTaskType.He,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var version = new WorkflowVersion
        {
            WorkflowDefinition = definition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = status,
            ChangeNote = "Test HE workflow.",
            PublishedAtUtc = status == WorkflowVersionStatus.Published ? DateTimeOffset.UtcNow : null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        version.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "HEMATOXYLIN",
            StepName = "Hematoxylin",
            ActionType = "Dispense",
            ReagentCode = reagentCode,
            VolumeUl = 100,
            DurationSeconds = 60,
            TargetTemperatureDeciC = 250,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        version.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = reagentCode,
            RequiredVolumeUl = 100,
            IsRequired = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }
}
