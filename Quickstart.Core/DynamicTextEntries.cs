namespace Quickstart.Core;

using Quickstart.Models;

/// <summary>
/// 文本分类下的内置动态条目（不落盘、不可编辑删除），内容随系统时间刷新。
/// </summary>
public static class DynamicTextEntries
{
    /// <summary>今天日期条目的固定 Id，不进入 config.json。</summary>
    public const string TodayDateId = "__dynamic_today_date__";

    public static bool IsDynamic(string? id)
        => string.Equals(id, TodayDateId, StringComparison.Ordinal);

    public static bool IsDynamic(QuickEntry? entry)
        => entry != null && IsDynamic(entry.Id);

    /// <summary>yyyy年M月d日（月日不补零），例如 2026年8月1日。</summary>
    public static string FormatTodayDate(DateTime? now = null)
        => (now ?? DateTime.Now).ToString("yyyy年M月d日");

    public static QuickEntry CreateTodayDateEntry(DateTime? now = null)
    {
        var text = FormatTodayDate(now);
        return new QuickEntry
        {
            Id = TodayDateId,
            Name = text,
            Path = text,
            Type = EntryType.Text,
            Group = string.Empty,
            SortOrder = int.MinValue,
            AddedAt = DateTime.MinValue,
            LastUsedAt = DateTime.MinValue
        };
    }

    /// <summary>复制时取实时内容，避免列表缓存跨日仍是旧日期。</summary>
    public static string ResolveContent(QuickEntry entry)
        => IsDynamic(entry) ? FormatTodayDate() : entry.Path;

    /// <summary>搜索时除日期串外，也匹配「今天」「日期」等别名。</summary>
    public static bool MatchesSearch(string? query, DateTime? now = null)
    {
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q))
            return true;

        var date = FormatTodayDate(now);
        if (date.Contains(q, StringComparison.OrdinalIgnoreCase)
            || q.Contains(date, StringComparison.OrdinalIgnoreCase))
            return true;

        // 固定别名：子串或整词
        ReadOnlySpan<string> aliases = ["今天日期", "今天", "今日", "日期", "date", "today"];
        foreach (var alias in aliases)
        {
            if (alias.Contains(q, StringComparison.OrdinalIgnoreCase)
                || q.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
