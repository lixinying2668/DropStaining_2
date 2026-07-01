namespace Stainer.Web.Domain.Entities;

public sealed class DabMixPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = string.Empty;
    public int PositionNo { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = DabMixPositionStatus.Available;
    public string? ActiveDabBatchId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public static class DabMixPositionStatus
{
    public const string Available = "Available";
    public const string Occupied = "Occupied";
    public const string AwaitingCleaning = "AwaitingCleaning";
    public const string NeedsManualResolution = "NeedsManualResolution";
    public const string Disabled = "Disabled";
}
