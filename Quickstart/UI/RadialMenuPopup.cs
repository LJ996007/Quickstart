namespace Quickstart.UI;

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

/// <summary>
/// 右滑圆环轮：透明分层窗体。
/// 中心圆 + 右侧扇形分类环 +（有分组时）更外侧窄环写分组名，再往右横向列表。
/// 手势：继续右滑选中条目并松手执行；在中心松手则停靠，可用鼠标点选，点外部或执行后关闭。
/// </summary>
internal sealed class RadialMenuPopup : Form
{
    private enum TabKind { Folders, Files, Urls, Texts, ClipboardHistory, RecentItems }

    private sealed class TabSlot
    {
        public required TabKind Kind { get; init; }
        public required string Label { get; init; }
        public int Count { get; set; }
        /// <summary>扇区起始角（度，GDI+：0=右，顺时针为正）。</summary>
        public float StartDeg { get; set; }
        public float SweepDeg { get; set; }
        public float MidDeg => StartDeg + SweepDeg / 2f;
        public float MidRad => MidDeg * MathF.PI / 180f;
    }

    /// <summary>分类环外侧窄环上的分组扇区；仅当该标签下存在非空分组时生成。</summary>
    private sealed class GroupSlot
    {
        public required TabKind ParentTab { get; init; }
        public required string Name { get; init; }
        public int Count { get; set; }
        public float StartDeg { get; set; }
        public float SweepDeg { get; set; }
        public float MidDeg => StartDeg + SweepDeg / 2f;
    }

    private sealed class ItemSlot
    {
        public required string Title { get; init; }
        public string? Subtitle { get; init; }
        public object? Payload { get; init; }
        public Image? Icon { get; set; }
        public bool OwnsIcon { get; set; }
        public float Stagger { get; set; }
        public float AnimT { get; set; }
        public RectangleF TargetBounds { get; set; }
    }

    /// <summary>默认顺序（与经典 MainPopup 一致）；用户可通过设置 / 主面板拖拽调整 MainPopupTabOrder。</summary>
    private static readonly TabKind[] DefaultTabOrder =
    [
        TabKind.Folders, TabKind.Files, TabKind.Urls, TabKind.Texts,
        TabKind.ClipboardHistory, TabKind.RecentItems
    ];

    // 平面 UI 色板：无阴影，靠细线与轻填充分层
    private static readonly Color HubFill = Color.FromArgb(255, 255, 255);
    private static readonly Color HubRing = Color.FromArgb(55, 138, 221);
    private static readonly Color Accent = Color.FromArgb(55, 138, 221);
    private static readonly Color AccentSoft = Color.FromArgb(232, 242, 255);
    private static readonly Color AccentMuted = Color.FromArgb(180, 210, 245);
    private static readonly Color SegmentFill = Color.FromArgb(252, 253, 255);
    private static readonly Color SegmentBorder = Color.FromArgb(222, 228, 236);
    private static readonly Color SegmentDivider = Color.FromArgb(210, 216, 224);
    private static readonly Color SegmentText = Color.FromArgb(42, 48, 58);
    private static readonly Color SegmentCount = Color.FromArgb(130, 136, 148);
    private static readonly Color SegmentTextHot = Color.FromArgb(28, 96, 180);
    private static readonly Color RowFill = Color.FromArgb(255, 255, 255);
    private static readonly Color RowFillHot = Color.FromArgb(236, 244, 255);
    private static readonly Color RowBorder = Color.FromArgb(230, 234, 240);
    private static readonly Color RowBorderHot = Color.FromArgb(55, 138, 221);
    private static readonly Color TitleColor = Color.FromArgb(28, 32, 40);
    private static readonly Color SubColor = Color.FromArgb(132, 138, 148);
    private static readonly Color HintColor = Color.FromArgb(148, 154, 164);
    private static readonly Color ListPanelFill = Color.FromArgb(250, 251, 253);
    private static readonly Color ListPanelBorder = Color.FromArgb(218, 224, 232);
    private static readonly Color ScrollTrack = Color.FromArgb(230, 234, 240);
    private static readonly Color ScrollThumb = Color.FromArgb(160, 170, 185);
    private static readonly Color GroupRingFill = Color.FromArgb(246, 249, 253);
    private static readonly Color GroupRingBorder = Color.FromArgb(214, 222, 232);
    private static readonly Color GroupText = Color.FromArgb(70, 78, 92);
    private static readonly Color GroupTextHot = Color.FromArgb(28, 96, 180);

    /// <summary>右侧列表可视行数（面板高度固定）；超出可用滚轮上下滑动。</summary>
    private const int MaxVisibleItems = 8;
    /// <summary>单次载入上限，避免收藏极多时一次建上千图标。</summary>
    private const int MaxLoadedItems = 80;
    // 扇区缩小，为右侧列表让出空间，避免互相遮挡
    private const float HubRadiusLogical = 40f;
    private const float FanInnerRadiusLogical = 48f;
    private const float FanOuterRadiusLogical = 108f;
    /// <summary>分类环外再套一层窄环，仅用于显示该标签下的分组名。</summary>
    private const float FanGroupRingWidthLogical = 22f;
    // 右侧扇区：从上(-90°)到下(+90°)，覆盖右半环，便于右滑连贯
    private const float FanStartDeg = -90f;
    private const float FanTotalSweepDeg = 180f;
    private const float ListGapFromFanLogical = 14f;
    private const float ListRowWidthLogical = 260f;
    private const float ListRowHeightLogical = 44f;
    private const float ListRowGapLogical = 5f;
    private const float ListPanelPadLogical = 8f;
    private const float IconLogical = 20f;
    private const float FormPadLogical = 12f;
    private const int AnimDurationMs = 180;
    private const int AnimFrameMs = 12;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private readonly ConfigManager _configManager;
    private readonly ProcessLauncher _launcher;
    private readonly ClipboardHistoryService? _clipboardHistory;
    private readonly FaviconService _faviconService = new();
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly List<TabSlot> _tabs = [];
    private readonly List<GroupSlot> _groups = [];
    private readonly List<ItemSlot> _items = [];
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _faviconInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _segmentTitleFont;
    private readonly Font _segmentCountFont;
    private readonly Font _groupFont;
    private readonly Font _itemFont;
    private readonly Font _itemSubFont;
    private readonly Font _hubTitleFont;
    private readonly Font _hubCountFont;

    private Image? _webPlaceholder;
    private TabKind? _activeTab;
    private TabKind? _hoverTab;
    private string _activeGroup = EntryQueries.AllGroupsLabel;
    private string? _hoverGroup;
    private int _hoverItemIndex = -1;
    private long _animStartTick;
    private bool _animating;
    private PointF _center;
    private float _dpiScale = 1f;
    private float _hubR;
    private float _fanInnerR;
    private float _fanOuterR;
    /// <summary>分组外环外径；无任何分组时与 _fanOuterR 相同。</summary>
    private float _fanGroupOuterR;
    /// <summary>内容最外沿（列表左侧对齐基准）：有分组时为分组环外径，否则为分类环外径。</summary>
    private float _contentOuterR;
    private RectangleF _listPanelBounds;
    private RectangleF _listClipBounds;
    /// <summary>列表首个可见项下标；仅当 _items.Count &gt; MaxVisibleItems 时可滚动。</summary>
    private int _scrollIndex;
    private bool _renderPending;
    private Bitmap? _layerBmp;
    private Graphics? _layerGfx;
    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _hLayerBmp;
    private IntPtr _oldMemBmp;
    private bool _layerReady;
    private TabKind? _cachedActiveForStatic;
    private TabKind? _cachedHoverForStatic;
    private string? _cachedActiveGroupForStatic;
    private string? _cachedHoverGroupForStatic;
    private Bitmap? _staticLayer; // 扇区+中心缓存，动画时只重绘列表

    // 手势跟踪阶段：NOACTIVATE；中心松手后进入交互模式：可点选 / ESC / 点外部关闭。
    private bool _gestureMode = true;
    private bool _showWithoutActivation = true;
    private bool _suppressAutoHide;
    /// <summary>呼出前的目标窗口，用于「直接粘贴到光标处」。</summary>
    private IntPtr _deliveryTargetWindow;

