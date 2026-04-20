namespace Quickstart.Core;

using Quickstart.Models;

public enum OpenWith
{
    TotalCommander,
    Explorer,
    DirectoryOpus,
    LastUsed
}

public sealed class AppConfig
{
    public List<QuickEntry> Entries { get; set; } = [];
    public Dictionary<string, DateTime> GroupLastUsedAt { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TotalCommanderPath { get; set; } = string.Empty;
    public string DirectoryOpusPath { get; set; } = string.Empty;
    public OpenWith DefaultOpenWith { get; set; } = OpenWith.TotalCommander;
    public bool StartWithWindows { get; set; }
    public bool ShellMenuEnabled { get; set; }
    public string HotKey { get; set; } = string.Empty;
}
