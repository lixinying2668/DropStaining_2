using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class MockScannerLisDemoTests
{
    [Fact]
    public async Task Mock_sample_and_reagent_scanners_write_formal_sessions_and_do_not_create_bad_business_records()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        var sampleValid = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-tongling",
            count = 1,
            scenario = "Tongling",
            rawCode = "001",
            slotCode = "A-01"
        });
        Assert.Equal(SampleScanStatus.Valid, sampleValid.Items.Single().ScanStatus);
        Assert.Equal("001", sampleValid.Items.Single().PrimaryAntibodyCode);

        var sampleHospital = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-hospital",
            count = 1,
            scenario = "HospitalQr",
            rawCode = " HOSP-RAW-001\r\n",
            slotCode = "A-02"
        });
        Assert.Equal("HOSP-RAW-001", sampleHospital.Items.Single().NormalizedCode);

        _ = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-empty",
            count = 1,
            scenario = "Empty"
        });
        _ = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-damaged",
            count = 1,
            scenario = "Damaged"
        });

        _ = await PostJsonAsync<DeviceFaultMutationResponse>(client, "/api/device/mock-faults", new
        {
            commandId = "cmd-sample-timeout-fault",
            moduleCode = DeviceModules.SampleScanner,
            faultType = DeviceFaultTypes.TimeoutNextCommand,
            reason = "integration test sample timeout"
        });
        var sampleTimeout = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-timeout",
            count = 1,
            scenario = "Tongling",
            rawCode = "002"
        });
        Assert.Equal(SampleScanStatus.TimedOut, sampleTimeout.Items.Single().ScanStatus);

        var sampleDisconnect = await PostJsonAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-sample-disconnect",
            count = 1,
            scenario = "Disconnect"
        });
        Assert.Equal(SampleScanStatus.DeviceDisconnected, sampleDisconnect.Items.Single().ScanStatus);

        var reagentValid = await PostJsonAsync<MockReagentScanResponse>(client, "/api/reagents/scan", new
        {
            commandId = "cmd-reagent-r1-valid",
            scope = "position",
            position = "R1",
            scenario = "Valid",
            barcodesByPosition = new Dictionary<string, string?> { ["R1"] = "HEM05020270101001" }
        });
        Assert.Equal(ReagentScanResult.Valid, reagentValid.Results.Single().ScanResult);

        var reagentIllegal = await PostJsonAsync<MockReagentScanResponse>(client, "/api/reagents/scan", new
        {
            commandId = "cmd-reagent-r2-illegal",
            scope = "position",
            position = "R2",
            scenario = "Illegal17"
        });
        Assert.Equal(ReagentScanResult.Invalid, reagentIllegal.Results.Single().ScanResult);
        await using (var invalidScope = factory.Services.CreateAsyncScope())
        {
            var invalidContext = invalidScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.True(await invalidContext.ReagentScanItems.AnyAsync(x => x.ScanResult == ReagentScanResult.Invalid && x.RawBarcode!.StartsWith("ZZZ")));
        }

        var reagentColumn = await PostJsonAsync<MockReagentScanResponse>(client, "/api/reagents/scan", new
        {
            commandId = "cmd-reagent-column-empty",
            scope = "ch1",
            scenario = "Empty"
        });
        Assert.Equal(8, reagentColumn.PositionCount);
        Assert.All(reagentColumn.Results, x => Assert.Equal(ReagentScanResult.Empty, x.ScanResult));

        var reagentFullRack = await PostJsonAsync<MockReagentScanResponse>(client, "/api/reagents/scan", new
        {
            commandId = "cmd-reagent-full-valid",
            scope = "all",
            scenario = "Valid"
        });
        Assert.Equal(40, reagentFullRack.PositionCount);
        Assert.Equal("Completed", reagentFullRack.Session.Status);

        var rack = await client.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");
        Assert.Equal(40, rack!.Count);
        Assert.All(rack, x => Assert.Equal(ReagentScanResult.Valid, x.ScanState));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.SampleScanItems.AnyAsync(x => x.ScanStatus == SampleScanStatus.Empty));
        Assert.True(await dbContext.SampleScanItems.AnyAsync(x => x.ScanStatus == SampleScanStatus.Invalid));
        Assert.True(await dbContext.SampleScanItems.AnyAsync(x => x.ScanStatus == SampleScanStatus.TimedOut));
        Assert.True(await dbContext.SampleScanItems.AnyAsync(x => x.ScanStatus == SampleScanStatus.DeviceDisconnected));
        Assert.False(await dbContext.StainingTasks.AnyAsync());
        Assert.False(await dbContext.ReagentBottles.AnyAsync(x => x.ReagentCode == "ZZZ"));
        var sampleAudit = await dbContext.AuditLogs.FirstAsync(x => x.Action == "sample.mock_scan");
        Assert.Contains("rawCode", sampleAudit.Message);
        Assert.Contains("normalizedCode", sampleAudit.Message);
        Assert.Contains("scannedAtUtc", sampleAudit.Message);
        var reagentAudit = await dbContext.AuditLogs.FirstAsync(x => x.Action == "reagent.scan_confirm" && x.Message.Contains("rawBarcode"));
        Assert.Contains("scanStatus", reagentAudit.Message);
        Assert.Contains("errorReason", reagentAudit.Message);
    }

    [Fact]
    public async Task Mock_lis_query_path_records_candidates_selection_failures_and_creates_task_only_after_compatibility()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        string ihcVersionId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcVersionId = (await CreatePublishedWorkflowVersionAsync(dbContext, "LIS-IHC", StainingTaskType.Ihc, "P01")).Id;
            dbContext.PrimaryAntibodyWorkflowMappings.Add(new PrimaryAntibodyWorkflowMapping
            {
                PrimaryAntibodyCode = "P01",
                WorkflowVersionId = ihcVersionId
            });
            dbContext.MockLisEntries.AddRange(
                new MockLisEntry { NormalizedCode = "HOSP-SINGLE", PrimaryAntibodyCode = "P01", Scenario = MockLisScenario.Candidate },
                new MockLisEntry { NormalizedCode = "HOSP-NONE", Scenario = MockLisScenario.NoResult },
                new MockLisEntry { NormalizedCode = "HOSP-MULTI", PrimaryAntibodyCode = "P01", Scenario = MockLisScenario.Candidate },
                new MockLisEntry { NormalizedCode = "HOSP-MULTI", PrimaryAntibodyCode = "P02", Scenario = MockLisScenario.Candidate },
                new MockLisEntry { NormalizedCode = "HOSP-TIMEOUT", Scenario = MockLisScenario.Timeout },
                new MockLisEntry { NormalizedCode = "HOSP-ERROR", Scenario = MockLisScenario.Exception });
            await dbContext.SaveChangesAsync();
        }

        _ = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-lis-batch-a",
            drawerCode = "A"
        });
        _ = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-lis-select-a",
            drawerCode = "A",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionId
        });

        var singleQuery = await PostJsonAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-lis-single-query",
            rawCode = " HOSP-SINGLE\r\n"
        });
        Assert.Equal(LisQueryStatus.SingleCandidate, singleQuery.Status);
        Assert.Equal("HOSP-SINGLE", singleQuery.NormalizedCode);

        var task = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-lis-single-task",
            inputMode = "HospitalBarcode",
            rawCode = "HOSP-SINGLE",
            lisQueryLogId = singleQuery.LisQueryLogId,
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);
        Assert.Equal("Compatible", task.CompatibilityValidationStatus);

        var noneQuery = await PostJsonAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-lis-none-query",
            rawCode = "HOSP-NONE"
        });
        Assert.Equal(LisQueryStatus.NoResult, noneQuery.Status);
        var noneTask = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-lis-none-task",
            inputMode = "HospitalBarcode",
            rawCode = "HOSP-NONE",
            lisQueryLogId = noneQuery.LisQueryLogId,
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.NotFound, noneTask.StatusCode);

        var multiQuery = await PostJsonAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-lis-multi-query",
            rawCode = "HOSP-MULTI"
        });
        Assert.Equal(2, multiQuery.CandidatePrimaryAntibodyCodes.Count);
        var multiNeedsSelection = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-lis-multi-no-selection",
            inputMode = "HospitalBarcode",
            rawCode = "HOSP-MULTI",
            lisQueryLogId = multiQuery.LisQueryLogId,
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.Conflict, multiNeedsSelection.StatusCode);
        var selectionBody = await multiNeedsSelection.Content.ReadFromJsonAsync<TaskCreationResponse>();
        Assert.True(selectionBody!.RequiresSelection);

        var incompatible = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-lis-multi-incompatible",
            inputMode = "HospitalBarcode",
            rawCode = "HOSP-MULTI",
            lisQueryLogId = multiQuery.LisQueryLogId,
            selectedPrimaryAntibodyCode = "P02",
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        Assert.Equal("ihc_channel_workflow_incompatible", (await incompatible.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var timeoutQuery = await PostJsonAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-lis-timeout-query",
            rawCode = "HOSP-TIMEOUT"
        });
        Assert.Equal(LisQueryStatus.TimedOut, timeoutQuery.Status);
        var timeoutTask = await client.PostAsJsonAsync("/api/tasks/ihc", new
        {
            commandId = "cmd-lis-timeout-task",
            inputMode = "HospitalBarcode",
            rawCode = "HOSP-TIMEOUT",
            lisQueryLogId = timeoutQuery.LisQueryLogId,
            drawerCode = "A",
            slotCode = "A-02"
        });
        Assert.Equal(HttpStatusCode.GatewayTimeout, timeoutTask.StatusCode);

        var errorQuery = await PostJsonAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-lis-error-query",
            rawCode = "HOSP-ERROR"
        });
        Assert.Equal(LisQueryStatus.Failed, errorQuery.Status);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var singleLog = await verifyContext.LisQueryLogs.SingleAsync(x => x.Id == singleQuery.LisQueryLogId);
        Assert.Equal(LisQueryStatus.Selected, singleLog.Status);
        Assert.Equal("P01", singleLog.SelectedPrimaryAntibodyCode);
        var failedLog = await verifyContext.LisQueryLogs.SingleAsync(x => x.Id == multiQuery.LisQueryLogId);
        Assert.Equal(LisQueryStatus.CompatibilityFailed, failedLog.Status);
        Assert.Equal("P02", failedLog.SelectedPrimaryAntibodyCode);
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "lis.selection.compatibility_failed"));
        Assert.Equal(1, await verifyContext.StainingTasks.CountAsync());
    }

    [Fact]
    public async Task Mock_demo_data_seed_and_reset_are_development_mock_only_and_idempotent()
    {
        await using var factory = CreateFactory(environment: "Development");
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        var seeded = await PostJsonAsync<MockDemoDataResponse>(client, "/api/mock-demo-data/seed", new
        {
            commandId = "cmd-demo-seed"
        });
        Assert.True(seeded.CreatedCount > 0);

        int bottleCountAfterSeed;
        string heVersionId;
        string ihcVersionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            bottleCountAfterSeed = await dbContext.ReagentBottles.CountAsync();
            Assert.True(bottleCountAfterSeed >= 40);
            heVersionId = await dbContext.WorkflowVersions
                .Where(x => x.WorkflowDefinition!.Code == "MOCK-HE-DEMO" && x.Status == WorkflowVersionStatus.Published)
                .Select(x => x.Id)
                .SingleAsync();
            ihcVersionId = await dbContext.WorkflowVersions
                .Where(x => x.WorkflowDefinition!.Code == "MOCK-IHC-P01-DEMO" && x.Status == WorkflowVersionStatus.Published)
                .Select(x => x.Id)
                .SingleAsync();
            Assert.True(await dbContext.MockLisEntries.AnyAsync(x => x.NormalizedCode == "HOSP-MOCK-MULTI"));
            Assert.True(await dbContext.MockDemoDataTags.AnyAsync(x => x.DemoKey == MockDemoDataSeeder.DemoKey));
            Assert.True(await dbContext.ReagentScanItems.AnyAsync(x => x.RawBarcode == "ZZZ05020991231001" && x.ScanResult == ReagentScanResult.Invalid));
            Assert.True(await dbContext.ReagentBottles.AnyAsync(x => x.Status == "Expired" && x.ExpirationDate < DateOnly.FromDateTime(DateTime.UtcNow)));
            Assert.True(await dbContext.ReagentBottles.AnyAsync(x => x.Status == "Available" && x.RemainingVolumeUl < 100));
            var p01Batches = await dbContext.ReagentRackPlacements
                .Where(x => x.RemovedAtUtc == null && x.ReagentBottle!.ReagentCode == "P01")
                .Select(x => x.ReagentBottle!.ProductionBatchNo)
                .Distinct()
                .CountAsync();
            Assert.True(p01Batches >= 2);

            var userDefinition = await dbContext.ReagentDefinitions.SingleAsync(x => x.ReagentCode == "HEM");
            dbContext.ReagentBottles.Add(new ReagentBottle
            {
                ReagentDefinitionId = userDefinition.Id,
                FullBarcode = "HEM08020981231999",
                ReagentCode = "HEM",
                ProductionBatchNo = "20981231",
                SerialNo = "999",
                InitialVolumeUl = 8000,
                RemainingVolumeUl = 7000,
                ExpirationDate = new DateOnly(2098, 12, 31),
                Status = "Available",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
            bottleCountAfterSeed = await dbContext.ReagentBottles.CountAsync();
        }

        var heBatch = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-demo-batch-he",
            drawerCode = "A"
        });
        await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-demo-workflow-he",
            channelBatchId = heBatch.ChannelBatchId,
            drawerCode = "A",
            experimentType = StainingTaskType.He,
            workflowVersionId = heVersionId
        });
        await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-demo-task-he",
            slotCode = "A-01",
            drawerCode = "A",
            channelBatchId = heBatch.ChannelBatchId,
            workflowVersionId = heVersionId
        });

        var ihcBatch = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-demo-batch-ihc",
            drawerCode = "B"
        });
        await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-demo-workflow-ihc",
            channelBatchId = ihcBatch.ChannelBatchId,
            drawerCode = "B",
            experimentType = StainingTaskType.Ihc,
            workflowVersionId = ihcVersionId
        });
        await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-demo-task-ihc",
            inputMode = "PrimaryAntibody",
            rawCode = "P01",
            slotCode = "B-01",
            drawerCode = "B",
            channelBatchId = ihcBatch.ChannelBatchId,
            workflowVersionId = ihcVersionId
        });
        await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = "cmd-demo-device-init"
        });
        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.True(preflight!.Ok, JsonSerializer.Serialize(preflight.Issues));
        Assert.Equal(2, preflight.TaskCount);

        _ = await PostJsonAsync<MockDemoDataResponse>(client, "/api/mock-demo-data/seed", new
        {
            commandId = "cmd-demo-seed-again"
        });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.Equal(bottleCountAfterSeed, await dbContext.ReagentBottles.CountAsync());
        }

        var rejectedReset = await client.PostAsJsonAsync("/api/mock-demo-data/reset", new
        {
            commandId = "cmd-demo-reset-rejected",
            confirmation = "reset"
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedReset.StatusCode);

        var reset = await PostJsonAsync<MockDemoDataResponse>(client, "/api/mock-demo-data/reset", new
        {
            commandId = "cmd-demo-reset",
            confirmation = "RESET MOCK DEMO DATA"
        });
        Assert.True(reset.DeletedCount > 0);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            Assert.False(await dbContext.MockDemoDataTags.AnyAsync(x => x.DemoKey == MockDemoDataSeeder.DemoKey));
            Assert.Equal(1, await dbContext.ReagentBottles.CountAsync());
            Assert.True(await dbContext.ReagentBottles.AnyAsync(x => x.FullBarcode == "HEM08020981231999"));
            Assert.Equal(2, await dbContext.StainingTasks.CountAsync());
            Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "mock_demo.reset"));
        }

        await using var developmentRealFactory = CreateFactory(environment: "Development", deviceMode: DeviceModes.Real);
        using var developmentRealClient = developmentRealFactory.CreateClient();
        await LoginAsync(developmentRealClient, "admin", "admin");
        var developmentRealMode = await developmentRealClient.GetFromJsonAsync<DeviceModeStatusResponse>("/api/device-mode");
        Assert.Equal(DeviceModes.Real, developmentRealMode!.ConfiguredMode);
        Assert.Equal(DeviceModes.Mock, developmentRealMode.CurrentMode);
        var developmentRealFallback = await PostJsonAsync<MockDemoDataResponse>(developmentRealClient, "/api/mock-demo-data/seed", new
        {
            commandId = "cmd-demo-development-real-fallback"
        });
        Assert.True(developmentRealFallback.Ok);

        await using var productionFactory = CreateFactory(environment: "Production");
        using var productionClient = productionFactory.CreateClient();
        await LoginAsync(productionClient, "admin", "admin");
        var productionRejected = await productionClient.PostAsJsonAsync("/api/mock-demo-data/seed", new
        {
            commandId = "cmd-demo-production-rejected"
        });
        Assert.Equal(HttpStatusCode.Conflict, productionRejected.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment = "Testing", string deviceMode = DeviceModes.Mock)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-mock-scanner-lis-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.UseSetting("Device:Mode", deviceMode);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                        ["Device:Mode"] = deviceMode
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

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<WorkflowVersion> CreatePublishedWorkflowVersionAsync(StainerDbContext dbContext, string workflowCode, string workflowType, string reagentCode)
    {
        var reagent = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == reagentCode);
        if (reagent is null)
        {
            dbContext.ReagentDefinitions.Add(new ReagentDefinition
            {
                ReagentCode = reagentCode,
                Name = $"{reagentCode} reagent",
                ReagentType = "primary",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var workflow = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = $"{workflowCode} workflow",
            WorkflowType = workflowType,
            Description = "Mock scanner LIS integration test"
        };
        var version = new WorkflowVersion
        {
            VersionNo = 1,
            VersionLabel = "1",
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "test",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        version.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "PRIMARY_ANTIBODY",
            StepName = "Primary antibody",
            ActionType = "Dispense",
            ReagentCode = reagentCode,
            VolumeUl = 100,
            DurationSeconds = 1,
            FailureStrategy = "Stop"
        });
        version.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = reagentCode,
            RequiredVolumeUl = 100,
            IsRequired = true
        });
        workflow.Versions.Add(version);
        dbContext.WorkflowDefinitions.Add(workflow);
        await dbContext.SaveChangesAsync();
        return version;
    }
}
