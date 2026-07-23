namespace Stainer.Web.Application.Requests;

// 染色机工艺原子动作所需的各阶段 Z 轴高度（单位：微米 µm）。
// 第一阶段仅做业务封装，这些高度是占位默认值；后续接真实 SOCON 时可改为从配置 / 坐标档案读取。
// 测试与运行时均可通过构造一个显式的 RobotArmAtomicHeights 覆盖默认值，保证可观测、可断言。
public sealed record RobotArmAtomicHeights
{
    // 吸液阶段：针尖深入液面附近。
    public long AspirateZUm { get; init; } = -5_000;
    // 配液 / 混匀阶段：在目标孔位释放液体。
    public long MixZUm { get; init; } = -2_000;
    // 滴液阶段：在玻片 / 孔位上方滴液。
    public long DispenseZUm { get; init; } = -3_000;
    // 内壁清洗阶段：在洗针位进行吸 / 排清洗。
    public long WashInnerZUm { get; init; } = -5_000;
    // 外壁清洗阶段：执行外壁冲洗动作的高度。
    public long WashOuterZUm { get; init; } = -4_000;
    // 安全高度：每个动作结束后抬升回到的高度（回零 / 待机）。
    public long SafeZUm { get; init; } = 100_000;
}

// 以下请求 DTO 仅承载业务参数（针头、体积、原因等）；Z 高度由 RobotArmAtomicActionService 统一管理，
// 与现有 DeviceRequests 中的 CommandId 风格保持一致，方便后续接设备命令链路。

public sealed record TakeLiquidRequest(
    string CommandId,
    string? NeedleCode,
    int VolumeUl,
    string? Reason = null,
    // 可选：按调用指定吸液高度 / 安全高度（单位 µm）。未传则回退到 RobotArmAtomicHeights 配置。
    // XY 移动由调用方负责，这里只决定 Z。
    long? AspirateZUm = null,
    long? SafeZUm = null);

public sealed record PrepareMixRequest(
    string CommandId,
    string? NeedleCode,
    int VolumeUl,
    string? Reason = null,
    // 可选：按调用指定配液高度 / 安全高度（单位 µm）。未传则回退到 RobotArmAtomicHeights 配置。
    long? MixZUm = null,
    long? SafeZUm = null);

public sealed record DispenseLiquidRequest(
    string CommandId,
    string? NeedleCode,
    int VolumeUl,
    string? Reason = null,
    // 可选：按调用指定滴液高度 / 安全高度（单位 µm）。未传则回退到 RobotArmAtomicHeights 配置。
    long? DispenseZUm = null,
    long? SafeZUm = null);

public sealed record WashInnerRequest(
    string CommandId,
    string? NeedleCode,
    int WashVolumeUl,
    int WasteVolumeUl,
    string? Reason = null,
    // 可选：按调用指定内壁清洗高度 / 安全高度（单位 µm）。未传则回退到 RobotArmAtomicHeights 配置。
    long? WashInnerZUm = null,
    long? SafeZUm = null);

public sealed record WashOuterRequest(
    string CommandId,
    string? NeedleCode,
    string? Reason = null,
    // 可选：按调用指定外壁清洗高度 / 安全高度（单位 µm）。未传则回退到 RobotArmAtomicHeights 配置。
    long? WashOuterZUm = null,
    long? SafeZUm = null);

// 单个原子步骤的语义化记录，用于结果追踪与日志展示（与底层 primitives 调用顺序一一对应）。
public sealed record RobotArmAtomicStep(string Name, string? Detail = null);

// 原子动作完成后交给记录器的上下文（Mock 运行状态 / 流水账写入用）。
// NetVolumeUl：正=吸入（针头液量增加），负=排出（针头液量减少），0=清洗。
// ClearsNeedle：清洗类动作把针头清空（液量归零、NeedsWash=false）。
// FinalZUm：动作结束时机械臂所在 Z（=安全高度）。
public sealed record RobotArmAtomicActionContext(
    string CommandId,
    string? NeedleCode,
    string Action,
    int NetVolumeUl,
    bool ClearsNeedle,
    string? Reason,
    long FinalZUm);

// 原子动作统一返回结果。Steps 为按执行顺序记录的语义化步骤，便于前端 / 日志观测。
public sealed record RobotArmAtomicActionResult(
    bool Ok,
    string Action,
    IReadOnlyList<RobotArmAtomicStep> Steps,
    string Message,
    string? ErrorCode = null)
{
    public static RobotArmAtomicActionResult Succeeded(string action, IReadOnlyList<RobotArmAtomicStep> steps, string message) =>
        new(true, action, steps, message, null);

    public static RobotArmAtomicActionResult Failed(string action, IReadOnlyList<RobotArmAtomicStep> steps, string message, string errorCode) =>
        new(false, action, steps, message, errorCode);
}
