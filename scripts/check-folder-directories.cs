// Run: dotnet run --file scripts/check-folder-directories.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.UI;

static class FolderChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread]
    static void Main()
    {
        var root = Path.GetFullPath("artifacts/folder-directory-checks"); Directory.CreateDirectory(root);
        AppPaths.SetRootForTests(root);
        using var config = new ConfigManager();
        config.Config.TrackOpusRecentFolders = false;
        var fixture = Path.Combine(root, "favorites.ofv");
        File.WriteAllText(fixture, """
            <favorites><folder label="分组"><path label="中文 #100%"><dir><pathstring>C:\资料 #100%</pathstring></dir></path>
            <path label="重复"><dir><pathstring>c:\资料 #100%\</pathstring></dir></path></folder>
            <path label="根目录"><dir><pathstring>C:\</pathstring></dir></path>
            <path label="命令"><command>Go C:\never-execute</command></path>
            <path><dir><pathstring>relative</pathstring></dir></path></favorites>
            """);
        config.Config.OpusFavoritesFile = fixture;
        config.Config.Entries.Add(new QuickEntry { Name = "本地重名", Path = @"C:\资料 #100%", Type = EntryType.Folder });
        config.Config.Entries.Add(new QuickEntry { Name = "本地独有", Path = root, Type = EntryType.Folder, LastUsedAt = DateTime.Now.AddDays(-1) });
        config.Config.Entries.Add(new QuickEntry { Name = "非目录", Path = fixture, Type = EntryType.File });
        Check(FolderDirectoryService.GetFavorites(config.Config, out var error).Count == 3 && error == "", "Merge, nesting, dedup, command filtering");
        config.Config.FolderFavoriteSource = FolderFavoriteSource.DirectoryOpus;
        Check(FolderDirectoryService.GetFavorites(config.Config, out _).Count == 2, "Opus-only source");
        config.Config.FolderFavoriteSource = FolderFavoriteSource.Quickstart;
        Check(FolderDirectoryService.GetFavorites(config.Config, out _).Count == 2, "Independent source and file filtering");
        config.RecordRecentFolder(root);
        config.RecordRecentFolder(@"C:\资料 #100%");
        config.RecordRecentFolder(root.ToUpperInvariant() + "\\");
        var recent = FolderDirectoryService.GetRecent(config.Config);
        Check(recent.Count == 2 && recent[0].Path.Equals(root, StringComparison.OrdinalIgnoreCase), "MRU promotion, case and trailing separator dedup");
        Check(!recent.Any(e => e.Path == fixture), "Files never appear as recent folders");
        using (var editor = new FolderFavoritesForm(config)) { }
        Check(config.Config.Entries.Count == 3, "Opening/canceling editor does not mutate favorites");
        var independentPath = Path.Combine(root, "独立收藏"); Directory.CreateDirectory(independentPath);
        using (var editor = new FolderFavoritesForm(config))
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            ((System.Windows.Forms.TextBox)typeof(FolderFavoritesForm).GetField("_path", flags)!.GetValue(editor)!).Text = independentPath;
            ((System.Windows.Forms.TextBox)typeof(FolderFavoritesForm).GetField("_name", flags)!.GetValue(editor)!).Text = "独立设置";
            typeof(FolderFavoritesForm).GetMethod("EditEntry", flags)!.Invoke(editor, [false]);
            typeof(FolderFavoritesForm).GetMethod("Save", flags)!.Invoke(editor, null);
        }
        Check(config.Config.Entries.Count == 4 && config.Config.Entries.Any(e => e.Type == EntryType.File && e.Path == fixture)
            && config.Config.Entries.Any(e => e.Path == independentPath && e.Name == "独立设置"), "Independent editor saves folders and preserves non-folder entries");
        for (var i = 0; i < 50; i++) config.RecordRecentFolder(Path.Combine(root, "folder" + i));
        Check(config.Config.RecentFolders.Count == 40, "History bounded at 40");
        config.FlushPendingSave();
        using var restored = new ConfigManager(); restored.Load();
        Check(restored.Config.RecentFolders.Count == 40 && restored.Config.OpusFavoritesFile == fixture, "Settings/history survive reload");
        File.WriteAllText(fixture, "<!DOCTYPE favorites [<!ENTITY x SYSTEM 'file:///C:/private'>]><favorites>&x;</favorites>");
        Check(FolderDirectoryService.ReadOpusFolders(fixture, out error).Count == 0 && error.Length > 0, "DTD/entity expansion rejected");
        Check(FolderDirectoryService.ReadOpusFolders(fixture + ".missing", out error).Count == 0 && error.Length > 0, "Missing source reported");
        var actual = FolderDirectoryService.ReadOpusFolders(FolderDirectoryService.DefaultOpusFavoritesFile, out error);
        Console.WriteLine($"Local Opus read: {actual.Count} folders, success={error.Length == 0}");
        var runtime = Path.Combine(Path.GetDirectoryName(DopusDetector.Detect()) ?? "", "dopusrt.exe");
        if (File.Exists(runtime))
        {
            var active = Task.Run(() => OpusRecentFolderTracker.ReadActivePathAsync(runtime)).GetAwaiter().GetResult();
            Check(active.Length == 0 || Directory.Exists(active), "Actual Opus active source tab");
            Console.WriteLine(active.Length > 0 ? "PASS read-only Opus current-directory export." : "Opus has no active directory window; export returned empty.");
        }
        Console.WriteLine("PASS sources, XML safety, MRU ordering/dedup/limit, config persistence, editor cancellation.");
    }
}
