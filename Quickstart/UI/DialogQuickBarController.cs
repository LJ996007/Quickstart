namespace Quickstart.UI;

using Quickstart.Core;

/// <summary>Tracks foreground file dialogs without taking their focus or indexing the desktop.</summary>
public sealed class DialogQuickBarController : IDisposable
{
    private readonly ConfigManager _config;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly CancellationTokenSource _lifetime = new();
    private DialogQuickBar _bar;
    private readonly Control _dispatcher = new();
    private FileDialogWindowEvents? _windowEvents;
    private bool _wakePending;
    private Task<FileDialogTarget?>? _probe;
    private IntPtr _probeSource, _lastSource, _lastExternalSource, _dismissed;
    private readonly DialogProbeBackoff _backoff = new();
    private bool _ticking, _disposed;
    public FileDialogTarget? CurrentTarget { get; private set; }
    public DialogQuickBar Bar => _bar;
    public Action<string>? Diagnostic { get; set; }

    public DialogQuickBarController(ConfigManager config)
    {
        _config = config; _bar = CreateBar();
        _ = _dispatcher.Handle;
        _timer.Tick += async (_, _) => await TickAsync();
    }
    private DialogQuickBar CreateBar()
    {
        var bar = new DialogQuickBar(_config);
        bar.Dismissed += () => _dismissed = CurrentTarget?.Window ?? IntPtr.Zero;
        return bar;
    }
    public void Start()
    {
        if (_windowEvents == null)
        {
            _windowEvents = new FileDialogWindowEvents();
            _windowEvents.Changed += Wake;
        }
        _timer.Start(); Wake();
    }
    private void Wake()
    {
        if (_disposed || _wakePending) return;
        _wakePending = true;
        try
        {
            _dispatcher.BeginInvoke(async () =>
            {
                _wakePending = false;
                await TickAsync();
            });
        }
        catch (InvalidOperationException) { _wakePending = false; }
    }
    private void EnsureBar()
    {
        if (!_bar.IsDisposed && !_bar.Disposing) return;
        _bar = CreateBar(); CurrentTarget = null; _dismissed = IntPtr.Zero;
    }

    public async Task<FileDialogTarget?> ResolveAsync(IntPtr source)
    {
        if (!_config.Config.FileDialogNavigationEnabled || _disposed) return null;
        var resolved = FileDialogNavigator.ResolveDialogWindow(source);
        if (CurrentTarget is { } target && FileDialogNavigator.IsLiveWindow(target)
            && (resolved == target.Window || (FileDialogNavigator.IsOwnWindow(source) && _lastExternalSource == target.Window))) return target;
        if (!FileDialogNavigator.IsPossibleDialog(resolved)) return null;
        if (_probe is { IsCompleted: false })
        {
            var pendingSource = _probeSource;
            var pending = await _probe;
            if (pendingSource == resolved) return pending;
        }
        _probeSource = resolved;
        Diagnostic?.Invoke($"Probe started: hwnd={resolved}");
        _probe = FileDialogNavigator.DetectAsync(resolved, _lifetime.Token, Diagnostic);
        return await _probe;
    }

    public async Task<bool> ShowSearchAsync(string query, IntPtr source)
    {
        var target = await ResolveAsync(source);
        if (_disposed || target == null) return false;
        EnsureBar();
        CurrentTarget = target; _dismissed = IntPtr.Zero;
        _bar.Bind(target); _bar.ShowAttached(focus: true, query: query);
        return true;
    }

    private async Task TickAsync()
    {
        if (_disposed || _ticking) return;
        _ticking = true;
        try
        {
            EnsureBar();
            if (!_config.Config.FileDialogNavigationEnabled) { _bar.Hide(); CurrentTarget = null; return; }
            if (_bar.IsManaging) return;
            if (CurrentTarget != null && !FileDialogNavigator.IsLiveWindow(CurrentTarget))
            { CurrentTarget = null; _dismissed = IntPtr.Zero; _bar.Hide(); }
            var source = FileDialogNavigator.ResolveDialogWindow(FileDialogNavigator.CaptureForegroundWindow());
            // ContainsFocus is thread-local and can remain true after another app is activated.
            // Only the actual foreground window may keep the companion visible.
            if ((_bar.IsHandleCreated && source == _bar.Handle)
                || (_bar.IsNavigating && CurrentTarget?.Window == source)) { _bar.Reposition(); return; }
            if (FileDialogNavigator.IsOwnWindow(source)) { _bar.Hide(); return; }
            _lastExternalSource = source;
            if (CurrentTarget is { } target && source == target.Window)
            {
                if (_dismissed != source) { _bar.Bind(target); _bar.ShowAttached(); }
                return;
            }
            _bar.Hide();
            if (source != _lastSource)
            {
                _lastSource = source; _dismissed = IntPtr.Zero;
                _backoff.RemoveClosed((window, pid) => FileDialogNavigator.IsLiveWindow(new(window, pid, false, false, "")));
            }
            var sourcePid = FileDialogNavigator.GetProcessId(source);
            if (!_backoff.CanProbe(source, sourcePid, Environment.TickCount64) || !FileDialogNavigator.IsPossibleDialog(source)) return;
            FileDialogTarget? detected;
            try { detected = await ResolveAsync(source); }
            catch
            {
                _backoff.Failed(source, sourcePid, Environment.TickCount64, providerFailure: true);
                throw;
            }
            if (detected == null) _backoff.Failed(source, sourcePid, Environment.TickCount64);
            else _backoff.Succeeded(source, sourcePid);
            Diagnostic?.Invoke(detected == null ? "Probe: no file dialog" : "Probe: file dialog detected");
            if (_disposed || detected == null || !_config.Config.FileDialogNavigationEnabled) return;
            // Detection may finish after the user switches applications.
            if (FileDialogNavigator.ResolveDialogWindow(FileDialogNavigator.CaptureForegroundWindow()) != detected.Window) return;
            CurrentTarget = detected;
            if (_dismissed != detected.Window) { _bar.Bind(detected); _bar.ShowAttached(); }
        }
        catch (Exception ex) { Diagnostic?.Invoke($"Probe failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { _ticking = false; }
    }
    public void Dispose()
    {
        _disposed = true; _windowEvents?.Dispose(); _timer.Stop(); _timer.Dispose(); _lifetime.Cancel(); _bar.Dispose(); _dispatcher.Dispose(); _lifetime.Dispose();
    }
}
