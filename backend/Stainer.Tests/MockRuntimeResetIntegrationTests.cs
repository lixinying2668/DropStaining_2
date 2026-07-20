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

/// <summary>
/// Integration tests for POST /api/mock-runtime/reset covering all 9 scenarios
/// from the mock-runtime-reset design document (handoff .omc/handoffs/mock-reset-design.md S4).
/// </summary>
public sealed class MockRuntimeResetIntegrationTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        TestPaths.TempRoot, "stainer-mock-reset-tests", Guid.NewGuid().ToString("N"), "stainer.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = CreateFactory(_databasePath, deviceMode: DeviceModes.Mock);
        _client = _factory.CreateClient();
        await LoginAsync(_client, "admin", "admin");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // =====================================================================
    // Scenario 1: Full data reset — success
    // =====================================================================
    [Fact]
    public async Task Full_runtime_data_reset_succeeds()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var seedResult = await SeedFullMockRuntimeAsync(db);
        Assert.True(seedResult.MachineRunId is not null);

        var response = await _client.PostAsJsonAsync("/api/mock-runtime/reset", new
        {
            commandId = "cmd-reset-s1-full",
            preflightStateHash = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MockRuntimeResetResponse>();
        Assert.NotNull(body);
        Assert.True(body.Ok);
        Assert.Equal("cmd-reset-s1-full", body.CommandId);
        Assert.False(body.Replayed);
        Assert.True(body.DeletedRows > 0);
        Assert.True(body.ResetStateRows > 0);
    }

    // =====================================================================
    // Scenario 2: All runtime tables empty after reset; command_receipts has only reset's id
    // =====================================================================
    [Fact]
    public async Task Runtime_tables_empty_after_reset_and_command_receipts_has_only_reset_id()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        await SeedFullMockRuntimeAsync(db);

        var cmdId = "cmd-reset-s2-empty";
        await PostResetAsync(_client, cmdId);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();

        // All runtime tables must be empty
        Assert.Empty(await vdb.MachineRuns.ToListAsync());
        Assert.Empty(await vdb.ChannelBatches.ToListAsync());
        Assert.Empty(await vdb.WorkflowExecutions.ToListAsync());
        Assert.Empty(await vdb.WorkflowStepExecutions.ToListAsync());
        Assert.Empty(await vdb.DeviceCommandExecutions.ToListAsync());
        Assert.Empty(await vdb.DispenseExecutions.ToListAsync());
        Assert.Empty(await vdb.PipettingOperations.ToListAsync());
        Assert.Empty(await vdb.ReagentConsumptions.ToListAsync());
        Assert.Empty(await vdb.ReagentReservations.ToListAsync());
        Assert.Empty(await vdb.SystemLiquidUsages.ToListAsync());
        Assert.Empty(await vdb.DabBatches.ToListAsync());
        Assert.Empty(await vdb.DabBatchTasks.ToListAsync());
        Assert.Empty(await vdb.DabBatchUsages.ToListAsync());
        Assert.Empty(await vdb.DabRepreparationPlans.ToListAsync());
        Assert.Empty(await vdb.Alarms.ToListAsync());
        Assert.Empty(await vdb.AlarmActions.ToListAsync());
        Assert.Empty(await vdb.SampleScanSessions.ToListAsync());
        Assert.Empty(await vdb.SampleScanItems.ToListAsync());
        Assert.Empty(await vdb.ReagentScanSessions.ToListAsync());
        Assert.Empty(await vdb.ReagentScanItems.ToListAsync());
        Assert.Empty(await vdb.ReagentRackPlacements.ToListAsync());
        Assert.Empty(await vdb.DevicePrecheckRuns.ToListAsync());
        Assert.Empty(await vdb.DeviceCommunicationRecords.ToListAsync());
        Assert.Empty(await vdb.DeviceInitializationRuns.ToListAsync());
        Assert.Empty(await vdb.DeviceInitializationChecks.ToListAsync());
        Assert.Empty(await vdb.TemperatureTelemetry.ToListAsync());
        Assert.Empty(await vdb.FluidicsTelemetry.ToListAsync());
        Assert.Empty(await vdb.WorkflowAssignmentHistory.ToListAsync());
        Assert.Empty(await vdb.LisQueryLogs.ToListAsync());
        Assert.Empty(await vdb.MachineResourceLeases.ToListAsync());
        Assert.Empty(await vdb.StainingTasks.ToListAsync());
        Assert.Empty(await vdb.SlideTasks.ToListAsync());
        Assert.Empty(await vdb.ReagentBottles.ToListAsync());

        // command_receipts: only the reset command
        var receipts = await vdb.CommandReceipts.ToListAsync();
        Assert.Single(receipts);
        Assert.Equal(cmdId, receipts[0].CommandId);
    }

    // =====================================================================
    // Scenario 3: Base configuration retained (unchanged after reset)
    // =====================================================================
    [Fact]
    public async Task Base_configuration_retained_after_reset()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        await SeedFullMockRuntimeAsync(db);

        // Snapshot base config counts before reset
        await using var preScope = _factory.Services.CreateAsyncScope();
        var preDb = preScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var preUserCount = await preDb.Users.CountAsync();
        var preRoleCount = await preDb.Roles.CountAsync();
        var preDrawerCount = await preDb.Drawers.CountAsync();
        var preSlotCount = await preDb.PhysicalSlots.CountAsync();
        var preRackPositionCount = await preDb.ReagentRackPositions.CountAsync();
        var preWorkflowDefCount = await preDb.WorkflowDefinitions.CountAsync();
        var preWorkflowVerCount = await preDb.WorkflowVersions.CountAsync();
        var preWorkflowStepCount = await preDb.WorkflowSteps.CountAsync();
        var preReqCount = await preDb.WorkflowReagentRequirements.CountAsync();
        var preMappingCount = await preDb.PrimaryAntibodyWorkflowMappings.CountAsync();
        var preReagentDefCount = await preDb.ReagentDefinitions.CountAsync();
        var preLisCount = await preDb.HospitalBarcodeMappings.CountAsync();
        var preMockLisCount = await preDb.Set<MockLisEntry>().CountAsync();
        var preLiquidClassCount = await preDb.LiquidClassProfiles.CountAsync();

        await PostResetAsync(_client, "cmd-reset-s3-retained");

        // Verify counts unchanged
        await using var postScope = _factory.Services.CreateAsyncScope();
        var postDb = postScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(preUserCount, await postDb.Users.CountAsync());
        Assert.Equal(preRoleCount, await postDb.Roles.CountAsync());
        Assert.Equal(preDrawerCount, await postDb.Drawers.CountAsync());
        Assert.Equal(preSlotCount, await postDb.PhysicalSlots.CountAsync());
        Assert.Equal(preRackPositionCount, await postDb.ReagentRackPositions.CountAsync());
        Assert.Equal(preWorkflowDefCount, await postDb.WorkflowDefinitions.CountAsync());
        Assert.Equal(preWorkflowVerCount, await postDb.WorkflowVersions.CountAsync());
        Assert.Equal(preWorkflowStepCount, await postDb.WorkflowSteps.CountAsync());
        Assert.Equal(preReqCount, await postDb.WorkflowReagentRequirements.CountAsync());
        Assert.Equal(preMappingCount, await postDb.PrimaryAntibodyWorkflowMappings.CountAsync());
        Assert.Equal(preReagentDefCount, await postDb.ReagentDefinitions.CountAsync());
        Assert.Equal(preLisCount, await postDb.HospitalBarcodeMappings.CountAsync());
        Assert.Equal(preMockLisCount, await postDb.Set<MockLisEntry>().CountAsync());
        Assert.Equal(preLiquidClassCount, await postDb.LiquidClassProfiles.CountAsync());
    }

    // =====================================================================
    // Scenario 4: Device states restored to MockDeviceBaseline values
    // =====================================================================
    [Fact]
    public async Task Device_states_restored_to_baseline_after_reset()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        // Device-state tables are lazy-seeded by the control services' EnsureSeededCoreAsync;
        // trigger them via DI so reset's UPDATEs have rows to restore (and we can perturb them).
        await EnsureDeviceStatesSeededAsync(_factory);
        // Perturb device states so we can verify they get restored
        await PerturbDeviceStatesAsync(db);
        await SeedFullMockRuntimeAsync(db);

        await PostResetAsync(_client, "cmd-reset-s4-baseline");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();

        // ThermalPointStates
        var thermals = await vdb.ThermalPointStates.ToListAsync();
        Assert.NotEmpty(thermals);
        foreach (var t in thermals)
        {
            Assert.Equal(MockDeviceBaseline.ThermalCurrentTemperatureDeciC, t.CurrentTemperatureDeciC);
            Assert.Equal(MockDeviceBaseline.ThermalTargetTemperatureDeciC, t.TargetTemperatureDeciC);
            Assert.Equal(MockDeviceBaseline.ThermalIsEnabled, t.IsEnabled);
            Assert.Equal(MockDeviceBaseline.ThermalIsConnected, t.IsConnected);
            Assert.Equal(MockDeviceBaseline.ThermalStatus, t.Status);
            Assert.Null(t.FaultCode);
            Assert.Null(t.FaultMessage);
        }

        // CoolingUnitState
        var cooling = await vdb.CoolingUnitStates.SingleAsync();
        Assert.Equal(MockDeviceBaseline.CoolingCurrentTemperatureDeciC, cooling.CurrentTemperatureDeciC);
        Assert.Equal(MockDeviceBaseline.CoolingTargetTemperatureDeciC, cooling.TargetTemperatureDeciC);
        Assert.Equal(MockDeviceBaseline.CoolingIsEnabled, cooling.IsEnabled);
        Assert.Equal(MockDeviceBaseline.CoolingIsConnected, cooling.IsConnected);
        Assert.Equal(MockDeviceBaseline.CoolingStatus, cooling.Status);
        Assert.Null(cooling.FaultCode);
        Assert.Null(cooling.FaultMessage);

        // PumpChannelStates
        var pumps = await vdb.PumpChannelStates.ToListAsync();
        Assert.NotEmpty(pumps);
        foreach (var p in pumps)
        {
            Assert.Equal(MockDeviceBaseline.PumpSpeedPercent, p.SpeedPercent);
            Assert.Equal(MockDeviceBaseline.PumpDirection, p.Direction);
            Assert.Equal(MockDeviceBaseline.PumpStatus, p.Status);
            Assert.Equal(MockDeviceBaseline.PumpIsConnected, p.IsConnected);
            Assert.Null(p.TargetPointCode);
            Assert.Null(p.DurationMs);
            Assert.Null(p.CurrentCommandId);
            Assert.Null(p.MachineRunId);
            Assert.Null(p.WorkflowStepExecutionId);
            Assert.Null(p.DeviceCommandExecutionId);
            Assert.Null(p.FaultCode);
            Assert.Null(p.FaultMessage);
        }

        // MixerChannelStates
        var mixers = await vdb.MixerChannelStates.ToListAsync();
        Assert.NotEmpty(mixers);
        foreach (var m in mixers)
        {
            Assert.Equal(MockDeviceBaseline.MixerStatus, m.Status);
            Assert.Equal(MockDeviceBaseline.MixerIsConnected, m.IsConnected);
            Assert.Null(m.CurrentRoundKey);
            Assert.Null(m.CurrentCommandId);
            Assert.Null(m.MachineRunId);
            Assert.Null(m.WorkflowStepExecutionId);
            Assert.Null(m.DeviceCommandExecutionId);
            Assert.Null(m.FaultCode);
            Assert.Null(m.FaultMessage);
        }

        // LiquidContainerStates
        var liquids = await vdb.LiquidContainerStates.ToListAsync();
        Assert.Equal(4, liquids.Count);
        foreach (var liquid in MockDeviceBaseline.Liquids)
        {
            var state = liquids.Single(l => l.SourceType == liquid.SourceType);
            Assert.Equal(liquid.DisplayName, state.DisplayName);
            Assert.Equal(liquid.IsWaste, state.IsWaste);
            Assert.Equal(liquid.CapacityUl, state.CapacityUl);
            Assert.Equal(liquid.CurrentVolumeUl, state.CurrentVolumeUl);
            Assert.Equal(liquid.LowThresholdUl, state.LowThresholdUl);
            Assert.Equal(liquid.FullThresholdUl, state.FullThresholdUl);
            Assert.Equal(MockDeviceBaseline.LiquidLevelStatus, state.LevelStatus);
            Assert.Equal(MockDeviceBaseline.LiquidIsConnected, state.IsConnected);
            Assert.Null(state.FaultCode);
            Assert.Null(state.FaultMessage);
        }

        // RobotArmStates
        var robots = await vdb.RobotArmStates.ToListAsync();
        Assert.NotEmpty(robots);
        foreach (var r in robots)
        {
            Assert.Equal(MockDeviceBaseline.RobotArmIsHomed, r.IsHomed);
            Assert.Equal(MockDeviceBaseline.RobotArmIsConnected, r.IsConnected);
            Assert.Equal(MockDeviceBaseline.RobotArmStatus, r.Status);
            Assert.Null(r.CurrentTargetPointCode);
            Assert.Null(r.CurrentXUm);
            Assert.Null(r.CurrentYUm);
            Assert.Null(r.CurrentZUm);
            Assert.Null(r.CoordinateProfileVersionId);
            Assert.Null(r.CurrentCommandId);
            Assert.Null(r.MachineRunId);
            Assert.Null(r.WorkflowStepExecutionId);
            Assert.Null(r.DeviceCommandExecutionId);
            Assert.Null(r.LastErrorCode);
            Assert.Null(r.LastErrorMessage);
        }

        // NeedleStates
        var needles = await vdb.NeedleStates.ToListAsync();
        Assert.NotEmpty(needles);
        foreach (var n in needles)
        {
            Assert.Equal(MockDeviceBaseline.NeedleLoadedSourceType, n.LoadedSourceType);
            Assert.Equal(MockDeviceBaseline.NeedleVolumeUl, n.VolumeUl);
            Assert.Equal(MockDeviceBaseline.NeedleLiquidClassParametersJson, n.LiquidClassParametersJson);
            Assert.Equal(MockDeviceBaseline.NeedleNeedsWash, n.NeedsWash);
            Assert.Equal(MockDeviceBaseline.NeedleStatus, n.Status);
            Assert.Equal(MockDeviceBaseline.NeedleIsConnected, n.IsConnected);
            Assert.Null(n.LoadedReagentCode);
            Assert.Null(n.SourceBottleId);
            Assert.Null(n.DabBatchId);
            Assert.Null(n.SystemLiquidSourceType);
            Assert.Null(n.SourcePositionCode);
            Assert.Null(n.LiquidClassVersionId);
            Assert.Null(n.LiquidClassVersionNo);
            Assert.Null(n.CurrentCommandId);
            Assert.Null(n.MachineRunId);
            Assert.Null(n.WorkflowStepExecutionId);
            Assert.Null(n.DeviceCommandExecutionId);
            Assert.Null(n.LastErrorCode);
            Assert.Null(n.LastErrorMessage);
        }

        // DabMixPositions: active_dab_batch_id must be NULL
        var dabPositions = await vdb.DabMixPositions.ToListAsync();
        Assert.NotEmpty(dabPositions);
        foreach (var d in dabPositions)
        {
            Assert.Null(d.ActiveDabBatchId);
        }
    }

    // =====================================================================
    // Scenario 5: Slot A-01 can accept a new task after reset
    // =====================================================================
    [Fact]
    public async Task Slot_A01_can_accept_new_task_after_reset()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        await SeedFullMockRuntimeAsync(db);
        // Confirm A-01 is occupied before reset
        var a01Slot = await db.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
        Assert.True(await db.StainingTasks.AnyAsync(t => t.PhysicalSlotId == a01Slot.Id));

        await PostResetAsync(_client, "cmd-reset-s5-slot");

        // After reset, A-01 should be free — create a new HE task
        string? heVersionId;
        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var sdb = setupScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            // Create a simple HE workflow if none exists
            heVersionId = await sdb.WorkflowVersions
                .Where(v => v.WorkflowDefinition != null && v.WorkflowDefinition.WorkflowType == StainingTaskType.He && v.Status == WorkflowVersionStatus.Published)
                .Select(v => v.Id)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(heVersionId))
            {
                // Use a simple HE workflow
                var liquidClassProfileId = await sdb.LiquidClassProfiles
                    .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
                    .Select(x => x.Id)
                    .SingleAsync();
                var reagentDef = await sdb.ReagentDefinitions.FirstOrDefaultAsync(x => x.ReagentCode == "HEM");
                if (reagentDef is null)
                {
                    reagentDef = new ReagentDefinition
                    {
                        ReagentCode = "HEM", Name = "Hematoxylin", ReagentType = "test",
                        LiquidClassProfileId = liquidClassProfileId, CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    sdb.ReagentDefinitions.Add(reagentDef);
                    await sdb.SaveChangesAsync();
                }
                var definition = new WorkflowDefinition
                {
                    Code = "HE-S5-TEST", Name = "HE S5 Test", WorkflowType = StainingTaskType.He,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                var version = new WorkflowVersion
                {
                    WorkflowDefinition = definition, VersionNo = 1, VersionLabel = "1.0",
                    Status = WorkflowVersionStatus.Published,
                    PublishedAtUtc = DateTimeOffset.UtcNow,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                version.Steps.Add(new WorkflowStep
                {
                    StepNo = 1, MajorStepCode = "HEMATOXYLIN", StepName = "Hematoxylin",
                    ActionType = "Dispense", ReagentCode = "HEM", VolumeUl = 100,
                    DurationSeconds = 60, TargetTemperatureDeciC = 250,
                    FailureStrategy = "Stop", CreatedAtUtc = DateTimeOffset.UtcNow
                });
                version.ReagentRequirements.Add(new WorkflowReagentRequirement
                {
                    ReagentCode = "HEM", RequiredVolumeUl = 100, IsRequired = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                sdb.WorkflowVersions.Add(version);
                await sdb.SaveChangesAsync();
                heVersionId = version.Id;
            }
        }

        // Create a batch, select workflow, create task on A-01
        var batchId = await CreateEmptyChannelBatchAsync(_factory, "A");
        _ = await PostAsync<ChannelBatchWorkflowResponse>(_client, "/api/channel-batches/workflow-selection", new
        {
            commandId = "cmd-reset-s5-select",
            channelBatchId = batchId,
            drawerCode = "A",
            experimentType = "HE",
            workflowVersionId = heVersionId
        });

        var task = await PostAsync<TaskCreationResponse>(_client, "/api/tasks/he", new
        {
            commandId = "cmd-reset-s5-task",
            drawerCode = "A",
            slotCode = "A-01"
        });
        Assert.True(task.Ok);
    }

    // =====================================================================
    // Scenario 6: Non-Mock mode rejected (mock_runtime_reset_mode_required)
    // =====================================================================
    [Fact]
    public async Task Non_mock_mode_rejected_with_mode_required_error()
    {
        // Create a separate factory with Real mode
        await using var realFactory = CreateFactory(
            Path.Combine(TestPaths.TempRoot, "stainer-mock-reset-tests", Guid.NewGuid().ToString("N"), "stainer.db"),
            deviceMode: DeviceModes.Real);
        using var realClient = realFactory.CreateClient();
        await LoginAsync(realClient, "admin", "admin");

        var response = await realClient.PostAsJsonAsync("/api/mock-runtime/reset", new
        {
            commandId = "cmd-reset-s6-nonmock",
            preflightStateHash = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mock_runtime_reset_mode_required", body.GetProperty("code").GetString());

        realClient.Dispose();
        await realFactory.DisposeAsync();
    }

    // =====================================================================
    // Scenario 7: Active run rejected (mock_runtime_reset_active_run)
    // =====================================================================
    [Fact]
    public async Task Active_run_rejected_with_active_run_error()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var seedResult = await SeedFullMockRuntimeAsync(db);

        // Set the machine run to Running status (bypassing normal flow)
        var run = await db.MachineRuns.SingleAsync(x => x.Id == seedResult.MachineRunId);
        run.Status = RuntimeLedgerStatus.Running;
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/mock-runtime/reset", new
        {
            commandId = "cmd-reset-s7-activerun",
            preflightStateHash = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mock_runtime_reset_active_run", body.GetProperty("code").GetString());

        // Verify no data was deleted
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await vdb.MachineRuns.AnyAsync());
        Assert.True(await vdb.StainingTasks.AnyAsync());
        Assert.True(await vdb.ChannelBatches.AnyAsync());
    }

    // =====================================================================
    // Scenario 8: Transaction rollback via TestFailureFactory
    // =====================================================================
    [Fact]
    public async Task Transaction_rollback_when_failure_injected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var seedResult = await SeedFullMockRuntimeAsync(db);

        // Capture pre-failure state for comparison
        var preMachineRunCount = await db.MachineRuns.CountAsync();
        var preChannelBatchCount = await db.ChannelBatches.CountAsync();
        var preStainingTaskCount = await db.StainingTasks.CountAsync();

        // Perturb device states so we can verify they are NOT changed on rollback
        await PerturbDeviceStatesAsync(db);

        // Inject failure
        MockRuntimeResetService.TestFailureFactory = () => new InvalidOperationException("simulated mid-reset failure");
        try
        {
            var response = await _client.PostAsJsonAsync("/api/mock-runtime/reset", new
            {
                commandId = "cmd-reset-s8-rollback",
                preflightStateHash = (string?)null
            });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("mock_runtime_reset_failed", body.GetProperty("code").GetString());
        }
        finally
        {
            MockRuntimeResetService.TestFailureFactory = null;
        }

        // Verify rollback: runtime data unchanged
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(preMachineRunCount, await vdb.MachineRuns.CountAsync());
        Assert.Equal(preChannelBatchCount, await vdb.ChannelBatches.CountAsync());
        Assert.Equal(preStainingTaskCount, await vdb.StainingTasks.CountAsync());

        // Verify command_receipts does NOT contain the failed command
        Assert.False(await vdb.CommandReceipts.AnyAsync(r => r.CommandId == "cmd-reset-s8-rollback"));

        // Verify device states were NOT restored (perturbation still present)
        var thermals = await vdb.ThermalPointStates.ToListAsync();
        Assert.All(thermals, t =>
        {
            // Perturbed values should still be there (not baseline)
            Assert.Equal(999, t.CurrentTemperatureDeciC);
        });
    }

    // =====================================================================
    // Scenario 9: Idempotent replay — same commandId twice
    // =====================================================================
    [Fact]
    public async Task Idempotent_replay_same_command_id_returns_replayed_true()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        await SeedFullMockRuntimeAsync(db);

        var cmdId = "cmd-reset-s9-idempotent";

        // First call
        var first = await PostResetAsync(_client, cmdId);
        Assert.True(first.Ok);
        Assert.False(first.Replayed);

        // Second call with same commandId
        var second = await PostResetAsync(_client, cmdId);
        Assert.True(second.Ok);
        Assert.True(second.Replayed);

        // Responses should be consistent
        Assert.Equal(first.DeletedRows, second.DeletedRows);
        Assert.Equal(first.ResetStateRows, second.ResetStateRows);
        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.RetainedSummary, second.RetainedSummary);
    }

    // =====================================================================
    // Scenario 10: A Faulted run does NOT block reset (operators can clean up after a Mock fault)
    // =====================================================================
    [Fact]
    public async Task Faulted_run_does_not_block_reset()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var seedResult = await SeedFullMockRuntimeAsync(db);

        // A faulted run is not "truly executing" — reset must clear it so Mock can recover.
        var run = await db.MachineRuns.SingleAsync(x => x.Id == seedResult.MachineRunId);
        run.Status = RuntimeLedgerStatus.Faulted;
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/mock-runtime/reset", new
        {
            commandId = "cmd-reset-s10-faulted",
            preflightStateHash = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MockRuntimeResetResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Ok);

        // The faulted run and all runtime data were cleared.
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await vdb.MachineRuns.AnyAsync());
        Assert.False(await vdb.ChannelBatches.AnyAsync());
    }

    // =====================================================================
    // Scenario 11: Runtime audit cleared, config/account audit retained
    // (covers task.*/channel.*/channel_batch.*/dab.*/fluidics.*/mock_runtime.* that the old
    //  narrow filter missed, plus prior mock_runtime.reset audits)
    // =====================================================================
    [Fact]
    public async Task Runtime_audit_cleared_but_config_audit_retained()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        await SeedFullMockRuntimeAsync(db);

        var now = DateTimeOffset.UtcNow;
        // Runtime audit rows — the categories the previous narrow prefix filter failed to delete.
        (string Action, string EntityType)[] runtimeAudits =
        [
            ("task.create_he", "StainingTask"),
            ("channel.workflow.select", "ChannelBatch"),
            ("channel_batch.ensure_active", "ChannelBatch"),
            ("channelbatch.workflow_selected", "ChannelBatch"),
            ("run.create", "MachineRun"),
            ("run.dab_consumption", "MachineRun"),
            ("dab.batch_create", "DabBatch"),
            ("fluidics.pump.run", "PumpChannelState"),
            ("alarm.acknowledge", "Alarm"),
            ("sample.mock_scan", "SampleScanSession"),
            ("reagent.scan_session.start", "ReagentScanSession"),
            ("device.mock_fault.configured", "DeviceFault"),
            ("startup.recovery.scan", "MachineRun"),
            ("mock_runtime.reset", "MockRuntime"), // a PRIOR reset's audit must also be cleared
        ];
        // Config/account audit rows — must be retained.
        (string Action, string EntityType)[] configAudits =
        [
            ("workflow.version.publish", "WorkflowVersion"),
            ("workflow.step.create", "WorkflowStep"),
            ("primary_antibody_mapping.create", "PrimaryAntibodyWorkflowMapping"),
            ("user.create", "User"),
            ("auth.login", "User"),
            ("coordinate_profile.version.create", "CoordinateProfileVersion"),
            ("scanner_profile.create", "ScannerProfile"),
            ("serial_connection.save", "SerialConnectionProfile"),
            ("precision_calibration.save", "PrecisionCalibrationProfile"),
            ("engineering.device_profile.save", "DeviceProfile"),
            ("mock_demo.seed", "MockDemoData"),
        ];
        foreach (var (action, et) in runtimeAudits.Concat(configAudits))
        {
            db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                EntityType = et,
                EntityId = "audit-seed",
                ActorUserId = null,
                Message = "{}",
                CreatedAtUtc = now,
            });
        }
        await db.SaveChangesAsync();

        await PostResetAsync(_client, "cmd-reset-s11-audit");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();

        foreach (var (action, et) in runtimeAudits)
        {
            Assert.False(await vdb.AuditLogs.AnyAsync(a => a.Action == action && a.EntityType == et && a.EntityId == "audit-seed"),
                $"runtime audit should be cleared: {action}/{et}");
        }
        foreach (var (action, et) in configAudits)
        {
            Assert.True(await vdb.AuditLogs.AnyAsync(a => a.Action == action && a.EntityType == et && a.EntityId == "audit-seed"),
                $"config/account audit should be retained: {action}/{et}");
        }

        // The prior mock_runtime.reset audit was deleted; exactly one NEW mock_runtime.reset audit
        // (for this reset) remains.
        var resetAudits = await vdb.AuditLogs.Where(a => a.Action == "mock_runtime.reset").ToListAsync();
        Assert.Single(resetAudits);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static WebApplicationFactory<Program> CreateFactory(string databasePath, string deviceMode)
    {
        var leasePath = Path.Combine(Path.GetDirectoryName(databasePath)!, "machine-executor.lock");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Device:Mode", deviceMode);
            builder.UseSetting("Device:StartupInitialization:Enabled", "false");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
            builder.UseSetting("MachineExecutor:LeasePath", leasePath);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Device:Mode"] = deviceMode,
                ["Device:StartupInitialization:Enabled"] = "false",
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                ["MachineExecutor:LeasePath"] = leasePath
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

    private static async Task<MockRuntimeResetResponse> PostResetAsync(HttpClient client, string commandId)
    {
        return await PostAsync<MockRuntimeResetResponse>(client, "/api/mock-runtime/reset", new
        {
            commandId,
            preflightStateHash = (string?)null
        });
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

    /// <summary>
    /// Seeds complete Mock runtime data: sample/scan data, channel batches, machine run,
    /// workflow executions, steps, DAB batches, reagent scan sessions/items/placements,
    /// reagent bottles, reagent consumptions/reservations, alarms, precheck, init runs,
    /// communication records, telemetry, staining task occupying A-01.
    /// </summary>
    private static async Task<SeedResult> SeedFullMockRuntimeAsync(StainerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await db.Users.Select(u => u.Id).FirstAsync();
        var a01Slot = await db.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
        var b01Slot = await db.PhysicalSlots.SingleAsync(x => x.Code == "B-01");
        var drawerA = await db.Drawers.SingleAsync(x => x.Code == "A");
        var liquidClassProfileId = await db.LiquidClassProfiles
            .Where(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null)
            .Select(x => x.Id)
            .SingleAsync();

        // Reagent definition for test bottles
        var reagentDef = await db.ReagentDefinitions.FirstOrDefaultAsync(x => x.ReagentCode == "P01");
        if (reagentDef is null)
        {
            reagentDef = new ReagentDefinition
            {
                ReagentCode = "P01", Name = "Test P01", ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId, CreatedAtUtc = now
            };
            db.ReagentDefinitions.Add(reagentDef);
            await db.SaveChangesAsync();
        }

        // Reagent definition for DAB bottles
        var dabADef = await db.ReagentDefinitions.FirstOrDefaultAsync(x => x.ReagentCode == "DBA");
        if (dabADef is null)
        {
            dabADef = new ReagentDefinition
            {
                ReagentCode = "DBA", Name = "DAB-A", ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId, CreatedAtUtc = now
            };
            db.ReagentDefinitions.Add(dabADef);
        }
        var dabBDef = await db.ReagentDefinitions.FirstOrDefaultAsync(x => x.ReagentCode == "DBB");
        if (dabBDef is null)
        {
            dabBDef = new ReagentDefinition
            {
                ReagentCode = "DBB", Name = "DAB-B", ReagentType = "test",
                LiquidClassProfileId = liquidClassProfileId, CreatedAtUtc = now
            };
            db.ReagentDefinitions.Add(dabBDef);
        }
        await db.SaveChangesAsync();

        // --- Reagent bottles (deleted by reset) ---
        var bottle1 = new ReagentBottle
        {
            ReagentDefinitionId = reagentDef.Id, FullBarcode = "BT-P01-001", ReagentCode = "P01",
            ProductionBatchNo = "B001", SerialNo = "S001", InitialVolumeUl = 5000,
            RemainingVolumeUl = 4000, ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = "Available", CreatedAtUtc = now
        };
        var dabABottle = new ReagentBottle
        {
            ReagentDefinitionId = dabADef.Id, FullBarcode = "BT-DBA-001", ReagentCode = "DBA",
            ProductionBatchNo = "B002", SerialNo = "S002", InitialVolumeUl = 10000,
            RemainingVolumeUl = 8000, ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = "Available", CreatedAtUtc = now
        };
        var dabBBottle = new ReagentBottle
        {
            ReagentDefinitionId = dabBDef.Id, FullBarcode = "BT-DBB-001", ReagentCode = "DBB",
            ProductionBatchNo = "B003", SerialNo = "S003", InitialVolumeUl = 10000,
            RemainingVolumeUl = 8000, ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = "Available", CreatedAtUtc = now
        };
        db.ReagentBottles.AddRange(bottle1, dabABottle, dabBBottle);
        await db.SaveChangesAsync();

        // --- Reagent rack placements ---
        var rackPos = await db.ReagentRackPositions.FirstOrDefaultAsync();
        if (rackPos is not null)
        {
            db.ReagentRackPlacements.Add(new ReagentRackPlacement
            {
                ReagentBottleId = bottle1.Id, ReagentRackPositionId = rackPos.Id,
                PlacedAtUtc = now, CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }

        // --- Reagent scan session + items ---
        var scanSession = new ReagentScanSession
        {
            SessionCode = "SCAN-RS-001", Status = "Completed",
            CreatedByUserId = userId, StartedAtUtc = now,
            CompletedAtUtc = now
        };
        if (rackPos is not null)
        {
            scanSession.Items.Add(new ReagentScanItem
            {
                ReagentScanSessionId = scanSession.Id,
                ReagentRackPositionId = rackPos.Id,
                ScannerChannelNo = 1, ScannerChannelCode = "CH1",
                ScanResult = ReagentScanResult.Valid, RawBarcode = "BT-P01-001",
                ParsedReagentCode = "P01", ParsedQuantityUl = 5000,
                ParsedBatchNo = "B001", ParsedSerialNo = "S001",
                IsValidationPassed = true, ValidationMessage = "OK",
                CreatedAtUtc = now
            });
        }
        db.ReagentScanSessions.Add(scanSession);
        await db.SaveChangesAsync();

        // Link placements to scan session
        if (rackPos is not null)
        {
            var placement = await db.ReagentRackPlacements.FirstAsync();
            placement.ReagentScanSessionId = scanSession.Id;
            await db.SaveChangesAsync();
        }

        // --- Sample scan session + items ---
        var sampleSession = new SampleScanSession
        {
            SessionCode = "SCAN-SS-001", Status = "Completed",
            CreatedByUserId = userId, StartedAtUtc = now, CompletedAtUtc = now
        };
        sampleSession.Items.Add(new SampleScanItem
        {
            SampleScanSessionId = sampleSession.Id,
            SlotCode = a01Slot.Code, ScanKind = SampleScanKind.TonglingPrimaryAntibody,
            ScanStatus = SampleScanStatus.Valid, RawCode = "001",
            NormalizedCode = "001", PrimaryAntibodyCode = "P01",
            DeviceStatus = "Connected", ScannedAtUtc = now, CreatedAtUtc = now
        });
        db.SampleScanSessions.Add(sampleSession);
        await db.SaveChangesAsync();

        // --- LIS query log ---
        db.LisQueryLogs.Add(new LisQueryLog
        {
            Source = "MockLIS", Status = LisQueryStatus.SingleCandidate,
            RawCode = "001", NormalizedCode = "001",
            CandidatePrimaryAntibodyCodesJson = "[\"P01\"]",
            SelectedByUserId = userId, StartedAtUtc = now, CreatedAtUtc = now,
            CompletedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Workflow (if not seeded) ---
        var workflowVersion = await db.WorkflowVersions
            .Include(v => v.WorkflowDefinition)
            .Include(v => v.Steps)
            .FirstOrDefaultAsync(v => v.Status == WorkflowVersionStatus.Published && v.WorkflowDefinition!.WorkflowType == StainingTaskType.Ihc);
        if (workflowVersion is null)
        {
            var definition = new WorkflowDefinition
            {
                Code = "IHC-RESET-TEST", Name = "IHC Reset Test", WorkflowType = StainingTaskType.Ihc,
                CreatedAtUtc = now
            };
            workflowVersion = new WorkflowVersion
            {
                WorkflowDefinition = definition, VersionNo = 1, VersionLabel = "1.0",
                Status = WorkflowVersionStatus.Published,
                PublishedAtUtc = now, CreatedAtUtc = now
            };
            workflowVersion.Steps.Add(new WorkflowStep
            {
                StepNo = 1, MajorStepCode = "PRIMARY_ANTIBODY", StepName = "Primary",
                ActionType = "Dispense", ReagentCode = "P01", VolumeUl = 100,
                DurationSeconds = 60, TargetTemperatureDeciC = 250,
                FailureStrategy = "Stop", CreatedAtUtc = now
            });
            db.WorkflowVersions.Add(workflowVersion);
            await db.SaveChangesAsync();
        }

        // --- Channel batch ---
        var channelBatch = new ChannelBatch
        {
            DrawerId = drawerA.Id, DrawerCode = drawerA.Code,
            Status = RuntimeLedgerStatus.Completed,
            ExperimentType = StainingTaskType.Ihc,
            SelectedWorkflowVersionId = workflowVersion.Id,
            WorkflowSnapshotJson = "{}",
            WorkflowSelectionStatus = WorkflowSelectionStatus.Selected,
            WorkflowSelectedAtUtc = now, CreatedAtUtc = now
        };
        db.ChannelBatches.Add(channelBatch);
        await db.SaveChangesAsync();

        // --- Staining task occupying A-01 ---
        var stainingTask = new StainingTask
        {
            TaskCode = "TASK-RESET-S1-001", TaskType = StainingTaskType.Ihc,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = a01Slot.Id,
            WorkflowDefinitionId = workflowVersion.WorkflowDefinitionId,
            WorkflowVersionId = workflowVersion.Id,
            WorkflowSnapshotJson = "{}",
            ConfirmedPrimaryAntibodyCode = "P01",
            PrimaryAntibodyCode = "P01",
            CreatedByUserId = userId, CreatedAtUtc = now
        };
        db.StainingTasks.Add(stainingTask);
        await db.SaveChangesAsync();

        // --- Slide task ---
        var slideTask = new SlideTask
        {
            ChannelBatchId = channelBatch.Id,
            StainingTaskId = stainingTask.Id,
            PhysicalSlotId = a01Slot.Id,
            SlotCode = a01Slot.Code,
            TaskType = StainingTaskType.Ihc,
            Status = RuntimeLedgerStatus.Completed,
            CreatedAtUtc = now
        };
        db.SlideTasks.Add(slideTask);
        await db.SaveChangesAsync();

        // --- Machine run ---
        var machineRun = new MachineRun
        {
            RunCode = "RUN-RESET-001", Status = RuntimeLedgerStatus.Completed,
            RequestedByUserId = userId, CreatedAtUtc = now,
            StartedAtUtc = now, CompletedAtUtc = now
        };
        channelBatch.MachineRunId = machineRun.Id;
        db.MachineRuns.Add(machineRun);
        await db.SaveChangesAsync();

        // --- Workflow execution ---
        var workflowExecution = new WorkflowExecution
        {
            MachineRunId = machineRun.Id,
            SlideTaskId = slideTask.Id,
            WorkflowVersionId = workflowVersion.Id,
            Status = RuntimeLedgerStatus.Completed,
            CreatedAtUtc = now, StartedAtUtc = now, CompletedAtUtc = now
        };
        db.WorkflowExecutions.Add(workflowExecution);
        await db.SaveChangesAsync();

        // --- Workflow step execution ---
        var stepExecution = new WorkflowStepExecution
        {
            WorkflowExecutionId = workflowExecution.Id,
            StepNo = 1, MajorStepCode = "PRIMARY_ANTIBODY", StepName = "Primary",
            ActionType = "Dispense", ReagentCode = "P01",
            VolumeUl = 100, Status = RuntimeLedgerStatus.Completed,
            CreatedAtUtc = now, StartedAtUtc = now, CompletedAtUtc = now
        };
        db.WorkflowStepExecutions.Add(stepExecution);
        await db.SaveChangesAsync();

        // --- Device command execution ---
        var deviceCommand = new DeviceCommandExecution
        {
            MachineRunId = machineRun.Id,
            WorkflowStepExecutionId = stepExecution.Id,
            CommandType = "Dispense", Status = DeviceCommandStatus.Completed,
            CreatedAtUtc = now, CompletedAtUtc = now
        };
        db.DeviceCommandExecutions.Add(deviceCommand);
        await db.SaveChangesAsync();

        // --- Dispense execution ---
        db.DispenseExecutions.Add(new DispenseExecution
        {
            DeviceCommandExecutionId = deviceCommand.Id,
            ReagentBottleId = bottle1.Id, ReagentCode = "P01",
            VolumeUl = 100, Status = DeviceCommandStatus.Completed,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Pipetting operation ---
        db.PipettingOperations.Add(new PipettingOperation
        {
            OperationType = PipettingOperationTypes.Dispense,
            Status = DeviceCommandStatus.Completed,
            NeedleCode = NeedleCodes.Needle1,
            ExecutionMode = PipettingExecutionModes.Single,
            TargetPointCode = "A-01",
            SourceType = NeedleLoadSourceTypes.ReagentBottle,
            ReagentCode = "P01", ReagentBottleId = bottle1.Id,
            VolumeUl = 100, MachineRunId = machineRun.Id,
            CreatedAtUtc = now, CompletedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Reagent consumption ---
        db.ReagentConsumptions.Add(new ReagentConsumption
        {
            MachineRunId = machineRun.Id,
            WorkflowStepExecutionId = stepExecution.Id,
            DeviceCommandExecutionId = deviceCommand.Id,
            ReagentBottleId = bottle1.Id, ReagentCode = "P01",
            SourceRole = "Primary", VolumeUl = 100,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Reagent reservation ---
        db.ReagentReservations.Add(new ReagentReservation
        {
            MachineRunId = machineRun.Id,
            ReagentBottleId = bottle1.Id, ReagentCode = "P01",
            ReservationKind = ReagentReservationKind.MachineRun,
            SourceRole = "Primary", Status = ReagentReservationStatus.Consumed,
            RequiredVolumeUl = 100, ReservedVolumeUl = 100,
            CreatedByUserId = userId, CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- DAB batch ---
        var dabMixPosition = await db.DabMixPositions.FirstOrDefaultAsync();
        var dabBatchId = Guid.NewGuid().ToString();
        var dabBatch = new DabBatch
        {
            Id = dabBatchId,
            DabMixPositionId = dabMixPosition?.Id ?? Guid.NewGuid().ToString(),
            PositionCode = "M1",
            DabAReagentBottleId = dabABottle.Id,
            DabBReagentBottleId = dabBBottle.Id,
            CreatedByUserId = userId,
            Status = DabBatchStatus.Available,
            SlideCount = 1, UsedVolumeUl = 200, RemainingVolumeUl = 400,
            CreatedAtUtc = now
        };
        if (dabMixPosition is not null)
        {
            dabMixPosition.ActiveDabBatchId = dabBatch.Id;
        }
        db.DabBatches.Add(dabBatch);
        await db.SaveChangesAsync();

        // --- DAB batch task ---
        db.DabBatchTasks.Add(new DabBatchTask
        {
            DabBatchId = dabBatch.Id,
            StainingTaskId = stainingTask.Id,
            RequiredVolumeUl = 200,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- DAB batch usage ---
        db.DabBatchUsages.Add(new DabBatchUsage
        {
            DabBatchId = dabBatch.Id,
            MachineRunId = machineRun.Id,
            WorkflowStepExecutionId = stepExecution.Id,
            StainingTaskId = stainingTask.Id,
            VolumeUl = 200,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- DAB repreparation plan ---
        db.DabRepreparationPlans.Add(new DabRepreparationPlan
        {
            ExpiredDabBatchId = dabBatch.Id,
            MachineRunId = machineRun.Id,
            Status = DabRepreparationPlanStatus.AwaitingMixPosition,
            Reason = "test", CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- System liquid usage ---
        db.SystemLiquidUsages.Add(new SystemLiquidUsage
        {
            MachineRunId = machineRun.Id,
            WorkflowStepExecutionId = stepExecution.Id,
            DeviceCommandExecutionId = deviceCommand.Id,
            DabBatchId = dabBatch.Id,
            SourceType = SystemLiquidSourceTypes.SystemWater,
            VolumeUl = 50, LevelSnapshotJson = "{}",
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Alarms ---
        var alarm = new Alarm
        {
            MachineRunId = machineRun.Id,
            Code = "TEST_ALARM", Severity = "Warning",
            Message = "Test alarm", Status = "Active",
            CreatedAtUtc = now
        };
        db.Alarms.Add(alarm);
        await db.SaveChangesAsync();

        db.AlarmActions.Add(new AlarmAction
        {
            AlarmId = alarm.Id,
            ActorUserId = userId,
            Action = "Acknowledge", Message = "Acknowledged",
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Device precheck run ---
        db.DevicePrecheckRuns.Add(new DevicePrecheckRun
        {
            CommandId = "cmd-precheck-001", DeviceMode = "Mock", RunMode = "Full",
            Ok = true, ChecksJson = "[]", GeneratedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Device communication records ---
        db.DeviceCommunicationRecords.Add(new DeviceCommunicationRecord
        {
            DeviceMode = "Mock", AdapterName = "MockDeviceAdapter",
            ModuleCode = "temperature", Action = "SetTemperature",
            CommandId = "cmd-comm-001", Source = "Test",
            Status = "Completed", Ok = true, Acknowledged = true,
            Message = "OK", RequestJson = "{}", ResponseJson = "{}",
            StartedAtUtc = now, CompletedAtUtc = now, CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Device initialization run + check ---
        var initRun = new DeviceInitializationRun
        {
            CommandId = "cmd-init-001", Status = DeviceInitializationStatus.Ready,
            DeviceMode = "Mock", AdapterName = "MockDeviceAdapter",
            RequestedByUserId = userId, StartedAtUtc = now,
            CompletedAtUtc = now, CreatedAtUtc = now
        };
        db.DeviceInitializationRuns.Add(initRun);
        await db.SaveChangesAsync();

        db.DeviceInitializationChecks.Add(new DeviceInitializationCheck
        {
            DeviceInitializationRunId = initRun.Id,
            StepNo = 1, ModuleCode = "temperature",
            Status = DeviceInitializationCheckStatus.Succeeded,
            Message = "OK", ResultJson = "{}",
            StartedAtUtc = now, CompletedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Temperature telemetry ---
        db.TemperatureTelemetry.Add(new TemperatureTelemetry
        {
            SourceType = "ThermalPoint", SourceId = "tp-1",
            CurrentTemperatureDeciC = 250, TargetTemperatureDeciC = 250,
            IsEnabled = false, IsConnected = true, Status = "Off",
            RecordedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Fluidics telemetry ---
        db.FluidicsTelemetry.Add(new FluidicsTelemetry
        {
            SourceType = "PumpChannel", SourceId = "pump-1",
            EventType = "PumpChanged", Status = "Idle",
            RecordedAtUtc = now
        });
        await db.SaveChangesAsync();

        // --- Workflow assignment history ---
        db.WorkflowAssignmentHistory.Add(new WorkflowAssignmentHistory
        {
            ChannelBatchId = channelBatch.Id,
            NewExperimentType = StainingTaskType.Ihc,
            NewWorkflowVersionId = workflowVersion.Id,
            NewWorkflowSnapshotJson = "{}",
            ActionType = WorkflowAssignmentAction.InitialSelection,
            CreatedAtUtc = now, Reason = "test"
        });
        await db.SaveChangesAsync();

        // --- Machine resource lease (Released, not Acquired — so reset guards don't block) ---
        db.MachineResourceLeases.Add(new MachineResourceLease
        {
            ResourceCode = "Platform", ResourceType = MachineResourceTypes.Platform,
            Status = MachineResourceLeaseStatus.Released,
            MachineRunId = machineRun.Id, CreatedAtUtc = now,
            AcquiredAtUtc = now, ReleasedAtUtc = now
        });
        await db.SaveChangesAsync();

        return new SeedResult(machineRun.Id);
    }

    /// <summary>
    /// Trigger each control subsystem's lazy EnsureSeededCoreAsync via DI so the device-state
    /// tables (thermal/cooling/pump/mixer/liquid/robot/needle) have baseline rows for reset to
    /// UPDATE and for assertions to read. Idempotent (seeders are create-if-missing).
    /// </summary>
    private static async Task EnsureDeviceStatesSeededAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ThermalControlService>().GetStateAsync(advance: false, CancellationToken.None);
        await sp.GetRequiredService<FluidicsControlService>().GetStateAsync(CancellationToken.None);
        await sp.GetRequiredService<MotionControlService>().GetReadinessAsync(CancellationToken.None);
    }

    private static async Task PerturbDeviceStatesAsync(StainerDbContext db)
    {
        // Perturb thermal states so reset has something to restore
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE thermal_point_states SET current_temperature_deci_c = 999");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE liquid_container_states SET current_volume_ul = 0");

        // Perturb needle states
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE needle_states SET loaded_source_type = 'ReagentBottle', volume_ul = 500, needs_wash = 1");

        // Perturb robot arm
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE robot_arm_states SET is_homed = 1, status = 'Completed'");
    }

    private sealed record SeedResult(string? MachineRunId);
}
