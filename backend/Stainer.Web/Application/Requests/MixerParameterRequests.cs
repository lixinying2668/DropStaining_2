namespace Stainer.Web.Application.Requests;

// 保存混匀参数配置（配置模块「清洗混匀 → 混匀参数」）。drawerCode 由路由 {drawerCode} 提供（权威），
// 不放入请求体，避免 minimal API 对全简单属性 record 的 body 推断冲突（与 SaveSerialConnectionRequest 一致）。
public sealed record SaveMixerParameterRequest(
    string CommandId,
    string? Origin,
    int? StartStroke,
    int? TotalStroke,
    int? TopDwellMs,
    int? BottomDwellMs,
    int? ForwardSpeed,
    int? ReverseSpeed,
    int? TargetCycles,
    int? RemainingCycles,
    string Reason);
