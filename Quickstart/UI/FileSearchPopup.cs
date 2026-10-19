namespace Quickstart.UI;

using System.Diagnostics;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class FileSearchPopup : Form
{
    private readonly ConfigManager _config;
    private readonly ProcessLauncher _launcher;
    private readonly EverythingSearchClient _client = new();
    private readonly TextBox _query = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索本机文件、文件夹（支持 Everything 搜索语法）" };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ListView _results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _previous = new() { Text = "上一页", AutoSize = true, Enabled = false };
    private readonly Button _next = new() { Text = "下一页", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(20, 20) };
    private readonly ContextMenuStrip _menu = new();
    private CancellationTokenSource? _searchCts;
    private int _revision;
    private uint _offset;
    private bool _changing;
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
        Font = UiTheme.CreateFont(9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(860, 480);
        MinimumSize = new Size(580, 320);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        _filter.Items.AddRange(["全部", "文件", "文件夹"]);
        _sort.Items.AddRange(["名称排序", "最近修改", "文件最大"]);
        _filter.SelectedIndex = _sort.SelectedIndex = 0;
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(_query, 0, 0);
        top.Controls.Add(_filter, 1, 0);
        top.Controls.Add(_sort, 2, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var external = new Button { Text = "在 Everything 中打开", AutoSize = true };
        external.Click += async (_, _) => await OpenExternalAsync();
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_previous, 1, 0);
        footer.Controls.Add(_next, 2, 0);
        footer.Controls.Add(external, 3, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_dialogMode, 0, 0);
        layout.Controls.Add(top, 0, 1);
        layout.Controls.Add(_results, 0, 2);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        _results.Columns.Add("名称", 250);
        _results.Columns.Add("所在文件夹", 330);
        _results.Columns.Add("大小", 85);
        _results.Columns.Add("修改时间", 145);
        _results.SmallImageList = _icons;
        _results.Resize += (_, _) => ResizeColumns();
        DpiChanged += (_, _) => ResizeColumns();
        _query.TextChanged += (_, _) => ScheduleSearch();
        _filter.SelectedIndexChanged += (_, _) => ScheduleSearch();
        _sort.SelectedIndexChanged += (_, _) => ScheduleSearch();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        _previous.Click += async (_, _) => { _offset -= EverythingIpcProtocol.PageSize; await SearchAsync(); };
        _next.Click += async (_, _) => { _offset += EverythingIpcProtocol.PageSize; await SearchAsync(); };
        _results.DoubleClick += (_, _) => RunAction(OpenSelected);
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
        _results.ContextMenuStrip = _menu;
        _menu.Opening += (_, e) =>
        {
            e.Cancel = Selected == null;
            _menu.Items[0].Text = UseDialog ? "跳转到此目录（Enter）" : "打开（Enter）";
            _menu.Items[1].Text = UseDialog ? "跳转到所在目录（Ctrl+Enter）" : "打开所在位置（Ctrl+Enter）";
        };
        _results.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _results.SelectedItems.Clear();
            var item = _results.GetItemAt(e.X, e.Y);
            if (item != null) item.Selected = true;
        };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        VisibleChanged += (_, _) =>
        {
            if (Visible) return;
            _debounce.Stop(); _searchCts?.Cancel(); _revision++;
            _dialogProbe?.Cancel();
        };
        ResumeLayout(true);
        ResizeColumns();
    }

    private void ResizeColumns()
    {
        if (_results.Columns.Count != 4) return;
        var scale = DeviceDpi / 96d;
        int S(int value) => (int)Math.Round(value * scale);
        var width = Math.Max(S(550), _results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        _results.Columns[2].Width = S(85);
        _results.Columns[3].Width = S(145);
        var remaining = width - S(230);
        _results.Columns[0].Width = (int)(remaining * .42);
        _results.Columns[1].Width = remaining - _results.Columns[0].Width;
        var iconSize = Math.Clamp(S(20), 16, 64);
        if (_icons.ImageSize.Width != iconSize) _icons.ImageSize = new Size(iconSize, iconSize);
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
        _query.Text = query;
        _filter.SelectedIndex = 0;
        _offset = 0;
        _changing = false;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 3));
        WindowActivator.ClaimForegroundRights();
        Show(); Activate(); _query.Focus(); _query.SelectAll();
        ScheduleSearch();
        if (_detectingDialog) _ = DetectDialogAsync(sourceWindow, session, _dialogProbe.Token);
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
        finally { if (!IsDisposed && session == _showSession) _detectingDialog = false; }
    }

    private string EffectiveQuery => _filter.SelectedIndex switch
    {
        1 => $"file: <{_query.Text.Trim()}>",
        2 => $"folder: <{_query.Text.Trim()}>",
        _ => _query.Text.Trim()
    };

    private void ScheduleSearch()
    {
        if (_changing) return;
        _revision++;
        _searchCts?.Cancel();
        _debounce.Stop();
        _offset = 0;
        _results.Items.Clear();
        _previous.Enabled = _next.Enabled = false;
        _status.Text = "输入关键词开始搜索";
        if (!string.IsNullOrWhiteSpace(_query.Text)) _debounce.Start();
    }

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var revision = ++_revision;
        _results.Items.Clear();
        _previous.Enabled = _next.Enabled = false;
        _status.Text = "正在搜索…";
        try
        {
            var page = await _client.SearchAsync(_config.Config.EverythingPath, EffectiveQuery, _offset,
                _sort.SelectedIndex switch { 1 => 14u, 2 => 6u, _ => 1u }, cts.Token);
            if (revision != _revision || IsDisposed || !Visible) return;
            _results.BeginUpdate();
            try
            {
                _icons.Images.Clear();
                foreach (var result in page.Items)
                {
                    var key = result.IsFolder ? "<folder>" : Path.GetExtension(result.FullPath).ToLowerInvariant();
                    if (!_icons.Images.ContainsKey(key))
                    {
                        var icon = result.IsFolder ? IconExtractor.GetIcon("folder", true) : IconExtractor.GetGenericTypeIcon(key);
                        _icons.Images.Add(key, icon ?? SystemIcons.Application);
                    }
                    var item = new ListViewItem(result.Name) { Tag = result, ImageKey = key, ToolTipText = result.FullPath };
                    item.SubItems.Add(Path.GetDirectoryName(result.FullPath) ?? result.FullPath);
                    item.SubItems.Add(result.IsFolder ? "文件夹" : FormatSize(result.Size));
                    item.SubItems.Add(result.ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—");
                    _results.Items.Add(item);
                }
                if (_results.Items.Count > 0) _results.Items[0].Selected = true;
            }
            finally { _results.EndUpdate(); }
            _offset = page.Offset;
            _previous.Enabled = _offset > 0;
            _next.Enabled = (ulong)_offset + (uint)page.Items.Count < page.Total && page.Items.Count > 0;
            _status.Text = _dialogDetectionError ?? (page.Total == 0 ? "未找到匹配结果" : $"共 {page.Total:N0} 项 · 第 {_offset / EverythingIpcProtocol.PageSize + 1} 页");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && revision == _revision)
            {
                _status.Text = ex.Message;
                _previous.Enabled = _offset > 0;
            }
        }
    }

    private static string FormatSize(long? size) => size switch
    {
        null => "—", < 1024 => $"{size} B", < 1048576 => $"{size / 1024d:0.#} KB",
        < 1073741824 => $"{size / 1048576d:0.#} MB", _ => $"{size / 1073741824d:0.#} GB"
    };
    private FileSearchResult? Selected => _results.SelectedItems.Count > 0 ? _results.SelectedItems[0].Tag as FileSearchResult : null;
    private static QuickEntry ToEntry(FileSearchResult result) => new() { Name = result.Name, Path = result.FullPath,
        Type = result.IsFolder ? EntryType.Folder : EntryType.File };
    private async void OpenSelected()
    {
        if (_detectingDialog || _navigating) { _status.Text = "正在识别或操作文件对话框，请稍候…"; return; }
        if (_dialogDetectionFailed) { _status.Text = _dialogDetectionError ?? "未能完成对话框识别，请关闭后重新呼出搜索。"; return; }
        if (UseDialog)
        {
            var folder = Selected is { } item ? (item.IsFolder ? item.FullPath : Path.GetDirectoryName(item.FullPath)) : _query.Text.Trim().Trim('"');
            await NavigateFolderAsync(folder ?? "");
            return;
        }
        try
        {
        if (Selected is not { } result) return;
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
        if (string.IsNullOrWhiteSpace(_query.Text)) return;
        try
        {
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
        if (keyData == Keys.F5) { ScheduleSearch(); return true; }
        if (keyData == Keys.Enter) { RunAction(OpenSelected); return true; }
        if (keyData == (Keys.Control | Keys.Enter)) { RunAction(LocateSelected); return true; }
        if (keyData == (Keys.Control | Keys.C) && _results.Focused) { RunAction(CopySelectedPath); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showSession++; _dialogProbe?.Cancel(); _dialogProbe?.Dispose();
            _revision++; _searchCts?.Cancel(); _searchCts?.Dispose();
            _debounce.Dispose(); _client.Dispose(); _menu.Dispose(); _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
