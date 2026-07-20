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

public sealed class BusinessWriteApiIntegrationTests
{
    [Fact]
    public async Task Twin_snapshot_without_login_returns_structured_unauthorized_response()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/twin/snapshot");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("authentication_required", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Twin_snapshot_control_values_exclude_structural_configuration_controls()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");

        var response = await adminClient.GetAsync("/api/twin/snapshot");
        response.EnsureSuccessStatusCode();
        using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var controlValues = snapshot.RootElement.GetProperty("control_values");

        Assert.True(controlValues.TryGetProperty("reagentTempText", out _));
        Assert.False(controlValues.TryGetProperty("configProfileFold", out _));
        Assert.False(controlValues.TryGetProperty("configProfileSummary", out _));
        Assert.False(controlValues.TryGetProperty("configFlowFold", out _));
    }

    [Fact]
    public async Task Admin_user_management_is_authorized_idempotent_audited_and_rolls_back_on_failure()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");

        var createRequest = new
        {
            commandId = "cmd-user-create-001",
            username = "api-user",
            displayName = "API User",
            password = "Start123!",
            roles = new[] { "operator" }
        };

        var created = await PostJsonAsync<UserMutationResponse>(adminClient, "/api/users", createRequest);
        Assert.False(created.Replayed);

        var replayed = await PostJsonAsync<UserMutationResponse>(adminClient, "/api/users", createRequest);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.UserId, replayed.UserId);

        var renamed = await PutJsonAsync<UserMutationResponse>(adminClient, $"/api/users/{created.UserId}/display-name", new
        {
            commandId = "cmd-user-rename-001",
            displayName = "Renamed API User"
        });
        Assert.Equal("Renamed API User", renamed.DisplayName);

        var disabled = await PutJsonAsync<UserMutationResponse>(adminClient, $"/api/users/{created.UserId}/enabled", new
        {
            commandId = "cmd-user-disable-001",
            enabled = false
        });
        Assert.False(disabled.Enabled);

        var reset = await PutJsonAsync<UserMutationResponse>(adminClient, $"/api/users/{created.UserId}/password", new
        {
            commandId = "cmd-user-password-001",
            newPassword = "Next123!"
        });
        Assert.True(reset.Ok);

        var roles = await PutJsonAsync<UserMutationResponse>(adminClient, $"/api/users/{created.UserId}/roles", new
        {
            commandId = "cmd-user-roles-001",
            roles = new[] { "operator", "admin" }
        });
        Assert.Contains("admin", roles.Roles);

        var duplicateUsername = await adminClient.PostAsJsonAsync("/api/users", new
        {
            commandId = "cmd-user-duplicate-001",
            username = "api-user",
            displayName = "Duplicate",
            password = "Start123!",
            roles = new[] { "operator" }
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUsername.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.Equal(1, await dbContext.Users.CountAsync(x => x.Username == "api-user"));
            Assert.Equal(1, await dbContext.CommandReceipts.CountAsync(x => x.CommandId == "cmd-user-create-001"));
            Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-user-duplicate-001"));
            Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "user.create" && x.EntityId == created.UserId));
            Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "user.set_roles" && x.EntityId == created.UserId));
        }

        // 该用户自身从未执行过业务动作（所有变更均由 admin 发起），按现行删除策略可删除。
        var delete = await adminClient.DeleteAsync($"/api/users/{created.UserId}?commandId=cmd-user-delete-001");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");
        var forbidden = await operatorClient.PostAsJsonAsync("/api/users", new
        {
            commandId = "cmd-user-forbidden-001",
            username = "bad",
            displayName = "Bad",
            password = "Start123!",
            roles = new[] { "operator" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Channel_batch_active_api_creates_empty_batch_for_samples_ui_and_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var created = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-samples-active-a-001",
            drawerCode = "A"
        });
        Assert.True(created.Ok);
        Assert.False(created.Replayed);
        Assert.Equal("A", created.DrawerCode);
        Assert.Equal(RuntimeLedgerStatus.Pending, created.Status);
        Assert.Equal(WorkflowSelectionStatus.Unselected, created.WorkflowSelectionStatus);

        var replayed = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-samples-active-a-001",
            drawerCode = "A"
        });
        Assert.True(replayed.Replayed);
        Assert.Equal(created.ChannelBatchId, replayed.ChannelBatchId);

        var reused = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-samples-active-a-002",
            drawerCode = "A"
        });
        Assert.False(reused.Replayed);
        Assert.Equal(created.ChannelBatchId, reused.ChannelBatchId);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await dbContext.ChannelBatches.CountAsync(x => x.DrawerCode == "A" && x.Status == RuntimeLedgerStatus.Pending));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "channel_batch.ensure_active" && x.EntityId == created.ChannelBatchId));
    }

    [Fact]
    public async Task Workflow_draft_creation_is_authorized_idempotent_audited_and_can_copy_latest_version()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");

        var createRequest = new
        {
            commandId = "cmd-workflow-draft-create-001",
            code = "DRAFT-API",
            name = "API Draft Workflow",
            workflowType = StainingTaskType.Ihc,
            description = "Created by API integration test.",
            versionLabel = "0.1",
            changeNote = "Create blank draft."
        };

        var created = await PostJsonAsync<WorkflowDraftMutationResponse>(adminClient, "/api/workflows/drafts", createRequest);
        Assert.False(created.Replayed);
        Assert.Equal(WorkflowVersionStatus.Draft, created.Status);
        Assert.Equal(1, created.VersionNo);

        var replayed = await PostJsonAsync<WorkflowDraftMutationResponse>(adminClient, "/api/workflows/drafts", createRequest);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.WorkflowVersionId, replayed.WorkflowVersionId);

        string sourceWorkflowId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var sourceVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "COPY-SOURCE", StainingTaskType.Ihc, "SRC", 500);
            sourceWorkflowId = sourceVersion.WorkflowDefinition!.Id;
        }

        var copied = await PostJsonAsync<WorkflowDraftMutationResponse>(adminClient, "/api/workflows/drafts", new
        {
            commandId = "cmd-workflow-draft-copy-001",
            sourceWorkflowId,
            versionLabel = "2.0",
            changeNote = "Copy latest version."
        });
        Assert.Equal(sourceWorkflowId, copied.WorkflowDefinitionId);
        Assert.Equal(2, copied.VersionNo);
        Assert.Equal("2.0", copied.VersionLabel);
        Assert.Equal(WorkflowVersionStatus.Draft, copied.Status);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var blank = await dbContext.WorkflowVersions.SingleAsync(x => x.Id == created.WorkflowVersionId);
            Assert.Equal(WorkflowVersionStatus.Draft, blank.Status);
            Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "workflow.draft.create" && x.EntityId == created.WorkflowVersionId));
            Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "workflow.draft.copy" && x.EntityId == copied.WorkflowVersionId));
            Assert.Equal(1, await dbContext.WorkflowSteps.CountAsync(x => x.WorkflowVersionId == copied.WorkflowVersionId));
            Assert.Equal(1, await dbContext.WorkflowReagentRequirements.CountAsync(x => x.WorkflowVersionId == copied.WorkflowVersionId));
        }

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");
        var forbidden = await operatorClient.PostAsJsonAsync("/api/workflows/drafts", new
        {
            commandId = "cmd-workflow-draft-forbidden-001",
            code = "NOPE",
            name = "Forbidden",
            workflowType = StainingTaskType.He
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Workflow_configuration_closed_loop_publishes_retires_and_manages_primary_antibody_mappings()
    {
        await using var factory = CreateFactory();
        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");

        var created = await PostJsonAsync<WorkflowDraftMutationResponse>(adminClient, "/api/workflows", new
        {
            commandId = "cmd-workflow-config-create-he",
            code = "CFG-HE-CLOSED",
            name = "Configuration HE Closed Loop",
            workflowType = StainingTaskType.He,
            description = "Created by configuration API test.",
            versionLabel = "0.1",
            changeNote = "Create Draft."
        });
        Assert.Equal(WorkflowVersionStatus.Draft, created.Status);

        var emptyValidation = await adminClient.GetFromJsonAsync<PublishValidationResponse>(
            $"/api/workflow-versions/{created.WorkflowVersionId}/publish-validation");
        Assert.NotNull(emptyValidation);
        Assert.Equal("Fail", emptyValidation!.Result);
        Assert.Contains(emptyValidation.Issues, x => x.Code == "workflow_steps_required");

        _ = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/steps", new
        {
            commandId = "cmd-workflow-config-step-hem",
            stepNo = 1,
            majorStepCode = "HEMATOXYLIN",
            stepName = "Hematoxylin",
            actionType = "Dispense",
            reagentCode = "HEM",
            volumeUl = 100,
            durationSeconds = 3,
            targetTemperatureDeciC = 250,
            mixParametersJson = "{}",
            washParametersJson = "{}",
            legacyParametersJson = "{}",
            failureStrategy = "Stop"
        });
        _ = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/steps", new
        {
            commandId = "cmd-workflow-config-step-wash",
            stepNo = 2,
            majorStepCode = "FINAL_WASH",
            stepName = "Terminal wash",
            actionType = "Wash",
            reagentCode = "WAS",
            volumeUl = 100,
            durationSeconds = 3,
            targetTemperatureDeciC = 250,
            mixParametersJson = "{}",
            washParametersJson = "{}",
            legacyParametersJson = "{}",
            failureStrategy = "Stop"
        });
        _ = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/reagent-requirements/recalculate", new
        {
            commandId = "cmd-workflow-config-req-recalc"
        });

        var detail = await adminClient.GetFromJsonAsync<WorkflowVersionMaintenanceResponse>($"/api/workflow-versions/{created.WorkflowVersionId}");
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Steps.Count);
        Assert.Equal(2, detail.ReagentRequirements.Count);

        var valid = await adminClient.GetFromJsonAsync<PublishValidationResponse>(
            $"/api/workflow-versions/{created.WorkflowVersionId}/publish-validation");
        Assert.NotNull(valid);
        Assert.Equal("Pass", valid!.Result);

        var published = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/publish", new
        {
            commandId = "cmd-workflow-config-publish"
        });
        Assert.True(published.Ok);

        var replayedPublish = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/publish", new
        {
            commandId = "cmd-workflow-config-publish"
        });
        Assert.True(replayedPublish.Replayed);

        var publishedEdit = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/steps", new
        {
            commandId = "cmd-workflow-config-step-after-publish",
            stepNo = 3,
            majorStepCode = "WASH",
            stepName = "Late wash",
            actionType = "Wash",
            reagentCode = "WAS"
        });
        Assert.True(publishedEdit.Ok);
        detail = await adminClient.GetFromJsonAsync<WorkflowVersionMaintenanceResponse>($"/api/workflow-versions/{created.WorkflowVersionId}");
        Assert.Contains(detail!.Steps, x => x.StepNo == 3 && x.StepName == "Late wash");

        var retired = await PostJsonAsync<CommandResponse>(adminClient, $"/api/workflow-versions/{created.WorkflowVersionId}/retire", new
        {
            commandId = "cmd-workflow-config-retire",
            reason = "Closed loop test retire."
        });
        Assert.True(retired.Ok);

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");
        _ = await PostJsonAsync<ChannelBatchActivationResponse>(operatorClient, "/api/channel-batches/active", new
        {
            commandId = "cmd-workflow-config-active-a",
            drawerCode = "A"
        });
        var retiredSelection = await operatorClient.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-workflow-config-select-retired",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = created.WorkflowVersionId
        });
        Assert.Equal(HttpStatusCode.Conflict, retiredSelection.StatusCode);
        Assert.Equal("workflow_version_not_published", (await retiredSelection.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var forbiddenCreate = await operatorClient.PostAsJsonAsync("/api/workflows", new
        {
            commandId = "cmd-workflow-config-forbidden",
            code = "NO-OP",
            name = "Forbidden",
            workflowType = StainingTaskType.Ihc
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);

        var workflows = await adminClient.GetFromJsonAsync<List<WorkflowSummaryResponse>>("/api/workflows");
        Assert.NotNull(workflows);
        var seededHeVersion = workflows!.Single(x => x.Code == ReferenceDataSeeder.DefaultHeWorkflowCode)
            .Versions.Single(x => x.Status == WorkflowVersionStatus.Published);
        var seededIhcVersion = workflows.Single(x => x.Code == ReferenceDataSeeder.DefaultIhcWorkflowCode)
            .Versions.Single(x => x.Status == WorkflowVersionStatus.Published);

        var invalidMapping = await adminClient.PostAsJsonAsync("/api/primary-antibody-mappings", new
        {
            commandId = "cmd-primary-map-he-invalid",
            primaryAntibodyCode = "CFG999",
            workflowVersionId = seededHeVersion.Id
        });
        Assert.Equal(HttpStatusCode.Conflict, invalidMapping.StatusCode);
        Assert.Equal("primary_antibody_mapping_target_invalid", (await invalidMapping.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var mapping = await PostJsonAsync<CommandResponse>(adminClient, "/api/primary-antibody-mappings", new
        {
            commandId = "cmd-primary-map-create-cfg999",
            primaryAntibodyCode = "CFG999",
            workflowVersionId = seededIhcVersion.Id
        });
        Assert.True(mapping.Ok);

        var mappingReplay = await PostJsonAsync<CommandResponse>(adminClient, "/api/primary-antibody-mappings", new
        {
            commandId = "cmd-primary-map-create-cfg999",
            primaryAntibodyCode = "CFG999",
            workflowVersionId = seededIhcVersion.Id
        });
        Assert.True(mappingReplay.Replayed);

        var mappingList = await adminClient.GetFromJsonAsync<List<PrimaryAntibodyMappingResponse>>("/api/primary-antibody-mappings");
        Assert.NotNull(mappingList);
        var persistedMapping = mappingList!.Single(x => x.PrimaryAntibodyCode == "CFG999" && x.WorkflowVersionId == seededIhcVersion.Id);
        Assert.True(persistedMapping.IsEnabled);

        var missingReason = await adminClient.PostAsJsonAsync($"/api/primary-antibody-mappings/{persistedMapping.Id}/disable", new
        {
            commandId = "cmd-primary-map-disable-missing-reason"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal("reason_required", (await missingReason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        _ = await PostJsonAsync<CommandResponse>(adminClient, $"/api/primary-antibody-mappings/{persistedMapping.Id}/disable", new
        {
            commandId = "cmd-primary-map-disable-cfg999",
            reason = "Closed loop test disable."
        });
        _ = await PostJsonAsync<CommandResponse>(adminClient, $"/api/primary-antibody-mappings/{persistedMapping.Id}/enable", new
        {
            commandId = "cmd-primary-map-enable-cfg999",
            reason = "Closed loop test enable."
        });

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var finalVersion = await dbContext.WorkflowVersions.SingleAsync(x => x.Id == created.WorkflowVersionId);
        Assert.Equal(WorkflowVersionStatus.Retired, finalVersion.Status);
        Assert.NotNull(finalVersion.PublishedAtUtc);
        Assert.NotNull(finalVersion.RetiredAtUtc);
        Assert.Equal(1, await dbContext.PrimaryAntibodyWorkflowMappings.CountAsync(x => x.PrimaryAntibodyCode == "CFG999" && x.WorkflowVersionId == seededIhcVersion.Id));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "workflow.create" && x.EntityId == created.WorkflowVersionId));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "workflow.version.publish" && x.EntityId == created.WorkflowVersionId));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "workflow.version.retire" && x.EntityId == created.WorkflowVersionId));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "primary_antibody_mapping.create"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "primary_antibody_mapping.disable"));

        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        var events = publisher.Snapshot();
        Assert.Contains(events, x => x.Type == MachineEventTypes.WorkflowVersionChanged && x.EntityId == created.WorkflowVersionId);
        Assert.Contains(events, x => x.Type == MachineEventTypes.PrimaryAntibodyMappingChanged && x.EntityId == persistedMapping.Id);
    }

    [Fact]
    public async Task Workflow_editor_round_trips_planning_rules_step_ui_metadata_and_draft_deletion()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        var created = await PostJsonAsync<WorkflowDraftMutationResponse>(client, "/api/workflows", new
        {
            commandId = "cmd-workflow-editor-create",
            code = "CFG-UI-ROUNDTRIP",
            name = "UI round-trip workflow",
            workflowType = StainingTaskType.Ihc,
            description = "Persisted from the configuration editor.",
            versionLabel = "0.1",
            changeNote = "Initial editor draft."
        });
        var planningRulesJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            targetTempC = 41.5,
            tempControlFromStep = 1,
            allowMultiPrimary = true,
            dabRatio = new { a = 2, b = 1, pureWater = 17, preparePolicy = "per_run" },
            notes = "UI scheduling rules"
        });
        _ = await PutJsonAsync<CommandResponse>(client, $"/api/workflow-versions/{created.WorkflowVersionId}", new
        {
            commandId = "cmd-workflow-editor-rules",
            name = (string?)null,
            description = (string?)null,
            isEnabled = (bool?)null,
            versionLabel = (string?)null,
            changeNote = (string?)null,
            planningRulesJson
        });

        var legacyParametersJson = JsonSerializer.Serialize(new
        {
            ui = new
            {
                opKey = "primary",
                label = "Primary antibody",
                toleranceSec = 12,
                immediateAfterPrev = true,
                requiresTemp = true,
                reagentRole = "primary",
                allowMultiPrimary = true,
                notes = "Per-slide antibody mapping"
            }
        });
        _ = await PostJsonAsync<CommandResponse>(client, $"/api/workflow-versions/{created.WorkflowVersionId}/steps", new
        {
            commandId = "cmd-workflow-editor-step",
            stepNo = 1,
            majorStepCode = "PRIMARY_ANTIBODY",
            stepName = "Primary antibody",
            actionType = "Dispense",
            reagentCode = "P01",
            volumeUl = 100,
            durationSeconds = 120,
            targetTemperatureDeciC = 415,
            mixParametersJson = "{\"cycles\":2}",
            washParametersJson = "{}",
            legacyParametersJson,
            failureStrategy = "Stop"
        });

        var detail = await client.GetFromJsonAsync<WorkflowVersionMaintenanceResponse>($"/api/workflow-versions/{created.WorkflowVersionId}");
        Assert.NotNull(detail);
        using (var planning = JsonDocument.Parse(detail!.PlanningRulesJson))
        {
            Assert.Equal(41.5, planning.RootElement.GetProperty("targetTempC").GetDouble());
            Assert.Equal(2, planning.RootElement.GetProperty("dabRatio").GetProperty("a").GetInt32());
            Assert.True(planning.RootElement.GetProperty("allowMultiPrimary").GetBoolean());
        }
        var persistedStep = Assert.Single(detail.Steps);
        Assert.Equal("{\"cycles\":2}", persistedStep.MixParametersJson);
        using (var legacy = JsonDocument.Parse(persistedStep.LegacyParametersJson))
        {
            Assert.Equal(12, legacy.RootElement.GetProperty("ui").GetProperty("toleranceSec").GetInt32());
            Assert.True(legacy.RootElement.GetProperty("ui").GetProperty("immediateAfterPrev").GetBoolean());
        }

        var copied = await PostJsonAsync<WorkflowDraftMutationResponse>(client, $"/api/workflow-versions/{created.WorkflowVersionId}/copy-draft", new
        {
            commandId = "cmd-workflow-editor-copy",
            versionLabel = "0.2",
            changeNote = "Editor copy."
        });
        var copiedDetail = await client.GetFromJsonAsync<WorkflowVersionMaintenanceResponse>($"/api/workflow-versions/{copied.WorkflowVersionId}");
        Assert.NotNull(copiedDetail);
        Assert.Equal(detail.PlanningRulesJson, copiedDetail!.PlanningRulesJson);
        Assert.Equal(persistedStep.LegacyParametersJson, Assert.Single(copiedDetail.Steps).LegacyParametersJson);

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/workflow-versions/{copied.WorkflowVersionId}?commandId=cmd-workflow-editor-delete");
        var deleted = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var missing = await client.GetAsync($"/api/workflow-versions/{copied.WorkflowVersionId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Seeded_manual_acceptance_workflows_are_queryable_and_001_is_compatible()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var workflows = await client.GetFromJsonAsync<List<WorkflowSummaryResponse>>("/api/workflows");
        Assert.NotNull(workflows);
        var he = workflows!.Single(x => x.Code == ReferenceDataSeeder.DefaultHeWorkflowCode);
        var heVersion = he.Versions.Single(x => x.VersionNo == 1);
        Assert.Equal("HE 快速染色模板", he.Name);
        Assert.Equal(StainingTaskType.He, he.WorkflowType);
        Assert.Equal(WorkflowVersionStatus.Published, heVersion.Status);

        var ihc = workflows.Single(x => x.Code == ReferenceDataSeeder.DefaultIhcWorkflowCode);
        var ihcVersion = ihc.Versions.Single(x => x.VersionNo == 1);
        Assert.Equal("IHC 标准流程 40℃", ihc.Name);
        Assert.Equal(StainingTaskType.Ihc, ihc.WorkflowType);
        Assert.Equal(WorkflowVersionStatus.Published, ihcVersion.Status);

        await LoginAsync(client, "operator", "operator");
        _ = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-seed-compatible-active-d",
            drawerCode = "D"
        });
        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-seed-compatible-select-d",
            drawerCode = "D",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersion.Id
        });

        // IHC task: primary antibody comes from the seeded workflow's PRIMARY step (ReagentCode = "P01").
        var task = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-seed-compatible-ihc-001",
            drawerCode = "D",
            slotCode = "D-01"
        });
        Assert.True(task.Ok);
        Assert.Equal(ihcVersion.Id, task.WorkflowVersionId);
        Assert.Equal("Compatible", task.CompatibilityValidationStatus);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        // Verify the task has the workflow-determined primary antibody (P01), not the legacy mapping code (001).
        var persisted = await dbContext.StainingTasks.SingleAsync(x => x.Id == task.TaskId);
        Assert.Equal("P01", persisted.ConfirmedPrimaryAntibodyCode);
        Assert.Equal("P01", persisted.PrimaryAntibodyCode);

        // Legacy mapping still exists for backward compatibility.
        Assert.True(await dbContext.PrimaryAntibodyWorkflowMappings
            .Include(x => x.WorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .AnyAsync(x =>
                x.PrimaryAntibodyCode == ReferenceDataSeeder.ManualPrimaryAntibodyCode
                && x.WorkflowVersionId == ihcVersion.Id
                && x.IsEnabled
                && x.WorkflowVersion!.Status == WorkflowVersionStatus.Published
                && x.WorkflowVersion.WorkflowDefinition!.WorkflowType == StainingTaskType.Ihc));
    }

    [Fact]
    public async Task Task_creation_covers_he_manual_confirmation_and_ihc_selection_rules()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionId;
        string ihcVersionOneId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var heVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "HE-API", StainingTaskType.He, "HEM", 2000);
            var ihcVersionOne = await CreatePublishedIhcWorkflowVersionAsync(dbContext, "IHC-API-1", "PA1", 1000);
            var ihcVersionTwo = await CreatePublishedIhcWorkflowVersionAsync(dbContext, "IHC-API-2", "PA1", 1000);
            _ = await CreatePublishedIhcWorkflowVersionAsync(dbContext, "IHC-API-3", "PA2", 1000);
            dbContext.PrimaryAntibodyWorkflowMappings.AddRange(
                new PrimaryAntibodyWorkflowMapping { PrimaryAntibodyCode = "PA1", WorkflowVersionId = ihcVersionOne.Id },
                new PrimaryAntibodyWorkflowMapping { PrimaryAntibodyCode = "PA1", WorkflowVersionId = ihcVersionTwo.Id },
                new PrimaryAntibodyWorkflowMapping { PrimaryAntibodyCode = "PA2", WorkflowVersionId = ihcVersionTwo.Id });
            dbContext.HospitalBarcodeMappings.AddRange(
                new HospitalBarcodeMapping { HospitalCode = "HOSP-MULTI", PrimaryAntibodyCode = "PA1" },
                new HospitalBarcodeMapping { HospitalCode = "HOSP-MULTI", PrimaryAntibodyCode = "PA2" });
            await dbContext.SaveChangesAsync();

            heVersionId = heVersion.Id;
            ihcVersionOneId = ihcVersionOne.Id;
        }
        _ = await CreateEmptyChannelBatchAsync(factory, "A");
        _ = await CreateEmptyChannelBatchAsync(factory, "B");

        var heSelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-a-he-select-001",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionId
        });
        Assert.Equal(StainingTaskType.He, heSelection.ExperimentType);

        var ihcSelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-b-ihc-select-001",
            drawerCode = "B",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionOneId
        });
        Assert.Equal(StainingTaskType.Ihc, ihcSelection.ExperimentType);

        var heTask = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-he-task-001",
            workflowVersionId = heVersionId,
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(heTask.Ok);
        Assert.False(string.IsNullOrWhiteSpace(heTask.TaskId));

        // IHC task on a HE channel still rejected by experiment type mismatch.
        var multiWorkflow = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-multi-workflow-001",
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.Conflict, multiWorkflow.StatusCode);
        var multiWorkflowBody = await multiWorkflow.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("channel_experiment_type_mismatch", multiWorkflowBody.GetProperty("code").GetString());

        // IHC task: primary antibody comes from workflow step, not client input.
        // Client submits inputMode/rawCode (legacy fields, ignored by new logic).
        var ihcTask = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-ihc-task-001",
            drawerCode = "B",
            slotCode = "B-01"
        });
        Assert.True(ihcTask.Ok);
        Assert.Equal("Compatible", ihcTask.CompatibilityValidationStatus);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var persisted = await verifyContext.StainingTasks.SingleAsync(x => x.Id == ihcTask.TaskId);
        // Primary antibody is determined by workflow PRIMARY step, not client input.
        Assert.Equal("PA1", persisted.PrimaryAntibodyCode);
        Assert.Equal("PA1", persisted.ConfirmedPrimaryAntibodyCode);
        Assert.Contains(ihcVersionOneId, persisted.WorkflowSnapshotJson);
        var batch = await verifyContext.ChannelBatches.Include(x => x.SlideTasks).SingleAsync(x => x.Id == ihcSelection.ChannelBatchId);
        Assert.Equal(ihcVersionOneId, batch.SelectedWorkflowVersionId);
        Assert.Single(batch.SlideTasks);
    }

    [Fact]
    public async Task Task_creation_inherits_channel_workflow_and_validates_ihc_compatibility()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionId;
        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            heVersionId = (await CreatePublishedWorkflowVersionAsync(dbContext, "HE-102", StainingTaskType.He, "HEM", 100)).Id;
            ihcVersionId = (await CreatePublishedIhcWorkflowVersionAsync(dbContext, "IHC-102", "PA1", 100)).Id;
            dbContext.PrimaryAntibodyWorkflowMappings.AddRange(
                new PrimaryAntibodyWorkflowMapping { PrimaryAntibodyCode = "PA1", WorkflowVersionId = ihcVersionId },
                new PrimaryAntibodyWorkflowMapping { PrimaryAntibodyCode = "PA2", WorkflowVersionId = ihcVersionId });
            await dbContext.SaveChangesAsync();
        }

        var batchAId = await CreateEmptyChannelBatchAsync(factory, "A");
        var batchBId = await CreateEmptyChannelBatchAsync(factory, "B");
        var batchCId = await CreateEmptyChannelBatchAsync(factory, "C");

        // Unselected workflow → channel_workflow_required
        var unselected = await client.PostAsJsonAsync("/api/tasks/he", new
        {
            commandId = "cmd-102-unselected-he",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, unselected.StatusCode);
        Assert.Equal("channel_workflow_required", (await unselected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var heSelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-102-select-a-he",
            channelBatchId = batchAId,
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionId
        });
        var ihcSelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-102-select-b-ihc",
            channelBatchId = batchBId,
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionId
        });
        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-102-select-c-ihc",
            channelBatchId = batchCId,
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionId
        });

        var heTask = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-102-he-task",
            drawerCode = "A",
            slotCode = "A-01",
            workflowVersionId = heVersionId
        });
        Assert.Equal(heSelection.ChannelBatchId, heTask.ChannelBatchId);
        Assert.Equal(StainingTaskType.He, heTask.ExperimentType);
        Assert.Equal(heVersionId, heTask.WorkflowVersionId);

        // IHC task on a HE channel → channel_experiment_type_mismatch
        var ihcOnHeChannel = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-102-ihc-on-he",
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.Conflict, ihcOnHeChannel.StatusCode);
        Assert.Equal("channel_experiment_type_mismatch", (await ihcOnHeChannel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // IHC task: primary antibody from workflow step. Legacy fields ignored.
        var ihcRequest = new
        {
            commandId = "cmd-102-ihc-pa1",
            drawerCode = "B",
            slotCode = "B-01"
        };
        var ihcTask = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", ihcRequest);
        Assert.Equal(ihcSelection.ChannelBatchId, ihcTask.ChannelBatchId);
        Assert.Equal(StainingTaskType.Ihc, ihcTask.ExperimentType);
        Assert.Equal(ihcVersionId, ihcTask.WorkflowVersionId);
        Assert.Equal("Compatible", ihcTask.CompatibilityValidationStatus);

        // Idempotent replay
        var replayed = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", ihcRequest);
        Assert.True(replayed.Replayed);
        Assert.Equal(ihcTask.TaskId, replayed.TaskId);

        // Second IHC task on same channel: antibody still from workflow (PA1), not from any client input.
        var ihcTaskTwo = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-102-ihc-pa2",
            drawerCode = "B",
            slotCode = "B-02"
        });
        Assert.Equal(ihcVersionId, ihcTaskTwo.WorkflowVersionId);

        // Concurrent IHC tasks on same slot → slot_not_idle
        var concurrentResponses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/tasks/ihc", new
            {
                commandId = "cmd-102-concurrent-one",
                drawerCode = "C",
                slotCode = "C-01"
            }),
            client.PostAsJsonAsync("/api/tasks/ihc", new
            {
                commandId = "cmd-102-concurrent-two",
                drawerCode = "C",
                slotCode = "C-01"
            }));
        Assert.Equal(1, concurrentResponses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, concurrentResponses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var concurrentConflict = concurrentResponses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("slot_not_idle", (await concurrentConflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Locked batch cannot accept new slides
        await using (var lockScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = lockScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var batch = await dbContext.ChannelBatches.SingleAsync(x => x.Id == batchBId);
            batch.WorkflowLockedAtUtc = DateTimeOffset.UtcNow;
            batch.StartedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        var lockedAdd = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-102-locked-add",
            drawerCode = "B",
            slotCode = "B-04"
        });
        Assert.Equal(HttpStatusCode.Conflict, lockedAdd.StatusCode);
        Assert.Equal("channel_batch_locked", (await lockedAdd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batchB = await verifyContext.ChannelBatches.SingleAsync(x => x.Id == batchBId);
        var bTasks = await verifyContext.StainingTasks
            .Where(x => x.Id == ihcTask.TaskId || x.Id == ihcTaskTwo.TaskId)
            .OrderBy(x => x.PhysicalSlotId)
            .ToListAsync();
        Assert.Equal(2, bTasks.Count);
        Assert.All(bTasks, x =>
        {
            Assert.Equal(ihcVersionId, x.WorkflowVersionId);
            Assert.Equal(batchB.WorkflowSnapshotJson, x.WorkflowSnapshotJson);
            Assert.Equal("Compatible", x.CompatibilityValidationStatus);
            // Both tasks have the same primary antibody from the workflow step.
            Assert.Equal("PA1", x.ConfirmedPrimaryAntibodyCode);
        });
        Assert.Equal(1, await verifyContext.StainingTasks.CountAsync(x => x.Id == ihcTask.TaskId));
        Assert.Equal(1, await verifyContext.SlideTasks.CountAsync(x => x.ChannelBatchId == batchCId && x.SlotCode == "C-01"));
    }

    [Fact]
    public async Task Initial_channel_workflow_selection_api_validates_published_type_idempotency_history_and_audit()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string hePublishedId;
        string ihcPublishedId;
        string heDraftId;
        string heRetiredId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            hePublishedId = (await CreateWorkflowVersionAsync(dbContext, "HE-INITIAL-API", StainingTaskType.He, "HEM", 100, WorkflowVersionStatus.Published)).Id;
            ihcPublishedId = (await CreateWorkflowVersionAsync(dbContext, "IHC-INITIAL-API", StainingTaskType.Ihc, "ABC", 100, WorkflowVersionStatus.Published)).Id;
            heDraftId = (await CreateWorkflowVersionAsync(dbContext, "HE-DRAFT-INITIAL-API", StainingTaskType.He, "HEM", 100, WorkflowVersionStatus.Draft)).Id;
            heRetiredId = (await CreateWorkflowVersionAsync(dbContext, "HE-RETIRED-INITIAL-API", StainingTaskType.He, "HEM", 100, WorkflowVersionStatus.Retired)).Id;
        }

        var batchAId = await CreateEmptyChannelBatchAsync(factory, "A");
        _ = await CreateEmptyChannelBatchAsync(factory, "B");
        _ = await CreateEmptyChannelBatchAsync(factory, "C");
        _ = await CreateEmptyChannelBatchAsync(factory, "D");

        var heRequest = new
        {
            commandId = "cmd-initial-select-he",
            channelBatchId = batchAId,
            experimentType = StainingTaskType.He,
            workflowVersionId = hePublishedId
        };
        var he = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", heRequest);
        Assert.Equal(StainingTaskType.He, he.ExperimentType);
        Assert.Equal(WorkflowSelectionStatus.Selected, he.WorkflowSelectionStatus);

        var replayed = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", heRequest);
        Assert.True(replayed.Replayed);
        Assert.Equal(he.ChannelBatchId, replayed.ChannelBatchId);

        var ihc = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-initial-select-ihc",
            drawerCode = "B",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcPublishedId
        });
        Assert.Equal(StainingTaskType.Ihc, ihc.ExperimentType);

        var draft = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-initial-select-draft",
            drawerCode = "C",
            experimentType = StainingTaskType.He,
            workflowVersionId = heDraftId
        });
        Assert.Equal(HttpStatusCode.Conflict, draft.StatusCode);
        Assert.Equal("workflow_version_not_published", (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var retired = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-initial-select-retired",
            drawerCode = "C",
            experimentType = StainingTaskType.He,
            workflowVersionId = heRetiredId
        });
        Assert.Equal(HttpStatusCode.Conflict, retired.StatusCode);
        Assert.Equal("workflow_version_not_published", (await retired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var mismatch = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-initial-select-type-mismatch",
            drawerCode = "C",
            experimentType = StainingTaskType.He,
            workflowVersionId = ihcPublishedId
        });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("workflow_type_mismatch", (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var alreadySelected = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-initial-select-he-again",
            channelBatchId = batchAId,
            experimentType = StainingTaskType.He,
            workflowVersionId = hePublishedId
        });
        Assert.Equal(HttpStatusCode.Conflict, alreadySelected.StatusCode);
        Assert.Equal("channel_workflow_already_selected", (await alreadySelected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batch = await verifyContext.ChannelBatches.SingleAsync(x => x.Id == batchAId);
        Assert.Equal(StainingTaskType.He, batch.ExperimentType);
        Assert.Equal(hePublishedId, batch.SelectedWorkflowVersionId);
        Assert.Equal(WorkflowSelectionStatus.Selected, batch.WorkflowSelectionStatus);
        Assert.NotEqual("{}", batch.WorkflowSnapshotJson);
        Assert.NotNull(batch.WorkflowSelectedAtUtc);
        Assert.NotNull(batch.WorkflowSelectedByUserId);
        Assert.Equal(1, await verifyContext.WorkflowAssignmentHistory.CountAsync(x => x.ChannelBatchId == batchAId && x.ActionType == WorkflowAssignmentAction.InitialSelection));
        var history = await verifyContext.WorkflowAssignmentHistory.SingleAsync(x => x.ChannelBatchId == batchAId);
        Assert.Equal(hePublishedId, history.NewWorkflowVersionId);
        Assert.Equal(StainingTaskType.He, history.NewExperimentType);
        Assert.Equal("cmd-initial-select-he", history.CommandId);
        Assert.Equal("cmd-initial-select-he", history.CorrelationId);
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "channel.workflow.select" && x.EntityId == batchAId));
    }

    [Fact]
    public async Task Channel_batch_workflow_rules_capacity_change_history_and_locking_are_enforced()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionOneId;
        string heVersionTwoId;
        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var heVersionOne = await CreatePublishedWorkflowVersionAsync(dbContext, "HE-CHANNEL-1", StainingTaskType.He, "HEM", 100);
            var heVersionTwo = await CreatePublishedWorkflowVersionAsync(dbContext, "HE-CHANNEL-2", StainingTaskType.He, "EOS", 100);
            var ihcVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "IHC-CHANNEL-1", StainingTaskType.Ihc, "ABC", 100);
            dbContext.PrimaryAntibodyWorkflowMappings.Add(new PrimaryAntibodyWorkflowMapping
            {
                PrimaryAntibodyCode = "PA1",
                WorkflowVersionId = ihcVersion.Id
            });
            await dbContext.SaveChangesAsync();
            heVersionOneId = heVersionOne.Id;
            heVersionTwoId = heVersionTwo.Id;
            ihcVersionId = ihcVersion.Id;
        }
        _ = await CreateEmptyChannelBatchAsync(factory, "A");
        _ = await CreateEmptyChannelBatchAsync(factory, "B");
        _ = await CreateEmptyChannelBatchAsync(factory, "C");
        _ = await CreateEmptyChannelBatchAsync(factory, "D");

        var channelA = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-a-he-select",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionOneId
        });
        Assert.Equal(WorkflowSelectionStatus.Selected, channelA.WorkflowSelectionStatus);

        var channelB = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-b-ihc-select",
            drawerCode = "B",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionId
        });
        Assert.Equal(StainingTaskType.Ihc, channelB.ExperimentType);

        var taskIds = new List<string>();
        for (var i = 1; i <= 4; i++)
        {
            var task = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
            {
                commandId = $"cmd-channel-rules-he-task-{i}",
                workflowVersionId = heVersionOneId,
                drawerCode = "A",
                slotCode = $"A-{i:00}"
            });
            taskIds.Add(task.TaskId!);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
            dbContext.PhysicalSlots.Add(new PhysicalSlot
            {
                DrawerId = drawer.Id,
                Code = "A-05",
                SlotNo = 5,
                VerticalOrderFromBottom = 5,
                HeatPointId = 99,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var fifth = await client.PostAsJsonAsync("/api/tasks/he", new
        {
            commandId = "cmd-channel-rules-he-task-5",
            workflowVersionId = heVersionOneId,
            drawerCode = "A",
            slotCode = "A-05"
        });
        Assert.Equal(HttpStatusCode.Conflict, fifth.StatusCode);
        Assert.Equal("channel_batch_full", (await fifth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-c-he-select",
            drawerCode = "C",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionOneId
        });
        var differentScript = await client.PostAsJsonAsync("/api/tasks/he", new
        {
            commandId = "cmd-channel-rules-c-he-task-mismatch",
            workflowVersionId = heVersionTwoId,
            drawerCode = "C",
            slotCode = "C-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, differentScript.StatusCode);
        Assert.Equal("channel_workflow_mismatch", (await differentScript.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-d-he-select",
            drawerCode = "D",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionOneId
        });
        var mixedType = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-channel-rules-d-ihc-mix",
            inputMode = "DirectPrimaryAntibody",
            rawCode = "PA1",
            drawerCode = "D",
            slotCode = "D-01"
        });
        Assert.Equal(HttpStatusCode.Conflict, mixedType.StatusCode);
        Assert.Equal("channel_experiment_type_mismatch", (await mixedType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var secondSelection = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-a-second-selection",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionTwoId
        });
        Assert.Equal(HttpStatusCode.Conflict, secondSelection.StatusCode);
        Assert.Equal("channel_workflow_already_selected", (await secondSelection.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.Equal(heVersionOneId, await dbContext.ChannelBatches.Where(x => x.Id == channelA.ChannelBatchId).Select(x => x.SelectedWorkflowVersionId).SingleAsync());
            Assert.Equal(4, await dbContext.StainingTasks.CountAsync(x => taskIds.Contains(x.Id) && x.WorkflowVersionId == heVersionOneId));
            Assert.False(await dbContext.WorkflowAssignmentHistory.AnyAsync(x => x.ChannelBatchId == channelA.ChannelBatchId && x.ActionType == WorkflowAssignmentAction.PreStartChange));
            Assert.False(await dbContext.AuditLogs.AnyAsync(x => x.Action == "channel.workflow.change" && x.EntityId == channelA.ChannelBatchId));
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-channel-rules-run-create",
            stainingTaskIds = taskIds
        });
        var initialization = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = "cmd-channel-rules-device-initialize"
        });
        Assert.True(initialization.Ok, initialization.Message);
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-channel-rules-run-start" });

        var lockedChange = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-rules-a-change-after-start",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionOneId,
            reason = "should be rejected after start"
        });
        Assert.Equal(HttpStatusCode.Conflict, lockedChange.StatusCode);
        Assert.Equal("channel_workflow_locked", (await lockedChange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var lockedAdd = await client.PostAsJsonAsync("/api/tasks/he", new
        {
            commandId = "cmd-channel-rules-a-add-after-start",
            workflowVersionId = heVersionOneId,
            drawerCode = "A",
            slotCode = "A-05"
        });
        Assert.Equal(HttpStatusCode.Conflict, lockedAdd.StatusCode);
        Assert.Equal("channel_batch_locked", (await lockedAdd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalContext = finalScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var lockedBatch = await finalContext.ChannelBatches.SingleAsync(x => x.Id == channelA.ChannelBatchId);
        Assert.Equal(WorkflowSelectionStatus.Locked, lockedBatch.WorkflowSelectionStatus);
        Assert.NotNull(lockedBatch.WorkflowLockedAtUtc);
        Assert.True(await finalContext.WorkflowAssignmentHistory.AnyAsync(x => x.ChannelBatchId == channelA.ChannelBatchId && x.ActionType == WorkflowAssignmentAction.Lock));
    }

    [Fact]
    public async Task Second_channel_workflow_selection_is_rejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string heVersionOneId;
        string heVersionTwoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seedContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            heVersionOneId = (await CreatePublishedWorkflowVersionAsync(seedContext, "HE-CONCURRENT-1", StainingTaskType.He, "HEM", 100)).Id;
            heVersionTwoId = (await CreatePublishedWorkflowVersionAsync(seedContext, "HE-CONCURRENT-2", StainingTaskType.He, "EOS", 100)).Id;
        }
        _ = await CreateEmptyChannelBatchAsync(factory, "A");

        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-concurrent-one",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionOneId
        });
        var second = await client.PostAsJsonAsync("/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-channel-concurrent-two",
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionTwoId
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("channel_workflow_already_selected", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await dbContext.ChannelBatches.CountAsync(x => x.DrawerCode == "A" && x.Status == RuntimeLedgerStatus.Pending));
        Assert.Equal(1, await dbContext.WorkflowAssignmentHistory.CountAsync(x => x.ActionType == WorkflowAssignmentAction.InitialSelection));
    }

    [Fact]
    public async Task Reagent_scan_session_start_complete_are_idempotent_audited_authorized_and_published()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var anonymousClient = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var emptyOverview = await client.GetFromJsonAsync<ReagentScanSessionOverviewResponse>("/api/reagents/scan-sessions/overview");
        Assert.NotNull(emptyOverview);
        Assert.Null(emptyOverview!.ActiveSession);
        Assert.Null(emptyOverview.LatestCompletedSession);

        var forbiddenStart = await anonymousClient.PostAsJsonAsync("/api/reagents/scan-sessions/start", new
        {
            commandId = "cmd-reagent-session-forbidden-start"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, forbiddenStart.StatusCode);

        var started = await PostJsonAsync<ReagentScanSessionMutationResponse>(client, "/api/reagents/scan-sessions/start", new
        {
            commandId = "cmd-reagent-session-start-001"
        });
        Assert.True(started.Ok);
        Assert.False(started.Replayed);
        Assert.Equal("Active", started.Session.Status);
        Assert.Equal(0, started.Session.ScannedCount);
        Assert.Equal(40, started.Session.UnscannedCount);
        Assert.False(started.Session.HasWarning);

        var reused = await PostJsonAsync<ReagentScanSessionMutationResponse>(client, "/api/reagents/scan-sessions/start", new
        {
            commandId = "cmd-reagent-session-start-002"
        });
        Assert.False(reused.Replayed);
        Assert.Equal(started.Session.ScanSessionId, reused.Session.ScanSessionId);

        var replayedStart = await PostJsonAsync<ReagentScanSessionMutationResponse>(client, "/api/reagents/scan-sessions/start", new
        {
            commandId = "cmd-reagent-session-start-001"
        });
        Assert.True(replayedStart.Replayed);
        Assert.Equal(started.Session.ScanSessionId, replayedStart.Session.ScanSessionId);

        var activeOverview = await client.GetFromJsonAsync<ReagentScanSessionOverviewResponse>("/api/reagents/scan-sessions/overview");
        Assert.Equal(started.Session.ScanSessionId, activeOverview!.ActiveSession?.ScanSessionId);
        Assert.Null(activeOverview.LatestCompletedSession);

        await using (var activeScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = activeScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.Equal(1, await dbContext.ReagentScanSessions.CountAsync(x => x.Status == "Active" && x.CompletedAtUtc == null));
            Assert.Equal(0, await dbContext.ReagentScanItems.CountAsync(x => x.ReagentScanSessionId == started.Session.ScanSessionId));
        }

        var completed = await PostJsonAsync<ReagentScanSessionMutationResponse>(
            client,
            $"/api/reagents/scan-sessions/{started.Session.ScanSessionId}/complete",
            new
            {
                commandId = "cmd-reagent-session-complete-001"
            });
        Assert.True(completed.Ok);
        Assert.False(completed.Replayed);
        Assert.Equal("Completed", completed.Session.Status);
        Assert.NotNull(completed.Session.CompletedAtUtc);
        Assert.True(completed.Session.HasWarning);
        Assert.Equal(40, completed.Session.UnscannedCount);
        Assert.Equal(0, completed.Session.EmptyCount);

        var replayedComplete = await PostJsonAsync<ReagentScanSessionMutationResponse>(
            client,
            $"/api/reagents/scan-sessions/{started.Session.ScanSessionId}/complete",
            new
            {
                commandId = "cmd-reagent-session-complete-001"
            });
        Assert.True(replayedComplete.Replayed);
        Assert.Equal(completed.Session.ScanSessionId, replayedComplete.Session.ScanSessionId);

        var completedAgain = await client.PostAsJsonAsync(
            $"/api/reagents/scan-sessions/{started.Session.ScanSessionId}/complete",
            new
            {
                commandId = "cmd-reagent-session-complete-002"
            });
        Assert.Equal(HttpStatusCode.Conflict, completedAgain.StatusCode);
        var completedAgainBody = await completedAgain.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("reagent_scan_session_not_active", completedAgainBody.GetProperty("code").GetString());

        var forbiddenComplete = await anonymousClient.PostAsJsonAsync(
            $"/api/reagents/scan-sessions/{started.Session.ScanSessionId}/complete",
            new
            {
                commandId = "cmd-reagent-session-forbidden-complete"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, forbiddenComplete.StatusCode);

        var completedOverview = await client.GetFromJsonAsync<ReagentScanSessionOverviewResponse>("/api/reagents/scan-sessions/overview");
        Assert.Null(completedOverview!.ActiveSession);
        Assert.Equal(started.Session.ScanSessionId, completedOverview.LatestCompletedSession?.ScanSessionId);
        Assert.True(completedOverview.LatestCompletedSession?.HasWarning);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var persisted = await verifyContext.ReagentScanSessions.SingleAsync(x => x.Id == started.Session.ScanSessionId);
        Assert.Equal("Completed", persisted.Status);
        Assert.NotNull(persisted.CompletedAtUtc);
        Assert.Equal(0, await verifyContext.ReagentScanItems.CountAsync(x => x.ReagentScanSessionId == persisted.Id));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "reagent.scan_session.start" && x.EntityId == persisted.Id));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "reagent.scan_session.complete" && x.EntityId == persisted.Id));

        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        var scanSessionEvents = publisher.Snapshot()
            .Where(x => x.Type == MachineEventTypes.ScanSessionChanged && x.EntityId == persisted.Id)
            .ToList();
        Assert.Equal(2, scanSessionEvents.Count);
        Assert.Contains(scanSessionEvents, x => string.Equals(x.Payload["status"]?.ToString(), "Active", StringComparison.Ordinal));
        Assert.Contains(scanSessionEvents, x => string.Equals(x.Payload["status"]?.ToString(), "Completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Active_reagent_scan_session_confirms_single_positions_rescans_and_publishes_events()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            if (!await dbContext.ReagentDefinitions.AnyAsync(x => x.ReagentCode == "ABC"))
            {
                dbContext.ReagentDefinitions.Add(new ReagentDefinition
                {
                    ReagentCode = "ABC",
                    Name = "ABC Test Reagent",
                    ReagentType = "Primary",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
        }

        var started = await PostJsonAsync<ReagentScanSessionMutationResponse>(client, "/api/reagents/scan-sessions/start", new
        {
            commandId = "cmd-reagent-position-session-start"
        });

        var valid = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-r1-valid",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R1", scanResult = "VALID", rawBarcode = "ABC05020270101001", locatorCode = "R1", expirationDate = "2028-01-01" }
            }
        });
        Assert.Equal("R1", valid.Position);
        Assert.Equal(ReagentScanResult.Valid, valid.ScanResult);

        var replayed = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-r1-valid",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R1", scanResult = "VALID", rawBarcode = "ABC05020270101001", locatorCode = "R1", expirationDate = "2028-01-01" }
            }
        });
        Assert.True(replayed.Replayed);

        var empty = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-r2-empty",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R2", scanResult = "EMPTY", rawBarcode = (string?)null, locatorCode = "R2", expirationDate = (string?)null }
            }
        });
        Assert.Equal(ReagentScanResult.Empty, empty.ScanResult);

        var invalid = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-r3-invalid",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R3", scanResult = "VALID", rawBarcode = "BAD", locatorCode = "R3", expirationDate = (string?)null }
            }
        });
        Assert.Equal(ReagentScanResult.Invalid, invalid.ScanResult);
        Assert.Contains("17 characters", invalid.ValidationMessage);

        var rescan = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-r1-rescan-empty",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R1", scanResult = "EMPTY", rawBarcode = (string?)null, locatorCode = "R1", expirationDate = (string?)null }
            }
        });
        Assert.Equal(ReagentScanResult.Empty, rescan.ScanResult);

        var rack = await client.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");
        Assert.Equal(ReagentScanResult.Empty, Assert.Single(rack!, x => x.Position == "R1").ScanState);
        Assert.Equal(ReagentScanResult.Empty, Assert.Single(rack!, x => x.Position == "R2").ScanState);
        Assert.Equal(ReagentScanResult.Invalid, Assert.Single(rack!, x => x.Position == "R3").ScanState);
        Assert.Equal("UNSCANNED", Assert.Single(rack!, x => x.Position == "R4").ScanState);

        _ = await PostJsonAsync<ReagentScanSessionMutationResponse>(
            client,
            $"/api/reagents/scan-sessions/{started.Session.ScanSessionId}/complete",
            new { commandId = "cmd-reagent-position-session-complete" });

        var afterComplete = await client.PostAsJsonAsync("/api/reagents/scan-confirm", new
        {
            commandId = "cmd-reagent-position-after-complete",
            scanSessionId = started.Session.ScanSessionId,
            items = new[]
            {
                new { position = "R4", scanResult = "EMPTY", rawBarcode = (string?)null, locatorCode = "R4", expirationDate = (string?)null }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, afterComplete.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await verifyContext.ReagentBottles.CountAsync(x => x.FullBarcode == "ABC05020270101001"));
        Assert.Equal(3, await verifyContext.ReagentScanItems.CountAsync(x => x.ReagentScanSessionId == started.Session.ScanSessionId));
        Assert.False(await verifyContext.ReagentRackPlacements.AnyAsync(x => x.RemovedAtUtc == null && x.ReagentScanSessionId == started.Session.ScanSessionId));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "reagent.scan_rescan" && x.EntityId == started.Session.ScanSessionId));

        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.ReagentChanged && x.Payload["position"]?.ToString() == "R1");
    }

    [Fact]
    public async Task Reagent_scan_engineering_writes_preflight_and_transaction_rollback_are_covered()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        string heVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var heVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "HE-PREFLIGHT", StainingTaskType.He, "ABC", 1000);
            heVersionId = heVersion.Id;
        }
        _ = await CreateEmptyChannelBatchAsync(factory, "B");

        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-preflight-channel-b-he-select-001",
            drawerCode = "B",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionId
        });

        _ = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-preflight-task-001",
            workflowVersionId = heVersionId,
            drawerCode = "B",
            slotCode = "B-01"
        });

        var scan = await PostJsonAsync<ReagentScanConfirmationResponse>(client, "/api/reagents/scan-confirm", new
        {
            commandId = "cmd-scan-confirm-001",
            items = new object[]
            {
                new { position = "R1", scanResult = "VALID", rawBarcode = "ABC05020260101001", locatorCode = "R1", expirationDate = "2027-01-01" },
                new { position = "R2", scanResult = "INVALID", rawBarcode = "BAD", locatorCode = "R2", expirationDate = (string?)null }
            }
        });
        Assert.Equal(1, scan.ValidCount);
        Assert.Equal(1, scan.InvalidCount);
        Assert.Equal(38, scan.EmptyCount);

        await OpenEngineeringSessionAsync(client, "business-write");

        var calibration = await PostJsonAsync<EngineeringWriteResponse>(client, "/api/engineering/coordinate-points/calibrate", new
        {
            commandId = "cmd-coordinate-calibrate-001",
            profileCode = ReferenceDataSeeder.DefaultCoordinateProfileCode,
            pointCode = "R1",
            calibratedXUm = 111L,
            calibratedYUm = 222L,
            safeZUm = 1000L,
            aspirateZUm = 900L,
            dispenseZUm = 800L,
            reason = "integration test calibration"
        });
        Assert.True(calibration.Ok);

        var liquid = await PostJsonAsync<EngineeringWriteResponse>(client, "/api/engineering/liquid-classes", new
        {
            commandId = "cmd-liquid-save-001",
            code = "LC-WRITE",
            name = "Write Liquid",
            aspirateSpeedUlPerSecond = 10,
            dispenseSpeedUlPerSecond = 20,
            leadingAirGapUl = 1,
            trailingAirGapUl = 2,
            excessVolumeUl = 3,
            preWetCycles = 1,
            mixCycles = 2,
            isEnabled = true,
            reason = "integration test liquid"
        });
        Assert.True(liquid.Ok);

        var device = await PostJsonAsync<EngineeringWriteResponse>(client, "/api/engineering/device-profiles", new
        {
            commandId = "cmd-device-save-001",
            code = "DEVICE-WRITE",
            name = "Write Device",
            isActive = true,
            reason = "integration test device"
        });
        Assert.True(device.Ok);

        var failedCalibration = await client.PostAsJsonAsync("/api/engineering/coordinate-points/calibrate", new
        {
            commandId = "cmd-coordinate-rollback-001",
            profileCode = ReferenceDataSeeder.DefaultCoordinateProfileCode,
            pointCode = "NO-SUCH-POINT",
            calibratedXUm = 1L,
            calibratedYUm = 2L,
            safeZUm = 3L,
            aspirateZUm = 4L,
            dispenseZUm = 5L,
            reason = "should rollback"
        });
        Assert.Equal(HttpStatusCode.NotFound, failedCalibration.StatusCode);

        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.False(preflight!.Ok);
        Assert.Contains(preflight.Issues, x => x.Code == "scan_has_invalid_items");

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(40, await verifyContext.ReagentScanItems.CountAsync(x => x.ReagentScanSessionId == scan.SessionId));
        Assert.True(await verifyContext.ReagentBottles.AnyAsync(x => x.FullBarcode == "ABC05020260101001"));
        Assert.True(await verifyContext.CoordinateCalibrationHistory.AnyAsync(x => x.CoordinatePointId == calibration.EntityId));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "engineering.coordinate.calibrate" && x.Message.Contains("integration test calibration")));
        Assert.False(await verifyContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-coordinate-rollback-001"));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-business-write-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
                    });
                });
            });
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new
        {
            username,
            password = "123456",
            role
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task OpenEngineeringSessionAsync(HttpClient client, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/engineering/session", new
        {
            commandId = $"cmd-business-engineering-session-{suffix}",
            password = "123456",
            reason = $"business write engineering test {suffix}",
            target = "business-write"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<T> PutJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PutAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static async Task<WorkflowVersion> CreatePublishedWorkflowVersionAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string workflowType,
        string reagentCode,
        int requiredVolumeUl)
    {
        return await CreateWorkflowVersionAsync(
            dbContext,
            workflowCode,
            workflowType,
            reagentCode,
            requiredVolumeUl,
            WorkflowVersionStatus.Published);
    }

    /// <summary>
    /// Creates a published IHC workflow version with a PRIMARY_ANTIBODY step whose ReagentCode
    /// determines the primary antibody for tasks. This is the new IHC workflow-driven antibody model.
    /// </summary>
    private static async Task<WorkflowVersion> CreatePublishedIhcWorkflowVersionAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string primaryAntibodyReagentCode,
        int requiredVolumeUl = 100)
    {
        var liquidClassProfileId = await dbContext.LiquidClassProfiles
            .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
            .Select(x => x.Id)
            .SingleAsync();
        var reagentDefinition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == primaryAntibodyReagentCode);
        if (reagentDefinition is null)
        {
            reagentDefinition = new ReagentDefinition
            {
                ReagentCode = primaryAntibodyReagentCode,
                Name = $"Reagent {primaryAntibodyReagentCode}",
                ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentDefinitions.Add(reagentDefinition);
        }
        else if (reagentDefinition.LiquidClassProfileId is null)
        {
            reagentDefinition.LiquidClassProfileId = liquidClassProfileId;
        }

        var workflowDefinition = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = $"{workflowCode} definition",
            WorkflowType = StainingTaskType.Ihc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "Published IHC workflow for integration test.",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "PRIMARY_ANTIBODY",
            StepName = "Primary antibody",
            ActionType = "Dispense",
            ReagentCode = primaryAntibodyReagentCode,
            VolumeUl = requiredVolumeUl,
            DurationSeconds = 60,
            TargetTemperatureDeciC = 250,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        workflowVersion.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = primaryAntibodyReagentCode,
            RequiredVolumeUl = requiredVolumeUl,
            IsRequired = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        dbContext.WorkflowVersions.Add(workflowVersion);
        await dbContext.SaveChangesAsync();
        return workflowVersion;
    }

    private static async Task<WorkflowVersion> CreateWorkflowVersionAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string workflowType,
        string reagentCode,
        int requiredVolumeUl,
        string status)
    {
        var liquidClassProfileId = await dbContext.LiquidClassProfiles
            .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
            .Select(x => x.Id)
            .SingleAsync();
        var reagentDefinition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == reagentCode);
        if (reagentDefinition is null)
        {
            reagentDefinition = new ReagentDefinition
            {
                ReagentCode = reagentCode,
                Name = $"Reagent {reagentCode}",
                ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentDefinitions.Add(reagentDefinition);
        }
        else if (reagentDefinition.LiquidClassProfileId is null)
        {
            reagentDefinition.LiquidClassProfileId = liquidClassProfileId;
        }

        var workflowDefinition = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = $"{workflowCode} definition",
            WorkflowType = workflowType,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = status,
            ChangeNote = "Published for API integration test.",
            PublishedAtUtc = status == WorkflowVersionStatus.Published ? DateTimeOffset.UtcNow : null,
            RetiredAtUtc = status == WorkflowVersionStatus.Retired ? DateTimeOffset.UtcNow : null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "STEP",
            StepName = "Test step",
            ActionType = "Dispense",
            ReagentCode = reagentCode,
            VolumeUl = requiredVolumeUl,
            DurationSeconds = 60,
            TargetTemperatureDeciC = 250,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        workflowVersion.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = reagentCode,
            RequiredVolumeUl = requiredVolumeUl,
            IsRequired = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        dbContext.WorkflowVersions.Add(workflowVersion);
        await dbContext.SaveChangesAsync();
        return workflowVersion;
    }
}
