namespace Quickstart.UI;

using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Utils;

internal sealed class AiActionPickerPopup : Form
{
    private const string PromptHeader = "Prompt";
    private const string ToolHeader = "工具";
    private const int MaxRecentChips = 3;

    // 逻辑像素（96 DPI）。高度只按「条目数 × 行高」估算，避免测量布局状态导致裁切。
    private const int PreferredWidth = 460;
    private const int CompactWidth = 400;
    private const int MaxWidth = 560;
    private const int MinHeight = 180;
    private const int MaxHeight = 720;
    private const int TopBarLogicalHeight = 36;
    private const int TopBarBottomGap = 8;
    private const int GroupHeaderLogicalHeight = 26;
    private const int RowLogicalHeight = 36;
    private const int SkillRowLogicalHeight = 44;
    private const int RowGap = 4;
    private const int RootPadding = 10;
    private const int ChipLogicalHeight = 28;
    private const int RecentLabelLogicalWidth = 36;
    private const int SettingsButtonLogicalWidth = 28;
    // 单个最近 chip 最大宽度（逻辑像素）；过长名才省略
    private const int ChipMaxLogicalWidth = 160;
    private const int ChipGapLogical = 4;
    // 圆角裁切 + 双边框 + 列表底内边距，宁可多留一点也不裁最后一项
    private const int ContentBottomSlack = 20;
    private const int ChromeLogical = 8;
    private const int ListBottomPadding = 8;

    private static readonly Color PromptDot = Color.FromArgb(55, 138, 221);   // #378ADD
    private static readonly Color ToolDot = Color.FromArgb(99, 153, 34);      // #639922
    private static readonly Color SkillDot = Color.FromArgb(127, 119, 221);   // #7F77DD

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private readonly ConfigManager _configManager;
    private readonly TableLayoutPanel _root;
    private readonly Panel _topBar;
    private readonly Panel _recentHost;
    private readonly Label _recentLabel;
    private readonly FlowLayoutPanel _recentChips;
    private readonly Label _promptHeaderLabel;
    private readonly Label _toolHeaderLabel;
    private readonly FlowLayoutPanel _promptList;
    private readonly FlowLayoutPanel _toolList;
    private readonly List<ActionItemControl> _items = [];
    private readonly List<RecentChipControl> _chips = [];
    private readonly List<Control> _dragHandles = [];
    private readonly List<AiActionSelection> _allActions = [];

    // 动作列表签名：未变则跳过整表销毁重建，只刷新「最近」chip，减少左滑首帧卡顿。
    private string? _actionsSignature;
    private string? _recentSignature;
    private ActionItemControl? _lastHighlightedItem;
    private RecentChipControl? _lastHighlightedChip;
    private Size _lastPopupSize;
    private int _lastPopupDpi;

    // 手势跟踪阶段：不抢前台；松手未选中后进入交互模式：可拖动、ESC 关闭。
    private bool _gestureMode = true;
    private bool _showWithoutActivation = true;
    private bool _suppressAutoHide;

    public event Action<AiActionSelection, Point>? ActionSelected;
    public event Action? ShowSettings;

