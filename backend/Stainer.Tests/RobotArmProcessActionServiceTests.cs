using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Data;
using Xunit;

namespace Stainer.Tests;

public sealed class RobotArmProcessActionServiceTests
{
    [Fact]
    public async Task MoveToSourceAndTakeLiquid_moves_then_takes_liquid()
    {
        var (service, events, _) = BuildService();

        var result = await service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-take", "Needle1", 100));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["MoveZ:Z1:20000", "MoveZ:Z2:20000", "MoveXY:123:456", "TakeLiquid"], events);
    }

    [Fact]
    public async Task MoveToTargetAndDispenseLiquid_moves_then_dispenses()
    {
        var (service, events, _) = BuildService();

        var result = await service.MoveToTargetAndDispenseLiquidAsync(Move(), new DispenseLiquidRequest("cmd-dispense", "Needle1", 50));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["MoveZ:Z1:20000", "MoveZ:Z2:20000", "MoveXY:123:456", "DispenseLiquid"], events);
    }

    [Fact]
    public async Task MoveToMixAndPrepareMix_moves_then_prepares_mix()
    {
        var (service, events, _) = BuildService();

        var result = await service.MoveToMixAndPrepareMixAsync(Move(), new PrepareMixRequest("cmd-mix", "Needle1", 80));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["MoveZ:Z1:20000", "MoveZ:Z2:20000", "MoveXY:123:456", "PrepareMix"], events);
    }

    [Fact]
    public async Task MoveToInnerWashAndWashInner_moves_then_inner_washes()
    {
        var (service, events, _) = BuildService();

        var result = await service.MoveToInnerWashAndWashInnerAsync(Move(), new WashInnerRequest("cmd-inner", "Needle1", 200, 200));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["MoveZ:Z1:20000", "MoveZ:Z2:20000", "MoveXY:123:456", "WashInner"], events);
    }

    [Fact]
    public async Task MoveToOuterWashAndWashOuter_moves_then_outer_washes()
    {
        var (service, events, _) = BuildService();

        var result = await service.MoveToOuterWashAndWashOuterAsync(Move(), new WashOuterRequest("cmd-outer", "Needle1"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["MoveZ:Z1:20000", "MoveZ:Z2:20000", "MoveXY:123:456", "WashOuter"], events);
    }

    [Fact]
    public async Task Default_process_action_chain_does_not_write_engineering_state_or_ledger()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var motion = new MotionControlService(dbContext, null!, configuration, new TestHostEnvironment(), primitives);
        var atomic = new RobotArmAtomicActionService(primitives, new RobotArmAtomicHeights
        {
            AspirateZUm = 1_000,
            MixZUm = 2_000,
            DispenseZUm = 3_000,
            WashInnerZUm = 4_000,
            WashOuterZUm = 5_000,
            SafeZUm = 90_000
        });
        var service = new RobotArmProcessActionService(motion, atomic, NullLogger<RobotArmProcessActionService>.Instance);

        var result = await service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-process-no-write", "Needle1", 100));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000),
            RobotPrimitiveCall.MoveXY(123, 456),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 1_000),
            RobotPrimitiveCall.Aspirate(100),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 90_000)
        ], primitives.Calls);
        await AssertNoEngineeringWritesAsync(dbContext, "cmd-process-no-write");
    }

    [Fact]
    public async Task Move_failure_skips_every_atomic_action()
    {
        var cases = new Func<IRobotArmProcessActionService, Task<RobotArmProcessActionResult>>[]
        {
            service => service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-take", "Needle1", 100)),
            service => service.MoveToTargetAndDispenseLiquidAsync(Move(), new DispenseLiquidRequest("cmd-dispense", "Needle1", 50)),
            service => service.MoveToMixAndPrepareMixAsync(Move(), new PrepareMixRequest("cmd-mix", "Needle1", 80)),
            service => service.MoveToInnerWashAndWashInnerAsync(Move(), new WashInnerRequest("cmd-inner", "Needle1", 200, 200)),
            service => service.MoveToOuterWashAndWashOuterAsync(Move(), new WashOuterRequest("cmd-outer", "Needle1"))
        };

        foreach (var run in cases)
        {
            var primitives = new RecordingRobotMotionPrimitives([], throwOnZ1: true);
            var atomic = new RecordingAtomicActionService([]);
            var service = BuildService(primitives, atomic);

            var result = await run(service);

            Assert.False(result.Success);
            Assert.Equal(RobotArmProcessFailureStage.Move, result.FailureStage);
            Assert.Equal(0, atomic.CallCount);
        }
    }

    [Fact]
    public async Task Atomic_failure_returns_action_stage_failure()
    {
        var atomic = new RecordingAtomicActionService([], failedAction: "DispenseLiquid");
        var service = BuildService(new RecordingRobotMotionPrimitives([]), atomic);

        var result = await service.MoveToTargetAndDispenseLiquidAsync(Move(), new DispenseLiquidRequest("cmd-dispense", "Needle1", 50));

        Assert.False(result.Success);
        Assert.Equal(RobotArmProcessFailureStage.Action, result.FailureStage);
        Assert.Equal("DispenseLiquid failed", result.ErrorMessage);
        Assert.Equal(1, atomic.CallCount);
    }

    [Fact]
    public async Task Non_cancel_exception_from_atomic_action_is_logged_and_converted_to_action_failure()
    {
        var atomic = new RecordingAtomicActionService([], throwAction: "PrepareMix");
        var service = BuildService(new RecordingRobotMotionPrimitives([]), atomic);

        var result = await service.MoveToMixAndPrepareMixAsync(Move(), new PrepareMixRequest("cmd-mix", "Needle1", 80));

        Assert.False(result.Success);
        Assert.Equal(RobotArmProcessFailureStage.Action, result.FailureStage);
        Assert.Equal("PrepareMix threw", result.ErrorMessage);
    }

    [Fact]
    public async Task Non_cancel_exception_from_movement_is_logged_and_converted_to_move_failure()
    {
        var atomic = new RecordingAtomicActionService([]);
        var service = new RobotArmProcessActionService(null!, atomic, NullLogger<RobotArmProcessActionService>.Instance);

        var result = await service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-take", "Needle1", 100));

        Assert.False(result.Success);
        Assert.Equal(RobotArmProcessFailureStage.Move, result.FailureStage);
        Assert.Equal(0, atomic.CallCount);
    }

    [Fact]
    public async Task OperationCanceledException_from_atomic_action_is_not_converted()
    {
        var atomic = new RecordingAtomicActionService([], cancelAction: "WashInner");
        var service = BuildService(new RecordingRobotMotionPrimitives([]), atomic);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.MoveToInnerWashAndWashInnerAsync(Move(), new WashInnerRequest("cmd-inner", "Needle1", 200, 200)));
    }

    [Fact]
    public async Task OperationCanceledException_from_movement_is_not_converted_and_skips_atomic_action()
    {
        var atomic = new RecordingAtomicActionService([]);
        var service = BuildService(new RecordingRobotMotionPrimitives([]), atomic);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-take", "Needle1", 100), cts.Token));

        Assert.Equal(0, atomic.CallCount);
    }

    [Fact]
    public async Task Real_mode_process_action_fails_closed_without_mock_fallback()
    {
        var root = Path.Combine(TestPaths.TempRoot, "stainer-process-real-di-tests", Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={Path.Combine(root, "stainer.db")}",
            ["MachineExecutor:LeasePath"] = Path.Combine(root, $"machine-executor-{Guid.NewGuid():N}.lock"),
            ["Safety:LogDirectory"] = Path.Combine(root, "logs"),
            ["Device:Mode"] = DeviceModes.Real,
            ["Device:StartupInitialization:Enabled"] = "false"
        };

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var pair in settings)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRobotArmAtomicActionService>());
        var service = scope.ServiceProvider.GetRequiredService<IRobotArmProcessActionService>();

        var result = await service.MoveToSourceAndTakeLiquidAsync(Move(), new TakeLiquidRequest("cmd-take", "Needle1", 100));

        Assert.False(result.Success);
        Assert.Equal(RobotArmProcessFailureStage.Move, result.FailureStage);
        Assert.Contains("unavailable in Real mode", result.ErrorMessage);
    }

    private static RobotMoveToXYAtSafeHeightRequest Move() => new("P1", 123, 456, 20_000);

    private static async Task AssertNoEngineeringWritesAsync(StainerDbContext dbContext, string commandId)
    {
        Assert.False(await dbContext.RobotArmStates.AnyAsync(x => x.CurrentCommandId == commandId || x.DeviceCommandExecutionId == commandId));
        Assert.False(await dbContext.NeedleStates.AnyAsync(x => x.CurrentCommandId == commandId || x.DeviceCommandExecutionId == commandId));
        Assert.False(await dbContext.PipettingOperations.AnyAsync(x => x.DeviceCommandExecutionId == commandId));
        Assert.False(await dbContext.AuditLogs.AnyAsync(x => x.Action.StartsWith("atomic.action.") && x.Message.Contains(commandId)));
    }

    private static async Task<StainerDbContext> CreateMigratedContextAsync()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-process-action-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var connectionString = $"Data Source={databasePath}";
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor())
            .Options;
        var dbContext = new StainerDbContext(options);
        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    private static (IRobotArmProcessActionService Service, List<string> Events, RecordingAtomicActionService Atomic) BuildService()
    {
        var events = new List<string>();
        var atomic = new RecordingAtomicActionService(events);
        return (BuildService(new RecordingRobotMotionPrimitives(events), atomic), events, atomic);
    }

    private static IRobotArmProcessActionService BuildService(
        IRobotMotionPrimitives primitives,
        RecordingAtomicActionService atomic)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var motion = new MotionControlService(null!, null!, configuration, new TestHostEnvironment(), primitives);
        return new RobotArmProcessActionService(motion, atomic, NullLogger<RobotArmProcessActionService>.Instance);
    }

    private sealed class RecordingRobotMotionPrimitives(
        List<string> events,
        bool throwOnZ1 = false) : IRobotMotionPrimitives
    {
        public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RobotArmPositionUm(0, 0, 0, 0));

        public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"MoveXY:{xUm}:{yUm}");
            return Task.CompletedTask;
        }

        public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"MoveZ:{axis}:{zUm}");
            if (throwOnZ1 && axis == RobotZAxis.Z1)
            {
                throw new InvalidOperationException("Z1 failed");
            }

            return Task.CompletedTask;
        }

        public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WashOuterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAtomicActionService(
        List<string> events,
        string? failedAction = null,
        string? throwAction = null,
        string? cancelAction = null) : IRobotArmAtomicActionService
    {
        public int CallCount { get; private set; }

        public Task<RobotArmAtomicActionResult> TakeLiquidAsync(TakeLiquidRequest request, CancellationToken cancellationToken = default) =>
            RunAsync("TakeLiquid");

        public Task<RobotArmAtomicActionResult> PrepareMixAsync(PrepareMixRequest request, CancellationToken cancellationToken = default) =>
            RunAsync("PrepareMix");

        public Task<RobotArmAtomicActionResult> DispenseLiquidAsync(DispenseLiquidRequest request, CancellationToken cancellationToken = default) =>
            RunAsync("DispenseLiquid");

        public Task<RobotArmAtomicActionResult> WashInnerAsync(WashInnerRequest request, CancellationToken cancellationToken = default) =>
            RunAsync("WashInner");

        public Task<RobotArmAtomicActionResult> WashOuterAsync(WashOuterRequest request, CancellationToken cancellationToken = default) =>
            RunAsync("WashOuter");

        private Task<RobotArmAtomicActionResult> RunAsync(string action)
        {
            CallCount++;
            events.Add(action);
            if (cancelAction == action)
            {
                throw new OperationCanceledException($"{action} cancelled");
            }

            if (throwAction == action)
            {
                throw new InvalidOperationException($"{action} threw");
            }

            return Task.FromResult(failedAction == action
                ? RobotArmAtomicActionResult.Failed(action, [], $"{action} failed", "atomic_failed")
                : RobotArmAtomicActionResult.Succeeded(action, [], $"{action} ok"));
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = nameof(RobotArmProcessActionServiceTests);
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
