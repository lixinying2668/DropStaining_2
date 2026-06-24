namespace Stainer.Web.Domain.Entities;

public sealed class ReagentRackPlacement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReagentBottleId { get; set; } = string.Empty;
    public string ReagentRackPositionId { get; set; } = string.Empty;
    public string? ReagentScanSessionId { get; set; }
    public DateTimeOffset PlacedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ReagentBottle? ReagentBottle { get; set; }
    public ReagentRackPosition? ReagentRackPosition { get; set; }
    public ReagentScanSession? ReagentScanSession { get; set; }
}
