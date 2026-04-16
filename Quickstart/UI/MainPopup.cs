namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class MainPopup : Form
{
    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ImageList _imageList;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly ContextMenuStrip _itemMenu;

    public event Action? ShowSettings;

    public MainPopup(ConfigManager configManager, ProcessLauncher launcher)
    {
        _configManager = configManager;
        _launcher = launcher;

        AutoScaleMode = AutoScaleMode.Dpi;

        // Form settings - borderless popup style
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(420, 500);
        BackColor = Color.FromArgb(250, 250, 250);
        TopMost = true;

        // Rounded border panel
        var border = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(200, 200, 200)
        };

        var inner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 250, 250)
        };

        // Search box
        _searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 11),
            PlaceholderText = "搜索文件夹或文件... (拼音首字母也可)",
            BorderStyle = BorderStyle.None,
            Height = 40,
            Padding = new Padding(8, 8, 8, 8),
            BackColor = Color.White
        };

        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 10, 8, 2),
            BackColor = Color.White
        };
        searchPanel.Controls.Add(_searchBox);

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(220, 220, 220)
        };

        // Toolbar
        var toolbar = CreateToolbar();

        // Image list for icons
        _imageList = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };

        // ListView
        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            SmallImageList = _imageList,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
            ShowGroups = false,
            MultiSelect = false
        };

        _listView.Columns.Add("名称", 170);
        _listView.Columns.Add("路径", 230);

        // Item context menu
        _itemMenu = BuildItemContextMenu();
        _listView.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _listView.FocusedItem?.Bounds.Contains(e.Location) == true)
            {
                _itemMenu.Show(_listView, e.Location);
            }
        };

        _listView.MouseDoubleClick += (_, e) =>
        {
            if (_listView.SelectedItems.Count > 0)
                OpenSelectedEntry();
        };

        _listView.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _listView.SelectedItems.Count > 0)
            {
                OpenSelectedEntry();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && _listView.SelectedItems.Count > 0)
            {
                DeleteSelectedEntry();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        // Debounce timer for search
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RefreshList();
        };

        _searchBox.TextChanged += (_, _) =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };

        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _listView.Items.Count > 0)
            {
                _listView.Focus();
                _listView.Items[0].Selected = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _listView.Items.Count > 0)
            {
                _listView.Items[0].Selected = true;
                OpenSelectedEntry();
                e.Handled = true;
            }
        };

        // Enable drag-drop
        AllowDrop = true;
        _listView.AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _listView.DragEnter += OnDragEnter;
        _listView.DragDrop += OnDragDrop;

        // Assembly: inner contains toolbar at bottom, listview fills, separator, searchpanel at top
        inner.Controls.Add(_listView);      // Fill
        inner.Controls.Add(toolbar);        // Bottom
        inner.Controls.Add(separator);      // Top (below search)
        inner.Controls.Add(searchPanel);    // Top

        border.Controls.Add(inner);
        Controls.Add(border);

        // Hide on deactivate
        Deactivate += (_, _) =>
        {
            if (Visible) Hide();
        };
    }

    private Panel CreateToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(245, 245, 245),
            Padding = new Padding(8, 6, 8, 6)
        };

        var addBtn = new Button
        {
            Text = "+ 添加",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(96, 34),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(8, 8),
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White
        };
        addBtn.FlatAppearance.BorderSize = 0;
        addBtn.Click += (_, _) => AddNewEntry();

        var settingsBtn = new Button
        {
            Text = "设置",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(86, 34),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(110, 8),
            BackColor = Color.FromArgb(245, 245, 245),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        settingsBtn.FlatAppearance.BorderSize = 1;
        settingsBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        settingsBtn.Click += (_, _) => ShowSettings?.Invoke();

        var countLabel = new Label
        {
            Name = "countLabel",
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 90,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(150, 150, 150),
            Padding = new Padding(0, 0, 4, 0),
            TextAlign = ContentAlignment.MiddleRight
        };

        toolbar.Controls.Add(countLabel);
        toolbar.Controls.Add(addBtn);
        toolbar.Controls.Add(settingsBtn);
        return toolbar;
    }

    private ContextMenuStrip BuildItemContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openDefault = new ToolStripMenuItem("打开");
        openDefault.Font = new Font(openDefault.Font, FontStyle.Bold);
        openDefault.Click += (_, _) => OpenSelectedEntry();
        menu.Items.Add(openDefault);

        var openTc = new ToolStripMenuItem("用 Total Commander 打开");
        openTc.Click += (_, _) => OpenSelectedEntry(OpenWith.TotalCommander);
        menu.Items.Add(openTc);

        var openExplorer = new ToolStripMenuItem("用资源管理器打开");
        openExplorer.Click += (_, _) => OpenSelectedEntry(OpenWith.Explorer);
        menu.Items.Add(openExplorer);

        menu.Items.Add(new ToolStripSeparator());

        var editItem = new ToolStripMenuItem("编辑(&E)");
        editItem.Click += (_, _) => EditSelectedEntry();
        menu.Items.Add(editItem);

        var deleteItem = new ToolStripMenuItem("删除(&D)");
        deleteItem.Click += (_, _) => DeleteSelectedEntry();
        menu.Items.Add(deleteItem);

        menu.Items.Add(new ToolStripSeparator());

        var locateItem = new ToolStripMenuItem("在资源管理器中定位");
        locateItem.Click += (_, _) =>
        {
            var entry = GetSelectedEntry();
            if (entry != null)
                ProcessLauncher.OpenInExplorer(entry.Path);
        };
        menu.Items.Add(locateItem);

        return menu;
    }

    public void ShowPopup()
    {
        RefreshList();
        PositionNearTray();
        _searchBox.Clear();
        Show();
        Activate();
        _searchBox.Focus();
    }

    public void AddPathEntry(string path)
    {
        var isDir = Directory.Exists(path);
        var isFile = File.Exists(path);
        if (!isDir && !isFile) return;

        var entry = new QuickEntry
        {
            Name = Path.GetFileName(path),
            Path = path,
            Type = isDir ? EntryType.Folder : EntryType.File
        };

        using var form = new EntryEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.AddEntry(entry);
            RefreshList();
        }
    }

    private void AddNewEntry()
    {
        var entry = new QuickEntry();
        using var form = new EntryEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.AddEntry(entry);
            RefreshList();
        }
    }

    private void EditSelectedEntry()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        using var form = new EntryEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.UpdateEntry(entry);
            RefreshList();
        }
    }

    private void DeleteSelectedEntry()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        var result = MessageBox.Show(
            $"确定要删除 \"{entry.Name}\" 吗？",
            "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _configManager.RemoveEntry(entry.Id);
            RefreshList();
        }
    }

    private void OpenSelectedEntry(OpenWith? overrideWith = null)
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;
        _launcher.Open(entry, overrideWith);
        Hide();
    }

    private QuickEntry? GetSelectedEntry()
    {
        if (_listView.SelectedItems.Count == 0) return null;
        return _listView.SelectedItems[0].Tag as QuickEntry;
    }

    public void RefreshList()
    {
        var query = _searchBox.Text.Trim();
        var entries = _configManager.Config.Entries;

        if (!string.IsNullOrEmpty(query))
        {
            entries = entries
                .Where(e => PinyinHelper.MatchesPinyin(e.Name, query)
                         || PinyinHelper.MatchesPinyin(e.Path, query))
                .ToList();
        }

        entries = entries.OrderBy(e => e.SortOrder).ToList();

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var entry in entries)
        {
            var iconKey = entry.Type == EntryType.Folder ? "<DIR>" : Path.GetExtension(entry.Path).ToLowerInvariant();
            if (string.IsNullOrEmpty(iconKey)) iconKey = "<NOEXT>";

            if (!_imageList.Images.ContainsKey(iconKey))
            {
                var icon = IconExtractor.GetIcon(entry.Path, entry.Type == EntryType.Folder);
                if (icon != null)
                    _imageList.Images.Add(iconKey, icon);
            }

            var item = new ListViewItem(entry.Name, iconKey)
            {
                Tag = entry
            };
            item.SubItems.Add(entry.Path);

            if (!PathExists(entry))
            {
                item.ForeColor = Color.Red;
            }

            _listView.Items.Add(item);
        }

        _listView.EndUpdate();

        // Update count label
        var countLabel = Controls.Find("countLabel", true).FirstOrDefault() as Label;
        if (countLabel != null)
            countLabel.Text = $"{entries.Count} 项";
    }

    private static bool PathExists(QuickEntry entry)
    {
        return entry.Type == EntryType.Folder
            ? Directory.Exists(entry.Path)
            : File.Exists(entry.Path);
    }

    private void PositionNearTray()
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;
        var taskbarOnBottom = screen.Bottom < Screen.PrimaryScreen.Bounds.Bottom;

        int x = screen.Right - Width - 8;
        int y = taskbarOnBottom
            ? screen.Bottom - Height - 8
            : screen.Top + 8;

        Location = new Point(x, y);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Link;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var path in files)
            {
                AddPathEntry(path);
            }
        }
    }

    protected override bool ShowWithoutActivation => false;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - hide from Alt+Tab
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var pen = new Pen(Color.FromArgb(200, 200, 200));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
