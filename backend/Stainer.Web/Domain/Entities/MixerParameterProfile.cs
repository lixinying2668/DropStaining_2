namespace Stainer.Web.Domain.Entities;

// 混匀参数配置（DB 持久化）。配置模块「清洗混匀 → 混匀参数」按抽屉(drawer)单行 upsert，
// 镜像 PrecisionCalibrationProfile 的单行-per-key + 幂等 + 审计模式。
// 注意：仅持久化工程配置参数，不触发真实混匀动作；真实电机配置将来走主控 0x0A，此处为上位机侧参数档案。
public sealed class MixerParameterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DrawerCode { get; set; } = MixerDrawerCodes.A;
    public string? Origin { get; set; }              // 混匀原点
    public int? StartStroke { get; set; }            // 混匀始行程
    public int? TotalStroke { get; set; }            // 混匀总行程
    public int? TopDwellMs { get; set; }             // 上停顿时间(ms)
    public int? BottomDwellMs { get; set; }          // 下停顿时间(ms)
    public int? ForwardSpeed { get; set; }           // 电机前进速度
    public int? ReverseSpeed { get; set; }           // 电机回退速度
    public int? TargetCycles { get; set; }           // 混匀次数
    public int? RemainingCycles { get; set; }        // 剩余混匀次数（可重置）
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public static class MixerDrawerCodes
{
    public const string A = "A";
    public const string B = "B";
    public const string C = "C";
    public const string D = "D";
    public static readonly string[] All = { A, B, C, D };
}
