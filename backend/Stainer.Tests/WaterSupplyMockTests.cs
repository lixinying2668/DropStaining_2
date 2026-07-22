using System.Net;
using System.Net.Http.Json;
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

public sealed class WaterSupplyMockTests
{
    [Fact]
    public async Task Get_state_seeds_four_channels_with_defaults()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var state = await client.GetFromJsonAsync<WaterSupplyStateResponse>("/api/water-supply/state");
        Assert.NotNull(state);
        Assert.Equal(4, state!.Channels.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, state.Channels.Select(x => x.ChannelNo).ToArray());
        Assert.Equal(new[] { "CH1", "CH2", "CH3", "CH4" }, state.Channels.Select(x => x.ChannelCode).ToArray());
        Assert.All(state.Channels, ch =>
        {
            Assert.Equal(250, ch.InletTemperatureDeciC);          // 25.0 ℃
            Assert.Equal(450, ch.OutletTargetTemperatureDeciC);    // 45.0 ℃
            Assert.Equal(250, ch.OutletTemperatureDeciC);          // 25.0 ℃
            Assert.Equal(0, ch.OutletVolumeMl);
            Assert.Equal(250, ch.OutletFlowRateMlPerMinute);
            Assert.False(ch.OutletEnabled);
            Assert.Equal(WaterSupplyStatuses.Idle, ch.Status);
            Assert.True(ch.IsConnected);
        });
    }

    [Fact]
    public async Task Admin_can_set_target_temperature_and_replay_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var body = new { commandId = "cmd-ws-target-1", targetTemperatureDeciC = 420, reason = "raise target" };
        var first = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/target-temperature", body);
        Assert.True(first.Ok);
        Assert.False(first.Replayed);
        Assert.Equal(420, first.State.Channels.Single(x => x.ChannelNo == 1).OutletTargetTemperatureDeciC);

        var replay = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/target-temperature", body);
        Assert.True(replay.Replayed);
    }

    [Fact]
    public async Task Admin_can_set_flow_and_replay_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var body = new { commandId = "cmd-ws-flow-2", flowRateMlPerMinute = 300, reason = "increase flow" };
        var first = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/2/flow", body);
        Assert.True(first.Ok);
        Assert.False(first.Replayed);
        Assert.Equal(300, first.State.Channels.Single(x => x.ChannelNo == 2).OutletFlowRateMlPerMinute);

        var replay = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/2/flow", body);
        Assert.True(replay.Replayed);
    }

    [Fact]
    public async Task Admin_can_open_and_close_outlet()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var opened = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/outlet", new
        {
            commandId = "cmd-ws-outlet-open",
            enabled = true,
            reason = "open outlet"
        });
        var openChannel = opened.State.Channels.Single(x => x.ChannelNo == 1);
        Assert.True(openChannel.OutletEnabled);
        Assert.Equal(WaterSupplyStatuses.Running, openChannel.Status);
        // Mock：打开出水即认为出水温度已达目标温度。
        Assert.Equal(openChannel.OutletTargetTemperatureDeciC, openChannel.OutletTemperatureDeciC);

        var closed = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/outlet", new
        {
            commandId = "cmd-ws-outlet-close",
            enabled = false,
            reason = "close outlet"
        });
        var closeChannel = closed.State.Channels.Single(x => x.ChannelNo == 1);
        Assert.False(closeChannel.OutletEnabled);
        Assert.Equal(WaterSupplyStatuses.Stopped, closeChannel.Status);
    }

    [Fact]
    public async Task Opening_outlet_deducts_system_water_without_fluidics_warmup()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);
        // 真实"空库首次点击供水孔"场景：不预先调用 /api/fluidics/state。
        // SystemWater 容器由 Program.cs 启动 seed（FluidicsControlService.EnsureSeededAsync）保证存在，
        // 故首次打开供水孔即可正确扣减，不会因容器缺失而静默跳过。
        int systemWaterBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            systemWaterBefore = await db.LiquidContainerStates
                .Where(x => x.SourceType == LiquidSourceTypes.SystemWater)
                .Select(x => x.CurrentVolumeUl)
                .SingleAsync();
        }

        var opened = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/3/outlet", new
        {
            commandId = "cmd-ws-outlet-volume",
            enabled = true,
            durationMs = 10000,
            reason = "dispense to measure volume"
        });
        var channel = opened.State.Channels.Single(x => x.ChannelNo == 3);
        Assert.True(channel.OutletVolumeMl > 0);

        int systemWaterAfter;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            systemWaterAfter = await db.LiquidContainerStates
                .Where(x => x.SourceType == LiquidSourceTypes.SystemWater)
                .Select(x => x.CurrentVolumeUl)
                .SingleAsync();
        }
        // 1 ml = 1000 uL：SystemWater 扣减量 == 通道累计出水量 × 1000。
        Assert.Equal(systemWaterBefore - channel.OutletVolumeMl * 1000, systemWaterAfter);
    }

    [Fact]
    public async Task Repeated_fault_does_not_create_duplicate_active_alarms()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        // 同一通道、同一故障类型、不同 commandId 连续配置三次（每次独立请求/独立 DbContext）。
        foreach (var commandId in new[] { "cmd-ws-fault-dup-1", "cmd-ws-fault-dup-2", "cmd-ws-fault-dup-3" })
        {
            await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/faults", new
            {
                commandId,
                channelNo = 1,
                faultType = WaterSupplyFaultTypes.SensorFailure,
                reason = "repeat fault drill"
            });
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var active = await dbContext.Alarms
            .Where(x => x.Status == "Active" && x.Code == "water_supply_CH1_SensorFailure")
            .ToListAsync();
        Assert.Single(active);
    }

    [Fact]
    public async Task Operator_post_is_forbidden()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, role: "operator");

        var response = await client.PostAsJsonAsync("/api/water-supply/channels/1/target-temperature", new
        {
            commandId = "cmd-ws-operator",
            targetTemperatureDeciC = 420,
            reason = "operator attempt"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_mode_post_is_fail_closed_and_persists_no_receipt()
    {
        await using var factory = CreateFactory(deviceMode: DeviceModes.Real);
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/water-supply/channels/1/outlet", new
        {
            commandId = "cmd-ws-real",
            enabled = true,
            reason = "real mode attempt"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("water_supply_mock_not_available", await response.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-ws-real"));
    }

    [Fact]
    public async Task Fault_blocks_outlet_then_clear_recovers()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        var faulted = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/faults", new
        {
            commandId = "cmd-ws-fault",
            channelNo = 2,
            faultType = WaterSupplyFaultTypes.Failure,
            reason = "sensor fault drill"
        });
        Assert.Equal(WaterSupplyStatuses.Faulted, faulted.State.Channels.Single(x => x.ChannelNo == 2).Status);

        var blocked = await client.PostAsJsonAsync("/api/water-supply/channels/2/outlet", new
        {
            commandId = "cmd-ws-outlet-blocked",
            enabled = true,
            reason = "should be blocked by fault"
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/faults/clear", new
        {
            commandId = "cmd-ws-fault-clear",
            channelNo = 2,
            reason = "fault cleared"
        });

        var opened = await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/2/outlet", new
        {
            commandId = "cmd-ws-outlet-after-clear",
            enabled = true,
            reason = "open after recovery"
        });
        Assert.True(opened.State.Channels.Single(x => x.ChannelNo == 2).OutletEnabled);
    }

    [Fact]
    public async Task Disconnected_status_blocks_outlet()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/faults", new
        {
            commandId = "cmd-ws-disconnect",
            channelNo = 4,
            faultType = WaterSupplyFaultTypes.Disconnected,
            reason = "disconnect drill"
        });

        var blocked = await client.PostAsJsonAsync("/api/water-supply/channels/4/outlet", new
        {
            commandId = "cmd-ws-outlet-disconnected",
            enabled = true,
            reason = "should be blocked by disconnect"
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Audit_log_and_command_receipt_are_written()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/flow", new
        {
            commandId = "cmd-ws-audit",
            flowRateMlPerMinute = 180,
            reason = "audit trail"
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "water_supply.flow.set"));
        Assert.True(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-ws-audit"));
        Assert.True(await dbContext.WaterSupplyTelemetry.AnyAsync(x => x.ChannelNo == 1));
    }

    [Fact]
    public async Task Mock_reset_restores_water_supply_defaults()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client);

        await PostAsync<WaterSupplyMutationResponse>(client, "/api/water-supply/channels/1/outlet", new
        {
            commandId = "cmd-ws-before-reset",
            enabled = true,
            reason = "dirty state before reset"
        });
        var beforeReset = await client.GetFromJsonAsync<WaterSupplyStateResponse>("/api/water-supply/state");
        Assert.NotEqual(0, beforeReset!.Channels.Single(x => x.ChannelNo == 1).OutletVolumeMl);

        await PostAsync<MockRuntimeResetResponse>(client, "/api/mock-runtime/reset", new { commandId = "cmd-ws-reset" });

        var afterReset = await client.GetFromJsonAsync<WaterSupplyStateResponse>("/api/water-supply/state");
        var channel = afterReset!.Channels.Single(x => x.ChannelNo == 1);
        Assert.Equal(0, channel.OutletVolumeMl);
        Assert.False(channel.OutletEnabled);
        Assert.Equal(WaterSupplyStatuses.Idle, channel.Status);
    }

    private static WebApplicationFactory<Program> CreateFactory(string deviceMode = DeviceModes.Mock)
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-water-supply-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
    }

    private static async Task LoginAsync(HttpClient client, string role = "admin")
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username = role == "admin" ? "admin" : "operator", password = "123456", role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
