namespace Quickstart.Core;

internal sealed record WindowsRecentItem(
    string Name,
    string DisplayPath,
    string LaunchPath,
    bool IsDirectory,
    DateTime LastUsedAt);

internal static class WindowsRecentItemsService
{
    private const int MaxItems = 20;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private static readonly object CacheLock = new();
    private static IReadOnlyList<WindowsRecentItem> _cachedItems = [];
    private static DateTime _cacheTimeUtc = DateTime.MinValue;

    public static IReadOnlyList<WindowsRecentItem> GetItems()
    {
        lock (CacheLock)
        {
            if (DateTime.UtcNow - _cacheTimeUtc < CacheDuration)
                return _cachedItems;

            _cachedItems = LoadItems();
            _cacheTimeUtc = DateTime.UtcNow;
            return _cachedItems;
        }
    }

    private static IReadOnlyList<WindowsRecentItem> LoadItems()
    {
        var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrWhiteSpace(recentFolder) || !Directory.Exists(recentFolder))
            return [];

        try
        {
            // 不解析 .lnk 的 COM 目标：内置 COM 与单文件裁剪不兼容，会导致发布版
            // 返回空记录甚至损坏路径。Windows Shell 可以直接打开快捷方式本身。
            return new DirectoryInfo(recentFolder)
                .EnumerateFiles("*.lnk", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .DistinctBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxItems)
                .Select(shortcut => new WindowsRecentItem(
                    Path.GetFileNameWithoutExtension(shortcut.Name),
                    shortcut.FullName,
                    shortcut.FullName,
                    IsDirectory: false,
                    shortcut.LastWriteTime))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
