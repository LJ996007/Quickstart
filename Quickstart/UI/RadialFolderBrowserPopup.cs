namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

internal sealed class RadialFolderBrowserPopup : Form
{
    private sealed record FolderView(string Folder, string Query, bool IncludeSubfolders, FileSearchPage? Page,
        int TopIndex, int Selection);

    private readonly FileSearchIconButton _back = new(FileSearchGlyph.Back, "返回（Alt+左箭头）");
    private readonly Button _breadcrumb = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    private readonly TextBox _query = new() { PlaceholderText = "查找文件或文件夹", AccessibleName = "查找文件或文件夹" };
    private readonly CheckBox _includeSubfolders = new() { Appearance = Appearance.Button, Text = "含子目录", TextAlign = ContentAlignment.MiddleCenter };
    private readonly ListView _results = new()
    {
        View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, ShowItemToolTips = true, AccessibleName = "目录内容"
    };
    private readonly Button _previous = new() { Text = "上一页" };
    private readonly Button _next = new() { Text = "下一页" };
    private readonly Label _status = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TableLayoutPanel _layout = new();
    private readonly TableLayoutPanel _navigation = new();
    private readonly TableLayoutPanel _search = new();
    private readonly TableLayoutPanel _footer = new();
    private readonly ContextMenuStrip _ancestors = new();
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

    public event Action? ReturnToWheel;
    public event Action<FileSearchResult>? OpenItem;

    public RadialFolderBrowserPopup()
    {
        Text = "目录浏览";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Border;
        Padding = new Padding(1);
        ForeColor = UiTheme.TextPrimary;

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
        _results.Columns.Add("展开");
        _results.SmallImageList = _icons;

        ConfigureRow(_navigation, _back, _breadcrumb);
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
        _query.TextChanged += (_, _) => ScheduleSearch();
        _includeSubfolders.CheckedChanged += (_, _) => ScheduleSearch();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = LoadAsync(); };
        _previous.Click += (_, _) => _ = LoadAsync(_page!.Offset - EverythingIpcProtocol.PageSize);
        _next.Click += (_, _) => _ = LoadAsync(_page!.Offset + EverythingIpcProtocol.PageSize);
        _results.ClientSizeChanged += (_, _) => ResizeColumns();
        _results.MouseUp += OnResultClick;
        Deactivate += (_, _) => { if (!_opening && !_ancestors.Visible) Hide(); };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
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
        if (pathError == null) _ = LoadAsync();
        else ClearResults($"无法读取目录：{pathError}");
    }

    public void RefreshDisplay()
    {
        if (Visible)
            ApplyMetrics(Screen.FromHandle(Handle), Location);
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
        var size = PopupScreenLayout.Fit(new Size(S(430), S(526)), area.Size);
        _layout.SuspendLayout();
        if (_displayDpi != dpi)
        {
            var previousFont = _displayFont;
            _displayFont = new Font(UiTheme.FontFamily, 12f * dpi / 96f, GraphicsUnit.Pixel);
            Font = _displayFont;
            previousFont?.Dispose();
            _displayDpi = dpi;
        }
        _layout.Padding = new Padding(S(8));
        _layout.RowStyles[0].Height = S(34);
        _layout.RowStyles[1].Height = S(40);
        _layout.RowStyles[3].Height = S(30);
        _navigation.ColumnStyles[0].Width = S(34);
        _search.ColumnStyles[1].Width = S(82);
        _includeSubfolders.Margin = new Padding(S(6), 0, 0, S(5));
        _search.Controls[0].Margin = new Padding(0, 0, 0, S(5));
        _footer.ColumnStyles[1].Width = S(60);
        _footer.ColumnStyles[2].Width = S(60);
        var iconSize = Math.Clamp(S(22), 16, 96);
        if (_icons.Images.Count == 0 || _icons.ImageSize.Width != iconSize)
        {
            _icons.ImageSize = new Size(iconSize, iconSize);
            _icons.Images.Clear();
            if (IconExtractor.GetIcon("folder", isDirectory: true, useLargeIcon: iconSize > 24) is { } folderIcon)
                _icons.Images.Add("folder", folderIcon);
            if (IconExtractor.GetGenericTypeIcon("", useLargeIcon: iconSize > 24) is { } fileIcon)
                _icons.Images.Add("file", fileIcon);
            foreach (ListViewItem row in _results.Items)
                row.ImageKey = ((FileSearchResult)row.Tag!).IsFolder ? "folder" : "file";
        }
        Bounds = new Rectangle(
            Math.Clamp(anchor.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(anchor.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)), size.Width, size.Height);
        _layout.ResumeLayout(performLayout: true);
        ResizeColumns();
    }

    private void ResizeColumns()
    {
        var width = _results.ClientSize.Width;
        if (width <= 0) return;
        var arrow = UiScaleHelper.Scale(_results, 26);
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

    private async Task LoadAsync(uint offset = 0)
    {
        CancelRead();
        ClearResults("正在读取…");
        var request = new CancellationTokenSource();
        _read = request;
        try
        {
            var page = await LocalFolderBrowser.ReadAsync(_folder, _query.Text.Trim(), 0, offset, 1, request.Token,
                includeSubfolders: IncludeSubfolders);
            if (ReferenceEquals(_read, request) && Visible)
                ShowPage(page);
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
                    item.IsFolder ? "folder" : "file")
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
            _results.TopItem?.Index ?? 0, _results.SelectedIndices.Count > 0 ? _results.SelectedIndices[0] : -1));
        SetFolder(path);
        _ = LoadAsync();
    }

    private void Restore(int index)
    {
        var view = _history[index];
        _history.RemoveRange(index, _history.Count - index);
        SetFolder(view.Folder, view.Query, view.IncludeSubfolders);
        if (view.Page == null)
        {
            _ = LoadAsync();
            return;
        }
        ShowPage(view.Page);
        if (view.Selection >= 0 && view.Selection < _results.Items.Count)
            _results.Items[view.Selection].Selected = true;
        if (view.TopIndex < _results.Items.Count)
            _results.TopItem = _results.Items[view.TopIndex];
    }

    private void GoBack()
    {
        if (_history.Count > 0) Restore(_history.Count - 1);
        else { Hide(); ReturnToWheel?.Invoke(); }
    }

    private void OnResultClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var hit = _results.HitTest(e.Location);
        if (hit.Item?.Tag is not FileSearchResult result) return;
        if (result.IsFolder && hit.SubItem == hit.Item.SubItems[2])
            EnterFolder(result.FullPath);
        else if (hit.SubItem == hit.Item.SubItems[0])
            Open(result);
    }

    private void Open(FileSearchResult result)
    {
        Hide();
        OpenItem?.Invoke(result);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { GoBack(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _query.Focus(); _query.SelectAll(); return true; }
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
        if (!Visible && !Disposing && !IsDisposed) CancelRead();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelRead();
            _debounce.Dispose();
            _ancestors.Dispose();
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
