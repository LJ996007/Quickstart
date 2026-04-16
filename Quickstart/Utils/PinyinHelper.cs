namespace Quickstart.Utils;

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

    public static char? GetInitial(char ch)
    {
        // ASCII letters
        if (ch is >= 'a' and <= 'z') return char.ToUpper(ch);
        if (ch is >= 'A' and <= 'Z') return ch;
        if (ch is >= '0' and <= '9') return ch;

        // Convert to GB2312
        byte[] bytes;
        try
        {
            var gb2312 = System.Text.Encoding.GetEncoding("GB2312");
            bytes = gb2312.GetBytes(ch.ToString());
        }
        catch
        {
            return null;
        }

        if (bytes.Length != 2) return null;

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
        var sb = new System.Text.StringBuilder(text.Length);
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
