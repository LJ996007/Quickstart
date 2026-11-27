namespace Quickstart.UI;

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Quickstart.Core;
using Quickstart.Utils;

internal sealed class RadialFolderBrowserPopup : Form
{
    private sealed record FolderView(string Folder, string Query, bool IncludeSubfolders, FileSearchPage? Page,
        int TopIndex, string? Selection);

    private readonly FileSearchIconButton _back = new(FileSearchGlyph.Back, "返回（Alt+左箭头）");
    private readonly Button _breadcrumb = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    private readonly Panel _dragGrip = new() { Cursor = Cursors.SizeAll, AccessibleName = "拖动窗口" };
    private readonly TextBox _query = new() { PlaceholderText = "查找文件或文件夹", AccessibleName = "查找文件或文件夹" };
    private readonly CheckBox _includeSubfolders = new() { Appearance = Appearance.Button, Text = "含子目录", TextAlign = ContentAlignment.MiddleCenter };
    private readonly ListView _results = new()
    {
        View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, ShowItemToolTips = true, AccessibleName = "目录内容"
    };
    private readonly Button _previous = new() { Text = "上一页" };
    private readonly Button _next = new() { Text = "下一页" };
    private readonly System.Windows.Forms.Timer _previewDelay = new() { Interval = 280 };
    private readonly Label _status = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TableLayoutPanel _layout = new();
    private readonly TableLayoutPanel _navigation = new();
    private readonly TableLayoutPanel _search = new();
    private readonly TableLayoutPanel _footer = new();
    private readonly ContextMenuStrip _ancestors = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 180 };
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<FolderView> _history = [];
    private CancellationTokenSource? _read;
    private FileSearchPage? _page;
    private string _root = "";
    private string _rootName = "";
    private string _folder = "";
    private bool _changing;
    private bool _opening;
    private Font? _displayFont;
    private int _displayDpi;
    private FileSearchResult? _previewPending;
    private FileSearchResult? _previewed;
    private FilePreviewPopup? _previewPopup;
    private Point _anchor;
    private Point? _dragOffset;
    private readonly ConfigManager _config;

    public event Action? ReturnToWheel;
    public event Action<FileSearchResult>? OpenItem;

    public RadialFolderBrowserPopup(ConfigManager config)
    {
        _config = config;
        Text = "目录浏览";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Border;
        Padding = new Padding(1);
        ForeColor = UiTheme.TextPrimary;
        FormStyler.ApplyRounded(this);

        foreach (var button in new[] { _breadcrumb, _previous, _next })
            UiTheme.StyleFlatButton(button, UiTheme.Surface);
        _breadcrumb.ForeColor = UiTheme.AccentText;
        _includeSubfolders.FlatStyle = FlatStyle.Flat;
        _includeSubfolders.FlatAppearance.BorderColor = UiTheme.Border;
        _includeSubfolders.FlatAppearance.CheckedBackColor = UiTheme.SelectionFill;
        _includeSubfolders.AccessibleName = "搜索子目录";
        _status.ForeColor = UiTheme.TextSecondary;
        _results.BackColor = UiTheme.Card;
        _results.ForeColor = UiTheme.TextPrimary;
        _results.Columns.Add("名称");
        _results.Columns.Add("相对位置");
        _results.Columns.Add("展开", 0, HorizontalAlignment.Center);
        _results.SmallImageList = _icons;
        ConfigureMenu();
        _results.Dock = DockStyle.Fill;

        ConfigureRow(_navigation, _back, _breadcrumb, _dragGrip);
        _navigation.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute);
        _navigation.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        ConfigureRow(_search, new FileSearchField(_query, search: true), _includeSubfolders);
        ConfigureRow(_footer, _status, _previous, _next);

        _layout.Dock = DockStyle.Fill;
        _layout.BackColor = UiTheme.Surface;
        _layout.ColumnCount = 1;
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute));
        foreach (var control in new Control[] { _navigation, _search, _results, _footer })
        {
            control.Dock = DockStyle.Fill;
            _layout.Controls.Add(control, 0, _layout.Controls.Count);
        }
        Controls.Add(_layout);

        _back.Click += (_, _) => GoBack();
        _breadcrumb.Click += (_, _) => ShowAncestors();
        _dragGrip.Paint += PaintDragGrip;
        _dragGrip.MouseDown += BeginDrag;
        _dragGrip.MouseMove += MoveDrag;
        _dragGrip.MouseUp += EndDrag;
        _dragGrip.MouseCaptureChanged += (_, _) => { if (!_dragGrip.Capture) _dragOffset = null; };
        _query.TextChanged += (_, _) => ScheduleSearch();
        _includeSubfolders.CheckedChanged += (_, _) => ScheduleSearch();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = LoadAsync(); };
        _previous.Click += (_, _) => _ = LoadAsync(_page!.Offset - EverythingIpcProtocol.PageSize);
        _next.Click += (_, _) => _ = LoadAsync(_page!.Offset + EverythingIpcProtocol.PageSize);
        _results.ClientSizeChanged += (_, _) => ResizeColumns();
        _results.MouseDown += OnResultMouseDown;
        _results.MouseUp += OnResultClick;
        _results.MouseDoubleClick += OnResultDoubleClick;
        _previewDelay.Tick += (_, _) => FlushPreview();
        Deactivate += OnBrowserDeactivate;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    private void OnBrowserDeactivate(object? sender, EventArgs e)
    {
        if (_opening || _ancestors.Visible || _menu.Visible) return;
        if (Bounds.Contains(Cursor.Position)) return;
        // 旁边的预览窗拿了激活（WebView2 初始化偶尔会）或光标在它上面时不关目录列表。
        if (_previewPopup is { Visible: true } preview &&
            (preview.Bounds.Contains(Cursor.Position) || preview.IsForegroundOrOwnsForeground())) return;
        Hide();
    }

    private void ConfigureMenu()
    {
        UiTheme.StyleMenu(_menu);
        AddMenu("打开（Enter）", () => { if (Selected is { } result) Open(result); });
        AddMenu("打开所在位置（Ctrl+Enter）", LocateSelected);
        AddMenu("复制文件（Ctrl+C）", CopySelectedFile);
        AddMenu("复制完整路径（Ctrl+Shift+C）", CopySelectedPath);
        _menu.Opening += (_, e) => OnMenuOpening(e);
        _results.ContextMenuStrip = _menu;
    }

    private void OnMenuOpening(CancelEventArgs e)
    {
        e.Cancel = Selected == null;
        if (e.Cancel) return;
        var folder = Selected!.IsFolder;
        _menu.Items[0].Text = folder ? "进入文件夹（Enter）" : "打开（Enter）";
        _menu.Items[2].Text = folder ? "复制文件夹（Ctrl+C）" : "复制文件（Ctrl+C）";
    }

    private void AddMenu(string title, Action action) =>
        _menu.Items.Add(title, null, (_, _) => RunAction(action));

    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private FileSearchResult? Selected => _results.SelectedItems.Count > 0
        ? _results.SelectedItems[0].Tag as FileSearchResult
        : null;

    private void OnResultMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        // 右键先选中光标下的条目，避免误操作上一次选中的文件。
        _results.SelectedItems.Clear();
        if (_results.GetItemAt(e.X, e.Y) is { } item) item.Selected = true;
    }

    private void CopySelectedFile()
    {
        if (Selected is not { } result) return;
        var files = new StringCollection { result.FullPath };
        Clipboard.SetFileDropList(files);
        _status.Text = result.IsFolder ? "已复制文件夹，可粘贴到资源管理器" : "已复制文件，可粘贴到资源管理器";
    }

    private void CopySelectedPath()
    {
        if (Selected is not { } result) return;
        Clipboard.SetText(result.FullPath);
        _status.Text = "已复制完整路径";
    }

    private void LocateSelected()
    {
        if (Selected is { } result) Reveal(result);
    }

    private void Reveal(FileSearchResult result)
    {
        // Windows 文件名不含引号，explorer 的 /select 只接受一个带引号的参数。
        using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.FullPath}\"") { UseShellExecute = true });
    }

    private static void ConfigureRow(TableLayoutPanel row, params Control[] controls)
    {
        row.ColumnCount = controls.Length;
        row.RowCount = 1;
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < controls.Length; i++)
        {
            if (i > 0) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute));
            controls[i].Dock = DockStyle.Fill;
            controls[i].Margin = Padding.Empty;
            row.Controls.Add(controls[i], i, 0);
        }
    }

    public void ShowFolder(string folder, string title, Rectangle anchor)
    {
        _history.Clear();
        _root = folder;
        string? pathError = null;
        try
        {
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            pathError = ex.Message;
        }
        _rootName = title;
        SetFolder(_root);
        var screen = Screen.FromPoint(anchor.Location);
        UiScaleHelper.MoveIntoWorkingArea(this, screen);
        ApplyMetrics(screen, anchor.Location);
        _opening = true;
        try
        {
            Show();
            WindowActivator.TryForceForeground(Handle);
            _query.Focus();
        }
        finally { _opening = false; }
        if (pathError == null)
        {
            _config.RecordFolderBrowserUse(_folder);
            _ = LoadAsync();
        }
        else ClearResults($"无法读取目录：{pathError}");
    }

    public void RefreshDisplay()
    {
        if (Visible)
            ApplyMetrics(Screen.FromHandle(Handle), _anchor);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible)
            BeginInvoke(() => { if (Visible && !IsDisposed) RefreshDisplay(); });
    }

    private void ApplyMetrics(Screen screen, Point anchor)
    {
        var dpi = UiScaleHelper.GetDpiForScreen(screen);
        int S(int value) => UiScaleHelper.Scale(value, dpi);
        var area = screen.WorkingArea;
        _anchor = anchor;
        var size = PopupScreenLayout.Fit(new Size(S(430), S(526)), area.Size);
        _layout.SuspendLayout();
        if (_displayDpi != dpi)
        {
            var previousFont = _displayFont;
            _displayFont = new Font(UiTheme.FontFamily, 12f * dpi / 96f, GraphicsUnit.Pixel);
            Font = _displayFont;
            _menu.Font = _displayFont;
            previousFont?.Dispose();
            _displayDpi = dpi;
        }
        _layout.Padding = new Padding(S(8));
        _layout.RowStyles[0].Height = S(34);
        _layout.RowStyles[1].Height = S(40);
        _layout.RowStyles[3].Height = S(30);
        _navigation.ColumnStyles[0].Width = S(34);
        _navigation.ColumnStyles[2].Width = S(34);
        _search.ColumnStyles[1].Width = S(82);
        _includeSubfolders.Margin = new Padding(S(6), 0, 0, S(5));
        _search.Controls[0].Margin = new Padding(0, 0, 0, S(5));
        _footer.ColumnStyles[1].Width = S(60);
        _footer.ColumnStyles[2].Width = S(60);
        var iconSize = Math.Clamp(S(22), 16, 96);
        if (_icons.ImageSize.Width != iconSize)
        {
            _icons.ImageSize = new Size(iconSize, iconSize);
            _icons.Images.Clear();
            foreach (ListViewItem row in _results.Items)
                row.ImageKey = EnsureResultIcon((FileSearchResult)row.Tag!);
        }
        var location = _dragOffset.HasValue ? Location : new Point(
            Math.Clamp(anchor.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(anchor.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
        Bounds = new Rectangle(location, size);
        _layout.ResumeLayout(performLayout: true);
        _previewPopup?.FollowOwner(Bounds);
        ResizeColumns();
    }

    private void PaintDragGrip(object? sender, PaintEventArgs e)
    {
        var dpi = _displayDpi == 0 ? DeviceDpi : _displayDpi;
        var step = UiScaleHelper.Scale(4, dpi);
        var dot = UiScaleHelper.Scale(2, dpi);
        var x = (_dragGrip.Width - dot - step) / 2;
        var y = (_dragGrip.Height - dot - step * 2) / 2;
        using var brush = new SolidBrush(UiTheme.TextSecondary);
        for (var row = 0; row < 3; row++)
        {
            e.Graphics.FillEllipse(brush, x, y, dot, dot);
            e.Graphics.FillEllipse(brush, x + step, y, dot, dot);
            y += step;
        }
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var origin = _dragGrip.PointToScreen(e.Location);
        _dragOffset = new Point(origin.X - Left, origin.Y - Top);
        _dragGrip.Capture = true;
    }

    private void MoveDrag(object? sender, MouseEventArgs e)
    {
        if (_dragOffset is not { } offset) return;
        var cursor = _dragGrip.PointToScreen(e.Location);
        Location = new Point(cursor.X - offset.X, cursor.Y - offset.Y);
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragOffset.HasValue) return;
        _dragOffset = null;
        _dragGrip.Capture = false;
        _anchor = Location;
        RefreshDisplay();
    }

    private void ResizeColumns()
    {
        var width = _results.ClientSize.Width;
        if (width <= 0) return;
        var arrow = Math.Min(width / 3, UiScaleHelper.Scale(_results, 68));
        var relative = IncludeSubfolders ? (int)(width * .32) : 0;
        _results.Columns[0].Width = Math.Max(0, width - arrow - relative);
        _results.Columns[1].Width = relative;
        _results.Columns[2].Width = arrow;
    }

    private bool IncludeSubfolders => _includeSubfolders.Checked && !string.IsNullOrWhiteSpace(_query.Text);

    private void ScheduleSearch()
    {
        if (_changing) return;
        CancelRead();
        _includeSubfolders.Enabled = !string.IsNullOrWhiteSpace(_query.Text);
        ClearResults("正在读取…");
        _debounce.Start();
    }

    private void CancelRead()
    {
        _debounce.Stop();
        var pending = _read;
        _read = null;
        pending?.Cancel();
    }

    private async Task LoadAsync(uint offset = 0, FolderView? restore = null)
    {
        CancelRead();
        ClearResults("正在读取…");
        var request = new CancellationTokenSource();
        _read = request;
        try
        {
            var page = await LocalFolderBrowser.ReadAsync(_folder, _query.Text.Trim(), 0, offset, 1, request.Token,
                selectedPath: restore?.Selection, includeSubfolders: IncludeSubfolders, lastUsed: _config.GetFolderBrowserUsage());
            if (ReferenceEquals(_read, request) && Visible)
            {
                ShowPage(page);
                if (restore != null && _results.Items.Count > 0)
                {
                    _results.TopItem = _results.Items[Math.Min(restore.TopIndex, _results.Items.Count - 1)];
                    var selected = _results.Items.Cast<ListViewItem>().FirstOrDefault(row =>
                        string.Equals(((FileSearchResult)row.Tag!).FullPath, restore.Selection, StringComparison.OrdinalIgnoreCase));
                    if (selected != null) selected.Selected = true;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_read, request) && Visible)
                ClearResults($"无法读取目录：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_read, request)) _read = null;
            request.Dispose();
        }
    }

    private void ClearResults(string status)
    {
        _page = null;
        _results.Items.Clear();
        _previous.Visible = _next.Visible = false;
        _status.Text = status;
    }

    private void ShowPage(FileSearchPage page)
    {
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            foreach (var item in page.Items)
            {
                var parent = Path.GetDirectoryName(item.FullPath);
                var relative = IncludeSubfolders && parent != null ? Path.GetRelativePath(_folder, parent) : "";
                var row = new ListViewItem([item.Name, relative == "." ? "" : relative, item.IsFolder ? "›" : ""],
                    EnsureResultIcon(item))
                {
                    Tag = item, ToolTipText = item.FullPath, UseItemStyleForSubItems = false
                };
                row.SubItems[1].ForeColor = UiTheme.TextSecondary;
                row.SubItems[2].ForeColor = UiTheme.AccentText;
                _results.Items.Add(row);
            }
        }
        finally { _results.EndUpdate(); }
        _page = page;
        _previous.Visible = _next.Visible = page.Total > EverythingIpcProtocol.PageSize;
        _previous.Enabled = page.Offset > 0;
        _next.Enabled = page.Offset + (uint)page.Items.Count < page.Total;
        _status.Text = page.Total == 0
            ? (string.IsNullOrWhiteSpace(_query.Text) ? "此文件夹为空" : "未找到匹配项")
            : (_next.Visible ? $"{page.Offset + 1}–{page.Offset + page.Items.Count} / {page.Total}" : "");
        ResizeColumns();
    }

    private string EnsureResultIcon(FileSearchResult item)
    {
        var extension = Path.GetExtension(item.FullPath).ToLowerInvariant();
        var key = item.IsFolder ? "folder" : "file" + extension;
        if (!_icons.Images.ContainsKey(key))
        {
            var useLargeIcon = _icons.ImageSize.Width > 24;
            var icon = item.IsFolder
                ? IconExtractor.GetIcon("folder", isDirectory: true, useLargeIcon)
                : IconExtractor.GetGenericTypeIcon(extension, useLargeIcon);
            _icons.Images.Add(key, icon ?? SystemIcons.Application);
        }
        return key;
    }

    private void SetFolder(string path, string query = "", bool includeSubfolders = false)
    {
        CancelRead();
        _folder = path;
        _changing = true;
        _query.Text = query;
        _includeSubfolders.Checked = includeSubfolders;
        _includeSubfolders.Enabled = !string.IsNullOrWhiteSpace(query);
        _changing = false;
        _breadcrumb.Text = string.Join("  ›  ", Ancestors().Select(DisplayName));
        _breadcrumb.AccessibleName = $"{_breadcrumb.Text}，选择上级目录";
        HidePreview();
        ClearResults("");
    }

    private List<string> Ancestors()
    {
        var paths = new List<string> { _folder };
        while (!string.Equals(paths[^1], _root, StringComparison.OrdinalIgnoreCase) &&
               Path.GetDirectoryName(paths[^1]) is { } parent)
            paths.Add(parent);
        paths.Reverse();
        return paths;
    }

    private string DisplayName(string path) =>
        string.Equals(path, _root, StringComparison.OrdinalIgnoreCase) ? _rootName : Path.GetFileName(path);

    private void ShowAncestors()
    {
        _ancestors.Items.Clear();
        var paths = Ancestors();
        if (paths.Count < 2) return;
        foreach (var path in paths.Take(paths.Count - 1))
        {
            var item = _ancestors.Items.Add(DisplayName(path));
            item.Click += (_, _) =>
            {
                var index = _history.FindLastIndex(view => string.Equals(view.Folder, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) Restore(index);
                else EnterFolder(path);
            };
        }
        _ancestors.Show(_breadcrumb, new Point(0, _breadcrumb.Height));
    }

    private void EnterFolder(string path)
    {
        _history.Add(new FolderView(_folder, _query.Text, IncludeSubfolders, _page,
            _results.TopItem?.Index ?? 0, _results.SelectedItems.Count > 0
                ? ((FileSearchResult)_results.SelectedItems[0].Tag!).FullPath : null));
        _config.RecordFolderBrowserUse(path);
        SetFolder(path);
        _ = LoadAsync();
    }

    private void Restore(int index)
    {
        var view = _history[index];
        _history.RemoveRange(index, _history.Count - index);
        SetFolder(view.Folder, view.Query, view.IncludeSubfolders);
        _ = LoadAsync(view.Page?.Offset ?? 0, view);
    }

    private void GoBack()
    {
        if (_history.Count > 0) Restore(_history.Count - 1);
        else { Hide(); ReturnToWheel?.Invoke(); }
    }

    private void OnResultClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        var hit = _results.HitTest(e.Location);
        if (hit.Item?.Tag is not FileSearchResult result) return;
        var arrow = hit.SubItem == hit.Item.SubItems[2];
        if (!arrow && hit.SubItem != hit.Item.SubItems[0]) return;
        ActivateResult(result, arrow, reveal: Control.ModifierKeys.HasFlag(Keys.Control), open: false);
    }

    private void OnResultDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _previewDelay.Stop();
        _previewPending = null;
        if (_results.HitTest(e.Location).Item?.Tag is not FileSearchResult result) return;        ActivateResult(result, arrow: false, reveal: Control.ModifierKeys.HasFlag(Keys.Control), open: true);
    }

    private enum ResultClick { Preview, OpenFile, OpenFolder, EnterFolder, Reveal }

    /// <summary>单击文件预览、双击用默认程序打开、Ctrl+单击定位所在文件夹。</summary>
    private static ResultClick ResolveClick(bool isFolder, bool arrow, bool reveal, bool open)
    {
        if (reveal) return ResultClick.Reveal;
        if (isFolder) return arrow ? ResultClick.EnterFolder : ResultClick.OpenFolder;
        return open ? ResultClick.OpenFile : ResultClick.Preview;
    }

    private void ActivateResult(FileSearchResult result, bool arrow, bool reveal, bool open)
    {
        switch (ResolveClick(result.IsFolder, arrow, reveal, open))
        {
            case ResultClick.Reveal: Reveal(result); break;
            case ResultClick.EnterFolder: EnterFolder(result.FullPath); break;
            case ResultClick.OpenFolder:
            case ResultClick.OpenFile: Open(result); break;
            default:
                _previewPending = result;
                _previewDelay.Start();
                break;
        }
    }

    private void FlushPreview()
    {
        _previewDelay.Stop();
        if (_previewPending is not { } result) return;
        _previewPending = null;
        if (!OfficePreviewDocument.IsSupported(result.FullPath))
        {
            _status.Text = "此类型暂不支持预览，双击或用默认程序打开";
            return;
        }
        if (NewFileSize(result.FullPath) is long size && size > OfficePreviewDocument.MaxFileBytes)
        {
            _status.Text = "文件超过 64 MB，双击用默认程序打开";
            return;
        }
        _previewed = result;
        if (_previewPopup is null || _previewPopup.IsDisposed)
        {
            _previewPopup = new FilePreviewPopup { OwnerBounds = () => Bounds };
            _previewPopup.OpenRequested += () => { if (_previewed is { } file) Open(file); };
            // 在预览里按 Esc 或者点到两个窗口之外，整组关闭。
            _previewPopup.Dismissed += () => { HidePreview(); Hide(); };
        }
        _previewPopup.ShowFile(result.FullPath, Bounds);
    }

    private static long? NewFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private void HidePreview()
    {
        _previewDelay.Stop();
        _previewPending = null;
        _previewPopup?.HideUnlessPinned();
    }

    private void Open(FileSearchResult result)
    {
        _config.RecordFolderBrowserUse(result.FullPath);
        Hide();
        OpenItem?.Invoke(result);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { GoBack(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _query.Focus(); _query.SelectAll(); return true; }
        if (_results.Focused && keyData == (Keys.Control | Keys.Shift | Keys.C)) { RunAction(CopySelectedPath); return true; }
        if (_results.Focused && keyData == (Keys.Control | Keys.C)) { RunAction(CopySelectedFile); return true; }
        if (_query.Focused && keyData == Keys.Down && _results.Items.Count > 0)
        {
            _results.Items[0].Selected = true;
            _results.Focus();
            return true;
        }
        if ((_query.Focused || _results.Focused) && keyData is Keys.Enter or (Keys.Control | Keys.Enter))
        {
            var row = _results.SelectedItems.Count > 0 ? _results.SelectedItems[0]
                : (_results.Items.Count > 0 ? _results.Items[0] : null);
            if (row?.Tag is FileSearchResult result)
            {
                if (result.IsFolder && keyData == Keys.Enter) EnterFolder(result.FullPath);
                else Open(result);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible && !Disposing && !IsDisposed)
        {
            CancelRead();
            HidePreview();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelRead();
            _debounce.Dispose();
            _previewDelay.Dispose();
            _previewPopup?.Dispose();
            _ancestors.Dispose();
            _menu.Dispose();
            _results.SmallImageList = null;
            _icons.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _displayFont?.Dispose();
            _displayFont = null;
        }
    }
}
