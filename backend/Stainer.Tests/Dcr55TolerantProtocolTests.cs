using Stainer.Web.Application.Devices;

namespace Stainer.Tests;

// Dcr55TolerantProtocol 的离线单元测试（【新增】文件，不改任何现有代码）。
// 验证容错解析：兼容 \r / \r\n / \n / 无结束符；空、多条码、控制字符、非 ASCII 仍判非法。
public sealed class Dcr55TolerantProtocolTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 7, 8, 1, 2, 3, TimeSpan.Zero);

    [Theory]
    [InlineData("ABC123\r\n", "ABC123")]      // 标准 CRLF
    [InlineData("ABC123\r", "ABC123")]        // 仅 CR（现场 DCR55 实测）
    [InlineData("ABC123\n", "ABC123")]        // 仅 LF
    [InlineData("ABC123", "ABC123")]          // 无结束符
    [InlineData("ABC123\r\n\r\n", "ABC123")]  // 尾部多余换行
    public void Parses_barcode_with_various_terminators(string rawText, string expectedBarcode)
    {
        var result = Dcr55TolerantProtocol.ParseBarcodeResult(rawText, ObservedAt);

        Assert.Equal(Dcr55ScanStatus.Success, result.Status);
        Assert.Equal(expectedBarcode, result.Barcode);
        Assert.Equal(rawText, result.RawText);
        Assert.Equal(ObservedAt, result.Timestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r\n\r\n")]
    public void Empty_or_whitespace_only_response_is_invalid(string rawText)
    {
        var result = Dcr55TolerantProtocol.ParseBarcodeResult(rawText, ObservedAt);

        Assert.Equal(Dcr55ScanStatus.InvalidResponse, result.Status);
        Assert.Null(result.Barcode);
    }

    [Fact]
    public void Multiple_barcodes_in_one_response_is_invalid()
    {
        var result = Dcr55TolerantProtocol.ParseBarcodeResult("ABC123\r\nDEF456\r\n", ObservedAt);

        Assert.Equal(Dcr55ScanStatus.InvalidResponse, result.Status);
        Assert.Null(result.Barcode);
    }

    [Fact]
    public void Control_character_in_barcode_is_invalid()
    {
        // 用显式 (char) 构造，避免源码里混入不可见控制字符。
        var rawText = "ABC123" + (char)0x01 + "\r\n";

        var result = Dcr55TolerantProtocol.ParseBarcodeResult(rawText, ObservedAt);

        Assert.Equal(Dcr55ScanStatus.InvalidResponse, result.Status);
        Assert.Null(result.Barcode);
    }

    [Fact]
    public void Non_ascii_byte_in_response_is_invalid()
    {
        // 0x80 > 0x7F，应被判为非法响应。
        var rawText = "ABC123" + (char)0x80 + "\r";

        var result = Dcr55TolerantProtocol.ParseBarcodeResult(rawText, ObservedAt);

        Assert.Equal(Dcr55ScanStatus.InvalidResponse, result.Status);
        Assert.Null(result.Barcode);
    }

    [Fact]
    public void Null_raw_text_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Dcr55TolerantProtocol.ParseBarcodeResult(null!));
    }
}
