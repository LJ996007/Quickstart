namespace Quickstart.UI;

using System.Globalization;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class MainPopup : Form
{
    private enum TabKind { Folders, Files, Urls, Texts, ClipboardHistory, RecentItems }
    private static readonly TabKind[] DefaultTabOrder =
    [
        TabKind.Folders, TabKind.Files, TabKind.Urls, TabKind.Texts,
        TabKind.ClipboardHistory, TabKind.RecentItems
    ];
    private readonly record struct PathStatus(bool Exists, DateTime CheckedAtUtc);

    private static readonly Size ExpandedPopupLogicalSize = new(380, 440);
    private static readonly Size MinimumExpandedPopupLogicalSize = new(300, 340);
    private const int CollapsedPopupHeightDeltaLogical = 28;
    private const string AllGroupsLabel = "全部";
    private const string EntryReorderDataFormat = "Quickstart.EntryReorder";

    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly ClipboardHistoryService? _clipboardHistory;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ImageList _imageList;
    private Image? _webEntryImage;
    private readonly FaviconService _faviconService = new();
    private readonly AsyncIconLoader _iconLoader = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _iconLoadsInFlight = new(StringComparer.OrdinalIgnoreCase);
    // 统一存放高分辨率原图，自绘时再用高质量插值缩放到统一尺寸，保证图标大小/清晰度一致
    private readonly Dictionary<string, Image> _iconImages = new(StringComparer.OrdinalIgnoreCase);
    // 按当前 DPI 预缩放后的图标。列表重绘时直接贴图，避免鼠标划过时反复做高质量插值。
    private readonly Dictionary<string, Bitmap> _renderedIconImages = new(StringComparer.OrdinalIgnoreCase);
    private int _iconRenderSize;
    private const int IconSourceSize = 48;
    private const int MaxTruncateCacheEntries = 2000;
    private static readonly Font BoldMenuFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PathStatus> _pathExistsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _pathChecksInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _pathCheckGate = new(4);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly List<Label> _tabLabels = [];
    private readonly List<TabKind> _tabOrder = [];
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
    private Label? _tabDragLabel;
    private Point _tabDragStart;
    private bool _tabDragging;
    private int _tabDropIndex = -1;
    private bool _suppressTabClick;
    private List<string>? _lastGroupSignature;
    private readonly Dictionary<(string Text, int Width), string> _truncateCache = new();
    // 嵌套计数：右键菜单 / 子对话框打开期间禁止失焦自动隐藏
    private int _autoHideSuspendCount;
    private bool SuppressAutoHide => _autoHideSuspendCount > 0;

    public event Action? ShowSettings;

    public MainPopup(ConfigManager configManager, ProcessLauncher launcher, ClipboardHistoryService? clipboardHistory = null)
    {
        _configManager = configManager;
        _launcher = launcher;
        _clipboardHistory = clipboardHistory;
        RestoreRememberedView();
        _tabOrder.AddRange(CreateTabOrder(_configManager.Config.MainPopupTabOrder));

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

        _listView = new BufferedListView
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

        _tabLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = _tabOrder.Count + 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(240, 240, 240)
        };
        _tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < _tabOrder.Count; i++)
            _tabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        _tabLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (var i = 0; i < _tabOrder.Count; i++)
        {
            var kind = _tabOrder[i];
            var label = new Label
            {
                Text = GetTabText(kind),
                Tag = kind,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9f),
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            label.Click += (_, _) => OnTabLabelClick(kind);
            label.MouseDown += OnTabLabelMouseDown;
            label.MouseMove += OnTabLabelMouseMove;
            label.MouseUp += OnTabLabelMouseUp;

            var tooltip = kind switch
            {
                TabKind.ClipboardHistory => "剪贴板历史",
                TabKind.RecentItems => "Windows 最近使用的文件和文件夹",
                _ => kind.ToString()
            };
            _toolTip.SetToolTip(label, $"{tooltip}\n按住并上下拖动可调整顺序");
            _tabLabels.Add(label);
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

        _listView.MouseUp += OnListMouseUp;

        _listView.MouseDoubleClick += (_, _) =>
        {
            if (_listView.SelectedItems.Count > 0)
                OpenSelectedEntry();
        };

        // 左键单击历史/文本复制；右键弹出条目菜单（见 OnListMouseUp）

        if (_clipboardHistory != null)
            _clipboardHistory.Changed += OnClipboardHistoryChanged;

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
                if (_activeTab == TabKind.ClipboardHistory)
                {
                    var hist = GetSelectedHistoryItem();
                    if (hist != null)
                    {
                        _clipboardHistory?.Remove(hist.Id);
                        RefreshList();
                    }
                }
                else
                {
                    DeleteSelectedEntry();
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        // 拼音有记忆化、分组签名未变时不重建 UI，120ms 比 200ms 更跟手
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 120 };
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
            // 右键菜单 / 编辑对话框期间不自动隐藏，方便连续操作
            if (Visible && !SuppressAutoHide) Hide();
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
            BackColor = Color.FromArgb(250, 250, 250),
            ForeColor = Color.FromArgb(72, 72, 72),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 235);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 225, 225);
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

        for (int i = 0; i < _tabLabels.Count; i++)
            _tabLayout.RowStyles[i].Height = tabHeight;
        _tabLayout.MinimumSize = new Size(tabWidth, tabHeight * _tabLabels.Count);
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
        DisposeRenderedIcons();
        // 通用网页占位图按固定高分辨率生成一次，缩放交给自绘
        _webEntryImage ??= LoadWebEntryImage(IconSourceSize);

        // 原图与 DPI 无关，保留缓存；仅重建 ImageList 的行高占位图和目标尺寸绘制缓存。
        foreach (var pair in _iconImages)
            _imageList.Images.Add(pair.Key, pair.Value);

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

    private static string GetTabText(TabKind kind)
        => kind switch
        {
            TabKind.Folders => "文\n件\n夹",
            TabKind.Files => "文\n件",
            TabKind.Urls => "网\n页",
            TabKind.Texts => "文\n本",
            TabKind.ClipboardHistory => "剪\n贴\n板",
            TabKind.RecentItems => "最\n近",
            _ => kind.ToString()
        };

    private static IEnumerable<TabKind> CreateTabOrder(IEnumerable<string>? savedOrder)
    {
        var included = new HashSet<TabKind>();
        foreach (var savedTab in savedOrder ?? [])
        {
            if (Enum.TryParse<TabKind>(savedTab, ignoreCase: true, out var kind)
                && Enum.IsDefined(kind)
                && included.Add(kind))
            {
                yield return kind;
            }
        }

        foreach (var kind in DefaultTabOrder)
        {
            if (included.Add(kind))
                yield return kind;
        }
    }

    private void OnTabLabelClick(TabKind kind)
    {
        if (_suppressTabClick)
            return;

        if (CanSwitchTab(kind))
            SwitchTab(kind);
    }

    private void OnTabLabelMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Label label)
            return;

        _tabDragLabel = label;
        _tabDragStart = Cursor.Position;
        _tabDragging = false;
        _tabDropIndex = -1;
    }

    private void OnTabLabelMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _tabDragLabel == null)
            return;

        if (!_tabDragging)
        {
            var dragSize = SystemInformation.DragSize;
            var dragBounds = new Rectangle(
                _tabDragStart.X - dragSize.Width / 2,
                _tabDragStart.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);
            if (dragBounds.Contains(Cursor.Position))
                return;

            _tabDragging = true;
            _tabDragLabel.Cursor = Cursors.SizeNS;
        }

        var tabPoint = _tabLayout.PointToClient(Cursor.Position);
        var targetIndex = _tabLabels.FindIndex(label => label.Bounds.Contains(tabPoint));
        if (targetIndex < 0)
        {
            if (tabPoint.Y < 0)
                targetIndex = 0;
            else if (tabPoint.Y >= _tabLayout.ClientSize.Height)
                targetIndex = _tabLabels.Count - 1;
            else
                return;
        }

        if (_tabDropIndex == targetIndex)
            return;

        _tabDropIndex = targetIndex;
        ApplyTabStyles();
        if (_tabLabels[targetIndex].Tag is TabKind targetKind && targetKind != _activeTab)
            _tabLabels[targetIndex].BackColor = Color.FromArgb(220, 232, 246);
    }

    private void OnTabLabelMouseUp(object? sender, MouseEventArgs e)
    {
        if (_tabDragging)
        {
            _suppressTabClick = true;
            BeginInvoke(() => _suppressTabClick = false);
        }

        var sourceIndex = _tabDragLabel == null ? -1 : _tabLabels.IndexOf(_tabDragLabel);
        if (_tabDragging
            && sourceIndex >= 0
            && _tabDropIndex >= 0
            && _tabDropIndex < _tabLabels.Count
            && sourceIndex != _tabDropIndex)
        {
            MoveTabLabel(sourceIndex, _tabDropIndex);
            _configManager.SetMainPopupTabOrder(_tabOrder.Select(kind => kind.ToString()));
            ShowToast("标签顺序已保存");
        }

        ResetTabDragState();
    }

    private void MoveTabLabel(int sourceIndex, int targetIndex)
    {
        var label = _tabLabels[sourceIndex];
        var kind = _tabOrder[sourceIndex];
        _tabLabels.RemoveAt(sourceIndex);
        _tabLabels.Insert(targetIndex, label);
        _tabOrder.RemoveAt(sourceIndex);
        _tabOrder.Insert(targetIndex, kind);

        _tabLayout.SuspendLayout();
        try
        {
            foreach (var tabLabel in _tabLabels)
                _tabLayout.Controls.Remove(tabLabel);
            for (var index = 0; index < _tabLabels.Count; index++)
                _tabLayout.Controls.Add(_tabLabels[index], 0, index);
        }
        finally
        {
            _tabLayout.ResumeLayout(performLayout: true);
        }

        ApplyTabStyles();
    }

    private void ResetTabDragState()
    {
        if (_tabDragLabel != null)
            _tabDragLabel.Cursor = Cursors.Hand;
        _tabDragLabel = null;
        _tabDragging = false;
        _tabDropIndex = -1;
        ApplyTabStyles();
    }

    private bool CanSwitchTab(TabKind kind)
        => kind != TabKind.ClipboardHistory
            || _configManager.Config.ClipboardHistory?.Enabled != false
            || _activeTab == TabKind.ClipboardHistory;

    private void SwitchTab(TabKind kind)
    {
        if (_activeTab == kind) return;
        _activeTab = kind;
        _activeGroup = AllGroupsLabel;
        PersistCurrentView();
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        RefreshList();
    }

    private void ApplyTabStyles()
    {
        var historyEnabled = _configManager.Config.ClipboardHistory?.Enabled != false;
        foreach (var label in _tabLabels)
        {
            var kind = label.Tag is TabKind taggedKind ? taggedKind : TabKind.Folders;
            var active = kind == _activeTab;
            var available = kind != TabKind.ClipboardHistory || historyEnabled || active;
            label.BackColor = active ? Color.FromArgb(60, 60, 60) : Color.FromArgb(240, 240, 240);
            label.ForeColor = active
                ? Color.White
                : available ? Color.FromArgb(80, 80, 80) : Color.FromArgb(165, 165, 165);
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
            TabKind.ClipboardHistory => "搜索剪贴板历史...",
            TabKind.RecentItems => "搜索最近使用的文件和文件夹...",
            _ => "搜索..."
        };

        // 系统数据 Tab 不支持新增收藏
        var canAdd = _activeTab is not (TabKind.ClipboardHistory or TabKind.RecentItems);
        _addButton.Enabled = canAdd;
        _addButton.Visible = canAdd;
    }

    private void SuspendAutoHide() => _autoHideSuspendCount++;

    private void ResumeAutoHide()
    {
        if (_autoHideSuspendCount > 0)
            _autoHideSuspendCount--;
    }

    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        var hit = FindListItemAtClientPoint(new Point(e.X, e.Y));
        if (hit == null)
            return;

        // 右键：选中光标下的项再弹菜单（不要依赖 FocusedItem，避免点偏/焦点漂移）
        if (e.Button == MouseButtons.Right)
        {
            hit.Selected = true;
            hit.Focused = true;
            ShowItemContextMenu(e.Location);
            return;
        }

        // 左键单击：与手势松手一致，直接打开/复制（拖拽排序走 ItemDrag，不会走到这里）
        if (e.Button != MouseButtons.Left)
            return;

        hit.Selected = true;
        OpenSelectedEntry();
    }

    /// <summary>
    /// Details 模式下 GetItemAt 常只命中图标/文字区域，行内空白点不中；
    /// 手势高亮/松手需要按「整行」命中，与 FullRowSelect 视觉一致。
    /// </summary>
    private ListViewItem? FindListItemAtScreenPoint(Point screenPt)
        => FindListItemAtClientPoint(_listView.PointToClient(screenPt));

    private ListViewItem? FindListItemAtClientPoint(Point clientPt)
    {
        if (!_listView.ClientRectangle.Contains(clientPt))
            return null;

        // 先走系统 HitTest / GetItemAt
        var hit = _listView.HitTest(clientPt);
        if (hit.Item != null)
            return hit.Item;

        var byAt = _listView.GetItemAt(clientPt.X, clientPt.Y);
        if (byAt != null)
            return byAt;

        // 回退：按行 Bounds 的整行矩形命中（X 扩到列表客户区宽度）
        foreach (ListViewItem item in _listView.Items)
        {
            var bounds = item.Bounds;
            if (bounds.Height <= 0)
                continue;

            var fullRow = new Rectangle(
                _listView.ClientRectangle.Left,
                bounds.Top,
                _listView.ClientSize.Width,
                bounds.Height);
            if (fullRow.Contains(clientPt))
                return item;
        }

        return null;
    }

    /// <summary>
    /// 列表项右键菜单。必须延迟 Dispose：若菜单项 Click 里再开模态对话框，
    /// 会泵消息触发 Closed，过早 Dispose 会抛 ObjectDisposedException(ContextMenuStrip)。
    /// 菜单打开期间同样要抑制失焦隐藏，否则 Deactivate 会先 Hide 主窗体导致打开/编辑失效。
    /// </summary>
    private void ShowItemContextMenu(Point location)
    {
        var menu = BuildItemContextMenu();
        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        SuspendAutoHide();
        menu.Closed += (_, _) =>
        {
            // 等当前 Click / 即将弹出的模态框消息队列走完再释放
            BeginInvoke(() =>
            {
                try
                {
                    if (!menu.IsDisposed)
                        menu.Dispose();
                }
                catch
                {
                    // ignore dispose races
                }

                ResumeAutoHide();
            });
        };

        menu.Show(_listView, location);
    }

    // 打开子对话框时保持主弹窗可见（抑制失焦自动隐藏），关闭后重新激活，便于连续编辑
    private DialogResult ShowChildDialog(Form dialog)
    {
        SuspendAutoHide();
        try
        {
            return DialogPresenter.ShowModal(dialog, this);
        }
        finally
        {
            ResumeAutoHide();
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
        var menu = new ContextMenuStrip();

        if (_activeTab == TabKind.ClipboardHistory)
        {
            var hist = GetSelectedHistoryItem();
            if (hist == null)
                return menu;

            var copy = new ToolStripMenuItem("复制为纯文本") { Font = BoldMenuFont };
            copy.Click += (_, _) => OpenSelectedEntry();
            menu.Items.Add(copy);

            menu.Items.Add(new ToolStripSeparator());

            var del = new ToolStripMenuItem("删除本条");
            del.Click += (_, _) =>
            {
                _clipboardHistory?.Remove(hist.Id);
                RefreshList();
            };
            menu.Items.Add(del);

            var clear = new ToolStripMenuItem("清空历史");
            clear.Click += (_, _) =>
            {
                if (MessageBox.Show(this, "确定清空全部剪贴板历史？", "剪贴板历史",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                _clipboardHistory?.Clear();
                RefreshList();
            };
            menu.Items.Add(clear);
            return menu;
        }

        if (_activeTab == TabKind.RecentItems)
        {
            var recent = GetSelectedRecentItem();
            if (recent == null)
                return menu;

            var open = new ToolStripMenuItem("打开") { Font = BoldMenuFont };
            open.Click += (_, _) => OpenSelectedEntry();
            menu.Items.Add(open);

            var locate = new ToolStripMenuItem("在资源管理器中定位");
            locate.Click += (_, _) => ProcessLauncher.OpenInExplorer(recent.DisplayPath);
            menu.Items.Add(locate);
            return menu;
        }

        var entry = GetSelectedEntry();
        if (entry == null) return menu;

        switch (entry.Type)
        {
            case EntryType.Folder:
            case EntryType.File:
            {
                var openDefault = new ToolStripMenuItem("打开") { Font = BoldMenuFont };
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
                var openUrl = new ToolStripMenuItem("在浏览器中打开") { Font = BoldMenuFont };
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
                var copyText = new ToolStripMenuItem("复制文本") { Font = BoldMenuFont };
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
        if (_configManager.Config.RememberLastView)
        {
            RestoreRememberedView();
            ShowPopup(_activeTab, preserveGroup: true);
        }
        else
        {
            ShowPopup(TabKind.Folders);
        }
    }

    private void ShowPopup(TabKind kind, bool focusList = true, bool preserveGroup = false)
    {
        _activeTab = kind;
        if (!preserveGroup)
            _activeGroup = AllGroupsLabel;
        _searchBox.Clear();
        SetSearchExpanded(false);
        ApplyTabStyles();
        UpdateSearchPlaceholder();
        EnsurePopupSizeForScreen(Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position));
        RefreshList();
        PersistCurrentView();
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
        if (_activeTab == TabKind.ClipboardHistory)
            return;

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

        var originalPath = entry.Path;
        using var form = new EntryEditForm(entry, BuildGroupSuggestions());
        if (ShowChildDialog(form) == DialogResult.OK)
        {
            _configManager.UpdateEntry(entry);
            _pathExistsCache.TryRemove(originalPath, out _);
            _pathExistsCache.TryRemove(entry.Path, out _);
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
        SuspendAutoHide();
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
            ResumeAutoHide();
            ReactivateAfterChildDialog();
        }

        if (result == DialogResult.Yes)
        {
            _configManager.RemoveEntry(entry.Id);
            _pathExistsCache.TryRemove(entry.Path, out _);
            CustomIconStore.Remove(entry.Id);
            RemoveCachedIcon(CustomIconKey(entry.Id));
            ReconcileActiveGroup();
            RefreshList();
        }
    }

    private void OpenSelectedEntry(OpenWith? overrideWith = null)
    {
        if (_activeTab == TabKind.ClipboardHistory)
        {
            var hist = GetSelectedHistoryItem();
            if (hist != null)
                ExecuteHistoryItem(hist, hideFirst: true);
            return;
        }

        if (_activeTab == TabKind.RecentItems)
        {
            var recent = GetSelectedRecentItem();
            if (recent != null)
                ExecuteRecentItem(recent, hideFirst: true);
            return;
        }

        var entry = GetSelectedEntry();
        if (entry == null) return;
        ExecuteEntry(entry, overrideWith, hideFirst: true);
    }

    private void ExecuteHistoryItem(ClipboardHistoryItem item, bool hideFirst = false)
    {
        if (_clipboardHistory == null || string.IsNullOrEmpty(item.Text))
            return;

        // 先 Toast + 关窗，复制走服务（STA 重试 + 纯文本）
        ShowToast("已复制");
        if (hideFirst)
            Hide();

        _ = CopyHistoryItemAsync(item.Text);
    }

    private async Task CopyHistoryItemAsync(string text)
    {
        try
        {
            if (_clipboardHistory != null)
                await _clipboardHistory.CopyPlainTextAsync(text);
            else
                CopyToClipboard(text);
        }
        catch
        {
            // 写剪贴板失败时不再弹窗打扰
        }
    }

    private void ExecuteEntry(QuickEntry entry, OpenWith? overrideWith = null, bool hideFirst = false)
    {
        // 文本复制必须在 UI 线程；先复制再关窗，避免剪贴板/Toast 异常。
        if (entry.Type == EntryType.Text)
        {
            _configManager.TouchEntry(entry.Id);
            CopyToClipboard(entry.Path);
            if (hideFirst)
                Hide();
            return;
        }

        // 先在仍有前台权限时授权，再关弹窗。Process.Start 必须留在 UI 线程：
        // 放到 Task.Run 后，Windows 会把外部程序视为后台抢焦点，Directory Opus /
        // Everything 一类单实例程序尤其容易首次正常、后续只显示不激活。
        WindowActivator.AllowAnyForeground();
        if (hideFirst)
            Hide();

        LaunchEntry(entry, overrideWith);
    }

    private void LaunchEntry(QuickEntry entry, OpenWith? overrideWith)
    {
        try
        {
            if (entry.Type == EntryType.Url)
            {
                _configManager.TouchEntry(entry.Id);
                ProcessLauncher.OpenUrl(entry.Path);
                return;
            }

            // File / Folder：ProcessLauncher 内部会 TouchEntry。
            _launcher.Open(entry, overrideWith);
        }
        catch
        {
            // 打开失败静默忽略，与 ProcessLauncher 原有行为一致。
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
        PersistCurrentView();

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

    private void RestoreRememberedView()
    {
        var config = _configManager.Config;
        if (!config.RememberLastView)
            return;

        _activeTab = Enum.TryParse<TabKind>(config.LastViewTab, ignoreCase: true, out var tab)
            ? tab
            : TabKind.Folders;
        _activeGroup = string.IsNullOrWhiteSpace(config.LastViewGroup)
            ? AllGroupsLabel
            : config.LastViewGroup.Trim();
    }

    private void PersistCurrentView()
    {
        if (_configManager.Config.RememberLastView)
            _configManager.SetLastView(_activeTab.ToString(), _activeGroup);
    }

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
        if (_activeTab == TabKind.ClipboardHistory)
        {
            RefreshClipboardHistoryList();
            return;
        }

        if (_activeTab == TabKind.RecentItems)
        {
            RefreshRecentItemsList();
            return;
        }

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
            entries = _configManager.Config.SortByRecentUsage
                ? entries.OrderByDescending(e => e.LastUsedAt).ThenBy(e => e.SortOrder).ToList()
                : entries.OrderBy(e => e.SortOrder).ToList();
        }

        var faviconsToLoad = new List<string>();
        var fileIconsToLoad = new List<(string IconKey, string Path)>();
        var customIconsToLoad = new List<(string IconKey, string Path)>();
        var pathsToValidate = new List<QuickEntry>();

        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var entry in entries)
        {
            string? iconKey;
            if (entry.Type == EntryType.Url)
            {
                var customKey = CustomIconKey(entry.Id);
                if (!string.IsNullOrEmpty(entry.CustomIconPath) && _iconImages.ContainsKey(customKey))
                {
                    // 用户自定义图标（已在内存）
                    iconKey = customKey;
                }
                else if (!string.IsNullOrEmpty(entry.CustomIconPath))
                {
                    // 自定义图标走后台读盘，先用网页通用占位
                    iconKey = EnsureUrlPlaceholderIcon();
                    customIconsToLoad.Add((customKey, entry.CustomIconPath));
                }
                else
                {
                    // 仅查内存 favicon；磁盘/网络加载走后台，避免 UI 同步 I/O
                    var host = FaviconService.GetHost(entry.Path);
                    var favicon = host != null ? _faviconService.TryGetMemoryCached(entry.Path) : null;
                    if (favicon != null && host != null)
                    {
                        iconKey = FaviconKey(host);
                        RegisterIcon(iconKey, favicon, clone: true);
                    }
                    else
                    {
                        iconKey = EnsureUrlPlaceholderIcon();
                        if (host != null && !_faviconService.IsKnownMiss(entry.Path))
                            faviconsToLoad.Add(entry.Path);
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
                // .exe/.lnk 等：未缓存时用扩展名通用图标占位，后台补真实图标
                if (IconExtractor.NeedsPerFileIcon(entry.Path))
                {
                    var realKey = FileIconKey(entry.Path);
                    if (_iconImages.ContainsKey(realKey))
                    {
                        iconKey = realKey;
                    }
                    else
                    {
                        iconKey = EnsureGenericExtensionIcon(entry.Path);
                        fileIconsToLoad.Add((realKey, entry.Path));
                    }
                }
                else
                {
                    iconKey = EnsureGenericExtensionIcon(entry.Path);
                }
            }

            var item = string.IsNullOrEmpty(iconKey)
                ? new ListViewItem(entry.Name)
                : new ListViewItem(entry.Name, iconKey) { ImageKey = iconKey };

            item.Tag = entry;
            item.ToolTipText = entry.Type == EntryType.Text
                ? (entry.Path.Length > 200 ? entry.Path[..200] + "..." : entry.Path)
                : entry.Path;

            if (entry.Type is EntryType.Folder or EntryType.File)
            {
                if (_pathExistsCache.TryGetValue(entry.Path, out var status)
                    && DateTime.UtcNow - status.CheckedAtUtc < TimeSpan.FromSeconds(30))
                {
                    if (!status.Exists)
                        item.ForeColor = Color.Red;
                }
                else
                {
                    pathsToValidate.Add(entry);
                }
            }

            _listView.Items.Add(item);
        }

        _listView.EndUpdate();

        _countLabel.Text = $"{entries.Count} 项";
        UpdateListColumnWidth();

        foreach (var url in faviconsToLoad.Distinct(StringComparer.OrdinalIgnoreCase))
            _ = LoadFaviconAsync(url);

        foreach (var (iconKey, path) in fileIconsToLoad)
            QueueFileIconLoad(iconKey, path);

        foreach (var (iconKey, path) in customIconsToLoad)
            QueueCustomIconLoad(iconKey, path);

        QueuePathValidation(pathsToValidate);
    }

    private static string FaviconKey(string host) => "<FAV:" + host + ">";
    private static string FileIconKey(string path) => "<FILE:" + path + ">";

    private string EnsureUrlPlaceholderIcon()
    {
        if (_webEntryImage != null)
        {
            const string key = "<URL_CUSTOM>";
            RegisterIcon(key, _webEntryImage, clone: true);
            return key;
        }

        const string fallback = ".url";
        if (!_iconImages.ContainsKey(fallback))
            RegisterIcon(fallback, IconExtractor.GetGenericTypeIcon(".url", useLargeIcon: true)?.ToBitmap());
        return fallback;
    }

    private string EnsureGenericExtensionIcon(string path)
    {
        var iconKey = Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrEmpty(iconKey))
            iconKey = "<NOEXT>";
        if (!_iconImages.ContainsKey(iconKey))
            RegisterIcon(iconKey, IconExtractor.GetGenericTypeIcon(
                iconKey == "<NOEXT>" ? string.Empty : iconKey,
                useLargeIcon: true)?.ToBitmap());
        return iconKey;
    }

    // 登记一张原图：_iconImages 供自绘高质量缩放，_imageList 仅用于撑行高
    private void RegisterIcon(string key, Image? image, bool clone = false)
    {
        if (image == null || _iconImages.ContainsKey(key))
            return;

        // FaviconService 和网页占位图仍由各自服务持有；登记时复制一份，避免重复释放。
        var ownedImage = clone ? new Bitmap(image) : image;
        _iconImages[key] = ownedImage;
        if (!_imageList.Images.ContainsKey(key))
            _imageList.Images.Add(key, ownedImage);
    }

    /// <summary>覆盖登记图标（异步真图到达时替换占位）。</summary>
    private void ReplaceIcon(string key, Image image)
    {
        RemoveCachedIcon(key);
        RegisterIcon(key, image);
    }

    private static string CustomIconKey(string id) => "<CUSTOM:" + id + ">";

    private void QueueFileIconLoad(string iconKey, string path)
    {
        if (!_iconLoadsInFlight.TryAdd(iconKey, 0))
            return;

        var token = _disposeCts.Token;
        _iconLoader.Enqueue(() =>
        {
            try
            {
                if (token.IsCancellationRequested)
                    return;

                // Icon 由 IconExtractor 全局缓存持有，不可 Dispose
                var icon = IconExtractor.ExtractRealFileIcon(path, useLargeIcon: true);
                if (icon == null)
                    return;

                var bitmap = icon.ToBitmap();
                if (IsDisposed || !IsHandleCreated || token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return;
                }

                try
                {
                    BeginInvoke(() =>
                    {
                        try
                        {
                            if (IsDisposed || token.IsCancellationRequested)
                            {
                                bitmap.Dispose();
                                return;
                            }

                            ReplaceIcon(iconKey, bitmap);
                            ApplyIconKeyToItems(iconKey, match: tag =>
                                tag is QuickEntry entry
                                && entry.Type == EntryType.File
                                && string.Equals(FileIconKey(entry.Path), iconKey, StringComparison.OrdinalIgnoreCase));
                            ApplyIconKeyToRecentItems(iconKey, path);
                        }
                        catch
                        {
                            bitmap.Dispose();
                        }
                    });
                }
                catch
                {
                    bitmap.Dispose();
                }
            }
            finally
            {
                _iconLoadsInFlight.TryRemove(iconKey, out _);
            }
        });
    }

    private void QueueCustomIconLoad(string iconKey, string path)
    {
        if (!_iconLoadsInFlight.TryAdd(iconKey, 0))
            return;

        var token = _disposeCts.Token;
        _iconLoader.Enqueue(() =>
        {
            try
            {
                if (token.IsCancellationRequested)
                    return;

                var image = CustomIconStore.TryLoad(path);
                if (image == null)
                    return;

                if (IsDisposed || !IsHandleCreated || token.IsCancellationRequested)
                {
                    image.Dispose();
                    return;
                }

                try
                {
                    BeginInvoke(() =>
                    {
                        try
                        {
                            if (IsDisposed || token.IsCancellationRequested)
                            {
                                image.Dispose();
                                return;
                            }

                            ReplaceIcon(iconKey, image);
                            ApplyIconKeyToItems(iconKey, match: tag =>
                                tag is QuickEntry entry
                                && entry.Type == EntryType.Url
                                && string.Equals(CustomIconKey(entry.Id), iconKey, StringComparison.OrdinalIgnoreCase));
                        }
                        catch
                        {
                            image.Dispose();
                        }
                    });
                }
                catch
                {
                    image.Dispose();
                }
            }
            finally
            {
                _iconLoadsInFlight.TryRemove(iconKey, out _);
            }
        });
    }

    private void ApplyIconKeyToItems(string iconKey, Func<object?, bool> match)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        var changed = false;
        foreach (ListViewItem item in _listView.Items)
        {
            if (match(item.Tag) && item.ImageKey != iconKey)
            {
                item.ImageKey = iconKey;
                changed = true;
            }
        }

        if (changed)
            _listView.Invalidate();
    }

    private void ApplyIconKeyToRecentItems(string iconKey, string path)
    {
        if (_activeTab != TabKind.RecentItems)
            return;

        var changed = false;
        foreach (ListViewItem item in _listView.Items)
        {
            if (item.Tag is WindowsRecentItem recent
                && string.Equals(recent.DisplayPath, path, StringComparison.OrdinalIgnoreCase)
                && item.ImageKey != iconKey)
            {
                item.ImageKey = iconKey;
                changed = true;
            }
        }

        if (changed)
            _listView.Invalidate();
    }

    // 清除某个 key 的图标缓存（编辑/删除后强制重新加载）
    private void RemoveCachedIcon(string key)
    {
        if (_iconImages.Remove(key, out var image))
            image?.Dispose();

        if (_renderedIconImages.Remove(key, out var rendered))
            rendered.Dispose();

        if (_imageList.Images.ContainsKey(key))
            _imageList.Images.RemoveByKey(key);

        _iconLoadsInFlight.TryRemove(key, out _);
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
        RegisterIcon(key, favicon, clone: true);

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
        if (_activeTab is TabKind.ClipboardHistory or TabKind.RecentItems)
            return [];

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

    private void RefreshRecentItemsList()
    {
        RebuildGroupLabels([]);
        SetGroupColumnVisible(false);

        var query = _searchBox.Text.Trim();
        var items = WindowsRecentItemsService.GetItems().ToList();
        if (!string.IsNullOrEmpty(query))
        {
            items = items
                .Where(item => PinyinHelper.MatchesPinyin(item.Name, query)
                    || PinyinHelper.MatchesPinyin(item.DisplayPath, query))
                .ToList();
        }

        _listView.BeginUpdate();
        _listView.Items.Clear();

        if (items.Count == 0)
        {
            var empty = new ListViewItem(string.IsNullOrEmpty(query)
                ? "暂无 Windows 最近使用记录"
                : "无匹配的最近使用记录")
            {
                ForeColor = Color.FromArgb(140, 140, 140)
            };
            _listView.Items.Add(empty);
        }
        else
        {
            var fileIconsToLoad = new List<(string IconKey, string Path)>();
            foreach (var recent in items)
            {
                string iconKey;
                if (recent.IsDirectory)
                {
                    iconKey = "<DIR>";
                    if (!_iconImages.ContainsKey(iconKey))
                        RegisterIcon(iconKey, IconExtractor.GetIcon(recent.DisplayPath, isDirectory: true, useLargeIcon: true)?.ToBitmap());
                }
                else if (IconExtractor.NeedsPerFileIcon(recent.DisplayPath))
                {
                    var realKey = FileIconKey(recent.DisplayPath);
                    if (_iconImages.ContainsKey(realKey))
                    {
                        iconKey = realKey;
                    }
                    else
                    {
                        iconKey = EnsureGenericExtensionIcon(recent.DisplayPath);
                        fileIconsToLoad.Add((realKey, recent.DisplayPath));
                    }
                }
                else
                {
                    iconKey = EnsureGenericExtensionIcon(recent.DisplayPath);
                }

                var item = new ListViewItem(recent.Name, iconKey)
                {
                    ImageKey = iconKey,
                    Tag = recent,
                    ToolTipText = $"{FormatRelativeTime(recent.LastUsedAt)}\n{recent.DisplayPath}"
                };
                _listView.Items.Add(item);
            }

            foreach (var (iconKey, path) in fileIconsToLoad)
                QueueFileIconLoad(iconKey, path);
        }

        _listView.EndUpdate();
        _countLabel.Text = items.Count == 0 ? "0 项" : $"{items.Count} 项";
        UpdateListColumnWidth();
    }

    private WindowsRecentItem? GetSelectedRecentItem()
        => _listView.SelectedItems.Count == 0
            ? null
            : _listView.SelectedItems[0].Tag as WindowsRecentItem;

    private void ExecuteRecentItem(WindowsRecentItem item, bool hideFirst = false)
    {
        try
        {
            WindowActivator.AllowAnyForeground();
            if (hideFirst)
                Hide();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.LaunchPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // 最近记录可能在显示后被移动或删除；打开失败时静默忽略。
        }
    }

    private void RefreshClipboardHistoryList()
    {
        // 历史无分组
        RebuildGroupLabels([]);
        SetGroupColumnVisible(false);

        var query = _searchBox.Text.Trim();
        var items = _clipboardHistory?.GetItems().ToList() ?? [];
        if (!string.IsNullOrEmpty(query))
        {
            items = items
                .Where(i => i.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || PinyinHelper.MatchesPinyin(i.Preview(80), query))
                .ToList();
        }

        _listView.BeginUpdate();
        _listView.Items.Clear();

        if (items.Count == 0)
        {
            var empty = new ListViewItem(
                string.IsNullOrEmpty(query)
                    ? "暂无历史 · 复制文本后自动出现"
                    : "无匹配历史");
            empty.ForeColor = Color.FromArgb(140, 140, 140);
            empty.Tag = null;
            _listView.Items.Add(empty);
        }
        else
        {
            foreach (var hist in items)
            {
                var item = new ListViewItem(hist.Preview());
                item.Tag = hist;
                var tip = hist.Text.Length > 800 ? hist.Text[..800] + "…" : hist.Text;
                item.ToolTipText = $"{FormatRelativeTime(hist.CopiedAt)} · {hist.CharCount} 字\n{tip}";
                _listView.Items.Add(item);
            }
        }

        _listView.EndUpdate();
        _countLabel.Text = items.Count == 0 ? "0 项" : $"{items.Count} 项";
        UpdateListColumnWidth();
    }

    private static string FormatRelativeTime(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 45) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} 天前";
        return time.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private ClipboardHistoryItem? GetSelectedHistoryItem()
    {
        if (_listView.SelectedItems.Count == 0) return null;
        return _listView.SelectedItems[0].Tag as ClipboardHistoryItem;
    }

    private void OnClipboardHistoryChanged()
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (!Visible || _activeTab != TabKind.ClipboardHistory)
            return;
        RefreshList();
    }

    private void SetGroupColumnVisible(bool visible)
    {
        _groupLayout.Visible = visible;
        _groupSeparator.Visible = visible;
    }

    private void RebuildGroupLabels(IEnumerable<QuickEntry> currentTypeEntries)
    {
        if (_activeTab is TabKind.ClipboardHistory or TabKind.RecentItems)
        {
            // 清空分组 UI
            if (_lastGroupSignature is { Count: 0 })
            {
                ApplyGroupStyles();
                SetGroupColumnVisible(false);
                return;
            }

            _lastGroupSignature = [];
            _groupLayout.SuspendLayout();
            foreach (var label in _groupLabels)
                label.Dispose();
            _groupLabels.Clear();
            _groupLayout.Controls.Clear();
            _groupLayout.ResumeLayout();
            SetGroupColumnVisible(false);
            return;
        }

        SetGroupColumnVisible(true);
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
        _configManager.TouchGroup(group);
        PersistCurrentView();
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
                var target = _iconRenderSize > 0 ? _iconRenderSize : _imageList.ImageSize.Width;
                var rendered = GetRenderedIcon(key, img, target);
                var dy = bounds.Y + (bounds.Height - rendered.Height) / 2;
                g.DrawImageUnscaled(rendered, textX, dy);
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

    private Bitmap GetRenderedIcon(string key, Image source, int target)
    {
        if (_renderedIconImages.TryGetValue(key, out var cached))
            return cached;

        var rendered = new Bitmap(target, target, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var fit = Math.Min((double)target / source.Width, (double)target / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * fit));
        var height = Math.Max(1, (int)Math.Round(source.Height * fit));
        var x = (target - width) / 2;
        var y = (target - height) / 2;

        using (var graphics = Graphics.FromImage(rendered))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(x, y, width, height));
        }

        _renderedIconImages[key] = rendered;
        return rendered;
    }

    private void DisposeRenderedIcons()
    {
        foreach (var image in _renderedIconImages.Values)
            image.Dispose();
        _renderedIconImages.Clear();
    }

    private string GetTruncatedText(string text, int maxPx)
    {
        if (string.IsNullOrEmpty(text) || maxPx <= 0)
            return text;

        var key = (text, maxPx);
        if (_truncateCache.TryGetValue(key, out var cached))
            return cached;

        // DPI/改名会让 (文本,宽度) 键不断积累；超上限整表清空
        if (_truncateCache.Count > MaxTruncateCacheEntries)
            _truncateCache.Clear();

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

    private void QueuePathValidation(IEnumerable<QuickEntry> entries)
    {
        foreach (var entry in entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                     .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            var path = entry.Path;
            if (!_pathChecksInFlight.TryAdd(path, 0))
                continue;

            _ = ValidatePathAsync(path, entry.Type);
        }
    }

    private async Task ValidatePathAsync(string path, EntryType type)
    {
        var token = _disposeCts.Token;
        var gateEntered = false;
        try
        {
            await _pathCheckGate.WaitAsync(token).ConfigureAwait(false);
            gateEntered = true;

            // File/Directory.Exists 可能在断开的网络盘上阻塞，必须与 UI 和鼠标钩子线程隔离。
            var exists = await Task.Run(
                () => type == EntryType.Folder ? Directory.Exists(path) : File.Exists(path),
                CancellationToken.None).ConfigureAwait(false);

            if (token.IsCancellationRequested)
                return;

            _pathExistsCache[path] = new PathStatus(exists, DateTime.UtcNow);
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(() => ApplyPathStatus(path, exists));
            }
            catch (InvalidOperationException)
            {
                // 窗口正在销毁，无需再更新颜色。
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (gateEntered)
                _pathCheckGate.Release();
            _pathChecksInFlight.TryRemove(path, out _);
        }
    }

    private void ApplyPathStatus(string path, bool exists)
    {
        if (IsDisposed)
            return;

        foreach (ListViewItem item in _listView.Items)
        {
            if (item.Tag is QuickEntry entry
                && entry.Type is EntryType.Folder or EntryType.File
                && string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                item.ForeColor = exists ? _listView.ForeColor : Color.Red;
            }
        }
    }

    private void OnListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem { Tag: QuickEntry entry })
            return;

        if (!CanReorderEntries())
        {
            ShowToast(_configManager.Config.SortByRecentUsage
                ? "最近使用排序下不能手动调整顺序"
                : "搜索时不能调整顺序");
            return;
        }

        var data = new DataObject();
        data.SetData(EntryReorderDataFormat, entry.Id);
        _listView.DoDragDrop(data, DragDropEffects.Move);
    }

    private bool CanReorderEntries()
        => _activeTab is not (TabKind.ClipboardHistory or TabKind.RecentItems)
            && string.IsNullOrWhiteSpace(_searchBox.Text)
            && !_configManager.Config.SortByRecentUsage;

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
        if (_configManager.Config.RememberLastView)
            RestoreRememberedView();
        else
        {
            _activeTab = TabKind.Folders;
            _activeGroup = AllGroupsLabel;
        }
        _searchBox.Clear();
        SetSearchExpanded(false);
        ApplyTabStyles();
        UpdateSearchPlaceholder();

        var screen = Screen.FromPoint(screenPt);
        EnsurePopupSizeForScreen(screen);
        RefreshList();
        PersistCurrentView();

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
            foreach (var label in _tabLabels)
            {
                if (label.Bounds.Contains(tabClientPt) && label.Tag is TabKind kind)
                {
                    if (_activeTab != kind && CanSwitchTab(kind))
                        SwitchTab(kind);
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

        var item = FindListItemAtScreenPoint(screenPt);
        if (item != null)
        {
            if (!item.Selected)
            {
                _listView.SelectedItems.Clear();
                item.Selected = true;
                item.Focused = true;
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

        // 整行命中；松手点在列表区内但略偏时，回退到当前高亮项（手势高亮与松手可能差 1 帧）
        var hitItem = FindListItemAtScreenPoint(screenPt);
        if (hitItem == null)
        {
            var listClient = _listView.PointToClient(screenPt);
            if (_listView.ClientRectangle.Contains(listClient) && _listView.SelectedItems.Count > 0)
                hitItem = _listView.SelectedItems[0];
        }

        if (hitItem?.Tag is ClipboardHistoryItem hist)
        {
            ExecuteHistoryItem(hist, hideFirst: true);
            return true;
        }

        if (hitItem?.Tag is QuickEntry entry)
        {
            ExecuteEntry(entry, hideFirst: true);
            return true;
        }

        if (hitItem?.Tag is WindowsRecentItem recent)
        {
            ExecuteRecentItem(recent, hideFirst: true);
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
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            // 注意：不要在此处加 WS_EX_COMPOSITED（0x02000000）。它会继承到所有子控件，
            // 与 OwnerDraw 的 BufferedListView / Label 命中检测不兼容，会让右滑松手打不开
            // 文件夹/文件（HitTest/GetItemAt 返回 null）。BufferedListView 自身已开双缓冲。
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
            if (_clipboardHistory != null)
                _clipboardHistory.Changed -= OnClipboardHistoryChanged;
            _disposeCts.Cancel();
            _iconLoader.Dispose();
            _debounceTimer.Dispose();
            _toastTimer.Dispose();
            _toolTip.Dispose();
            DisposeRenderedIcons();
            foreach (var image in _iconImages.Values)
                image?.Dispose();
            _iconImages.Clear();
            _webEntryImage?.Dispose();
            _faviconService.Dispose();
            _disposeCts.Dispose();
        }

        base.Dispose(disposing);

        if (disposing)
            _imageList.Dispose();
    }
}
