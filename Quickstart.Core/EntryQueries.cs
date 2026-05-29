namespace Quickstart.Core;

using Quickstart.Models;
using Quickstart.Utils;

/// <summary>
/// 跨平台共享的条目查询逻辑：按类型筛选、分组名排序、搜索过滤与匹配质量排序。
/// 与 Windows 端 MainPopup 的行为保持一致，供 Avalonia 端复用，避免双份实现。
/// </summary>
public static class EntryQueries
{
    public const string AllGroupsLabel = "全部";

    public static string NormalizeGroupName(string? group)
        => string.IsNullOrWhiteSpace(group) ? string.Empty : group.Trim();

    public static List<QuickEntry> ByType(IEnumerable<QuickEntry> entries, EntryType type)
        => entries.Where(e => e.Type == type).ToList();

    /// <summary>当前类型下的分组名（非空），按首个条目的 SortOrder→出现顺序→名称排序。</summary>
    public static List<string> OrderedGroupNames(IReadOnlyList<QuickEntry> typeEntries)
        => typeEntries
            .Select((entry, index) => new { entry, index, group = NormalizeGroupName(entry.Group) })
            .Where(item => !string.IsNullOrEmpty(item.group))
            .GroupBy(item => item.group, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Name = g.First().group,
                FirstSortOrder = g.Min(i => i.entry.SortOrder),
                FirstIndex = g.Min(i => i.index)
            })
            .OrderBy(x => x.FirstSortOrder)
            .ThenBy(x => x.FirstIndex)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Name)
            .ToList();

    /// <summary>匹配质量：名称前缀(0) &lt; 名称子串(1) &lt; 路径子串(2) &lt; 仅拼音(3)。</summary>
    public static int MatchRank(QuickEntry entry, string query)
    {
        var name = entry.Name ?? string.Empty;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (!string.IsNullOrEmpty(entry.Path) && entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    /// <summary>按当前分组与搜索词过滤并排序。</summary>
    public static List<QuickEntry> FilterAndSort(IReadOnlyList<QuickEntry> typeEntries, string activeGroup, string? query)
    {
        IEnumerable<QuickEntry> scoped = string.Equals(activeGroup, AllGroupsLabel, StringComparison.OrdinalIgnoreCase)
            ? typeEntries
            : typeEntries.Where(e => string.Equals(NormalizeGroupName(e.Group), activeGroup, StringComparison.OrdinalIgnoreCase));

        var q = query?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(q))
        {
            return scoped
                .Where(e => PinyinHelper.MatchesPinyin(e.Name, q) || PinyinHelper.MatchesPinyin(e.Path, q))
                .OrderBy(e => MatchRank(e, q))
                .ThenBy(e => e.SortOrder)
                .ToList();
        }

        return scoped.OrderBy(e => e.SortOrder).ToList();
    }
}