    public RadialMenuPopup(ConfigManager configManager, ProcessLauncher launcher, ClipboardHistoryService? clipboardHistory = null)
    {
        _configManager = configManager;
        _launcher = launcher;
        _clipboardHistory = clipboardHistory;

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        BackColor = Color.Black;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _segmentTitleFont = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Regular);
        _segmentCountFont = new Font("Microsoft YaHei UI", 7f, FontStyle.Regular);
        _groupFont = new Font("Microsoft YaHei UI", 7f, FontStyle.Regular);
        _itemFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        _itemSubFont = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular);
        _hubTitleFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        _hubCountFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);

        _animTimer = new System.Windows.Forms.Timer { Interval = AnimFrameMs };
        _animTimer.Tick += (_, _) => OnAnimTick();

        ApplyContentSize();
        _center = ComputeWheelCenter();

        Resize += (_, _) =>
        {
            DisposeLayerResources();
            _center = ComputeWheelCenter();
            RelayoutGeometry();
            InvalidateStaticLayer();
            RequestRender();
        };
        DpiChanged += (_, _) =>
        {
            UpdateDpiScale();
            DisposeLayerResources();
            ApplyContentSize();
            _center = ComputeWheelCenter();
            RelayoutGeometry();
            InvalidateStaticLayer();
            RequestRender();
        };
        LocationChanged += (_, _) =>
        {
            // 仅位置变化：直接推当前图层，不必整帧重绘
            if (_layerReady && Visible)
                PushLayeredBitmap();
        };
        Deactivate += (_, _) =>
        {
            if (Visible && !_gestureMode && !_suppressAutoHide)
                Hide();
        };
        MouseMove += OnInteractiveMouseMove;
        MouseDown += OnInteractiveMouseDown;
        MouseWheel += OnListMouseWheel;
    }

    public void ShowAtGesturePoint(Point screenPt, IntPtr sourceWindow = default)
    {
        CaptureDeliveryTarget(sourceWindow);
        UpdateDpiScale();
        ApplyContentSize();
        _center = ComputeWheelCenter();

        EnterGestureMode();

        RebuildTabs();
        _activeTab = ResolveInitialTab();
        _hoverTab = _activeTab;
        _activeGroup = ResolveInitialGroup(_activeTab);
        _hoverGroup = IsAllGroups(_activeGroup) ? null : _activeGroup;
        _hoverItemIndex = -1;
        // 焦点标签就绪后再建外环（仅当前标签有分组时显示，并均分整个右半环）
        RebuildGroupSlots();
        if (_activeTab.HasValue)
            PersistLastView(_activeTab.Value, _activeGroup);
        LoadItemsForActiveTab(restartAnim: true);

        var screen = Screen.FromPoint(screenPt);
        var wa = screen.WorkingArea;
        // 手势点对齐到中心圆，继续右滑即可扫过扇区与列表
        var x = screenPt.X - (int)_center.X;
        var y = screenPt.Y - (int)_center.Y;
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
        Location = new Point(x, y);

        Show();
        RequestRender();
    }

    private void CaptureDeliveryTarget(IntPtr preferred = default)
    {
        if (PlainTextPasteService.IsValidTargetWindow(preferred) && preferred != Handle)
        {
            _deliveryTargetWindow = preferred;
            return;
        }

        try
        {
            var fg = GetForegroundWindow();
            if (PlainTextPasteService.IsValidTargetWindow(fg) && fg != Handle)
            {
                _deliveryTargetWindow = fg;
                return;
            }
        }
        catch
        {
            // ignore
        }

        if (!PlainTextPasteService.IsValidTargetWindow(_deliveryTargetWindow)
            || _deliveryTargetWindow == Handle)
            _deliveryTargetWindow = IntPtr.Zero;
    }

    public void HighlightAtScreenPoint(Point screenPt)
    {
        if (!Visible || !_gestureMode)
            return;

        ApplyPointerAtScreenPoint(screenPt, switchTabOnHover: true);
    }

    /// <summary>
    /// 手势松手：命中列表项则执行并关闭；落在中心圆则进入可点选交互模式；其它位置关闭。
    /// </summary>
    public bool TryReleaseAtScreenPoint(Point screenPt)
    {
        if (!Visible)
            return false;

        var client = PointToClient(screenPt);
        HitTest(client, out _, out _, out var itemIndex);

        if (itemIndex >= 0 && itemIndex < _items.Count)
        {
            ExecutePayload(_items[itemIndex].Payload);
            Hide();
            return true;
        }

        // 中心圆松手：停靠，允许鼠标点选；点界面外或执行动作后再退出
        if (IsOverHub(client))
        {
            EnterInteractiveMode();
            return false;
        }

        Hide();
        return false;
    }

    /// <summary>
    /// 中心松手后：抢前台，支持 ESC / 点击外部关闭，以及鼠标悬停与点击选择。
    /// </summary>
    public void EnterInteractiveMode()
    {
        if (!Visible || IsDisposed)
            return;

        _gestureMode = false;
        _showWithoutActivation = false;
        ApplyNoActivateStyle(enabled: false);

        _suppressAutoHide = true;
        try
        {
            if (IsHandleCreated)
                WindowActivator.TryForceForeground(Handle);
            else
            {
                WindowActivator.ClaimForegroundRights();
                Activate();
            }

            if (!Focused)
                Focus();
        }
        finally
        {
            BeginInvoke(() =>
            {
                _suppressAutoHide = false;
                if (Visible && !ContainsFocus && Form.ActiveForm != this)
                {
                    WindowActivator.TryForceForeground(Handle);
                    Focus();
                }
            });
        }

        // 松手瞬间清掉悬停高亮，避免中心松手后仍显示拖过的高亮
        if (_hoverItemIndex != -1 || _hoverTab != _activeTab || _hoverGroup != null)
        {
            _hoverItemIndex = -1;
            _hoverTab = _activeTab;
            _hoverGroup = null;
            InvalidateStaticLayer();
            RequestRender();
        }
    }

    protected override bool ShowWithoutActivation => _showWithoutActivation;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            if (_showWithoutActivation)
                cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RequestRender();
    }

    protected override void OnPaint(PaintEventArgs e) => RequestRender();

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!_gestureMode && CanScrollList)
        {
            if (e.KeyCode is Keys.Up or Keys.PageUp)
            {
                TryScrollList(e.KeyCode == Keys.PageUp ? -MaxVisibleItems : -1);
                e.Handled = true;
                return;
            }

            if (e.KeyCode is Keys.Down or Keys.PageDown)
            {
                TryScrollList(e.KeyCode == Keys.PageDown ? MaxVisibleItems : 1);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Home)
            {
                TryScrollList(-_scrollIndex);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.End)
            {
                TryScrollList(MaxScrollIndex - _scrollIndex);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void EnterGestureMode()
    {
        _gestureMode = true;
        _showWithoutActivation = true;
        _suppressAutoHide = false;
        ApplyNoActivateStyle(enabled: true);
    }

    private void ApplyNoActivateStyle(bool enabled)
    {
        if (!IsHandleCreated)
            return;

        var ex = GetWindowLong(Handle, GWL_EXSTYLE);
        var next = enabled
            ? ex | WS_EX_NOACTIVATE
            : ex & ~WS_EX_NOACTIVATE;
        if (next != ex)
            SetWindowLong(Handle, GWL_EXSTYLE, next);
    }

    private void OnInteractiveMouseMove(object? sender, MouseEventArgs e)
    {
        if (!Visible || _gestureMode)
            return;

        // 交互模式：仅高亮，不因悬停切换分类（点击扇区才切换）
        ApplyPointerAtClientPoint(e.Location, switchTabOnHover: false);
    }

    private void OnInteractiveMouseDown(object? sender, MouseEventArgs e)
    {
        if (!Visible || _gestureMode)
            return;
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
            return;

        HitTest(e.Location, out var tab, out var group, out var itemIndex);

        if (itemIndex >= 0 && itemIndex < _items.Count)
        {
            ExecutePayload(_items[itemIndex].Payload);
            Hide();
            return;
        }

        if (IsOverHub(e.Location))
            return;

        if (tab.HasValue)
        {
            var nextGroup = string.IsNullOrEmpty(group)
                ? EntryQueries.AllGroupsLabel
                : group;
            var tabChanged = tab != _activeTab;
            var groupChanged = !string.Equals(_activeGroup, nextGroup, StringComparison.OrdinalIgnoreCase);
            if (tabChanged || groupChanged)
            {
                _activeTab = tab;
                _hoverTab = tab;
                _activeGroup = nextGroup;
                _hoverGroup = group;
                _hoverItemIndex = -1;
                PersistLastView(tab.Value, _activeGroup);
                if (!IsAllGroups(nextGroup))
                    _configManager.TouchGroup(nextGroup);
                RebuildGroupSlots();
                InvalidateStaticLayer();
                LoadItemsForActiveTab(restartAnim: true);
                RequestRender();
            }

            return;
        }

        // 点在中心或当前扇区：保持打开；点在窗体透明区不会进到这里（分层窗体 hit-test 透明穿透）
    }

    private void ApplyPointerAtScreenPoint(Point screenPt, bool switchTabOnHover)
        => ApplyPointerAtClientPoint(PointToClient(screenPt), switchTabOnHover);

    private void ApplyPointerAtClientPoint(Point client, bool switchTabOnHover)
    {
        HitTest(client, out var tab, out var group, out var itemIndex);

        // 右侧列表：只高亮条目，绝不改标签/分组（否则移入列表会冲掉刚选中的分组）
        if (!_listPanelBounds.IsEmpty && _listPanelBounds.Contains(client))
        {
            var needRender = false;
            var listHoverGroup = IsAllGroups(_activeGroup) ? null : _activeGroup;
            if (_hoverTab != _activeTab
                || !string.Equals(_hoverGroup, listHoverGroup, StringComparison.OrdinalIgnoreCase))
            {
                _hoverTab = _activeTab;
                _hoverGroup = listHoverGroup;
                RebuildGroupSlots();
                InvalidateStaticLayer();
                needRender = true;
            }

            if (itemIndex != _hoverItemIndex)
            {
                _hoverItemIndex = itemIndex;
                needRender = true;
            }

            if (needRender)
                RequestRender();
            return;
        }

        var needRingRender = false;

        if (tab != _hoverTab || !string.Equals(_hoverGroup, group, StringComparison.OrdinalIgnoreCase))
        {
            _hoverTab = tab;
            _hoverGroup = group;
            RebuildGroupSlots();
            InvalidateStaticLayer();
            needRingRender = true;
        }

        if (switchTabOnHover && tab.HasValue)
        {
            // group==null 表示落在分类环（全部）；有值表示落在分组外环
            var nextGroup = string.IsNullOrEmpty(group)
                ? EntryQueries.AllGroupsLabel
                : group;
            var tabChanged = tab != _activeTab;
            var groupChanged = !string.Equals(_activeGroup, nextGroup, StringComparison.OrdinalIgnoreCase);
            if (tabChanged || groupChanged)
            {
                _activeTab = tab;
                _activeGroup = nextGroup;
                _hoverItemIndex = -1;
                PersistLastView(tab.Value, _activeGroup);
                RebuildGroupSlots();
                InvalidateStaticLayer();
                LoadItemsForActiveTab(restartAnim: true);
                needRingRender = true;
            }
            else if (itemIndex != _hoverItemIndex)
            {
                _hoverItemIndex = itemIndex;
                needRingRender = true;
            }
        }
        else if (itemIndex != _hoverItemIndex)
        {
            _hoverItemIndex = itemIndex;
            needRingRender = true;
        }

        if (needRingRender)
            RequestRender();
    }

    private static bool IsAllGroups(string? group)
        => string.IsNullOrWhiteSpace(group)
           || string.Equals(group, EntryQueries.AllGroupsLabel, StringComparison.OrdinalIgnoreCase);

    private bool IsOverHub(Point client)
    {
        var dx = client.X - _center.X;
        var dy = client.Y - _center.Y;
        // 略放宽容差：中心松手不必精确压在圆边内
        var r = _hubR + S(6);
        return dx * dx + dy * dy <= r * r;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            ClearItems(disposeOwned: true);
            _faviconService.Dispose();
            _segmentTitleFont.Dispose();
            _segmentCountFont.Dispose();
            _groupFont.Dispose();
            _itemFont.Dispose();
            _itemSubFont.Dispose();
            _hubTitleFont.Dispose();
            _hubCountFont.Dispose();
            _webPlaceholder?.Dispose();
            foreach (var img in _iconCache.Values)
                img.Dispose();
            _iconCache.Clear();
            DisposeLayerResources();
        }

        base.Dispose(disposing);
    }

    private void UpdateDpiScale()
    {
        _dpiScale = UiScaleHelper.GetDpi(this) / 96f;
        if (_dpiScale <= 0)
            _dpiScale = 1f;
    }

    private float S(float logical) => logical * _dpiScale;

    private void ApplyContentSize()
    {
        UpdateDpiScale();
        _hubR = S(HubRadiusLogical);
        _fanInnerR = S(FanInnerRadiusLogical);
        _fanOuterR = S(FanOuterRadiusLogical);

        // 有配置分组时预留外环宽度，保证右半环完整落在窗体内；无分组则不占位
        var groupRingLogical = HasAnyConfiguredGroups() ? FanGroupRingWidthLogical : 0f;
        _fanGroupOuterR = S(FanOuterRadiusLogical + groupRingLogical);
        _contentOuterR = _fanGroupOuterR;

        var listW = S(ListRowWidthLogical + ListPanelPadLogical * 2);
        var gap = S(ListGapFromFanLogical);
        var pad = S(FormPadLogical);
        var maxListH = MaxVisibleItems * S(ListRowHeightLogical)
            + Math.Max(0, MaxVisibleItems - 1) * S(ListRowGapLogical)
            + S(ListPanelPadLogical) * 2;

        // 中心靠左：pad + hub；内容外沿 = centerX + contentOuter；列表在外沿右侧
        // centerX = pad + hubR  →  总宽 = centerX + contentOuter + gap + listW + pad
        // 高度取半环直径与列表高度的较大者，保证右半环完整可见
        var centerX = pad + _hubR;
        var width = (int)Math.Ceiling(centerX + _contentOuterR + gap + listW + pad);
        var height = (int)Math.Ceiling(Math.Max(_contentOuterR * 2f + pad * 2, maxListH + pad * 2));
        Size = new Size(Math.Max(width, 480), Math.Max(height, 320));
    }

    /// <summary>是否存在任意可显示的配置分组（剪贴板/最近无分组）。</summary>
    private bool HasAnyConfiguredGroups()
    {
        foreach (var entry in _configManager.Config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Group))
                continue;
            if (entry.Type is EntryType.Folder or EntryType.File or EntryType.Url or EntryType.Text)
                return true;
        }

        return false;
    }

    private PointF ComputeWheelCenter()
    {
        // 中心圆靠左，扇区向右展开，右侧整块留给列表
        var pad = S(FormPadLogical);
        return new PointF(pad + _hubR, Height / 2f);
    }

    private void InvalidateStaticLayer()
    {
        _staticLayer?.Dispose();
        _staticLayer = null;
        _cachedActiveForStatic = null;
        _cachedHoverForStatic = null;
        _cachedActiveGroupForStatic = null;
        _cachedHoverGroupForStatic = null;
    }

    private void DisposeLayerResources()
    {
        InvalidateStaticLayer();

        if (_layerGfx != null)
        {
            _layerGfx.Dispose();
            _layerGfx = null;
        }

        if (_layerReady)
        {
            if (_memDc != IntPtr.Zero && _oldMemBmp != IntPtr.Zero)
                SelectObject(_memDc, _oldMemBmp);
            if (_hLayerBmp != IntPtr.Zero)
            {
                DeleteObject(_hLayerBmp);
                _hLayerBmp = IntPtr.Zero;
            }
            if (_memDc != IntPtr.Zero)
            {
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }
            if (_screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, _screenDc);
                _screenDc = IntPtr.Zero;
            }
            _oldMemBmp = IntPtr.Zero;
            _layerReady = false;
        }

        _layerBmp?.Dispose();
        _layerBmp = null;
    }

    private void EnsureLayerResources()
    {
        if (_layerReady && _layerBmp != null && _layerBmp.Width == Width && _layerBmp.Height == Height)
            return;

        DisposeLayerResources();
        if (Width <= 0 || Height <= 0)
            return;

        _layerBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        _layerGfx = Graphics.FromImage(_layerBmp);
        _layerGfx.CompositingMode = CompositingMode.SourceOver;

        _screenDc = GetDC(IntPtr.Zero);
        _memDc = CreateCompatibleDC(_screenDc);
        _hLayerBmp = _layerBmp.GetHbitmap(Color.FromArgb(0));
        _oldMemBmp = SelectObject(_memDc, _hLayerBmp);
        _layerReady = true;
    }

    private void SyncLayerBitmapToHbitmap()
    {
        // Bitmap 已画完：重建 HBITMAP（GDI+ 与 HBITMAP 不同步时必须重建）
        if (!_layerReady || _layerBmp == null)
            return;

        if (_memDc != IntPtr.Zero && _oldMemBmp != IntPtr.Zero)
            SelectObject(_memDc, _oldMemBmp);
        if (_hLayerBmp != IntPtr.Zero)
            DeleteObject(_hLayerBmp);

        _hLayerBmp = _layerBmp.GetHbitmap(Color.FromArgb(0));
        _oldMemBmp = SelectObject(_memDc, _hLayerBmp);
    }

    private void PushLayeredBitmap()
    {
        if (!_layerReady || !IsHandleCreated)
            return;

        var size = new SizeApi { Cx = Width, Cy = Height };
        var pointSource = new PointApi { X = 0, Y = 0 };
        var topPos = new PointApi { X = Left, Y = Top };
        var blend = new BlendFunction
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };

        UpdateLayeredWindow(Handle, _screenDc, ref topPos, ref size, _memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
    }

    private void RequestRender()
    {
        if (!IsHandleCreated)
            return;

        // 动画中由定时器直接渲染，避免 BeginInvoke 排队卡顿
        if (_animating)
            return;

        if (_renderPending)
            return;

        _renderPending = true;
        try
        {
            BeginInvoke(() =>
            {
                _renderPending = false;
                if (!IsDisposed && IsHandleCreated && Visible)
                    RenderLayered(fast: false);
            });
        }
        catch (InvalidOperationException)
        {
            _renderPending = false;
        }
    }

    private void RenderLayered(bool fast)
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
            return;

        EnsureLayerResources();
        if (_layerGfx == null || _layerBmp == null)
            return;

        var g = _layerGfx;
        g.Clear(Color.Transparent);

        if (fast)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Low;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }
        else
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        // 静态层：扇区 + 中心（分类/悬停变化时重建）
        EnsureStaticLayer(fast);
        if (_staticLayer != null)
            g.DrawImageUnscaled(_staticLayer, 0, 0);

        DrawListPanel(g);
        DrawItems(g);

        SyncLayerBitmapToHbitmap();
        PushLayeredBitmap();
    }

    private void EnsureStaticLayer(bool fast)
    {
        if (_staticLayer != null
            && _cachedActiveForStatic == _activeTab
            && _cachedHoverForStatic == _hoverTab
            && string.Equals(_cachedActiveGroupForStatic, _activeGroup, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_cachedHoverGroupForStatic, _hoverGroup, StringComparison.OrdinalIgnoreCase)
            && _staticLayer.Width == Width
            && _staticLayer.Height == Height)
        {
            return;
        }

        _staticLayer?.Dispose();
        _staticLayer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(_staticLayer);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = fast ? PixelOffsetMode.Default : PixelOffsetMode.HighQuality;
        g.CompositingQuality = fast ? CompositingQuality.HighSpeed : CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        DrawSegments(g);
        DrawGroupRing(g);
        DrawHub(g);

        _cachedActiveForStatic = _activeTab;
        _cachedHoverForStatic = _hoverTab;
        _cachedActiveGroupForStatic = _activeGroup;
        _cachedHoverGroupForStatic = _hoverGroup;
    }

    private void RebuildTabs()
    {
        _tabs.Clear();
        var historyEnabled = _configManager.Config.ClipboardHistory?.Enabled != false;

        // 右侧扇区自上而下 = 用户配置的标签顺序（MainPopupTabOrder，与经典面板共用）
        foreach (var kind in CreateTabOrder(_configManager.Config.MainPopupTabOrder))
        {
            if (kind == TabKind.ClipboardHistory && !historyEnabled)
                continue;

            _tabs.Add(new TabSlot
            {
                Kind = kind,
                Label = GetTabLabel(kind),
                Count = CountForTab(kind)
            });
        }

        RelayoutGeometry();
    }

    private static IEnumerable<TabKind> CreateTabOrder(IEnumerable<string>? savedOrder)
    {
        var included = new HashSet<TabKind>();
        foreach (var saved in savedOrder ?? [])
        {
            if (Enum.TryParse<TabKind>(saved, ignoreCase: true, out var kind)
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

    private int CountForTab(TabKind kind)
    {
        return kind switch
        {
            TabKind.Folders => _configManager.Config.Entries.Count(e => e.Type == EntryType.Folder),
            TabKind.Files => _configManager.Config.Entries.Count(e => e.Type == EntryType.File),
            TabKind.Urls => _configManager.Config.Entries.Count(e => e.Type == EntryType.Url),
            // +1：内置「今天日期」动态条目
            TabKind.Texts => _configManager.Config.Entries.Count(e => e.Type == EntryType.Text) + 1,
            TabKind.ClipboardHistory => _clipboardHistory?.GetItems().Count ?? 0,
            TabKind.RecentItems => WindowsRecentItemsService.GetItems().Count,
            _ => 0
        };
    }

    private void RelayoutGeometry()
    {
        _hubR = S(HubRadiusLogical);
        _fanInnerR = S(FanInnerRadiusLogical);
        _fanOuterR = S(FanOuterRadiusLogical);

        if (_tabs.Count == 0)
        {
            _groups.Clear();
            _fanGroupOuterR = _fanOuterR;
            _contentOuterR = _fanOuterR;
            return;
        }

        var n = _tabs.Count;
        var sweep = FanTotalSweepDeg / n;
        for (var i = 0; i < n; i++)
        {
            _tabs[i].StartDeg = FanStartDeg + i * sweep;
            _tabs[i].SweepDeg = sweep;
            _tabs[i].Count = CountForTab(_tabs[i].Kind);
        }

        RebuildGroupSlots();
        LayoutItemList();
    }

    /// <summary>
    /// 仅当「焦点标签」（悬停优先，否则当前选中）存在分组时生成外环。
    /// 外环不固定；分组在整个右半大扇形（180°）上均分，而非挤在该标签小扇区内。
    /// </summary>
    private void RebuildGroupSlots()
    {
        _groups.Clear();

        var focusTab = _hoverTab ?? _activeTab;
        if (!focusTab.HasValue || !TabSupportsGroups(focusTab.Value))
        {
            UpdateGroupRingRadius();
            return;
        }

        var typeEntries = GetTypeEntriesForTab(focusTab.Value);
        var names = EntryQueries.OrderedGroupNames(typeEntries);
        if (names.Count == 0)
        {
            UpdateGroupRingRadius();
            return;
        }

        // 当前焦点标签的全部分组，均分整个右半环
        var unit = FanTotalSweepDeg / names.Count;
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var count = typeEntries.Count(e =>
                string.Equals(EntryQueries.NormalizeGroupName(e.Group), name, StringComparison.OrdinalIgnoreCase));
            _groups.Add(new GroupSlot
            {
                ParentTab = focusTab.Value,
                Name = name,
                Count = count,
                StartDeg = FanStartDeg + i * unit,
                SweepDeg = unit
            });
        }

        UpdateGroupRingRadius();
    }

    /// <summary>有分组扇区时外环可点可选；列表侧始终按「是否配置过分组」预留宽度，避免跳动。</summary>
    private void UpdateGroupRingRadius()
    {
        _fanGroupOuterR = _groups.Count > 0
            ? S(FanOuterRadiusLogical + FanGroupRingWidthLogical)
            : _fanOuterR;
        _contentOuterR = HasAnyConfiguredGroups()
            ? S(FanOuterRadiusLogical + FanGroupRingWidthLogical)
            : _fanOuterR;
    }

    private static bool TabSupportsGroups(TabKind kind)
        => kind is TabKind.Folders or TabKind.Files or TabKind.Urls or TabKind.Texts;

    private List<QuickEntry> GetTypeEntriesForTab(TabKind kind)
    {
        var type = kind switch
        {
            TabKind.Folders => EntryType.Folder,
            TabKind.Files => EntryType.File,
            TabKind.Urls => EntryType.Url,
            TabKind.Texts => EntryType.Text,
            _ => EntryType.Folder
        };
        return EntryQueries.ByType(_configManager.Config.Entries, type);
    }

    /// <summary>列表固定在扇区更右侧；高度按最多 MaxVisibleItems 行，超出靠 _scrollIndex 滑动。</summary>
    private void LayoutItemList()
    {
        _listPanelBounds = RectangleF.Empty;
        _listClipBounds = RectangleF.Empty;
        if (_items.Count == 0)
        {
            _scrollIndex = 0;
            return;
        }

        var rowW = S(ListRowWidthLogical);
        var rowH = S(ListRowHeightLogical);
        var gap = S(ListRowGapLogical);
        var pad = S(ListPanelPadLogical);
        var n = _items.Count;
        var visible = Math.Min(n, MaxVisibleItems);
        _scrollIndex = ClampScrollIndex(_scrollIndex, n, visible);

        var totalH = visible * rowH + Math.Max(0, visible - 1) * gap;
        // 可滚动时右侧预留细滚动条
        var scrollGutter = n > MaxVisibleItems ? S(8) : 0f;
        var panelW = rowW + pad * 2 + scrollGutter;
        var panelH = totalH + pad * 2;

        var margin = S(8);
        // 内容最右点 = center.X + contentOuterR（含分组外环）；列表必须完全在其右侧
        var fanRight = _center.X + _contentOuterR;
        var listLeft = fanRight + S(ListGapFromFanLogical);

        // 若窗体宽度不够，优先保证不重叠：压缩不再左移盖住扇区
        if (listLeft + panelW > Width - margin)
        {
            // 宽度不足时仍保持 listLeft 不小于 fanRight+gap，必要时裁切行宽由窗体尺寸保证
            listLeft = Math.Min(listLeft, Width - panelW - margin);
            if (listLeft < fanRight + S(ListGapFromFanLogical))
                listLeft = fanRight + S(ListGapFromFanLogical);
        }

        // 垂直居中于窗体（比跟随扇区中点更稳，减少上下跳动）
        var listTop = (Height - panelH) / 2f;
        listTop = Math.Clamp(listTop, margin, Math.Max(margin, Height - panelH - margin));

        _listPanelBounds = new RectangleF(listLeft, listTop, panelW, panelH);
        _listClipBounds = new RectangleF(listLeft + pad, listTop + pad, rowW, totalH);

        for (var i = 0; i < n; i++)
        {
            var visRow = i - _scrollIndex;
            if (visRow < 0 || visRow >= visible)
            {
                _items[i].TargetBounds = RectangleF.Empty;
                _items[i].Stagger = 0f;
                continue;
            }

            _items[i].TargetBounds = new RectangleF(
                listLeft + pad,
                listTop + pad + visRow * (rowH + gap),
                rowW,
                rowH);
            // 更小错峰，动画更干脆（仅当前视口内）
            _items[i].Stagger = visRow * 0.02f;
        }
    }

    private static int ClampScrollIndex(int scroll, int itemCount, int visibleCount)
    {
        if (itemCount <= visibleCount)
            return 0;
        return Math.Clamp(scroll, 0, itemCount - visibleCount);
    }

    private bool CanScrollList => _items.Count > MaxVisibleItems;

    private int MaxScrollIndex => Math.Max(0, _items.Count - MaxVisibleItems);

    private bool TryScrollList(int deltaRows, Point? screenPtForHover = null)
    {
        if (!CanScrollList || deltaRows == 0)
            return false;

        var next = ClampScrollIndex(_scrollIndex + deltaRows, _items.Count, MaxVisibleItems);
        if (next == _scrollIndex)
            return false;

        _scrollIndex = next;
        LayoutItemList();

        // 滚动后按当前指针位置刷新高亮（手势/交互共用）
        if (screenPtForHover.HasValue)
            ApplyPointerAtScreenPoint(screenPtForHover.Value, switchTabOnHover: _gestureMode);
        else if (_hoverItemIndex >= 0
                 && (_hoverItemIndex < _scrollIndex || _hoverItemIndex >= _scrollIndex + MaxVisibleItems))
        {
            _hoverItemIndex = -1;
        }

        // 滚动不重跑入场动画
        foreach (var item in _items)
        {
            if (!item.TargetBounds.IsEmpty)
                item.AnimT = 1f;
        }

        RequestRender();
        return true;
    }

    private void OnListMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!Visible || _items.Count == 0 || !CanScrollList)
            return;

        // 仅当指针在列表区域（或整窗在交互模式）时滚动，避免误触
        var client = e.Location;
        var overList = !_listPanelBounds.IsEmpty && _listPanelBounds.Contains(client);
        if (!overList && _gestureMode)
            return;

        // WinForms：Delta>0 为向上滚 → 列表内容上移 → 减小首行下标
        var steps = Math.Max(1, Math.Abs(e.Delta) / 120);
        var deltaRows = e.Delta > 0 ? -steps : steps;
        TryScrollList(deltaRows, PointToScreen(client));
    }

    private void ClearItems(bool disposeOwned)
    {
        if (disposeOwned)
        {
            foreach (var item in _items)
            {
                if (item.OwnsIcon)
                    item.Icon?.Dispose();
            }
        }

        _items.Clear();
        _faviconInflight.Clear();
        _listPanelBounds = RectangleF.Empty;
        _listClipBounds = RectangleF.Empty;
        _scrollIndex = 0;
    }

    private void LoadItemsForActiveTab(bool restartAnim)
    {
        ClearItems(disposeOwned: true);
        _hoverItemIndex = -1;
        _scrollIndex = 0;

        if (!_activeTab.HasValue)
        {
            RequestRender();
            return;
        }

        // 刷新扇区计数；分组无效时回退「全部」再加载
        foreach (var tab in _tabs)
            tab.Count = CountForTab(tab.Kind);
        ReconcileActiveGroup();

        switch (_activeTab.Value)
        {
            case TabKind.ClipboardHistory:
                foreach (var hist in (_clipboardHistory?.GetItems() ?? []).Take(MaxLoadedItems))
                {
                    _items.Add(new ItemSlot
                    {
                        Title = hist.Preview(40),
                        Subtitle = FormatRelativeTime(hist.CopiedAt),
                        Payload = hist,
                        Icon = GetCachedGlyphIcon("clipboard"),
                        OwnsIcon = false
                    });
                }
                break;

            case TabKind.RecentItems:
                foreach (var recent in WindowsRecentItemsService.GetItems().Take(MaxLoadedItems))
                {
                    _items.Add(new ItemSlot
                    {
                        Title = Truncate(recent.Name, 32),
                        Subtitle = Truncate(recent.DisplayPath, 48),
                        Payload = recent,
                        Icon = ResolvePathIcon(recent.LaunchPath, recent.IsDirectory),
                        OwnsIcon = false
                    });
                }
                break;

            default:
                foreach (var entry in GetEntriesForTab(_activeTab.Value).Take(MaxLoadedItems))
                {
                    var slot = new ItemSlot
                    {
                        Title = Truncate(entry.Name, 32),
                        Subtitle = entry.Type == EntryType.Text
                            ? Truncate(entry.Path.Replace("\r\n", " ").Replace('\n', ' '), 48)
                            : Truncate(entry.Path, 48),
                        Payload = entry
                    };
                    AssignEntryIcon(slot, entry);
                    _items.Add(slot);
                }
                break;
        }

        LayoutItemList();
        InvalidateStaticLayer();

        if (restartAnim)
            StartFireworkAnim();
        else
        {
            foreach (var item in _items)
                item.AnimT = 1f;
            RequestRender();
        }
    }

    private void AssignEntryIcon(ItemSlot slot, QuickEntry entry)
    {
        switch (entry.Type)
        {
            case EntryType.Folder:
                slot.Icon = ResolvePathIcon(entry.Path, isDirectory: true);
                break;
            case EntryType.File:
                slot.Icon = ResolvePathIcon(entry.Path, isDirectory: false);
                break;
            case EntryType.Url:
                slot.Icon = _faviconService.TryGetMemoryCached(entry.Path)
                    ?? _faviconService.TryGetCached(entry.Path)
                    ?? GetWebPlaceholder();
                slot.OwnsIcon = false;
                QueueFavicon(slot, entry.Path);
                break;
            case EntryType.Text:
                slot.Icon = GetCachedGlyphIcon("text");
                break;
        }
    }

    private Image? ResolvePathIcon(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GetCachedGlyphIcon(isDirectory ? "folder" : "file");

        // 文件夹共用同一壳图标；普通扩展名共用；exe/lnk 等按完整路径
        string key;
        if (isDirectory)
        {
            key = "dir:";
        }
        else if (IconExtractor.NeedsPerFileIcon(path))
        {
            key = "file:" + path.ToLowerInvariant();
        }
        else
        {
            var ext = Path.GetExtension(path);
            key = "ext:" + (string.IsNullOrEmpty(ext) ? "<noext>" : ext.ToLowerInvariant());
        }

        if (_iconCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            // Icon 由 IconExtractor 全局缓存持有，不可 Dispose（using 会拆掉后续项的图标）
            var icon = IconExtractor.GetIcon(path, isDirectory, useLargeIcon: false);
            if (icon != null)
            {
                var bmp = icon.ToBitmap();
                _iconCache[key] = bmp;
                return bmp;
            }
        }
        catch
        {
            // fall through
        }

        return GetCachedGlyphIcon(isDirectory ? "folder" : "file");
    }

    private Image GetWebPlaceholder()
    {
        if (_webPlaceholder != null)
            return _webPlaceholder;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Quickstart.Resources.web-url.png");
            if (stream != null)
            {
                _webPlaceholder = Image.FromStream(stream);
                return _webPlaceholder;
            }
        }
        catch
        {
            // ignore
        }

        _webPlaceholder = CreateGlyphBitmap("\uE774", Accent);
        return _webPlaceholder;
    }

    private Image GetCachedGlyphIcon(string kind)
    {
        if (_iconCache.TryGetValue("glyph:" + kind, out var cached))
            return cached;

        var (glyph, color) = kind switch
        {
            "folder" => ("\uE8B7", Color.FromArgb(120, 130, 145)),
            "file" => ("\uE8A5", Color.FromArgb(120, 130, 145)),
            "text" => ("\uE8C1", Color.FromArgb(120, 130, 145)),
            "clipboard" => ("\uE77F", Color.FromArgb(120, 130, 145)),
            _ => ("\uE8A5", Color.FromArgb(120, 130, 145))
        };

        var bmp = CreateGlyphBitmap(glyph, color);
        _iconCache["glyph:" + kind] = bmp;
        return bmp;
    }

    private Bitmap CreateGlyphBitmap(string glyph, Color color)
    {
        var size = Math.Max(22, (int)S(22));
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        using var font = new Font("Segoe MDL2 Assets", size * 0.58f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), sf);
        return bmp;
    }

    private void QueueFavicon(ItemSlot slot, string url)
    {
        var host = FaviconService.GetHost(url);
        if (host == null)
            return;
        if (!_faviconInflight.Add(host))
            return;

        _ = LoadFaviconAsync(slot, url, host);
    }

    private async Task LoadFaviconAsync(ItemSlot slot, string url, string host)
    {
        try
        {
            var image = await _faviconService.GetFaviconAsync(url).ConfigureAwait(false);
            if (IsDisposed || !IsHandleCreated)
                return;

            void Apply()
            {
                if (IsDisposed || !_items.Contains(slot))
                    return;
                if (image != null)
                    slot.Icon = image;
                RequestRender();
            }

            try
            {
                if (IsHandleCreated)
                    BeginInvoke(Apply);
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _faviconInflight.Remove(host);
        }
    }

    private IEnumerable<QuickEntry> GetEntriesForTab(TabKind kind)
    {
        var typeEntries = GetTypeEntriesForTab(kind);
        var activeGroup = TabSupportsGroups(kind) ? _activeGroup : EntryQueries.AllGroupsLabel;

        IEnumerable<QuickEntry> scoped = string.Equals(activeGroup, EntryQueries.AllGroupsLabel, StringComparison.OrdinalIgnoreCase)
            ? typeEntries
            : typeEntries.Where(e =>
                string.Equals(EntryQueries.NormalizeGroupName(e.Group), activeGroup, StringComparison.OrdinalIgnoreCase));

        IEnumerable<QuickEntry> ordered = _configManager.Config.SortByRecentUsage
            ? scoped.OrderBy(e => e.SortOrder).ThenByDescending(e => e.LastUsedAt)
            : scoped.OrderBy(e => e.SortOrder);

        // 文本分类 +「全部」时置顶内置「今天日期」动态条目
        if (kind == TabKind.Texts
            && string.Equals(activeGroup, EntryQueries.AllGroupsLabel, StringComparison.OrdinalIgnoreCase))
        {
            ordered = ordered.Prepend(DynamicTextEntries.CreateTodayDateEntry());
        }

        return ordered;
    }

    private void ReconcileActiveGroup()
    {
        if (string.Equals(_activeGroup, EntryQueries.AllGroupsLabel, StringComparison.OrdinalIgnoreCase))
            return;
        if (!_activeTab.HasValue || !TabSupportsGroups(_activeTab.Value))
        {
            _activeGroup = EntryQueries.AllGroupsLabel;
            return;
        }

        var names = EntryQueries.OrderedGroupNames(GetTypeEntriesForTab(_activeTab.Value));
        if (names.All(n => !string.Equals(n, _activeGroup, StringComparison.OrdinalIgnoreCase)))
            _activeGroup = EntryQueries.AllGroupsLabel;
    }

    private void StartFireworkAnim()
    {
        _animStartTick = Environment.TickCount64;
        _animating = true;
        foreach (var item in _items)
        {
            // 仅视口内做入场动画；屏外项保持就绪，滚到时直接显示
            item.AnimT = item.TargetBounds.IsEmpty ? 1f : 0f;
        }

        // 先出一帧静态层，再进动画
        EnsureStaticLayer(fast: false);
        if (!_animTimer.Enabled)
            _animTimer.Start();
        RenderLayered(fast: true);
    }

    private void OnAnimTick()
    {
        if (!_animating)
        {
            _animTimer.Stop();
            return;
        }

        var elapsed = Environment.TickCount64 - _animStartTick;
        var allDone = true;
        var end = Math.Min(_items.Count, _scrollIndex + MaxVisibleItems);

        for (var i = _scrollIndex; i < end; i++)
        {
            var item = _items[i];
            if (item.TargetBounds.IsEmpty)
            {
                item.AnimT = 1f;
                continue;
            }

            var local = (elapsed / (float)AnimDurationMs) - item.Stagger;
            if (local < 0f)
            {
                item.AnimT = 0f;
                allDone = false;
                continue;
            }

            var t = Math.Clamp(local, 0f, 1f);
            // easeOutCubic：前快后稳
            item.AnimT = 1f - (1f - t) * (1f - t) * (1f - t);
            if (t < 1f)
                allDone = false;
        }

        // 定时器线程即 UI 线程：直接渲染，不走 BeginInvoke
        RenderLayered(fast: true);

        if (allDone)
        {
            _animating = false;
            _animTimer.Stop();
            foreach (var item in _items)
                item.AnimT = 1f;
            // 结束时高质量补一帧
            RenderLayered(fast: false);
        }
    }

    private void HitTest(Point client, out TabKind? tab, out string? group, out int itemIndex)
    {
        tab = null;
        group = null;
        itemIndex = -1;

        // 1) 列表（当前视口内可见行）
        // 返回当前标签 + 当前分组，避免外层把 group=null 误判为「全部」
        if (!_listPanelBounds.IsEmpty && _listPanelBounds.Contains(client))
        {
            var end = Math.Min(_items.Count, _scrollIndex + MaxVisibleItems);
            for (var i = _scrollIndex; i < end; i++)
            {
                var item = _items[i];
                if (item.TargetBounds.IsEmpty || item.AnimT < 0.28f)
                    continue;
                if (GetAnimatedItemBounds(item).Contains(client))
                {
                    itemIndex = i;
                    tab = _activeTab;
                    group = IsAllGroups(_activeGroup) ? null : _activeGroup;
                    return;
                }
            }

            // 在列表面板上但未命中具体行：保持当前分类与分组
            tab = _activeTab;
            group = IsAllGroups(_activeGroup) ? null : _activeGroup;
            return;
        }

        var dx = client.X - _center.X;
        var dy = client.Y - _center.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        var deg = MathF.Atan2(dy, dx) * 180f / MathF.PI;

        // 2) 分组外环：仅当前焦点标签有分组时存在，且占满整个右半环
        if (_groups.Count > 0
            && dist >= _fanOuterR - S(1)
            && dist <= _fanGroupOuterR + S(4))
        {
            foreach (var slot in _groups)
            {
                if (AngleInSweep(deg, slot.StartDeg, slot.SweepDeg))
                {
                    tab = slot.ParentTab;
                    group = slot.Name;
                    return;
                }
            }
        }

        // 3) 分类环（中环）
        if (dist >= _fanInnerR - S(2) && dist <= _fanOuterR + S(4))
        {
            foreach (var slot in _tabs)
            {
                if (AngleInSweep(deg, slot.StartDeg, slot.SweepDeg))
                {
                    tab = slot.Kind;
                    // group 保持 null → 表示「该标签全部」，外环若存在可再细选
                    return;
                }
            }
        }

        // 4) 中心圆：保持当前分类与分组
        if (dist <= _hubR)
        {
            tab = _activeTab;
            group = IsAllGroups(_activeGroup) ? null : _activeGroup;
        }
    }

    private static bool AngleInSweep(float deg, float start, float sweep)
    {
        // 归一化到 [-180, 180)
        static float Norm(float a)
        {
            while (a < -180f) a += 360f;
            while (a >= 180f) a -= 360f;
            return a;
        }

        var d = Norm(deg);
        var s = Norm(start);
        var e = Norm(start + sweep);

        if (sweep >= 359.9f)
            return true;

        // start→end 顺时针扫过（GDI 正方向）
        // 在屏幕 atan2 中，顺时针：角度增加
        if (s <= e)
            return d >= s && d <= e;

        // 跨越 ±180
        return d >= s || d <= e;
    }

    private RectangleF GetAnimatedItemBounds(ItemSlot item)
    {
        var target = item.TargetBounds;
        if (target.IsEmpty)
            return RectangleF.Empty;

        var t = Math.Clamp(item.AnimT, 0f, 1f);
        if (t >= 0.999f)
            return target;

        // 轻量：从左侧（扇区外缘）水平滑入，不缩放，减少每帧几何计算
        var slide = S(28f) * (1f - t);
        return new RectangleF(target.X - slide, target.Y, target.Width, target.Height);
    }

    private void DrawSegments(Graphics g)
    {
        if (_tabs.Count == 0)
            return;

        // 外环底：整块浅底 + 细描边，平面分割
        using (var fanPath = CreateDonutSegmentPath(_center, _fanInnerR, _fanOuterR, FanStartDeg, FanTotalSweepDeg))
        {
            using (var brush = new SolidBrush(SegmentFill))
                g.FillPath(brush, fanPath);
            using (var pen = new Pen(SegmentBorder, Math.Max(1f, S(1.1f))))
                g.DrawPath(pen, fanPath);
        }

        foreach (var slot in _tabs)
        {
            var active = slot.Kind == _activeTab;
            var hot = slot.Kind == _hoverTab && !active;
            using var path = CreateDonutSegmentPath(_center, _fanInnerR, _fanOuterR, slot.StartDeg, slot.SweepDeg);

            if (active || hot)
            {
                using var brush = new SolidBrush(active ? AccentSoft : Color.FromArgb(245, 249, 255));
                g.FillPath(brush, path);
            }

            // 扇区分割线（平面 UI：细线而非描边块）
            DrawSegmentDivider(g, slot.StartDeg);
            if (slot == _tabs[^1])
                DrawSegmentDivider(g, slot.StartDeg + slot.SweepDeg);

            if (active)
            {
                // 外弧强调条：贴在分类环外缘内侧（分组环在更外侧时仍清晰）
                const float degPad = 0.6f;
                using var outerAccent = CreateDonutSegmentPath(
                    _center,
                    _fanOuterR - S(3.2f),
                    _fanOuterR - S(0.4f),
                    slot.StartDeg + degPad,
                    Math.Max(1f, slot.SweepDeg - degPad * 2f));
                using var accentBrush = new SolidBrush(Accent);
                g.FillPath(accentBrush, outerAccent);
            }

            var midR = (_fanInnerR + _fanOuterR) / 2f;
            var rad = slot.MidDeg * MathF.PI / 180f;
            var tx = _center.X + MathF.Cos(rad) * midR;
            var ty = _center.Y + MathF.Sin(rad) * midR;

            var titleColor = active || hot ? SegmentTextHot : SegmentText;
            var countColor = active || hot ? Color.FromArgb(70, 118, 180) : SegmentCount;

            var title = slot.Label;
            var count = $"{slot.Count} 项";
            var titleSize = g.MeasureString(title, _segmentTitleFont);
            var countSize = g.MeasureString(count, _segmentCountFont);

            var blockH = titleSize.Height + countSize.Height - S(2);
            var titlePos = new PointF(tx - titleSize.Width / 2f, ty - blockH / 2f);
            var countPos = new PointF(tx - countSize.Width / 2f, titlePos.Y + titleSize.Height - S(2));

            using (var b = new SolidBrush(titleColor))
                g.DrawString(title, _segmentTitleFont, b, titlePos);
            using (var b = new SolidBrush(countColor))
                g.DrawString(count, _segmentCountFont, b, countPos);
        }
    }

    /// <summary>
    /// 分类环外侧窄环：仅焦点标签有分组时绘制；分组均分整个右半大扇形，名称按弧长截断。
    /// </summary>
    private void DrawGroupRing(Graphics g)
    {
        if (_groups.Count == 0 || _fanGroupOuterR <= _fanOuterR + 0.5f)
            return;

        var ringInner = _fanOuterR + S(0.6f);
        var ringOuter = _fanGroupOuterR;

        // 整半环底
        using (var basePath = CreateDonutSegmentPath(_center, ringInner, ringOuter, FanStartDeg, FanTotalSweepDeg))
        {
            using (var brush = new SolidBrush(GroupRingFill))
                g.FillPath(brush, basePath);
            using (var pen = new Pen(GroupRingBorder, Math.Max(1f, S(1f))))
                g.DrawPath(pen, basePath);
        }

        foreach (var slot in _groups)
        {
            var active = slot.ParentTab == _activeTab
                && string.Equals(slot.Name, _activeGroup, StringComparison.OrdinalIgnoreCase);
            var hot = string.Equals(slot.Name, _hoverGroup, StringComparison.OrdinalIgnoreCase)
                && !active;

            if (active || hot)
            {
                using var path = CreateDonutSegmentPath(_center, ringInner, ringOuter, slot.StartDeg, slot.SweepDeg);
                using var brush = new SolidBrush(active ? AccentSoft : Color.FromArgb(245, 249, 255));
                g.FillPath(brush, path);
            }

            DrawGroupDivider(g, ringInner, ringOuter, slot.StartDeg);
            if (slot == _groups[^1])
                DrawGroupDivider(g, ringInner, ringOuter, slot.StartDeg + slot.SweepDeg);

            if (active)
            {
                const float degPad = 0.5f;
                using var outerAccent = CreateDonutSegmentPath(
                    _center,
                    ringOuter - S(2.4f),
                    ringOuter - S(0.3f),
                    slot.StartDeg + degPad,
                    Math.Max(1f, slot.SweepDeg - degPad * 2f));
                using var accentBrush = new SolidBrush(Accent);
                g.FillPath(accentBrush, outerAccent);
            }

            DrawGroupLabel(g, slot, ringInner, ringOuter, active || hot);
        }
    }

    private void DrawGroupDivider(Graphics g, float innerR, float outerR, float deg)
    {
        var rad = deg * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var x1 = _center.X + cos * (innerR + S(0.5f));
        var y1 = _center.Y + sin * (innerR + S(0.5f));
        var x2 = _center.X + cos * (outerR - S(0.5f));
        var y2 = _center.Y + sin * (outerR - S(0.5f));
        using var pen = new Pen(SegmentDivider, Math.Max(1f, S(0.9f)));
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    private void DrawGroupLabel(Graphics g, GroupSlot slot, float innerR, float outerR, bool emphasize)
    {
        var midR = (innerR + outerR) / 2f;
        var rad = slot.MidDeg * MathF.PI / 180f;
        var tx = _center.X + MathF.Cos(rad) * midR;
        var ty = _center.Y + MathF.Sin(rad) * midR;

        // 按弧长自动分配可用宽度，过长则省略号
        var arcLen = midR * (slot.SweepDeg * MathF.PI / 180f);
        var maxW = Math.Max(S(10f), arcLen - S(4f));
        var color = emphasize ? GroupTextHot : GroupText;
        var text = FitTextToWidth(g, slot.Name, _groupFont, maxW);

        var size = g.MeasureString(text, _groupFont);
        var pos = new PointF(tx - size.Width / 2f, ty - size.Height / 2f);
        using var brush = new SolidBrush(color);
        g.DrawString(text, _groupFont, brush, pos);
    }

    private static string FitTextToWidth(Graphics g, string text, Font font, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (g.MeasureString(text, font).Width <= maxWidth)
            return text;

        const string ellipsis = "…";
        var ellipsisW = g.MeasureString(ellipsis, font).Width;
        if (ellipsisW >= maxWidth)
            return ellipsis;

        for (var len = text.Length - 1; len >= 1; len--)
        {
            var candidate = text[..len] + ellipsis;
            if (g.MeasureString(candidate, font).Width <= maxWidth)
                return candidate;
        }

        return ellipsis;
    }

    private void DrawSegmentDivider(Graphics g, float deg)
    {
        var rad = deg * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var x1 = _center.X + cos * (_fanInnerR + S(1));
        var y1 = _center.Y + sin * (_fanInnerR + S(1));
        var x2 = _center.X + cos * (_fanOuterR - S(1));
        var y2 = _center.Y + sin * (_fanOuterR - S(1));
        using var pen = new Pen(SegmentDivider, Math.Max(1f, S(1f)));
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    private void DrawHub(Graphics g)
    {
        var rect = new RectangleF(_center.X - _hubR, _center.Y - _hubR, _hubR * 2, _hubR * 2);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var brush = new SolidBrush(HubFill);
            g.FillPath(brush, path);

            // 内圈细线 + 外圈强调环：扁平层次
            using var borderPen = new Pen(SegmentBorder, Math.Max(1f, S(1.15f)));
            g.DrawPath(borderPen, path);

            var ringInset = S(3.5f);
            var ringRect = RectangleF.Inflate(rect, -ringInset, -ringInset);
            using var ringPen = new Pen(AccentMuted, Math.Max(1.2f, S(1.5f)));
            g.DrawEllipse(ringPen, ringRect);
        }

        // 顶部小点：当前分类指示
        if (_activeTab.HasValue)
        {
            var dotR = S(3.2f);
            var dotCenter = new PointF(_center.X, rect.Y + S(11));
            using var dotBrush = new SolidBrush(HubRing);
            g.FillEllipse(dotBrush, dotCenter.X - dotR, dotCenter.Y - dotR, dotR * 2, dotR * 2);
        }

        var title = _activeTab.HasValue ? GetTabLabel(_activeTab.Value) : "Quickstart";
        var count = _activeTab.HasValue
            ? $"{_tabs.FirstOrDefault(t => t.Kind == _activeTab.Value)?.Count ?? 0} 项"
            : "";

        using var titleBrush = new SolidBrush(SegmentText);
        using var countBrush = new SolidBrush(SegmentCount);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        if (string.IsNullOrEmpty(count))
        {
            g.DrawString(title, _hubTitleFont, titleBrush, rect, sf);
        }
        else
        {
            var titleRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.30f, rect.Width, rect.Height * 0.32f);
            var countRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.56f, rect.Width, rect.Height * 0.24f);
            g.DrawString(title, _hubTitleFont, titleBrush, titleRect, sf);
            g.DrawString(count, _hubCountFont, countBrush, countRect, sf);
        }
    }

    private void DrawListPanel(Graphics g)
    {
        if (_listPanelBounds.IsEmpty || _items.Count == 0)
            return;

        var maxT = 0f;
        var end = Math.Min(_items.Count, _scrollIndex + MaxVisibleItems);
        for (var i = _scrollIndex; i < end; i++)
            if (_items[i].AnimT > maxT) maxT = _items[i].AnimT;
        if (maxT < 0.04f)
            return;

        var alpha = (int)(Math.Clamp(maxT, 0f, 1f) * 255);

        using var path = CreateRoundRect(_listPanelBounds, S(12));
        using (var brush = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), ListPanelFill)))
            g.FillPath(brush, path);
        using (var pen = new Pen(Color.FromArgb(Math.Min(255, alpha), ListPanelBorder), Math.Max(1f, S(1f))))
            g.DrawPath(pen, path);

        if (CanScrollList)
            DrawListScrollbar(g, alpha);
    }

    private void DrawListScrollbar(Graphics g, int alpha)
    {
        if (_listClipBounds.IsEmpty || _items.Count <= MaxVisibleItems)
            return;

        var trackW = S(3.5f);
        var trackPad = S(3f);
        var trackX = _listPanelBounds.Right - trackPad - trackW;
        var trackY = _listClipBounds.Y;
        var trackH = _listClipBounds.Height;
        var track = new RectangleF(trackX, trackY, trackW, trackH);

        using (var brush = new SolidBrush(Color.FromArgb(Math.Min(200, alpha), ScrollTrack)))
        using (var path = CreateRoundRect(track, trackW / 2f))
            g.FillPath(brush, path);

        var visibleRatio = MaxVisibleItems / (float)_items.Count;
        var thumbH = Math.Max(S(18f), trackH * visibleRatio);
        var maxScroll = MaxScrollIndex;
        var thumbT = maxScroll <= 0 ? 0f : _scrollIndex / (float)maxScroll;
        var thumbY = trackY + (trackH - thumbH) * thumbT;
        var thumb = new RectangleF(trackX, thumbY, trackW, thumbH);

        using (var brush = new SolidBrush(Color.FromArgb(Math.Min(230, alpha), ScrollThumb)))
        using (var path = CreateRoundRect(thumb, trackW / 2f))
            g.FillPath(brush, path);
    }

    private void DrawItems(Graphics g)
    {
        if (_items.Count == 0)
        {
            if (_activeTab.HasValue)
            {
                using var brush = new SolidBrush(HintColor);
                var msg = "暂无项目";
                var size = g.MeasureString(msg, _itemFont);
                var x = _center.X + _contentOuterR + S(18);
                g.DrawString(msg, _itemFont, brush, x, _center.Y - size.Height / 2f);
            }
            return;
        }

        var iconSize = S(IconLogical);
        var corner = S(10f);
        var accentBarW = S(3f);
        var visStart = _scrollIndex;
        var visEnd = Math.Min(_items.Count, _scrollIndex + MaxVisibleItems);

        // 单遍绘制；高亮项最后画
        var hotIndex = _hoverItemIndex;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = visStart; i < visEnd; i++)
            {
                var hot = i == hotIndex;
                if (pass == 0 && hot) continue;
                if (pass == 1 && !hot) continue;

                var item = _items[i];
                if (item.TargetBounds.IsEmpty || item.AnimT <= 0.02f)
                    continue;

                var bounds = GetAnimatedItemBounds(item);
                if (bounds.IsEmpty)
                    continue;
                var alpha = (int)(Math.Clamp(item.AnimT, 0f, 1f) * 255);

                using (var path = CreateRoundRect(bounds, Math.Min(corner, bounds.Height / 2f)))
                using (var brush = new SolidBrush(Color.FromArgb(alpha, hot ? RowFillHot : RowFill)))
                using (var pen = new Pen(
                    Color.FromArgb(Math.Min(255, alpha), hot ? RowBorderHot : RowBorder),
                    hot ? Math.Max(1.2f, S(1.25f)) : Math.Max(1f, S(1f))))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                // 高亮左侧色条：平面强调
                if (hot && item.AnimT > 0.4f)
                {
                    var bar = new RectangleF(bounds.X + S(1), bounds.Y + S(8), accentBarW, bounds.Height - S(16));
                    using var barPath = CreateRoundRect(bar, accentBarW / 2f);
                    using var barBrush = new SolidBrush(Color.FromArgb(alpha, Accent));
                    g.FillPath(barBrush, barPath);
                }

                if (item.AnimT < 0.2f)
                    continue;

                var contentLeft = bounds.X + S(hot ? 12 : 10);
                if (item.Icon != null)
                {
                    var ix = contentLeft;
                    var iy = bounds.Y + (bounds.Height - iconSize) / 2f;
                    try
                    {
                        // 动画中不做 ColorMatrix，直接贴图更快
                        g.DrawImage(item.Icon, ix, iy, iconSize, iconSize);
                    }
                    catch
                    {
                        // ignore
                    }
                    contentLeft = ix + iconSize + S(8);
                }

                var textWidth = Math.Max(8f, bounds.Right - contentLeft - S(10));
                var titleRect = new RectangleF(contentLeft, bounds.Y + S(4), textWidth, bounds.Height * 0.48f);
                using (var brush = new SolidBrush(Color.FromArgb(alpha, TitleColor)))
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                    g.DrawString(item.Title, _itemFont, brush, titleRect, sf);

                if (!string.IsNullOrEmpty(item.Subtitle) && item.AnimT > 0.35f)
                {
                    var subRect = new RectangleF(contentLeft, bounds.Y + bounds.Height * 0.48f, textWidth, bounds.Height * 0.42f);
                    using var brush = new SolidBrush(Color.FromArgb(alpha, SubColor));
                    using var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    g.DrawString(item.Subtitle, _itemSubFont, brush, subRect, sf);
                }
            }
        }
    }

    /// <summary>环形扇区路径（GDI+ 角度：0=右，顺时针）。</summary>
    private static GraphicsPath CreateDonutSegmentPath(PointF center, float innerR, float outerR, float startDeg, float sweepDeg)
    {
        var path = new GraphicsPath();
        if (sweepDeg <= 0.01f || outerR <= innerR)
            return path;

        var outer = new RectangleF(center.X - outerR, center.Y - outerR, outerR * 2, outerR * 2);
        var inner = new RectangleF(center.X - innerR, center.Y - innerR, innerR * 2, innerR * 2);

        path.AddArc(outer, startDeg, sweepDeg);
        path.AddArc(inner, startDeg + sweepDeg, -sweepDeg);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateRoundRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        var arc = new RectangleF(rect.X, rect.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ExecutePayload(object? payload)
    {
        switch (payload)
        {
            case QuickEntry entry:
                ExecuteEntry(entry);
                break;
            case ClipboardHistoryItem hist:
                ExecuteHistory(hist);
                break;
            case WindowsRecentItem recent:
                ExecuteRecent(recent);
                break;
        }
    }

    private void ExecuteEntry(QuickEntry entry)
    {
        if (entry.Type == EntryType.Text)
        {
            if (!DynamicTextEntries.IsDynamic(entry))
                _configManager.TouchEntry(entry.Id);
            DeliverText(
                DynamicTextEntries.ResolveContent(entry),
                _configManager.Config.TextEntryAction,
                fromHistory: false);
            return;
        }

        WindowActivator.AllowAnyForeground();
        try
        {
            if (entry.Type == EntryType.Url)
            {
                _configManager.TouchEntry(entry.Id);
                ProcessLauncher.OpenUrl(entry.Path);
            }
            else
            {
                _launcher.Open(entry);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void ExecuteHistory(ClipboardHistoryItem item)
    {
        if (_clipboardHistory == null || string.IsNullOrEmpty(item.Text))
            return;
        DeliverText(item.Text, _configManager.Config.ClipboardHistoryAction, fromHistory: true);
    }

    private void DeliverText(string text, TextDeliveryAction action, bool fromHistory)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var canPaste = action == TextDeliveryAction.PasteAtCursor
            && PlainTextPasteService.IsValidTargetWindow(_deliveryTargetWindow)
            && _deliveryTargetWindow != Handle;

        if (canPaste)
        {
            var target = _deliveryTargetWindow;
            _ = PasteTextToTargetAsync(text, target, fromHistory);
            return;
        }

        _ = CopyHistoryAsync(text);
    }

    private async Task PasteTextToTargetAsync(string text, IntPtr targetWindow, bool fromHistory)
    {
        try
        {
            if (fromHistory && _clipboardHistory != null)
                await _clipboardHistory.CopyPlainTextAsync(text);

            await PlainTextPasteService.PasteTextAsync(text, targetWindow);
        }
        catch
        {
            try
            {
                await CopyHistoryAsync(text);
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task CopyHistoryAsync(string text)
    {
        try
        {
            if (_clipboardHistory != null)
                await _clipboardHistory.CopyPlainTextAsync(text);
            else if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }
        catch
        {
            // ignore
        }
    }

    private static void ExecuteRecent(WindowsRecentItem item)
    {
        try
        {
            WindowActivator.AllowAnyForeground();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.LaunchPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private TabKind ResolveInitialTab()
    {
        if (_configManager.Config.RememberLastView
            && Enum.TryParse<TabKind>(_configManager.Config.LastViewTab, ignoreCase: true, out var saved)
            && _tabs.Any(t => t.Kind == saved))
        {
            return saved;
        }

        // 默认使用用户排序后的第一个标签
        return _tabs.Count > 0 ? _tabs[0].Kind : TabKind.Folders;
    }

    private string ResolveInitialGroup(TabKind? tab)
    {
        if (!tab.HasValue || !TabSupportsGroups(tab.Value))
            return EntryQueries.AllGroupsLabel;

        if (!_configManager.Config.RememberLastView)
            return EntryQueries.AllGroupsLabel;

        var saved = string.IsNullOrWhiteSpace(_configManager.Config.LastViewGroup)
            ? EntryQueries.AllGroupsLabel
            : _configManager.Config.LastViewGroup.Trim();
        if (string.Equals(saved, EntryQueries.AllGroupsLabel, StringComparison.OrdinalIgnoreCase))
            return EntryQueries.AllGroupsLabel;

        var names = EntryQueries.OrderedGroupNames(GetTypeEntriesForTab(tab.Value));
        return names.Any(n => string.Equals(n, saved, StringComparison.OrdinalIgnoreCase))
            ? saved
            : EntryQueries.AllGroupsLabel;
    }

    private void PersistLastView(TabKind kind, string group)
    {
        if (!_configManager.Config.RememberLastView)
            return;

        var normalizedGroup = string.IsNullOrWhiteSpace(group)
            ? EntryQueries.AllGroupsLabel
            : group.Trim();
        _configManager.SetLastView(kind.ToString(), normalizedGroup);
    }

    private static string GetTabLabel(TabKind kind)
        => kind switch
        {
            TabKind.Folders => "文件夹",
            TabKind.Files => "文件",
            TabKind.Urls => "网页",
            TabKind.Texts => "文本",
            TabKind.ClipboardHistory => "剪贴板",
            TabKind.RecentItems => "最近",
            _ => kind.ToString()
        };

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var one = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (one.Length <= maxChars)
            return one;
        return one[..maxChars] + "…";
    }

    private static string FormatRelativeTime(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} 天前";
        return time.ToString("MM-dd HH:mm");
    }

    #region Layered window interop

    [StructLayout(LayoutKind.Sequential)]
    private struct PointApi
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeApi
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref PointApi pptDst,
        ref SizeApi psize,
        IntPtr hdcSrc,
        ref PointApi pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    #endregion
}
