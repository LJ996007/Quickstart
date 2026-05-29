namespace Quickstart.UI;

using System.Globalization;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class MainPopup : Form
{
    private enum TabKind { Folders, Files, Urls, Texts }

    private static readonly Size ExpandedPopupLogicalSize = new(380, 440);
    private static readonly Size MinimumExpandedPopupLogicalSize = new(300, 340);
    private const int CollapsedPopupHeightDeltaLogical = 28;
    private const string AllGroupsLabel = "全部";
    private const string EntryReorderDataFormat = "Quickstart.EntryReorder";

    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ImageList _imageList;
    private Image? _webEntryImage;
    private readonly FaviconService _faviconService = new();
    // 统一存放高分辨率原图，自绘时再用高质量插值缩放到统一尺寸，保证图标大小/清晰度一致
    private readonly Dictionary<string, Image> _iconImages = new(StringComparer.OrdinalIgnoreCase);
    private int _iconRenderSize;
    private const int IconSourceSize = 48;
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
    private readonly ToolTip _toolTip = new();
    private readonly Label _countLabel;
    private readonly Panel _listHost;
    private readonly Panel _tabSeparator;
    private readonly Panel _groupSeparator;
    private TabKind _activeTab = TabKind.Folders;
    private string _activeGroup = AllGroupsLabel;
    private bool _isSearchExpanded;
    private List<string>? _lastGroupSignature;
    private readonly Dictionary<(string Text, int Width), string> _truncateCache = new();
    private bool _suppressAutoHide;

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
        FormStyler.ApplyRounded(this);

        var border = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(220, 220, 220)
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
            PlaceholderText = "搜索文件夹... (拼音首字母也可)",
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
            using var bg = new SolidBrush(e.Item.Selected ? Color.FromArgb(235, 245, 255) : _listView.BackColor);
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

        _tabLabels = new Label[4];
        _tabLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(240, 240, 240)
        };
        _tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++)
            _tabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        _tabLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        string[] tabTexts = ["文\n件\n夹", "文\n件", "网\n页", "文\n本"];
        TabKind[] tabKinds = [TabKind.Folders, TabKind.Files, TabKind.Urls, TabKind.Texts];
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

        _addButton = CreateToolbarIconButton("\uE710", "添加");
        _addButton.Click += (_, _) => AddNewEntry();

        _settingsButton = CreateToolbarIconButton("\uE713", "设置");
        _settingsButton.Margin = new Padding(4, 0, 0, 0);
        _settingsButton.Click += (_, _) => ShowSettings?.Invoke();

        _countLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(150, 150, 150),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(8, 0, 0, 0)
        };

        var buttonFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0)
        };
        buttonFlow.Controls.Add(_addButton);
        buttonFlow.Controls.Add(_settingsButton);

        _toolbarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Color.FromArgb(245, 245, 245)
        };
        _toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _toolbarLayout.Controls.Add(buttonFlow, 0, 0);
        _toolbarLayout.Controls.Add(_countLabel, 1, 0);

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

        _listView.ItemDrag += OnListItemDrag;

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
        _listView.DragOver += OnDragOver;
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
            // 打开编辑/删除等子对话框期间不自动隐藏，方便连续编辑多个条目
            if (Visible && !_suppressAutoHide) Hide();
        };
    }

    private Button CreateToolbarIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Text = glyph,
            AccessibleName = tooltip,
            Font = new Font("Segoe MDL2 Assets", 10f),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(245, 245, 245),
            ForeColor = Color.FromArgb(75, 75, 75),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 238, 246);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(218, 230, 246);
        _toolTip.SetToolTip(button, tooltip);
        return button;
    }

    private void ApplyScaledMetrics()
    {
        // 字体/DPI 变化会使按 (文本,宽度) 缓存的截断结果失效
        _truncateCache.Clear();

        var separatorWidth = Math.Max(1, UiScaleHelper.Scale(this, 1));
        _separatorPanel.Height = separatorWidth;
        _separatorPanel.MinimumSize = new Size(0, separatorWidth);
        _tabSeparator.Width = separatorWidth;
        _groupSeparator.Width = separatorWidth;

        var expandedPadding = UiScaleHelper.ScalePadding(this, new Padding(6, 2, 6, 2));
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

        var toolbarHorizontalPadding = UiScaleHelper.Scale(this, 6);
        var toolbarVerticalPadding = UiScaleHelper.Scale(this, 4);
        _toolbarLayout.Padding = new Padding(
            toolbarHorizontalPadding,
            toolbarVerticalPadding,
            toolbarHorizontalPadding,
            toolbarVerticalPadding);
        var toolbarButtonSize = UiScaleHelper.Scale(this, 28);
        _addButton.Size = new Size(toolbarButtonSize, toolbarButtonSize);
        _settingsButton.Size = new Size(toolbarButtonSize, toolbarButtonSize);
        _countLabel.MinimumSize = new Size(UiScaleHelper.Scale(this, 64), Math.Max(_addButton.Height, _settingsButton.Height));
        _countLabel.Margin = new Padding(UiScaleHelper.Scale(this, 8), 0, 0, 0);
        _countLabel.Padding = new Padding(0);
        _toolbarLayout.MinimumSize = new Size(0, Math.Max(_addButton.Height, _settingsButton.Height));

        var tabWidth = 0;
        var tabHeight = 0;
        foreach (var label in _tabLabels)
        {
            var measured = TextRenderer.MeasureText(label.Text, label.Font);
            tabWidth = Math.Max(tabWidth, measured.Width + UiScaleHelper.Scale(this, 10));
            tabHeight = Math.Max(tabHeight, measured.Height + UiScaleHelper.Scale(this, 8));
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
        var iconSize = UiScaleHelper.GetIconSize(this, 20);
        _iconRenderSize = iconSize;
        if (_imageList.ImageSize.Width == iconSize)
            return;

        // ImageList 仅用于撑起行高；实际绘制用 _iconImages 的高分原图
        _imageList.ImageSize = new Size(iconSize, iconSize);
        _imageList.Images.Clear();
        _iconImages.Clear();
        // 通用网页占位图按固定高分辨率生成一次，缩放交给自绘
        _webEntryImage ??= LoadWebEntryImage(IconSourceSize);

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
        var minWidth = UiScaleHelper.Scale(this, 32);
        var maxWidth = UiScaleHelper.Scale(this, 48);
        var horizontalPadding = UiScaleHelper.Scale(this, 10);
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

        var labelWidth = Math.Max(UiScaleHelper.Scale(this, 32), _groupLayout.ClientSize.Width - UiScaleHelper.Scale(this, 1));
        var font = _tabLabels[0].Font;
        var verticalPadding = UiScaleHelper.Scale(this, 8);
        var minLabelHeight = UiScaleHelper.Scale(this, 40);

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
        TabKind[] kinds = [TabKind.Folders, TabKind.Files, TabKind.Urls, TabKind.Texts];
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
            TabKind.Folders => "搜索文件夹... (拼音首字母也可)",
            TabKind.Files => "搜索要打开的文件... (拼音首字母也可)",
            TabKind.Urls => "搜索网页...",
            TabKind.Texts => "搜索文本...",
            _ => "搜索..."
        };
    }

    // 打开子对话框时保持主弹窗可见（抑制失焦自动隐藏），关闭后重新激活，便于连续编辑
    private DialogResult ShowChildDialog(Form dialog)
    {
        _suppressAutoHide = true;
        try
        {
            return DialogPresenter.ShowModal(dialog, this);
        }
        finally
        {
            _suppressAutoHide = false;
            ReactivateAfterChildDialog();
        }
    }

    private void ReactivateAfterChildDialog()
    {
        if (!Visible)
            return;

        Activate();
        if (_listView.Items.Count > 0)
            _listView.Focus();
    }

    // 按条目类型汇总各自已有的分组名，供编辑对话框下拉选择
    private Dictionary<EntryType, List<string>> BuildGroupSuggestions()
    {
        var result = new Dictionary<EntryType, List<string>>();
        foreach (var type in new[] { EntryType.Folder, EntryType.File, EntryType.Url, EntryType.Text })
        {
            var entries = _configManager.Config.Entries.Where(e => e.Type == type);
            result[type] = GetOrderedGroupNames(entries).ToList();
        }

        return result;
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
        ShowPopup(TabKind.Folders);
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
                DialogPresenter.ShowMessage(
                    this,
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

        if (TryFocusExistingEntry(path, showDuplicateToast: true))
            return;

        var entry = new QuickEntry
        {
            Name = Path.GetFileName(path),
            Path = path,
            Type = isDir ? EntryType.Folder : EntryType.File
        };

        ShowPopup(GetTabKind(entry.Type), focusList: false);
        using var form = new EntryEditForm(entry, BuildGroupSuggestions());
        if (ShowChildDialog(form) == DialogResult.OK)
            AddEntryAndFocus(entry);
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

        using var form = new EntryEditForm(entry, BuildGroupSuggestions());
        if (ShowChildDialog(form) == DialogResult.OK)
            AddEntryAndFocus(entry);
    }

    private void AddNewEntry()
    {
        var entry = new QuickEntry
        {
            Type = _activeTab switch
            {
                TabKind.Files => EntryType.File,
                TabKind.Urls => EntryType.Url,
                TabKind.Texts => EntryType.Text,
                _ => EntryType.Folder
            }
        };
        using var form = new EntryEditForm(entry, BuildGroupSuggestions());
        if (ShowChildDialog(form) == DialogResult.OK)
            AddEntryAndFocus(entry);
    }

    private void EditSelectedEntry()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        using var form = new EntryEditForm(entry, BuildGroupSuggestions());
        if (ShowChildDialog(form) == DialogResult.OK)
        {
            _configManager.UpdateEntry(entry);
            RemoveCachedIcon(CustomIconKey(entry.Id)); // 图标可能已更改，丢弃旧缓存
            ReconcileActiveGroup();
            RefreshList();
            SelectEntryById(entry.Id);
        }
    }

    private void DeleteSelectedEntry()
    {
        var entry = GetSelectedEntry();
        if (entry == null) return;

        DialogResult result;
        _suppressAutoHide = true;
        try
        {
            result = DialogPresenter.ShowMessage(
                this,
                $"确定要删除 \"{entry.Name}\" 吗？",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
        }
        finally
        {
            _suppressAutoHide = false;
            ReactivateAfterChildDialog();
        }

        if (result == DialogResult.Yes)
        {
            _configManager.RemoveEntry(entry.Id);
            CustomIconStore.Remove(entry.Id);
            RemoveCachedIcon(CustomIconKey(entry.Id));
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

    private void AddEntryAndFocus(QuickEntry entry)
    {
        if (_configManager.AddEntry(entry))
        {
            FocusEntry(entry);
            return;
        }

        var existing = FindEntryByPath(entry.Path);
        if (existing != null)
        {
            FocusEntry(existing, GetDuplicateMessage(existing));
            return;
        }

        ShowToast("该项目已存在");
    }

    private bool TryFocusExistingUrl(string url, bool showDuplicateToast)
    {
        var existing = FindEntryByPath(url, EntryType.Url);
        if (existing == null)
            return false;

        FocusEntry(existing, showDuplicateToast ? "该网站已存在" : null);
        return true;
    }

    private bool TryFocusExistingEntry(string path, bool showDuplicateToast)
    {
        var existing = FindEntryByPath(path);
        if (existing == null)
            return false;

        FocusEntry(existing, showDuplicateToast ? GetDuplicateMessage(existing) : null);
        return true;
    }

    private void FocusEntry(QuickEntry entry, string? toastMessage = null)
    {
        _activeTab = GetTabKind(entry.Type);
        _activeGroup = AllGroupsLabel;
        _searchBox.Clear();
        SetSearchExpanded(false);
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        EnsurePopupSizeForScreen(Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position));
        RefreshList();

        if (!Visible)
        {
            PositionNearTray();
            Show();
            Activate();
        }

        SelectEntryById(entry.Id);
        if (!string.IsNullOrWhiteSpace(toastMessage))
            ShowToast(toastMessage);
    }

    private static TabKind GetTabKind(EntryType type)
        => type switch
        {
            EntryType.File => TabKind.Files,
            EntryType.Url => TabKind.Urls,
            EntryType.Text => TabKind.Texts,
            _ => TabKind.Folders
        };

    private static string GetDuplicateMessage(QuickEntry entry)
        => entry.Type == EntryType.Url ? "该网站已存在" : "该项目已存在";

    private QuickEntry? FindEntryByPath(string path, EntryType? type = null)
        => _configManager.Config.Entries.FirstOrDefault(e =>
            (!type.HasValue || e.Type == type.Value)
            && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

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
            // 命中项按匹配质量排序（前缀 > 子串 > 路径子串 > 仅拼音），同级再按用户排序
            entries = entries
                .Where(e => PinyinHelper.MatchesPinyin(e.Name, query)
                    || PinyinHelper.MatchesPinyin(e.Path, query))
                .OrderBy(e => GetMatchRank(e, query))
                .ThenBy(e => e.SortOrder)
                .ToList();
        }
        else
        {
            entries = entries.OrderBy(e => e.SortOrder).ToList();
        }

        var faviconsToLoad = new List<string>();

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var entry in entries)
        {
            string? iconKey;
            if (entry.Type == EntryType.Url)
            {
                var customKey = CustomIconKey(entry.Id);
                if (!string.IsNullOrEmpty(entry.CustomIconPath) && EnsureCustomIcon(customKey, entry.CustomIconPath))
                {
                    // 用户自定义图标优先
                    iconKey = customKey;
                }
                else
                {
                    // 否则使用已缓存的网站图标，未命中则用通用图标占位并触发后台加载
                    var host = FaviconService.GetHost(entry.Path);
                    var favicon = host != null ? _faviconService.TryGetCached(entry.Path) : null;
                    if (favicon != null && host != null)
                    {
                        iconKey = FaviconKey(host);
                        RegisterIcon(iconKey, favicon);
                    }
                    else if (_webEntryImage != null)
                    {
                        iconKey = "<URL_CUSTOM>";
                        RegisterIcon(iconKey, _webEntryImage);
                        if (host != null) faviconsToLoad.Add(entry.Path);
                    }
                    else
                    {
                        iconKey = ".url";
                        if (!_iconImages.ContainsKey(iconKey))
                            RegisterIcon(iconKey, IconExtractor.GetIcon(".url", isDirectory: false, useLargeIcon: true)?.ToBitmap());
                        if (host != null) faviconsToLoad.Add(entry.Path);
                    }
                }
            }
            else if (entry.Type == EntryType.Text)
                iconKey = null;
            else if (entry.Type == EntryType.Folder)
            {
                iconKey = "<DIR>";
                if (!_iconImages.ContainsKey(iconKey))
                    RegisterIcon(iconKey, IconExtractor.GetIcon(entry.Path, isDirectory: true, useLargeIcon: true)?.ToBitmap());
            }
            else
            {
                iconKey = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (string.IsNullOrEmpty(iconKey)) iconKey = "<NOEXT>";
                if (!_iconImages.ContainsKey(iconKey))
                    RegisterIcon(iconKey, IconExtractor.GetIcon(entry.Path, isDirectory: false, useLargeIcon: true)?.ToBitmap());
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

        foreach (var url in faviconsToLoad.Distinct(StringComparer.OrdinalIgnoreCase))
            _ = LoadFaviconAsync(url);
    }

    private static string FaviconKey(string host) => "<FAV:" + host + ">";

    // 登记一张原图：_iconImages 供自绘高质量缩放，_imageList 仅用于撑行高
    private void RegisterIcon(string key, Image? image)
    {
        if (image == null || _iconImages.ContainsKey(key))
            return;

        _iconImages[key] = image;
        if (!_imageList.Images.ContainsKey(key))
            _imageList.Images.Add(key, image);
    }

    private static string CustomIconKey(string id) => "<CUSTOM:" + id + ">";

    private bool EnsureCustomIcon(string key, string path)
    {
        if (_iconImages.ContainsKey(key))
            return true;

        var image = CustomIconStore.TryLoad(path);
        if (image == null)
            return false;

        RegisterIcon(key, image);
        return true;
    }

    // 清除某个 key 的图标缓存（编辑/删除后强制重新加载）
    private void RemoveCachedIcon(string key)
    {
        if (_iconImages.Remove(key, out var image))
            image?.Dispose();

        if (_imageList.Images.ContainsKey(key))
            _imageList.Images.RemoveByKey(key);
    }

    private async Task LoadFaviconAsync(string url)
    {
        try
        {
            var favicon = await _faviconService.GetFaviconAsync(url);
            if (favicon == null || IsDisposed || !IsHandleCreated)
                return;

            var host = FaviconService.GetHost(url);
            if (host == null)
                return;

            if (InvokeRequired)
                BeginInvoke(() => ApplyFaviconToItems(host, favicon));
            else
                ApplyFaviconToItems(host, favicon);
        }
        catch
        {
            // 加载失败保持通用图标即可
        }
    }

    private void ApplyFaviconToItems(string host, Image favicon)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        var key = FaviconKey(host);
        RegisterIcon(key, favicon);

        var changed = false;
        foreach (ListViewItem item in _listView.Items)
        {
            if (item.Tag is QuickEntry entry
                && entry.Type == EntryType.Url
                && string.Equals(FaviconService.GetHost(entry.Path), host, StringComparison.OrdinalIgnoreCase)
                && item.ImageKey != key)
            {
                item.ImageKey = key;
                changed = true;
            }
        }

        if (changed)
            _listView.Invalidate();
    }

    // 匹配质量分级，数值越小越靠前
    private static int GetMatchRank(QuickEntry entry, string query)
    {
        var name = entry.Name ?? string.Empty;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (!string.IsNullOrEmpty(entry.Path) && entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3; // 仅拼音首字母匹配
    }

    private List<QuickEntry> GetEntriesForActiveTab()
    {
        var entries = _configManager.Config.Entries;
        return _activeTab switch
        {
            TabKind.Folders => entries.Where(e => e.Type == EntryType.Folder).ToList(),
            TabKind.Files => entries.Where(e => e.Type == EntryType.File).ToList(),
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

        // 分组集合（名称+顺序）未变时无需重建 UI。搜索过滤不会改变分组集合
        // （分组取自整类条目），这能避免每次按键都 Dispose/重建 Label + 重新布局测量。
        if (_lastGroupSignature != null && _lastGroupSignature.SequenceEqual(groups, StringComparer.Ordinal))
        {
            ApplyGroupStyles();
            return;
        }
        _lastGroupSignature = new List<string>(groups);

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
            .Select((entry, index) => new
            {
                Entry = entry,
                Index = index,
                Group = NormalizeGroupName(entry.Group)
            })
            .Where(item => !string.IsNullOrEmpty(item.Group))
            .GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.First().Group,
                FirstSortOrder = group.Min(item => item.Entry.SortOrder),
                FirstIndex = group.Min(item => item.Index)
            })
            .OrderBy(item => item.FirstSortOrder)
            .ThenBy(item => item.FirstIndex)
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

    private void SwitchGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            group = AllGroupsLabel;

        if (string.Equals(_activeGroup, group, StringComparison.OrdinalIgnoreCase))
            return;

        _activeGroup = group;
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
        if (e.ColumnIndex == 0)
        {
            var key = item.ImageKey;
            if (!string.IsNullOrEmpty(key) && _iconImages.TryGetValue(key, out var img) && img != null)
            {
                // 所有图标统一占用同一目标边长的方形槽位，等比缩放并居中，用高质量插值，
                // 保证大小一致、清晰、不变形
                var target = _iconRenderSize > 0 ? _iconRenderSize : _imageList.ImageSize.Width;
                var fit = Math.Min((double)target / img.Width, (double)target / img.Height);
                int dw = Math.Max(1, (int)Math.Round(img.Width * fit));
                int dh = Math.Max(1, (int)Math.Round(img.Height * fit));
                int dx = textX + (target - dw) / 2;
                int dy = bounds.Y + (bounds.Height - dh) / 2;

                var prevInterp = g.InterpolationMode;
                var prevOffset = g.PixelOffsetMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(img, new Rectangle(dx, dy, dw, dh));
                g.InterpolationMode = prevInterp;
                g.PixelOffsetMode = prevOffset;
                textX += target + UiScaleHelper.Scale(this, 4);
            }
        }

        var textColor = item.Selected ? Color.FromArgb(59, 130, 246) : item.ForeColor;
        var textBounds = new Rectangle(textX, bounds.Y, bounds.Right - textX - UiScaleHelper.Scale(this, 2), bounds.Height);
        var display = GetTruncatedText(item.Text, textBounds.Width);

        TextRenderer.DrawText(
            g,
            display,
            _listView.Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private string GetTruncatedText(string text, int maxPx)
    {
        if (string.IsNullOrEmpty(text) || maxPx <= 0)
            return text;

        var key = (text, maxPx);
        if (_truncateCache.TryGetValue(key, out var cached))
            return cached;

        var result = MidTruncate(text, _listView.Font, maxPx);
        _truncateCache[key] = result;
        return result;
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

    private void OnListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem { Tag: QuickEntry entry })
            return;

        if (!CanReorderEntries())
        {
            ShowToast("搜索时不能调整顺序");
            return;
        }

        var data = new DataObject();
        data.SetData(EntryReorderDataFormat, entry.Id);
        _listView.DoDragDrop(data, DragDropEffects.Move);
    }

    private bool CanReorderEntries()
        => string.IsNullOrWhiteSpace(_searchBox.Text);

    private static bool HasEntryReorderData(IDataObject? data)
        => data?.GetDataPresent(EntryReorderDataFormat) == true;

    private static string? GetEntryReorderId(IDataObject? data)
        => data?.GetData(EntryReorderDataFormat) as string;

    private bool TryReorderEntryFromDrop(DragEventArgs e)
    {
        var draggedId = GetEntryReorderId(e.Data);
        if (string.IsNullOrWhiteSpace(draggedId) || !CanReorderEntries())
            return false;

        var scopeEntries = GetDisplayedEntries().ToList();
        var oldIndex = scopeEntries.FindIndex(entry => string.Equals(entry.Id, draggedId, StringComparison.OrdinalIgnoreCase));
        if (oldIndex < 0)
            return false;

        var clientPoint = _listView.PointToClient(new Point(e.X, e.Y));
        var targetItem = _listView.GetItemAt(clientPoint.X, clientPoint.Y);
        var insertIndex = scopeEntries.Count;

        if (targetItem?.Tag is QuickEntry targetEntry)
        {
            insertIndex = scopeEntries.FindIndex(entry => string.Equals(entry.Id, targetEntry.Id, StringComparison.OrdinalIgnoreCase));
            if (insertIndex < 0)
                insertIndex = scopeEntries.Count;
            else if (clientPoint.Y > targetItem.Bounds.Top + targetItem.Bounds.Height / 2)
                insertIndex++;
        }

        var draggedEntry = scopeEntries[oldIndex];
        scopeEntries.RemoveAt(oldIndex);
        if (insertIndex > oldIndex)
            insertIndex--;

        insertIndex = Math.Clamp(insertIndex, 0, scopeEntries.Count);
        scopeEntries.Insert(insertIndex, draggedEntry);

        var fullCategoryEntries = GetEntriesForActiveTab()
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.AddedAt)
            .ToList();

        var orderedEntries = string.Equals(_activeGroup, AllGroupsLabel, StringComparison.OrdinalIgnoreCase)
            ? scopeEntries
            : MergeReorderedGroupEntries(fullCategoryEntries, scopeEntries);

        _configManager.ReorderEntries(orderedEntries.Select(entry => entry.Id));
        RefreshList();
        SelectEntryById(draggedId);
        ShowToast("已调整顺序");
        return true;
    }

    private IEnumerable<QuickEntry> GetDisplayedEntries()
    {
        var entries = GetEntriesForActiveTab()
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.AddedAt);

        if (string.Equals(_activeGroup, AllGroupsLabel, StringComparison.OrdinalIgnoreCase))
            return entries.ToList();

        return entries
            .Where(IsEntryInActiveGroup)
            .ToList();
    }

    private List<QuickEntry> MergeReorderedGroupEntries(List<QuickEntry> fullCategoryEntries, List<QuickEntry> reorderedGroupEntries)
    {
        var groupQueue = new Queue<QuickEntry>(reorderedGroupEntries);
        var merged = new List<QuickEntry>(fullCategoryEntries.Count);

        foreach (var entry in fullCategoryEntries)
            merged.Add(IsEntryInActiveGroup(entry) ? groupQueue.Dequeue() : entry);

        return merged;
    }

    private bool IsEntryInActiveGroup(QuickEntry entry)
        => string.Equals(NormalizeGroupName(entry.Group), _activeGroup, StringComparison.OrdinalIgnoreCase);

    public void ShowAtGesturePoint(Point screenPt)
    {
        _activeTab = TabKind.Folders;
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
            TabKind[] kinds = [TabKind.Folders, TabKind.Files, TabKind.Urls, TabKind.Texts];
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
        if (sender == _listView && HasEntryReorderData(e.Data) && CanReorderEntries())
            e.Effect = DragDropEffects.Move;
        else if (_activeTab is TabKind.Folders or TabKind.Files && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Link;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender == _listView && HasEntryReorderData(e.Data) && CanReorderEntries())
            e.Effect = DragDropEffects.Move;
        else if (_activeTab is TabKind.Folders or TabKind.Files && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Link;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (sender == _listView && HasEntryReorderData(e.Data))
        {
            TryReorderEntryFromDrop(e);
            return;
        }

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
        using var pen = new Pen(Color.FromArgb(220, 220, 220));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _faviconService.Dispose();
            foreach (var image in _iconImages.Values)
                image?.Dispose();
            _iconImages.Clear();
            _webEntryImage?.Dispose();
        }

        base.Dispose(disposing);
    }
}
