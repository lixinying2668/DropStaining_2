namespace Stainer.Web.Application.Services;

public interface IReagentBarcodeParser
{
    ReagentBarcodeParseResult Parse(string? scanText);
}
