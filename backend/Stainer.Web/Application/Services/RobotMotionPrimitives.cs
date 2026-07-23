using Microsoft.AspNetCore.Http;

namespace Stainer.Web.Application.Services;

// 机械臂底层动作原语（Ports）。原子动作服务（RobotArmAtomicActionService）只依赖这套接口，
// 不直接耦合 MotionControlService / EngineeringPipettingService 的重 DB 上下文，也不触碰 SOCON Bridge。
// 第一阶段只提供 Mock 实现；后续接真实硬件时新增 SoconRobotMotionPrimitives 并在 DI 中替换注册即可，
// 原子动作编排与单测无需改动。
public interface IRobotMotionPrimitives
{
    Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default);

    Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default);

    /// <summary>移动 Z 轴到绝对高度（单位：微米 µm）。</summary>
    Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default);

    /// <summary>在当前位置吸入指定体积液体（单位：微升 µL）。</summary>
    Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default);

    /// <summary>在当前位置排出指定体积液体（单位：微升 µL）。</summary>
    Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default);

    /// <summary>执行外壁清洗动作（外壁冲洗，不涉及吸 / 排具体体积）。</summary>
    Task WashOuterAsync(CancellationToken cancellationToken = default);
}

public enum RobotZAxis
{
    Z1 = 1,
    Z2 = 2
}

public sealed record RobotArmPositionUm(long XUm, long YUm, long Z1Um, long Z2Um);

// 一条原语调用的结构化记录，供 Mock 实现记录调用顺序、供单元测试断言动作顺序。
public sealed record RobotPrimitiveCall(
    string Kind,
    RobotZAxis? Axis,
    long? XUm,
    long? YUm,
    long? ZUm,
    int? VolumeUl)
{
    public static RobotPrimitiveCall GetPosition() => new("GetPosition", null, null, null, null, null);
    public static RobotPrimitiveCall MoveXY(long xUm, long yUm) => new("MoveXY", null, xUm, yUm, null, null);
    public static RobotPrimitiveCall MoveZ(RobotZAxis axis, long zUm) => new("MoveZ", axis, null, null, zUm, null);
    public static RobotPrimitiveCall Aspirate(int volumeUl) => new("Aspirate", null, null, null, null, volumeUl);
    public static RobotPrimitiveCall Dispense(int volumeUl) => new("Dispense", null, null, null, null, volumeUl);
    public static RobotPrimitiveCall WashOuter() => new("WashOuter", null, null, null, null, null);
}

// 第一阶段 Mock 实现：不调用任何真实硬件 / SOCON，仅记录调用顺序并模拟成功。
// 满足“不实现真实硬件调用 / 保持 Mock 模式正常”的约束；同时其 Calls 列表直接服务于单元测试。
public sealed class MockRobotMotionPrimitives : IRobotMotionPrimitives
{
    private readonly object _gate = new();
    private long _xUm;
    private long _yUm;
    private long _z1Um;
    private long _z2Um;

    public List<RobotPrimitiveCall> Calls { get; } = new();

    public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Calls.Add(RobotPrimitiveCall.GetPosition());
            return Task.FromResult(new RobotArmPositionUm(_xUm, _yUm, _z1Um, _z2Um));
        }
    }

    public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _xUm = xUm;
            _yUm = yUm;
            Calls.Add(RobotPrimitiveCall.MoveXY(xUm, yUm));
        }

        return Task.CompletedTask;
    }

    public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            switch (axis)
            {
                case RobotZAxis.Z1:
                    _z1Um = zUm;
                    break;
                case RobotZAxis.Z2:
                    _z2Um = zUm;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown robot Z axis.");
            }

            Calls.Add(RobotPrimitiveCall.MoveZ(axis, zUm));
        }

        return Task.CompletedTask;
    }

    public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Calls.Add(RobotPrimitiveCall.Aspirate(volumeUl));
        }

        return Task.CompletedTask;
    }

    public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Calls.Add(RobotPrimitiveCall.Dispense(volumeUl));
        }

        return Task.CompletedTask;
    }

    public Task WashOuterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            Calls.Add(RobotPrimitiveCall.WashOuter());
        }

        return Task.CompletedTask;
    }
}

public sealed class UnavailableRobotMotionPrimitives : IRobotMotionPrimitives
{
    public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<RobotArmPositionUm>(FailClosed());

    public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default) =>
        Task.FromException(FailClosed());

    public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default) =>
        Task.FromException(FailClosed());

    public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default) =>
        Task.FromException(FailClosed());

    public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default) =>
        Task.FromException(FailClosed());

    public Task WashOuterAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(FailClosed());

    private static BusinessRuleException FailClosed() =>
        new(
            "robot_motion_primitives_unavailable",
            "Robot motion primitives are unavailable in Real mode until a real implementation is registered.",
            StatusCodes.Status409Conflict);
}
