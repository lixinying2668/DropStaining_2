using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
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

public sealed class MotionControlMockTests
{
    [Fact]
    public async Task Single_needle_requires_wash_before_switch_while_dual_needles_can_hold_different_reagents()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<MotionControlService>();
        await service.InitializeModuleAsync(DeviceModules.RobotArm);
        await service.InitializeModuleAsync(DeviceModules.Needles);
        await AddBottleAsync(dbContext, "ABC", "ABC00320270101001", 1000, "R1");
        await AddBottleAsync(dbContext, "DEF", "DEF00320270101001", 1000, "R2");
        var snapshot = BuildCoordinateSnapshot("coord-motion-direct");

        var first = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-abc-n1", "Needle1", "ABC", "A-01", 100, snapshot));
        Assert.True(first.Ok, first.Message);
        var secondNeedle = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-def-n2", "Needle2", "DEF", "A-02", 100, snapshot));
        Assert.True(secondNeedle.Ok, secondNeedle.Message);

        var n1 = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        var n2 = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle2);
        Assert.Equal("ABC", n1.LoadedReagentCode);
        Assert.Equal("DEF", n2.LoadedReagentCode);
        Assert.True(n1.NeedsWash);
        Assert.True(n2.NeedsWash);

        var blocked = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-def-n1-blocked", "Needle1", "DEF", "A-01", 50, snapshot));
        Assert.False(blocked.Ok);
        Assert.Equal("needle_reagent_switch_requires_wash", blocked.ErrorCode);

        var washed = await service.WashNeedlesFromDeviceAsync(new DeviceOperationRequest(
            Context("cmd-motion-wash-n1"),
            DeviceModules.NeedleWash,
            "WashNeedle",
            new Dictionary<string, object?> { ["needleCode"] = "Needle1" }));
        Assert.True(washed.Ok, washed.Message);
        var switched = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-def-n1-after-wash", "Needle1", "DEF", "A-01", 50, snapshot));
        Assert.True(switched.Ok, switched.Message);
    }

    [Fact]
    public async Task Dual_needle_geometry_falls_back_to_sequential_and_records_pipette_stages()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<MotionControlService>();
        await service.InitializeModuleAsync(DeviceModules.RobotArm);
        await service.InitializeModuleAsync(DeviceModules.Needles);
        await AddBottleAsync(dbContext, "ABC", "ABC00320270101002", 1000, "R1");
        var snapshot = BuildCoordinateSnapshot("coord-motion-sequential");

        var request = PipetteRequest("cmd-motion-sequential", "Needle1", "ABC", "A-01", 100, snapshot, new Dictionary<string, object?>
        {
            ["useBothNeedles"] = true,
            ["secondaryTargetPointCode"] = "A-03"
        });
        var result = await service.PipetteFromDeviceAsync(request);
        Assert.True(result.Ok, result.Message);

        var operations = await dbContext.PipettingOperations
            .Where(x => x.DeviceCommandExecutionId == "cmd-motion-sequential")
            .ToListAsync();
        operations = operations.OrderBy(x => x.CreatedAtUtc).ToList();
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.LiquidDetect);
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.Aspirate);
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.Dispense);
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.Blowout);
        Assert.All(operations.Where(x => x.OperationType != PipettingOperationTypes.WashNeedle), x => Assert.Equal(PipettingExecutionModes.Sequential, x.ExecutionMode));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "motion.operation.dispense"));
    }

    [Fact]
    public async Task Dual_needle_keeps_conservative_sequential_execution_even_with_secondary_target()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<MotionControlService>();
        await service.InitializeModuleAsync(DeviceModules.RobotArm);
        await service.InitializeModuleAsync(DeviceModules.Needles);
        await AddBottleAsync(dbContext, "ABC", "ABC00320270101022", 1000, "R1");

        var synchronized = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-sync", "Needle1", "ABC", "A-01", 100, BuildCoordinateSnapshot("coord-motion-sync"), new Dictionary<string, object?>
        {
            ["useBothNeedles"] = true,
            ["secondaryTargetPointCode"] = "A-02",
            ["allowAutomaticWash"] = true
        }));
        Assert.True(synchronized.Ok, synchronized.Message);
        Assert.Contains(
            await dbContext.PipettingOperations.Where(x => x.DeviceCommandExecutionId == "cmd-motion-sync").ToListAsync(),
            x => x.OperationType == PipettingOperationTypes.Dispense && x.ExecutionMode == PipettingExecutionModes.Sequential);

        var unsafeSecondary = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-unsafe-secondary", "Needle1", "ABC", "A-01", 100, BuildCoordinateSnapshot("coord-motion-unsafe", a02SafeZUm: 300_000), new Dictionary<string, object?>
        {
            ["useBothNeedles"] = true,
            ["secondaryTargetPointCode"] = "A-02",
            ["allowAutomaticWash"] = true
        }));
        Assert.True(unsafeSecondary.Ok, unsafeSecondary.Message);
        Assert.Contains(
            await dbContext.PipettingOperations.Where(x => x.DeviceCommandExecutionId == "cmd-motion-unsafe-secondary").ToListAsync(),
            x => x.OperationType == PipettingOperationTypes.Dispense && x.ExecutionMode == PipettingExecutionModes.Sequential);
    }

    [Fact]
    public async Task Unknown_failure_preserves_loaded_needle_until_explicit_manual_wash()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<MotionControlService>();
        await service.InitializeModuleAsync(DeviceModules.RobotArm);
        await service.InitializeModuleAsync(DeviceModules.Needles);
        await AddBottleAsync(dbContext, "ABC", "ABC00320270101003", 1000, "R1");
        var snapshot = BuildCoordinateSnapshot("coord-motion-unknown");

        var first = await service.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-preserve-load", "Needle1", "ABC", "A-01", 100, snapshot));
        Assert.True(first.Ok, first.Message);
        await service.RecordDeviceFailureFromExecutorAsync(
            DeviceModules.Pipette,
            DeviceCommandStatuses.Unknown,
            "pipette_unknown",
            "Unknown pipette outcome.",
            "Needle1",
            "run-motion-unknown",
            "step-motion-unknown",
            "cmd-motion-unknown",
            CancellationToken.None);

        var needle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal(MotionStatuses.Unknown, needle.Status);
        Assert.Equal("ABC", needle.LoadedReagentCode);
        Assert.Equal(NeedleLoadSourceTypes.ReagentBottle, needle.LoadedSourceType);
        Assert.True(needle.NeedsWash);

        var init = await service.InitializeModuleAsync(DeviceModules.Needles);
        Assert.False(init.Ok);
        var wash = await service.WashNeedlesFromDeviceAsync(new DeviceOperationRequest(
            Context("cmd-motion-manual-wash-after-unknown"),
            DeviceModules.NeedleWash,
            "WashNeedle",
            new Dictionary<string, object?> { ["needleCode"] = "Needle1" }));
        Assert.True(wash.Ok, wash.Message);
        var recovered = await service.InitializeModuleAsync(DeviceModules.Needles);
        Assert.True(recovered.Ok, recovered.Message);
    }

    [Fact]
    public async Task Executor_unknown_pipette_does_not_consume_inventory_and_keeps_resource_for_manual_resolution()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "motion-unknown");

        string taskId;
        string bottleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "MOTION-UNKNOWN", "A-01", "ABC", 100);
            bottleId = await AddBottleAsync(dbContext, "ABC", "ABC00320270101004", 1000, "R1");
        }

        _ = await PostJsonAsync<DeviceFaultMutationResponse>(client, "/api/device/mock-faults", new
        {
            commandId = "cmd-motion-unknown-fault",
            moduleCode = DeviceModules.Pipette,
            faultType = DeviceFaultTypes.ReturnUnknown,
            reason = "pipette unknown",
            errorCode = "pipette_unknown",
            message = "Injected pipette unknown."
        });
        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new { commandId = "cmd-motion-unknown-run", stainingTaskIds = new[] { taskId } });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-motion-unknown-start" });
        _ = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await verifyContext.ReagentConsumptions.AnyAsync(x => x.MachineRunId == run.RunId));
        Assert.Equal(1000, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == bottleId)).RemainingVolumeUl);
        Assert.True(await verifyContext.DeviceCommandExecutions.AnyAsync(x => x.MachineRunId == run.RunId && x.Status == DeviceCommandStatus.Unknown));
        Assert.True(await verifyContext.MachineResourceLeases.AnyAsync(x => x.MachineRunId == run.RunId && x.Status == MachineResourceLeaseStatus.NeedsManualResolution));
    }

    [Fact]
    public async Task Preheld_resource_creates_waiting_lease_and_faults_without_inventory_consumption()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "motion-resource");

        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "MOTION-RESOURCE", "A-01", "ABC", 100);
            await AddBottleAsync(dbContext, "ABC", "ABC00320270101005", 1000, "R1");
            dbContext.MachineResourceLeases.Add(new MachineResourceLease
            {
                ResourceCode = "Platform:RobotArm",
                ResourceType = MachineResourceTypes.Platform,
                Status = MachineResourceLeaseStatus.Acquired,
                DeviceCommandExecutionId = "external-command",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                AcquiredAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new { commandId = "cmd-motion-resource-run", stainingTaskIds = new[] { taskId } });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-motion-resource-start" });
        _ = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await verifyContext.MachineResourceLeases.AnyAsync(x => x.MachineRunId == run.RunId && x.Status == MachineResourceLeaseStatus.Waiting && x.ResourceCode == "Platform:RobotArm"));
        Assert.False(await verifyContext.ReagentConsumptions.AnyAsync(x => x.MachineRunId == run.RunId));
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "resource_waiting"));
    }

    [Theory]
    [InlineData("Platform:RobotArm", "A-01", "ABC", "Dispense", "PRIMARY_ANTIBODY")]
    [InlineData("WashStation:NeedleWash", "A-01", "ABC", "Dispense", "PRIMARY_ANTIBODY")]
    [InlineData("DabPosition:DAB", "A-01", "DAB", "Dab", "DAB")]
    [InlineData("Needle:Needle1", "A-01", "ABC", "Dispense", "PRIMARY_ANTIBODY")]
    [InlineData("Needle:Needle2", "A-02", "ABC", "Dispense", "PRIMARY_ANTIBODY")]
    [InlineData("Source:ABC", "A-01", "ABC", "Dispense", "PRIMARY_ANTIBODY")]
    public async Task He_and_ihc_same_run_resource_competition_records_waiting_without_preemption(
        string preheldResourceCode,
        string ihcSlotCode,
        string ihcReagentCode,
        string ihcActionType,
        string ihcMajorStepCode)
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, $"motion-competition-{preheldResourceCode.Replace(':', '-')}");

        string ihcTaskId;
        string heTaskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcTaskId = await CreateConfirmedTaskAsync(dbContext, $"IHC-{Guid.NewGuid():N}"[..24], ihcSlotCode, ihcReagentCode, 100, StainingTaskType.Ihc, ihcActionType, ihcMajorStepCode);
            heTaskId = await CreateConfirmedTaskAsync(dbContext, $"HE-{Guid.NewGuid():N}"[..24], "B-01", "HEM", 100, StainingTaskType.He, "Dispense", "HEMATOXYLIN");
            if (ihcReagentCode != "DAB")
            {
                await AddBottleAsync(dbContext, ihcReagentCode, $"ABC{Guid.NewGuid():N}"[..17], 1000, "R1");
            }
            await AddBottleAsync(dbContext, "HEM", $"HEM{Guid.NewGuid():N}"[..17], 1000, "R3");
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new { commandId = $"cmd-motion-competition-run-{Guid.NewGuid():N}", stainingTaskIds = new[] { ihcTaskId, heTaskId } });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            dbContext.MachineResourceLeases.Add(new MachineResourceLease
            {
                ResourceCode = preheldResourceCode,
                ResourceType = ResourceTypeForCode(preheldResourceCode),
                Status = MachineResourceLeaseStatus.Acquired,
                DeviceCommandExecutionId = $"external-{preheldResourceCode}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                AcquiredAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = $"cmd-motion-competition-start-{Guid.NewGuid():N}" });
        _ = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await verifyContext.MachineResourceLeases.AnyAsync(x => x.MachineRunId == run.RunId && x.Status == MachineResourceLeaseStatus.Waiting && x.ResourceCode == preheldResourceCode));
        Assert.True(await verifyContext.MachineResourceLeases.AnyAsync(x => x.ResourceCode == preheldResourceCode && x.Status == MachineResourceLeaseStatus.Acquired && x.DeviceCommandExecutionId!.StartsWith("external-")));
        Assert.False(await verifyContext.ReagentConsumptions.AnyAsync(x => x.MachineRunId == run.RunId));
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "resource_waiting"));
    }

    [Fact]
    public async Task Pause_stop_and_startup_recovery_do_not_clear_loaded_needle_or_release_uncertain_resource()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var motion = scope.ServiceProvider.GetRequiredService<MotionControlService>();
        var executor = scope.ServiceProvider.GetRequiredService<MachineExecutor>();
        await motion.InitializeModuleAsync(DeviceModules.RobotArm);
        await motion.InitializeModuleAsync(DeviceModules.Needles);
        await AddBottleAsync(dbContext, "ABC", "ABC00320270101033", 1000, "R1");
        var loaded = await motion.PipetteFromDeviceAsync(PipetteRequest("cmd-motion-loaded-before-pause", "Needle1", "ABC", "A-01", 100, BuildCoordinateSnapshot("coord-pause-stop")));
        Assert.True(loaded.Ok, loaded.Message);

        var run = new MachineRun
        {
            RunCode = "RUN-MOTION-PAUSE-STOP",
            Status = RuntimeLedgerStatus.Running,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.MachineRuns.Add(run);
        dbContext.MachineResourceLeases.Add(new MachineResourceLease
        {
            ResourceCode = "Platform:RobotArm",
            ResourceType = MachineResourceTypes.Platform,
            Status = MachineResourceLeaseStatus.Acquired,
            MachineRunId = run.Id,
            DeviceCommandExecutionId = "cmd-motion-uncertain-resource",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AcquiredAtUtc = DateTimeOffset.UtcNow
        });
        var command = new DeviceCommandExecution
        {
            Id = "cmd-motion-uncertain-resource",
            MachineRunId = run.Id,
            CommandType = "Dispense",
            Status = DeviceCommandStatus.DeviceAcknowledged,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CommandSentAtUtc = DateTimeOffset.UtcNow,
            AcknowledgedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.DeviceCommandExecutions.Add(command);
        await dbContext.SaveChangesAsync();

        await InvokeExecutorLifecycleAsync(executor, "PauseRunAsync", dbContext, run.Id);
        var pausedNeedle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal("ABC", pausedNeedle.LoadedReagentCode);
        Assert.True(pausedNeedle.NeedsWash);
        Assert.Equal(MachineResourceLeaseStatus.Acquired, (await dbContext.MachineResourceLeases.SingleAsync(x => x.DeviceCommandExecutionId == command.Id)).Status);

        run.Status = RuntimeLedgerStatus.Running;
        await dbContext.SaveChangesAsync();
        await InvokeExecutorLifecycleAsync(executor, "StopRunAsync", dbContext, run.Id);
        var stoppedNeedle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal("ABC", stoppedNeedle.LoadedReagentCode);
        Assert.True(stoppedNeedle.NeedsWash);
        Assert.Equal(MachineResourceLeaseStatus.Acquired, (await dbContext.MachineResourceLeases.SingleAsync(x => x.DeviceCommandExecutionId == command.Id)).Status);

        var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
        var report = await recovery.RecoverAsync();
        Assert.Equal(1, report.CommandsMarkedUnknown);
        var recoveredNeedle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal("ABC", recoveredNeedle.LoadedReagentCode);
        Assert.True(recoveredNeedle.NeedsWash);
        Assert.Equal(DeviceCommandStatus.Unknown, (await dbContext.DeviceCommandExecutions.SingleAsync(x => x.Id == command.Id)).Status);
        Assert.Equal(MachineResourceLeaseStatus.NeedsManualResolution, (await dbContext.MachineResourceLeases.SingleAsync(x => x.DeviceCommandExecutionId == command.Id)).Status);
    }

    [Fact]
    public async Task Replaying_same_pipette_command_does_not_deduct_reagent_inventory_twice()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "motion-idempotent-consumption");

        string taskId;
        string bottleId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "MOTION-IDEMPOTENT", "A-01", "ABC", 100);
            bottleId = await AddBottleAsync(dbContext, "ABC", "ABC00320270101044", 1000, "R1");
        }

        var runResponse = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new { commandId = "cmd-motion-idempotent-run", stainingTaskIds = new[] { taskId } });
        await using var scope = factory.Services.CreateAsyncScope();
        var verifyContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<MachineExecutor>();
        var run = await verifyContext.MachineRuns
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.SlideTask)
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.StepExecutions)
            .SingleAsync(x => x.Id == runResponse.RunId);
        var step = run.WorkflowExecutions.SelectMany(x => x.StepExecutions).Single();
        var command = new DeviceCommandExecution
        {
            Id = "cmd-motion-idempotent-pipette",
            MachineRunId = run.Id,
            WorkflowStepExecutionId = step.Id,
            CommandType = "Dispense",
            Status = DeviceCommandStatus.Completed,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        verifyContext.DeviceCommandExecutions.Add(command);
        await verifyContext.SaveChangesAsync();

        Assert.True(await InvokeConsumeReagentAsync(executor, verifyContext, run, step, command, "ABC", 100));
        await verifyContext.SaveChangesAsync();
        Assert.Equal(900, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == bottleId)).RemainingVolumeUl);
        Assert.True(await InvokeConsumeReagentAsync(executor, verifyContext, run, step, command, "ABC", 100));
        await verifyContext.SaveChangesAsync();

        Assert.Equal(900, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == bottleId)).RemainingVolumeUl);
        Assert.Equal(1, await verifyContext.ReagentConsumptions.CountAsync(x => x.DeviceCommandExecutionId == command.Id));
        Assert.Equal(1, await verifyContext.DispenseExecutions.CountAsync(x => x.DeviceCommandExecutionId == command.Id));
    }

    private static DeviceOperationRequest PipetteRequest(
        string commandId,
        string needleCode,
        string reagentCode,
        string targetPointCode,
        int volumeUl,
        string coordinateSnapshotJson,
        Dictionary<string, object?>? extra = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["needleCode"] = needleCode,
            ["reagentCode"] = reagentCode,
            ["volumeUl"] = volumeUl,
            ["adjustedVolumeUl"] = volumeUl,
            ["targetPointCode"] = targetPointCode,
            ["coordinateProfileVersionId"] = "coord-motion",
            ["coordinateSnapshotJson"] = coordinateSnapshotJson,
            ["liquidClassVersionId"] = "lc-motion",
            ["liquidClassVersionNo"] = 1,
            ["liquidClassParametersJson"] = "{}"
        };
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        return new DeviceOperationRequest(Context(commandId), DeviceModules.Pipette, "Pipette", parameters);
    }

    private static DeviceCommandContext Context(string commandId) =>
        new(commandId, commandId, "test", nameof(MotionControlMockTests));

    private static async Task InvokeExecutorLifecycleAsync(MachineExecutor executor, string methodName, StainerDbContext dbContext, string runId)
    {
        var method = typeof(MachineExecutor).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task?)method!.Invoke(executor, [dbContext, runId, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    private static async Task<bool> InvokeConsumeReagentAsync(
        MachineExecutor executor,
        StainerDbContext dbContext,
        MachineRun run,
        WorkflowStepExecution step,
        DeviceCommandExecution command,
        string reagentCode,
        int volumeUl)
    {
        var method = typeof(MachineExecutor).GetMethod("ConsumeReagentAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task<bool>?)method!.Invoke(executor, [dbContext, run, step, command, reagentCode, volumeUl, CancellationToken.None]);
        Assert.NotNull(task);
        return await task!;
    }

    private static string ResourceTypeForCode(string resourceCode)
    {
        var prefix = resourceCode.Split(':', 2)[0];
        return prefix switch
        {
            "Platform" => MachineResourceTypes.Platform,
            "WashStation" => MachineResourceTypes.WashStation,
            "DabPosition" => MachineResourceTypes.DabPosition,
            "Needle" => MachineResourceTypes.Needle,
            "Source" => MachineResourceTypes.Source,
            _ => prefix
        };
    }

    private static FactoryContext CreateFactory(string? databasePath = null)
    {
        var root = databasePath is null
            ? Path.Combine(Path.GetTempPath(), "stainer-motion-tests", Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(databasePath)!;
        databasePath ??= Path.Combine(root, "stainer.db");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = Path.Combine(root, $"machine-executor-{Guid.NewGuid():N}.lock"),
            ["Safety:LogDirectory"] = Path.Combine(root, "logs"),
            ["Device:Mode"] = DeviceModes.Mock,
            ["Device:StartupInitialization:Enabled"] = "false"
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

    private static async Task<string> CreateConfirmedTaskAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string slotCode,
        string reagentCode,
        int volumeUl,
        string workflowType = StainingTaskType.Ihc,
        string actionType = "Dispense",
        string majorStepCode = "PRIMARY_ANTIBODY")
    {
        var defaultLiquidClass = await dbContext.LiquidClassProfiles
            .Include(x => x.EnabledVersion)
            .SingleAsync(x => x.EnabledVersionId != null);
        var definition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == reagentCode);
        if (definition is null)
        {
            definition = new ReagentDefinition
            {
                ReagentCode = reagentCode,
                Name = $"Reagent {reagentCode}",
                ReagentType = "test",
                LiquidClassProfileId = defaultLiquidClass.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentDefinitions.Add(definition);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            definition.LiquidClassProfileId ??= defaultLiquidClass.Id;
        }
        await dbContext.SaveChangesAsync();

        var workflowDefinition = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = workflowCode,
            WorkflowType = workflowType,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "Motion test workflow.",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = majorStepCode,
            StepName = actionType,
            ActionType = actionType,
            ReagentCode = reagentCode,
            VolumeUl = volumeUl,
            DurationSeconds = 1,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        workflowVersion.ReagentRequirements.Add(new WorkflowReagentRequirement
        {
            ReagentCode = reagentCode,
            RequiredVolumeUl = volumeUl,
            IsRequired = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var slot = await dbContext.PhysicalSlots.Include(x => x.Drawer).SingleAsync(x => x.Code == slotCode);
        var coordinateVersionId = await dbContext.CoordinateProfileVersions
            .Where(x => x.IsActive && x.Status == CoordinateProfileVersionStatus.Active)
            .Select(x => x.Id)
            .SingleAsync();
        var workflowSnapshot = JsonSerializer.Serialize(new { workflowVersionId = workflowVersion.Id, source = "motion-test" });
        var coordinateSnapshot = BuildCoordinateSnapshot(coordinateVersionId);
        var liquidSnapshot = await new LiquidClassSnapshotFactory(dbContext).FreezeForWorkflowAsync(workflowVersion);
        var task = new StainingTask
        {
            TaskCode = $"TASK-{workflowCode}",
            TaskType = workflowType,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = slot.Id,
            WorkflowDefinition = workflowDefinition,
            WorkflowVersion = workflowVersion,
            WorkflowSnapshotJson = workflowSnapshot,
            CandidateResultsJson = "[]",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var batch = new ChannelBatch
        {
            DrawerId = slot.DrawerId,
            DrawerCode = slot.Drawer!.Code,
            Status = RuntimeLedgerStatus.Pending,
            ExperimentType = workflowType,
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
        dbContext.StainingTasks.Add(task);
        dbContext.ChannelBatches.Add(batch);
        dbContext.SlideTasks.Add(new SlideTask
        {
            ChannelBatch = batch,
            StainingTask = task,
            PhysicalSlot = slot,
            SlotCode = slot.Code,
            TaskType = workflowType,
            Status = RuntimeLedgerStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return task.Id;
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

    private static string BuildCoordinateSnapshot(string coordinateVersionId, int a02SafeZUm = 20_000)
    {
        var targetPoints = new[]
        {
            new { pointCode = "A-01", pointType = "SlideSlot", calibratedXUm = 100_000, calibratedYUm = 100_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "A-02", pointType = "SlideSlot", calibratedXUm = 125_000, calibratedYUm = 100_000, calibratedZUm = 10_000, safeZUm = a02SafeZUm, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "A-03", pointType = "SlideSlot", calibratedXUm = 150_000, calibratedYUm = 100_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "B-01", pointType = "SlideSlot", calibratedXUm = 100_000, calibratedYUm = 125_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 8_000, dispenseZUm = 7_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "R1", pointType = "ReagentRack", calibratedXUm = 50_000, calibratedYUm = 30_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "R2", pointType = "ReagentRack", calibratedXUm = 70_000, calibratedYUm = 30_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "R3", pointType = "ReagentRack", calibratedXUm = 90_000, calibratedYUm = 30_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "M1", pointType = "DabMix", calibratedXUm = 300_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true },
            new { pointCode = "NeedleWash", pointType = "NeedleWash", calibratedXUm = 320_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true }
        };
        return JsonSerializer.Serialize(new { coordinateProfileVersionId = coordinateVersionId, source = "motion-test", targetPoints });
    }

    private sealed record FactoryContext(WebApplicationFactory<Program> Factory, string DatabasePath);
}
