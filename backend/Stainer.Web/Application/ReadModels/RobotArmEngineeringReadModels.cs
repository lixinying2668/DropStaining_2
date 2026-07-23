namespace Stainer.Web.Application.ReadModels;

// 机械臂工程隔离测试统一结果。Action 为 "MoveXY" 或具体原子动作名；Steps 为语义化步骤。
public sealed record RobotArmHardwareActionResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string Action,
    IReadOnlyList<string> Steps,
    string Message);
