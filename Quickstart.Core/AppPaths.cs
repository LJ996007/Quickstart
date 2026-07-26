namespace Quickstart.Core;

/// <summary>
/// 跨平台应用数据目录。Windows 使用 LocalApplicationData；
/// macOS 使用 ~/Library/Application Support/Quickstart（与原生惯例一致）。
/// </summary>
public static class AppPaths
{
    private static readonly object Sync = new();
    private static string? _root;
    private static bool _migrationAttempted;

    /// <summary>应用数据根目录（自动创建；首次访问时完成 macOS 旧路径迁移）。</summary>
    public static string Root
    {
        get
        {
            EnsureInitialized();
            return _root!;
        }
    }

    public static string ConfigPath => Path.Combine(Root, "config.json");
    public static string ConfigBackupPath => Path.Combine(Root, "config.json.bak");
    public static string SecretsPath => Path.Combine(Root, "ai-secrets.local.json");
    public static string ClipboardHistoryPath => Path.Combine(Root, "clipboard-history.json");
    public static string FaviconsDir => Path.Combine(Root, "favicons");
    public static string CustomIconsDir => Path.Combine(Root, "custom-icons");

    /// <summary>
    /// 解析并创建根目录；macOS 上若目标为空而旧路径（LocalApplicationData）有数据，则迁移一次。
    /// 应在 Load 配置前调用（进程内幂等）。
    /// </summary>
    public static void EnsureInitialized()
    {
        EnsureResolved();
        Directory.CreateDirectory(_root!);
        TryMigrateLegacyMacData();
    }

    // Root 属性会调用本方法；EnsureResolved 仅解析字符串，避免递归。

    /// <summary>测试或特殊场景可覆盖根目录（传入 null 恢复默认）。</summary>
    public static void SetRootForTests(string? root)
    {
        lock (Sync)
        {
            _root = root;
            _migrationAttempted = root != null;
        }
    }

    private static void EnsureResolved()
    {
        if (_root != null)
            return;

        lock (Sync)
        {
            _root ??= ResolveDefaultRoot();
        }
    }

    private static string ResolveDefaultRoot()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            return Path.Combine(home, "Library", "Application Support", "Quickstart");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quickstart");
    }

    /// <summary>
    /// .NET 在 macOS 上 LocalApplicationData 常为 ~/.local/share；
    /// 早期 Avalonia 版可能已把配置写在那里，迁移到 Application Support。
    /// </summary>
    private static void TryMigrateLegacyMacData()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        lock (Sync)
        {
            if (_migrationAttempted)
                return;
            _migrationAttempted = true;
        }

        try
        {
            var target = _root!;
            Directory.CreateDirectory(target);

            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quickstart");

            if (string.IsNullOrWhiteSpace(legacy)
                || !Directory.Exists(legacy)
                || PathsEqual(legacy, target))
            {
                return;
            }

            // 目标已有 config 则不覆盖，避免冲掉新数据
            var targetConfig = Path.Combine(target, "config.json");
            var legacyConfig = Path.Combine(legacy, "config.json");
            if (File.Exists(targetConfig) || !File.Exists(legacyConfig))
            {
                // 仍尝试补齐 secrets / history / caches
                CopyIfMissing(Path.Combine(legacy, "ai-secrets.local.json"), Path.Combine(target, "ai-secrets.local.json"));
                CopyIfMissing(Path.Combine(legacy, "clipboard-history.json"), Path.Combine(target, "clipboard-history.json"));
                CopyDirectoryMerge(Path.Combine(legacy, "favicons"), Path.Combine(target, "favicons"));
                CopyDirectoryMerge(Path.Combine(legacy, "favicons-mac"), Path.Combine(target, "favicons"));
                CopyDirectoryMerge(Path.Combine(legacy, "custom-icons"), Path.Combine(target, "custom-icons"));
                return;
            }

            foreach (var name in new[]
                     {
                         "config.json", "config.json.bak", "ai-secrets.local.json", "clipboard-history.json"
                     })
            {
                CopyIfMissing(Path.Combine(legacy, name), Path.Combine(target, name));
            }

            CopyDirectoryMerge(Path.Combine(legacy, "favicons"), Path.Combine(target, "favicons"));
            CopyDirectoryMerge(Path.Combine(legacy, "favicons-mac"), Path.Combine(target, "favicons"));
            CopyDirectoryMerge(Path.Combine(legacy, "custom-icons"), Path.Combine(target, "custom-icons"));
        }
        catch
        {
            // 迁移失败不阻断启动；用户仍可手动拷贝
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            var fullA = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullB = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CopyIfMissing(string source, string dest)
    {
        if (!File.Exists(source) || File.Exists(dest))
            return;
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Copy(source, dest, overwrite: false);
    }

    private static void CopyDirectoryMerge(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            return;
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Copy(file, dest, overwrite: false);
        }
    }
}
