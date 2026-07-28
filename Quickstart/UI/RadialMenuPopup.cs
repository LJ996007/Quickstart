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
/// 中心圆 + 右侧扇形分类环（参考图镜像），再往右横向列表；
/// 手势持续右移：扇区 → 列表项 → 松手执行。
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

    private static readonly Color HubFill = Color.FromArgb(255, 255, 255);
    private static readonly Color Accent = Color.FromArgb(91, 155, 230);
    private static readonly Color AccentSoft = Color.FromArgb(210, 230, 252);
    private static readonly Color SegmentFill = Color.FromArgb(255, 255, 255);
    private static readonly Color SegmentBorder = Color.FromArgb(228, 232, 238);
    private static readonly Color SegmentText = Color.FromArgb(48, 52, 58);
    private static readonly Color SegmentCount = Color.FromArgb(120, 126, 136);
    private static readonly Color SegmentTextHot = Color.FromArgb(36, 90, 160);
    private static readonly Color RowFill = Color.FromArgb(255, 255, 255);
    private static readonly Color RowFillHot = Color.FromArgb(245, 249, 255);
    private static readonly Color RowBorder = Color.FromArgb(226, 230, 236);
    private static readonly Color RowBorderHot = Color.FromArgb(55, 138, 221);
    private static readonly Color TitleColor = Color.FromArgb(32, 33, 36);
    private static readonly Color SubColor = Color.FromArgb(138, 143, 152);
    private static readonly Color HintColor = Color.FromArgb(150, 155, 162);
    private static readonly Color ListPanelFill = Color.FromArgb(248, 250, 252);
    private static readonly Color ListPanelBorder = Color.FromArgb(220, 225, 232);

    private const int MaxOuterItems = 8;
    // 扇区缩小，为右侧列表让出空间，避免互相遮挡
    private const float HubRadiusLogical = 40f;
    private const float FanInnerRadiusLogical = 48f;
    private const float FanOuterRadiusLogical = 108f;
    // 右侧扇区：从上(-90°)到下(+90°)，覆盖右半环，便于右滑连贯
    private const float FanStartDeg = -90f;
    private const float FanTotalSweepDeg = 180f;
    private const float ListGapFromFanLogical = 14f;
    private const float ListRowWidthLogical = 260f;
    private const float ListRowHeightLogical = 46f;
    private const float ListRowGapLogical = 6f;
    private const float ListPanelPadLogical = 8f;
    private const float IconLogical = 20f;
    private const float FormPadLogical = 12f;
    private const int AnimDurationMs = 180;
    private const int AnimFrameMs = 12;

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
    private readonly List<ItemSlot> _items = [];
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _faviconInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _segmentTitleFont;
    private readonly Font _segmentCountFont;
    private readonly Font _itemFont;
    private readonly Font _itemSubFont;
    private readonly Font _hubTitleFont;
    private readonly Font _hubCountFont;

    private Image? _webPlaceholder;
    private TabKind? _activeTab;
    private TabKind? _hoverTab;
    private int _hoverItemIndex = -1;
    private long _animStartTick;
    private bool _animating;
    private PointF _center;
    private float _dpiScale = 1f;
    private float _hubR;
    private float _fanInnerR;
    private float _fanOuterR;
    private RectangleF _listPanelBounds;
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
    private Bitmap? _staticLayer; // 扇区+中心缓存，动画时只重绘列表

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
    }

    public void ShowAtGesturePoint(Point screenPt)
    {
        UpdateDpiScale();
        ApplyContentSize();
        _center = ComputeWheelCenter();

        RebuildTabs();
        _activeTab = ResolveInitialTab();
        _hoverTab = _activeTab;
        _hoverItemIndex = -1;
        if (_activeTab.HasValue)
            PersistLastViewTab(_activeTab.Value);
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

    public void HighlightAtScreenPoint(Point screenPt)
    {
        if (!Visible)
            return;

        var client = PointToClient(screenPt);
        HitTest(client, out var tab, out var itemIndex);

        var needRender = false;

        if (tab != _hoverTab)
        {
            _hoverTab = tab;
            InvalidateStaticLayer();
            needRender = true;
        }

        // 右滑进入扇区即切换分类（连贯）
        if (tab.HasValue && tab != _activeTab)
        {
            _activeTab = tab;
            _hoverItemIndex = -1;
            PersistLastViewTab(tab.Value);
            InvalidateStaticLayer();
            LoadItemsForActiveTab(restartAnim: true);
            needRender = true;
        }
        else if (itemIndex != _hoverItemIndex)
        {
            _hoverItemIndex = itemIndex;
            needRender = true;
        }

        if (needRender)
            RequestRender();
    }

    public bool TryReleaseAtScreenPoint(Point screenPt)
    {
        if (!Visible)
            return false;

        var client = PointToClient(screenPt);
        HitTest(client, out _, out var itemIndex);

        if (itemIndex >= 0 && itemIndex < _items.Count)
        {
            ExecutePayload(_items[itemIndex].Payload);
            Hide();
            return true;
        }

        Hide();
        return false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
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
            return;
        }

        base.OnKeyDown(e);
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

        var listW = S(ListRowWidthLogical + ListPanelPadLogical * 2);
        var gap = S(ListGapFromFanLogical);
        var pad = S(FormPadLogical);
        var maxListH = MaxOuterItems * S(ListRowHeightLogical)
            + Math.Max(0, MaxOuterItems - 1) * S(ListRowGapLogical)
            + S(ListPanelPadLogical) * 2;

        // 中心靠左：pad + hub；扇区外沿 = centerX + fanOuter；列表在外沿右侧
        // centerX = pad + hubR  →  总宽 = centerX + fanOuter + gap + listW + pad
        var centerX = pad + _hubR;
        var width = (int)Math.Ceiling(centerX + _fanOuterR + gap + listW + pad);
        var height = (int)Math.Ceiling(Math.Max(_fanOuterR * 2f + pad * 2, maxListH + pad * 2));
        Size = new Size(Math.Max(width, 480), Math.Max(height, 320));
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

        DrawListPanel(g, drawShadow: !fast);
        DrawItems(g, drawShadow: !fast);

        SyncLayerBitmapToHbitmap();
        PushLayeredBitmap();
    }

    private void EnsureStaticLayer(bool fast)
    {
        if (_staticLayer != null
            && _cachedActiveForStatic == _activeTab
            && _cachedHoverForStatic == _hoverTab
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

        DrawSegments(g, drawShadow: !fast);
        DrawHub(g, drawShadow: !fast);

        _cachedActiveForStatic = _activeTab;
        _cachedHoverForStatic = _hoverTab;
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
            TabKind.Texts => _configManager.Config.Entries.Count(e => e.Type == EntryType.Text),
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
            return;

        var n = _tabs.Count;
        var sweep = FanTotalSweepDeg / n;
        for (var i = 0; i < n; i++)
        {
            _tabs[i].StartDeg = FanStartDeg + i * sweep;
            _tabs[i].SweepDeg = sweep;
            _tabs[i].Count = CountForTab(_tabs[i].Kind);
        }

        LayoutItemList();
    }

    /// <summary>列表固定在扇区更右侧，与扇区硬分隔，绝不遮挡。</summary>
    private void LayoutItemList()
    {
        _listPanelBounds = RectangleF.Empty;
        if (_items.Count == 0)
            return;

        var rowW = S(ListRowWidthLogical);
        var rowH = S(ListRowHeightLogical);
        var gap = S(ListRowGapLogical);
        var pad = S(ListPanelPadLogical);
        var n = _items.Count;

        var totalH = n * rowH + Math.Max(0, n - 1) * gap;
        var panelW = rowW + pad * 2;
        var panelH = totalH + pad * 2;

        var margin = S(8);
        // 扇区最右点 = center.X + fanOuterR；列表必须完全在其右侧
        var fanRight = _center.X + _fanOuterR;
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

        for (var i = 0; i < n; i++)
        {
            _items[i].TargetBounds = new RectangleF(
                listLeft + pad,
                listTop + pad + i * (rowH + gap),
                rowW,
                rowH);
            // 更小错峰，动画更干脆
            _items[i].Stagger = i * 0.02f;
        }
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
    }

    private void LoadItemsForActiveTab(bool restartAnim)
    {
        ClearItems(disposeOwned: true);
        _hoverItemIndex = -1;

        if (!_activeTab.HasValue)
        {
            RequestRender();
            return;
        }

        // 刷新扇区计数
        foreach (var tab in _tabs)
            tab.Count = CountForTab(tab.Kind);

        switch (_activeTab.Value)
        {
            case TabKind.ClipboardHistory:
                foreach (var hist in (_clipboardHistory?.GetItems() ?? []).Take(MaxOuterItems))
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
                foreach (var recent in WindowsRecentItemsService.GetItems().Take(MaxOuterItems))
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
                foreach (var entry in GetEntriesForTab(_activeTab.Value).Take(MaxOuterItems))
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

        var key = (isDirectory ? "dir:" : "file:") + path.ToLowerInvariant();
        if (_iconCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var icon = IconExtractor.GetIcon(path, isDirectory, useLargeIcon: false);
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
        var type = kind switch
        {
            TabKind.Folders => EntryType.Folder,
            TabKind.Files => EntryType.File,
            TabKind.Urls => EntryType.Url,
            TabKind.Texts => EntryType.Text,
            _ => EntryType.Folder
        };

        var typeEntries = EntryQueries.ByType(_configManager.Config.Entries, type);
        return _configManager.Config.SortByRecentUsage
            ? typeEntries.OrderBy(e => e.SortOrder).ThenByDescending(e => e.LastUsedAt)
            : typeEntries.OrderBy(e => e.SortOrder);
    }

    private void StartFireworkAnim()
    {
        _animStartTick = Environment.TickCount64;
        _animating = true;
        foreach (var item in _items)
            item.AnimT = 0f;

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

        foreach (var item in _items)
        {
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
            // 结束时高质量补一帧（含阴影）
            RenderLayered(fast: false);
        }
    }

    private void HitTest(Point client, out TabKind? tab, out int itemIndex)
    {
        tab = null;
        itemIndex = -1;

        // 1) 列表（最右，继续右滑命中）
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.AnimT < 0.28f)
                continue;
            if (GetAnimatedItemBounds(item).Contains(client))
            {
                itemIndex = i;
                tab = _activeTab;
                return;
            }
        }

        var dx = client.X - _center.X;
        var dy = client.Y - _center.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);

        // 2) 扇区环（中环）
        if (dist >= _fanInnerR - S(2) && dist <= _fanOuterR + S(4))
        {
            // 屏幕角：atan2(y,x)，转为 GDI 度（0=右，顺时针）
            var deg = MathF.Atan2(dy, dx) * 180f / MathF.PI;
            foreach (var slot in _tabs)
            {
                if (AngleInSweep(deg, slot.StartDeg, slot.SweepDeg))
                {
                    tab = slot.Kind;
                    return;
                }
            }
        }

        // 3) 中心圆：保持当前分类
        if (dist <= _hubR)
            tab = _activeTab;
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
        var t = Math.Clamp(item.AnimT, 0f, 1f);
        var target = item.TargetBounds;
        if (t >= 0.999f)
            return target;

        // 轻量：从左侧（扇区外缘）水平滑入，不缩放，减少每帧几何计算
        var slide = S(28f) * (1f - t);
        return new RectangleF(target.X - slide, target.Y, target.Width, target.Height);
    }

    private void DrawSegments(Graphics g, bool drawShadow)
    {
        if (_tabs.Count == 0)
            return;

        if (drawShadow)
        {
            using var shadowPath = CreateDonutFanPath(_center, _fanInnerR, _fanOuterR + S(1), FanStartDeg, FanTotalSweepDeg);
            using var shadowBrush = new SolidBrush(Color.FromArgb(22, 0, 0, 0));
            var state = g.Save();
            g.TranslateTransform(0, S(1.5f));
            g.FillPath(shadowBrush, shadowPath);
            g.Restore(state);
        }

        foreach (var slot in _tabs)
        {
            var active = slot.Kind == _activeTab || slot.Kind == _hoverTab;
            using var path = CreateDonutSegmentPath(_center, _fanInnerR, _fanOuterR, slot.StartDeg, slot.SweepDeg);

            using (var brush = new SolidBrush(active ? AccentSoft : SegmentFill))
                g.FillPath(brush, path);

            using (var pen = new Pen(active ? Accent : SegmentBorder, active ? Math.Max(1.4f, S(1.6f)) : Math.Max(1f, S(1.1f))))
                g.DrawPath(pen, path);

            var midR = (_fanInnerR + _fanOuterR) / 2f;
            var rad = slot.MidDeg * MathF.PI / 180f;
            var tx = _center.X + MathF.Cos(rad) * midR;
            var ty = _center.Y + MathF.Sin(rad) * midR;

            var titleColor = active ? SegmentTextHot : SegmentText;
            var countColor = active ? Color.FromArgb(70, 110, 170) : SegmentCount;

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

    private void DrawHub(Graphics g, bool drawShadow)
    {
        var rect = new RectangleF(_center.X - _hubR, _center.Y - _hubR, _hubR * 2, _hubR * 2);
        if (drawShadow)
            DrawSoftShadowEllipse(g, rect, 28);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var brush = new SolidBrush(HubFill);
            g.FillPath(brush, path);
            using var pen = new Pen(SegmentBorder, Math.Max(1.1f, S(1.25f)));
            g.DrawPath(pen, path);
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
            var titleRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.28f, rect.Width, rect.Height * 0.32f);
            var countRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.55f, rect.Width, rect.Height * 0.25f);
            g.DrawString(title, _hubTitleFont, titleBrush, titleRect, sf);
            g.DrawString(count, _hubCountFont, countBrush, countRect, sf);
        }
    }

    private void DrawListPanel(Graphics g, bool drawShadow)
    {
        if (_listPanelBounds.IsEmpty || _items.Count == 0)
            return;

        var maxT = 0f;
        foreach (var item in _items)
            if (item.AnimT > maxT) maxT = item.AnimT;
        if (maxT < 0.04f)
            return;

        var alpha = (int)(Math.Clamp(maxT, 0f, 1f) * 255);
        if (drawShadow)
            DrawSoftShadow(g, _listPanelBounds, S(12), Math.Min(alpha, 40));

        using var path = CreateRoundRect(_listPanelBounds, S(14));
        using (var brush = new SolidBrush(Color.FromArgb(Math.Min(250, alpha), ListPanelFill)))
            g.FillPath(brush, path);
        using (var pen = new Pen(Color.FromArgb(Math.Min(230, alpha), ListPanelBorder), Math.Max(1f, S(1.05f))))
            g.DrawPath(pen, path);
    }

    private void DrawItems(Graphics g, bool drawShadow)
    {
        if (_items.Count == 0)
        {
            if (_activeTab.HasValue)
            {
                using var brush = new SolidBrush(HintColor);
                var msg = "暂无项目";
                var size = g.MeasureString(msg, _itemFont);
                var x = _center.X + _fanOuterR + S(18);
                g.DrawString(msg, _itemFont, brush, x, _center.Y - size.Height / 2f);
            }
            return;
        }

        var iconSize = S(IconLogical);
        var corner = S(11f);

        // 单遍绘制；高亮项最后画
        var hotIndex = _hoverItemIndex;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var hot = i == hotIndex;
                if (pass == 0 && hot) continue;
                if (pass == 1 && !hot) continue;

                var item = _items[i];
                if (item.AnimT <= 0.02f)
                    continue;

                var bounds = GetAnimatedItemBounds(item);
                var alpha = (int)(Math.Clamp(item.AnimT, 0f, 1f) * 255);
                if (drawShadow && item.AnimT > 0.85f)
                    DrawSoftShadow(g, bounds, corner, Math.Min(alpha / 4, 28));

                using (var path = CreateRoundRect(bounds, Math.Min(corner, bounds.Height / 2f)))
                using (var brush = new SolidBrush(Color.FromArgb(alpha, hot ? RowFillHot : RowFill)))
                using (var pen = new Pen(
                    Color.FromArgb(Math.Min(255, alpha + 20), hot ? RowBorderHot : RowBorder),
                    hot ? Math.Max(1.4f, S(1.5f)) : Math.Max(1f, S(1.05f))))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                if (item.AnimT < 0.2f)
                    continue;

                var contentLeft = bounds.X + S(10);
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

    private void DrawSoftShadow(Graphics g, RectangleF rect, float radius, int alpha)
    {
        if (alpha <= 0)
            return;

        var shadow = RectangleF.Inflate(rect, S(2.5f), S(3.5f));
        shadow.Offset(0, S(2));
        using var path = CreateRoundRect(shadow, Math.Max(radius, S(4f)));
        using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 70), 0, 0, 0));
        g.FillPath(brush, path);
    }

    private void DrawSoftShadowEllipse(Graphics g, RectangleF rect, int alpha)
    {
        var shadow = RectangleF.Inflate(rect, S(3), S(4));
        shadow.Offset(0, S(2));
        using var path = new GraphicsPath();
        path.AddEllipse(shadow);
        using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 60), 0, 0, 0));
        g.FillPath(brush, path);
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

    private static GraphicsPath CreateDonutFanPath(PointF center, float innerR, float outerR, float startDeg, float sweepDeg)
        => CreateDonutSegmentPath(center, innerR, outerR, startDeg, sweepDeg);

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
            _configManager.TouchEntry(entry.Id);
            try
            {
                if (!string.IsNullOrEmpty(entry.Path))
                    Clipboard.SetText(entry.Path);
            }
            catch
            {
                // ignore
            }
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
        _ = CopyHistoryAsync(item.Text);
    }

    private async Task CopyHistoryAsync(string text)
    {
        try
        {
            if (_clipboardHistory != null)
                await _clipboardHistory.CopyPlainTextAsync(text);
            else
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

    private void PersistLastViewTab(TabKind kind)
    {
        if (!_configManager.Config.RememberLastView)
            return;

        if (string.Equals(_configManager.Config.LastViewTab, kind.ToString(), StringComparison.Ordinal))
            return;

        _configManager.Config.LastViewTab = kind.ToString();
        _configManager.Save();
    }

    private static string GetTabLabel(TabKind kind)
        => kind switch
        {
            TabKind.Folders => "文件夹",
            TabKind.Files => "文件",
            TabKind.Urls => "网页",
            TabKind.Texts => "文本",
            TabKind.ClipboardHistory => "历史",
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
