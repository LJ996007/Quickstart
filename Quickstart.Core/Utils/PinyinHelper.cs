namespace Quickstart.Utils;

using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Lightweight pinyin initial letter matcher for common Chinese characters.
/// Covers GB2312 first-level characters (~3755 chars) mapped to initials.
/// </summary>
public static class PinyinHelper
{
    // GB2312 first-level Chinese character section boundaries (sorted by pinyin)
    private static readonly (int Code, char Initial)[] Sections =
    [
        (0xB0A1, 'A'), (0xB0C5, 'B'), (0xB2C1, 'C'), (0xB4EE, 'D'),
        (0xB6EA, 'E'), (0xB7A2, 'F'), (0xB8C1, 'G'), (0xB9FE, 'H'),
        (0xBBF7, 'J'), (0xBFA6, 'K'), (0xC0AC, 'L'), (0xC2E8, 'M'),
        (0xC4C3, 'N'), (0xC5B6, 'O'), (0xC5BE, 'P'), (0xC6DA, 'Q'),
        (0xC8BB, 'R'), (0xC8F6, 'S'), (0xCBFA, 'T'), (0xCDDA, 'W'),
        (0xCEF4, 'X'), (0xD1B9, 'Y'), (0xD4D1, 'Z'),
    ];

    // 缓存 GB2312 编码器，避免每个字符都做一次 GetEncoding 查找。
    // Program.Main 会先注册 CodePagesEncodingProvider，此处再访问。
    private static readonly Encoding? Gb2312 = TryGetGb2312();

    // 记忆化整串首字母结果，避免每次按键对每个条目重复计算。条目名稳定，命中率高。
    private const int MaxInitialsCacheEntries = 4096;
    private static readonly ConcurrentDictionary<string, string> InitialsCache = new();

    private static Encoding? TryGetGb2312()
    {
        try { return Encoding.GetEncoding("GB2312"); }
        catch { return null; }
    }

    private static void TrimInitialsCacheIfNeeded()
    {
        // 托盘长驻时剪贴板预览/最近路径会不断进来；超上限整表清空，重算成本低
        if (InitialsCache.Count > MaxInitialsCacheEntries)
            InitialsCache.Clear();
    }

    public static char? GetInitial(char ch)
    {
        // ASCII letters
        if (ch is >= 'a' and <= 'z') return char.ToUpper(ch);
        if (ch is >= 'A' and <= 'Z') return ch;
        if (ch is >= '0' and <= '9') return ch;

        if (Gb2312 == null) return null;

        // 单字符转 GB2312，使用栈缓冲避免堆分配
        Span<char> chars = stackalloc char[1] { ch };
        Span<byte> bytes = stackalloc byte[4];
        int count;
        try
        {
            count = Gb2312.GetBytes(chars, bytes);
        }
        catch
        {
            return null;
        }

        if (count != 2) return null;

        int code = bytes[0] * 256 + bytes[1];

        // Outside first-level range
        if (code < 0xB0A1 || code > 0xD7F9) return null;

        for (int i = Sections.Length - 1; i >= 0; i--)
        {
            if (code >= Sections[i].Code)
                return Sections[i].Initial;
        }

        return null;
    }

    public static string GetInitials(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        TrimInitialsCacheIfNeeded();
        return InitialsCache.GetOrAdd(text, ComputeInitials);
    }

    private static string ComputeInitials(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var initial = GetInitial(ch);
            if (initial.HasValue)
                sb.Append(initial.Value);
        }
        return sb.ToString();
    }

    public static bool MatchesPinyin(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;

        // Direct substring match (case-insensitive)
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        // Pinyin initials match
        var initials = GetInitials(text);
        return initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
