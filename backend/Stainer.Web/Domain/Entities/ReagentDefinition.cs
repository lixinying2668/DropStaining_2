namespace Stainer.Web.Domain.Entities;

public sealed class ReagentDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReagentCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LiquidClassProfileId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public LiquidClassProfile? LiquidClassProfile { get; set; }
    public ICollection<ReagentBottle> Bottles { get; } = new List<ReagentBottle>();
}
