namespace Quickstart.Core;

using System.Xml;
using System.Xml.Linq;
using Quickstart.Models;

public sealed record FolderDirectory(string Name, string Path);

/// <summary>Read-only Opus integration. Never executes favorite commands or writes Opus configuration.</summary>
public static class FolderDirectoryService
{
    public static string DefaultOpusFavoritesFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GPSoftware", "Directory Opus", "ConfigFiles", "favorites.ofv");
    public static string OpusRecentFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GPSoftware", "Directory Opus", "State Data", "recent.osd");

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return "";
        try
        {
            if (path.IndexOfAny(['\r', '\n', '\0', '"', '*', '?']) >= 0) return "";
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch { return ""; }
    }
    public static FolderDirectory FromPath(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return new(string.IsNullOrEmpty(name) ? path : name, path);
    }
    public static XDocument ReadXml(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    public static IReadOnlyList<FolderDirectory> ReadOpusFolders(string file, out string error)
    {
        error = "";
        try
        {
            return ReadXml(file).Descendants("path").Select(e =>
            {
                var path = NormalizePath(e.Element("dir")?.Element("pathstring")?.Value);
                var label = (string?)e.Attribute("label");
                return new FolderDirectory(string.IsNullOrWhiteSpace(label) ? FromPath(path).Name : label, path);
            }).Where(e => e.Path.Length > 0).DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) { error = "无法读取 Opus 目录：" + ex.Message; return []; }
    }
    public static IReadOnlyList<FolderDirectory> GetFavorites(AppConfig config, out string error)
    {
        error = "";
        var local = config.Entries.Where(e => e.Type == EntryType.Folder).OrderBy(e => e.SortOrder)
            .Select(e => new FolderDirectory(e.Name, NormalizePath(e.Path))).Where(e => e.Path.Length > 0);
        if (config.FolderFavoriteSource == FolderFavoriteSource.Quickstart) return local.ToList();
        var opus = ReadOpusFolders(string.IsNullOrWhiteSpace(config.OpusFavoritesFile) ? DefaultOpusFavoritesFile : config.OpusFavoritesFile, out error);
        return (config.FolderFavoriteSource == FolderFavoriteSource.DirectoryOpus ? opus : opus.Concat(local))
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static IReadOnlyList<FolderDirectory> GetRecent(AppConfig config)
    {
        var used = (config.RecentFolders ?? []).Select(p => (Path: p.Key, Time: p.Value.ToUniversalTime()))
            .Concat(config.Entries.Where(e => e.Type == EntryType.Folder && e.LastUsedAt != default)
                .Select(e => (Path: e.Path, Time: e.LastUsedAt.ToUniversalTime())))
            .OrderByDescending(e => e.Time).Select(e => NormalizePath(e.Path)).Where(p => p.Length > 0);
        // Opus's persisted list supplies earlier history; it does not contain per-folder timestamps.
        var opus = config.TrackOpusRecentFolders ? ReadOpusFolders(OpusRecentFile, out _).Select(e => e.Path) : [];
        return used.Concat(opus).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).Select(FromPath).ToList();
    }
}
