namespace Stainer.Web.Domain.Entities;

public sealed class LiquidClassProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? AspirateSpeedUlPerSecond { get; set; }
    public int? DispenseSpeedUlPerSecond { get; set; }
    public int? PreWetCycles { get; set; }
    public int? MixCycles { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public ICollection<ReagentDefinition> ReagentDefinitions { get; } = new List<ReagentDefinition>();
}
