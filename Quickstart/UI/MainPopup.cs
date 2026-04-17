namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class MainPopup : Form
{
    // Which tab category is active
    private enum TabKind { Files, Urls, Texts }

    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ImageList _imageList;
    private readonly Image? _webEntryImage;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly Label[] _tabLabels;
    private readonly Panel _tabPanel;
    private TabKind _activeTab = TabKind.Files;

    // "Copied!" toast label (shown briefly after text copy)
    private readonly Label _toastLabel;
    private readonly System.Windows.Forms.Timer _toastTimer;

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

        // ── Right-side vertical tab panel ──
        _tabPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 36,
            BackColor = Color.FromArgb(240, 240, 240)
        };

        string[] tabTexts = ["文\n件", "网\n页", "文\n本"];
        TabKind[] tabKinds = [TabKind.Files, TabKind.Urls, TabKind.Texts];
        _tabLabels = new Label[3];

        for (int i = 0; i < 3; i++)
        {
            var kind = tabKinds[i];
            var lbl = new Label
            {
                Text = tabTexts[i],
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9f),
                Dock = DockStyle.Top,
                Height = 56,
                Cursor = Cursors.Hand
            };
            lbl.Click += (_, _) => SwitchTab(kind);
            _tabLabels[i] = lbl;
        }

        // Add in reverse order so the first tab is on top (Dock=Top stacks)
        for (int i = _tabLabels.Length - 1; i >= 0; i--)
            _tabPanel.Controls.Add(_tabLabels[i]);

        ApplyTabStyles();

        // Separator between tabs and content
        var tabSep = new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = Color.FromArgb(220, 220, 220)
        };

        // Image list for icons
        _imageList = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        _webEntryImage = LoadWebEntryImage();

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

        _listView.Columns.Add("名称", 358);
        _listView.OwnerDraw = true;
        _listView.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _listView.DrawItem += (_, e) =>
        {
            // 只画选中背景，文字由 DrawSubItem 负责
            using var bg = new SolidBrush(e.Item.Selected ? SystemColors.Highlight : _listView.BackColor);
            e.Graphics.FillRectangle(bg, e.Bounds);
        };
        _listView.DrawSubItem += OnDrawSubItem;

        // Item context menu — built dynamically on each right-click
        _listView.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _listView.FocusedItem?.Bounds.Contains(e.Location) == true)
            {
                var menu = BuildItemContextMenu();
                menu.Show(_listView, e.Location);
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

        // Enable drag-drop (file tab only — checked in OnDragEnter)
        AllowDrop = true;
        _listView.AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _listView.DragEnter += OnDragEnter;
        _listView.DragDrop += OnDragDrop;

        // Toast label for "已复制" feedback
        _toastLabel = new Label
        {
            Text = "已复制",
            AutoSize = false,
            Size = new Size(80, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 60, 60),
            Visible = false
        };
        _toastTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toastLabel.Visible = false;
        };

        // Assembly: inner contains toolbar at bottom, tab panel right, listview fills, separator, searchpanel at top
        inner.Controls.Add(_listView);      // Fill
        inner.Controls.Add(_toastLabel);    // Overlay (positioned in RefreshList)
        inner.Controls.Add(toolbar);        // Bottom
        inner.Controls.Add(tabSep);         // Right (separator)
        inner.Controls.Add(_tabPanel);      // Right (tabs)
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

    // ── Tab switching ──

    private void SwitchTab(TabKind kind)
    {
        if (_activeTab == kind) return;
        _activeTab = kind;
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        RefreshList();
    }

    private void ApplyTabStyles()
    {
        TabKind[] kinds = [TabKind.Files, TabKind.Urls, TabKind.Texts];
        for (int i = 0; i < _tabLabels.Length; i++)
        {
            bool active = kinds[i] == _activeTab;
            _tabLabels[i].BackColor = active ? Color.FromArgb(60, 60, 60) : Color.FromArgb(240, 240, 240);
            _tabLabels[i].ForeColor = active ? Color.White : Color.FromArgb(80, 80, 80);
        }
    }

    private void UpdateSearchPlaceholder()
    {
        _searchBox.PlaceholderText = _activeTab switch
        {
            TabKind.Files => "搜索文件夹或文件... (拼音首字母也可)",
            TabKind.Urls  => "搜索网页...",
            TabKind.Texts => "搜索文本...",
            _ => "搜索..."
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
            Size = new Size(96, 34),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(8, 8)
        };
        ButtonStyler.ApplyPrimary(addBtn);
        addBtn.Click += (_, _) => AddNewEntry();

        var settingsBtn = new Button
        {
            Text = "设置",
            Size = new Size(86, 34),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(110, 8)
        };
        ButtonStyler.ApplySecondary(settingsBtn);
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
        var entry = GetSelectedEntry();
        var menu = new ContextMenuStrip();

        if (entry == null) return menu;

        switch (entry.Type)
        {
            case EntryType.Folder:
            case EntryType.File:
            {
                var openDefault = new ToolStripMenuItem("打开");
                openDefault.Font = new Font(openDefault.Font, FontStyle.Bold);
                openDefault.Click += (_, _) => OpenSelectedEntry();
                menu.Items.Add(openDefault);

                if (entry.Type == EntryType.Folder)
                {
                    var openTc = new ToolStripMenuItem("用 Total Commander 打开");
                    openTc.Click += (_, _) => OpenSelectedEntry(OpenWith.TotalCommander);
                    menu.Items.Add(openTc);

                    var openExplorer = new ToolStripMenuItem("用资源管理器打开");
                    openExplorer.Click += (_, _) => OpenSelectedEntry(OpenWith.Explorer);
                    menu.Items.Add(openExplorer);

                    var openDopus = new ToolStripMenuItem("用 Directory Opus 打开");
                    openDopus.Click += (_, _) => OpenSelectedEntry(OpenWith.DirectoryOpus);
                    menu.Items.Add(openDopus);
                }

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
                    var e2 = GetSelectedEntry();
                    if (e2 != null) ProcessLauncher.OpenInExplorer(e2.Path);
                };
                menu.Items.Add(locateItem);
                break;
            }

            case EntryType.Url:
            {
                var openUrl = new ToolStripMenuItem("在浏览器中打开");
                openUrl.Font = new Font(openUrl.Font, FontStyle.Bold);
                openUrl.Click += (_, _) => OpenSelectedEntry();
                menu.Items.Add(openUrl);

                var copyUrl = new ToolStripMenuItem("复制网址");
                copyUrl.Click += (_, _) =>
                {
                    var e2 = GetSelectedEntry();
                    if (e2 != null) CopyToClipboard(e2.Path);
                };
                menu.Items.Add(copyUrl);

                menu.Items.Add(new ToolStripSeparator());

                var editUrl = new ToolStripMenuItem("编辑(&E)");
                editUrl.Click += (_, _) => EditSelectedEntry();
                menu.Items.Add(editUrl);

                var deleteUrl = new ToolStripMenuItem("删除(&D)");
                deleteUrl.Click += (_, _) => DeleteSelectedEntry();
                menu.Items.Add(deleteUrl);
                break;
            }

            case EntryType.Text:
            {
                var copyText = new ToolStripMenuItem("复制文本");
                copyText.Font = new Font(copyText.Font, FontStyle.Bold);
                copyText.Click += (_, _) => OpenSelectedEntry();
                menu.Items.Add(copyText);

                menu.Items.Add(new ToolStripSeparator());

                var editText = new ToolStripMenuItem("编辑(&E)");
                editText.Click += (_, _) => EditSelectedEntry();
                menu.Items.Add(editText);

                var deleteText = new ToolStripMenuItem("删除(&D)");
                deleteText.Click += (_, _) => DeleteSelectedEntry();
                menu.Items.Add(deleteText);
                break;
            }
        }

        return menu;
    }

    public void ShowPopup()
    {
        _activeTab = TabKind.Files;
        ApplyTabStyles();
        UpdateSearchPlaceholder();
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
        var entry = new QuickEntry
        {
            Type = _activeTab switch
            {
                TabKind.Urls  => EntryType.Url,
                TabKind.Texts => EntryType.Text,
                _ => EntryType.Folder
            }
        };
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
        ExecuteEntry(entry, overrideWith);
        Hide();
    }

    /// <summary>Execute the action for an entry based on its type.</summary>
    private void ExecuteEntry(QuickEntry entry, OpenWith? overrideWith = null)
    {
        switch (entry.Type)
        {
            case EntryType.Url:
                _configManager.TouchEntry(entry.Id);
                ProcessLauncher.OpenUrl(entry.Path);
                break;
            case EntryType.Text:
                _configManager.TouchEntry(entry.Id);
                CopyToClipboard(entry.Path);
                break;
            default:
                _launcher.Open(entry, overrideWith);
                break;
        }
    }

    private void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
        ShowToast();
    }

    private void ShowToast()
    {
        // Position toast in center of the ListView
        _toastLabel.Location = new Point(
            _listView.Left + (_listView.Width - _toastLabel.Width) / 2,
            _listView.Top + (_listView.Height - _toastLabel.Height) / 2);
        _toastLabel.BringToFront();
        _toastLabel.Visible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
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

        // Filter by active tab
        entries = _activeTab switch
        {
            TabKind.Files => entries.Where(e => e.Type is EntryType.Folder or EntryType.File).ToList(),
            TabKind.Urls  => entries.Where(e => e.Type == EntryType.Url).ToList(),
            TabKind.Texts => entries.Where(e => e.Type == EntryType.Text).ToList(),
            _ => entries
        };

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
            string? iconKey;
            if (entry.Type == EntryType.Url)
                iconKey = _webEntryImage != null ? "<URL_CUSTOM>" : ".url";
            else if (entry.Type == EntryType.Text)
                iconKey = null;
            else if (entry.Type == EntryType.Folder)
                iconKey = "<DIR>";
            else
            {
                iconKey = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (string.IsNullOrEmpty(iconKey)) iconKey = "<NOEXT>";
            }

            if (!string.IsNullOrEmpty(iconKey) && !_imageList.Images.ContainsKey(iconKey))
            {
                if (entry.Type == EntryType.Url)
                {
                    if (_webEntryImage != null)
                    {
                        _imageList.Images.Add(iconKey, _webEntryImage);
                    }
                    else
                    {
                        var icon = IconExtractor.GetIcon(iconKey, isDirectory: false);
                        if (icon != null)
                            _imageList.Images.Add(iconKey, icon);
                    }
                }
                else
                {
                    var icon = IconExtractor.GetIcon(entry.Path, entry.Type == EntryType.Folder);
                    if (icon != null)
                        _imageList.Images.Add(iconKey, icon);
                }
            }

            var item = string.IsNullOrEmpty(iconKey)
                ? new ListViewItem(entry.Name)
                : new ListViewItem(entry.Name, iconKey)
                {
                    ImageKey = iconKey
                };

            item.Tag = entry;
            item.ToolTipText = entry.Type == EntryType.Text
                ? (entry.Path.Length > 200 ? entry.Path[..200] + "..." : entry.Path)
                : entry.Path;

            if (entry.Type is EntryType.Folder or EntryType.File && !PathExists(entry))
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

    private static Image? LoadWebEntryImage()
    {
        try
        {
            var asm = typeof(MainPopup).Assembly;
            using var stream = asm.GetManifestResourceStream("Quickstart.Resources.web-url.png");
            if (stream == null) return null;

            using var original = Image.FromStream(stream);
            return new Bitmap(original, new Size(16, 16));
        }
        catch
        {
            return null;
        }
    }

    private void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics;
        var item = e.Item!;
        var bounds = e.Bounds;

        // 图标（仅第 0 列）
        int textX = bounds.X + 2;
        if (e.ColumnIndex == 0 && _listView.SmallImageList is { } imgList)
        {
            bool drewIcon = false;
            var key = item.ImageKey;
            if (!string.IsNullOrEmpty(key) && imgList.Images.ContainsKey(key))
            {
                var img = imgList.Images[key];
                if (img != null)
                {
                    int iconY = bounds.Y + (bounds.Height - imgList.ImageSize.Height) / 2;
                    g.DrawImage(img, textX, iconY, imgList.ImageSize.Width, imgList.ImageSize.Height);
                    drewIcon = true;
                }
            }
            if (drewIcon)
                textX += imgList.ImageSize.Width + 4;
        }

        // 文字颜色
        var textColor = item.Selected ? SystemColors.HighlightText : item.ForeColor;

        // 文字区域
        var textBounds = new Rectangle(textX, bounds.Y, bounds.Right - textX - 2, bounds.Height);
        var display = MidTruncate(item.Text, _listView.Font, textBounds.Width);

        TextRenderer.DrawText(g, display, _listView.Font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private static string MidTruncate(string text, Font font, int maxPx)
    {
        if (string.IsNullOrEmpty(text) || maxPx <= 0) return text;
        if (TextRenderer.MeasureText(text, font).Width <= maxPx) return text;

        const string dots = "...";
        int dotsW = TextRenderer.MeasureText(dots, font).Width;
        int avail = maxPx - dotsW;
        if (avail <= 0) return dots;

        // 二分求起始段可放字符数（占一半空间）
        int half = avail / 2;
        int lo = 0, hi = text.Length / 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (TextRenderer.MeasureText(text[..mid], font).Width <= half) lo = mid;
            else hi = mid - 1;
        }
        int startLen = lo;
        int usedStart = startLen > 0 ? TextRenderer.MeasureText(text[..startLen], font).Width : 0;

        // 剩余空间给末尾段
        int endAvail = avail - usedStart;
        lo = 0; hi = text.Length - startLen;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (TextRenderer.MeasureText(text[^mid..], font).Width <= endAvail) lo = mid;
            else hi = mid - 1;
        }
        int endLen = lo;

        return text[..startLen] + dots + (endLen > 0 ? text[^endLen..] : "");
    }

    private static bool PathExists(QuickEntry entry)
    {
        return entry.Type == EntryType.Folder
            ? Directory.Exists(entry.Path)
            : File.Exists(entry.Path);
    }

    public void ShowAtGesturePoint(Point screenPt)
    {
        // Reset to Files tab for gesture
        _activeTab = TabKind.Files;
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        RefreshList();
        _searchBox.Clear();

        // Position so cursor lands in the list area (search panel is ~49px tall, add 8px margin)
        var wa = Screen.FromPoint(screenPt).WorkingArea;
        int x = Math.Max(wa.Left, Math.Min(screenPt.X - 8, wa.Right - Width));
        int y = Math.Max(wa.Top, Math.Min(screenPt.Y - 57, wa.Bottom - Height));
        Location = new Point(x, y);

        Show(); // No Activate() — keep focus on drag source window
    }

    public void HighlightAtScreenPoint(Point screenPt)
    {
        // Check if mouse is over a tab label → auto-switch tab
        var tabClientPt = _tabPanel.PointToClient(screenPt);
        if (_tabPanel.ClientRectangle.Contains(tabClientPt))
        {
            TabKind[] kinds = [TabKind.Files, TabKind.Urls, TabKind.Texts];
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i].Bounds.Contains(tabClientPt))
                {
                    if (_activeTab != kinds[i])
                        SwitchTab(kinds[i]);
                    break;
                }
            }
            // Clear list selection when over tabs
            if (_listView.SelectedItems.Count > 0)
                _listView.SelectedItems.Clear();
            return;
        }

        // Otherwise highlight list item
        var clientPt = _listView.PointToClient(screenPt);
        var item = _listView.GetItemAt(clientPt.X, clientPt.Y);
        if (item != null)
        {
            if (!item.Selected)
            {
                _listView.SelectedItems.Clear();
                item.Selected = true;
            }
        }
        else
        {
            if (_listView.SelectedItems.Count > 0)
                _listView.SelectedItems.Clear();
        }
    }

    public bool TryReleaseAtScreenPoint(Point screenPt)
    {
        if (!Bounds.Contains(screenPt))
        {
            Hide();
            return false;
        }

        var clientPt = _listView.PointToClient(screenPt);
        var item = _listView.GetItemAt(clientPt.X, clientPt.Y);
        if (item?.Tag is QuickEntry entry)
        {
            ExecuteEntry(entry);
            Hide();
            return true;
        }

        // Released in popup but not on an entry — activate so user can continue interacting
        Activate();
        _searchBox.Focus();
        return false;
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
        if (_activeTab == TabKind.Files && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
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

    protected override bool ShowWithoutActivation => true;

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
