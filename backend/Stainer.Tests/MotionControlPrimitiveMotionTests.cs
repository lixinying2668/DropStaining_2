using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;
using Xunit;

namespace Stainer.Tests;

public sealed class MotionControlPrimitiveMotionTests
{
    [Fact]
    public async Task MoveToXYAtSafeHeight_moves_Z1_then_Z2_then_absolute_XY()
    {
        var primitives = new RecordingRobotMotionPrimitives();
        var service = BuildService(primitives);

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 12_345, 67_890, 20_000));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000),
            RobotPrimitiveCall.MoveXY(12_345, 67_890)
        ], primitives.Calls);
    }

    [Fact]
    public async Task Z1_failure_stops_before_Z2_and_XY()
    {
        var primitives = new RecordingRobotMotionPrimitives { ThrowOnZAxis = RobotZAxis.Z1 };
        var service = BuildService(primitives);

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000));

        Assert.False(result.Ok);
        Assert.Equal("motion_travel_safe_z_failed", result.ErrorCode);
        Assert.Equal([RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000)], primitives.Calls);
    }

    [Fact]
    public async Task Z2_failure_stops_before_XY()
    {
        var primitives = new RecordingRobotMotionPrimitives { ThrowOnZAxis = RobotZAxis.Z2 };
        var service = BuildService(primitives);

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000));

        Assert.False(result.Ok);
        Assert.Equal("motion_travel_safe_z_failed", result.ErrorCode);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000)
        ], primitives.Calls);
    }

    [Fact]
    public async Task XY_failure_has_no_follow_up_Z_or_process_actions_and_does_not_report_success()
    {
        var primitives = new RecordingRobotMotionPrimitives { ThrowOnXY = true };
        var service = BuildService(primitives);

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000));

        Assert.False(result.Ok);
        Assert.Equal("motion_move_xy_failed", result.ErrorCode);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000),
            RobotPrimitiveCall.MoveXY(100, 200)
        ], primitives.Calls);
        Assert.DoesNotContain(primitives.Calls, x => x.Kind is "Aspirate" or "Dispense" or "WashOuter");
    }

    [Fact]
    public async Task Success_leaves_XY_at_absolute_target_and_both_Z_axes_at_safe_height()
    {
        var primitives = new RecordingRobotMotionPrimitives();
        var service = BuildService(primitives);

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 123, 456, 20_000));
        var callsBeforeReadback = primitives.Calls.ToArray();
        var position = await primitives.GetPositionAsync();

        Assert.True(result.Ok, result.Message);
        Assert.Equal(new RobotArmPositionUm(123, 456, 20_000, 20_000), position);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000),
            RobotPrimitiveCall.MoveXY(123, 456)
        ], callsBeforeReadback);
    }

    [Fact]
    public async Task Cancelled_token_stops_without_continuing_motion()
    {
        var primitives = new RecordingRobotMotionPrimitives();
        var service = BuildService(primitives);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000), cts.Token));

        Assert.Empty(primitives.Calls);
    }

    [Fact]
    public async Task Token_cancelled_after_Z1_stops_before_Z2_and_XY()
    {
        using var cts = new CancellationTokenSource();
        var primitives = new RecordingRobotMotionPrimitives { CancelAfterZ1 = cts };
        var service = BuildService(primitives);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000), cts.Token));

        Assert.Equal([RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000)], primitives.Calls);
    }

    [Fact]
    public async Task Token_cancelled_after_Z2_stops_before_XY()
    {
        using var cts = new CancellationTokenSource();
        var primitives = new RecordingRobotMotionPrimitives { CancelAfterZ2 = cts };
        var service = BuildService(primitives);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000), cts.Token));

        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000)
        ], primitives.Calls);
    }

    [Fact]
    public async Task EnsureTravelSafeZ_only_moves_Z1_then_Z2()
    {
        var primitives = new RecordingRobotMotionPrimitives();
        var service = BuildService(primitives);

        var result = await service.EnsureTravelSafeZAsync(new RobotTravelSafeZRequest(20_000));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 20_000),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z2, 20_000)
        ], primitives.Calls);
        Assert.DoesNotContain(primitives.Calls, x => x.Kind is "MoveXY" or "Aspirate" or "Dispense" or "WashOuter");
    }

    [Fact]
    public async Task Real_mode_motion_service_write_fails_closed_without_mock_fallback()
    {
        var root = Path.Combine(TestPaths.TempRoot, "stainer-motion-real-di-tests", Guid.NewGuid().ToString("N"));
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
        var service = scope.ServiceProvider.GetRequiredService<MotionControlService>();

        var result = await service.MoveToXYAtSafeHeightAsync(new RobotMoveToXYAtSafeHeightRequest("A-01", 100, 200, 20_000));

        Assert.False(result.Ok);
        Assert.Equal("motion_travel_safe_z_failed", result.ErrorCode);
        Assert.Contains("unavailable in Real mode", result.Message);
    }

    private static MotionControlService BuildService(IRobotMotionPrimitives primitives)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        return new MotionControlService(null!, null!, configuration, new TestHostEnvironment(), primitives);
    }

    private sealed class RecordingRobotMotionPrimitives : IRobotMotionPrimitives
    {
        private long _xUm;
        private long _yUm;
        private long _z1Um;
        private long _z2Um;

        public List<RobotPrimitiveCall> Calls { get; } = [];
        public RobotZAxis? ThrowOnZAxis { get; set; }
        public bool ThrowOnXY { get; set; }
        public CancellationTokenSource? CancelAfterZ1 { get; set; }
        public CancellationTokenSource? CancelAfterZ2 { get; set; }

        public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.GetPosition());
            return Task.FromResult(new RobotArmPositionUm(_xUm, _yUm, _z1Um, _z2Um));
        }

        public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.MoveXY(xUm, yUm));
            if (ThrowOnXY)
            {
                throw new InvalidOperationException("xy failed");
            }

            _xUm = xUm;
            _yUm = yUm;
            return Task.CompletedTask;
        }

        public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.MoveZ(axis, zUm));
            if (ThrowOnZAxis == axis)
            {
                throw new InvalidOperationException($"{axis} failed");
            }

            if (axis == RobotZAxis.Z1)
            {
                _z1Um = zUm;
                CancelAfterZ1?.Cancel();
            }
            else
            {
                _z2Um = zUm;
                CancelAfterZ2?.Cancel();
            }

            return Task.CompletedTask;
        }

        public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.Aspirate(volumeUl));
            return Task.CompletedTask;
        }

        public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.Dispense(volumeUl));
            return Task.CompletedTask;
        }

        public Task WashOuterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(RobotPrimitiveCall.WashOuter());
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = nameof(MotionControlPrimitiveMotionTests);
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
