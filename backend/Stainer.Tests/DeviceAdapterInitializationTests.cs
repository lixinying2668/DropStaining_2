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

public sealed class DeviceAdapterInitializationTests
{
    [Fact]
    public async Task Mock_scanners_share_device_state_and_fault_plans()
    {
        var stateStore = new MockDeviceStateStore();
        var adapter = new MockDeviceAdapter(stateStore);

        var sample = await adapter.ScanSampleAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-unit-sample", null, "test", nameof(DeviceAdapterInitializationTests)),
            DeviceModules.SampleScanner,
            "scan",
            new Dictionary<string, object?> { ["rawCode"] = "001" }));
        Assert.True(sample.Ok);
        Assert.Equal("001", sample.Data["rawCode"]);

        var reagent = await adapter.ScanReagentAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-unit-reagent", null, "test", nameof(DeviceAdapterInitializationTests)),
            DeviceModules.ReagentScanner,
            "scan",
            new Dictionary<string, object?> { ["rawBarcode"] = "HEM05020270101001" }));
        Assert.True(reagent.Ok);
        Assert.Equal("HEM05020270101001", reagent.Data["rawBarcode"]);

        await adapter.ConfigureFaultAsync(new DeviceFaultCommand(
            DeviceModules.SampleScanner,
            DeviceFaultTypes.TimeoutNextCommand,
            "sample_timeout",
            "Sample timeout.",
            "unit test",
            "cmd-unit-sample-timeout-fault",
            null,
            "test"));
        var timedOut = await adapter.ScanSampleAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-unit-sample-timeout", null, "test", nameof(DeviceAdapterInitializationTests)),
            DeviceModules.SampleScanner,
            "scan",
            new Dictionary<string, object?>()));
        Assert.False(timedOut.Ok);
        Assert.Equal(DeviceCommandStatuses.TimedOut, timedOut.Status);

        await adapter.ConfigureFaultAsync(new DeviceFaultCommand(
            DeviceModules.ReagentScanner,
            DeviceFaultTypes.Disconnect,
            "reagent_disconnected",
            "Reagent scanner disconnected.",
            "unit test",
            "cmd-unit-reagent-disconnect-fault",
            null,
            "test"));
        var disconnected = await adapter.ScanReagentAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-unit-reagent-disconnect", null, "test", nameof(DeviceAdapterInitializationTests)),
            DeviceModules.ReagentScanner,
            "scan",
            new Dictionary<string, object?>()));
        Assert.False(disconnected.Ok);
        var snapshot = await adapter.GetStatusAsync();
        Assert.Equal(
            DeviceConnectionStatuses.Disconnected,
            snapshot.Modules.Single(x => x.ModuleCode == DeviceModules.ReagentScanner).ConnectionStatus);
        Assert.Contains(snapshot.FaultPlans, x => x.ModuleCode == DeviceModules.ReagentScanner && x.Active);
    }

    [Fact]
    public async Task Dependency_injection_selects_mock_or_safe_unavailable_real_adapter()
    {
        var mockContext = CreateFactory();
        await using (var mockFactory = mockContext.Factory)
        await using (var scope = mockFactory.Services.CreateAsyncScope())
        {
            var adapter = scope.ServiceProvider.GetRequiredService<IDeviceAdapter>();
            Assert.IsType<MockDeviceAdapter>(adapter);
            Assert.Equal(DeviceModes.Mock, adapter.Mode);
        }

        var realContext = CreateFactory(deviceMode: DeviceModes.Real);
        await using (var realFactory = realContext.Factory)
        await using (var scope = realFactory.Services.CreateAsyncScope())
        {
            var adapter = scope.ServiceProvider.GetRequiredService<IDeviceAdapter>();
            Assert.IsType<UnavailableRealDeviceAdapter>(adapter);
            var result = await adapter.MoveRobotAsync(Request(DeviceModules.RobotArm, "home"));
            Assert.False(result.Ok);
            Assert.Equal(DeviceCommandStatuses.NotSupported, result.Status);
            Assert.Equal("real_adapter_not_implemented", result.ErrorCode);
            Assert.Contains("No hardware command was sent", result.Message);
        }
    }

    [Fact]
    public async Task Initialization_is_persisted_idempotent_audited_signaled_and_concurrently_readable()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var before = await client.GetFromJsonAsync<DeviceInitializationResponse>("/api/device-initialization");
        Assert.NotNull(before);
        Assert.Equal("NotStarted", before!.Status);

        var request = new { commandId = "cmd-device-init-success" };
        var initialized = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", request);
        Assert.True(initialized.Ok, initialized.Message);
        Assert.False(initialized.Replayed);
        Assert.Equal(DeviceInitializationStatus.Ready, initialized.Status);
        Assert.Equal(12, initialized.Checks.Count);
        Assert.All(initialized.Checks, check => Assert.Equal(DeviceInitializationCheckStatus.Succeeded, check.Status));

        var replayed = await PostJsonAsync<DeviceInitializationResponse>(client, "/api/device-initialization", request);
        Assert.True(replayed.Replayed);
        Assert.Equal(initialized.RunId, replayed.RunId);

        var adapter = factory.Services.GetRequiredService<IDeviceAdapter>();
        var beforeConcurrentCommands = await adapter.GetStatusAsync();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            adapter.ExecuteWorkflowActionAsync(new DeviceOperationRequest(
                new DeviceCommandContext($"cmd-concurrent-{index}", "corr-concurrent", "test", nameof(DeviceAdapterInitializationTests)),
                DeviceModules.Workflow,
                "concurrency-probe",
                new Dictionary<string, object?> { ["index"] = index }))));

        var snapshots = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            client.GetFromJsonAsync<DeviceStatusSnapshot>("/api/device/state")));
        Assert.All(snapshots, snapshot => Assert.True(snapshot!.Ready));
        Assert.All(snapshots, snapshot => Assert.Equal(snapshots[0]!.Version, snapshot!.Version));
        Assert.True(snapshots[0]!.Version >= beforeConcurrentCommands.Version + 40);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await dbContext.DeviceInitializationRuns.CountAsync(x => x.CommandId == request.commandId));
        Assert.Equal(12, await dbContext.DeviceInitializationChecks.CountAsync(x => x.DeviceInitializationRunId == initialized.RunId));
        Assert.Equal(1, await dbContext.CommandReceipts.CountAsync(x => x.CommandId == request.commandId));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "device.initialization.completed" && x.EntityId == initialized.RunId));

        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.DeviceInitializationChanged && x.EntityId == initialized.RunId);
    }

    [Fact]
    public async Task Controlled_fault_causes_timeout_alarm_and_retry_can_restore_ready_state()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");

        var forbidden = await operatorClient.PostAsJsonAsync("/api/device/mock-faults", new
        {
            commandId = "cmd-device-fault-forbidden",
            moduleCode = DeviceModules.Controller,
            faultType = DeviceFaultTypes.TimeoutNextCommand,
            reason = "operator must not inject faults"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");
        var faultRequest = new
        {
            commandId = "cmd-device-fault-timeout",
            moduleCode = DeviceModules.Controller,
            faultType = DeviceFaultTypes.TimeoutNextCommand,
            reason = "initialization timeout test",
            errorCode = "controller_timeout",
            message = "Injected controller timeout."
        };
        var configured = await PostJsonAsync<DeviceFaultMutationResponse>(adminClient, "/api/device/mock-faults", faultRequest);
        Assert.True(configured.Ok);
        Assert.Contains(configured.State.FaultPlans, x => x.Active && x.CommandId == faultRequest.commandId);
        var replayedFault = await PostJsonAsync<DeviceFaultMutationResponse>(adminClient, "/api/device/mock-faults", faultRequest);
        Assert.True(replayedFault.Replayed);

        var failed = await PostJsonAsync<DeviceInitializationResponse>(adminClient, "/api/device-initialization", new
        {
            commandId = "cmd-device-init-failed"
        });
        Assert.False(failed.Ok);
        Assert.Equal(DeviceInitializationStatus.Failed, failed.Status);
        Assert.Contains(failed.Checks, x => x.ModuleCode == DeviceModules.Controller && x.Status == DeviceInitializationCheckStatus.TimedOut);

        var missingReason = await adminClient.PostAsJsonAsync($"/api/device-initialization/{failed.RunId}/retry", new
        {
            commandId = "cmd-device-init-retry-no-reason",
            reason = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        _ = await PostJsonAsync<DeviceFaultMutationResponse>(adminClient, "/api/device/mock-faults/clear", new
        {
            commandId = "cmd-device-fault-clear",
            moduleCode = DeviceModules.Controller,
            reason = "restore controller after timeout test"
        });
        var retried = await PostJsonAsync<DeviceInitializationResponse>(adminClient, $"/api/device-initialization/{failed.RunId}/retry", new
        {
            commandId = "cmd-device-init-retry-success",
            reason = "controller fault was cleared"
        });
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal(2, retried.AttemptNo);
        Assert.Equal(failed.RunId, retried.RetryOfRunId);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.Alarms.AnyAsync(x => x.Code == "device_initialization_failed" && x.Status == "Active"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "device.mock_fault.configured"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "device.mock_fault.cleared"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "device.initialization.failed" && x.EntityId == failed.RunId));

        var publisher = factory.Services.GetRequiredService<InMemoryRuntimeEventPublisher>();
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.DeviceStateChanged);
        Assert.Contains(publisher.Snapshot(), x => x.Type == MachineEventTypes.DeviceInitializationChanged);
    }

    [Fact]
    public async Task Initialization_requires_authentication_and_latest_result_survives_host_restart()
    {
        var context = CreateFactory();
        string runId;
        await using (var firstFactory = context.Factory)
        {
            using var anonymous = firstFactory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/device-initialization")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/device-initialization", new { commandId = "cmd-anonymous-init" })).StatusCode);

            using var operatorClient = firstFactory.CreateClient();
            await LoginAsync(operatorClient, "operator", "operator");
            var initialized = await PostJsonAsync<DeviceInitializationResponse>(operatorClient, "/api/device-initialization", new
            {
                commandId = "cmd-device-init-before-restart"
            });
            Assert.True(initialized.Ok, initialized.Message);
            runId = initialized.RunId!;
        }

        var restartedContext = CreateFactory(context.DatabasePath);
        await using var restartedFactory = restartedContext.Factory;
        using var restartedClient = restartedFactory.CreateClient();
        await LoginAsync(restartedClient, "operator", "operator");
        var latest = await restartedClient.GetFromJsonAsync<DeviceInitializationResponse>("/api/device-initialization");
        Assert.NotNull(latest);
        Assert.True(latest!.Ok);
        Assert.Equal(runId, latest.RunId);
        Assert.Equal(DeviceInitializationStatus.Ready, latest.Status);
        Assert.Equal(12, latest.Checks.Count);
    }

    private static DeviceOperationRequest Request(string moduleCode, string action)
    {
        return new DeviceOperationRequest(
            new DeviceCommandContext("cmd-real-adapter-test", "corr-real-adapter-test", "test", nameof(DeviceAdapterInitializationTests)),
            moduleCode,
            action,
            new Dictionary<string, object?>());
    }

    private static FactoryContext CreateFactory(string? databasePath = null, string deviceMode = DeviceModes.Mock)
    {
        var root = databasePath is null
            ? Path.Combine(Path.GetTempPath(), "stainer-device-adapter-tests", Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(databasePath)!;
        databasePath ??= Path.Combine(root, "stainer.db");
        var leasePath = Path.Combine(root, $"machine-executor-{Guid.NewGuid():N}.lock");
        var logDirectory = Path.Combine(root, "logs");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = leasePath,
            ["Safety:LogDirectory"] = logDirectory,
            ["Device:Mode"] = deviceMode,
            ["Device:RealHealthCheckComplete"] = "false"
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
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            });
        return new FactoryContext(factory, databasePath);
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

    private sealed record FactoryContext(WebApplicationFactory<Program> Factory, string DatabasePath);
}
