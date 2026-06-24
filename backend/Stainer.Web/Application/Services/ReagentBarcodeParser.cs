namespace Stainer.Web.Application.Services;

public sealed class ReagentBarcodeParser : IReagentBarcodeParser
{
    public const int BarcodeLength = 17;
    public const int MicrolitersPerPointOneMl = 100;

    public ReagentBarcodeParseResult Parse(string? scanText)
    {
        var rawText = scanText?.Trim() ?? string.Empty;

        if (rawText.Length == 0)
        {
            return Invalid(rawText, "Barcode text is empty.");
        }

        if (rawText.Length != BarcodeLength)
        {
            return Invalid(rawText, "Barcode text must be 17 characters.");
        }

        if (rawText.Any(char.IsWhiteSpace))
        {
            return Invalid(rawText, "Barcode text must not contain whitespace.");
        }

        var reagentCode = rawText[..3];
        var quantityText = rawText.Substring(3, 3);
        var batchNo = rawText.Substring(6, 8);
        var serialNo = rawText.Substring(14, 3);

        if (!quantityText.All(char.IsDigit))
        {
            return Invalid(rawText, "Barcode quantity segment must be numeric.");
        }

        var quantityUnits = int.Parse(quantityText);
        var quantityUl = quantityUnits * MicrolitersPerPointOneMl;

        return new ReagentBarcodeParseResult(
            rawText,
            true,
            reagentCode,
            quantityUnits,
            quantityUl,
            batchNo,
            serialNo,
            "OK");
    }

    private static ReagentBarcodeParseResult Invalid(string rawText, string message)
    {
        return new ReagentBarcodeParseResult(rawText, false, null, null, null, null, null, message);
    }
}
