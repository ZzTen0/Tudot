using System.Text.RegularExpressions;

namespace Tudot.Services;

/// <summary>
/// 文件夹名相似元素分析（批量统一命名用）。
/// 规则与 v1.2.1 交互预览页一致：
/// 按空格/_/-/+/·/. 分词，统一大小写比对；
/// 全部名字共有的词为强相似，三分之二以上共有的为弱相似；纯数字词不作文本标签。
/// </summary>
public static class NameSimilarity
{
    // 提取「数字串」或「文字串」；其余字符（空格、_、-、+、·、.、各类括号等）均为分隔符。
    // 文字覆盖：拉丁字母、CJK 汉字、平假名、片假名（含长音ー/扩展）、半角片假名、韩文音节。
    // 数字与文字交界处切分（相册1 → 相册 + 1），但中文/日文与英文字母粘连不切分（公司a 保持整体）。
    private static readonly Regex TokenRx = new(
        "[0-9]+|[A-Za-z一-鿿぀-ゟ゠-ヿㇰ-ㇿ가-힯･-ﾟ]+",
        RegexOptions.Compiled);
    private static readonly Regex NumberRx = new(@"\d+", RegexOptions.Compiled);

    public static List<string> Tokenize(string name) =>
        TokenRx.Matches(name).Select(m => m.Value).ToList();

    /// <summary>分析结果：Common 所有名字共有（保留首个名字中的原始写法），Weak 多数共有，Numbers 每行首个数字串，Suffixes 每行去掉公共词与数字词后的剩余拼接。</summary>
    public record Result(
        List<string> Common,
        List<string> Weak,
        List<string> Numbers,
        List<string> Suffixes);

    public static Result Analyze(IReadOnlyList<string> names)
    {
        var lists = names.Select(Tokenize).ToList();

        // 每行第一个数字串
        var numbers = names
            .Select(n => NumberRx.Match(n) is { Success: true } m ? m.Value : "")
            .ToList();

        // 词 → 出现于多少行（大小写不敏感，行内去重）
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var words in lists)
        {
            foreach (var k in words.Where(w => !int.TryParse(w, out _)).Distinct(StringComparer.OrdinalIgnoreCase))
                count[k] = count.GetValueOrDefault(k) + 1;
        }

        // 恢复首个名字中的原始写法
        string OriginalForm(string key)
        {
            foreach (var words in lists)
            {
                var hit = words.FirstOrDefault(w => string.Equals(w, key, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return key;
        }

        var common = count
            .Where(kv => kv.Value == names.Count)
            .Select(kv => OriginalForm(kv.Key))
            .ToList();

        var weakThreshold = (int)Math.Ceiling(names.Count * 2.0 / 3.0);
        var weak = count
            .Where(kv => kv.Value >= weakThreshold && kv.Value < names.Count)
            .Select(kv => OriginalForm(kv.Key))
            .ToList();

        var commonKeys = common.Select(c => c.ToLowerInvariant()).ToHashSet();
        var suffixes = lists
            .Select(words => string.Concat(words
                .Where(w => !commonKeys.Contains(w.ToLowerInvariant()) && !int.TryParse(w, out _))))
            .ToList();

        return new Result(common, weak, numbers, suffixes);
    }

    /// <summary>命名片段类型：普通文本 / 分隔符 / 原编号 / 后缀</summary>
    public enum PartKind { Text, Sep, Number, Suffix }

    /// <summary>命名片段</summary>
    public record NamePart(PartKind Kind, string Value = "");

    public static NamePart TextPart(string v) => new(PartKind.Text, v);
    public static NamePart SepPart(string v) => new(PartKind.Sep, v);
    public static NamePart NumberPart() => new(PartKind.Number);
    public static NamePart SuffixPart() => new(PartKind.Suffix);

    /// <summary>
    /// 按片段序列拼装最终名。
    /// 规则：空片段跳过；分隔符在句首、或前一片段也是分隔符时跳过；结尾残留的分隔符删除。
    /// </summary>
    public static string BuildName(IReadOnlyList<NamePart> parts, string number, string suffix, bool keepNumber)
    {
        var seg = new List<(string Value, bool IsSep)>();
        foreach (var p in parts)
        {
            switch (p.Kind)
            {
                case PartKind.Text when p.Value.Length > 0:
                    seg.Add((p.Value, false));
                    break;
                case PartKind.Number when keepNumber && number.Length > 0:
                    seg.Add((number, false));
                    break;
                case PartKind.Suffix when suffix.Length > 0:
                    seg.Add((suffix, false));
                    break;
                case PartKind.Sep:
                    // 句首或前一片段为分隔符时跳过，避免连续分隔符
                    if (seg.Count == 0 || seg[^1].IsSep) continue;
                    seg.Add((p.Value, true));
                    break;
            }
        }
        // 删除结尾未消费的分隔符
        while (seg.Count > 0 && seg[^1].IsSep) seg.RemoveAt(seg.Count - 1);
        return string.Concat(seg.Select(s => s.Value));
    }

    // ═══════ 导入时「同步重命名相册内文件名」 ═══════

    /// <summary>文件名片段类型：普通文本 / 分隔符 / 相册名后缀 / 分文件夹名 / 顺序编号</summary>
    public enum FilePartKind { Text, Sep, AlbumSuffix, SubDir, Number }

    /// <summary>文件名片段。Sep 的 Value 为分隔符文本；Number 的 Digits 为位数（4=0001）；Text 的 Value 为内容</summary>
    public record FilePart(FilePartKind Kind, string Value = "", int Digits = 4);

    /// <summary>
    /// 按片段序列拼装文件名（不含扩展名）。
    /// 与 BuildName 同规则：空片段跳过；分隔符不在句首、不连续；结尾分隔符删除。
    /// </summary>
    public static string BuildFileName(IReadOnlyList<FilePart> parts, string albumSuffix, string subDir, int number)
    {
        var seg = new List<(string Value, bool IsSep)>();
        foreach (var p in parts)
        {
            switch (p.Kind)
            {
                case FilePartKind.Text when p.Value.Length > 0:
                    seg.Add((p.Value, false));
                    break;
                case FilePartKind.AlbumSuffix when albumSuffix.Length > 0:
                    seg.Add((albumSuffix, false));
                    break;
                case FilePartKind.SubDir when subDir.Length > 0:
                    seg.Add((subDir, false));
                    break;
                case FilePartKind.Number:
                    seg.Add((number.ToString($"D{Math.Clamp(p.Digits, 1, 8)}"), false));
                    break;
                case FilePartKind.Sep:
                    if (seg.Count == 0 || seg[^1].IsSep) continue;
                    seg.Add((p.Value, true));
                    break;
            }
        }
        while (seg.Count > 0 && seg[^1].IsSep) seg.RemoveAt(seg.Count - 1);
        return string.Concat(seg.Select(s => s.Value));
    }
}
