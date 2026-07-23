using System.Text.Json;

namespace Stainer.Web.Application.Requests;

public sealed record StartEngineeringSessionRequest(
    string CommandId,
    string Password,
    string Reason,
    string Target,
    bool DangerousOperationConfirmed = false,
    int? DurationMinutes = null);

public sealed record EndEngineeringSessionRequest(
    string CommandId,
    string Reason,
    string Target);

public sealed record PreviewEngineeringConfigImportRequest(
    string ConfigType,
    string TargetCode,
    JsonElement Payload);

public sealed record ApplyEngineeringConfigImportRequest(
    string CommandId,
    string ConfigType,
    string TargetCode,
    string Reason,
    JsonElement Payload,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record PreviewDigitalTwinCoordinateImportRequest(
    string SourceFilePath,
    string? ProfileCode = null,
    string? VersionLabel = null,
    string? MappingVersion = null);

public sealed record ApplyDigitalTwinCoordinateImportRequest(
    string CommandId,
    string SourceFilePath,
    string Reason,
    string? ProfileCode = null,
    string? VersionLabel = null,
    string? MappingVersion = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record DeactivateCoordinateProfileVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record DisableLiquidClassVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

// 通用坐标点现场移动（玻片/A-B液/配液/清洗位等）请求。pointCode 由路由 {pointCode} 提供（权威）。
// 与 MoveReagentPositionHardwareRequest 同形，额外允许指定坐标档案版本（否则取 active 版本）。
public sealed record MoveCoordinatePointHardwareRequest(
    string CommandId,
    string NeedleCode,
    string TargetZ,
    string Reason,
    string? CoordinateProfileVersionId = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record EngineeringPipettingTestRequest(
    string CommandId,
    string Channel,
    string? Position = null,
    string? CoordinatePointCode = null,
    string? CoordinateProfileVersionId = null,
    int? Volume = null,
    int? VolumeUl = null,
    string? NeedleCode = null,
    string? LiquidClassVersionId = null,
    string? LiquidClassCode = null,
    JsonElement? OperationParameters = null,
    string? Reason = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

// 机械臂工程隔离测试：XY 安全平移。委托 MotionControlService.MoveToXYAtSafeHeightAsync（先抬双针 SafeZ 再平移 XY）。
// PointCode 仅用于审计/日志；实际坐标由 TargetXUm/TargetYUm/SafeZUm 显式给定。
public sealed record MoveRobotArmHardwareRequest(
    string CommandId,
    long TargetXUm,
    long TargetYUm,
    long SafeZUm,
    string? PointCode = null,
    string? Reason = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

// 机械臂工程隔离测试：单个工艺原子动作。委托 RobotArmAtomicActionService（编排底层 IRobotMotionPrimitives）。
// Action 取 TakeLiquid|PrepareMix|DispenseLiquid|WashInner|WashOuter；体积/高度未传则回退 RobotArmAtomicHeights。
public sealed record RunRobotArmAtomicHardwareRequest(
    string CommandId,
    string Action,
    string? NeedleCode = null,
    int? VolumeUl = null,
    int? WashVolumeUl = null,
    int? WasteVolumeUl = null,
    long? AspirateZUm = null,
    long? MixZUm = null,
    long? DispenseZUm = null,
    long? WashInnerZUm = null,
    long? WashOuterZUm = null,
    long? SafeZUm = null,
    string? Reason = null,
    string? Target = null,
    bool DangerousOperationConfirmed = false);
