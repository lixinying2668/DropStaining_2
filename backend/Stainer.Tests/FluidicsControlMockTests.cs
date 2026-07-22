using System.Diagnostics;
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
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class FluidicsControlMockTests
{
    [Fact]
    public async Task Pump_wash_mixer_liquid_api_writes_state_telemetry_audit_and_replays_without_duplicate_execution()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        var initial = await client.GetFromJsonAsync<FluidicsStateResponse>("/api/fluidics/state");
        Assert.NotNull(initial);
        Assert.True(initial!.Ready);
        Assert.Equal(new[] { "PWM0:A", "PWM1:B", "PWM2:C", "PWM3:D" }, initial.Pumps.Select(x => $"{x.PwmChannelCode}:{x.DrawerCode}"));

        var forward = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/pumps/run", new
        {
            commandId = "cmd-fluidics-pump-forward",
            pwmChannelCode = "PWM0",
            speedPercent = 70,
            reason = "forward pump test"
        });
        Assert.Equal(70, forward.State.Pumps.Single(x => x.PwmChannelCode == "PWM0").SpeedPercent);
        Assert.Equal(PumpDirections.Forward, forward.State.Pumps.Single(x => x.PwmChannelCode == "PWM0").Direction);

        var forwardReplay = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/pumps/run", new
        {
            commandId = "cmd-fluidics-pump-forward",
            pwmChannelCode = "PWM0",
            speedPercent = 70,
            reason = "forward pump test"
        });
        Assert.True(forwardReplay.Replayed);

        _ = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/pumps/stop", new
        {
            commandId = "cmd-fluidics-pump-stop",
            pwmChannelCode = "PWM0",
            reason = "stop pump test"
        });

        var reverse = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/pumps/run", new
        {
            commandId = "cmd-fluidics-pump-reverse",
            pwmChannelCode = "PWM1",
            speedPercent = -45,
            durationMs = 10,
            reason = "reverse pump test"
        });
        var pwm1 = reverse.State.Pumps.Single(x => x.PwmChannelCode == "PWM1");
        Assert.Equal(PumpDirections.Stopped, pwm1.Direction);
        Assert.Equal(FluidicsStatuses.Completed, pwm1.Status);

        _ = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/wash", new
        {
            commandId = "cmd-fluidics-wash-inner",
            targetPointCode = "WashInnerLeft",
            speedPercent = 60,
            durationMs = 10,
            reason = "inner wall wash"
        });
        _ = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/wash", new
        {
            commandId = "cmd-fluidics-wash-outer",
            targetPointCode = "WashOuterLeft",
            speedPercent = 60,
            durationMs = 10,
            reason = "outer wall wash"
        });

        var started = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/mixers/B/start", new
        {
            commandId = "cmd-fluidics-mixer-start",
            roundKey = "manual-round-1",
            reason = "mix start"
        });
        Assert.Equal(FluidicsStatuses.Running, started.State.Mixers.Single(x => x.DrawerCode == "B").Status);
        var completed = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/mixers/B/complete", new
        {
            commandId = "cmd-fluidics-mixer-complete",
            roundKey = "manual-round-1",
            reason = "mix complete"
        });
        Assert.Equal(FluidicsStatuses.Completed, completed.State.Mixers.Single(x => x.DrawerCode == "B").Status);
        var stopped = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/mixers/B/stop", new
        {
            commandId = "cmd-fluidics-mixer-stop",
            reason = "mix stop"
        });
        Assert.Equal(FluidicsStatuses.Stopped, stopped.State.Mixers.Single(x => x.DrawerCode == "B").Status);

        var waterLow = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/liquid-levels", new
        {
            commandId = "cmd-fluidics-water-low",
            sourceType = LiquidSourceTypes.SystemWater,
            levelStatus = LiquidLevelStatuses.Low,
            reason = "water low"
        });
        Assert.Equal(LiquidLevelStatuses.Low, waterLow.State.LiquidLevels.Single(x => x.SourceType == LiquidSourceTypes.SystemWater).LevelStatus);
        var pbsEmpty = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/liquid-levels", new
        {
            commandId = "cmd-fluidics-pbs-empty",
            sourceType = LiquidSourceTypes.Pbs,
            levelStatus = LiquidLevelStatuses.Empty,
            reason = "pbs empty"
        });
        Assert.Equal(LiquidLevelStatuses.Empty, pbsEmpty.State.LiquidLevels.Single(x => x.SourceType == LiquidSourceTypes.Pbs).LevelStatus);
        var wasteFull = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/liquid-levels", new
        {
            commandId = "cmd-fluidics-waste-full",
            sourceType = LiquidSourceTypes.Waste,
            levelStatus = LiquidLevelStatuses.Full,
            reason = "waste full"
        });
        Assert.Equal(LiquidLevelStatuses.Full, wasteFull.State.LiquidLevels.Single(x => x.SourceType == LiquidSourceTypes.Waste).LevelStatus);
        var toxicFault = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/liquid-levels", new
        {
            commandId = "cmd-fluidics-toxic-sensor",
            sourceType = LiquidSourceTypes.ToxicWaste,
            levelStatus = LiquidLevelStatuses.SensorFault,
            reason = "toxic sensor abnormal"
        });
        Assert.Equal(LiquidLevelStatuses.SensorFault, toxicFault.State.LiquidLevels.Single(x => x.SourceType == LiquidSourceTypes.ToxicWaste).LevelStatus);
        var disconnected = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/faults", new
        {
            commandId = "cmd-fluidics-pbs-disconnect",
            targetType = "LiquidLevel",
            sourceType = LiquidSourceTypes.Pbs,
            faultType = FluidicsFaultTypes.Disconnected,
            reason = "sensor cable disconnected"
        });
        Assert.False(disconnected.State.LiquidLevels.Single(x => x.SourceType == LiquidSourceTypes.Pbs).IsConnected);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await dbContext.CommandReceipts.CountAsync(x => x.CommandId == "cmd-fluidics-pump-forward"));
        Assert.Equal(1, await dbContext.FluidicsTelemetry.CountAsync(x => x.CommandId == "cmd-fluidics-pump-forward"));
        Assert.True(await dbContext.FluidicsTelemetry.AnyAsync(x => x.TargetPointCode == "WashInnerLeft"));
        Assert.True(await dbContext.FluidicsTelemetry.AnyAsync(x => x.TargetPointCode == "WashOuterLeft"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "fluidics.pump.run"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "fluidics.wash.inner"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "fluidics.wash.outer"));
    }

    [Fact]
    public async Task Fluidics_faults_block_initialization_and_preflight_then_clear_recover_after_restart()
    {
        var context = CreateFactory();
        string failedRunId;
        await using (var factory = context.Factory)
        {
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin", "admin");

            var faulted = await PostJsonAsync<FluidicsMutationResponse>(client, "/api/fluidics/faults", new
            {
                commandId = "cmd-fluidics-mixer-fault",
                targetType = "Mixer",
                drawerCode = "C",
                faultType = FluidicsFaultTypes.SensorFailure,
                reason = "mixer sensor failed"
            });
            Assert.Equal(FluidicsStatuses.Faulted, faulted.State.Mixers.Single(x => x.DrawerCode == "C").Status);

            var initialization = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
            {
                commandId = "cmd-fluidics-init-failed"
            });
            Assert.False(initialization.Ok);
            Assert.Contains(initialization.Checks, x => x.ModuleCode == DeviceModules.Mixer && x.Status == DeviceInitializationCheckStatus.Failed);
            failedRunId = initialization.RunId!;

            var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
            Assert.Contains(preflight!.Issues, x => x.Area == "Fluidics" && x.Code == "mixer_not_ready");
        }

        await using var restarted = CreateFactory(context.DatabasePath).Factory;
        using var restartedClient = restarted.CreateClient();
        await LoginAsync(restartedClient, "admin", "admin");
        var persisted = await restartedClient.GetFromJsonAsync<FluidicsStateResponse>("/api/fluidics/state");
        Assert.Equal(FluidicsStatuses.Faulted, persisted!.Mixers.Single(x => x.DrawerCode == "C").Status);

        var cleared = await PostJsonAsync<FluidicsMutationResponse>(restartedClient, "/api/fluidics/faults/clear", new
        {
            commandId = "cmd-fluidics-mixer-clear",
            targetType = "Mixer",
            drawerCode = "C",
            reason = "mixer inspected"
        });
        Assert.Null(cleared.State.Mixers.Single(x => x.DrawerCode == "C").FaultCode);

        var retried = await PostJsonAsync<DeviceInitializationResponse>(restartedClient, $"/api/device-initialization/{failedRunId}/retry", new
        {
            commandId = "cmd-fluidics-init-retry",
            reason = "fluidics fault cleared"
        });
        Assert.True(retried.Ok, retried.Message);
        var afterClearPreflight = await restartedClient.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
        Assert.DoesNotContain(afterClearPreflight!.Issues, x => x.Area == "Fluidics");
    }

    [Fact]
    public async Task Runtime_mixer_waits_for_same_channel_liquid_additions_then_records_associated_telemetry()
    {
        await using var factory = CreateFactory().Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "fluidics-mix");

        string[] taskIds;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskIds = await CreateTwoSlideMixRoundAsync(dbContext);
            await AddBottleAsync(dbContext, "ABC", "ABC90020270101001", 5000, "R1");
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-fluidics-mix-run",
            stainingTaskIds = taskIds
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-fluidics-mix-start" });
        await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Completed);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var commands = (await verifyContext.DeviceCommandExecutions
            .Where(x => x.MachineRunId == run.RunId)
            .ToListAsync())
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
        var lastDispenseIndex = commands.FindLastIndex(x => x.CommandType == "Dispense");
        var firstMixIndex = commands.FindIndex(x => x.CommandType == "Mix");
        Assert.True(lastDispenseIndex >= 0);
        Assert.True(firstMixIndex > lastDispenseIndex);
        Assert.Equal(2, commands.Count(x => x.CommandType == "Mix" && x.Status == DeviceCommandStatus.Completed));
        Assert.True(await verifyContext.FluidicsTelemetry.AnyAsync(x =>
            x.SourceType == FluidicsTelemetrySourceTypes.Mixer
            && x.MachineRunId == run.RunId
            && x.WorkflowStepExecutionId != null
            && x.DeviceCommandExecutionId != null
            && x.Status == FluidicsStatuses.Completed));
    }

    [Fact]
    public async Task Runtime_mixer_timeout_unknown_faults_run_and_persists_state_after_restart()
    {
        var context = CreateFactory();
        string runId;
        await using (var factory = context.Factory)
        {
            using var client = factory.CreateClient();
            await LoginAsync(client, "admin", "admin");
            await InitializeDevicesAsync(client, "fluidics-timeout");
            string[] taskIds;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
                taskIds = await CreateTwoSlideMixRoundAsync(dbContext, slideCount: 1);
                await AddBottleAsync(dbContext, "ABC", "ABC90020270101002", 5000, "R1");
            }

            _ = await PostJsonAsync<DeviceFaultMutationResponse>(client, "/api/device/mock-faults", new
            {
                commandId = "cmd-fluidics-mixer-timeout-fault",
                moduleCode = DeviceModules.Mixer,
                faultType = DeviceFaultTypes.TimeoutNextCommand,
                reason = "mixer timeout",
                errorCode = "mixer_timeout",
                message = "Injected mixer timeout."
            });
            var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
            {
                commandId = "cmd-fluidics-timeout-run",
                stainingTaskIds = taskIds
            });
            runId = run.RunId;
            await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-fluidics-timeout-start" });
            await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);

            await using var verifyScope = factory.Services.CreateAsyncScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var mixCommand = await verifyContext.DeviceCommandExecutions.SingleAsync(x => x.MachineRunId == run.RunId && x.CommandType == "Mix");
            Assert.Equal(DeviceCommandStatus.Unknown, mixCommand.Status);
            Assert.Equal(FluidicsStatuses.TimedOut, (await verifyContext.MixerChannelStates.SingleAsync(x => x.DrawerCode == "A")).Status);
        }

        await using var restarted = CreateFactory(context.DatabasePath).Factory;
        using var restartedClient = restarted.CreateClient();
        await LoginAsync(restartedClient, "admin", "admin");
        var state = await restartedClient.GetFromJsonAsync<FluidicsStateResponse>("/api/fluidics/state");
        Assert.Equal(FluidicsStatuses.TimedOut, state!.Mixers.Single(x => x.DrawerCode == "A").Status);
        var detail = await restartedClient.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
        Assert.Equal(RuntimeLedgerStatus.Faulted, detail!.Status);
    }

    [Fact]
    public async Task Permissions_real_mode_and_generic_unknown_fault_are_fail_closed()
    {
        await using (var factory = CreateFactory().Factory)
        {
            using var operatorClient = factory.CreateClient();
            await LoginAsync(operatorClient, "operator", "operator");
            var forbidden = await operatorClient.PostAsJsonAsync("/api/fluidics/pumps/run", new
            {
                commandId = "cmd-fluidics-operator-forbidden",
                pwmChannelCode = "PWM0",
                speedPercent = 50
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        await using (var realFactory = CreateFactory(deviceMode: DeviceModes.Real).Factory)
        {
            using var realClient = realFactory.CreateClient();
            await LoginAsync(realClient, "admin", "admin");
            var rejected = await realClient.PostAsJsonAsync("/api/fluidics/pumps/run", new
            {
                commandId = "cmd-fluidics-real-reject",
                pwmChannelCode = "PWM0",
                speedPercent = 50,
                reason = "real mode rejects mock control"
            });
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }

        var context = CreateFactory();
        await using var factory2 = context.Factory;
        using var client = factory2.CreateClient();
        await LoginAsync(client, "admin", "admin");
        _ = await PostJsonAsync<DeviceFaultMutationResponse>(client, "/api/device/mock-faults", new
        {
            commandId = "cmd-fluidics-level-unknown-fault",
            moduleCode = DeviceModules.LiquidLevel,
            faultType = DeviceFaultTypes.ReturnUnknown,
            reason = "liquid level unknown",
            errorCode = "liquid_unknown",
            message = "Injected liquid level unknown."
        });
        var failed = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = "cmd-fluidics-unknown-init"
        });
        Assert.False(failed.Ok);
        Assert.Contains(failed.Checks, x => x.ModuleCode == DeviceModules.LiquidLevel && x.Status == DeviceInitializationCheckStatus.Unknown);
        var deviceState = await client.GetFromJsonAsync<DeviceStatusSnapshot>("/api/device/state");
        Assert.Equal(DeviceConnectionStatuses.Faulted, deviceState!.Modules.Single(x => x.ModuleCode == DeviceModules.LiquidLevel).ConnectionStatus);
    }

    private static FactoryContext CreateFactory(string? databasePath = null, string deviceMode = DeviceModes.Mock)
    {
        var root = databasePath is null
            ? Path.Combine(TestPaths.TempRoot, "stainer-fluidics-tests", Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(databasePath)!;
        databasePath ??= Path.Combine(root, "stainer.db");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = Path.Combine(root, $"machine-executor-{Guid.NewGuid():N}.lock"),
            ["Safety:LogDirectory"] = Path.Combine(root, "logs"),
            ["Device:Mode"] = deviceMode,
            ["Device:HardwareAvailable"] = deviceMode == DeviceModes.Real ? "true" : "false",
            ["Device:StartupInitialization:Enabled"] = "false",
            ["MachineExecutor:StepVisibleDelayMilliseconds"] = "0",
            ["Motion:PipetteAspirateVisibleMilliseconds"] = "0",
            ["Motion:PipetteWashVisibleMilliseconds"] = "0"
        };
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var pair in settings)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });
        return new FactoryContext(factory, databasePath);
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username, password = "123456", role });
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

    private static async Task InitializeDevicesAsync(HttpClient client, string suffix)
    {
        var result = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = $"cmd-device-initialize-{suffix}"
        });
        Assert.True(result.Ok, result.Message);
    }

    private static async Task<MachineRunDetailResponse> WaitForRunStatusAsync(HttpClient client, string runId, string status)
    {
        for (var i = 0; i < 120; i++)
        {
            var detail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
            Assert.NotNull(detail);
            if (detail!.Status == status)
            {
                return detail;
            }

            await Task.Delay(50);
        }

        var finalDetail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
        Assert.Fail($"Run did not reach {status}; final status was {finalDetail?.Status}.");
        throw new UnreachableException();
    }

    private static async Task<string[]> CreateTwoSlideMixRoundAsync(StainerDbContext dbContext, int slideCount = 2)
    {
        var defaultLiquidClass = await dbContext.LiquidClassProfiles
            .Include(x => x.EnabledVersion)
            .SingleAsync(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null);
        var definition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == "ABC");
        if (definition is null)
        {
            dbContext.ReagentDefinitions.Add(new ReagentDefinition
            {
                ReagentCode = "ABC",
                Name = "Reagent ABC",
                ReagentType = "test",
                LiquidClassProfileId = defaultLiquidClass.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var workflowDefinition = new WorkflowDefinition
        {
            Code = $"FLUIDICS-MIX-{Guid.NewGuid():N}"[..24],
            Name = "Fluidics mix workflow",
            WorkflowType = StainingTaskType.Ihc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "Fluidics mix test workflow.",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "PRIMARY_ANTIBODY",
            StepName = "Dispense primary",
            ActionType = "Dispense",
            ReagentCode = "ABC",
            VolumeUl = 100,
            DurationSeconds = 1,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 2,
            MajorStepCode = "PRIMARY_ANTIBODY",
            StepName = "Mix primary",
            ActionType = "Mix",
            DurationSeconds = 1,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        workflowVersion.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = "ABC",
            RequiredVolumeUl = 100 * slideCount,
            IsRequired = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var coordinateVersionId = await dbContext.CoordinateProfileVersions
            .Where(x => x.IsActive && x.Status == CoordinateProfileVersionStatus.Active)
            .Select(x => x.Id)
            .SingleAsync();
        var workflowSnapshot = $$"""{"workflowVersionId":"{{workflowVersion.Id}}","source":"fluidics-test"}""";
        var coordinateSnapshot = BuildCoordinateSnapshot(coordinateVersionId);
        var liquidSnapshot = await new LiquidClassSnapshotFactory(dbContext).FreezeForWorkflowAsync(workflowVersion);
        var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
        var batch = new ChannelBatch
        {
            DrawerId = drawer.Id,
            DrawerCode = drawer.Code,
            Status = RuntimeLedgerStatus.Pending,
            ExperimentType = StainingTaskType.Ihc,
            SelectedWorkflowVersion = workflowVersion,
            WorkflowSnapshotJson = workflowSnapshot,
            CoordinateProfileVersionId = coordinateVersionId,
            CoordinateSnapshotJson = coordinateSnapshot,
            CoordinateSelectionStatus = CoordinateSelectionStatus.Frozen,
            LiquidClassSnapshotJson = liquidSnapshot,
            LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen,
            WorkflowSelectionStatus = WorkflowSelectionStatus.Selected,
            WorkflowSelectedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ChannelBatches.Add(batch);

        var ids = new List<string>();
        var slots = await dbContext.PhysicalSlots
            .Where(x => x.Code == "A-01" || x.Code == "A-02")
            .OrderBy(x => x.Code)
            .Take(slideCount)
            .ToListAsync();
        foreach (var slot in slots)
        {
            var task = new StainingTask
            {
                TaskCode = $"TASK-FLUIDICS-{slot.Code}-{Guid.NewGuid():N}"[..30],
                TaskType = StainingTaskType.Ihc,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlotId = slot.Id,
                WorkflowDefinition = workflowDefinition,
                WorkflowVersion = workflowVersion,
                WorkflowSnapshotJson = workflowSnapshot,
                CandidateResultsJson = "[]",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.StainingTasks.Add(task);
            dbContext.SlideTasks.Add(new SlideTask
            {
                ChannelBatch = batch,
                StainingTask = task,
                PhysicalSlot = slot,
                SlotCode = slot.Code,
                TaskType = StainingTaskType.Ihc,
                Status = RuntimeLedgerStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            ids.Add(task.Id);
        }

        await dbContext.SaveChangesAsync();
        return ids.ToArray();
    }

    private static async Task<string> AddBottleAsync(StainerDbContext dbContext, string reagentCode, string barcode, int volumeUl, string positionCode)
    {
        var definition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == reagentCode);
        if (definition is null)
        {
            definition = new ReagentDefinition
            {
                ReagentCode = reagentCode,
                Name = $"Reagent {reagentCode}",
                ReagentType = "test",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentDefinitions.Add(definition);
            await dbContext.SaveChangesAsync();
        }

        var position = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == positionCode);
        var bottle = new ReagentBottle
        {
            ReagentDefinitionId = definition.Id,
            FullBarcode = barcode,
            ReagentCode = reagentCode,
            ProductionBatchNo = "20270101",
            SerialNo = Guid.NewGuid().ToString("N")[..3],
            InitialVolumeUl = volumeUl,
            RemainingVolumeUl = volumeUl,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Status = "Available",
            FirstScannedAtUtc = DateTimeOffset.UtcNow,
            LastScannedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ReagentBottles.Add(bottle);
        dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
        {
            ReagentBottle = bottle,
            ReagentRackPositionId = position.Id,
            PlacedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return bottle.Id;
    }

    private static string BuildCoordinateSnapshot(string coordinateVersionId)
    {
        var targetPoints = new[]
        {
            new { pointCode = "A-01", pointType = "SlideSlot", calibratedXUm = 100_000, calibratedYUm = 100_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "A-02", pointType = "SlideSlot", calibratedXUm = 125_000, calibratedYUm = 100_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "R1", pointType = "ReagentRack", calibratedXUm = 50_000, calibratedYUm = 30_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "WashInnerLeft", pointType = "WashInner", calibratedXUm = 340_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "WashOuterLeft", pointType = "WashOuter", calibratedXUm = 360_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true }
        };
        return JsonSerializer.Serialize(new { coordinateProfileVersionId = coordinateVersionId, source = "fluidics-test", targetPoints });
    }

    private sealed record FactoryContext(WebApplicationFactory<Program> Factory, string DatabasePath);
}
