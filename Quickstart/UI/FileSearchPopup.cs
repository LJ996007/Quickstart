namespace Quickstart.UI;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class FileSearchPopup : Form
{
    private readonly ConfigManager _config;
    private readonly ProcessLauncher _launcher;
    private readonly EverythingSearchClient _client = new();
    private readonly TextBox _query = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索本机文件、文件夹（支持 Everything 搜索语法）" };
    private readonly ComboBox _filter = new FileSearchComboBox { AccessibleName = "文件类型" };
    private readonly ComboBox _sort = new FileSearchComboBox { AccessibleName = "排序方式" };
    private readonly ListView _results = new BufferedListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, ShowItemToolTips = true, BorderStyle = BorderStyle.None,
        OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = UiTheme.Card, ForeColor = UiTheme.TextPrimary };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _previous = new() { Text = "上一页", AutoSize = true, Enabled = false };
    private readonly Button _next = new() { Text = "下一页", AutoSize = true, Enabled = false };
    private readonly Button _external = new() { Text = "在 Everything 中打开", AutoSize = true };
    private readonly TableLayoutPanel _navigation = new() { Dock = DockStyle.Fill, ColumnCount = 4, Visible = false };
    private readonly Button _back = new FileSearchIconButton(FileSearchGlyph.Back, "返回（Alt+左箭头）");
    private readonly Button _up = new FileSearchIconButton(FileSearchGlyph.Up, "上一级（Alt+上箭头）");
    private readonly Button _returnToSearch = new() { Text = "搜索结果", AutoSize = true };
    private readonly TextBox _location = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "当前文件夹" };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(20, 20) };
    private readonly ContextMenuStrip _menu = new();
    private readonly OfficePreviewPane _preview = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.BorderSubtle };
    private readonly CheckBox _previewEnabled = new() { Text = "预览", Checked = true, Appearance = Appearance.Button,
        FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "文件预览" };
    private readonly TableLayoutPanel _layout = new() { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
    private readonly TableLayoutPanel _header = new() { Dock = DockStyle.Fill, ColumnCount = 3 };
    private readonly TableLayoutPanel _top = new() { Dock = DockStyle.Fill, ColumnCount = 4 };
    private readonly TableLayoutPanel _footer = new() { Dock = DockStyle.Fill, ColumnCount = 4 };
    private readonly Label _title = new() { Text = "本地搜索", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly FileSearchIconButton _maximize = new(FileSearchGlyph.Maximize, "最大化");
    private readonly FileSearchIconButton _close = new(FileSearchGlyph.Close, "关闭");
    private readonly ToolTip _toolTip = new();
    private bool _layoutReady;
    private bool _adjustingLayout;
    private readonly System.Windows.Forms.Timer _previewDelay = new() { Interval = 180 };
    private CancellationTokenSource? _searchCts;
    private int _revision;
    private uint _offset;
    private bool _changing;
    private string? _folder;
    private FileSearchPage? _page;
    private readonly Stack<SearchView> _history = new();
    private SearchView? _searchView;
    private sealed record SearchView(string? Folder, string Query, int Filter, int Sort, uint Offset,
        string? Selection, FileSearchPage? Page);
    private readonly CheckBox _dialogMode = new() { AutoSize = true, Dock = DockStyle.Fill, Visible = false };
    private FileDialogTarget? _dialogTarget;
    private bool _detectingDialog;
    private bool _dialogDetectionFailed;
    private string? _dialogDetectionError;
    private bool _navigating;
    private int _showSession;
    private CancellationTokenSource? _dialogProbe;

    public FileSearchPopup(ConfigManager config, ProcessLauncher launcher)
    {
        SuspendLayout();
        _config = config;
        _launcher = launcher;
        Text = "Quickstart · 本地搜索";
        Font = UiTheme.CreateFont(9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(720, 420);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.TextPrimary;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        _filter.Items.AddRange(["全部", "文件", "文件夹"]);
        _sort.Items.AddRange(["名称排序", "最近修改", "文件最大"]);
        _filter.SelectedIndex = _sort.SelectedIndex = 0;
        foreach (var row in new[] { _header, _top, _navigation, _footer })
        {
            row.RowCount = 1;
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        _header.Controls.Add(_title, 0, 0);
        _header.Controls.Add(_maximize, 1, 0);
        _header.Controls.Add(_close, 2, 0);
        _title.Font = UiTheme.CreateFont(12f, FontStyle.Bold);
        _header.MouseDown += BeginWindowDrag;
        _title.MouseDown += BeginWindowDrag;
        _header.DoubleClick += (_, _) => ToggleMaximize();
        _title.DoubleClick += (_, _) => ToggleMaximize();
        _maximize.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => Hide();
        foreach (var control in new Control[] { _title, _maximize, _close }) { control.Margin = Padding.Empty; control.Dock = DockStyle.Fill; }
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) _top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _top.Controls.Add(new FileSearchField(_query, true), 0, 0);
        _top.Controls.Add(_filter, 1, 0);
        _top.Controls.Add(_sort, 2, 0);
        _top.Controls.Add(_previewEnabled, 3, 0);
        _previewEnabled.FlatAppearance.BorderSize = 0;
        _footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) _footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _external.Click += async (_, _) => await OpenExternalAsync();
        _footer.Controls.Add(_status, 0, 0);
        _footer.Controls.Add(_previous, 1, 0);
        _footer.Controls.Add(_next, 2, 0);
        _footer.Controls.Add(_external, 3, 0);
        _status.Margin = Padding.Empty;
        _status.ForeColor = UiTheme.TextSecondary;
        for (var i = 0; i < 3; i++) _navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _navigation.Controls.Add(_back, 0, 0);
        _navigation.Controls.Add(_up, 1, 0);
        _navigation.Controls.Add(_returnToSearch, 2, 0);
        _navigation.Controls.Add(new FileSearchField(_location, false), 3, 0);
        _back.Click += async (_, _) => await GoBackAsync();
        _up.Click += async (_, _) => await GoUpAsync();
        _returnToSearch.Click += async (_, _) => await ReturnToSearchAsync();
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _layout.Controls.Add(_header, 0, 0);
        _layout.Controls.Add(_dialogMode, 0, 1);
        _layout.Controls.Add(_top, 0, 2);
        _layout.Controls.Add(_navigation, 0, 3);
        _split.Panel1.Controls.Add(_results);
        _split.Panel2.Controls.Add(_preview);
        _split.Panel1.BackColor = _split.Panel2.BackColor = UiTheme.Card;
        _split.Panel2.Paint += PaintPreviewDivider;
        _layout.Controls.Add(_split, 0, 4);
        _layout.Controls.Add(_footer, 0, 5);
        Controls.Add(_layout);
        _results.Columns.Add("名称", 250);
        _results.Columns.Add("所在文件夹", 330);
        _results.Columns.Add("大小", 85);
        _results.Columns.Add("修改时间", 145);
        _results.SmallImageList = _icons;
        _results.Resize += (_, _) => ResizeColumns();
        _results.DrawColumnHeader += DrawResultHeader;
        _results.DrawSubItem += DrawResultCell;
        _results.ColumnWidthChanging += (_, e) => { e.Cancel = true; e.NewWidth = _results.Columns[e.ColumnIndex].Width; };
        _query.TextChanged += (_, _) => ScheduleSearch();
        _filter.SelectedIndexChanged += (_, _) => ScheduleSearch();
        _sort.SelectedIndexChanged += (_, _) => ScheduleSearch();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        _previous.Click += async (_, _) => { _offset -= EverythingIpcProtocol.PageSize; await SearchAsync(); };
        _next.Click += async (_, _) => { _offset += EverythingIpcProtocol.PageSize; await SearchAsync(); };
        _results.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _results.GetItemAt(e.X, e.Y) != null) RunAction(OpenSelected);
        };
        _results.ItemSelectionChanged += (_, e) =>
        {
            if (!e.IsSelected) { _previewDelay.Stop(); _preview.Clear(); }
            else if (_results.Focused) SchedulePreview();
        };
        _results.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && _results.GetItemAt(e.X, e.Y) != null) SchedulePreview(); };
        _results.KeyUp += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown) SchedulePreview(); };
        _previewDelay.Tick += async (_, _) =>
        {
            _previewDelay.Stop();
            if (CanPreview && Selected is { IsFolder: false } result) await _preview.PreviewAsync(result.FullPath);
        };
        _preview.OpenRequested += (_, _) => RunAction(OpenSelected);
        _preview.EscapeRequested += (_, _) => Hide();
        _previewEnabled.CheckedChanged += (_, _) => UpdatePreviewLayout();
        _dialogMode.CheckedChanged += (_, _) => UpdatePreviewLayout();
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _results.Items.Count > 0)
            {
                _results.Focus(); _results.Items[0].Selected = true; _results.Items[0].Focused = true;
                e.SuppressKeyPress = true;
            }
        };
        AddMenu("打开（Enter）", OpenSelected);
        AddMenu("打开所在位置（Ctrl+Enter）", LocateSelected);
        AddMenu("复制完整路径（Ctrl+C）", CopySelectedPath);
        AddMenu("加入 Quickstart 收藏", AddSelectedFavorite);
        AddMenu("在资源管理器中打开", () => { if (Selected is { IsFolder: true } folder) OpenFolderInExplorer(folder.FullPath); });
        _results.ContextMenuStrip = _menu;
        _menu.Opening += (_, e) =>
        {
            e.Cancel = Selected == null;
            _menu.Items[0].Text = UseDialog ? "跳转到此目录（Enter）" : Selected?.IsFolder == true ? "进入文件夹（Enter）" : "打开（Enter）";
            _menu.Items[1].Text = UseDialog ? "跳转到所在目录（Ctrl+Enter）" : "打开所在位置（Ctrl+Enter）";
            _menu.Items[4].Visible = !UseDialog && Selected?.IsFolder == true;
        };
        _results.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _results.SelectedItems.Clear();
            var item = _results.GetItemAt(e.X, e.Y);
            if (item != null) item.Selected = true;
        };
        _toolTip.SetToolTip(_back, "返回（Alt+左箭头）");
        _toolTip.SetToolTip(_up, "上一级（Alt+上箭头）");
        _toolTip.SetToolTip(_returnToSearch, "返回原搜索结果");
        _toolTip.SetToolTip(_maximize, "最大化 / 还原");
        _toolTip.SetToolTip(_close, "关闭（Esc）");
        _status.TextChanged += (_, _) => _toolTip.SetToolTip(_status, _status.Text);
        _menu.Renderer = new ToolStripProfessionalRenderer(new SearchMenuColors());
        _menu.ShowImageMargin = false;
        _menu.BackColor = UiTheme.Card;
        _menu.ForeColor = UiTheme.TextPrimary;
        _menu.Font = Font;
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        VisibleChanged += (_, _) =>
        {
            if (Visible) return;
            _debounce.Stop(); _searchCts?.Cancel(); _revision++;
            _dialogProbe?.Cancel();
            _previewDelay.Stop(); _preview.Clear();
        };
        _layoutReady = true;
        ApplyScaledLayout();
        ResumeLayout(true);
        UpdatePreviewLayout();
        _split.SplitterDistance = Math.Max(0, (int)(_split.Width * .55));
        SizeChanged += (_, _) => { if (_layoutReady) UpdatePreviewLayout(); };
    }

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private void ApplyScaledLayout()
    {
        if (!_layoutReady) return;
        SuspendLayout();
        Padding = new Padding(S(6));
        _layout.Padding = new Padding(S(14), S(6), S(14), S(10));
        _layout.Margin = Padding.Empty;
        foreach (var row in new[] { 0, 2, 5 }) _layout.RowStyles[row].Height = S(48);
        _layout.RowStyles[3].Height = _folder == null ? 0 : S(48);
        foreach (var panel in new[] { _header, _top, _navigation }) panel.Margin = new Padding(0, 0, 0, S(12));
        _footer.Margin = new Padding(0, S(12), 0, 0);
        _split.Margin = Padding.Empty;
        _split.SplitterWidth = S(12);
        _split.BackColor = UiTheme.Surface;
        _split.Panel2.Padding = new Padding(S(12), 0, 0, 0);
        _header.ColumnStyles[1].Width = _header.ColumnStyles[2].Width = S(36);
        foreach (var combo in new[] { _filter, _sort })
        {
            combo.Margin = new Padding(S(8), 0, 0, 0);
            combo.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            combo.ItemHeight = S(28);
            combo.ItemHeight = Math.Max(S(20), combo.ItemHeight + S(36) - combo.Height);
            combo.DropDownHeight = S(180);
        }
        _filter.Width = S(88);
        _sort.Width = S(128);
        _previewEnabled.Size = new Size(S(64), S(36));
        _previewEnabled.Margin = new Padding(S(8), 0, 0, 0);
        _previewEnabled.FlatAppearance.MouseOverBackColor = UiTheme.ButtonHoverFill;
        _previewEnabled.FlatAppearance.MouseDownBackColor = UiTheme.ButtonPressedFill;
        foreach (var button in new[] { _previous, _next, _external, _returnToSearch }) StyleTextButton(button);
        _previous.Margin = _next.Margin = _external.Margin = new Padding(S(8), 0, 0, 0);
        _back.Size = _up.Size = new Size(S(36), S(36));
        _back.Margin = _up.Margin = _returnToSearch.Margin = new Padding(0, 0, S(8), 0);
        _back.AutoSize = _up.AutoSize = false;
        _title.Margin = _maximize.Margin = _close.Margin = Padding.Empty;
        _results.Margin = _preview.Margin = Padding.Empty;
        _dialogMode.Margin = new Padding(0, 0, 0, _dialogMode.Visible ? S(12) : 0);
        ResumeLayout(true);
        ResizeColumns();
    }

    private void StyleTextButton(Button button)
    {
        button.AutoSize = false;
        button.MinimumSize = Size.Empty;
        button.Size = new Size(Math.Max(S(64), TextRenderer.MeasureText(button.Text, Font).Width + S(24)), S(36));
        UiTheme.StyleFlatButton(button, UiTheme.Surface);
        button.ForeColor = UiTheme.TextSecondary;
    }

    private void PaintPreviewDivider(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(UiTheme.BorderSubtle);
        e.Graphics.DrawLine(pen, 0, 0, 0, _split.Panel2.Height);
    }

    private void DrawResultHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(UiTheme.SurfaceAlt);
        e.Graphics.FillRectangle(background, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -S(10), 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, Font, bounds, UiTheme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void DrawResultCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.SubItem == null || e.Item == null || e.Bounds.Width <= 0) return;
        using var background = new SolidBrush(e.Item.Selected ? UiTheme.SelectionFill : UiTheme.Card);
        e.Graphics.FillRectangle(background, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -S(10), 0);
        if (e.ColumnIndex == 0)
        {
            if (_icons.Images.ContainsKey(e.Item.ImageKey))
                e.Graphics.DrawImage(_icons.Images[e.Item.ImageKey]!, bounds.Left, bounds.Top + (bounds.Height - S(20)) / 2, S(20), S(20));
            bounds.X += S(30);
            bounds.Width = Math.Max(0, bounds.Width - S(30));
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, bounds,
            e.Item.Selected ? UiTheme.AccentText : e.ColumnIndex == 0 ? UiTheme.TextPrimary : UiTheme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (e.ColumnIndex == 0 && e.Item.Focused && _results.Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1));
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    private void ToggleMaximize()
    {
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        _maximize.Glyph = WindowState == FormWindowState.Maximized ? FileSearchGlyph.Restore : FileSearchGlyph.Maximize;
        _maximize.AccessibleName = WindowState == FormWindowState.Maximized ? "还原" : "最大化";
        _maximize.Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScaledLayout();
        FitToWorkingArea(Screen.FromControl(this));
        UpdatePreviewLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Initial autoscaling runs after construction; reapply logical metrics once it finishes.
        ApplyScaledLayout();
        FitToWorkingArea(Screen.FromControl(this));
        UpdatePreviewLayout();
    }

    private void FitToWorkingArea(Screen screen)
    {
        var area = screen.WorkingArea;
        MinimumSize = new Size(Math.Min(S(720), area.Width), Math.Min(S(420), area.Height));
        MaximizedBounds = area;
        if (WindowState != FormWindowState.Normal) return;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_title != null) _title.Text = Text.Replace("Quickstart · ", "", StringComparison.Ordinal);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 || m.Result != (IntPtr)1 || WindowState == FormWindowState.Maximized) return;
        var packed = m.LParam.ToInt64();
        var point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
        var grip = S(6);
        var left = point.X < grip;
        var right = point.X >= ClientSize.Width - grip;
        var top = point.Y < grip;
        var bottom = point.Y >= ClientSize.Height - grip;
        var hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
            : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
        m.Result = (IntPtr)hit;
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class SearchMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiTheme.Card;
        public override Color MenuItemSelected => UiTheme.SelectionFill;
        public override Color MenuItemBorder => UiTheme.SelectionFill;
        public override Color MenuBorder => UiTheme.BorderSubtle;
    }

    private bool CanPreview => Visible && !_split.Panel2Collapsed && _previewEnabled.Checked && !UseDialog && !_detectingDialog && !_dialogDetectionFailed;

    private void SchedulePreview()
    {
        _previewDelay.Stop();
        if (CanPreview && Selected is { IsFolder: false }) _previewDelay.Start();
        else _preview.Clear();
    }

    private void UpdatePreviewLayout()
    {
        if (!_layoutReady || _adjustingLayout) return;
        _adjustingLayout = true;
        try
        {
            var wasCollapsed = _split.Panel2Collapsed;
            var collapse = !_previewEnabled.Checked || UseDialog || _split.Width < S(840);
            _split.Panel1MinSize = _split.Panel2MinSize = 0;
            _split.Panel2Collapsed = collapse;
            if (!collapse)
            {
                _split.SplitterDistance = Math.Clamp(_split.SplitterDistance, S(420), Math.Max(S(420), _split.Width - S(320) - _split.SplitterWidth));
                _split.Panel1MinSize = S(420);
                _split.Panel2MinSize = S(320);
            }
            _previewEnabled.BackColor = _previewEnabled.Checked ? UiTheme.SelectionFill : UiTheme.Surface;
            _previewEnabled.ForeColor = _previewEnabled.Checked ? UiTheme.AccentText : UiTheme.TextSecondary;
            _toolTip.SetToolTip(_previewEnabled, _split.Width < S(840) ? "窗口加宽后显示预览" : "显示文件预览");
            if (collapse) { _previewDelay.Stop(); _preview.Clear(); }
            else if (CanPreview)
            {
                _ = _preview.WarmUpAsync();
                if (wasCollapsed) SchedulePreview();
            }
            ResizeColumns();
        }
        finally { _adjustingLayout = false; }
    }

    private void ResizeColumns()
    {
        if (_results.Columns.Count != 4) return;
        var scale = DeviceDpi / 96d;
        int S(int value) => (int)Math.Round(value * scale);
        var width = Math.Max(S(280), _results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        _results.Columns[2].Width = S(80);
        _results.Columns[3].Width = S(145);
        var remaining = width - S(225);
        var showParent = _folder == null && remaining >= S(360);
        _results.Columns[0].Width = showParent ? Math.Max(S(200), (int)(remaining * .55)) : remaining;
        _results.Columns[1].Width = showParent ? remaining - _results.Columns[0].Width : 0;
        var iconSize = Math.Clamp(S(30), 16, 96);
        if (_icons.ImageSize.Width != iconSize)
        {
            // ImageSize recreates the native image list and drops its images on a DPI change.
            _icons.ImageSize = new Size(iconSize, iconSize);
            foreach (ListViewItem item in _results.Items)
                if (item.Tag is FileSearchResult result) item.ImageKey = EnsureResultIcon(result);
            _results.Invalidate();
        }
    }

    public void ShowSearch(string query = "", IntPtr sourceWindow = default)
    {
        if (sourceWindow == IntPtr.Zero) sourceWindow = FileDialogNavigator.CaptureForegroundWindow();
        var session = ++_showSession;
        _dialogProbe?.Cancel();
        _dialogProbe?.Dispose();
        _dialogProbe = new CancellationTokenSource();
        _dialogTarget = null;
        _dialogMode.Visible = false;
        _dialogMode.Checked = false;
        _dialogDetectionFailed = false;
        _dialogDetectionError = null;
        _detectingDialog = _config.Config.FileDialogNavigationEnabled;
        Text = "Quickstart · 本地搜索";
        _changing = true;
        _folder = null;
        _page = null;
        _history.Clear();
        _searchView = null;
        _query.Text = query;
        _filter.SelectedIndex = 0;
        _offset = 0;
        _changing = false;
        UpdateNavigation();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min(S(720), area.Width), Math.Min(S(420), area.Height));
        MaximizedBounds = area;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 3));
        WindowActivator.ClaimForegroundRights();
        Show(); Activate(); _query.Focus(); _query.SelectAll();
        ScheduleSearch();
        if (_detectingDialog) _ = DetectDialogAsync(sourceWindow, session, _dialogProbe.Token);
        else UpdatePreviewLayout();
    }

    private bool UseDialog => _dialogTarget != null && _dialogMode.Checked;

    private async Task DetectDialogAsync(IntPtr source, int session, CancellationToken token)
    {
        try
        {
            var target = await FileDialogNavigator.DetectAsync(source, token);
            if (IsDisposed || session != _showSession || token.IsCancellationRequested) return;
            _dialogTarget = target;
            if (target != null)
            {
                _dialogMode.Text = $"跳转到 {target.Description} · 保留原文件名，不自动打开或保存";
                _dialogMode.Checked = true;
                _dialogMode.Visible = true;
                Text = $"Quickstart · 为{target.Description}选择目录";
                _filter.SelectedIndex = 2;
            }
        }
        catch (Exception ex)
        {
            if (IsDisposed || session != _showSession || token.IsCancellationRequested) return;
            _dialogDetectionFailed = true;
            _dialogDetectionError = ex is OperationCanceledException ? "识别文件对话框超时，请重新呼出搜索。" : $"无法识别文件对话框：{ex.GetBaseException().Message}";
            _status.Text = _dialogDetectionError;
        }
        finally
        {
            if (!IsDisposed && session == _showSession)
            {
                _detectingDialog = false;
                UpdatePreviewLayout();
            }
        }
    }

    private string EffectiveQuery => _filter.SelectedIndex switch
    {
        1 => $"file: <{_query.Text.Trim()}>",
        2 => $"folder: <{_query.Text.Trim()}>",
        _ => _query.Text.Trim()
    };

    private SearchView CaptureView() => new(_folder, _query.Text, _filter.SelectedIndex, _sort.SelectedIndex,
        _offset, Selected?.FullPath, _page);

    private void UpdateNavigation()
    {
        var browsing = _folder != null;
        _navigation.Visible = browsing;
        _back.Enabled = _history.Count > 0;
        _up.Enabled = browsing && Directory.GetParent(_folder!) != null;
        _returnToSearch.Enabled = _searchView != null;
        _location.Text = _folder ?? "";
        _query.PlaceholderText = browsing ? "筛选当前文件夹" : "搜索本机文件、文件夹（支持 Everything 搜索语法）";
        _query.AccessibleName = browsing ? "筛选当前文件夹" : "搜索本机文件、文件夹";
        _external.Text = browsing ? "在资源管理器中打开" : "在 Everything 中打开";
        _layout.RowStyles[3].Height = browsing ? S(48) : 0;
        StyleTextButton(_external);
        ResizeColumns();
    }

    private Task BrowseFolderAsync(string folder) => BrowseFolderAndSelectAsync(folder, null);

    private async Task BrowseFolderAndSelectAsync(string folder, string? selection)
    {
        var current = CaptureView();
        if (_folder == null) _searchView = current;
        _history.Push(current);
        _changing = true;
        _folder = folder;
        _query.Clear();
        _filter.SelectedIndex = 0;
        _offset = 0;
        _changing = false;
        UpdateNavigation();
        _results.Focus();
        await SearchAsync(selection);
    }

    private async Task RestoreViewAsync(SearchView view)
    {
        CancelPendingSearch();
        _changing = true;
        _folder = view.Folder;
        _query.Text = view.Query;
        _filter.SelectedIndex = view.Filter;
        _sort.SelectedIndex = view.Sort;
        _offset = view.Offset;
        _changing = false;
        UpdateNavigation();
        _results.Focus();
        // A return to search restores the exact page without waiting for Everything again.
        if (view.Folder == null && view.Page != null) ShowPage(view.Page, view.Selection);
        else if (_folder != null || !string.IsNullOrWhiteSpace(_query.Text)) await SearchAsync(view.Selection);
        else ScheduleSearch();
    }

    private async Task GoBackAsync()
    {
        if (_history.TryPop(out var view))
        {
            if (view.Folder == null) _searchView = null;
            await RestoreViewAsync(view);
        }
    }

    private async Task GoUpAsync()
    {
        if (_folder != null && Directory.GetParent(_folder) is { } parent)
        {
            var child = _folder;
            await BrowseFolderAndSelectAsync(parent.FullName, child);
        }
    }

    private async Task ReturnToSearchAsync()
    {
        if (_searchView is not { } view) return;
        _history.Clear();
        _searchView = null;
        await RestoreViewAsync(view);
    }

    private void CancelPendingSearch()
    {
        _debounce.Stop();
        _searchCts?.Cancel();
        _revision++;
        _previewDelay.Stop();
        _preview.Clear();
        _page = null;
        _results.Items.Clear();
        _previous.Enabled = _next.Enabled = false;
    }

    private void ScheduleSearch()
    {
        if (_changing) return;
        CancelPendingSearch();
        _offset = 0;
        _status.Text = _folder != null ? "正在读取文件夹…" : "输入关键词开始搜索";
        if (_folder != null || !string.IsNullOrWhiteSpace(_query.Text)) _debounce.Start();
    }

    private async Task SearchAsync(string? selection = null)
    {
        CancelPendingSearch();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var revision = ++_revision;
        _status.Text = _folder != null ? "正在读取文件夹…" : "正在搜索…";
        try
        {
            var sort = _sort.SelectedIndex switch { 1 => 14u, 2 => 6u, _ => 1u };
            var page = _folder != null
                ? await LocalFolderBrowser.ReadAsync(_folder, _query.Text.Trim(), _filter.SelectedIndex, _offset, sort, cts.Token, selection)
                : await _client.SearchAsync(_config.Config.EverythingPath, EffectiveQuery, _offset, sort, cts.Token, retry =>
                {
                    if (!IsDisposed && Visible && revision == _revision && !cts.IsCancellationRequested)
                        _status.Text = $"Everything 查询超时，正在自动重试（{retry}/2）…";
                });
            if (revision != _revision || IsDisposed || !Visible || cts.IsCancellationRequested) return;
            ShowPage(page, selection);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && revision == _revision)
            {
                _status.Text = _folder != null ? $"无法读取文件夹：{ex.Message}" : ex.Message;
                _previous.Enabled = _offset > 0;
            }
        }
    }

    private void ShowPage(FileSearchPage page, string? selection = null)
    {
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            _icons.Images.Clear();
            foreach (var result in page.Items)
            {
                var key = EnsureResultIcon(result);
                var item = new ListViewItem(result.Name) { Tag = result, ImageKey = key, ToolTipText = result.FullPath };
                item.SubItems.Add(Path.GetDirectoryName(result.FullPath) ?? result.FullPath);
                item.SubItems.Add(result.IsFolder ? "文件夹" : FormatSize(result.Size));
                item.SubItems.Add(result.ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—");
                _results.Items.Add(item);
            }
            SelectResult(selection);
        }
        finally { _results.EndUpdate(); }
        _page = page;
        _offset = page.Offset;
        _previous.Enabled = _offset > 0;
        _next.Enabled = (ulong)_offset + (uint)page.Items.Count < page.Total && page.Items.Count > 0;
        var empty = _folder != null && string.IsNullOrWhiteSpace(_query.Text) && _filter.SelectedIndex == 0
            ? "此文件夹为空" : "未找到匹配结果";
        _status.Text = _dialogDetectionError ?? (page.Total == 0 ? empty : $"共 {page.Total:N0} 项 · 第 {_offset / EverythingIpcProtocol.PageSize + 1} 页");
    }

    private string EnsureResultIcon(FileSearchResult result)
    {
        var key = result.IsFolder ? "<folder>" : Path.GetExtension(result.FullPath).ToLowerInvariant();
        if (!_icons.Images.ContainsKey(key))
        {
            var icon = result.IsFolder ? IconExtractor.GetIcon("folder", true) : IconExtractor.GetGenericTypeIcon(key);
            _icons.Images.Add(key, icon ?? SystemIcons.Application);
        }
        return key;
    }

    private void SelectResult(string? path)
    {
        if (_results.Items.Count == 0) return;
        var item = _results.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is FileSearchResult result &&
            string.Equals(result.FullPath, path, StringComparison.OrdinalIgnoreCase)) ?? _results.Items[0];
        _results.SelectedItems.Clear();
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    private static string FormatSize(long? size) => size switch
    {
        null => "—", < 1024 => $"{size} B", < 1048576 => $"{size / 1024d:0.#} KB",
        < 1073741824 => $"{size / 1048576d:0.#} MB", _ => $"{size / 1073741824d:0.#} GB"
    };
    private FileSearchResult? Selected => _results.SelectedItems.Count > 0 ? _results.SelectedItems[0].Tag as FileSearchResult : null;
    private string SelectedDirectory => Selected is { } item
        ? (item.IsFolder ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? "")
        : _folder ?? _query.Text.Trim().Trim('"');
    private static QuickEntry ToEntry(FileSearchResult result) => new() { Name = result.Name, Path = result.FullPath,
        Type = result.IsFolder ? EntryType.Folder : EntryType.File };
    private async void OpenSelected()
    {
        if (_detectingDialog || _navigating) { _status.Text = "正在识别或操作文件对话框，请稍候…"; return; }
        if (_dialogDetectionFailed) { _status.Text = _dialogDetectionError ?? "未能完成对话框识别，请关闭后重新呼出搜索。"; return; }
        if (UseDialog)
        {
            await NavigateFolderAsync(SelectedDirectory);
            return;
        }
        try
        {
            if (Selected is not { } result) return;
            if (result.IsFolder)
            {
                await BrowseFolderAsync(result.FullPath);
                return;
            }
            if (!File.Exists(result.FullPath) && !Directory.Exists(result.FullPath))
                throw new FileNotFoundException("文件已移动或删除，请重新搜索。");
            var entry = _config.Config.Entries.FirstOrDefault(e => string.Equals(e.Path, result.FullPath, StringComparison.OrdinalIgnoreCase)) ?? ToEntry(result);
            _launcher.Open(entry);
            Hide();
        }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
    }
    private void LocateSelected()
    {
        if (_detectingDialog || _navigating) { _status.Text = "正在识别或操作文件对话框，请稍候…"; return; }
        if (_dialogDetectionFailed) { _status.Text = _dialogDetectionError ?? "未能完成对话框识别，请关闭后重新呼出搜索。"; return; }
        if (Selected is not { } result) return;
        if (UseDialog) { _ = NavigateFolderAsync(result.IsFolder ? result.FullPath : Path.GetDirectoryName(result.FullPath) ?? ""); return; }
        // Windows file names cannot contain quotes; Explorer's /select syntax needs a quoted single argument.
        using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.FullPath}\"") { UseShellExecute = true });
        Hide();
    }

    private void OpenFolderInExplorer(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("文件夹已移动、删除或无法访问。");
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(folder);
        using var process = Process.Start(start);
        _config.RecordRecentFolder(folder);
        var favorite = _config.Config.Entries.FirstOrDefault(e => e.Type == EntryType.Folder &&
            string.Equals(FolderDirectoryService.NormalizePath(e.Path), FolderDirectoryService.NormalizePath(folder), StringComparison.OrdinalIgnoreCase));
        if (favorite != null) _config.TouchEntry(favorite.Id);
        Hide();
    }

    private async Task NavigateFolderAsync(string folder)
    {
        if (_navigating || _dialogTarget == null) return;
        _navigating = true;
        var session = _showSession;
        var target = _dialogTarget;
        try
        {
            Hide();
            await FileDialogNavigator.NavigateAsync(target, folder);
            _config.RecordRecentFolder(folder);
            var favorite = _config.Config.Entries.FirstOrDefault(e => e.Type == EntryType.Folder &&
                string.Equals(FolderDirectoryService.NormalizePath(e.Path), FolderDirectoryService.NormalizePath(folder), StringComparison.OrdinalIgnoreCase));
            if (favorite != null) _config.TouchEntry(favorite.Id);
        }
        catch (Exception ex)
        {
            if (IsDisposed || session != _showSession) return;
            Show(); Activate();
            _status.Text = ex is OperationCanceledException ? "目录跳转超时，请检查原对话框后重试。" : ex.Message;
        }
        finally { _navigating = false; }
    }
    private void CopySelectedPath() { if (Selected is { } result) { Clipboard.SetText(result.FullPath); _status.Text = "已复制完整路径"; } }
    private void AddSelectedFavorite()
    {
        if (Selected is { } result)
            _status.Text = _config.AddEntry(ToEntry(result)) ? "已加入收藏" : "该路径已在收藏中";
    }
    private void AddMenu(string title, Action action) => _menu.Items.Add(title, null, (_, _) => RunAction(action));
    private void RunAction(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    private async Task OpenExternalAsync()
    {
        try
        {
            if (_folder != null) { OpenFolderInExplorer(_folder); return; }
            if (string.IsNullOrWhiteSpace(_query.Text)) return;
            var path = _config.Config.EverythingPath;
            if (!File.Exists(path)) path = await Task.Run(() => EverythingDetector.Detect()) ?? "";
            await EverythingLauncher.SearchAsync(path, EffectiveQuery);
            if (!IsDisposed) Hide();
        }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData == (Keys.Control | Keys.L)) { _query.Focus(); _query.SelectAll(); return true; }
        if (keyData == (Keys.Alt | Keys.Left) || (keyData == Keys.Back && _results.Focused))
        { _ = GoBackAsync(); return true; }
        if (keyData == (Keys.Alt | Keys.Up)) { _ = GoUpAsync(); return true; }
        // Let the browser/PDF viewer handle text copy, selection and document navigation.
        if (_preview.HasPreviewFocus) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.F5)
        {
            if (_folder != null || !string.IsNullOrWhiteSpace(_query.Text)) _ = SearchAsync(Selected?.FullPath);
            return true;
        }
        if (keyData == Keys.Enter && (_query.Focused || _results.Focused)) { RunAction(OpenSelected); return true; }
        if (keyData == (Keys.Control | Keys.Enter)) { RunAction(LocateSelected); return true; }
        if (keyData == (Keys.Control | Keys.C) && _results.Focused) { RunAction(CopySelectedPath); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _showSession++; _dialogProbe?.Cancel(); _dialogProbe?.Dispose();
            _revision++; _searchCts?.Cancel(); _searchCts?.Dispose();
            _debounce.Dispose(); _client.Dispose(); _menu.Dispose(); _icons.Dispose(); _toolTip.Dispose();
            _previewDelay.Dispose(); _preview.Dispose();
        }
        base.Dispose(disposing);
    }
}
