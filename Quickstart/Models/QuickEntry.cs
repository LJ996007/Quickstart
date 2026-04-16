namespace Quickstart.Models;

public enum EntryType
{
    Folder,
    File
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
}