    public AiActionPickerPopup(ConfigManager configManager)
    {
        _configManager = configManager;

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(250, 250, 250);
        Padding = new Padding(1);
        SetStyle(
            ControlStyles.ResizeRedraw
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);
        DoubleBuffered = true;
        FormStyler.ApplyRounded(this);

        var border = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(220, 220, 220)
        };

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 250, 250),
            ColumnCount = 1,
            RowCount = 2,
            Padding = UiScaleHelper.ScalePadding(this, new Padding(RootPadding)),
            Margin = new Padding(0)
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 顶栏行高 = 内容高度 + 底间距，间距放进 Absolute 行高，避免 Margin 挤占下方列表
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, TopBarLogicalHeight + TopBarBottomGap)));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ── 顶栏：最近（文字+chip 同一水平线）+ 设置 ──────────────────
        // 无「左滑动作」标题；设置钮固定右侧，拖动区域在顶栏空白处。
        _topBar = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, UiScaleHelper.Scale(this, TopBarBottomGap)),
            Height = UiScaleHelper.Scale(this, TopBarLogicalHeight + TopBarBottomGap)
        };

        var settingsButton = CreateSettingsButton();
        settingsButton.Dock = DockStyle.Right;
        settingsButton.Width = UiScaleHelper.Scale(this, SettingsButtonLogicalWidth);
        settingsButton.Height = UiScaleHelper.Scale(this, TopBarLogicalHeight);

        _recentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _recentLabel = new Label
        {
            Text = "最近",
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = UiScaleHelper.Scale(this, RecentLabelLogicalWidth),
            Height = UiScaleHelper.Scale(this, TopBarLogicalHeight),
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.FromArgb(120, 120, 120),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Visible = false
        };

        _recentChips = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, UiScaleHelper.Scale(this, (TopBarLogicalHeight - ChipLogicalHeight) / 2), 0, 0),
            Margin = new Padding(0),
            Height = UiScaleHelper.Scale(this, TopBarLogicalHeight)
        };

        _recentHost.Controls.Add(_recentChips);
        _recentHost.Controls.Add(_recentLabel);
        _topBar.Controls.Add(_recentHost);
        _topBar.Controls.Add(settingsButton);
        _root.Controls.Add(_topBar, 0, 0);
        InstallTitleDragHandlers(_topBar, _recentHost, _recentLabel, _recentChips, _root, border, this);

        // ── 双栏内容区 ────────────────────────────────────────────────
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(250, 250, 250)
        };
        // 左右等分，列间距略留白，视觉更对称
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, GroupHeaderLogicalHeight)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.Controls.Add(content, 0, 1);

        _promptHeaderLabel = CreateGroupHeader(PromptHeader);
        _toolHeaderLabel = CreateGroupHeader(ToolHeader);
        _promptHeaderLabel.Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 6), 0);
        _toolHeaderLabel.Margin = new Padding(UiScaleHelper.Scale(this, 6), 0, 0, 0);
        content.Controls.Add(_promptHeaderLabel, 0, 0);
        content.Controls.Add(_toolHeaderLabel, 1, 0);
        InstallTitleDragHandlers(_promptHeaderLabel, _toolHeaderLabel, content);

        _promptList = CreateListPanel();
        _toolList = CreateListPanel();
        _promptList.Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 6), 0);
        _toolList.Margin = new Padding(UiScaleHelper.Scale(this, 6), 0, 0, 0);
        content.Controls.Add(_promptList, 0, 1);
        content.Controls.Add(_toolList, 1, 1);
        InstallTitleDragHandlers(_promptList, _toolList);

        border.Controls.Add(_root);
        Controls.Add(border);

        Deactivate += (_, _) =>
        {
            if (Visible && _interactiveDocked && !_suppressAutoHide)
                Hide();
        };
    }

    private bool _interactiveDocked => !_gestureMode;

    public bool HasActions => _items.Count > 0 || _chips.Count > 0;

    public void ShowAtGesturePoint(Point screenPt)
    {
        SuspendLayout();
        try
        {
            RefreshActions();
            if (!HasActions)
                return;

            EnterGestureMode();

            var screen = Screen.FromPoint(screenPt);
            EnsurePopupSizeForScreen(screen);
            var workingArea = screen.WorkingArea;
            var margin = UiScaleHelper.Scale(this, 8);
            var x = Math.Max(workingArea.Left + margin, Math.Min(screenPt.X - Width + margin, workingArea.Right - Width - margin));
            var y = Math.Max(workingArea.Top + margin, Math.Min(screenPt.Y - (Height / 2), workingArea.Bottom - Height - margin));
            Location = new Point(x, y);
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }

        // 手势阶段不抢前台：来源窗（微信等）保持激活，松手后 Ctrl+C 才能命中选区。
        if (!Visible)
            Show();
        HighlightAtScreenPoint(screenPt);
    }

    /// <summary>
    /// 手势松手且未点中动作项：停靠在屏幕上，允许拖动 / ESC / 点击外部关闭。
    /// </summary>
    public void EnterInteractiveMode()
    {
        if (!Visible || IsDisposed)
            return;

        _gestureMode = false;
        _showWithoutActivation = false;
        ApplyNoActivateStyle(enabled: false);
        UpdateDragCursors();

        // 手势阶段一直 NOACTIVATE，松手后必须真正抢到前台，ESC / 拖动 / 点击才可靠。
        // 先抑制失焦自动隐藏，避免 Activate 过程中的瞬时 Deactivate 把面板关掉。
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
            // 稍后再恢复失焦隐藏，等前台切换稳定。
            BeginInvoke(new Action(() =>
            {
                _suppressAutoHide = false;
                if (Visible && !ContainsFocus && Form.ActiveForm != this)
                {
                    // 仍未拿到焦点时再试一次，避免 ESC 失效。
                    WindowActivator.TryForceForeground(Handle);
                    Focus();
                }
            }));
        }
    }

    protected override bool ShowWithoutActivation => _showWithoutActivation;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // 默认 TOOLWINDOW；手势模式额外 NOACTIVATE（句柄创建后由 ApplyNoActivateStyle 动态切换）。
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            if (_showWithoutActivation)
                cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void HighlightAtScreenPoint(Point screenPt)
    {
        if (!_gestureMode)
            return;

        var activeItem = GetItemAtScreenPoint(screenPt);
        var activeChip = GetChipAtScreenPoint(screenPt);

        // 只切换前后两项，避免每帧 Invalidate 全部条目
        if (!ReferenceEquals(_lastHighlightedItem, activeItem))
        {
            _lastHighlightedItem?.SetHighlighted(false);
            activeItem?.SetHighlighted(true);
            _lastHighlightedItem = activeItem;
        }

        if (!ReferenceEquals(_lastHighlightedChip, activeChip))
        {
            _lastHighlightedChip?.SetHighlighted(false);
            activeChip?.SetHighlighted(true);
            _lastHighlightedChip = activeChip;
        }
    }

    public AiActionSelection? TryReleaseAtScreenPoint(Point screenPt)
    {
        if (!Bounds.Contains(screenPt))
            return null;

        var item = GetItemAtScreenPoint(screenPt);
        if (item != null)
        {
            _configManager.TouchAiAction(item.Selection.RecentKey);
            Hide();
            return item.Selection;
        }

        var chip = GetChipAtScreenPoint(screenPt);
        if (chip != null)
        {
            _configManager.TouchAiAction(chip.Selection.RecentKey);
            Hide();
            return chip.Selection;
        }

        return null;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            EnterGestureMode();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void EnterGestureMode()
    {
        _gestureMode = true;
        _showWithoutActivation = true;
        ApplyNoActivateStyle(enabled: true);
        ClearHighlight();
        UpdateDragCursors();
    }

    private void ClearHighlight()
    {
        _lastHighlightedItem?.SetHighlighted(false);
        _lastHighlightedChip?.SetHighlighted(false);
        _lastHighlightedItem = null;
        _lastHighlightedChip = null;
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

    private void RefreshActions()
    {
        var config = _configManager.Config.Ai;
        var actionsSignature = BuildActionsSignature();
        var recentSignature = BuildRecentSignature(config.RecentAiActionIds);

        // 动作列表未变：只刷新「最近」chip，避免整表销毁重建
        if (string.Equals(_actionsSignature, actionsSignature, StringComparison.Ordinal)
            && _items.Count > 0)
        {
            if (!string.Equals(_recentSignature, recentSignature, StringComparison.Ordinal))
            {
                RebuildRecentChips(config.RecentAiActionIds);
                _recentSignature = recentSignature;
            }

            return;
        }

        ClearHighlight();
        _promptList.SuspendLayout();
        _toolList.SuspendLayout();
        _recentChips.SuspendLayout();
        DisposeChildControls(_promptList);
        DisposeChildControls(_toolList);
        DisposeChildControls(_recentChips);
        _items.Clear();
        _chips.Clear();
        _allActions.Clear();
        try
        {
            foreach (var prompt in config.PromptPresets)
            {
                var selection = new AiActionSelection
                {
                    Kind = AiActionKind.Prompt,
                    Id = prompt.Id,
                    Name = prompt.Name
                };
                _allActions.Add(selection);
                AddActionItem(_promptList, selection);
            }

            var plainPaste = new AiActionSelection
            {
                Kind = AiActionKind.PlainTextPaste,
                Id = "plain-text-paste",
                Name = "粘贴为纯文本"
            };
            _allActions.Add(plainPaste);
            AddActionItem(_toolList, plainPaste);

            // 截图 OCR：默认显示；仅当 Ocr.Enabled=false 时隐藏
            if (_configManager.Config.Ocr?.Enabled != false)
            {
                var screenshotOcr = new AiActionSelection
                {
                    Kind = AiActionKind.ScreenshotOcr,
                    Id = "screenshot-ocr",
                    Name = "截图 OCR"
                };
                _allActions.Add(screenshotOcr);
                AddActionItem(_toolList, screenshotOcr);
            }

            var everything = new AiActionSelection
            {
                Kind = AiActionKind.EverythingSearch,
                Id = "everything-search",
                Name = "Everything 搜索"
            };
            _allActions.Add(everything);
            AddActionItem(_toolList, everything);

            foreach (var tool in _configManager.Config.WebSearchTools.Where(tool => tool.Enabled))
            {
                if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.UrlTemplate))
                    continue;

                var selection = new AiActionSelection
                {
                    Kind = AiActionKind.WebSearch,
                    Id = tool.Id,
                    Name = tool.Name,
                    UrlTemplate = tool.UrlTemplate
                };
                _allActions.Add(selection);
                AddActionItem(_toolList, selection);
            }

            // Skill 暂不在左滑面板列出（与历史行为一致；后续可按需并入工具栏）

            var promptCount = _promptList.Controls.OfType<ActionItemControl>().Count();
            var toolCount = _toolList.Controls.OfType<ActionItemControl>().Count();
            _promptHeaderLabel.Text = promptCount > 0 ? $"{PromptHeader} · {promptCount}" : PromptHeader;
            _toolHeaderLabel.Text = toolCount > 0 ? $"{ToolHeader} · {toolCount}" : ToolHeader;

            if (promptCount == 0)
                _promptList.Controls.Add(CreateEmptyLabel("暂无 Prompt"));

            RebuildRecentChips(config.RecentAiActionIds);
            _actionsSignature = actionsSignature;
            _recentSignature = recentSignature;
            // 动作集变了，强制下次重算尺寸
            _lastPopupSize = Size.Empty;
            _lastPopupDpi = 0;
        }
        finally
        {
            _promptList.ResumeLayout(performLayout: true);
            _toolList.ResumeLayout(performLayout: true);
            _recentChips.ResumeLayout(performLayout: true);
        }
    }

    private string BuildActionsSignature()
    {
        var cfg = _configManager.Config;
        var ocr = cfg.Ocr?.Enabled != false ? "1" : "0";
        var sb = new System.Text.StringBuilder(128);
        sb.Append("ocr=").Append(ocr).Append('|');
        foreach (var p in cfg.Ai.PromptPresets)
            sb.Append("P:").Append(p.Id).Append(':').Append(p.Name).Append(';');
        sb.Append("T:plain;T:ocr;T:everything;");
        foreach (var tool in cfg.WebSearchTools.Where(t => t.Enabled))
            sb.Append("W:").Append(tool.Id).Append(':').Append(tool.Name).Append(':').Append(tool.UrlTemplate).Append(';');
        return sb.ToString();
    }

    private static string BuildRecentSignature(List<string>? recentKeys)
    {
        if (recentKeys == null || recentKeys.Count == 0)
            return string.Empty;

        // 只看前 MaxRecentChips 个有效 key 的顺序
        var n = Math.Min(MaxRecentChips + 2, recentKeys.Count); // 多取一点，兼容失效 key
        return string.Join('\u001f', recentKeys.Take(n));
    }

    private void RebuildRecentChips(List<string>? recentKeys)
    {
        _lastHighlightedChip?.SetHighlighted(false);
        _lastHighlightedChip = null;
        _chips.Clear();
        DisposeChildControls(_recentChips);

        if (recentKeys == null || recentKeys.Count == 0 || _allActions.Count == 0)
        {
            _recentLabel.Visible = false;
            _recentChips.Visible = false;
            // chip 变化可能影响顶栏宽度，下次强制重算尺寸
            _lastPopupSize = Size.Empty;
            return;
        }

        var byKey = _allActions
            .GroupBy(a => a.RecentKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var key in recentKeys)
        {
            if (added >= MaxRecentChips)
                break;
            if (!byKey.TryGetValue(key, out var selection))
                continue;

            AddRecentChip(selection);
            added++;
        }

        var hasRecent = _chips.Count > 0;
        _recentLabel.Visible = hasRecent;
        _recentChips.Visible = hasRecent;
        _lastPopupSize = Size.Empty;
    }

    private void AddRecentChip(AiActionSelection selection)
    {
        var chip = new RecentChipControl(selection)
        {
            Height = UiScaleHelper.Scale(this, ChipLogicalHeight),
            Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 4), 0)
        };
        chip.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            var screenPoint = chip.PointToScreen(e.Location);
            RaiseActionSelected(selection, screenPoint);
        };
        _chips.Add(chip);
        _recentChips.Controls.Add(chip);
    }

    private static void DisposeChildControls(Control parent)
    {
        foreach (Control child in parent.Controls.Cast<Control>().ToArray())
            child.Dispose();
        parent.Controls.Clear();
    }

    private void AddActionItem(FlowLayoutPanel host, AiActionSelection selection)
    {
        var rowH = selection.IsSkill ? SkillRowLogicalHeight : RowLogicalHeight;
        var item = new ActionItemControl(selection)
        {
            Width = Math.Max(UiScaleHelper.Scale(this, 140), host.ClientSize.Width - UiScaleHelper.Scale(this, 4)),
            Height = UiScaleHelper.Scale(this, rowH),
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, RowGap))
        };
        item.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            var screenPoint = item.PointToScreen(e.Location);
            RaiseActionSelected(selection, screenPoint);
        };
        _items.Add(item);
        host.Controls.Add(item);
    }

    private void RaiseActionSelected(AiActionSelection selection, Point screenPoint)
    {
        _configManager.TouchAiAction(selection.RecentKey);
        Hide();
        ActionSelected?.Invoke(selection, screenPoint);
    }

    private ActionItemControl? GetItemAtScreenPoint(Point screenPt)
    {
        // 先用列表客户区快速剔除，再遍历（条目少时也比每次全量 RectangleToScreen 更稳）
        if (_items.Count == 0)
            return null;

        if (_promptList.IsHandleCreated
            && _promptList.RectangleToScreen(_promptList.ClientRectangle).Contains(screenPt))
        {
            foreach (var item in _items)
            {
                if (item.Parent != _promptList)
                    continue;
                if (item.RectangleToScreen(item.ClientRectangle).Contains(screenPt))
                    return item;
            }
        }

        if (_toolList.IsHandleCreated
            && _toolList.RectangleToScreen(_toolList.ClientRectangle).Contains(screenPt))
        {
            foreach (var item in _items)
            {
                if (item.Parent != _toolList)
                    continue;
                if (item.RectangleToScreen(item.ClientRectangle).Contains(screenPt))
                    return item;
            }
        }

        return null;
    }

    private RecentChipControl? GetChipAtScreenPoint(Point screenPt)
    {
        if (_chips.Count == 0 || !_recentChips.IsHandleCreated)
            return null;
        if (!_recentChips.RectangleToScreen(_recentChips.ClientRectangle).Contains(screenPt))
            return null;

        foreach (var chip in _chips)
        {
            if (chip.RectangleToScreen(chip.ClientRectangle).Contains(screenPt))
                return chip;
        }

        return null;
    }

    private void EnsurePopupSizeForScreen(Screen screen)
    {
        var dpi = UiScaleHelper.GetDpi(this);
        int S(int logical) => UiScaleHelper.Scale(logical, dpi);

        var promptItems = _items.Where(i => i.Parent == _promptList).ToList();
        var toolItems = _items.Where(i => i.Parent == _toolList).ToList();
        var hasBothColumns = promptItems.Count > 0 && toolItems.Count > 0;

        var logicalW = Math.Min(MaxWidth, hasBothColumns ? PreferredWidth : CompactWidth);
        var margin = S(8);
        var maxFormW = Math.Max(S(CompactWidth), screen.WorkingArea.Width - margin * 2);
        var maxFormH = Math.Max(S(MinHeight), screen.WorkingArea.Height - margin * 2);

        // 列表内容高度：优先用控件已设高度；若尚未布局则按逻辑行高回退
        var listContentPx = Math.Max(
            MeasureListContentHeight(promptItems, _promptList),
            MeasureListContentHeight(toolItems, _toolList));
        if (listContentPx <= 0)
        {
            var rows = Math.Max(1, Math.Max(promptItems.Count, toolItems.Count));
            listContentPx = rows * S(RowLogicalHeight) + Math.Max(0, rows - 1) * S(RowGap) + S(ListBottomPadding);
        }

        // 窗体总高 = 顶栏 + 分组标题 + 列表 + 内边距 + 边框余量
        var neededHeight =
            S(TopBarLogicalHeight + TopBarBottomGap)
            + S(GroupHeaderLogicalHeight)
            + listContentPx
            + S(RootPadding) * 2
            + S(ContentBottomSlack)
            + S(ChromeLogical);

        // 最近 chip：先按文字真实宽度测量
        var chipNaturalWidths = MeasureRecentChipNaturalWidths(S(ChipMaxLogicalWidth));
        var recentNeededW = EstimateRecentBarWidth(chipNaturalWidths, dpi);

        var finalW = Math.Min(Math.Max(S(logicalW), recentNeededW), maxFormW);
        var finalH = Math.Clamp(neededHeight, S(MinHeight), Math.Min(S(MaxHeight), maxFormH));
        var finalSize = new Size(finalW, finalH);

        // 尺寸与 DPI 未变且动作列表未重建时，跳过重布局
        var sizeUnchanged = finalSize == _lastPopupSize && dpi == _lastPopupDpi && Size == finalSize;
        if (!sizeUnchanged)
        {
            Size = finalSize;
            PerformLayout();
            _root.PerformLayout();
            _promptList.PerformLayout();
            _toolList.PerformLayout();
            _recentChips.PerformLayout();

            var colGap = S(2);
            foreach (var item in _items)
            {
                var hostWidth = item.Parent?.ClientSize.Width ?? 0;
                item.Width = hostWidth > colGap
                    ? Math.Max(S(140), hostWidth - colGap)
                    : Math.Max(S(140), (finalW / 2) - S(RootPadding) - S(12));
            }

            ApplyListScrollMetrics(_promptList, promptItems);
            ApplyListScrollMetrics(_toolList, toolItems);
            _lastPopupSize = finalSize;
            _lastPopupDpi = dpi;
        }

        // chip 宽度每次都应用（最近列表可能单独变了）
        ApplyRecentChipWidths(chipNaturalWidths, dpi);
    }

    /// <summary>测量每个最近 chip 的自然宽度（完整文字），不依赖布局后的 ClientSize。</summary>
    private int[] MeasureRecentChipNaturalWidths(int chipMaxPx)
    {
        if (_chips.Count == 0)
            return [];

        using var g = CreateGraphics();
        var widths = new int[_chips.Count];
        for (var i = 0; i < _chips.Count; i++)
            widths[i] = _chips[i].MeasureNaturalWidth(g, chipMaxPx);
        return widths;
    }

    /// <summary>估算顶栏「最近 + chips + 设置」需要的窗体宽度。</summary>
    private int EstimateRecentBarWidth(int[] chipWidths, int dpi)
    {
        if (chipWidths.Length == 0)
            return 0;

        int S(int logical) => UiScaleHelper.Scale(logical, dpi);
        var chipsTotal = 0;
        foreach (var w in chipWidths)
            chipsTotal += w + S(ChipGapLogical);

        return S(RootPadding) * 2
               + S(ChromeLogical)
               + S(RecentLabelLogicalWidth)
               + chipsTotal
               + S(SettingsButtonLogicalWidth)
               + S(12); // 设置钮与 chip 之间的呼吸空间
    }

    /// <summary>
    /// 给 chip 赋最终宽度：能完整显示就完整显示；
    /// 仅当顶栏可用宽度不足时，按比例压缩（仍优先保证可读，不再均分到 72px）。
    /// </summary>
    private void ApplyRecentChipWidths(int[] naturalWidths, int dpi)
    {
        if (_chips.Count == 0 || naturalWidths.Length != _chips.Count)
            return;

        int S(int logical) => UiScaleHelper.Scale(logical, dpi);
        var available = _recentChips.ClientSize.Width;
        if (available <= 0)
        {
            // 布局尚未给出宽度时，用窗体宽回退估算
            available = Math.Max(
                S(120),
                Width
                - S(RootPadding) * 2
                - S(ChromeLogical)
                - S(RecentLabelLogicalWidth)
                - S(SettingsButtonLogicalWidth)
                - S(12));
        }

        var totalNatural = 0;
        foreach (var w in naturalWidths)
            totalNatural += w + S(ChipGapLogical);

        if (totalNatural <= available)
        {
            for (var i = 0; i < _chips.Count; i++)
                _chips[i].Width = naturalWidths[i];
            return;
        }

        // 不够放：按比例缩小，但单 chip 不低于能显示约 4 个汉字的宽度
        var minChip = S(64);
        var scale = (double)available / totalNatural;
        var used = 0;
        for (var i = 0; i < _chips.Count; i++)
        {
            var target = (int)Math.Round(naturalWidths[i] * scale, MidpointRounding.AwayFromZero);
            if (i == _chips.Count - 1)
                target = Math.Max(minChip, available - used - S(ChipGapLogical));
            else
                target = Math.Clamp(target, minChip, naturalWidths[i]);

            _chips[i].Width = target;
            used += target + S(ChipGapLogical);
        }
    }

    private int MeasureListContentHeight(IReadOnlyList<ActionItemControl> items, FlowLayoutPanel list)
    {
        var dpi = UiScaleHelper.GetDpi(this);
        int S(int logical) => UiScaleHelper.Scale(logical, dpi);

        if (items.Count == 0)
        {
            // 空态占位
            return list.Controls.Count > 0
                ? list.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + list.Padding.Vertical
                : 0;
        }

        var total = 0;
        foreach (var item in items)
        {
            var h = item.Height > 0
                ? item.Height
                : S(item.Selection.IsSkill ? SkillRowLogicalHeight : RowLogicalHeight);
            var m = item.Margin.Vertical > 0 ? item.Margin.Vertical : S(RowGap);
            total += h + m;
        }

        // 最后一项 Margin 是间距；额外底 padding 防止贴边被圆角裁切
        total += Math.Max(list.Padding.Bottom, S(ListBottomPadding));
        return total;
    }

    private void ApplyListScrollMetrics(FlowLayoutPanel list, IReadOnlyList<ActionItemControl> items)
    {
        var contentH = MeasureListContentHeight(items, list);
        var bottomPad = UiScaleHelper.Scale(this, ListBottomPadding);
        list.Padding = new Padding(0, 0, 0, bottomPad);
        list.AutoScroll = true;
        // 明确最小滚动区域，避免 Dock=Fill 时 FlowLayoutPanel 算不出可滚高度
        list.AutoScrollMinSize = new Size(0, Math.Max(0, contentH));
        list.HorizontalScroll.Enabled = false;
        list.HorizontalScroll.Visible = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var pen = new Pen(Color.FromArgb(210, 210, 210));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void InstallTitleDragHandlers(params Control[] controls)
    {
        foreach (var control in controls)
        {
            if (control is Button)
                continue;

            _dragHandles.Add(control);
            control.MouseDown += OnDragHandleMouseDown;
        }

        UpdateDragCursors();
    }

    private void UpdateDragCursors()
    {
        // 标题区给拖动手势；列表空白区保持默认箭头，避免和动作项手型冲突。
        foreach (var handle in _dragHandles)
        {
            if (handle is Button)
                continue;
            if (ReferenceEquals(handle, _promptList)
                || ReferenceEquals(handle, _toolList)
                || ReferenceEquals(handle, _recentChips))
            {
                handle.Cursor = Cursors.Default;
                continue;
            }

            handle.Cursor = _interactiveDocked ? Cursors.SizeAll : Cursors.Default;
        }
    }

    private void OnDragHandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_interactiveDocked)
            return;

        if (sender is Control control)
        {
            // 点在动作项 / chip / 按钮上时不启动拖动。
            var screenPt = control.PointToScreen(e.Location);
            if (GetItemAtScreenPoint(screenPt) != null)
                return;
            if (GetChipAtScreenPoint(screenPt) != null)
                return;
            if (IsPointOverButton(screenPt))
                return;
        }

        // 系统拖动期间可能短暂失焦，抑制自动隐藏。
        _suppressAutoHide = true;
        try
        {
            BeginSystemMove();
        }
        finally
        {
            BeginInvoke(new Action(() => _suppressAutoHide = false));
        }
    }

    private bool IsPointOverButton(Point screenPt)
    {
        foreach (Control child in GetAllControls(this))
        {
            if (child is Button button
                && button.Visible
                && button.RectangleToScreen(button.ClientRectangle).Contains(screenPt))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Control> GetAllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in GetAllControls(child))
                yield return nested;
        }
    }

    private void BeginSystemMove()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private Button CreateSettingsButton()
    {
        var settingsButton = new Button
        {
            Text = "⚙",
            AccessibleName = "打开设置",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(90, 90, 90),
            Font = new Font("Segoe UI Symbol", 11f),
            Cursor = Cursors.Hand,
            TabStop = false,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 235);
        settingsButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 225, 225);
        settingsButton.Click += (_, _) =>
        {
            _suppressAutoHide = true;
            try
            {
                Hide();
                ShowSettings?.Invoke();
            }
            finally
            {
                _suppressAutoHide = false;
            }
        };
        return settingsButton;
    }

    private static Label CreateGroupHeader(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        ForeColor = Color.FromArgb(90, 90, 90),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 2)
    };

    private FlowLayoutPanel CreateListPanel() => new()
    {
        Dock = DockStyle.Fill,
        // 始终可滚：内容少时不出现条，内容被截断时一定能滚到底
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(0, 0, 0, UiScaleHelper.Scale(this, ListBottomPadding)),
        Margin = new Padding(0)
    };

    private Label CreateEmptyLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = UiScaleHelper.Scale(this, 28),
        Dock = DockStyle.Top,
        ForeColor = Color.FromArgb(140, 140, 140),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0),
        Font = new Font("Microsoft YaHei UI", 9f)
    };

    private static Color DotColorFor(AiActionKind kind) => kind switch
    {
        AiActionKind.Prompt => PromptDot,
        AiActionKind.Skill => SkillDot,
        _ => ToolDot
    };

    private sealed class ActionItemControl : Control
    {
        private bool _highlighted;

        public ActionItemControl(AiActionSelection selection)
        {
            Selection = selection;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        public AiActionSelection Selection { get; }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;

            _highlighted = highlighted;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.White);
            // 悬停高亮时用高速抗锯齿，降低手势拖动时的重绘成本
            e.Graphics.SmoothingMode = _highlighted
                ? System.Drawing.Drawing2D.SmoothingMode.HighSpeed
                : System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var backBrush = new SolidBrush(_highlighted
                ? Color.FromArgb(230, 240, 250)
                : Color.FromArgb(255, 255, 255));
            using var borderPen = new Pen(_highlighted
                ? Color.FromArgb(55, 138, 221)
                : Color.FromArgb(230, 228, 220));
            using var path = CreateRoundRect(rect, LogicalScale(8));
            e.Graphics.FillPath(backBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            // 类型色点
            var dotSize = LogicalScale(8);
            var padX = LogicalScale(10);
            var dotX = padX;
            var dotY = (Height - dotSize) / 2;
            using (var dotBrush = new SolidBrush(DotColorFor(Selection.Kind)))
                e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

            var titleColor = Color.FromArgb(35, 35, 35);
            var metaColor = Color.FromArgb(120, 120, 120);
            var textLeft = padX + dotSize + LogicalScale(8);
            var textWidth = Width - textLeft - padX;

            if (Selection.IsSkill)
            {
                var titleTop = LogicalScale(5);
                var titleH = LogicalScale(20);
                var metaTop = LogicalScale(24);
                TextRenderer.DrawText(
                    e.Graphics,
                    Selection.Name,
                    Font,
                    new Rectangle(textLeft, titleTop, textWidth, titleH),
                    titleColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(
                    e.Graphics,
                    $"{Selection.StepCount} 步",
                    new Font(Font.FontFamily, Math.Max(7.5f, Font.Size - 1f)),
                    new Rectangle(textLeft, metaTop, textWidth, LogicalScale(16)),
                    metaColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    Selection.Name,
                    Font,
                    new Rectangle(textLeft, 0, textWidth, Height),
                    titleColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int LogicalScale(int logicalPixels)
            => (int)Math.Round(logicalPixels * DeviceDpi / 96f, MidpointRounding.AwayFromZero);

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRect(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = Math.Max(2, radius * 2);
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class RecentChipControl : Control
    {
        private bool _highlighted;

        public RecentChipControl(AiActionSelection selection)
        {
            Selection = selection;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f);
            Width = 80;
        }

        public AiActionSelection Selection { get; }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;

            _highlighted = highlighted;
            Invalidate();
        }

        /// <summary>按完整文字测量自然宽度（含色点与内边距），不修改当前 Width。</summary>
        public int MeasureNaturalWidth(Graphics g, int maxWidth)
        {
            // 不传窄的 proposedSize，避免 TextRenderer 提前按上限截断测量结果
            var textSize = TextRenderer.MeasureText(
                g,
                Selection.Name,
                Font,
                new Size(int.MaxValue, Height > 0 ? Height : 32),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var pad = LogicalScale(8) * 2 + LogicalScale(7) + LogicalScale(5); // 左右 padding + 色点 + 间距
            return Math.Min(maxWidth, Math.Max(LogicalScale(52), textSize.Width + pad + LogicalScale(4)));
        }

        public void MeasurePreferredWidth(Graphics g, int maxWidth)
        {
            Width = MeasureNaturalWidth(g, maxWidth);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(250, 250, 250));
            e.Graphics.SmoothingMode = _highlighted
                ? System.Drawing.Drawing2D.SmoothingMode.HighSpeed
                : System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var backBrush = new SolidBrush(_highlighted
                ? Color.FromArgb(230, 240, 250)
                : Color.FromArgb(245, 245, 245));
            using var borderPen = new Pen(_highlighted
                ? Color.FromArgb(55, 138, 221)
                : Color.FromArgb(220, 218, 210));
            using var path = CreateRoundRect(rect, LogicalScale(14));
            e.Graphics.FillPath(backBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            var padX = LogicalScale(8);
            var dotSize = LogicalScale(7);
            var dotY = (Height - dotSize) / 2;
            using (var dotBrush = new SolidBrush(DotColorFor(Selection.Kind)))
                e.Graphics.FillEllipse(dotBrush, padX, dotY, dotSize, dotSize);

            var textLeft = padX + dotSize + LogicalScale(5);
            TextRenderer.DrawText(
                e.Graphics,
                Selection.Name,
                Font,
                new Rectangle(textLeft, 0, Width - textLeft - padX, Height),
                Color.FromArgb(40, 40, 40),
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private int LogicalScale(int logicalPixels)
            => (int)Math.Round(logicalPixels * DeviceDpi / 96f, MidpointRounding.AwayFromZero);

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRect(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = Math.Max(2, radius * 2);
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
