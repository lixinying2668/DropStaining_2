using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class MockBackendEndToEndAcceptanceTests
{
    [Fact]
    public async Task Demo_data_runs_he_and_ihc_with_scans_lis_cross_bottle_dab_devices_and_traceability()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var seeded = await PostAsync<MockDemoDataResponse>(client, "/api/mock-demo-data/seed", new
        {
            commandId = "cmd-acceptance-demo-seed"
        });
        Assert.True(seeded.CreatedCount > 0);

        string heVersionId;
        string ihcVersionId;
        string dabABottleId;
        string dabBBottleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            heVersionId = await PublishedVersionIdAsync(dbContext, "SYSTEM-HE-FAST-V1");
            ihcVersionId = await PublishedVersionIdAsync(dbContext, "SYSTEM-IHC-STANDARD-40C-V1");

            var p01Bottles = await dbContext.ReagentBottles
                .Where(x => x.ReagentCode == "P01" && x.Status == "Available" && x.ExpirationDate >= DateOnly.FromDateTime(DateTime.UtcNow))
                .ToListAsync();
            Assert.True(p01Bottles.Count >= 4);
            foreach (var bottle in p01Bottles)
            {
                bottle.RemainingVolumeUl = Math.Min(bottle.RemainingVolumeUl, 80);
            }

            dabABottleId = await AvailableBottleIdAsync(dbContext, "DBA");
            dabBBottleId = await AvailableBottleIdAsync(dbContext, "DBB");
            await dbContext.SaveChangesAsync();
        }

        var tonglingScan = await PostAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-acceptance-scan-tongling",
            count = 1,
            scenario = "Tongling",
            rawCode = "001",
            slotCode = "B-01"
        });
        Assert.Equal("001", tonglingScan.Items.Single().PrimaryAntibodyCode);

        var hospitalScan = await PostAsync<SampleScanSessionResponse>(client, "/api/samples/scan", new
        {
            commandId = "cmd-acceptance-scan-hospital",
            count = 1,
            scenario = "HospitalQr",
            rawCode = " HOSP-MOCK-SINGLE\r\n",
            slotCode = "B-02"
        });
        Assert.Equal("HOSP-MOCK-SINGLE", hospitalScan.Items.Single().NormalizedCode);

        var singleLis = await PostAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-acceptance-lis-single",
            rawCode = hospitalScan.Items.Single().NormalizedCode
        });
        Assert.Equal(LisQueryStatus.SingleCandidate, singleLis.Status);

        var multiLis = await PostAsync<MockLisQueryResponse>(client, "/api/lis/mock-query", new
        {
            commandId = "cmd-acceptance-lis-multi",
            rawCode = "HOSP-MOCK-MULTI"
        });
        Assert.Equal(LisQueryStatus.MultipleCandidates, multiLis.Status);
        Assert.Equal(["P01", "P02"], multiLis.CandidatePrimaryAntibodyCodes.OrderBy(x => x).ToArray());

        var heBatch = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-acceptance-batch-he",
            drawerCode = "A"
        });
        await SelectWorkflowAsync(client, "cmd-acceptance-workflow-he", heBatch.ChannelBatchId, "A", StainingTaskType.He, heVersionId);
        var heTask = await PostAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-acceptance-task-he",
            slotCode = "A-01",
            drawerCode = "A",
            channelBatchId = heBatch.ChannelBatchId,
            workflowVersionId = heVersionId
        });

        var ihcBatch = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-acceptance-batch-ihc",
            drawerCode = "B"
        });
        await SelectWorkflowAsync(client, "cmd-acceptance-workflow-ihc", ihcBatch.ChannelBatchId, "B", StainingTaskType.Ihc, ihcVersionId);
        var tonglingTask = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-acceptance-task-tongling",
            slotCode = "B-01",
            drawerCode = "B",
            channelBatchId = ihcBatch.ChannelBatchId
        });
        var singleTask = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-acceptance-task-hospital-single",
            slotCode = "B-02",
            drawerCode = "B",
            channelBatchId = ihcBatch.ChannelBatchId
        });

        var multiTask = await PostAsync<TaskCreationResponse>(client, "/api/tasks/ihc", new
        {
            commandId = "cmd-acceptance-task-hospital-multi-selected",
            slotCode = "B-03",
            drawerCode = "B",
            channelBatchId = ihcBatch.ChannelBatchId
        });

        await ExercisePeripheralMocksAsync(client);
        var initialization = await PostAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = "cmd-acceptance-initialize"
        });
        Assert.True(initialization.Ok, initialization.Message);

        var ihcTaskIds = new[] { tonglingTask.TaskId!, singleTask.TaskId!, multiTask.TaskId! };
        var dabBatch = await PostAsync<DabBatchResponse>(client, "/api/dab/batches", new
        {
            commandId = "cmd-acceptance-dab-create",
            taskIds = ihcTaskIds,
            dabAReagentBottleId = dabABottleId,
            dabBReagentBottleId = dabBBottleId,
            positionCode = "M1"
        });
        Assert.Equal(DabBatchStatus.PendingPreparation, dabBatch.Status);

        var precheck = await PostAsync<PrecheckReportResponse>(client, "/api/prechecks", new
        {
            commandId = "cmd-acceptance-precheck-all"
        });
        Assert.True(precheck.Ok, JsonSerializer.Serialize(precheck.Checks));

        var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.NotNull(preflight);
        Assert.True(preflight!.Ok, JsonSerializer.Serialize(new { preflight.Issues, preflight.Checks }));
        Assert.Equal(4, preflight.TaskCount);

        var allTaskIds = new[] { heTask.TaskId! }.Concat(ihcTaskIds).ToArray();
        var run = await PostAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-acceptance-run-create",
            stainingTaskIds = allTaskIds,
            preflightStateHash = preflight.StateHash
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var p01Reservations = await dbContext.ReagentReservations
                .Where(x => x.MachineRunId == run.RunId && x.ReagentCode == "P01")
                .ToListAsync();
            Assert.True(p01Reservations.Select(x => x.ReagentBottleId).Distinct().Count() > 1);
            Assert.Equal(300, p01Reservations.Sum(x => x.ReservedVolumeUl));
        }

        await PostAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new
        {
            commandId = "cmd-acceptance-run-start",
            preflightStateHash = preflight.StateHash
        });
        await PostAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/pause", new
        {
            commandId = "cmd-acceptance-run-pause"
        });
        var paused = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Paused);
        Assert.Contains(paused.WorkflowExecutions.SelectMany(x => x.Steps), x => x.Status == RuntimeLedgerStatus.Completed);

        await PostAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/resume", new
        {
            commandId = "cmd-acceptance-run-resume"
        });
        var completed = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Completed);
        Assert.Equal(2, completed.ChannelBatches.Count);
        Assert.Contains(completed.ChannelBatches, x => x.ExperimentType == StainingTaskType.He && x.Slides.Count == 1);
        Assert.Contains(completed.ChannelBatches, x => x.ExperimentType == StainingTaskType.Ihc && x.Slides.Count == 3);

        // After a successful run the DAB batch is fully consumed and transitions
        // Available → Depleted (DabLifecycleService.ConsumeAsync when
        // RemainingVolumeUl hits zero). MarkExpiredAsync requires Available state
        // and is covered for the Available → Expired path by DabLifecycleTests
        // (lines 378-393); calling /expire on a Depleted batch is not a valid
        // transition, so this end-to-end scenario exercises the authoritative
        // Depleted → AwaitingCleaning → Cleaned path instead (mirroring
        // DabLifecycleTests lines 206-228).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var batch = await dbContext.DabBatches.SingleAsync(x => x.Id == dabBatch.BatchId);
            Assert.Equal(DabBatchStatus.Depleted, batch.Status);
            Assert.Equal(DabCleaningStatus.Required, batch.CleaningStatus);
        }

        await PostAsync<DabBatchResponse>(client, $"/api/dab/batches/{dabBatch.BatchId}/cleaning/start", new
        {
            commandId = "cmd-acceptance-dab-clean-start"
        });
        var cleaned = await PostAsync<DabBatchResponse>(client, $"/api/dab/batches/{dabBatch.BatchId}/cleaning/confirm", new
        {
            commandId = "cmd-acceptance-dab-clean-confirm"
        });
        Assert.Equal(DabBatchStatus.Cleaned, cleaned.Status);

        await AssertGetContainsAsync(client, $"/api/history/runs/{run.RunId}", run.RunId);
        await AssertGetContainsAsync(client, $"/api/history/reagent-consumptions?machineRunId={run.RunId}", "P01");
        await AssertGetContainsAsync(client, $"/api/audit/logs?machineRunId={run.RunId}", run.RunId);
        await AssertCsvAsync(client, $"/api/history/export/runs?machineRunId={run.RunId}", run.RunId);
        await AssertCsvAsync(client, $"/api/history/export/reagent-consumptions?machineRunId={run.RunId}", "P01");
        await AssertCsvAsync(client, $"/api/audit/export?machineRunId={run.RunId}", run.RunId);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(300, await verifyContext.ReagentConsumptions
            .Where(x => x.MachineRunId == run.RunId && x.ReagentCode == "P01")
            .SumAsync(x => x.VolumeUl));
        Assert.True(await verifyContext.DeviceCommandExecutions.AnyAsync(x => x.MachineRunId == run.RunId && x.CommandType == "Dab" && x.Status == DeviceCommandStatus.Completed));
        Assert.True(await verifyContext.PipettingOperations.AnyAsync(x => x.MachineRunId == run.RunId && x.NeedleCode == NeedleCodes.Needle1));
        Assert.True(await verifyContext.PipettingOperations.AnyAsync(x => x.MachineRunId == run.RunId && x.NeedleCode == NeedleCodes.Needle2));
        var p01DispenseTargets = await verifyContext.PipettingOperations
            .Where(x => x.MachineRunId == run.RunId
                && x.ReagentCode == "P01"
                && x.OperationType == PipettingOperationTypes.Dispense)
            .Select(x => x.TargetPointCode)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(new[] { "B-01", "B-02", "B-03" }, p01DispenseTargets);

        var executedCommands = await verifyContext.DeviceCommandExecutions
            .AsNoTracking()
            .Include(x => x.WorkflowStepExecution)
                .ThenInclude(x => x!.WorkflowExecution)
                    .ThenInclude(x => x!.SlideTask)
                        .ThenInclude(x => x!.ChannelBatch)
            .Where(x => x.MachineRunId == run.RunId && x.WorkflowStepExecution != null)
            .ToListAsync();
        var executedTimeline = executedCommands
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.WorkflowStepExecution!.StepNo,
                DrawerCode = x.WorkflowStepExecution.WorkflowExecution!.SlideTask!.ChannelBatch!.DrawerCode,
                x.WorkflowStepExecution.WorkflowExecution.SlideTask.SlotCode
            })
            .ToList();
        Assert.Equal(
            executedTimeline.OrderBy(x => x.StepNo).ThenBy(x => x.DrawerCode).ThenBy(x => x.SlotCode),
            executedTimeline);
        Assert.True(await verifyContext.FluidicsTelemetry.AnyAsync(x => x.CommandId == "cmd-acceptance-pump"));
        Assert.True(await verifyContext.TemperatureTelemetry.AnyAsync());
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "run.reagent_consumption"));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "export.csv"));
    }

    private static async Task ExercisePeripheralMocksAsync(HttpClient client)
    {
        await PostAsync<ThermalMutationResponse>(client, "/api/thermal/cooling", new
        {
            commandId = "cmd-acceptance-cooling-on",
            targetTemperatureDeciC = 80,
            isEnabled = true
        });
        await WaitForCoolingStatusAsync(client, ThermalStatuses.Stable);
        await PostAsync<ThermalMutationResponse>(client, "/api/thermal/cooling", new
        {
            commandId = "cmd-acceptance-cooling-off",
            targetTemperatureDeciC = 80,
            isEnabled = false
        });
        await WaitForCoolingStatusAsync(client, ThermalStatuses.Off);

        await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/pumps/run", new
        {
            commandId = "cmd-acceptance-pump",
            pwmChannelCode = "PWM0",
            speedPercent = 60,
            durationMs = 10,
            reason = "acceptance pump"
        });
        await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/wash", new
        {
            commandId = "cmd-acceptance-wash-inner",
            targetPointCode = "WashInnerLeft",
            speedPercent = 50,
            durationMs = 10,
            reason = "acceptance inner wash"
        });
        await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/wash", new
        {
            commandId = "cmd-acceptance-wash-outer",
            targetPointCode = "WashOuterLeft",
            speedPercent = 50,
            durationMs = 10,
            reason = "acceptance outer wash"
        });
        await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/mixers/B/start", new
        {
            commandId = "cmd-acceptance-mixer-start",
            roundKey = "acceptance-round",
            reason = "acceptance mixer"
        });
        await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/mixers/B/complete", new
        {
            commandId = "cmd-acceptance-mixer-complete",
            roundKey = "acceptance-round",
            reason = "acceptance mixer"
        });
        foreach (var sourceType in LiquidSourceTypes.All)
        {
            await PostAsync<FluidicsMutationResponse>(client, "/api/fluidics/liquid-levels", new
            {
                commandId = $"cmd-acceptance-level-{sourceType.ToLowerInvariant()}",
                sourceType,
                levelStatus = LiquidLevelStatuses.Normal,
                reason = "acceptance normal level"
            });
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-mock-e2e-acceptance", Guid.NewGuid().ToString("N"), "stainer.db");
        var leasePath = Path.Combine(Path.GetDirectoryName(databasePath)!, "machine-executor.lock");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Device:Mode", DeviceModes.Mock);
            builder.UseSetting("Device:StartupInitialization:Enabled", "false");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
            builder.UseSetting("MachineExecutor:LeasePath", leasePath);
            builder.UseSetting("MachineExecutor:StepVisibleDelayMilliseconds", "0");
            builder.UseSetting("Motion:PipetteAspirateVisibleMilliseconds", "0");
            builder.UseSetting("Motion:PipetteWashVisibleMilliseconds", "0");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Device:Mode"] = DeviceModes.Mock,
                ["Device:StartupInitialization:Enabled"] = "false",
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                ["MachineExecutor:LeasePath"] = leasePath,
                ["MachineExecutor:StepVisibleDelayMilliseconds"] = "0",
                ["Motion:PipetteAspirateVisibleMilliseconds"] = "0",
                ["Motion:PipetteWashVisibleMilliseconds"] = "0"
            }));
        });
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username = "admin", password = "123456", role = "admin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task SelectWorkflowAsync(HttpClient client, string commandId, string batchId, string drawerCode, string experimentType, string workflowVersionId)
    {
        _ = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/workflow-selection", new
        {
            commandId,
            channelBatchId = batchId,
            drawerCode,
            experimentType,
            workflowVersionId
        });
    }

    private static async Task<string> PublishedVersionIdAsync(StainerDbContext dbContext, string workflowCode)
    {
        return await dbContext.WorkflowVersions
            .Where(x => x.WorkflowDefinition!.Code == workflowCode && x.Status == WorkflowVersionStatus.Published)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static async Task<string> AvailableBottleIdAsync(StainerDbContext dbContext, string reagentCode)
    {
        return await dbContext.ReagentBottles
            .Where(x => x.ReagentCode == reagentCode && x.Status == "Available" && x.ExpirationDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderByDescending(x => x.RemainingVolumeUl)
            .Select(x => x.Id)
            .FirstAsync();
    }

    private static async Task<MachineRunDetailResponse> WaitForRunStatusAsync(HttpClient client, string runId, string expectedStatus)
    {
        for (var attempt = 0; attempt < 800; attempt++)
        {
            var detail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
            Assert.NotNull(detail);
            if (detail!.Status == expectedStatus)
            {
                return detail;
            }

            await Task.Delay(50);
        }

        var final = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
        Assert.Fail($"Run did not reach {expectedStatus}; final status was {final?.Status}; alarms={JsonSerializer.Serialize(final?.Alarms)}.");
        throw new UnreachableException();
    }

    private static async Task WaitForCoolingStatusAsync(HttpClient client, string expectedStatus)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<ThermalStateResponse>("/api/thermal/state");
            if (state?.Cooling.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Cooling did not reach {expectedStatus}.");
    }

    private static async Task AssertGetContainsAsync(HttpClient client, string url, string expected)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertCsvAsync(HttpClient client, string url, string expected)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }
}
