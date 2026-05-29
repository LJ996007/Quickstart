namespace Quickstart.Models;

public enum EntryType
{
    Folder,
    File,
    Url,
    Text
}

public sealed class QuickEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public EntryType Type { get; set; } = EntryType.Folder;
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; }

    /// <summary>网页条目的自定义图标文件路径（位于应用数据目录），为空则使用自动抓取的网站图标。</summary>
    public string? CustomIconPath { get; set; }
}
