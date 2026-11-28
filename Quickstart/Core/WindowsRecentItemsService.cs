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
    private static IReadOnlyList<WindowsRecentItem> _cachedItems = [];
    private static DateTime _cacheTimeUtc = DateTime.MinValue;
    private static bool _refreshing;
    private static bool _refreshAgain;
    public static event Action? Changed;

    // Both popup views use this snapshot on the WinForms thread. Only the worker
    // touches the Recent directory, which may be on a redirected user profile.
    public static IReadOnlyList<WindowsRecentItem> GetItems()
    {
        if (!_refreshing && DateTime.UtcNow - _cacheTimeUtc >= CacheDuration)
            Refresh();
        return _cachedItems;
    }

    internal static void Refresh()
    {
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        _ = RefreshAsync();
    }

    private static async Task RefreshAsync()
    {
        try
        {
            do
            {
                _refreshAgain = false;
                var items = await Task.Run(LoadItems);
                _cacheTimeUtc = DateTime.UtcNow;
                if (_cachedItems.SequenceEqual(items)) continue;
                _cachedItems = items;
                Changed?.Invoke();
            } while (_refreshAgain);
        }
        finally { _refreshing = false; }
    }

    private static IReadOnlyList<WindowsRecentItem> LoadItems()
    {
        try
        {
            var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrWhiteSpace(recentFolder)) return [];
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
