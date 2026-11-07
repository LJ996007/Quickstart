namespace Quickstart.Core;

using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// 系统剪贴板历史：文本、文件和图片的监听、去重、持久化与写回。
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    private static string AppDataDir => AppPaths.Root;
    private static string HistoryPath => AppPaths.ClipboardHistoryPath;

    private readonly ConfigManager _configManager;
    private readonly object _lock = new();
    private readonly List<ClipboardHistoryItem> _items = [];
    private readonly System.Threading.Timer _persistTimer;
    private ClipboardListenerWindow? _listener;
    private uint _lastWrittenSequence;
    private int _readRevision;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _persistLock = new();
    private int _clearRevision;
    private int _captureSuppressCount;
    private volatile bool _persistPending;
    private volatile bool _disposed;
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
        // Also retain a screenshot/file copied before the application was launched.
        OnClipboardChanged();
    }

    public IReadOnlyList<ClipboardHistoryItem> GetItems()
    {
        lock (_lock)
            return _items.ToList();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _clearRevision++;
            Interlocked.Increment(ref _readRevision);
            _items.Clear();
        }
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
        if (!string.IsNullOrEmpty(text)) await CopyItemAsync(new() { Text = text }, token);
    }

    public async Task CopyItemAsync(ClipboardHistoryItem item, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        item = CloneItem(item);
        await _writeGate.WaitAsync(token);
        try
        {
            await ClipboardHistoryContent.RunAsync(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!ClipboardHistoryContent.PreparePreview(item)) throw new InvalidDataException("图片无法读取或尺寸超出限制。");
                ClipboardHistoryContent.Write(item);
                Volatile.Write(ref _lastWrittenSequence, GetClipboardSequenceNumber());
                return true;
            }, token);
            PushItem(item, fromClipboardListener: false);
        }
        finally { _writeGate.Release(); }
    }

    /// <returns>True when Ctrl+V was sent; false when only copied.</returns>
    public async Task<bool> DeliverItemAsync(ClipboardHistoryItem item, bool paste, IntPtr targetWindow)
    {
        if (!paste) { await CopyItemAsync(item); return false; }
        return await PlainTextPasteService.PasteContentAsync(
            ct => CopyItemAsync(item, ct), targetWindow);
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
        if (Volatile.Read(ref _captureSuppressCount) > 0)
            return;

        var revision = Interlocked.Increment(ref _readRevision);

        // 延迟一帧，等剪贴板 owner 写完
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80);
                await _readGate.WaitAsync();
                try
                {
                    if (_disposed || revision != Volatile.Read(ref _readRevision)
                        || Volatile.Read(ref _captureSuppressCount) > 0) return;
                    var sequence = GetClipboardSequenceNumber();
                    if (sequence == Volatile.Read(ref _lastWrittenSequence)) return;
                    var item = await ClipboardHistoryContent.RunAsync(() =>
                    {
                        var data = Clipboard.GetDataObject();
                        return data == null ? null : ClipboardHistoryContent.Read(data);
                    }, CancellationToken.None);
                    if (!_disposed && revision == Volatile.Read(ref _readRevision)
                        && Volatile.Read(ref _captureSuppressCount) == 0)
                    {
                        // Reading a delayed format may itself advance the Windows sequence
                        // without another update message. Retry the now materialized data;
                        // otherwise a perfectly valid copy could silently disappear.
                        if (sequence != GetClipboardSequenceNumber()) OnClipboardChanged();
                        else if (item != null) PushItem(item, fromClipboardListener: true);
                    }
                }
                finally { _readGate.Release(); }
            }
            catch
            {
                // 剪贴板被占用等，忽略本次
            }
        });
    }

    private void PushItem(ClipboardHistoryItem incoming, bool fromClipboardListener)
    {
        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (_disposed || (!cfg.Enabled && fromClipboardListener))
            return;

        incoming = CloneItem(incoming);
        if (!NormalizeItem(incoming, cfg.MaxTextLength)) return;

        lock (_lock)
        {
            if (cfg.IgnoreDuplicates
                && _items.Count > 0
                && _items[0].HasSameContent(incoming))
            {
                _items[0].CopiedAt = DateTime.Now;
            }
            else
            {
                // 同内容已在列表中：移到队首
                var existing = _items.FindIndex(i => i.HasSameContent(incoming));
                if (existing >= 0)
                {
                    var item = _items[existing];
                    _items.RemoveAt(existing);
                    item.CopiedAt = DateTime.Now;
                    _items.Insert(0, item);
                }
                else
                {
                    incoming.CopiedAt = DateTime.Now;
                    _items.Insert(0, incoming);
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

    private static bool NormalizeItem(ClipboardHistoryItem item, int maxTextLength)
    {
        switch (item.Kind)
        {
            case ClipboardHistoryKind.Text:
                item.Text = NormalizeText(item.Text ?? "", maxTextLength);
                item.FilePaths = []; item.ImagePng = [];
                return item.Text.Length > 0;
            case ClipboardHistoryKind.Files:
                item.FilePaths = (item.FilePaths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)
                    && Path.IsPathFullyQualified(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                item.Text = ""; item.ImagePng = [];
                return item.FilePaths.Length > 0;
            case ClipboardHistoryKind.Image:
                item.Text = ""; item.FilePaths = [];
                return item.ImagePng is { Length: > 0 and <= ClipboardHistoryContent.MaxImageBytes }
                    && item.ImageWidth > 0 && item.ImageHeight > 0
                    && (long)item.ImageWidth * item.ImageHeight <= ClipboardHistoryContent.MaxImagePixels;
            default: return false;
        }
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
        // Bound PNG payloads independently of the item count (JSON uses base64).
        long imageBytes = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            imageBytes += _items[i].ImagePng.Length;
            if (imageBytes > 64L * 1024 * 1024) { _items.RemoveRange(i, _items.Count - i); break; }
        }
    }

    private void RaiseChanged()
    {
        if (_disposed) return;
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
        int clearRevision;
        lock (_lock) clearRevision = _clearRevision;
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
                if (!NormalizeItem(item, cfg.MaxTextLength) || !ClipboardHistoryContent.PreparePreview(item)) continue;
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = Guid.NewGuid().ToString("N")[..8];
                diskItems.Add(item);
            }

            if (diskItems.Count == 0)
                return;

            lock (_lock)
            {
                if (_disposed || clearRevision != _clearRevision) return;
                // 以磁盘项为底，启动后已入队的内存新项置顶（与首条剪贴板事件竞态安全）
                var liveItems = _items.ToList();
                _items.Clear();
                _items.AddRange(diskItems);

                // 内存新项按时间倒序插到前面，并按类型及内容去重
                for (var i = liveItems.Count - 1; i >= 0; i--)
                {
                    var live = liveItems[i];
                    var existing = _items.FindIndex(x => x.HasSameContent(live));
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
        if (_disposed) return;
        var cfg = _configManager.Config.ClipboardHistory ?? new ClipboardHistoryConfig();
        if (!cfg.Persist)
        {
            TryDeleteHistoryFile();
            return;
        }

        _persistPending = true;
        try { _persistTimer.Change(800, Timeout.Infinite); }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private void FlushPersist()
    {
        lock (_persistLock) FlushPersistCore();
    }

    private void FlushPersistCore()
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
            Kind = src.Kind,
            FilePaths = (src.FilePaths ?? []).ToArray(),
            // PNG snapshots are immutable after capture; sharing avoids large per-refresh copies.
            ImagePng = src.ImagePng ?? [],
            ThumbnailPng = src.ThumbnailPng ?? [],
            ImageWidth = src.ImageWidth,
            ImageHeight = src.ImageHeight,
            CopiedAt = src.CopiedAt
        };

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private void TryDeleteHistoryFile()
    {
        lock (_persistLock)
        {
            try
            {
                if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            }
            catch { /* ignore */ }
        }
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
