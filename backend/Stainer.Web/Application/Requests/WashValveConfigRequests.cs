namespace Stainer.Web.Application.Requests;

// 保存清洗/电磁阀配置（配置模块「清洗混匀 → 样本清洗/阀状态」）。
public sealed record SaveWashValveConfigRequest(
    string CommandId,
    decimal? WashTempC,
    bool? SolenoidOpen,
    string Reason);
