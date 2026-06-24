namespace Stainer.Web.Domain.Entities;

public sealed class ReagentScanItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReagentScanSessionId { get; set; } = string.Empty;
    public string ReagentRackPositionId { get; set; } = string.Empty;
    public int ScannerChannelNo { get; set; }
    public string ScannerChannelCode { get; set; } = string.Empty;
    public string? LocatorCode { get; set; }
    public string ScanResult { get; set; } = string.Empty;
    public string? RawBarcode { get; set; }
    public string? ParsedReagentCode { get; set; }
    public int? ParsedQuantityUl { get; set; }
    public string? ParsedBatchNo { get; set; }
    public string? ParsedSerialNo { get; set; }
    public bool IsValidationPassed { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ReagentScanSession? ReagentScanSession { get; set; }
    public ReagentRackPosition? ReagentRackPosition { get; set; }
}
