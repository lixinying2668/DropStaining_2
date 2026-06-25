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

public sealed class WebHostIntegrationTests
{
    [Fact]
    public async Task Web_host_serves_pages_static_assets_health_api_and_fallback()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var info = await client.GetFromJsonAsync<SystemInfoResponse>("/api/system/info");
        Assert.NotNull(info);
        Assert.False(info!.PythonRuntimeRequired);
        Assert.Equal("ASP.NET Core", info.UiHost);

        foreach (var route in new[] { "/", "/dashboard", "/samples", "/reagents", "/run", "/alerts", "/history", "/configure", "/engineer", "/admin", "/mock-timeline" })
        {
            var html = await client.GetStringAsync(route);
            Assert.Contains("app.css", html);
            Assert.DoesNotContain("{%", html);
            Assert.DoesNotContain("{{", html);
        }

        var dashboard = await client.GetStringAsync("/dashboard");
        Assert.Contains("app-shell", dashboard);
        Assert.Contains("drawerBoard", dashboard);

        var mockTimeline = await client.GetStringAsync("/mock-timeline");
        Assert.Contains("mockGanttBoard", mockTimeline);
        Assert.Contains("/static/js/mock-timeline.js", mockTimeline);

        var css = await client.GetAsync("/static/css/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("text/css", css.Content.Headers.ContentType?.MediaType);

        var js = await client.GetAsync("/static/js/api.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);

        var fallback = await client.GetStringAsync("/kiosk/unknown");
        Assert.Contains("drawerBoard", fallback);
    }

    [Fact]
    public async Task Mock_timeline_page_is_hidden_in_production()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/mock-timeline");

        Assert.Contains("drawerBoard", html);
        Assert.DoesNotContain("mockGanttBoard", html);
        Assert.DoesNotContain("mock-timeline.js", html);
    }

