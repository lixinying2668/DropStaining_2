using Microsoft.AspNetCore.Http;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;

namespace Stainer.Web.Infrastructure.Devices;

/// <summary>
/// 双针机械臂运动原语的 SOCON 实现：把 <see cref="IRobotMotionPrimitives"/> 接到已审核的 SOCON Bridge
/// 动作路径（Z-SOPA），复用 <see cref="IReagentHardwareActionClient"/>，不改 Bridge、不补协议。
/// 默认关闭（DI 仅在 Real &amp;&amp; SoconRobotMotion:Enabled 时注册本类），Real 未开启时由
/// UnavailableRobotMotionPrimitives 兜底 409，绝不回退 Mock。
/// </summary>
public sealed class SoconRobotMotionPrimitivesOptions
{
    public bool Enabled { get; set; }
    // 默认安全高度（µm），与 RobotArmAtomicHeights.SafeZUm 一致；吸排探动作前后的回抬基准。
    public long DefaultSafeZUm { get; set; } = 100_000;
    // 上电初值（µm）：内存 last-position 的起点（Bridge 是写通道无读位置契约，GetPosition 返回上次指令目标）。
    public long DefaultXUm { get; set; } = 0;
    public long DefaultYUm { get; set; } = 0;
}

/// <summary>
/// SOCON 实现的机械臂原语。内部维护 last-position 状态（镜像 MockRobotMotionPrimitives 的字段模式），
/// 因为 IRobotMotionPrimitives 的方法签名只给 x/y/z/volume，而 ReagentHardwareActionRequest 还需要 SafeZ/ActionZ/axis。
/// MoveXY/MoveZ 只发 Move 操作（BuildSteps：SafeZ→X→Y，不下 action Z、不回 SafeZ）；
/// Aspirate/Dispense 发对应操作（BuildSteps：SafeZ→X→Y→ActionZ→Aspirate/Dispense→ReturnSafeZ）；
/// WashOuter 协议缺失，fail-closed 409。
/// </summary>
public sealed class SoconRobotMotionPrimitives(
    IReagentHardwareActionClient hardwareActionClient,
    SoconRobotMotionPrimitivesOptions options) : IRobotMotionPrimitives
{
    private readonly object _gate = new();
    private long _xUm = options.DefaultXUm;
    private long _yUm = options.DefaultYUm;
    private long _z1Um = options.DefaultSafeZUm;
    private long _z2Um = options.DefaultSafeZUm;
    private RobotZAxis _lastActiveAxis = RobotZAxis.Z1;

    public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Bridge 是写动作通道，没有"读位置"契约；返回上次指令的目标位置（内存近似），非硬件反馈。
        // 若现场需要真实位置反馈，应走 IRealDeviceReadAdapter，不在本原语职责内。
        lock (_gate)
        {
            return Task.FromResult(new RobotArmPositionUm(_xUm, _yUm, _z1Um, _z2Um));
        }
    }

    public async Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long safeZUm;
        lock (_gate)
        {
            _xUm = xUm;
            _yUm = yUm;
            safeZUm = _z1Um; // 保持当前 z1 高度平移 XY（调用方应先 EnsureTravelSafeZ 抬到安全高度）。
        }

        // BuildSteps 对 Move 只做 SafeZ→X→Y；axis=z1 且 SafeZ=当前 z1 高度，故 Z 实质不动，只平移 XY。
        await ExecuteAsync(
            new ReagentHardwareActionRequest(
                ReagentHardwareActionOperations.Move,
                "z1",
                xUm,
                yUm,
                safeZUm,
                safeZUm),
            "robot_move_xy_failed",
            cancellationToken);
    }

    public async Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var axisCode = axis == RobotZAxis.Z2 ? "z2" : "z1";
        long xUm;
        long yUm;
        lock (_gate)
        {
            if (axis == RobotZAxis.Z2)
            {
                _z2Um = zUm;
            }
            else
            {
                _z1Um = zUm;
            }

            _lastActiveAxis = axis;
            xUm = _xUm;
            yUm = _yUm;
        }

        // 技巧：SafeZ 与 ActionZ 都设为目标 Z，BuildSteps 的 MoveSafeZ 实质把该轴 Z 移到目标高度，X/Y 不动。
        await ExecuteAsync(
            new ReagentHardwareActionRequest(
                ReagentHardwareActionOperations.Move,
                axisCode,
                xUm,
                yUm,
                zUm,
                zUm),
            "robot_move_z_failed",
            cancellationToken);
    }

    public async Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (volumeUl <= 0)
        {
            throw new BusinessRuleException("robot_aspirate_volume_invalid", "Aspirate volume must be positive in uL.", StatusCodes.Status400BadRequest);
        }

        await ExecutePipetteAsync(ReagentHardwareActionOperations.Aspirate, volumeUl, "robot_aspirate_failed", cancellationToken);
    }

    public async Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (volumeUl <= 0)
        {
            throw new BusinessRuleException("robot_dispense_volume_invalid", "Dispense volume must be positive in uL.", StatusCodes.Status400BadRequest);
        }

        await ExecutePipetteAsync(ReagentHardwareActionOperations.Dispense, volumeUl, "robot_dispense_failed", cancellationToken);
    }

    public Task WashOuterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 外壁清洗不是 SOCON 动作，主控协议也无对应命令字；在获得经审核的清洗协议前保持 fail-closed。
        throw new BusinessRuleException(
            "robot_wash_not_socon_action",
            "Outer-needle wash is not a SOCON action and remains fail-closed until a reviewed protocol exists.",
            StatusCodes.Status409Conflict);
    }

    private async Task ExecutePipetteAsync(string operation, int volumeUl, string failErrorCode, CancellationToken cancellationToken)
    {
        string axisCode;
        long actionZUm;
        long xUm;
        long yUm;
        lock (_gate)
        {
            axisCode = _lastActiveAxis == RobotZAxis.Z2 ? "z2" : "z1";
            actionZUm = _lastActiveAxis == RobotZAxis.Z2 ? _z2Um : _z1Um;
            xUm = _xUm;
            yUm = _yUm;
        }

        await ExecuteAsync(
            new ReagentHardwareActionRequest(
                operation,
                axisCode,
                xUm,
                yUm,
                options.DefaultSafeZUm,
                actionZUm,
                volumeUl),
            failErrorCode,
            cancellationToken);
    }

    private async Task ExecuteAsync(ReagentHardwareActionRequest request, string failErrorCode, CancellationToken cancellationToken)
    {
        var result = await hardwareActionClient.ExecuteAsync(request, cancellationToken);
        if (!result.Ok)
        {
            throw new BusinessRuleException(
                result.ErrorCode ?? failErrorCode,
                result.Message,
                StatusCodes.Status503ServiceUnavailable);
        }
    }
}
