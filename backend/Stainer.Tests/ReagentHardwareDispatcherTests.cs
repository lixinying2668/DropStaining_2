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
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

// 验证试剂区硬件通信旁挂框架：试剂扫码确认（带 ScanSessionId）→ ReagentChanged 事件 →
// 装饰器镜像 → ReagentHardwareDispatcher 消费 → ReagentHardwareSink 四步范式 → DeviceCommunicationRecord 落库。
// 注意：必须走"带 ScanSessionId 的 scan-confirm"（ConfirmActiveSessionPositionAsync）才会发布 ReagentChanged；
// 无 session 的 qr/report 只发布 QrScanCompleted（不被本框架镜像）。
// 范式参考 DeviceAdapterInitializationTests（CreateFactory / LoginAsync / PostJsonAsync）。
public sealed class ReagentHardwareDispatcherTests
{
    [Fact]
    public async Task Reagent_scan_confirm_in_mock_mode_drives_hardware_pipeline_and_persists_record()
    {
        var context = CreateFactory(reagentHardwareEnabled: true);
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var rawBarcode = "HEM05020270101001";

        // 开启 scan session → 拿 scanSessionId。
        var started = await PostJsonAsync<ReagentScanSessionMutationResponse>(
            client, "/api/reagents/scan-sessions/start",
            new { commandId = "cmd-rh-session-start" });
        Assert.True(started.Ok, started.Message);
        var scanSessionId = started.Session.ScanSessionId;
        Assert.False(string.IsNullOrWhiteSpace(scanSessionId));

        // 带 session 的 scan-confirm → ConfirmActiveSessionPositionAsync → 发布 ReagentChanged（+ ReagentBottleChanged）。
        var confirmed = await PostJsonAsync<ReagentScanConfirmationResponse>(
            client, "/api/reagents/scan-confirm",
            new
            {
                commandId = "cmd-rh-confirm",
                scanSessionId,
                items = new[] { new { position = "R1", scanResult = "VALID", rawBarcode, locatorCode = "R1" } }
            });
        Assert.True(confirmed.Ok, confirmed.Message);

        // 等待 dispatcher：初始延迟 150ms + 提交确认 + sink 四步范式落库。
        var records = await WaitForReagentHardwareRecordsAsync(factory, TimeSpan.FromSeconds(8));

        if (records.Count == 0)
        {
            var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
            var snapshot = publisher.Snapshot();
            var hasReagentChanged = snapshot.Any(e => e.Type == MachineEventTypes.ReagentChanged);
            await using var diagScope = factory.Services.CreateAsyncScope();
            var diagDb = diagScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var failures = await diagDb.AuditLogs.AsNoTracking()
                .Where(x => x.Action == "reagent.hardware.dispatch_failed").ToListAsync();
            Assert.Fail($"No reagent hardware records. SnapshotHasReagentChanged={hasReagentChanged}, SnapshotCount={snapshot.Count}, SnapshotTypes=[{string.Join(",", snapshot.Select(e => e.Type).Distinct())}], DispatchFailures={failures.Count}, FailureMessages={string.Join(" | ", failures.Select(f => f.Message))}");
        }
        Assert.Contains(records, r => r.Action == "reagent.stateChanged");
        Assert.All(records, r =>
        {
            Assert.Equal(DeviceModules.ReagentScanner, r.ModuleCode);
            Assert.Equal(nameof(MockDeviceOperations), r.AdapterName);
            Assert.True(r.Ok);
            Assert.Equal(DeviceCommunicationPersistenceStatus.Complete, r.PersistenceStatus);
            Assert.StartsWith("reagent-hardware:", r.CommandId);
        });
    }

    [Fact]
    public async Task Reagent_hardware_remains_silent_when_switch_disabled()
    {
        var context = CreateFactory(reagentHardwareEnabled: false);
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var rawBarcode = "HEM05020270101002";
        var started = await PostJsonAsync<ReagentScanSessionMutationResponse>(
            client, "/api/reagents/scan-sessions/start",
            new { commandId = "cmd-rh-off-session-start" });
        await PostJsonAsync<ReagentScanConfirmationResponse>(
            client, "/api/reagents/scan-confirm",
            new
            {
                commandId = "cmd-rh-off-confirm",
                scanSessionId = started.Session.ScanSessionId,
                items = new[] { new { position = "R2", scanResult = "VALID", rawBarcode, locatorCode = "R2" } }
            });

        // 给 dispatcher 足够窗口，证明开关关闭时确实不产生任何 reagent.* 记录。
        await Task.Delay(1500);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var records = await dbContext.DeviceCommunicationRecords
            .Where(x => x.ModuleCode == DeviceModules.ReagentScanner && x.Action.StartsWith("reagent."))
            .ToListAsync();
        Assert.Empty(records);
    }

    private static async Task<List<DeviceCommunicationRecord>> WaitForReagentHardwareRecordsAsync(
        WebApplicationFactory<Program> factory, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var found = await dbContext.DeviceCommunicationRecords.AsNoTracking()
                .AnyAsync(x => x.ModuleCode == DeviceModules.ReagentScanner && x.Action.StartsWith("reagent."));
            if (found) break;
            await Task.Delay(100);
        }

        // 第一条到达后再给 grace，让同源的 ReagentBottleChanged 也落库。
        await Task.Delay(800);
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        return await finalDb.DeviceCommunicationRecords.AsNoTracking()
            .Where(x => x.ModuleCode == DeviceModules.ReagentScanner && x.Action.StartsWith("reagent."))
            .ToListAsync();
    }

    private static FactoryContext CreateFactory(
        string? databasePath = null,
        string deviceMode = DeviceModes.Mock,
        bool reagentHardwareEnabled = false)
    {
        var root = Path.Combine(TestPaths.TempRoot, "stainer-reagent-hardware-tests", Guid.NewGuid().ToString("N"));
        databasePath ??= Path.Combine(root, "stainer.db");
        var leasePath = Path.Combine(root, $"machine-executor-{Guid.NewGuid():N}.lock");
        var logDirectory = Path.Combine(root, "logs");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = leasePath,
            ["Safety:LogDirectory"] = logDirectory,
            ["Device:Mode"] = deviceMode,
            ["Device:RealHealthCheckComplete"] = "false",
            ["Device:HardwareAvailable"] = "false",
            ["Device:StartupInitialization:Enabled"] = "false",
            ["Device:ReagentHardware:Enabled"] = reagentHardwareEnabled ? "true" : "false"
        };
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.UseSetting("MachineExecutor:LeasePath", leasePath);
                builder.UseSetting("Safety:LogDirectory", logDirectory);
                builder.UseSetting("Device:Mode", deviceMode);
                builder.UseSetting("Device:RealHealthCheckComplete", "false");
                builder.UseSetting("Device:HardwareAvailable", "false");
                builder.UseSetting("Device:StartupInitialization:Enabled", "false");
                builder.UseSetting("Device:ReagentHardware:Enabled", reagentHardwareEnabled ? "true" : "false");
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

    private sealed record FactoryContext(WebApplicationFactory<Program> Factory, string DatabasePath);
}
