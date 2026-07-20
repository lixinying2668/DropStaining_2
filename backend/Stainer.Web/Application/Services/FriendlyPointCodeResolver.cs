using System.Text.RegularExpressions;

namespace Stainer.Web.Application.Services;

// 通道移液"友好孔位码" → 后端坐标点编码 解析器。
// 前端 configHoleOptions 用硬件布局友好码（S11/R11/P11/洗针孔1/A液…），后端 coordinate_points 用另一套
// （R1-R40 / A-01..D-04 / M1-M8 / WashInnerLeft…）。本解析器只在 pipetting 入口把友好码翻译成后端码，
// 不重命名坐标基线、不破坏其它引用。映射反向于 ReferenceDataSeeder 的生成规则，全部可算。
// 未命中返回 null（由调用方原样透传，交给 RequireCoordinatePointAsync 校验/拒绝）。
// 详见记忆 pipette-friendly-code-resolver。
public static class FriendlyPointCodeResolver
{
    private static readonly Regex ReagentPattern = new(@"^S([1-5])([1-8])$", RegexOptions.Compiled);
    private static readonly Regex SlotPattern = new(@"^R([1-4])([1-4])$", RegexOptions.Compiled);
    private static readonly string[] DabFriendly = { "P11", "P12", "P21", "P22", "P31", "P32", "P41", "P42" };

    // 按 coordinate-baseline CSV 的 2×2 洗针头网格（行,列）对应（行1/行2 ↔ backend Left/Right；
    // 列1=洗外壁 Outer、列2=洗内壁 Inner。源自 DigitalTwinCoordinateImportService 的 (row,col)→WashName）。
    // 如硬件实际编号不同，改这里即可。
    private static readonly Dictionary<string, string> WashMap = new()
    {
        ["洗针孔1"] = "WashOuterLeft",   // (行1,列1) 左列洗外壁 R1
        ["洗针孔2"] = "WashInnerLeft",   // (行1,列2) 右列洗内壁 R1
        ["洗针孔3"] = "WashOuterRight",  // (行2,列1) 左列洗外壁 R2
        ["洗针孔4"] = "WashInnerRight"   // (行2,列2) 右列洗内壁 R2
    };

    private static readonly Dictionary<string, string> DabSourceMap = new()
    {
        ["A液"] = "DabA",
        ["B液"] = "DabB"
    };

    public static string? Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var value = code.Trim();

        // 试剂：S{channel(1-5)}{bottle(1-8)} → R{(channel-1)*8+bottle}
        var m = ReagentPattern.Match(value);
        if (m.Success)
        {
            var channel = int.Parse(m.Groups[1].Value);
            var bottle = int.Parse(m.Groups[2].Value);
            return $"R{(channel - 1) * 8 + bottle}";
        }

        // 玻片/样本：R{ch(1-4)}{i(1-4)} → {drawer}-{i:00}（drawer A/B/C/D ↔ ch 1/2/3/4）
        m = SlotPattern.Match(value);
        if (m.Success)
        {
            var ch = int.Parse(m.Groups[1].Value);
            var i = int.Parse(m.Groups[2].Value);
            var drawer = (char)('A' + ch - 1);
            return $"{drawer}-{i:00}";
        }

        // DAB 混匀：P11,P12,P21,P22,P31,P32,P41,P42 → M1..M8（按序）
        var dabIndex = Array.IndexOf(DabFriendly, value);
        if (dabIndex >= 0)
        {
            return $"M{dabIndex + 1}";
        }

        // 清洗位
        if (WashMap.TryGetValue(value, out var wash))
        {
            return wash;
        }

        // DAB 源液瓶
        if (DabSourceMap.TryGetValue(value, out var dabSource))
        {
            return dabSource;
        }

        return null; // 未命中：透传（排毒孔/废液孔/清洗孔、后端原生码、未知码 → 交后端校验）
    }
}
