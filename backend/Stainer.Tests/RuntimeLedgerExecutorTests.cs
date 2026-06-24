using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
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
            heTaskId = await CreateConfirmedTaskAsync(dbContext, "HE-RUN", StainingTaskType.He, "A-02",
                [
                    ("HEMATOXYLIN", "Dispense", "HEM", 100),
                    ("TERMINAL_WASH", "Wash", null, null)
                ],
                [("HEM", 100)]);
            await AddBottleAsync(dbContext, "ABC", "ABC00320260101001", 300, "R1");
            await AddBottleAsync(dbContext, "ABC", "ABC00720260101002", 700, "R2");
            await AddBottleAsync(dbContext, "HEM", "HEM05020260101001", 5000, "R3");
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
        Assert.Contains(dabBatches, x => x.ExpiresAtUtc == x.PreparedAtUtc.AddHours(3));
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.MachineRunId == run.RunId && x.Code == "reagent_depleted"));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "run.reagent_consumption"));
    }

    [Fact]
    public async Task Pause_resume_fault_and_redo_complete_without_repeating_completed_actions()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

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
        Assert.Contains(faulted.Alarms, x => x.Code == "mock_fault");

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
            dbContext.DabBatches.Add(new DabBatch
            {
                DabMixPositionId = m1.Id,
                PositionCode = "M1",
                Status = RuntimeLedgerStatus.Available,
                RemainingVolumeUl = 500,
                PreparedAtUtc = DateTimeOffset.UtcNow.AddHours(-4),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-4)
            });
            await dbContext.SaveChangesAsync();
        }

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-run-create-003",
            stainingTaskIds = new[] { taskId }
        });
        await PostJsonAsync<RunCommandResponse>(client, $"/api/runs/{run.RunId}/start", new { commandId = "cmd-run-start-003" });

        var faulted = await WaitForRunStatusAsync(client, run.RunId, RuntimeLedgerStatus.Faulted);
        Assert.Contains(faulted.Alarms, x => x.Code == "dab_expired");
    }

    [Fact]
    public async Task Redo_requires_mock_device_state_to_be_ready()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

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
    public async Task Legacy_run_page_start_endpoint_controls_runtime_ledger()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

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
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-runtime-ledger-tests", Guid.NewGuid().ToString("N"), "stainer.db");
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

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
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
            if (detail!.Alarms.Any(x => x.Code == alarmCode))
            {
                return detail;
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

    private static async Task<string> CreateConfirmedTaskAsync(
        StainerDbContext dbContext,
        string workflowCode,
        string workflowType,
        string slotCode,
        IReadOnlyList<(string MajorStepCode, string ActionType, string? ReagentCode, int? VolumeUl)> steps,
        IReadOnlyList<(string ReagentCode, int RequiredVolumeUl)> requirements)
    {
        foreach (var requirement in requirements)
        {
            if (!await dbContext.ReagentDefinitions.AnyAsync(x => x.ReagentCode == requirement.ReagentCode))
            {
                dbContext.ReagentDefinitions.Add(new ReagentDefinition
                {
                    ReagentCode = requirement.ReagentCode,
                    Name = $"Reagent {requirement.ReagentCode}",
                    ReagentType = "test",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

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

        var slot = await dbContext.PhysicalSlots.SingleAsync(x => x.Code == slotCode);
        var task = new StainingTask
        {
            TaskCode = $"TASK-{workflowCode}",
            TaskType = workflowType,
            Status = StainingTaskStatus.Confirmed,
            PhysicalSlotId = slot.Id,
            WorkflowDefinition = workflowDefinition,
            WorkflowVersion = workflowVersion,
            WorkflowSnapshotJson = "{}",
            CandidateResultsJson = "[]",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.StainingTasks.Add(task);
        await dbContext.SaveChangesAsync();
        return task.Id;
    }

    private static async Task AddBottleAsync(StainerDbContext dbContext, string reagentCode, string barcode, int volumeUl, string positionCode)
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
    }
}
