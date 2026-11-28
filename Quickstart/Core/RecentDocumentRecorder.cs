namespace Quickstart.Core;

using System.Runtime.InteropServices;
using Quickstart.Utils;

/// <summary>Register displayed documents with the Windows Recent folder used by both launchers.</summary>
internal sealed class RecentDocumentRecorder : IDisposable
{
    private const uint ShardPathW = 3;
    private readonly Control _dispatcher;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 100 };
    private AsyncIconLoader? _shellWorker;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public RecentDocumentRecorder(Control dispatcher)
    {
        _dispatcher = dispatcher;
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            WindowsRecentItemsService.Refresh();
        };
    }

    public void Record(string path)
    {
        // A null path passed to SHAddToRecentDocs would clear the system history.
        if (_disposed || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        _shellWorker ??= new AsyncIconLoader("Quickstart.RecentDocuments");
        _shellWorker.Enqueue(() =>
        {
            // Explorer writes the shortcut after the native call returns. Subscribe
            // before submitting the path, including on the very first preview.
            _watcher ??= WatchRecentFolder();
            SHAddToRecentDocs(ShardPathW, path);
            if (_watcher == null) _dispatcher.BeginInvoke(() => ScheduleRefresh(this, EventArgs.Empty));
        });
    }

    private FileSystemWatcher? WatchRecentFolder()
    {
        FileSystemWatcher? watcher = null;
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            watcher = new FileSystemWatcher(folder, "*.lnk")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                SynchronizingObject = _dispatcher
            };
            watcher.Created += ScheduleRefresh;
            watcher.Changed += ScheduleRefresh;
            watcher.Renamed += ScheduleRefresh;
            watcher.Deleted += ScheduleRefresh;
            watcher.Error += ScheduleRefresh;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    private void ScheduleRefresh(object? sender, EventArgs e)
    {
        if (!_disposed) _refreshTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Dispose();
        // Close the watcher on the same worker, after all submitted paths.
        _shellWorker?.Enqueue(() => _watcher?.Dispose());
        _shellWorker?.Dispose();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHAddToRecentDocs(uint flags, string path);
}
