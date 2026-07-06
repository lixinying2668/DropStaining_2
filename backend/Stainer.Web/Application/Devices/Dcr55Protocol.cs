using System.Text;

namespace Stainer.Web.Application.Devices;

public static class Dcr55Protocol
{
    public const string SingleTriggerCommandText = "RDCMXEV1,P11,P20";
    public const string StopTriggerCommandText = "RDCMXEV1,P10";
    public const string ContinuousTriggerCommandText = "RDCMXEV1,P11,P21";

    public static string GetCommandText(Dcr55TriggerMode mode) => mode switch
    {
        Dcr55TriggerMode.Single => SingleTriggerCommandText,
        Dcr55TriggerMode.Stop => StopTriggerCommandText,
        Dcr55TriggerMode.Continuous => ContinuousTriggerCommandText,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static byte[] EncodeCommand(Dcr55TriggerMode mode, byte[] configuredTerminator)
    {
        ArgumentNullException.ThrowIfNull(configuredTerminator);
        return Encoding.ASCII.GetBytes(GetCommandText(mode)).Concat(configuredTerminator).ToArray();
    }

    public static Dcr55ScanResult ParseBarcodeResult(string rawResult)
    {
        ArgumentNullException.ThrowIfNull(rawResult);
        if (rawResult.Length == 0)
        {
            return new Dcr55ScanResult(Dcr55ScanOutcome.Pending, [], rawResult, null, 0);
        }

        var hasCompleteSuffix = rawResult.EndsWith("\r\n", StringComparison.Ordinal);
        var segments = rawResult.Split("\r\n", StringSplitOptions.None);
        var completedCount = segments.Length - 1;
        var completedRecords = segments.Take(completedCount).ToArray();
        var barcodes = completedRecords
            .Where(value => value.Length > 0)
            .ToArray();
        var pendingFragment = hasCompleteSuffix ? null : segments[^1];

        return new Dcr55ScanResult(
            hasCompleteSuffix && barcodes.Length > 0 ? Dcr55ScanOutcome.Completed : Dcr55ScanOutcome.Pending,
            barcodes,
            rawResult,
            pendingFragment,
            completedRecords.Count(value => value.Length == 0));
    }

    public static Dcr55ScanResult NoBarcodeTimeout(string rawResult = "") =>
        new(Dcr55ScanOutcome.NoBarcodeTimeout, [], rawResult, rawResult.Length == 0 ? null : rawResult, 0);
}

public enum Dcr55TriggerMode
{
    Single,
    Stop,
    Continuous
}

public enum Dcr55ScanOutcome
{
    Pending,
    Completed,
    NoBarcodeTimeout
}

public sealed record Dcr55ScanResult(
    Dcr55ScanOutcome Outcome,
    IReadOnlyList<string> Barcodes,
    string RawResult,
    string? PendingFragment,
    int EmptyRecordCount);
