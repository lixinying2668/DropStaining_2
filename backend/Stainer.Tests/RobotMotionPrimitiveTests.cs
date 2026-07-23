using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;
using Xunit;

namespace Stainer.Tests;

public sealed class RobotMotionPrimitiveTests
{
    [Fact]
    public async Task MoveXY_uses_absolute_coordinates_not_offsets()
    {
        var primitives = new MockRobotMotionPrimitives();

        await primitives.MoveXYAsync(100, 200);
        await primitives.MoveXYAsync(10, 20);

        var position = await primitives.GetPositionAsync();
        Assert.Equal(10, position.XUm);
        Assert.Equal(20, position.YUm);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveXY(100, 200),
            RobotPrimitiveCall.MoveXY(10, 20),
            RobotPrimitiveCall.GetPosition()
        ], primitives.Calls);
    }

    [Fact]
    public async Task MoveZ_on_Z1_does_not_change_Z2()
    {
        var primitives = new MockRobotMotionPrimitives();

        await primitives.MoveZAsync(RobotZAxis.Z2, 2_000);
        await primitives.MoveZAsync(RobotZAxis.Z1, 1_000);

        var position = await primitives.GetPositionAsync();
        Assert.Equal(1_000, position.Z1Um);
        Assert.Equal(2_000, position.Z2Um);
    }

    [Fact]
    public async Task MoveZ_on_Z2_does_not_change_Z1()
    {
        var primitives = new MockRobotMotionPrimitives();

        await primitives.MoveZAsync(RobotZAxis.Z1, 1_000);
        await primitives.MoveZAsync(RobotZAxis.Z2, 2_000);

        var position = await primitives.GetPositionAsync();
        Assert.Equal(1_000, position.Z1Um);
        Assert.Equal(2_000, position.Z2Um);
    }

    [Fact]
    public async Task GetPosition_returns_current_X_Y_Z1_Z2()
    {
        var primitives = new MockRobotMotionPrimitives();

        await primitives.MoveXYAsync(123, 456);
        await primitives.MoveZAsync(RobotZAxis.Z1, 7_890);
        await primitives.MoveZAsync(RobotZAxis.Z2, 8_901);

        var position = await primitives.GetPositionAsync();
        Assert.Equal(new RobotArmPositionUm(123, 456, 7_890, 8_901), position);
    }

    [Fact]
    public async Task Cancelled_token_does_not_update_state_or_record_successful_calls()
    {
        var primitives = new MockRobotMotionPrimitives();
        await primitives.MoveXYAsync(10, 20);
        await primitives.MoveZAsync(RobotZAxis.Z1, 100);
        await primitives.MoveZAsync(RobotZAxis.Z2, 200);
        primitives.Calls.Clear();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => primitives.MoveXYAsync(999, 999, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => primitives.MoveZAsync(RobotZAxis.Z1, 999, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => primitives.AspirateAsync(50, cancelled.Token));
        Assert.Empty(primitives.Calls);

        var position = await primitives.GetPositionAsync();
        Assert.Equal(new RobotArmPositionUm(10, 20, 100, 200), position);
    }

    [Fact]
    public async Task Unavailable_primitives_fail_closed_instead_of_mocking_real_motion()
    {
        var primitives = new UnavailableRobotMotionPrimitives();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => primitives.MoveZAsync(RobotZAxis.Z1, 1_000));

        Assert.Equal("robot_motion_primitives_unavailable", ex.Code);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task Real_mode_di_resolves_fail_closed_primitives_instead_of_mock()
    {
        var root = Path.Combine(TestPaths.TempRoot, "stainer-robot-motion-di-tests", Guid.NewGuid().ToString("N"));
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
        var primitives = scope.ServiceProvider.GetRequiredService<IRobotMotionPrimitives>();

        Assert.IsType<UnavailableRobotMotionPrimitives>(primitives);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => primitives.MoveXYAsync(1, 2));
        Assert.Equal("robot_motion_primitives_unavailable", ex.Code);
    }
}
