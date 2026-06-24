namespace Stainer.Web.Domain.Entities;

public sealed class ReagentBottle
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReagentDefinitionId { get; set; } = string.Empty;
    public string FullBarcode { get; set; } = string.Empty;
    public string ReagentCode { get; set; } = string.Empty;
    public string ProductionBatchNo { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public int InitialVolumeUl { get; set; }
    public int RemainingVolumeUl { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? FirstScannedAtUtc { get; set; }
    public DateTimeOffset? LastScannedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public ReagentDefinition? ReagentDefinition { get; set; }
    public ICollection<ReagentRackPlacement> Placements { get; } = new List<ReagentRackPlacement>();
}
