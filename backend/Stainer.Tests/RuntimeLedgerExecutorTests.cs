using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Application.Devices;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class RuntimeLedgerExecutorTests
{
    [Fact]
    public async Task Mock_executor_completes_ihc_and_he_with_cross_bottle_dab_and_depletion_alarm()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");
        await InitializeDevicesAsync(client, "executor-001");

        string ihcTaskId;
        string heTaskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            ihcTaskId = await CreateConfirmedTaskAsync(dbContext, "IHC-RUN", StainingTaskType.Ihc, "A-01",
                [
                    ("PRETREATMENT", "Heat", null, null),
                    ("PRIMARY_ANTIBODY", "Dispense", "ABC", 800),
                    ("DAB", "Dab", "DAB", 100),
                    ("HEMATOXYLIN", "Dispense", "HEM", 100)
                ],
                [("ABC", 800), ("HEM", 100)]);
            heTaskId = await CreateConfirmedTaskAsync(dbContext, "HE-RUN", StainingTaskType.He, "B-01",
                [
                    ("HEMATOXYLIN", "Dispense", "HEM", 100),
                    ("TERMINAL_WASH", "Wash", null, null)
                ],
                [("HEM", 100)]);
            await AddBottleAsync(dbContext, "ABC", "ABC00320260101001", 300, "R1");
            await AddBottleAsync(dbContext, "ABC", "ABC00720260101002", 700, "R2");
            await AddBottleAsync(dbContext, "HEM", "HEM05020260101001", 5000, "R3");
            var dabAId = await AddBottleAsync(dbContext, "DAB-A", "DABA20260101003", 100, "R4");
            var dabBId = await AddBottleAsync(dbContext, "DAB-B", "DABB20260101003", 100, "R5");
            var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
            await service.CreateBatchAsync(new CreateDabBatchRequest(
                "cmd-runtime-dab-create-001",
                [ihcTaskId],
                dabAId,
                dabBId,
                "M1"), await AdminActorAsync(dbContext));
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-001",
            stainingTaskIds = new[] { ihcTaskId, heTaskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-001" });

        var completed = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Completed);
        Assert.Equal(RuntimeLedgerStatus.Completed, completed.Status);
        Assert.All(completed.ChannelBatches.SelectMany(x => x.Slides), x => Assert.Equal(RuntimeLedgerStatus.WaitingUnload, x.Status));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(2, await verifyContext.ReagentConsumptions.CountAsync(x => x.MachineRunId == run.RunId && x.ReagentCode == "ABC"));
        Assert.Equal(800, await verifyContext.ReagentConsumptions.Where(x => x.MachineRunId == run.RunId && x.ReagentCode == "ABC").SumAsync(x => x.VolumeUl));
        var dabBatches = await verifyContext.DabBatches.AsNoTracking().ToListAsync();
        Assert.Contains(dabBatches, x => x.PreparedAtUtc.HasValue && x.ExpiresAtUtc == x.PreparedAtUtc.Value.AddHours(3));
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "reagent_depleted"));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "run.reagent_consumption"));
        var commandTimeline = (await verifyContext.DeviceCommandExecutions
            .AsNoTracking()
            .Include(x => x.WorkflowStepExecution)
                .ThenInclude(x => x!.WorkflowExecution)
                    .ThenInclude(x => x!.SlideTask)
                        .ThenInclude(x => x!.ChannelBatch)
            .Where(x => x.MachineRunId == run.RunId && x.WorkflowStepExecution != null)
            .ToListAsync())
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
        var actualTimeline = commandTimeline.Select(x => new
        {
            x.WorkflowStepExecution!.StepNo,
            DrawerCode = x.WorkflowStepExecution.WorkflowExecution!.SlideTask!.ChannelBatch!.DrawerCode,
            x.WorkflowStepExecution.WorkflowExecution.SlideTask.SlotCode
        }).ToList();
        var expectedTimeline = actualTimeline
            .OrderBy(x => x.StepNo)
            .ThenBy(x => x.DrawerCode)
            .ThenBy(x => x.SlotCode)
            .ToList();
        Assert.Equal(expectedTimeline, actualTimeline);

        var pipetteCommands = await verifyContext.DeviceCommandExecutions
            .Where(x => x.MachineRunId == run.RunId && x.LiquidClassSelectionStatus == LiquidClassSelectionStatus.Frozen)
            .ToListAsync();
        Assert.NotEmpty(pipetteCommands);
        Assert.All(pipetteCommands, command =>
        {
            Assert.NotNull(command.LiquidClassVersionId);
            Assert.True(command.LiquidClassVersionNo > 0);
            Assert.Contains("aspirateSpeedUlPerSecond", command.LiquidClassParametersJson);
            Assert.Contains("liquidOperations", command.PayloadJson);
        });

        using var twinSnapshot = await client.GetFromJsonAsync<JsonDocument>("/api/twin/snapshot");
        var twinPayload = twinSnapshot!.RootElement.GetProperty("digitalTwinPayload");
        Assert.True(twinPayload.TryGetProperty("runtime", out var runtime), twinPayload.GetRawText());
        var runtimeSteps = runtime.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(commandTimeline.Count, runtimeSteps.Length);
        Assert.Equal(Enumerable.Range(1, runtimeSteps.Length), runtimeSteps.Select(x => x.GetProperty("executionOrder").GetInt32()));
        Assert.Equal("A-01", runtimeSteps.First(x => x.GetProperty("reagentCode").GetString() == "ABC").GetProperty("targetPointCode").GetString());
        Assert.Equal("NeedleWash", runtimeSteps.First(x => x.GetProperty("actionType").GetString() == "Wash").GetProperty("targetPointCode").GetString());
    }

    [Fact]
    public async Task Mock_dab_preparation_completed_converts_reservations_to_consumption_once()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "dab-success");

        string taskId;
        string dabBatchId;
        string dabA1Id;
        string dabA2Id;
        string dabBId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-DAB-PREP-SUCCESS", StainingTaskType.Ihc, "A-01",
                [
                    ("DAB", "Dab", "DAB", 100)
                ],
                []);
            dabA1Id = await AddBottleAsync(dbContext, "DAB-A", "DABA20260101001", 20, "R1");
            dabA2Id = await AddBottleAsync(dbContext, "DAB-A", "DABA20260101002", 100, "R2");
            dabBId = await AddBottleAsync(dbContext, "DAB-B", "DABB20260101001", 100, "R3");
            var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
            var actor = await AdminActorAsync(dbContext);
            var created = await service.CreateBatchAsync(new CreateDabBatchRequest(
                "cmd-dab-prep-success-create",
                [taskId],
                dabA1Id,
                dabBId,
                "M1"), actor);
            dabBatchId = created.BatchId;
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-dab-prep-success-run",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-dab-prep-success-start" });
        await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Completed);
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-dab-prep-success-start-replay" });

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batch = await verifyContext.DabBatches.SingleAsync(x => x.Id == dabBatchId);
        Assert.Equal(DabBatchStatus.Available, batch.Status);
        Assert.Equal(600, batch.ActualPreparedVolumeUl);
        Assert.Equal(30, batch.DabAVolumeUl);
        Assert.Equal(30, batch.DabBVolumeUl);
        Assert.Equal(540, batch.WaterVolumeUl);
        Assert.Equal(600, batch.RemainingVolumeUl);
        Assert.Equal(4, await verifyContext.ReagentReservations.CountAsync(x => x.DabBatchId == dabBatchId && x.Status == ReagentReservationStatus.Consumed));
        Assert.Equal(2, await verifyContext.ReagentConsumptions.CountAsync(x => x.DabBatchId == dabBatchId && x.ReagentCode == "DAB-A"));
        Assert.Equal(30, await verifyContext.ReagentConsumptions.Where(x => x.DabBatchId == dabBatchId && x.ReagentCode == "DAB-A").SumAsync(x => x.VolumeUl));
        Assert.Equal(30, await verifyContext.ReagentConsumptions.Where(x => x.DabBatchId == dabBatchId && x.ReagentCode == "DAB-B").SumAsync(x => x.VolumeUl));
        Assert.Equal(0, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == dabA1Id)).RemainingVolumeUl);
        Assert.Equal(90, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == dabA2Id)).RemainingVolumeUl);
        Assert.Equal(70, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == dabBId)).RemainingVolumeUl);
        var water = await verifyContext.SystemLiquidUsages.SingleAsync(x => x.DabBatchId == dabBatchId);
        Assert.Equal(SystemLiquidSourceTypes.SystemWater, water.SourceType);
        Assert.Equal(540, water.VolumeUl);
        Assert.Contains("SystemWater", water.LevelSnapshotJson, StringComparison.Ordinal);
        var dabCommand = await verifyContext.DeviceCommandExecutions.SingleAsync(x => x.Id == water.DeviceCommandExecutionId);
        Assert.Equal(DeviceCommandStatus.Completed, dabCommand.Status);
        Assert.NotNull(dabCommand.CommandSentAtUtc);
        Assert.NotNull(dabCommand.AcknowledgedAtUtc);
        Assert.NotNull(dabCommand.CompletedAtUtc);
        Assert.Equal(2, await verifyContext.ReagentConsumptions.CountAsync(x => x.DabBatchId == dabBatchId && x.DeviceCommandExecutionId == dabCommand.Id && x.ReagentCode == "DAB-A"));
    }

    [Theory]
    [InlineData(DeviceFaultTypes.FailNextCommand, RuntimeLedgerStatus.Failed, DabBatchStatus.Failed, ReagentReservationStatus.Released, false, "dab_preparation_failed")]
    [InlineData(DeviceFaultTypes.TimeoutNextCommand, RuntimeLedgerStatus.Unknown, DabBatchStatus.Unknown, ReagentReservationStatus.Reserved, true, "dab_preparation_unknown")]
    [InlineData(DeviceFaultTypes.Disconnect, RuntimeLedgerStatus.Unknown, DabBatchStatus.Unknown, ReagentReservationStatus.Reserved, true, "dab_preparation_unknown")]
    [InlineData(DeviceFaultTypes.ReturnUnknown, RuntimeLedgerStatus.Unknown, DabBatchStatus.Unknown, ReagentReservationStatus.Reserved, true, "dab_preparation_unknown")]
    public async Task Mock_dab_preparation_failure_and_unknown_follow_safe_reservation_rules(
        string faultType,
        string expectedStepStatus,
        string expectedBatchStatus,
        string expectedReservationStatus,
        bool expectUnknownCommand,
        string expectedAlarmCode)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, $"dab-{faultType}");

        string taskId;
        string dabBatchId;
        string dabAId;
        string dabBId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, $"IHC-DAB-{faultType}", StainingTaskType.Ihc, "A-01",
                [
                    ("DAB", "Dab", "DAB", 100)
                ],
                []);
            dabAId = await AddBottleAsync(dbContext, "DAB-A", $"DABA{Guid.NewGuid():N}"[..17], 100, "R1");
            dabBId = await AddBottleAsync(dbContext, "DAB-B", $"DABB{Guid.NewGuid():N}"[..17], 100, "R2");
            var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
            var created = await service.CreateBatchAsync(new CreateDabBatchRequest(
                $"cmd-dab-{faultType}-create",
                [taskId],
                dabAId,
                dabBId,
                "M1"), await AdminActorAsync(dbContext));
            dabBatchId = created.BatchId;
        }

        _ = await PostJsonAsync<DeviceFaultMutationResponse>(client, "/api/device/mock-faults", new
        {
            commandId = $"cmd-dab-{faultType}-fault",
            moduleCode = DeviceModules.Dab,
            faultType,
            reason = "DAB preparation test fault",
            errorCode = $"dab_{faultType}",
            message = $"Injected DAB {faultType}"
        });
        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = $"cmd-dab-{faultType}-run",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = $"cmd-dab-{faultType}-start" });
        _ = await WaitForRunAlarmAsync(client, run.RunId, expectedAlarmCode);
        var faulted = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);
        Assert.Equal(RuntimeLedgerStatus.Faulted, faulted.Status);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var step = await verifyContext.WorkflowStepExecutions.SingleAsync(x => x.WorkflowExecution!.MachineRunId == run.RunId);
        Assert.Equal(expectedStepStatus, step.Status);
        var batch = await verifyContext.DabBatches.Include(x => x.DabMixPosition).SingleAsync(x => x.Id == dabBatchId);
        Assert.Equal(expectedBatchStatus, batch.Status);
        Assert.Equal(DabMixPositionStatus.NeedsManualResolution, batch.DabMixPosition!.Status);
        Assert.All(await verifyContext.ReagentReservations.Where(x => x.DabBatchId == dabBatchId).ToListAsync(), x => Assert.Equal(expectedReservationStatus, x.Status));
        Assert.False(await verifyContext.ReagentConsumptions.AnyAsync(x => x.DabBatchId == dabBatchId));
        Assert.False(await verifyContext.SystemLiquidUsages.AnyAsync(x => x.DabBatchId == dabBatchId));
        Assert.Equal(100, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == dabAId)).RemainingVolumeUl);
        Assert.Equal(100, (await verifyContext.ReagentBottles.SingleAsync(x => x.Id == dabBId)).RemainingVolumeUl);
        var command = await verifyContext.DeviceCommandExecutions.SingleAsync(x => x.MachineRunId == run.RunId && x.CommandType == "Dab");
        Assert.Equal(expectUnknownCommand ? DeviceCommandStatus.Unknown : DeviceCommandStatus.Failed, command.Status);
    }

    [Fact]
    public async Task Pause_resume_fault_and_redo_complete_without_repeating_completed_actions()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "executor-002");

        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-PAUSE", StainingTaskType.Ihc, "B-01",
                [
                    ("PRIMARY_ANTIBODY", "Dispense", "ABC", 100),
                    ("PRIMARY_ANTIBODY", "Dispense", "ABC", 100),
                    ("SECONDARY_ANTIBODY", "Dispense", "SEC", 100),
                    ("HEMATOXYLIN", "Dispense", "HEM", 100)
                ],
                [("ABC", 200), ("SEC", 100), ("HEM", 100)]);
            await AddBottleAsync(dbContext, "ABC", "ABC05020260101003", 5000, "R1");
            await AddBottleAsync(dbContext, "SEC", "SEC05020260101001", 5000, "R2");
            await AddBottleAsync(dbContext, "HEM", "HEM05020260101002", 5000, "R3");
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-002",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-002" });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/pause", new { commandId = "cmd-run-pause-002" });

        var paused = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Paused);
        var completedBeforePause = paused.WorkflowExecutions.SelectMany(x => x.Steps).Count(x => x.Status == RuntimeLedgerStatus.Completed);
        Assert.True(completedBeforePause >= 1);

        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/fault", new
        {
            commandId = "cmd-run-fault-002",
            message = "Injected test fault"
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/resume", new { commandId = "cmd-run-resume-002" });
        var faulted = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);
        _ = await WaitForRunAlarmAsync(client, run.RunId, "mock_fault");

        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/redo-current-major-step", new
        {
            commandId = "cmd-run-redo-002",
            reason = "integration redo"
        });
        var completed = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Completed);
        Assert.Contains(completed.WorkflowExecutions.SelectMany(x => x.Steps), x => x.RedoCount > 0);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var consumptionVolume = await verifyContext.ReagentConsumptions
            .Where(x => x.MachineRunId == run.RunId && x.ReagentCode == "ABC")
            .SumAsync(x => x.VolumeUl);
        Assert.True(consumptionVolume >= 200);
    }

    [Fact]
    public async Task Expired_dab_batch_blocks_run_with_alarm()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "executor-003");

        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-DAB-EXPIRED", StainingTaskType.Ihc, "C-01",
                [
                    ("DAB", "Dab", "DAB", 100)
                ],
                []);
            var m1 = await dbContext.DabMixPositions.SingleAsync(x => x.Code == "M1");
            var batch = new DabBatch
            {
                DabMixPositionId = m1.Id,
                DabMixPosition = m1,
                PositionCode = "M1",
                Status = DabBatchStatus.Available,
                CleaningStatus = DabCleaningStatus.NotRequired,
                RemainingVolumeUl = 500,
                PreparedAtUtc = DateTimeOffset.UtcNow.AddHours(-4),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-4)
            };
            batch.Tasks.Add(new DabBatchTask
            {
                StainingTaskId = taskId,
                RequiredVolumeUl = DabFormula.VolumePerSlideUl,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            m1.Status = DabMixPositionStatus.Occupied;
            m1.ActiveDabBatchId = batch.Id;
            dbContext.DabBatches.Add(batch);
            await dbContext.SaveChangesAsync();
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-003",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-003" });

        var faulted = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);
        _ = await WaitForRunAlarmAsync(client, run.RunId, "dab_expired");
    }

    [Fact]
    public async Task Expiry_processing_creates_alarm_and_repreparation_on_a_new_position_once()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        string taskId;
        string batchId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-DAB-REPREPARE", StainingTaskType.Ihc, "C-02", [("DAB", "Dab", "DAB", 200)], []);
            var dabAId = await AddBottleAsync(dbContext, "DAB-A", "DABA20260101991", 1000, "R1");
            var dabBId = await AddBottleAsync(dbContext, "DAB-B", "DABB20260101991", 1000, "R2");
            var created = await scope.ServiceProvider.GetRequiredService<DabLifecycleService>().CreateBatchAsync(
                new CreateDabBatchRequest("cmd-dab-reprepare-original", [taskId], dabAId, dabBId, "M1"),
                await AdminActorAsync(dbContext));
            batchId = created.BatchId;
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-dab-reprepare-run",
            stainingTaskIds = new[] { taskId }
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var batch = await dbContext.DabBatches.Include(x => x.ReagentReservations).SingleAsync(x => x.Id == batchId);
            batch.Status = DabBatchStatus.Available;
            batch.ActualPreparedVolumeUl = 600;
            batch.RemainingVolumeUl = 600;
            batch.PreparedAtUtc = DateTimeOffset.UtcNow.AddHours(-4);
            batch.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            foreach (var reservation in batch.ReagentReservations) reservation.Status = ReagentReservationStatus.Consumed;
            await dbContext.SaveChangesAsync();

            var service = scope.ServiceProvider.GetRequiredService<DabLifecycleService>();
            var first = await service.ProcessExpirationsAsync(DateTimeOffset.UtcNow);
            var replay = await service.ProcessExpirationsAsync(DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.Equal(1, first.NewlyExpiredCount);
            Assert.Equal(1, first.RepreparationPlanCount);
            Assert.Equal(1, first.ReplacementBatchCount);
            Assert.Equal(0, replay.NewlyExpiredCount);
            Assert.Equal(0, replay.RepreparationPlanCount);
            Assert.Equal(0, replay.ReplacementBatchCount);
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var original = await verify.DabBatches.Include(x => x.DabMixPosition).SingleAsync(x => x.Id == batchId);
        Assert.Equal(DabBatchStatus.Expired, original.Status);
        Assert.Equal(DabMixPositionStatus.AwaitingCleaning, original.DabMixPosition!.Status);
        var plan = await verify.DabRepreparationPlans.Include(x => x.ReplacementDabBatch).ThenInclude(x => x!.DabMixPosition).SingleAsync();
        Assert.Equal(DabRepreparationPlanStatus.Planned, plan.Status);
        Assert.Equal(run.RunId, plan.MachineRunId);
        Assert.NotNull(plan.ReplacementDabBatch);
        Assert.Equal(DabBatchStatus.PendingPreparation, plan.ReplacementDabBatch!.Status);
        Assert.NotEqual("M1", plan.ReplacementDabBatch.PositionCode);
        Assert.Equal(DabMixPositionStatus.Occupied, plan.ReplacementDabBatch.DabMixPosition!.Status);
        Assert.True(await verify.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "dab_expired_repreparation_required"));
        Assert.Equal(1, await verify.DabRepreparationPlans.CountAsync());
    }

    [Fact]
    public async Task Expiry_repreparation_waits_when_all_mix_positions_require_cleaning()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        string taskId;
        string batchId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-DAB-REPREPARE-BLOCKED", StainingTaskType.Ihc, "C-03", [("DAB", "Dab", "DAB", 200)], []);
            var dabAId = await AddBottleAsync(dbContext, "DAB-A", "DABA20260101992", 1000, "R1");
            var dabBId = await AddBottleAsync(dbContext, "DAB-B", "DABB20260101992", 1000, "R2");
            var created = await scope.ServiceProvider.GetRequiredService<DabLifecycleService>().CreateBatchAsync(
                new CreateDabBatchRequest("cmd-dab-reprepare-blocked-original", [taskId], dabAId, dabBId, "M1"),
                await AdminActorAsync(dbContext));
            batchId = created.BatchId;
        }
        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new { commandId = "cmd-dab-reprepare-blocked-run", stainingTaskIds = new[] { taskId } });

        await using var processScope = factory.Services.CreateAsyncScope();
        var context = processScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batch = await context.DabBatches.Include(x => x.ReagentReservations).SingleAsync(x => x.Id == batchId);
        batch.Status = DabBatchStatus.Available;
        batch.PreparedAtUtc = DateTimeOffset.UtcNow.AddHours(-4);
        batch.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var reservation in batch.ReagentReservations) reservation.Status = ReagentReservationStatus.Consumed;
        foreach (var position in await context.DabMixPositions.Where(x => x.Code != "M1").ToListAsync())
        {
            position.Status = DabMixPositionStatus.AwaitingCleaning;
            position.ActiveDabBatchId = Guid.NewGuid().ToString();
        }
        await context.SaveChangesAsync();

        var result = await processScope.ServiceProvider.GetRequiredService<DabLifecycleService>().ProcessExpirationsAsync(DateTimeOffset.UtcNow);
        Assert.Equal(1, result.NewlyExpiredCount);
        Assert.Equal(0, result.ReplacementBatchCount);
        var plan = await context.DabRepreparationPlans.SingleAsync();
        Assert.Equal(DabRepreparationPlanStatus.AwaitingMixPosition, plan.Status);
        Assert.Null(plan.ReplacementDabBatchId);
        Assert.True(await context.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "dab_mix_area_cleaning_required"));
    }

    [Fact]
    public async Task Redo_requires_mock_device_state_to_be_ready()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "executor-004");

        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-DEVICE-REDO", StainingTaskType.Ihc, "A-03",
                [
                    ("PRIMARY_ANTIBODY", "Dispense", "ABC", 100),
                    ("HEMATOXYLIN", "Dispense", "HEM", 100)
                ],
                [("ABC", 100), ("HEM", 100)]);
            await AddBottleAsync(dbContext, "ABC", "ABC05020260101008", 5000, "R1");
            await AddBottleAsync(dbContext, "HEM", "HEM05020260101008", 5000, "R2");
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-004",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/fault", new
        {
            commandId = "cmd-run-fault-004",
            message = "Injected device readiness test fault"
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-004" });
        await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var profiles = await dbContext.DeviceProfiles.ToListAsync();
            foreach (var profile in profiles)
            {
                profile.IsActive = false;
            }

            await dbContext.SaveChangesAsync();
        }

        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/redo-current-major-step", new
        {
            commandId = "cmd-run-redo-004",
            reason = "device state check"
        });
        var blocked = await WaitForRunAlarmAsync(client, run.RunId, "redo_device_not_ready");
        Assert.Equal(RuntimeLedgerStatus.Faulted, blocked.Status);
        Assert.DoesNotContain(blocked.WorkflowExecutions.SelectMany(x => x.Steps), x => x.RedoCount > 0);
    }

    [Fact]
    public async Task Machine_hub_pushes_temperature_step_and_alarm_events()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cookie = await LoginAsync(client, "admin", "admin");
        await InitializeDevicesAsync(client, "executor-signalr");

        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            taskId = await CreateConfirmedTaskAsync(dbContext, "IHC-SIGNALR", StainingTaskType.Ihc, "B-02",
                [
                    ("PRETREATMENT", "Heat", null, null),
                    ("DAB", "Dab", "DAB", 100)
                ],
                []);
            var m1 = await dbContext.DabMixPositions.SingleAsync(x => x.Code == "M1");
            var batch = new DabBatch
            {
                DabMixPositionId = m1.Id,
                DabMixPosition = m1,
                PositionCode = "M1",
                Status = DabBatchStatus.Available,
                CleaningStatus = DabCleaningStatus.NotRequired,
                RemainingVolumeUl = 500,
                PreparedAtUtc = DateTimeOffset.UtcNow.AddHours(-4),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-4)
            };
            batch.Tasks.Add(new DabBatchTask
            {
                StainingTaskId = taskId,
                RequiredVolumeUl = DabFormula.VolumePerSlideUl,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            m1.Status = DabMixPositionStatus.Occupied;
            m1.ActiveDabBatchId = batch.Id;
            dbContext.DabBatches.Add(batch);
            await dbContext.SaveChangesAsync();
        }

        using var socket = await ConnectMachineHubAsync(factory, cookie);
        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-signalr",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-signalr" });

        var receivedTypes = await ReceiveMachineEventTypesAsync(
            socket,
            new HashSet<string>(["temperature.changed", "workflowStep.completed", "alarm.raised"], StringComparer.Ordinal));
        Assert.Contains("temperature.changed", receivedTypes);
        Assert.Contains("workflowStep.completed", receivedTypes);
        Assert.Contains("alarm.raised", receivedTypes);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var point = await verifyContext.ThermalPointStates.SingleAsync(x => x.DrawerCode == "B" && x.SlotNo == 2);
        Assert.Equal(ThermalStatuses.Stable, point.Status);
        Assert.Equal(250, point.TargetTemperatureDeciC);
        Assert.True(await verifyContext.TemperatureTelemetry.AnyAsync(x => x.SourceId == point.Id && x.Status == ThermalStatuses.Stable));
        var command = await verifyContext.DeviceCommandExecutions.SingleAsync(x => x.MachineRunId == run.RunId && x.CommandType == "Heat");
        Assert.Contains("TargetTemperatureDeciC", command.PayloadJson);
    }

    [Fact]
    public async Task Legacy_run_page_start_endpoint_controls_runtime_ledger()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");
        await InitializeDevicesAsync(client, "executor-legacy");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            await CreateConfirmedTaskAsync(dbContext, "HE-PAGE", StainingTaskType.He, "D-01",
                [
                    ("HEMATOXYLIN", "Dispense", "HEM", 100),
                    ("TERMINAL_WASH", "Wash", null, null)
                ],
                [("HEM", 100)]);
            await AddBottleAsync(dbContext, "HEM", "HEM05020260101009", 5000, "R1");
        }

        var response = await client.PostAsync("/api/run/start", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await WaitForPageStatusAsync(client, "completed");
        Assert.Equal("completed", state.GetProperty("status").GetString());
        Assert.Contains(
            state.GetProperty("channels").EnumerateArray(),
            channel => channel.GetProperty("slides").GetArrayLength() == 1);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var runId = await verifyContext.MachineRuns.Select(x => x.Id).SingleAsync();
        Assert.True(await verifyContext.DeviceCommandExecutions.AnyAsync(x => x.MachineRunId == runId));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-runtime-ledger-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                        ["MachineExecutor:StepVisibleDelayMilliseconds"] = "0",
                        ["Motion:PipetteAspirateVisibleMilliseconds"] = "0",
                        ["Motion:PipetteWashVisibleMilliseconds"] = "0"
                    });
                });
            });
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new
        {
            username,
            password = "123456",
            role
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie")
            .First(x => x.StartsWith("stainer_session=", StringComparison.OrdinalIgnoreCase))
            .Split(';', 2)[0];
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

    private static async Task<MachineRunDetailResponse> WaitForRunAlarmAsync(HttpClient client, string runId, string alarmCode)
    {
        for (var i = 0; i < 120; i++)
        {
            var detail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{runId}");
            Assert.NotNull(detail);
            var diagnostics = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringErrorCodeResponse>>(
                $"/api/engineering/diagnostics/errors?machineRunId={Uri.EscapeDataString(runId)}&code={Uri.EscapeDataString(alarmCode)}&pageSize=10");
            if (diagnostics?.Items.Any(x => x.Code == alarmCode) == true)
            {
                return detail!;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Run did not raise alarm {alarmCode}.");
        throw new UnreachableException();
    }

    private static async Task<JsonElement> WaitForPageStatusAsync(HttpClient client, string status)
    {
        for (var i = 0; i < 120; i++)
        {
            using var response = await client.GetAsync("/api/state");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement.Clone();
            if (root.GetProperty("status").GetString() == status)
            {
                return root;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Page state did not reach {status}.");
        throw new UnreachableException();
    }

    private static async Task<WebSocket> ConnectMachineHubAsync(WebApplicationFactory<Program> factory, string cookie)
    {
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Cookie"] = cookie;
        var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/hubs/machine"), CancellationToken.None);
        await SendSignalRFrameAsync(socket, "{\"protocol\":\"json\",\"version\":1}");
        await ReceiveSignalRFramesAsync(socket, TimeSpan.FromSeconds(3));
        return socket;
    }

    private static async Task<HashSet<string>> ReceiveMachineEventTypesAsync(WebSocket socket, IReadOnlySet<string> expectedTypes)
    {
        var receivedTypes = new HashSet<string>(StringComparer.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!timeout.IsCancellationRequested && !expectedTypes.All(receivedTypes.Contains))
        {
            foreach (var frame in await ReceiveSignalRFramesAsync(socket, TimeSpan.FromSeconds(2)))
            {
                if (!frame.TryGetProperty("type", out var signalRType) || signalRType.GetInt32() != 1)
                {
                    continue;
                }

                if (!frame.TryGetProperty("target", out var target) || target.GetString() != "machineEvent")
                {
                    continue;
                }

                var machineEvent = frame.GetProperty("arguments")[0];
                var eventType = machineEvent.GetProperty("type").GetString();
                if (!string.IsNullOrWhiteSpace(eventType))
                {
                    receivedTypes.Add(eventType);
                }
            }
        }

        return receivedTypes;
    }

    private static async Task SendSignalRFrameAsync(WebSocket socket, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + '\x1e');
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReceiveSignalRFramesAsync(WebSocket socket, TimeSpan timeout)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return [];
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                Assert.Fail("Machine hub WebSocket closed before expected events were received.");
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage || !builder.ToString().Contains('\x1e', StringComparison.Ordinal))
            {
                continue;
            }

            var frames = new List<JsonElement>();
            foreach (var rawFrame in builder.ToString().Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(rawFrame);
                frames.Add(document.RootElement.Clone());
            }

            return frames;
        }

        return [];
    }

    private static async Task<string> CreateConfirmedTaskAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string workflowType,
        string slotCode,
        IReadOnlyList<(string MajorStepCode, string ActionType, string? ReagentCode, int? VolumeUl)> steps,
        IReadOnlyList<(string ReagentCode, int RequiredVolumeUl)> requirements)
    {
        var defaultLiquidClass = await dbContext.LiquidClassProfiles
            .Include(x => x.EnabledVersion)
            .SingleAsync(x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId != null);
        foreach (var requirement in requirements)
        {
            if (!await dbContext.ReagentDefinitions.AnyAsync(x => x.ReagentCode == requirement.ReagentCode))
            {
                dbContext.ReagentDefinitions.Add(new ReagentDefinition
                {
                    ReagentCode = requirement.ReagentCode,
                    Name = $"Reagent {requirement.ReagentCode}",
                    ReagentType = "test",
                    LiquidClassProfileId = defaultLiquidClass.Id,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }
        await dbContext.SaveChangesAsync();

        var workflowDefinition = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = $"{workflowCode} workflow",
            WorkflowType = workflowType,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = 1,
            VersionLabel = "1.0",
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "Runtime test workflow.",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var stepNo = 1;
        foreach (var step in steps)
        {
            workflowVersion.Steps.Add(new WorkflowStep
            {
                StepNo = stepNo++,
                MajorStepCode = step.MajorStepCode,
                StepName = step.MajorStepCode,
                ActionType = step.ActionType,
                ReagentCode = step.ReagentCode,
                VolumeUl = step.VolumeUl,
                DurationSeconds = 1,
                TargetTemperatureDeciC = 250,
                FailureStrategy = "Stop",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        foreach (var requirement in requirements)
        {
            workflowVersion.ReagentRequirements.Add(new WorkflowReagentRequirement
            {
                ReagentCode = requirement.ReagentCode,
                RequiredVolumeUl = requirement.RequiredVolumeUl,
                IsRequired = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var slot = await dbContext.PhysicalSlots.Include(x => x.Drawer).SingleAsync(x => x.Code == slotCode);
        var snapshot = $$"""{"workflowVersionId":"{{workflowVersion.Id}}","workflowType":"{{workflowType}}","source":"runtime-test"}""";
        var coordinateVersionId = await dbContext.CoordinateProfileVersions
            .Where(x => x.IsActive && x.Status == CoordinateProfileVersionStatus.Active)
            .Select(x => x.Id)
            .SingleAsync();
        var coordinateSnapshot = BuildCoordinateSnapshot(coordinateVersionId);
        var liquidClassSnapshot = await new LiquidClassSnapshotFactory(dbContext).FreezeForWorkflowAsync(workflowVersion);
        var task = new StainingTask
        {
            TaskCode = $"TASK-{workflowCode}",
            TaskType = workflowType,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = slot.Id,
            WorkflowDefinition = workflowDefinition,
            WorkflowVersion = workflowVersion,
            WorkflowSnapshotJson = snapshot,
            CandidateResultsJson = "[]",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.StainingTasks.Add(task);
        var batch = await dbContext.ChannelBatches
            .Include(x => x.SlideTasks)
            .SingleOrDefaultAsync(x => x.DrawerId == slot.DrawerId && x.Status == RuntimeLedgerStatus.Pending);
        if (batch is null)
        {
            batch = new ChannelBatch
            {
                DrawerId = slot.DrawerId,
                DrawerCode = slot.Drawer!.Code,
                Status = RuntimeLedgerStatus.Pending,
                ExperimentType = workflowType,
                SelectedWorkflowVersion = workflowVersion,
                WorkflowSnapshotJson = snapshot,
                CoordinateProfileVersionId = coordinateVersionId,
                CoordinateSnapshotJson = coordinateSnapshot,
                CoordinateSelectionStatus = CoordinateSelectionStatus.Frozen,
                LiquidClassSnapshotJson = liquidClassSnapshot,
                LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen,
                WorkflowSelectionStatus = WorkflowSelectionStatus.Selected,
                WorkflowSelectedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ChannelBatches.Add(batch);
        }
        else
        {
            Assert.Equal(workflowType, batch.ExperimentType);
            Assert.Equal(workflowVersion.Id, batch.SelectedWorkflowVersionId);
            Assert.Equal(CoordinateSelectionStatus.Frozen, batch.CoordinateSelectionStatus);
            Assert.Equal(coordinateVersionId, batch.CoordinateProfileVersionId);
        }

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

    private static async Task<AuthenticatedUser> AdminActorAsync(StainerDbContext dbContext)
    {
        var admin = await dbContext.Users.SingleAsync(x => x.Username == "admin");
        return new AuthenticatedUser(admin.Id, admin.Username, admin.DisplayName, "admin", ["admin"]);
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
            ProductionBatchNo = barcode.Length >= 14 ? barcode.Substring(6, 8) : "20260101",
            SerialNo = barcode.Length >= 17 ? barcode.Substring(14, 3) : Guid.NewGuid().ToString("N")[..3],
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
        var targetPoints = new List<object>();
        var index = 0;
        foreach (var drawer in new[] { "A", "B", "C", "D" })
        {
            for (var slot = 1; slot <= 4; slot++)
            {
                targetPoints.Add(new
                {
                    pointCode = $"{drawer}-{slot:00}",
                    pointType = "SlideSlot",
                    calibratedXUm = 100_000 + index * 25_000,
                    calibratedYUm = 100_000 + (drawer[0] - 'A') * 25_000,
                    calibratedZUm = 10_000,
                    safeZUm = 20_000,
                    liquidDetectZUm = 8_000,
                    dispenseZUm = 7_000,
                    validationStatus = "Verified",
                    requiresCalibration = false,
                    isEnabled = true
                });
                index++;
            }
        }

        for (var rack = 1; rack <= 8; rack++)
        {
            targetPoints.Add(new
            {
                pointCode = $"R{rack}",
                pointType = "ReagentRack",
                calibratedXUm = 50_000 + rack * 20_000,
                calibratedYUm = 30_000,
                calibratedZUm = 10_000,
                safeZUm = 20_000,
                liquidDetectZUm = 6_000,
                dispenseZUm = 5_000,
                validationStatus = "Verified",
                requiresCalibration = false,
                isEnabled = true
            });
        }

        targetPoints.Add(new { pointCode = "M1", pointType = "DabMix", calibratedXUm = 300_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true });
        targetPoints.Add(new { pointCode = "NeedleWash", pointType = "NeedleWash", calibratedXUm = 320_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true });
        targetPoints.Add(new { pointCode = "WashInnerLeft", pointType = "WashInner", calibratedXUm = 340_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true });
        targetPoints.Add(new { pointCode = "WashOuterLeft", pointType = "WashOuter", calibratedXUm = 360_000, calibratedYUm = 50_000, calibratedZUm = 10_000, safeZUm = 20_000, liquidDetectZUm = 6_000, dispenseZUm = 5_000, validationStatus = "Verified", requiresCalibration = false, isEnabled = true });
        return JsonSerializer.Serialize(new { coordinateProfileVersionId = coordinateVersionId, source = "runtime-test", targetPoints });
    }
}
