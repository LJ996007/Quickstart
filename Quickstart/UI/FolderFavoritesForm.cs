namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;

public sealed class FolderFavoritesForm : Form
{
    private readonly ConfigManager _config;
    private readonly List<QuickEntry> _local;
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _opusFile = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _track = new() { Text = "读取 Opus 最近目录，并记录当前访问的文件夹", AutoSize = true };
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(780, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly TextBox _name = new() { Width = 170, PlaceholderText = "收藏名称" };
    private readonly TextBox _path = new() { Width = 380, PlaceholderText = "完整文件夹路径" };

    public FolderFavoritesForm(ConfigManager config)
    {
        _config = config;
        _local = config.Config.Entries.Where(e => e.Type == EntryType.Folder).OrderBy(e => e.SortOrder)
            .Select(e => new QuickEntry { Id = e.Id, Name = e.Name, Path = e.Path, Type = e.Type, Group = e.Group,
                SortOrder = e.SortOrder, AddedAt = e.AddedAt, LastUsedAt = e.LastUsedAt, UseCount = e.UseCount, CustomIconPath = e.CustomIconPath }).ToList();
        Text = "目录收藏设置"; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 570); MinimumSize = new Size(820, 530);
        _source.Items.AddRange(["仅 Quickstart 收藏", "仅 Directory Opus 收藏", "Opus + Quickstart（合并去重）"]);
        _source.SelectedIndex = Math.Clamp((int)config.Config.FolderFavoriteSource, 0, 2);
        _opusFile.Text = string.IsNullOrWhiteSpace(config.Config.OpusFavoritesFile) ? FolderDirectoryService.DefaultOpusFavoritesFile : config.Config.OpusFavoritesFile;
        _track.Checked = config.Config.TrackOpusRecentFolders;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 8 };
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.AutoSize, i == 5 ? 100 : 0));
        root.Controls.Add(Row(new Label { Text = "收藏来源", AutoSize = true }, _source), 0, 0);
        var browse = new Button { Text = "选择文件…", AutoSize = true };
        browse.Click += (_, _) => { using var file = new OpenFileDialog { Filter = "Opus 收藏|*.ofv|所有文件|*.*", FileName = _opusFile.Text }; if (file.ShowDialog(this) == DialogResult.OK) _opusFile.Text = file.FileName; };
        root.Controls.Add(Row(new Label { Text = "Opus 收藏文件", AutoSize = true }, _opusFile, browse), 0, 1);
        root.Controls.Add(_track, 0, 2);
        root.Controls.Add(_info, 0, 3);
        root.Controls.Add(new Label { Text = "Quickstart 独立收藏（与主面板文件夹收藏共用；以下操作不修改 Opus）", AutoSize = true, Margin = new Padding(0, 12, 0, 6) }, 0, 4);
        _list.Columns.Add("名称", 220); _list.Columns.Add("文件夹", 540);
        root.Controls.Add(_list, 0, 5);
        var edit = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var choose = new Button { Text = "浏览…", AutoSize = true };
        choose.Click += (_, _) => { using var picker = new FolderBrowserDialog(); if (picker.ShowDialog(this) == DialogResult.OK) { _path.Text = picker.SelectedPath; _name.Text = FolderDirectoryService.FromPath(picker.SelectedPath).Name; } };
        edit.Controls.AddRange([_name, _path, choose]);
        root.Controls.Add(edit, 0, 6);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var add = new Button { Text = "添加", AutoSize = true };
        var update = new Button { Text = "更新选中", AutoSize = true };
        var remove = new Button { Text = "移除选中", AutoSize = true };
        var save = new Button { Text = "保存", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        add.Click += (_, _) => EditEntry(false); update.Click += (_, _) => EditEntry(true);
        remove.Click += (_, _) => { if (_list.SelectedItems.Count == 1) { _local.Remove((QuickEntry)_list.SelectedItems[0].Tag!); RefreshList(); } };
        save.Click += (_, _) => Save();
        actions.Controls.AddRange([add, update, remove, save, cancel]); root.Controls.Add(actions, 0, 7);
        Controls.Add(root); CancelButton = cancel;
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedItems.Count == 1) { var item = (QuickEntry)_list.SelectedItems[0].Tag!; _name.Text = item.Name; _path.Text = item.Path; } };
        _opusFile.TextChanged += (_, _) => UpdateInfo();
        _source.SelectedIndexChanged += (_, _) => UpdateInfo();
        RefreshList(); UpdateInfo();
    }
    private static Control Row(Control label, Control input, Control? button = null)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 8) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(label, 0, 0); row.Controls.Add(input, 1, 0); if (button != null) row.Controls.Add(button, 2, 0); return row;
    }
    private void UpdateInfo()
    {
        var items = FolderDirectoryService.ReadOpusFolders(_opusFile.Text, out var error);
        _info.Text = error.Length == 0 ? $"Opus 中有 {items.Count} 个有效路径；每次展开收藏时重新读取。底部按钮始终显示最近目录。"
            : error + "（Quickstart 收藏仍可独立使用）";
    }
    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var entry in _local) { var row = new ListViewItem(entry.Name) { Tag = entry }; row.SubItems.Add(entry.Path); _list.Items.Add(row); }
    }
    private void EditEntry(bool update)
    {
        var path = FolderDirectoryService.NormalizePath(_path.Text.Trim().Trim('"'));
        var selected = update && _list.SelectedItems.Count == 1 ? (QuickEntry)_list.SelectedItems[0].Tag! : null;
        if (update && selected == null) return;
        if (path.Length == 0 || !Directory.Exists(path)) { MessageBox.Show(this, "请选择存在的文件夹。", Text); return; }
        if (_local.Any(e => e != selected && string.Equals(FolderDirectoryService.NormalizePath(e.Path), path, StringComparison.OrdinalIgnoreCase)))
        { MessageBox.Show(this, "该目录已在 Quickstart 收藏中。", Text); return; }
        var entry = selected ?? new QuickEntry { Type = EntryType.Folder };
        entry.Path = path; entry.Name = string.IsNullOrWhiteSpace(_name.Text) ? FolderDirectoryService.FromPath(path).Name : _name.Text.Trim();
        if (selected == null) _local.Add(entry);
        RefreshList();
    }
    private void Save()
    {
        _config.Config.FolderFavoriteSource = (FolderFavoriteSource)_source.SelectedIndex;
        _config.Config.OpusFavoritesFile = _opusFile.Text.Trim();
        _config.Config.TrackOpusRecentFolders = _track.Checked;
        _config.Config.Entries.RemoveAll(e => e.Type == EntryType.Folder);
        for (var i = 0; i < _local.Count; i++) { _local[i].SortOrder = i; _config.Config.Entries.Add(_local[i]); }
        _config.Save(); DialogResult = DialogResult.OK; Close();
    }
}