    [Fact]
    public async Task Web_host_mock_api_supports_login_initialize_and_state()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/login", new { username = "operator", password = "123456", role = "operator" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var initialize = await client.PostAsync("/api/system/initialize", null);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        var samples = await client.PostAsync("/api/samples/scan?count=4", null);
        Assert.Equal(HttpStatusCode.OK, samples.StatusCode);

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");
        Assert.NotNull(state);
        Assert.True(state!.Initialized);
        Assert.Equal("ready", state.Status);
        Assert.Equal(4, state.Channels.SelectMany(x => x.Slides).Count());
    }

    [Fact]
    public async Task State_api_returns_current_run_snapshot_for_page_rendering()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        string slideTaskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
            var slot = await dbContext.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
            var workflow = new WorkflowDefinition
            {
                Code = "STATE-HE",
                Name = "State HE Workflow",
                WorkflowType = StainingTaskType.He,
                Description = "State API integration test"
            };
            var version = new WorkflowVersion
            {
                VersionNo = 810,
                VersionLabel = "8.1",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "state-api-test",
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            workflow.Versions.Add(version);

            var task = new StainingTask
            {
                TaskCode = "STATE-TASK-001",
                TaskType = StainingTaskType.He,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlot = slot,
                WorkflowDefinition = workflow,
                WorkflowVersion = version,
                WorkflowSnapshotJson = "{}",
                InputMode = "manual"
            };
            var run = new MachineRun
            {
                RunCode = "RUN-STATE-001",
                Status = RuntimeLedgerStatus.Running,
                CurrentMajorStepCode = "HEMATOXYLIN",
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            var batch = new ChannelBatch
            {
                MachineRun = run,
                DrawerId = drawer.Id,
                DrawerCode = "A",
                Status = RuntimeLedgerStatus.Running
            };
            var slide = new SlideTask
            {
                ChannelBatch = batch,
                StainingTask = task,
                PhysicalSlot = slot,
                SlotCode = "A-01",
                TaskType = StainingTaskType.He,
                Status = RuntimeLedgerStatus.Running
            };
            var execution = new WorkflowExecution
            {
                MachineRun = run,
                SlideTask = slide,
                WorkflowVersion = version,
                Status = RuntimeLedgerStatus.Running
            };
            execution.StepExecutions.Add(new WorkflowStepExecution
            {
                StepNo = 1,
                MajorStepCode = "HEMATOXYLIN",
                StepName = "Hematoxylin",
                ActionType = "Dispense",
                ReagentCode = "HEM",
                VolumeUl = 100,
                Status = RuntimeLedgerStatus.Running
            });
            dbContext.MachineRuns.Add(run);
            dbContext.StainingTasks.Add(task);
            dbContext.ChannelBatches.Add(batch);
            dbContext.SlideTasks.Add(slide);
            dbContext.WorkflowExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            slideTaskId = slide.Id;
        }

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");

        Assert.NotNull(state);
        Assert.Equal("running", state!.Status);
        Assert.Equal(4, state.Channels.Length);
        Assert.Contains(state.Channels.SelectMany(x => x.Slides), x => x.Id == slideTaskId);
    }

    [Fact]
    public async Task Read_apis_return_values_inserted_into_sqlite_not_legacy_json()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        string workflowId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var operatorRole = await dbContext.Roles.SingleAsync(x => x.Code == "operator");
            var r1 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R1");

            var user = new User
            {
                Username = "db-reader",
                DisplayName = "SQLite Reader"
            };
            user.UserRoles.Add(new UserRole { RoleId = operatorRole.Id });
            dbContext.Users.Add(user);

            var workflow = new WorkflowDefinition
            {
                Code = "DB-IHC",
                Name = "SQLite IHC Workflow",
                WorkflowType = "IHC",
                Description = "Inserted by integration test"
            };
            var version = new WorkflowVersion
            {
                VersionNo = 900,
                VersionLabel = "9.0",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "integration-test",
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            version.Steps.Add(new WorkflowStep
            {
                StepNo = 1,
                MajorStepCode = "ANTIBODY",
                StepName = "SQLite Antibody Step",
                ActionType = "dispense",
                ReagentCode = "TDB",
                VolumeUl = 123,
                DurationSeconds = 45,
                TargetTemperatureDeciC = 420,
                FailureStrategy = "Stop"
            });
            workflow.Versions.Add(version);
            dbContext.WorkflowDefinitions.Add(workflow);
            workflowId = workflow.Id;

            var liquidClass = new LiquidClassProfile
            {
                Code = "LC-WEB",
                Name = "SQLite Liquid Class",
                AspirateSpeedUlPerSecond = 11,
                DispenseSpeedUlPerSecond = 22
            };
            var reagent = new ReagentDefinition
            {
                ReagentCode = "TDB",
                Name = "SQLite Test Reagent",
                ReagentType = "Primary",
                LiquidClassProfile = liquidClass,
                MinimumAlarmVolumeUl = 1000
            };
            var bottle = new ReagentBottle
            {
                ReagentDefinition = reagent,
                FullBarcode = "TDB05020260101001",
                ReagentCode = "TDB",
                ProductionBatchNo = "20260101",
                SerialNo = "001",
                InitialVolumeUl = 5000,
                RemainingVolumeUl = 4500,
                ExpirationDate = new DateOnly(2027, 1, 1),
                Status = "Available",
                FirstScannedAtUtc = DateTimeOffset.UtcNow,
                LastScannedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
            {
                ReagentBottle = bottle,
                ReagentRackPositionId = r1.Id,
                PlacedAtUtc = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        var workflows = await client.GetFromJsonAsync<List<WorkflowSummaryResponse>>("/api/workflows");
        Assert.Contains(workflows!, x => x.Code == "DB-IHC" && x.Name == "SQLite IHC Workflow");

        var workflowDetail = await client.GetFromJsonAsync<WorkflowDetailResponse>($"/api/workflows/{workflowId}");
        Assert.NotNull(workflowDetail);
        Assert.Equal("SQLite Antibody Step", workflowDetail!.Versions.Single().Steps.Single().StepName);

        var protocols = await client.GetFromJsonAsync<List<ProtocolCompatResponse>>("/api/protocols");
        Assert.Contains(protocols!, x => x.Code == "DB-IHC" && x.Version == "9.0");

        var catalog = await client.GetFromJsonAsync<List<ReagentCatalogItemResponse>>("/api/reagents/catalog");
        Assert.Contains(catalog!, x => x.ReagentCode == "TDB" && x.Name == "SQLite Test Reagent" && x.LiquidClassCode == "LC-WEB");

        var rack = await client.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");
        var rackR1 = Assert.Single(rack!, x => x.Position == "R1");
        Assert.Equal("TDB", rackR1.Bottle?.ReagentCode);
        Assert.Equal(4500, rackR1.Bottle?.RemainingVolumeUl);

        var layout = await client.GetFromJsonAsync<EngineeringLayoutResponse>("/api/engineering/layout");
        Assert.Equal(4, layout!.Drawers.Count);
        Assert.Equal(16, layout.Drawers.SelectMany(x => x.Slots).Count());
        Assert.Equal(40, layout.ReagentRackPositions.Count);
        Assert.Equal(8, layout.DabMixPositions.Count);
        Assert.Equal(4, layout.WashPositions.Count);

        var coordinateProfiles = await client.GetFromJsonAsync<List<CoordinateProfileResponse>>("/api/engineering/coordinate-profiles");
        var factoryDefault = Assert.Single(coordinateProfiles!, x => x.Code == "FactoryDefault-v1");
        Assert.Contains(factoryDefault.Points, x => x.PointCode == "Needle1" && x.PresetXUm == 0 && x.PresetYUm == 0);
        Assert.Contains(factoryDefault.Points, x => x.PointCode == "Needle2" && x.PresetXUm == 25000 && x.PresetYUm == 0);

        var liquidClasses = await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes");
        Assert.Contains(liquidClasses!, x => x.Code == "LC-WEB" && x.AspirateSpeedUlPerSecond == 11);

        var users = await client.GetFromJsonAsync<List<UserListItemResponse>>("/api/users");
        Assert.Contains(users!, x => x.Username == "db-reader" && x.Roles.Contains("operator"));

        var roles = await client.GetFromJsonAsync<List<RoleListItemResponse>>("/api/roles");
        Assert.Contains(roles!, x => x.Code == "operator");
        Assert.Contains(roles!, x => x.Code == "engineer");
        Assert.Contains(roles!, x => x.Code == "admin");
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment = "Testing")
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-web-host-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
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

    private sealed record SystemInfoResponse(bool PythonRuntimeRequired, string UiHost);

    private sealed record RuntimeStateResponse(bool Initialized, string Status, RuntimeChannel[] Channels);

    private sealed record RuntimeChannel(RuntimeSlide[] Slides);

    private sealed record RuntimeSlide(string Id);
}
