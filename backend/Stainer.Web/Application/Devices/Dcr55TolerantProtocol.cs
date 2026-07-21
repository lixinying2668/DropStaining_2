namespace Stainer.Web.Application.Devices;

// DCR55 容错条码解析（与现有 Dcr55Protocol 并列的【新增】类型，不修改任何现有代码）。
//
// 背景：现场实测 DCR55 触发后返回的条码以单个 \r（0x0D）结尾，而现有
// Dcr55Protocol.ParseBarcodeResult 只接受以 \r\n 结尾的响应，遇到 \r 结尾会判非法。
// 本类型在“不改 Dcr55Protocol”的前提下提供等价的解析能力，放宽帧结束符约束：
// 兼容 \r / \r\n / \n / 无结束符；其余约束与 Dcr55Protocol 保持一致——
// 恰好一条非空、纯 ASCII、无控制字符的记录；多条码（一次返回多条）仍判为非法。
public static class Dcr55TolerantProtocol
{
    public static Dcr55ScanResult ParseBarcodeResult(string rawText, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        var observedAt = timestamp ?? DateTimeOffset.UtcNow;

        if (rawText.Length == 0 || rawText.Any(ch => ch > 0x7F))
        {
            return Invalid(rawText, observedAt);
        }

        // 去掉末尾换行后按任意换行拆分；要求恰好一条非空、无控制字符的记录。
        var normalized = rawText.TrimEnd('\r', '\n');
        var records = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (records.Length != 1
            || string.IsNullOrWhiteSpace(records[0])
            || records[0].Any(char.IsControl))
        {
            return Invalid(rawText, observedAt);
        }

        return new Dcr55ScanResult(records[0], rawText, Dcr55ScanStatus.Success, observedAt);
    }

    private static Dcr55ScanResult Invalid(string rawText, DateTimeOffset timestamp) =>
        new(null, rawText, Dcr55ScanStatus.InvalidResponse, timestamp);
}
