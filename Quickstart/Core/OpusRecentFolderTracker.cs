namespace Quickstart.Core;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Sample only the active Opus source tab, via its documented read-only /info API.</summary>
public sealed class OpusRecentFolderTracker(ConfigManager config) : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private bool _busy, _disposed;
    private string _previous = "";
    public void Start() { _timer.Tick += Tick; _timer.Start(); }
    private async void Tick(object? sender, EventArgs e)
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            var foreground = FileDialogNavigator.CaptureForegroundWindow();
            GetWindowThreadProcessId(foreground, out var pid);
            using var process = Process.GetProcessById((int)pid);
            if (!config.Config.TrackOpusRecentFolders || !process.ProcessName.Equals("dopus", StringComparison.OrdinalIgnoreCase))
            { _previous = ""; return; }
            var exe = string.IsNullOrWhiteSpace(config.Config.DirectoryOpusPath) ? DopusDetector.Detect() : config.Config.DirectoryOpusPath;
            var rt = Path.Combine(Path.GetDirectoryName(exe) ?? "", "dopusrt.exe");
            if (!File.Exists(rt)) return;
            var path = await Task.Run(() => ReadActivePathAsync(rt));
            if (_disposed || !config.Config.TrackOpusRecentFolders || FileDialogNavigator.CaptureForegroundWindow() != foreground) return;
            if (path.Length > 0 && !path.Equals(_previous, StringComparison.OrdinalIgnoreCase))
            { config.RecordRecentFolder(path); _previous = path; }
        }
        catch { /* Opus may close or be inaccessible while sampling. */ }
        finally { _busy = false; }
    }
    public static async Task<string> ReadActivePathAsync(string runtime)
    {
        var file = Path.Combine(Path.GetTempPath(), "quickstart-opus-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var start = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("/info"); start.ArgumentList.Add(file + ",paths");
            using var child = Process.Start(start);
            if (child == null) return "";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { child.Kill(); } catch { } return ""; }
            for (var i = 0; i < 5 && !File.Exists(file); i++) await Task.Delay(50);
            if (!File.Exists(file)) return "";
            var active = FolderDirectoryService.ReadXml(file).Descendants("path")
                .FirstOrDefault(p => (string?)p.Attribute("active_lister") == "1" && (string?)p.Attribute("tab_state") == "1");
            return FolderDirectoryService.NormalizePath(active?.Value);
        }
        catch { return ""; }
        finally { try { File.Delete(file); } catch { } }
    }
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Dispose(); }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
