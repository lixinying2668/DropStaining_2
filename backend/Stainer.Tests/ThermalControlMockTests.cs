using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
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

public sealed class ThermalControlMockTests
{
    [Fact]
    public async Task Sixteen_points_and_cooling_simulate_transitions_write_telemetry_and_publish_events()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var initial = await client.GetFromJsonAsync<ThermalStateResponse>("/api/thermal/state");
        Assert.NotNull(initial);
        Assert.True(initial!.Ready);
        Assert.Equal(16, initial.Points.Count);
        Assert.All(new[] { "A", "B", "C", "D" }, drawer =>
        {
            var board = drawer[0] - 'A';
            var points = initial.Points.Where(x => x.DrawerCode == drawer).OrderBy(x => x.SlotNo).ToList();
            Assert.Equal(4, points.Count);
            Assert.All(points, point => Assert.Equal(board, point.BoardNo));
            Assert.Equal(new[] { 0, 1, 2, 3 }, points.Select(x => x.PointNo));
        });

        var pointSet = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/points/A/1", new
        {
            commandId = "cmd-thermal-point-a1",
            targetTemperatureDeciC = 420,
            isEnabled = true
        });
        Assert.Equal(ThermalStatuses.Heating, pointSet.State.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1).Status);
        var stablePointState = await WaitForStateAsync(client, state => state.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1).Status == ThermalStatuses.Stable);
        var stablePoint = stablePointState.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1);
        Assert.Equal(420, stablePoint.CurrentTemperatureDeciC);
        var pointStopped = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/points/A/1", new
        {
            commandId = "cmd-thermal-point-a1-stop",
            targetTemperatureDeciC = 420,
            isEnabled = false
        });
        Assert.Equal(ThermalStatuses.Returning, pointStopped.State.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1).Status);
        var pointOff = await WaitForStateAsync(client, state => state.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1).Status == ThermalStatuses.Off);
        Assert.Equal(250, pointOff.Points.Single(x => x.DrawerCode == "A" && x.SlotNo == 1).CurrentTemperatureDeciC);

        var boardSet = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/boards/B", new
        {
            commandId = "cmd-thermal-board-b",
            targetTemperatureDeciC = 350,
            isEnabled = true
        });
        Assert.All(boardSet.State.Points.Where(x => x.DrawerCode == "B"), x => Assert.Equal(ThermalStatuses.Heating, x.Status));
        var stableBoard = await WaitForStateAsync(client, state => state.Points.Where(x => x.DrawerCode == "B").All(x => x.Status == ThermalStatuses.Stable));
        Assert.All(stableBoard.Points.Where(x => x.DrawerCode == "B"), x => Assert.Equal(350, x.CurrentTemperatureDeciC));

        var coolingSet = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/cooling", new
        {
            commandId = "cmd-cooling-set-50",
            targetTemperatureDeciC = 50,
            isEnabled = true
        });
        Assert.Equal(ThermalStatuses.Cooling, coolingSet.State.Cooling.Status);
        var stableCooling = await WaitForStateAsync(client, state => state.Cooling.Status == ThermalStatuses.Stable);
        Assert.Equal(50, stableCooling.Cooling.CurrentTemperatureDeciC);
        var coolingStopped = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/cooling", new
        {
            commandId = "cmd-cooling-stop",
            targetTemperatureDeciC = 50,
            isEnabled = false
        });
        Assert.Equal(ThermalStatuses.Returning, coolingStopped.State.Cooling.Status);
        var coolingOff = await WaitForStateAsync(client, state => state.Cooling.Status == ThermalStatuses.Off);
        Assert.Equal(250, coolingOff.Cooling.CurrentTemperatureDeciC);

        var telemetry = await client.GetFromJsonAsync<List<TemperatureTelemetryResponse>>("/api/thermal/telemetry?take=1000");
        Assert.Contains(telemetry!, x => x.SourceType == ThermalTelemetrySourceTypes.Point && x.DrawerCode == "A" && x.PointNo == 0 && x.Status == ThermalStatuses.Stable);
        Assert.Contains(telemetry!, x => x.SourceType == ThermalTelemetrySourceTypes.Cooling && x.Status == ThermalStatuses.Stable);
        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.TemperatureChanged && x.EntityType == "ThermalPointState");
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.CoolingChanged);
    }

    [Fact]
    public async Task Faults_block_start_then_clear_recover_and_survive_restart()
    {
        var context = CreateFactory();
        string runId;
        string failedInitializationRunId;
        await using (var factory = context.Factory)
        {
            using var client = factory.CreateClient();
            await LoginAsync(client);
            var initialization = await PostAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new { commandId = "cmd-thermal-init" });
            Assert.True(initialization.Ok);

            var fault = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/faults", new
            {
                commandId = "cmd-thermal-overtemp",
                targetType = "Point",
                drawerCode = "C",
                slotNo = 3,
                faultType = ThermalFaultTypes.OverTemperature,
                reason = "thermal startup guard test"
            });
            var faultedPoint = fault.State.Points.Single(x => x.DrawerCode == "C" && x.SlotNo == 3);
            Assert.Equal(ThermalStatuses.Faulted, faultedPoint.Status);
            Assert.Equal(ThermalFaultTypes.OverTemperature, faultedPoint.FaultCode);
            var preflight = await client.GetFromJsonAsync<PreflightValidationReportResponse>("/api/run/preflight");
            Assert.Contains(preflight!.Issues, x => x.Code == "thermal_point_not_ready");
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
                var coordinateVersion = await dbContext.CoordinateProfileVersions.SingleAsync(x => x.IsActive);
                var run = new MachineRun
                {
                    RunCode = $"RUN-THERMAL-{Guid.NewGuid():N}"[..30],
                    Status = RuntimeLedgerStatus.Created,
                    CoordinateProfileVersionId = coordinateVersion.Id,
                    CoordinateSnapshotJson = $$"""{"coordinateProfileVersionId":"{{coordinateVersion.Id}}"}""",
                    LiquidClassSelectionStatus = LiquidClassSelectionStatus.Frozen,
                    LiquidClassSnapshotJson = "{\"schemaVersion\":1,\"batches\":[]}",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.MachineRuns.Add(run);
                await dbContext.SaveChangesAsync();
                runId = run.Id;
            }

            var blocked = await client.PostAsJsonAsync($"/api/runs/{runId}/start", new { commandId = "cmd-thermal-start-blocked" });
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("thermal", (await blocked.Content.ReadAsStringAsync()).ToLowerInvariant());
            var failedInitialization = await PostAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new { commandId = "cmd-thermal-init-faulted" });
            Assert.False(failedInitialization.Ok);
            Assert.Contains(failedInitialization.Checks, x => x.ModuleCode == "temperature" && x.Status == DeviceInitializationCheckStatus.Failed);
            failedInitializationRunId = failedInitialization.RunId!;
        }

        await using var restarted = CreateFactory(context.DatabasePath).Factory;
        using var restartedClient = restarted.CreateClient();
        await LoginAsync(restartedClient);
        var persisted = await restartedClient.GetFromJsonAsync<ThermalStateResponse>("/api/thermal/state");
        var persistedPoint = persisted!.Points.Single(x => x.DrawerCode == "C" && x.SlotNo == 3);
        Assert.Equal(ThermalFaultTypes.OverTemperature, persistedPoint.FaultCode);

        var cleared = await PostAsync<ThermalMutationResponse>(restartedClient, "/api/thermal/faults/clear", new
        {
            commandId = "cmd-thermal-overtemp-clear",
            targetType = "Point",
            drawerCode = "C",
            slotNo = 3,
            reason = "sensor inspected and reset"
        });
        Assert.Null(cleared.State.Points.Single(x => x.DrawerCode == "C" && x.SlotNo == 3).FaultCode);
        var recovered = await WaitForStateAsync(restartedClient, state => state.Points.Single(x => x.DrawerCode == "C" && x.SlotNo == 3).Status == ThermalStatuses.Off);
        Assert.True(recovered.Ready);
        var retriedInitialization = await PostAsync<DeviceInitializationResponse>(restartedClient, $"/api/device-initialization/{failedInitializationRunId}/retry", new
        {
            commandId = "cmd-thermal-init-retry",
            reason = "thermal fault cleared and stable"
        });
        Assert.True(retriedInitialization.Ok);

        await using var verifyScope = restarted.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.Code.Contains(ThermalFaultTypes.OverTemperature) && x.Status == "Cleared"));
    }

    [Fact]
    public async Task All_supported_fault_types_produce_faulted_or_unknown_and_can_recover()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var index = 0;
        foreach (var faultType in ThermalFaultTypes.All.OrderBy(x => x))
        {
            index++;
            var configured = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/faults", new
            {
                commandId = $"cmd-thermal-fault-{index}",
                targetType = "Cooling",
                faultType,
                reason = $"verify {faultType}"
            });
            Assert.Equal(faultType == ThermalFaultTypes.Unknown ? ThermalStatuses.Unknown : ThermalStatuses.Faulted, configured.State.Cooling.Status);
            if (faultType == ThermalFaultTypes.Disconnected) Assert.False(configured.State.Cooling.IsConnected);

            var cleared = await PostAsync<ThermalMutationResponse>(client, "/api/thermal/faults/clear", new
            {
                commandId = $"cmd-thermal-clear-{index}",
                targetType = "Cooling",
                reason = "recover for next fault"
            });
            Assert.Null(cleared.State.Cooling.FaultCode);
            await WaitForStateAsync(client, state => state.Cooling.Status == ThermalStatuses.Stable);
        }
    }

    [Fact]
    public async Task Real_mode_rejects_thermal_mock_mutations_without_persisting_mock_state()
    {
        var context = CreateFactory(deviceMode: DeviceModes.Real);
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client);
        var response = await client.PostAsJsonAsync("/api/thermal/points/A/1", new
        {
            commandId = "cmd-real-thermal-rejected",
            targetTemperatureDeciC = 420,
            isEnabled = true
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-real-thermal-rejected"));
        Assert.False(await dbContext.ThermalPointStates.AnyAsync());
    }

    private static async Task<ThermalStateResponse> WaitForStateAsync(HttpClient client, Func<ThermalStateResponse, bool> predicate)
    {
        for (var i = 0; i < 40; i++)
        {
            var state = await client.GetFromJsonAsync<ThermalStateResponse>("/api/thermal/state");
            Assert.NotNull(state);
            if (predicate(state!)) return state!;
        }

        Assert.Fail("Thermal state did not stabilize.");
        throw new UnreachableException();
    }

    private static FactoryContext CreateFactory(string? databasePath = null, string deviceMode = DeviceModes.Mock)
    {
        databasePath ??= Path.Combine(Path.GetTempPath(), "stainer-thermal-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
            builder.UseSetting("Device:Mode", deviceMode);
            builder.UseSetting("Device:HardwareAvailable", deviceMode == DeviceModes.Real ? "true" : "false");
            builder.UseSetting("Device:StartupInitialization:Enabled", "false");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                ["Device:Mode"] = deviceMode,
                ["Device:HardwareAvailable"] = deviceMode == DeviceModes.Real ? "true" : "false",
                ["Device:StartupInitialization:Enabled"] = "false"
            }));
        });
        return new FactoryContext(factory, databasePath);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username = "admin", password = "123456", role = "admin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record FactoryContext(WebApplicationFactory<Program> Factory, string DatabasePath);
}
