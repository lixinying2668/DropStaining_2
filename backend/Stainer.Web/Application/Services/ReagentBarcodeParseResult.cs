namespace Stainer.Web.Application.Services;

public sealed record ReagentBarcodeParseResult(
    string RawText,
    bool IsValid,
    string? ReagentCode,
    int? QuantityUnitsOfPointOneMl,
    int? QuantityUl,
    string? ProductionBatchNo,
    string? SerialNo,
    string ValidationMessage);
