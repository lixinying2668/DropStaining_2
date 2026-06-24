namespace Stainer.Web.Domain.Entities;

public sealed class ReagentScanSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
    public ICollection<ReagentScanItem> Items { get; } = new List<ReagentScanItem>();
    public ICollection<ReagentRackPlacement> Placements { get; } = new List<ReagentRackPlacement>();
}
