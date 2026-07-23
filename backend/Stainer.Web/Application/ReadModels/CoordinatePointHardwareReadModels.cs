namespace Stainer.Web.Application.ReadModels;

// 通用坐标点现场移动结果。与 ReagentPositionHardwareActionResponse 同形，把 RackCode 换成 PointCode 以保持语义清晰。
public sealed record CoordinatePointHardwareActionResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string PointCode,
    string NeedleCode,
    string TargetZ,
    IReadOnlyList<string> CompletedSteps,
    string Message);
