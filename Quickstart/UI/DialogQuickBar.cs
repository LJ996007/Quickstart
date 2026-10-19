namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;
using System.Runtime.InteropServices;

/// <summary>A directory-only companion for one captured file dialog. Never launches a shell.</summary>
public sealed class DialogQuickBar : Form
{
    private readonly ConfigManager _config;
    private readonly EverythingSearchClient _search = new();
    private readonly TextBox _query = new() { BorderStyle = BorderStyle.None, PlaceholderText = "搜索文件夹，或输入完整目录…", AccessibleName = "搜索目录" };
    private readonly Button _recent = new() { Text = "最近", FlatStyle = FlatStyle.Flat, AccessibleName = "展开最近目录" };
    private readonly Button _favorites = new() { Text = "☆ 收藏", FlatStyle = FlatStyle.Flat, AccessibleName = "展开收藏目录" };
    private readonly Button _close = new() { Text = "×", FlatStyle = FlatStyle.Flat, AccessibleName = "暂时隐藏目录助手" };
    private readonly Button _manage = new() { Text = "⚙", FlatStyle = FlatStyle.Flat, AccessibleName = "设置目录收藏" };
    private readonly FlowLayoutPanel _shortcuts = new() { WrapContents = false };
    private readonly ToolTip _tips = new();
    private readonly ListView _results = new() { View = View.Details, HeaderStyle = ColumnHeaderStyle.None,
        FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, AccessibleName = "目录列表" };
    private readonly Label _status = new() { AutoEllipsis = true, ForeColor = UiTheme.TextMuted };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 180 };
    private CancellationTokenSource? _queryCts;
    private FileDialogTarget? _target;
    private int _revision;
    private bool _expanded, _navigating, _binding;
    public event Action? Dismissed;
    public FileDialogTarget? Target => _target;
    public bool IsNavigating => _navigating;
    public bool IsManaging { get; private set; }
    protected override bool ShowWithoutActivation => true;

    public DialogQuickBar(ConfigManager config)
    {
        _config = config;
        Text = "Quickstart · 对话框目录助手";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Font = UiTheme.CreateFont(9f);
        BackColor = UiTheme.Surface;
        FormStyler.ApplyRounded(this);
        Controls.AddRange([_query, _recent, _favorites, _manage, _close, _shortcuts, _results, _status]);
        _results.Columns.Add("目录"); _results.Columns.Add("路径");
        foreach (var button in new[] { _recent, _favorites, _manage, _close }) { UiTheme.StyleFlatButton(button, BackColor); button.ForeColor = UiTheme.TextSecondary; }
        _tips.SetToolTip(_recent, "展开最近使用的目录");
        _tips.SetToolTip(_manage, "设置收藏来源 / 管理 Quickstart 收藏");
        _manage.Click += (_, _) =>
        {
            IsManaging = true; Hide();
            try { using var settings = new FolderFavoritesForm(_config); settings.ShowDialog(); }
            finally { IsManaging = false; RefreshShortcuts(); ShowFavorites(true); ShowAttached(true); }
        };
        _favorites.Click += (_, _) => { _binding = true; _query.Clear(); _binding = false; ShowFavorites(true); };
        _recent.Click += (_, _) => { _binding = true; _query.Clear(); _binding = false; ShowRecent(); };
        _close.Click += (_, _) => { Hide(); Dismissed?.Invoke(); };
        _query.TextChanged += (_, _) => { if (!_binding) ScheduleSearch(); };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _results.Items.Count > 0)
            { _results.Focus(); _results.Items[0].Selected = true; _results.Items[0].Focused = true; e.SuppressKeyPress = true; }
        };
        _results.MouseUp += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var item = _results.GetItemAt(e.X, e.Y);
            if (item?.Tag is string path) await NavigateAsync(path);
        };
        VisibleChanged += (_, _) => { if (!Visible) CancelSearch(); };
        Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, ClientRectangle, UiTheme.Border, ButtonBorderStyle.Solid);
    }

    public void Bind(FileDialogTarget target)
    {
        if (_target == target) return;
        CancelSearch();
        _target = target;
        _binding = true; _query.Clear(); _binding = false;
        _expanded = false;
        _results.Items.Clear();
        RefreshShortcuts();
        _status.Text = $"{target.Description} · 点击最近或收藏展开目录";
    }

    private void RefreshShortcuts()
    {
        foreach (Control child in _shortcuts.Controls.Cast<Control>().ToArray()) child.Dispose();
        foreach (var entry in FolderDirectoryService.GetRecent(_config.Config).Take(4))
        {
            var button = new Button { Text = "▸ " + entry.Name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 0), Padding = new Padding(5, 0, 5, 0),
                AccessibleName = "最近目录：" + entry.Name, Tag = entry.Path, AutoEllipsis = true };
            button.FlatAppearance.BorderColor = UiTheme.BorderSubtle;
            button.FlatAppearance.MouseOverBackColor = UiTheme.SelectionFill;
            button.BackColor = UiTheme.Card; button.ForeColor = UiTheme.TextPrimary;
            button.Click += async (_, _) => await NavigateAsync(entry.Path);
            _tips.SetToolTip(button, entry.Path);
            _shortcuts.Controls.Add(button);
        }
        if (_shortcuts.Controls.Count == 0)
            _shortcuts.Controls.Add(new Label { Text = "暂无最近目录；点击 ☆ 收藏选择目录", AutoSize = true, ForeColor = UiTheme.TextMuted });
    }

    public void ShowAttached(bool focus = false, string? query = null)
    {
        if (_target == null || !FileDialogNavigator.IsLiveWindow(_target)) return;
        if (query != null)
        {
            _binding = true; _query.Text = query; _binding = false;
            if (string.IsNullOrWhiteSpace(query)) ShowRecent(); else ScheduleSearch();
        }
        if (!Visible) RefreshShortcuts();
        Reposition();
        if (!Visible) Show(new DialogOwner(_target.Window));
        // WPS can raise its modal again after ShowWithoutActivation. Keep this companion
        // above that dialog without making it globally topmost or moving keyboard focus.
        if (FileDialogNavigator.ResolveDialogWindow(FileDialogNavigator.CaptureForegroundWindow()) == _target.Window)
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
        if (focus) { Activate(); _query.Focus(); _query.SelectAll(); }
    }

    public void Reposition()
    {
        if (_target == null || !FileDialogNavigator.TryGetBounds(_target, out var dialog)) { Hide(); return; }
        var work = Screen.FromRectangle(dialog).WorkingArea;
        var scale = DeviceDpi / 96d;
        int S(int value) => (int)Math.Round(value * scale);
        var layout = DialogQuickBarLayout.Calculate(dialog, work, DeviceDpi, _expanded);
        var width = layout.Bounds.Width;
        var height = layout.Bounds.Height;
        var inputOffset = layout.InputOffset;
        SuspendLayout();
        Bounds = layout.Bounds;
        _query.SetBounds(S(14), inputOffset + S(13), Math.Max(S(80), width - S(228)), S(24));
        _recent.SetBounds(width - S(206), inputOffset + S(6), S(64), S(32));
        _favorites.SetBounds(width - S(142), inputOffset + S(6), S(76), S(32));
        _manage.SetBounds(width - S(65), inputOffset + S(6), S(30), S(32));
        _close.SetBounds(width - S(33), inputOffset + S(6), S(27), S(32));
        _shortcuts.SetBounds(S(12), inputOffset + S(42), width - S(24), S(28));
        foreach (Control shortcut in _shortcuts.Controls)
            if (shortcut is Button) shortcut.MaximumSize = new Size(Math.Max(S(40), (_shortcuts.Width - S(24)) / 4), S(28));
        _results.Visible = _expanded && layout.ResultsHeight > 0;
        if (_expanded && layout.ResultsHeight > 0) _results.SetBounds(S(12), layout.ResultsTop, width - S(24), layout.ResultsHeight);
        _status.SetBounds(S(12), height - S(22), width - S(24), S(19));
        _results.Columns[0].Width = Math.Max(S(100), (width - S(30)) / 3);
        _results.Columns[1].Width = Math.Max(S(60), width - S(52) - _results.Columns[0].Width);
        ResumeLayout(false);
    }

    private sealed class DialogOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle => handle;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private void ShowFavorites(bool expand)
    {
        CancelSearch(); _expanded = expand; _results.Items.Clear(); RefreshShortcuts();
        var folders = FolderDirectoryService.GetFavorites(_config.Config, out var error);
        foreach (var item in folders) AddRow(item.Name, item.Path);
        var source = _config.Config.FolderFavoriteSource switch { FolderFavoriteSource.Quickstart => "Quickstart", FolderFavoriteSource.DirectoryOpus => "Opus", _ => "Opus + Quickstart" };
        _status.Text = $"收藏目录（{source}）· {_results.Items.Count} 项 · ⚙ 设置来源和管理收藏";
        if (error.Length > 0) { _status.Text = $"收藏目录 · {_results.Items.Count} 项 · Opus 读取失败，点击 ⚙ 检查路径"; _tips.SetToolTip(_status, error); }
        else _tips.SetToolTip(_status, null);
        SelectFirst(); Reposition();
    }

    private void ShowRecent()
    {
        CancelSearch(); _expanded = true; _results.Items.Clear(); RefreshShortcuts();
        foreach (var item in FolderDirectoryService.GetRecent(_config.Config)) AddRow(item.Name, item.Path);
        _status.Text = $"最近目录 · {_results.Items.Count} 项 · 单击跳转";
        SelectFirst(); Reposition();
    }

    private void AddRow(string name, string path)
    {
        var row = new ListViewItem(name) { Tag = path, ToolTipText = path };
        row.SubItems.Add(path); _results.Items.Add(row);
    }
    private void SelectFirst() { if (_results.Items.Count > 0) _results.Items[0].Selected = true; }
    private void CancelSearch() { _revision++; _debounce.Stop(); _queryCts?.Cancel(); }
    private void ScheduleSearch()
    {
        CancelSearch(); _results.Items.Clear(); _expanded = true;
        if (string.IsNullOrWhiteSpace(_query.Text)) { ShowRecent(); return; }
        _status.Text = "正在搜索文件夹…"; Reposition(); _debounce.Start();
    }
    private async Task SearchAsync()
    {
        _queryCts?.Dispose(); _queryCts = new CancellationTokenSource();
        var token = _queryCts.Token; var revision = ++_revision;
        var query = _query.Text.Trim();
        try
        {
            var page = await _search.SearchAsync(_config.Config.EverythingPath, $"folder: <{query}>", 0, 1, token);
            if (IsDisposed || token.IsCancellationRequested || revision != _revision) return;
            _results.Items.Clear();
            foreach (var item in page.Items.Where(i => i.IsFolder)) AddRow(item.Name, item.FullPath);
            SelectFirst();
            _status.Text = page.Total == 0 ? "未找到目录；完整目录路径可直接按 Enter" : $"找到 {page.Total:N0} 个目录 · 显示前 {_results.Items.Count} 个 · 单击跳转";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && revision == _revision) _status.Text = ex.Message; }
    }

    private async Task NavigateAsync(string folder)
    {
        if (_navigating || _target == null) return;
        _navigating = true;
        var target = _target;
        CancelSearch();
        try
        {
            // Keep the captured target. No ProcessLauncher/Explorer fallback, including failures.
            await FileDialogNavigator.NavigateAsync(target, folder);
            if (IsDisposed || _target != target) return;
            _config.RecordRecentFolder(folder);
            var favorite = _config.Config.Entries.FirstOrDefault(e => e.Type == EntryType.Folder && string.Equals(e.Path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (favorite != null) _config.TouchEntry(favorite.Id);
            _binding = true; _query.Clear(); _binding = false;
            _expanded = false; _results.Items.Clear();
            RefreshShortcuts();
            _status.Text = "已切换目录 · 原文件名已保留";
            Reposition();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && _target == target)
                _status.Text = ex is OperationCanceledException ? "跳转超时，请检查原对话框" : ex.Message;
        }
        finally { _navigating = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { _expanded = false; _binding = true; _query.Clear(); _binding = false; CancelSearch(); Reposition(); return true; }
        if (keyData == (Keys.Control | Keys.L)) { _query.Focus(); _query.SelectAll(); return true; }
        if (keyData is Keys.Enter || keyData == (Keys.Control | Keys.Enter))
        {
            var typed = _query.Text.Trim().Trim('"');
            var path = _query.Focused && Path.IsPathFullyQualified(typed) ? typed
                : _results.SelectedItems.Count > 0 ? (string)_results.SelectedItems[0].Tag! : typed;
            _ = NavigateAsync(path); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { CancelSearch(); _queryCts?.Dispose(); _debounce.Dispose(); _search.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
