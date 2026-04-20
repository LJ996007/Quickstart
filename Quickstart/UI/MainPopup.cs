namespace Quickstart.UI;

using System.Globalization;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class MainPopup : Form
{
    private enum TabKind { Files, Urls, Texts }

    private static readonly Size ExpandedPopupLogicalSize = new(420, 500);
    private static readonly Size MinimumExpandedPopupLogicalSize = new(320, 380);
    private const int CollapsedPopupHeightDeltaLogical = 28;
    private const string AllGroupsLabel = "全部";

    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ImageList _imageList;
    private Image? _webEntryImage;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly Label[] _tabLabels;
    private readonly TableLayoutPanel _tabLayout;
    private readonly FlowLayoutPanel _groupLayout;
    private readonly List<Label> _groupLabels = [];
    private readonly Label _toastLabel;
    private readonly System.Windows.Forms.Timer _toastTimer;
    private readonly TableLayoutPanel _outerLayout;
    private readonly Panel _searchPanel;
    private readonly Panel _searchCollapsedPanel;
    private readonly Panel _searchCollapsedIndicator;
    private readonly Panel _separatorPanel;
    private readonly TableLayoutPanel _toolbarLayout;
    private readonly Button _addButton;
    private readonly Button _settingsButton;
    private readonly Label _countLabel;
    private readonly Panel _listHost;
    private readonly Panel _tabSeparator;
    private readonly Panel _groupSeparator;
    private TabKind _activeTab = TabKind.Files;
    private string _activeGroup = AllGroupsLabel;
    private bool _isSearchExpanded;

    public event Action? ShowSettings;

    public MainPopup(ConfigManager configManager, ProcessLauncher launcher)
    {
        _configManager = configManager;
        _launcher = launcher;

        AutoScaleMode = AutoScaleMode.Dpi;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = ExpandedPopupLogicalSize;
        BackColor = Color.FromArgb(250, 250, 250);
        TopMost = true;

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

        _outerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        _outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f),
            PlaceholderText = "搜索文件夹或文件... (拼音首字母也可)",
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0),
            BackColor = Color.White
        };

        _searchCollapsedIndicator = new Panel
        {
            BackColor = Color.FromArgb(205, 205, 205),
            Cursor = Cursors.Hand
        };

        _searchCollapsedPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.White,
            Cursor = Cursors.Hand
        };
        _searchCollapsedPanel.Controls.Add(_searchCollapsedIndicator);

        _searchPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        _searchPanel.Controls.Add(_searchBox);
        _searchPanel.Controls.Add(_searchCollapsedPanel);

        _separatorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 1,
            BackColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(0)
        };

        _imageList = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };

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
            MultiSelect = false,
            Margin = new Padding(0)
        };
        _listView.Columns.Add("名称", 100);
        _listView.OwnerDraw = true;
        _listView.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _listView.DrawItem += (_, e) =>
        {
            using var bg = new SolidBrush(e.Item.Selected ? SystemColors.Highlight : _listView.BackColor);
            e.Graphics.FillRectangle(bg, e.Bounds);
        };
        _listView.DrawSubItem += OnDrawSubItem;

        _toastLabel = new Label
        {
            Text = "已复制",
            AutoSize = false,
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

        _listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        _listHost.Controls.Add(_listView);
        _listHost.Controls.Add(_toastLabel);

        _tabLabels = new Label[3];
        _tabLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(240, 240, 240)
        };
        _tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
            _tabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        _tabLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        string[] tabTexts = ["文\n件", "网\n页", "文\n本"];
        TabKind[] tabKinds = [TabKind.Files, TabKind.Urls, TabKind.Texts];
        for (int i = 0; i < tabTexts.Length; i++)
        {
            var kind = tabKinds[i];
            var label = new Label
            {
                Text = tabTexts[i],
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9f),
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            label.Click += (_, _) => SwitchTab(kind);
            _tabLabels[i] = label;
            _tabLayout.Controls.Add(label, 0, i);
        }

        _tabSeparator = new Panel
        {
            Dock = DockStyle.Fill,
            Width = 1,
            BackColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(0)
        };

        _groupSeparator = new Panel
        {
            Dock = DockStyle.Fill,
            Width = 1,
            BackColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(0)
        };

        _groupLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(240, 240, 240)
        };
        _groupLayout.Resize += (_, _) => UpdateGroupLabelMetrics();

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contentLayout.Controls.Add(_tabLayout, 0, 0);
        contentLayout.Controls.Add(_tabSeparator, 1, 0);
        contentLayout.Controls.Add(_listHost, 2, 0);
        contentLayout.Controls.Add(_groupSeparator, 3, 0);
        contentLayout.Controls.Add(_groupLayout, 4, 0);

        _addButton = new Button
        {
            Text = "+ 添加",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0)
        };
        ButtonStyler.ApplyPrimary(_addButton);
        _addButton.Click += (_, _) => AddNewEntry();

        _settingsButton = new Button
        {
            Text = "设置",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(_settingsButton);
        _settingsButton.Click += (_, _) => ShowSettings?.Invoke();

        _countLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(150, 150, 150),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };

        var buttonFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        buttonFlow.Controls.Add(_addButton);
        buttonFlow.Controls.Add(_settingsButton);

        _toolbarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Color.FromArgb(245, 245, 245)
        };
        _toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbarLayout.Controls.Add(buttonFlow, 0, 0);
        _toolbarLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) }, 1, 0);
        _toolbarLayout.Controls.Add(_countLabel, 2, 0);

        _outerLayout.Controls.Add(_searchPanel, 0, 0);
        _outerLayout.Controls.Add(_separatorPanel, 0, 1);
        _outerLayout.Controls.Add(contentLayout, 0, 2);
        _outerLayout.Controls.Add(_toolbarLayout, 0, 3);

        inner.Controls.Add(_outerLayout);
        border.Controls.Add(inner);
        Controls.Add(border);

        _isSearchExpanded = false;
        ApplyTabStyles();
        ApplyScaledMetrics();

        _listView.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && _listView.FocusedItem?.Bounds.Contains(e.Location) == true)
            {
                var menu = BuildItemContextMenu();
                menu.Show(_listView, e.Location);
            }
        };

        _listView.MouseDoubleClick += (_, _) =>
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

        void ExpandSearch()
        {
            SetSearchExpanded(true, focusSearch: true);
        }

        _searchCollapsedPanel.Click += (_, _) => ExpandSearch();
        _searchCollapsedIndicator.Click += (_, _) => ExpandSearch();
        _searchPanel.Click += (_, _) =>
        {
            if (_isSearchExpanded)
                _searchBox.Focus();
        };
        _searchBox.Leave += (_, _) =>
        {
            BeginInvoke(() => CollapseSearchIfIdle());
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

        AllowDrop = true;
        _listView.AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _listView.DragEnter += OnDragEnter;
        _listView.DragDrop += OnDragDrop;

        Resize += (_, _) =>
        {
            UpdateListColumnWidth();
            UpdateGroupLabelMetrics();
            CenterToast();
            UpdateSearchIndicatorBounds();
        };
        _listView.Resize += (_, _) => UpdateListColumnWidth();
        _searchCollapsedPanel.Resize += (_, _) => UpdateSearchIndicatorBounds();

        DpiChanged += (_, _) => ApplyScaledMetrics();

        Deactivate += (_, _) =>
        {
            if (Visible) Hide();
        };
    }

    private void ApplyScaledMetrics()
    {
        var separatorWidth = Math.Max(1, UiScaleHelper.Scale(this, 1));
        _separatorPanel.Height = separatorWidth;
        _separatorPanel.MinimumSize = new Size(0, separatorWidth);
        _tabSeparator.Width = separatorWidth;
        _groupSeparator.Width = separatorWidth;

        var expandedPadding = UiScaleHelper.ScalePadding(this, new Padding(8, 4, 8, 4));
        var collapsedPadding = UiScaleHelper.ScalePadding(this, new Padding(0));
        var expandedSearchHeight = Math.Max(UiScaleHelper.Scale(this, 24), _searchBox.PreferredHeight);
        var collapsedSearchHeight = Math.Max(UiScaleHelper.Scale(this, 8), UiScaleHelper.Scale(this, 6));
        var expandedRowHeight = expandedSearchHeight + expandedPadding.Vertical;
        var collapsedRowHeight = collapsedSearchHeight + collapsedPadding.Vertical;

        _searchBox.MinimumSize = new Size(0, expandedSearchHeight);
        _searchCollapsedPanel.MinimumSize = new Size(0, collapsedSearchHeight);
        _searchCollapsedIndicator.Size = new Size(UiScaleHelper.Scale(this, 36), Math.Max(2, UiScaleHelper.Scale(this, 3)));

        _searchPanel.Padding = _isSearchExpanded ? expandedPadding : collapsedPadding;
        var activeRowHeight = _isSearchExpanded ? expandedRowHeight : collapsedRowHeight;
        _searchPanel.MinimumSize = new Size(0, activeRowHeight);
        _searchPanel.MaximumSize = new Size(0, activeRowHeight);
        _searchPanel.Height = activeRowHeight;
        _outerLayout.RowStyles[0].SizeType = SizeType.Absolute;
        _outerLayout.RowStyles[0].Height = activeRowHeight;
        _outerLayout.RowStyles[1].SizeType = SizeType.Absolute;
        _outerLayout.RowStyles[1].Height = separatorWidth;
        UpdateSearchIndicatorBounds();
        UpdateSearchPresentation();

        var toolbarHorizontalPadding = UiScaleHelper.Scale(this, 8);
        var toolbarVerticalPadding = UiScaleHelper.Scale(this, 3);
        _toolbarLayout.Padding = new Padding(
            toolbarHorizontalPadding,
            toolbarVerticalPadding,
            toolbarHorizontalPadding,
            toolbarVerticalPadding);
        _addButton.Size = UiScaleHelper.GetButtonSize(this, _addButton.Text, _addButton.Font, 96, 34, horizontalLogicalPadding: 12);
        _settingsButton.Size = UiScaleHelper.GetButtonSize(this, _settingsButton.Text, _settingsButton.Font, 86, 34, horizontalLogicalPadding: 12);
        _countLabel.MinimumSize = new Size(UiScaleHelper.Scale(this, 80), Math.Max(_addButton.Height, _settingsButton.Height));
        _countLabel.Padding = UiScaleHelper.ScalePadding(this, new Padding(0, 0, 4, 0));
        _toolbarLayout.MinimumSize = new Size(0, Math.Max(_addButton.Height, _settingsButton.Height) + toolbarVerticalPadding * 2);

        var tabWidth = 0;
        var tabHeight = 0;
        foreach (var label in _tabLabels)
        {
            var measured = TextRenderer.MeasureText(label.Text, label.Font);
            tabWidth = Math.Max(tabWidth, measured.Width + UiScaleHelper.Scale(this, 12));
            tabHeight = Math.Max(tabHeight, measured.Height + UiScaleHelper.Scale(this, 12));
        }

        for (int i = 0; i < _tabLabels.Length; i++)
            _tabLayout.RowStyles[i].Height = tabHeight;
        _tabLayout.MinimumSize = new Size(tabWidth, tabHeight * _tabLabels.Length);
        _tabLayout.Width = tabWidth;

        _groupLayout.Padding = UiScaleHelper.ScalePadding(this, new Padding(0));
        _groupLayout.MinimumSize = new Size(GetGroupColumnWidth(), 0);
        _groupLayout.Width = GetGroupColumnWidth();

        var toastSize = UiScaleHelper.GetButtonSize(this, _toastLabel.Text, _toastLabel.Font, 80, 28, horizontalLogicalPadding: 10, verticalLogicalPadding: 3);
        _toastLabel.Size = toastSize;

        UpdateImageList();
        UpdateListColumnWidth();
        UpdateGroupLabelMetrics();
        CenterToast();
        ApplyTabStyles();
        ApplyGroupStyles();

        if (Visible)
        {
            var screen = Screen.FromPoint(new Point(Math.Max(Left + 1, 0), Math.Max(Top + 1, 0)));
            EnsurePopupSizeForScreen(screen);
            ClampToWorkingArea(screen.WorkingArea);
        }
    }

    private void UpdateSearchPresentation()
    {
        _searchBox.Visible = _isSearchExpanded;
        _searchCollapsedPanel.Visible = !_isSearchExpanded;
        _searchCollapsedIndicator.Visible = !_isSearchExpanded;
        _outerLayout.PerformLayout();
        _searchPanel.PerformLayout();
        PerformLayout();
    }

    private void UpdateSearchIndicatorBounds()
    {
        if (_searchCollapsedPanel.Width <= 0 || _searchCollapsedPanel.Height <= 0)
            return;

        _searchCollapsedIndicator.Location = new Point(
            Math.Max(0, (_searchCollapsedPanel.Width - _searchCollapsedIndicator.Width) / 2),
            Math.Max(0, (_searchCollapsedPanel.Height - _searchCollapsedIndicator.Height) / 2));
    }

    private void SetSearchExpanded(bool expanded, bool focusSearch = false)
    {
        if (_isSearchExpanded == expanded)
        {
            if (expanded && focusSearch)
                _searchBox.Focus();
            return;
        }

        _isSearchExpanded = expanded;
        ApplyScaledMetrics();

        var screen = Visible
            ? Screen.FromPoint(new Point(Math.Max(Left + 1, 0), Math.Max(Top + 1, 0)))
            : Screen.FromPoint(Cursor.Position);
        EnsurePopupSizeForScreen(screen);
        ClampToWorkingArea(screen.WorkingArea);

        if (expanded)
        {
            if (focusSearch)
                _searchBox.Focus();
        }
        else if (Visible && _listView.Items.Count > 0)
        {
            _listView.Focus();
        }
    }

    private void CollapseSearchIfIdle()
    {
        if (!_isSearchExpanded)
            return;

        if (!string.IsNullOrWhiteSpace(_searchBox.Text))
            return;

        if (_searchBox.Focused || _searchPanel.ContainsFocus)
            return;

        SetSearchExpanded(false);
    }

    private void UpdateImageList()
    {
        var iconSize = UiScaleHelper.GetIconSize(this, 16);
        if (_imageList.ImageSize.Width == iconSize && _webEntryImage?.Width == iconSize)
            return;

        _imageList.ImageSize = new Size(iconSize, iconSize);
        _imageList.Images.Clear();
        _webEntryImage?.Dispose();
        _webEntryImage = LoadWebEntryImage(iconSize);

        if (IsHandleCreated)
            RefreshList();
    }

    private void UpdateListColumnWidth()
    {
        if (_listView.Columns.Count == 0)
            return;

        var width = Math.Max(UiScaleHelper.Scale(this, 120), _listView.ClientSize.Width - UiScaleHelper.Scale(this, 6));
        _listView.Columns[0].Width = width;
    }

    private void CenterToast()
    {
        if (_listHost.Width <= 0 || _listHost.Height <= 0)
            return;

        _toastLabel.Location = new Point(
            Math.Max(0, (_listHost.Width - _toastLabel.Width) / 2),
            Math.Max(0, (_listHost.Height - _toastLabel.Height) / 2));
        _toastLabel.BringToFront();
    }

    private int GetGroupColumnWidth()
    {
        var minWidth = UiScaleHelper.Scale(this, 36);
        var maxWidth = UiScaleHelper.Scale(this, 56);
        var horizontalPadding = UiScaleHelper.Scale(this, 12);
        var font = _tabLabels[0].Font;
        var maxMeasuredWidth = TextRenderer.MeasureText(GetVerticalLabelText(AllGroupsLabel), font).Width + horizontalPadding;

        foreach (var group in GetOrderedGroupNames(GetEntriesForActiveTab()))
        {
            maxMeasuredWidth = Math.Max(
                maxMeasuredWidth,
                TextRenderer.MeasureText(GetVerticalLabelText(group), font).Width + horizontalPadding);
        }

        return Math.Max(minWidth, Math.Min(maxWidth, maxMeasuredWidth));
    }

    private void UpdateGroupLabelMetrics()
    {
        if (_groupLabels.Count == 0)
            return;

        var labelWidth = Math.Max(UiScaleHelper.Scale(this, 36), _groupLayout.ClientSize.Width - UiScaleHelper.Scale(this, 1));
        var font = _tabLabels[0].Font;
        var verticalPadding = UiScaleHelper.Scale(this, 10);
        var minLabelHeight = UiScaleHelper.Scale(this, 44);

        foreach (var label in _groupLabels)
        {
            label.Font = font;
            var displayText = label.Text;
            var lineCount = Math.Max(1, displayText.Count(c => c == '\n') + 1);
            var singleLineHeight = TextRenderer.MeasureText("中", font).Height;
            var labelHeight = Math.Max(minLabelHeight, lineCount * singleLineHeight + verticalPadding);
            label.Size = new Size(labelWidth, labelHeight);
            label.Margin = new Padding(0);
            label.AutoEllipsis = true;
        }
    }

    private void SwitchTab(TabKind kind)
    {
        if (_activeTab == kind) return;
        _activeTab = kind;
        _activeGroup = AllGroupsLabel;
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

    private void ApplyGroupStyles()
    {
        foreach (var label in _groupLabels)
        {
            var group = label.Tag as string ?? AllGroupsLabel;
            var active = string.Equals(group, _activeGroup, StringComparison.OrdinalIgnoreCase);
            label.BackColor = active ? Color.FromArgb(60, 60, 60) : Color.FromArgb(240, 240, 240);
            label.ForeColor = active ? Color.White : Color.FromArgb(80, 80, 80);
        }
    }

    private void UpdateSearchPlaceholder()
    {
        _searchBox.PlaceholderText = _activeTab switch
        {
            TabKind.Files => "搜索文件夹或文件... (拼音首字母也可)",
            TabKind.Urls => "搜索网页...",
            TabKind.Texts => "搜索文本...",
            _ => "搜索..."
        };
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
                    var selected = GetSelectedEntry();
                    if (selected != null) ProcessLauncher.OpenInExplorer(selected.Path);
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
                    var selected = GetSelectedEntry();
                    if (selected != null) CopyToClipboard(selected.Path);
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
        ShowPopup(TabKind.Files);
    }

    private void ShowPopup(TabKind kind, bool focusList = true)
    {
        _activeTab = kind;
        _activeGroup = AllGroupsLabel;
        _searchBox.Clear();
        SetSearchExpanded(false);
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        EnsurePopupSizeForScreen(Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position));
        RefreshList();
        PositionNearTray();
        Show();
        Activate();
        if (focusList && _listView.Items.Count > 0)
            _listView.Focus();
    }

    public void HandleExternalRequest(string request)
    {
        if (QuickstartProtocol.IsProtocolUri(request))
        {
            if (!QuickstartProtocol.TryParseAddUrlRequest(request, out var addUrlRequest) || addUrlRequest == null)
            {
                MessageBox.Show(
                    "无效的网站添加请求。",
                    "Quickstart",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            AddUrlEntry(addUrlRequest.Url, addUrlRequest.Title);
            return;
        }

        AddPathEntry(request);
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
            ReconcileActiveGroup();
            RefreshList();
        }
    }

    public void AddUrlEntry(string url, string title)
    {
        if (TryFocusExistingUrl(url, showDuplicateToast: true))
            return;

        ShowPopup(TabKind.Urls, focusList: false);

        var entry = new QuickEntry
        {
            Name = string.IsNullOrWhiteSpace(title) ? GetFallbackUrlName(url) : title.Trim(),
            Path = url,
            Type = EntryType.Url
        };

        using var form = new EntryEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.AddEntry(entry);
            ReconcileActiveGroup();
            ShowPopup(TabKind.Urls);
            SelectEntryById(entry.Id);
        }
    }

    private void AddNewEntry()
    {
        var entry = new QuickEntry
        {
            Type = _activeTab switch
            {
                TabKind.Urls => EntryType.Url,
                TabKind.Texts => EntryType.Text,
                _ => EntryType.Folder
            }
        };
        using var form = new EntryEditForm(entry);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _configManager.AddEntry(entry);
            ReconcileActiveGroup();
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
            ReconcileActiveGroup();
            RefreshList();
        }
    }

    private void DeleteSelectedEntry()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        var result = MessageBox.Show(
            $"确定要删除 \"{entry.Name}\" 吗？",
            "确认删除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _configManager.RemoveEntry(entry.Id);
            ReconcileActiveGroup();
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

    private void ShowToast(string message = "已复制")
    {
        _toastLabel.Text = message;
        _toastLabel.Size = UiScaleHelper.GetButtonSize(this, message, _toastLabel.Font, 80, 28,
            horizontalLogicalPadding: 10, verticalLogicalPadding: 3);
        CenterToast();
        _toastLabel.Visible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private bool TryFocusExistingUrl(string url, bool showDuplicateToast)
    {
        var existing = FindEntryByPath(url, EntryType.Url);
        if (existing == null)
            return false;

        ShowPopup(TabKind.Urls);
        SelectEntryById(existing.Id);
        if (showDuplicateToast)
            ShowToast("该网站已存在");
        return true;
    }

    private QuickEntry? FindEntryByPath(string path, EntryType type)
        => _configManager.Config.Entries.FirstOrDefault(e =>
            e.Type == type && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    private bool SelectEntryById(string id)
    {
        foreach (ListViewItem item in _listView.Items)
        {
            if (item.Tag is not QuickEntry entry || entry.Id != id)
                continue;

            _listView.SelectedItems.Clear();
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            _listView.Focus();
            return true;
        }

        return false;
    }

    private static string GetFallbackUrlName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;

        return url;
    }

    private QuickEntry? GetSelectedEntry()
    {
        if (_listView.SelectedItems.Count == 0) return null;
        return _listView.SelectedItems[0].Tag as QuickEntry;
    }

    public void RefreshList()
    {
        var query = _searchBox.Text.Trim();
        var typeEntries = GetEntriesForActiveTab();
        RebuildGroupLabels(typeEntries);

        var entries = _activeGroup == AllGroupsLabel
            ? typeEntries
            : typeEntries
                .Where(e => string.Equals(NormalizeGroupName(e.Group), _activeGroup, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (!string.IsNullOrEmpty(query))
        {
            entries = entries
                .Where(e => PinyinHelper.MatchesPinyin(e.Name, query)
                    || PinyinHelper.MatchesPinyin(e.Path, query))
                .ToList();
        }

        entries = entries.OrderBy(e => e.SortOrder).ToList();

        var useLargeIcon = _imageList.ImageSize.Width > 16;

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
                        var icon = IconExtractor.GetIcon(iconKey, isDirectory: false, useLargeIcon);
                        if (icon != null)
                            _imageList.Images.Add(iconKey, icon);
                    }
                }
                else
                {
                    var icon = IconExtractor.GetIcon(entry.Path, entry.Type == EntryType.Folder, useLargeIcon);
                    if (icon != null)
                        _imageList.Images.Add(iconKey, icon);
                }
            }

            var item = string.IsNullOrEmpty(iconKey)
                ? new ListViewItem(entry.Name)
                : new ListViewItem(entry.Name, iconKey) { ImageKey = iconKey };

            item.Tag = entry;
            item.ToolTipText = entry.Type == EntryType.Text
                ? (entry.Path.Length > 200 ? entry.Path[..200] + "..." : entry.Path)
                : entry.Path;

            if (entry.Type is EntryType.Folder or EntryType.File && !PathExists(entry))
                item.ForeColor = Color.Red;

            _listView.Items.Add(item);
        }

        _listView.EndUpdate();

        _countLabel.Text = $"{entries.Count} 项";
        UpdateListColumnWidth();
    }

    private List<QuickEntry> GetEntriesForActiveTab()
    {
        var entries = _configManager.Config.Entries;
        return _activeTab switch
        {
            TabKind.Files => entries.Where(e => e.Type is EntryType.Folder or EntryType.File).ToList(),
            TabKind.Urls => entries.Where(e => e.Type == EntryType.Url).ToList(),
            TabKind.Texts => entries.Where(e => e.Type == EntryType.Text).ToList(),
            _ => entries
        };
    }

    private void RebuildGroupLabels(IEnumerable<QuickEntry> currentTypeEntries)
    {
        var groups = GetOrderedGroupNames(currentTypeEntries).ToList();
        if (_activeGroup != AllGroupsLabel
            && groups.All(group => !string.Equals(group, _activeGroup, StringComparison.OrdinalIgnoreCase)))
        {
            _activeGroup = AllGroupsLabel;
        }

        _groupLayout.SuspendLayout();
        foreach (var label in _groupLabels)
            label.Dispose();
        _groupLabels.Clear();
        _groupLayout.Controls.Clear();

        AddGroupLabel(AllGroupsLabel);
        foreach (var group in groups)
            AddGroupLabel(group);

        _groupLayout.ResumeLayout();
        _groupLayout.PerformLayout();
        _groupLayout.MinimumSize = new Size(GetGroupColumnWidth(), 0);
        _groupLayout.Width = GetGroupColumnWidth();
        UpdateGroupLabelMetrics();
        ApplyGroupStyles();
    }

    private IEnumerable<string> GetOrderedGroupNames(IEnumerable<QuickEntry> entries)
    {
        var groups = entries
            .Select(entry => NormalizeGroupName(entry.Group))
            .Where(group => !string.IsNullOrEmpty(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                _configManager.Config.GroupLastUsedAt.TryGetValue(group, out var lastUsedAt);
                return new { Name = group, LastUsedAt = lastUsedAt };
            })
            .OrderByDescending(item => item.LastUsedAt)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Name);

        return groups;
    }

    private void AddGroupLabel(string group)
    {
        var label = new Label
        {
            Text = GetVerticalLabelText(group),
            Tag = group,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
            AutoSize = false,
            AutoEllipsis = true
        };
        label.Click += (_, _) => SwitchGroup(group);
        _groupLabels.Add(label);
        _groupLayout.Controls.Add(label);
    }

    private void SwitchGroup(string group, bool rememberUsage = true)
    {
        if (string.IsNullOrWhiteSpace(group))
            group = AllGroupsLabel;

        if (string.Equals(_activeGroup, group, StringComparison.OrdinalIgnoreCase))
            return;

        _activeGroup = group;
        if (rememberUsage && !string.Equals(group, AllGroupsLabel, StringComparison.OrdinalIgnoreCase))
            _configManager.TouchGroup(group);

        RefreshList();
    }

    private void ReconcileActiveGroup()
    {
        if (_activeGroup == AllGroupsLabel)
            return;

        var groups = GetOrderedGroupNames(GetEntriesForActiveTab());
        if (groups.All(group => !string.Equals(group, _activeGroup, StringComparison.OrdinalIgnoreCase)))
            _activeGroup = AllGroupsLabel;
    }

    private Label? GetGroupLabelAtScreenPoint(Point screenPt)
        => _groupLabels.FirstOrDefault(label => label.RectangleToScreen(label.ClientRectangle).Contains(screenPt));

    private static string NormalizeGroupName(string? group)
        => string.IsNullOrWhiteSpace(group) ? string.Empty : group.Trim();

    private static string GetVerticalLabelText(string text)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? AllGroupsLabel : text.Trim();
        var indices = StringInfo.ParseCombiningCharacters(normalized);
        if (indices.Length <= 1)
            return normalized;

        var parts = new List<string>(indices.Length);
        for (int i = 0; i < indices.Length; i++)
        {
            int start = indices[i];
            int length = (i + 1 < indices.Length ? indices[i + 1] : normalized.Length) - start;
            parts.Add(normalized.Substring(start, length));
        }

        return string.Join("\n", parts);
    }

    private static Image? LoadWebEntryImage(int size)
    {
        try
        {
            var asm = typeof(MainPopup).Assembly;
            using var stream = asm.GetManifestResourceStream("Quickstart.Resources.web-url.png");
            if (stream == null) return null;

            using var original = Image.FromStream(stream);
            return new Bitmap(original, new Size(size, size));
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

        int textX = bounds.X + UiScaleHelper.Scale(this, 2);
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
                textX += imgList.ImageSize.Width + UiScaleHelper.Scale(this, 4);
        }

        var textColor = item.Selected ? SystemColors.HighlightText : item.ForeColor;
        var textBounds = new Rectangle(textX, bounds.Y, bounds.Right - textX - UiScaleHelper.Scale(this, 2), bounds.Height);
        var display = MidTruncate(item.Text, _listView.Font, textBounds.Width);

        TextRenderer.DrawText(
            g,
            display,
            _listView.Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private static string MidTruncate(string text, Font font, int maxPx)
    {
        if (string.IsNullOrEmpty(text) || maxPx <= 0) return text;
        if (TextRenderer.MeasureText(text, font).Width <= maxPx) return text;

        const string dots = "...";
        int dotsWidth = TextRenderer.MeasureText(dots, font).Width;
        int available = maxPx - dotsWidth;
        if (available <= 0) return dots;

        int half = available / 2;
        int lo = 0;
        int hi = text.Length / 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (TextRenderer.MeasureText(text[..mid], font).Width <= half) lo = mid;
            else hi = mid - 1;
        }
        int startLen = lo;
        int usedStart = startLen > 0 ? TextRenderer.MeasureText(text[..startLen], font).Width : 0;

        int endAvailable = available - usedStart;
        lo = 0;
        hi = text.Length - startLen;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (TextRenderer.MeasureText(text[^mid..], font).Width <= endAvailable) lo = mid;
            else hi = mid - 1;
        }

        int endLen = lo;
        return text[..startLen] + dots + (endLen > 0 ? text[^endLen..] : "");
    }

    private static bool PathExists(QuickEntry entry)
        => entry.Type == EntryType.Folder
            ? Directory.Exists(entry.Path)
            : File.Exists(entry.Path);

    public void ShowAtGesturePoint(Point screenPt)
    {
        _activeTab = TabKind.Files;
        _activeGroup = AllGroupsLabel;
        _searchBox.Clear();
        SetSearchExpanded(false);
        ApplyTabStyles();
        UpdateSearchPlaceholder();

        var screen = Screen.FromPoint(screenPt);
        EnsurePopupSizeForScreen(screen);
        RefreshList();

        var workingArea = screen.WorkingArea;
        var popupMargin = UiScaleHelper.Scale(this, 8);
        var listAnchorOffsetY = _searchPanel.Height + _separatorPanel.Height + popupMargin;
        int x = Math.Max(workingArea.Left + popupMargin, Math.Min(screenPt.X - popupMargin, workingArea.Right - Width - popupMargin));
        int y = Math.Max(workingArea.Top + popupMargin, Math.Min(screenPt.Y - listAnchorOffsetY, workingArea.Bottom - Height - popupMargin));
        Location = new Point(x, y);

        Show();
    }

    public void HighlightAtScreenPoint(Point screenPt)
    {
        var tabClientPt = _tabLayout.PointToClient(screenPt);
        if (_tabLayout.ClientRectangle.Contains(tabClientPt))
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

            if (_listView.SelectedItems.Count > 0)
                _listView.SelectedItems.Clear();
            return;
        }

        var groupLabel = GetGroupLabelAtScreenPoint(screenPt);
        if (groupLabel?.Tag is string group)
        {
            if (!string.Equals(_activeGroup, group, StringComparison.OrdinalIgnoreCase))
                SwitchGroup(group);

            if (_listView.SelectedItems.Count > 0)
                _listView.SelectedItems.Clear();
            return;
        }

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
        else if (_listView.SelectedItems.Count > 0)
        {
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

        if (GetGroupLabelAtScreenPoint(screenPt) != null)
        {
            Activate();
            if (_isSearchExpanded)
                _searchBox.Focus();
            else if (_listView.Items.Count > 0)
                _listView.Focus();
            return false;
        }

        Activate();
        if (_isSearchExpanded)
            _searchBox.Focus();
        else if (_listView.Items.Count > 0)
            _listView.Focus();
        return false;
    }

    private void EnsurePopupSizeForScreen(Screen screen)
    {
        var margin = UiScaleHelper.Scale(this, 8);
        var preferred = UiScaleHelper.ScaleSize(this, GetPreferredPopupLogicalSize());
        var minimum = UiScaleHelper.ScaleSize(this, GetMinimumPopupLogicalSize());
        var maxWidth = Math.Max(minimum.Width, screen.WorkingArea.Width - margin * 2);
        var maxHeight = Math.Max(minimum.Height, screen.WorkingArea.Height - margin * 2);

        Size = new Size(
            Math.Min(preferred.Width, maxWidth),
            Math.Min(preferred.Height, maxHeight));
    }

    private Size GetPreferredPopupLogicalSize()
        => _isSearchExpanded
            ? ExpandedPopupLogicalSize
            : new Size(ExpandedPopupLogicalSize.Width, ExpandedPopupLogicalSize.Height - CollapsedPopupHeightDeltaLogical);

    private Size GetMinimumPopupLogicalSize()
        => _isSearchExpanded
            ? MinimumExpandedPopupLogicalSize
            : new Size(
                MinimumExpandedPopupLogicalSize.Width,
                Math.Max(260, MinimumExpandedPopupLogicalSize.Height - CollapsedPopupHeightDeltaLogical));

    private void ClampToWorkingArea(Rectangle workingArea)
    {
        var margin = UiScaleHelper.Scale(this, 8);
        int x = Math.Max(workingArea.Left + margin, Math.Min(Left, workingArea.Right - Width - margin));
        int y = Math.Max(workingArea.Top + margin, Math.Min(Top, workingArea.Bottom - Height - margin));
        Location = new Point(x, y);
    }

    private void PositionNearTray()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var taskbarOnBottom = screen.Bottom < (Screen.PrimaryScreen?.Bounds.Bottom ?? screen.Bottom);
        var margin = UiScaleHelper.Scale(this, 8);

        int x = screen.Right - Width - margin;
        int y = taskbarOnBottom
            ? screen.Bottom - Height - margin
            : screen.Top + margin;

        Location = new Point(Math.Max(screen.Left + margin, x), Math.Max(screen.Top + margin, y));
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
                AddPathEntry(path);
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;
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
