namespace Quickstart.Core;

using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// 系统剪贴板文本历史：监听变化、去重入队、可选落盘、纯文本写回剪贴板。
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart");

    private static readonly string HistoryPath = Path.Combine(AppDataDir, "clipboard-history.json");

    private readonly ConfigManager _configManager;
    private readonly object _lock = new();
    private readonly List<ClipboardHistoryItem> _items = [];
    private readonly System.Threading.Timer _persistTimer;
    private ClipboardListenerWindow? _listener;
    private bool _suppressSelfWrite;
    private int _captureSuppressCount;
    private bool _persistPending;
    private bool _disposed;
    private Control? _uiSync;

    public event Action? Changed;

    public ClipboardHistoryService(ConfigManager configManager)
    {
        _configManager = configManager;
        _persistTimer = new System.Threading.Timer(_ => FlushPersist(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(Control uiSync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _uiSync = uiSync;

        // 监听窗口必须在 UI 线程创建；落盘加载放到线程池，避免启动路径同步读大 JSON
        if (_listener == null)
        {
            _listener = new ClipboardListenerWindow(OnClipboardChanged);
            _listener.CreateHandle();
        }

        _ = Task.Run(() =>
        {
            try
            {
                LoadFromDiskIfNeeded();
                ApplyMaxItems();
                RaiseChanged();
            }
            catch
            {
                // 启动加载失败不影响监听
            }
        });
    }

    public IReadOnlyList<ClipboardHistoryItem> GetItems()
    {
        lock (_lock)
            return _items.ToList();
    }

    public void Clear()
    {
        lock (_lock)
            _items.Clear();
        RaiseChanged();
        SchedulePersist();
        if (!(_configManager.Config.ClipboardHistory?.Persist ?? true))
            TryDeleteHistoryFile();
        else
            FlushPersist();
    }

    public bool Remove(string id)
    {
        var removed = false;
        lock (_lock)
        {
            var idx = _items.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _items.RemoveAt(idx);
                removed = true;
            }
        }

        if (!removed)
            return false;

        RaiseChanged();
        SchedulePersist();
        return true;
    }

    public void ApplyConfigLimits()
    {
        ApplyMaxItems();
        RaiseChanged();
        SchedulePersist();
    }

    /// <summary>
    /// 左滑捕获选区期间调用：忽略剪贴板变化，避免与 CaptureFromSourceAsync
    /// 的 Clear / Ctrl+C / Restore 争用剪贴板导致读空。
    /// </summary>
    public void BeginCaptureSuppress() => Interlocked.Increment(ref _captureSuppressCount);

    public void EndCaptureSuppress()
    {
        // 延迟解除：Restore 后可能还有一两次 WM_CLIPBOARDUPDATE
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(500); } catch { /* ignore */ }
            finally
            {
                if (Interlocked.Decrement(ref _captureSuppressCount) < 0)
                    Interlocked.Exchange(ref _captureSuppressCount, 0);
            }
        });
    }

    /// <summary>再次写入纯文本剪贴板，并记入历史（会触发监听，去重后置顶）。</summary>
    public async Task CopyPlainTextAsync(string text, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 先主动入队，保证 UI 立即刷新；再写系统剪贴板
        PushText(text, fromClipboardListener: false);

        _suppressSelfWrite = true;
        try
        {
            await SetClipboardPlainTextAsync(text, token);
        }
        finally
        {
            // 短暂抑制，避免立刻又读回同一内容重复处理
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400, CancellationToken.None);
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _suppressSelfWrite = false;
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        FlushPersist();
        _persistTimer.Dispose();
        _listener?.DestroyHandle();
        _listener = null;
    }

    private void OnClipboardChanged()
    {
        if (_disposed)
            return;

        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Enabled)
            return;
        if (_suppressSelfWrite || Volatile.Read(ref _captureSuppressCount) > 0)
            return;

        // 延迟一帧，等剪贴板 owner 写完
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80);
                if (Volatile.Read(ref _captureSuppressCount) > 0)
                    return;
                var text = await ReadClipboardPlainTextAsync(CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                PushText(text, fromClipboardListener: true);
            }
            catch
            {
                // 剪贴板被占用等，忽略本次
            }
        });
    }

    private void PushText(string rawText, bool fromClipboardListener)
    {
        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Enabled && fromClipboardListener)
            return;

        var text = NormalizeText(rawText, cfg.MaxTextLength);
        if (string.IsNullOrEmpty(text))
            return;

        lock (_lock)
        {
            if (cfg.IgnoreDuplicates
                && _items.Count > 0
                && string.Equals(_items[0].Text, text, StringComparison.Ordinal))
            {
                _items[0].CopiedAt = DateTime.Now;
            }
            else
            {
                // 同内容已在列表中：移到队首
                var existing = _items.FindIndex(i => string.Equals(i.Text, text, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    var item = _items[existing];
                    _items.RemoveAt(existing);
                    item.CopiedAt = DateTime.Now;
                    _items.Insert(0, item);
                }
                else
                {
                    _items.Insert(0, new ClipboardHistoryItem
                    {
                        Text = text,
                        CopiedAt = DateTime.Now
                    });
                }
            }

            TrimToMaxLocked(cfg.MaxItems);
        }

        RaiseChanged();
        SchedulePersist();
    }

    private static string NormalizeText(string raw, int maxLen)
    {
        var text = raw.Replace("\0", string.Empty);
        // 去掉仅空白
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (maxLen > 0 && text.Length > maxLen)
            text = text[..maxLen];

        return text;
    }

    private void ApplyMaxItems()
    {
        var max = _configManager.Config.ClipboardHistory?.MaxItems ?? 50;
        lock (_lock)
            TrimToMaxLocked(max);
    }

    private void TrimToMaxLocked(int max)
    {
        max = Math.Clamp(max, 5, 200);
        while (_items.Count > max)
            _items.RemoveAt(_items.Count - 1);
    }

    private void RaiseChanged()
    {
        var sync = _uiSync;
        if (sync is { IsDisposed: false, IsHandleCreated: true })
        {
            try
            {
                if (sync.InvokeRequired)
                    sync.BeginInvoke(() => Changed?.Invoke());
                else
                    Changed?.Invoke();
                return;
            }
            catch
            {
                // handle 可能正在销毁
            }
        }

        Changed?.Invoke();
    }

    private void LoadFromDiskIfNeeded()
    {
        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Persist || !File.Exists(HistoryPath))
            return;

        try
        {
            var json = File.ReadAllText(HistoryPath);
            var file = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.ClipboardHistoryFile);
            if (file?.Items == null || file.Items.Count == 0)
                return;

            // 先在锁外规范化磁盘项
            var diskItems = new List<ClipboardHistoryItem>();
            foreach (var item in file.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;
                item.Text = NormalizeText(item.Text, cfg.MaxTextLength);
                if (string.IsNullOrEmpty(item.Text))
                    continue;
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = Guid.NewGuid().ToString("N")[..8];
                diskItems.Add(item);
            }

            if (diskItems.Count == 0)
                return;

            lock (_lock)
            {
                // 以磁盘项为底，启动后已入队的内存新项置顶（与首条剪贴板事件竞态安全）
                var liveItems = _items.ToList();
                _items.Clear();
                _items.AddRange(diskItems);

                // 内存新项按时间倒序插到前面，并按文本去重
                for (var i = liveItems.Count - 1; i >= 0; i--)
                {
                    var live = liveItems[i];
                    var existing = _items.FindIndex(x =>
                        string.Equals(x.Text, live.Text, StringComparison.Ordinal));
                    if (existing >= 0)
                        _items.RemoveAt(existing);
                    _items.Insert(0, live);
                }

                TrimToMaxLocked(cfg.MaxItems);
            }
        }
        catch
        {
            // 损坏则忽略
        }
    }

    private void SchedulePersist()
    {
        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Persist)
        {
            TryDeleteHistoryFile();
            return;
        }

        _persistPending = true;
        _persistTimer.Change(800, Timeout.Infinite);
    }

    private void FlushPersist()
    {
        if (!_persistPending)
            return;
        _persistPending = false;

        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Persist)
        {
            TryDeleteHistoryFile();
            return;
        }

        List<ClipboardHistoryItem> snapshot;
        lock (_lock)
            snapshot = _items.Select(CloneItem).ToList();

        try
        {
            Directory.CreateDirectory(AppDataDir);
            var file = new ClipboardHistoryFile { Items = snapshot };
            var json = JsonSerializer.Serialize(file, AppConfigJsonContext.Default.ClipboardHistoryFile);
            var temp = HistoryPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, HistoryPath, overwrite: true);
            File.Delete(temp);
        }
        catch
        {
            // 写盘失败不打断主流程
        }
    }

    private static ClipboardHistoryItem CloneItem(ClipboardHistoryItem src)
        => new()
        {
            Id = src.Id,
            Text = src.Text,
            CopiedAt = src.CopiedAt
        };

    private static void TryDeleteHistoryFile()
    {
        try
        {
            if (File.Exists(HistoryPath))
                File.Delete(HistoryPath);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<string?> ReadClipboardPlainTextAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(ReadClipboardPlainTextCore());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Quickstart ClipboardHistory Read"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await tcs.Task.WaitAsync(token);
    }

    private static string? ReadClipboardPlainTextCore()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    && !Clipboard.ContainsText(TextDataFormat.Text))
                    return null;

                var text = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : Clipboard.GetText(TextDataFormat.Text);
                return text;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40 * (attempt + 1));
            }
        }

        return null;
    }

    private static async Task SetClipboardPlainTextAsync(string text, CancellationToken token)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return;
                }

                await SetClipboardOnStaThreadAsync(text, token);
                return;
            }
            catch (ExternalException)
            {
                await Task.Delay(40 * (attempt + 1), token);
            }
        }

        throw new InvalidOperationException("无法写入剪贴板，请稍后重试。");
    }

    private static Task SetClipboardOnStaThreadAsync(string text, CancellationToken token)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Quickstart ClipboardHistory Write"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.WaitAsync(token);
    }

    private sealed class ClipboardListenerWindow : NativeWindow
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private readonly Action _onChanged;

        public ClipboardListenerWindow(Action onChanged)
        {
            _onChanged = onChanged;
        }

        public void CreateHandle()
        {
            CreateHandle(new CreateParams
            {
                Caption = "QuickstartClipboardListener",
                ClassName = null,
                Parent = IntPtr.Zero,
                Style = 0
            });

            if (Handle != IntPtr.Zero)
                AddClipboardFormatListener(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                try
                {
                    _onChanged();
                }
                catch
                {
                    // ignore listener errors
                }
            }

            base.WndProc(ref m);
        }

        public new void DestroyHandle()
        {
            if (Handle != IntPtr.Zero)
                RemoveClipboardFormatListener(Handle);
            base.DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
