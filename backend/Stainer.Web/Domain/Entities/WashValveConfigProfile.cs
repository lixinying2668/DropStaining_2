namespace Stainer.Web.Domain.Entities;

// 清洗/电磁阀配置（DB 持久化）。配置模块「清洗混匀 → 样本清洗/阀状态」的单行配置，
// 镜像 SerialConnectionProfile 的单行-per-key + 幂等 + 审计模式。
// 注意：仅持久化工程配置（清洗温度、电磁阀预期开闭状态），不直接驱动真实硬件；真实电磁阀/温控将来走主控。
public sealed class WashValveConfigProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ScopeKey { get; set; } = WashValveConfigScopeKeys.SampleWash;
    public decimal? WashTempC { get; set; }   // 样本清洗温度 / ℃
    public bool SolenoidOpen { get; set; }    // 电磁阀预期开闭状态
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public static class WashValveConfigScopeKeys
{
    public const string SampleWash = "sample-wash";
}
