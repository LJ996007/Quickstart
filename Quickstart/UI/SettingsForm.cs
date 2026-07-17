namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class SettingsForm : Form
{
    // ── 视觉令牌（与主界面 / ButtonStyler 对齐）────────────────────────
    private static readonly Color BgApp = Color.FromArgb(250, 250, 250);
    private static readonly Color BgNav = Color.FromArgb(244, 245, 247);
    private static readonly Color BgContent = Color.FromArgb(250, 250, 250);
    private static readonly Color BgCard = Color.White;
    private static readonly Color BgBottom = Color.FromArgb(255, 255, 255);
    private static readonly Color BorderSoft = Color.FromArgb(228, 230, 234);
    private static readonly Color BorderCard = Color.FromArgb(232, 234, 238);
    private static readonly Color TextPrimary = Color.FromArgb(32, 33, 36);
    private static readonly Color TextSecondary = Color.FromArgb(90, 96, 105);
    private static readonly Color TextMuted = Color.FromArgb(138, 143, 152);
    private static readonly Color TextLabel = Color.FromArgb(72, 78, 88);
    private static readonly Color Accent = Color.FromArgb(96, 170, 255);
    private static readonly Color AccentSoft = Color.FromArgb(232, 242, 255);
    private static readonly Color AccentText = Color.FromArgb(36, 99, 168);
    private static readonly Color NavHover = Color.FromArgb(236, 238, 242);

    private const string UiFont = "Microsoft YaHei UI";

    private readonly ConfigManager _configManager;
    private readonly TextBox _tcPathBox;
    private readonly TextBox _dopusPathBox;
    private readonly TextBox _everythingPathBox;
    private readonly ComboBox _openWithBox;
    private readonly CheckBox _startupCheck;
    private readonly CheckBox _shellMenuCheck;
    private readonly TextBox _hotKeyBox;
    private readonly CheckBox _rightDragCheck;
    private readonly NumericUpDown _gestureDistanceBox;
    private readonly NumericUpDown _gestureToleranceBox;
    private readonly ComboBox _leftDragActionBox;
    private readonly Button _manageWebSearchToolsBtn;
    private readonly CheckBox _rememberLastViewCheck;
    private readonly CheckBox _sortRecentCheck;
    private readonly Label _websiteHintLabel;
    private readonly Button _copyBookmarkletBtn;
    private readonly Button _repairProtocolBtn;
    private readonly Button _openAiSettingsBtn;
    private readonly CheckBox _ocrEnabledCheck;
    private readonly TextBox _ocrApiKeyBox;
    private readonly TextBox _ocrSecretKeyBox;
    private readonly Label _ocrHintLabel;
    private readonly CheckBox _clipboardHistoryEnabledCheck;
    private readonly CheckBox _clipboardHistoryPersistCheck;
    private readonly NumericUpDown _clipboardHistoryMaxBox;
    private readonly Button _clipboardHistoryClearBtn;
    private readonly ClipboardHistoryService? _clipboardHistory;

    private readonly ListBox _navList;
    private readonly Panel _contentHost;
    private readonly Label _pageTitleLabel;
    private readonly Label _pageSubtitleLabel;
    private readonly Panel[] _pages;

    private readonly (string Title, string Subtitle)[] _pagesMeta =
    [
        ("常规", "路径、默认打开方式与系统集成"),
        ("快捷启动", "全局快捷键与右键拖动手势"),
        ("列表与工具", "列表行为、工具列与浏览器添加"),
        ("AI", "模型、Prompt 与 Skill 配置入口"),
        ("截图 OCR", "百度通用文字识别"),
        ("剪贴板", "剪贴板历史记录"),
        ("程序信息", "版本信息与修改记录")
    ];

    public SettingsForm(ConfigManager configManager, ClipboardHistoryService? clipboardHistory = null)
    {
        _configManager = configManager;
        _clipboardHistory = clipboardHistory;
        var config = configManager.Config;

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Quickstart 设置";
        ClientSize = new Size(760, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(UiFont, 9f);
        BackColor = BgApp;
        Padding = new Padding(0);
        FormStyler.ApplyRounded(this);

        // ── 控件 ──────────────────────────────────────────────────────
        const int pathLabelWidth = 126;

        _tcPathBox = CreateTextBox(config.TotalCommanderPath);
        var tcBrowseBtn = CreateBrowseButton();
        tcBrowseBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Total Commander|TOTALCMD64.EXE;TOTALCMD.EXE|所有文件|*.*",
                Title = "选择 Total Commander 可执行文件"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _tcPathBox.Text = dlg.FileName;
        };
        var tcDetectBtn = CreateDetectButton();
        tcDetectBtn.Click += (_, _) => DetectPath(
            TcDetector.Detect,
            _tcPathBox,
            "Total Commander",
            "未检测到 Total Commander，请手动指定路径。");

        _dopusPathBox = CreateTextBox(config.DirectoryOpusPath);
        var doBrowseBtn = CreateBrowseButton();
        doBrowseBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Directory Opus|dopus.exe|所有文件|*.*",
                Title = "选择 Directory Opus 可执行文件"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _dopusPathBox.Text = dlg.FileName;
        };
        var doDetectBtn = CreateDetectButton();
        doDetectBtn.Click += (_, _) => DetectPath(
            DopusDetector.Detect,
            _dopusPathBox,
            "Directory Opus",
            "未检测到 Directory Opus，请手动指定路径。");

        _everythingPathBox = CreateTextBox(config.EverythingPath);
        var everythingBrowseBtn = CreateBrowseButton();
        everythingBrowseBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Everything|Everything.exe|所有文件|*.*",
                Title = "选择 Everything 可执行文件"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _everythingPathBox.Text = dlg.FileName;
        };
        var everythingDetectBtn = CreateDetectButton();
        everythingDetectBtn.Click += (_, _) => DetectPath(
            EverythingDetector.Detect,
            _everythingPathBox,
            "Everything",
            "未检测到 Everything，请手动指定路径。");

        _openWithBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.System,
            Font = new Font(UiFont, 9f)
        };
        _openWithBox.Items.AddRange(["Total Commander", "资源管理器", "Directory Opus"]);
        _openWithBox.SelectedIndex = config.DefaultOpenWith switch
        {
            OpenWith.DirectoryOpus => 2,
            OpenWith.Explorer => 1,
            _ => 0
        };

        var tcPathRow = CreatePathFieldRow("Total Commander", _tcPathBox, tcBrowseBtn, tcDetectBtn, pathLabelWidth);
        var dopusPathRow = CreatePathFieldRow("Directory Opus", _dopusPathBox, doBrowseBtn, doDetectBtn, pathLabelWidth);
        var everythingPathRow = CreatePathFieldRow("Everything", _everythingPathBox, everythingBrowseBtn, everythingDetectBtn, pathLabelWidth);
        var openRow = CreateLabeledFieldRow("默认打开方式", _openWithBox, labelWidth: pathLabelWidth);

        _startupCheck = CreateCheckBox("开机自动启动", config.StartWithWindows);
        _shellMenuCheck = CreateCheckBox("在右键菜单中显示「添加到 Quickstart」", config.ShellMenuEnabled);

        _hotKeyBox = CreateTextBox(config.HotKey);
        _hotKeyBox.PlaceholderText = "例如 Ctrl+Shift+Space；留空表示禁用";
        var hotKeyRow = CreateLabeledFieldRow("全局快捷键", _hotKeyBox, labelWidth: 88);

        _rightDragCheck = CreateCheckBox("启用右键拖动手势", config.RightDragEnabled);
        _rightDragCheck.Margin = new Padding(0, 4, 0, 2);

        var gestureHintLabel = CreateHintLabel("向右拖动打开启动器；向左拖动执行下方动作");
        gestureHintLabel.Margin = new Padding(22, 0, 0, 8);

        _leftDragActionBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.System,
            Font = new Font(UiFont, 9f)
        };
        _leftDragActionBox.Items.AddRange(["动作面板（AI + Everything）", "直接用 Everything 搜索"]);
        _leftDragActionBox.SelectedIndex = config.LeftDragAction == LeftDragAction.EverythingSearch ? 1 : 0;
        var leftDragActionRow = CreateLabeledFieldRow("向左动作", _leftDragActionBox, labelWidth: 88);

        _gestureDistanceBox = CreateNumericUpDown(40, 600, 10, Math.Clamp(config.RightDragTriggerDistance, 40, 600));
        _gestureToleranceBox = CreateNumericUpDown(10, 300, 10, Math.Clamp(config.RightDragVerticalTolerance, 10, 300));

        var gestureMetricsRow = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Margin = new Padding(0, 8, 0, 0)
        };
        gestureMetricsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        gestureMetricsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        gestureMetricsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        gestureMetricsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        gestureMetricsRow.Controls.Add(CreateFieldLabel("触发距离"), 0, 0);
        gestureMetricsRow.Controls.Add(_gestureDistanceBox, 1, 0);
        gestureMetricsRow.Controls.Add(CreateFieldLabel("垂直容差"), 2, 0);
        gestureMetricsRow.Controls.Add(_gestureToleranceBox, 3, 0);

        var gestureOptionsPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(22, 0, 0, 0),
            Padding = new Padding(0)
        };
        gestureOptionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gestureOptionsPanel.Controls.Add(leftDragActionRow, 0, 0);
        gestureOptionsPanel.Controls.Add(gestureMetricsRow, 0, 1);

        void UpdateGestureControlsEnabled()
        {
            var enabled = _rightDragCheck.Checked;
            gestureHintLabel.Enabled = enabled;
            gestureOptionsPanel.Enabled = enabled;
        }
        _rightDragCheck.CheckedChanged += (_, _) => UpdateGestureControlsEnabled();
        UpdateGestureControlsEnabled();

        _rememberLastViewCheck = CreateCheckBox("记住上次标签和分组", config.RememberLastView);
        _rememberLastViewCheck.Margin = new Padding(0, 0, 20, 0);
        _sortRecentCheck = CreateCheckBox("最近使用的项目优先", config.SortByRecentUsage);
        _sortRecentCheck.Margin = new Padding(0);

        // 紧凑一行，不要 Dock.Fill，否则会在卡片底部撑出一条无意义的灰色空白
        var listBehaviorRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        listBehaviorRow.Controls.Add(_rememberLastViewCheck);
        listBehaviorRow.Controls.Add(_sortRecentCheck);

        _manageWebSearchToolsBtn = new RoundedButton
        {
            Text = "管理网页查询工具",
            Margin = new Padding(0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplySecondary(_manageWebSearchToolsBtn);
        _manageWebSearchToolsBtn.Click += (_, _) =>
        {
            using var form = new WebSearchToolsForm(_configManager);
            DialogPresenter.ShowModal(form, this);
        };
        var searchToolsRow = CreateLabeledFieldRow("工具列", _manageWebSearchToolsBtn, labelWidth: 88, controlDock: DockStyle.None);

        _websiteHintLabel = CreateHintLabel(
            "点击「复制一键添加书签」，把内容保存到浏览器收藏栏。之后在任意网页点一下该书签，Quickstart 会带着标题和网址弹出确认框。");

        _copyBookmarkletBtn = new RoundedButton
        {
            Text = "复制一键添加书签",
            Margin = new Padding(0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplyPrimary(_copyBookmarkletBtn);
        _copyBookmarkletBtn.Click += (_, _) =>
        {
            Clipboard.SetText(QuickstartProtocol.Bookmarklet);
            DialogPresenter.ShowMessage(
                this,
                "书签代码已复制。请在浏览器中新建书签，并把复制的内容粘贴到书签地址栏。",
                "浏览器一键添加网站",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        _repairProtocolBtn = new RoundedButton
        {
            Text = "重新注册协议",
            Margin = new Padding(8, 0, 0, 0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplySecondary(_repairProtocolBtn);
        _repairProtocolBtn.Click += (_, _) =>
        {
            ShellIntegration.RegisterProtocol(Application.ExecutablePath);
            DialogPresenter.ShowMessage(
                this,
                "quickstart:// 协议已重新注册。",
                "浏览器一键添加网站",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        var websiteActionsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0)
        };
        websiteActionsRow.Controls.Add(_copyBookmarkletBtn);
        websiteActionsRow.Controls.Add(_repairProtocolBtn);

        var aiHintLabel = CreateHintLabel(
            "配置模型 Provider、API Key、Prompt 预设和工作流 Skill。将向左拖动动作设为 AI 时可快速调用。");

        _openAiSettingsBtn = new RoundedButton
        {
            Text = "打开 AI 设置",
            Margin = new Padding(0, 12, 0, 0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplyPrimary(_openAiSettingsBtn);
        _openAiSettingsBtn.Click += (_, _) =>
        {
            using var form = new AiSettingsForm(_configManager);
            DialogPresenter.ShowModal(form, this);
        };

        // OCR
        var ocr = config.Ocr ?? new OcrConfig();
        var hasAk = AiSecretStore.HasApiKeyById(OcrConfig.BaiduApiKeySecretId);
        var hasSk = AiSecretStore.HasApiKeyById(OcrConfig.BaiduSecretKeySecretId);

        _ocrEnabledCheck = CreateCheckBox("在左滑工具列显示「截图 OCR」", ocr.Enabled);
        _ocrHintLabel = CreateHintLabel(
            "框选屏幕区域 → 百度通用文字识别 → 纯文本写入剪贴板。图片会上传百度云；密钥本地加密保存。");

        _ocrApiKeyBox = CreateTextBox(string.Empty);
        _ocrApiKeyBox.UseSystemPasswordChar = true;
        _ocrApiKeyBox.PlaceholderText = hasAk ? "已保存，留空保持不变" : "百度 API Key";

        _ocrSecretKeyBox = CreateTextBox(string.Empty);
        _ocrSecretKeyBox.UseSystemPasswordChar = true;
        _ocrSecretKeyBox.PlaceholderText = hasSk ? "已保存，留空保持不变" : "百度 Secret Key";

        var ocrAkRow = CreateLabeledFieldRow("API Key", _ocrApiKeyBox, labelWidth: 88);
        var ocrSkRow = CreateLabeledFieldRow("Secret Key", _ocrSecretKeyBox, labelWidth: 88);

        // 剪贴板
        var hist = config.ClipboardHistory ?? new ClipboardHistoryConfig();
        _clipboardHistoryEnabledCheck = CreateCheckBox("启用剪贴板历史（右滑「历史」Tab）", hist.Enabled);
        var histHint = CreateHintLabel(
            "系统复制的文本会自动记入历史。右滑点选后再次复制为纯文本，可到别处 Ctrl+V。");
        _clipboardHistoryPersistCheck = CreateCheckBox("退出后保存到本地", hist.Persist);

        _clipboardHistoryMaxBox = CreateNumericUpDown(5, 200, 1, Math.Clamp(hist.MaxItems, 5, 200));
        // 固定尺寸 + 禁止纵向拉伸，避免数字文字在框内偏上
        _clipboardHistoryMaxBox.Dock = DockStyle.None;
        _clipboardHistoryMaxBox.Anchor = AnchorStyles.Left;
        _clipboardHistoryMaxBox.AutoSize = false;
        _clipboardHistoryMaxBox.Width = 72;
        _clipboardHistoryMaxBox.Margin = new Padding(0, 0, 6, 0);
        _clipboardHistoryMaxBox.TextAlign = HorizontalAlignment.Left;

        _clipboardHistoryClearBtn = new RoundedButton
        {
            Text = "清空历史",
            AutoSize = false,
            Margin = new Padding(12, 0, 0, 0),
            Anchor = AnchorStyles.Left,
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplyDangerSecondary(_clipboardHistoryClearBtn);
        _clipboardHistoryClearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "确定清空全部剪贴板历史？", "剪贴板历史",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _clipboardHistory?.Clear();
            MessageBox.Show(this, "已清空。", "剪贴板历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        // 固定等高的 Flow 行：Label 与 NumericUpDown 设同一 Height + MiddleLeft，
        // 避免 TableLayout Anchor=None 在 AutoSize 单元格里仍出现基线漂移。
        var histMaxLabel = new Label
        {
            Text = "最多保留",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextLabel,
            Font = new Font(UiFont, 9f),
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(0),
            UseCompatibleTextRendering = false
        };
        var histUnitLabel = new Label
        {
            Text = "条",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextLabel,
            Font = new Font(UiFont, 9f),
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseCompatibleTextRendering = false
        };

        var histOptionsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        histOptionsRow.Controls.Add(histMaxLabel);
        histOptionsRow.Controls.Add(_clipboardHistoryMaxBox);
        histOptionsRow.Controls.Add(histUnitLabel);
        histOptionsRow.Controls.Add(_clipboardHistoryClearBtn);

        var histLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        histLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        histLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        histLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        histLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        histLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        histLayout.Controls.Add(_clipboardHistoryEnabledCheck, 0, 0);
        histLayout.Controls.Add(histHint, 0, 1);
        histLayout.Controls.Add(_clipboardHistoryPersistCheck, 0, 2);
        histLayout.Controls.Add(histOptionsRow, 0, 3);

        // ── 分页（卡片分区）────────────────────────────────────────────
        var generalPage = CreatePagePanel(
            CreateCard("路径与打开方式", compact: true,
                tcPathRow, dopusPathRow, everythingPathRow, openRow),
            CreateCard("系统集成",
                _startupCheck, _shellMenuCheck));

        var launchPage = CreatePagePanel(
            CreateCard("快捷键", hotKeyRow),
            CreateCard("右键拖动手势",
                _rightDragCheck, gestureHintLabel, gestureOptionsPanel));

        var listToolsPage = CreatePagePanel(
            CreateCard("列表行为", listBehaviorRow),
            CreateCard("工具列", searchToolsRow),
            CreateCard("浏览器一键添加网站",
                _websiteHintLabel, websiteActionsRow));

        var aiPage = CreatePagePanel(
            CreateCard("AI 能力", aiHintLabel, _openAiSettingsBtn));

        var ocrPage = CreatePagePanel(
            CreateCard("百度 OCR",
                _ocrEnabledCheck, _ocrHintLabel, ocrAkRow, ocrSkRow));

        var histPage = CreatePagePanel(CreateCard("剪贴板历史", histLayout));

        var productVersion = GetProductVersion();
        var productNameLabel = new Label
        {
            Text = "Quickstart",
            AutoSize = true,
            Font = new Font(UiFont, 16f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 4)
        };
        var versionLabel = new Label
        {
            Text = $"版本  v{productVersion}",
            AutoSize = true,
            Font = new Font(UiFont, 9f),
            ForeColor = AccentText,
            Margin = new Padding(0, 0, 0, 8)
        };
        var descriptionLabel = CreateHintLabel("Windows 系统托盘快捷启动工具，支持快速搜索和打开收藏的文件、文件夹与网站。");
        descriptionLabel.Margin = new Padding(0);

        var releaseNotesBox = new RichTextBox
        {
            Text = AppReleaseNotes.GetDisplayText(),
            ReadOnly = true,
            DetectUrls = false,
            BorderStyle = BorderStyle.None,
            BackColor = BgCard,
            ForeColor = TextSecondary,
            Font = new Font(UiFont, 9f),
            Dock = DockStyle.Top,
            Height = 190,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            TabStop = false,
            Margin = new Padding(0)
        };

        var infoPage = CreatePagePanel(
            CreateCard("关于", productNameLabel, versionLabel, descriptionLabel),
            CreateCard("版本说明", releaseNotesBox));

        _pages = [generalPage, launchPage, listToolsPage, aiPage, ocrPage, histPage, infoPage];

        // ── 页头 ──────────────────────────────────────────────────────
        _pageTitleLabel = new Label
        {
            Text = _pagesMeta[0].Title,
            AutoSize = true,
            Font = new Font(UiFont, 14f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 2)
        };
        _pageSubtitleLabel = new Label
        {
            Text = _pagesMeta[0].Subtitle,
            AutoSize = true,
            Font = new Font(UiFont, 8.5f),
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 0, 0)
        };

        var pageHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 4)
        };
        pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pageHeader.Controls.Add(_pageTitleLabel, 0, 0);
        pageHeader.Controls.Add(_pageSubtitleLabel, 0, 1);

        // ── 左侧导航 ──────────────────────────────────────────────────
        _navList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            Font = new Font(UiFont, 9.5f),
            BackColor = BgNav,
            ForeColor = TextSecondary,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var meta in _pagesMeta)
            _navList.Items.Add(meta.Title);

        _navList.DrawItem += OnNavDrawItem;
        _navList.MouseMove += (_, e) =>
        {
            var idx = _navList.IndexFromPoint(e.Location);
            if (idx != _navHoverIndex)
            {
                _navHoverIndex = idx;
                _navList.Invalidate();
            }
        };
        _navList.MouseLeave += (_, _) =>
        {
            _navHoverIndex = -1;
            _navList.Invalidate();
        };

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgContent,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        foreach (var page in _pages)
            _contentHost.Controls.Add(page);

        _navList.SelectedIndexChanged += (_, _) => ShowPage(_navList.SelectedIndex);
        _navList.SelectedIndex = 0;

        var navHeader = new Label
        {
            Text = "设置",
            Dock = DockStyle.Top,
            Height = 44,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFont, 11f, FontStyle.Bold),
            ForeColor = TextPrimary,
            BackColor = BgNav,
            Padding = new Padding(18, 0, 0, 0)
        };

        var navBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgNav,
            Padding = new Padding(8, 4, 8, 8)
        };
        navBody.Controls.Add(_navList);

        var navShell = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgNav,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        navShell.Controls.Add(navBody);
        navShell.Controls.Add(navHeader);

        var contentColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(22, 16, 22, 12),
            BackColor = BgContent
        };
        contentColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentColumn.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Add(control, column, row)：页头在第 0 行，内容在第 1 行
        contentColumn.Controls.Add(pageHeader, 0, 0);
        contentColumn.Controls.Add(_contentHost, 0, 1);

        // ── 底部栏 ────────────────────────────────────────────────────
        var okBtn = new RoundedButton
        {
            Text = "保存",
            Margin = new Padding(8, 0, 0, 0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplyPrimary(okBtn);
        okBtn.Click += OnSave;

        var cancelBtn = new RoundedButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0),
            Font = new Font(UiFont, 9f)
        };
        ButtonStyler.ApplySecondary(cancelBtn);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quickstart",
            "config.json");
        var infoLabel = new Label
        {
            Text = $"配置文件：{configPath}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFont, 8f),
            ForeColor = TextMuted,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        // 右侧按钮区用 AutoSize 的 TableLayout，避免 FlowLayoutPanel 在
        // AutoSize 列里 PreferredWidth 偏小导致「保存」被裁成半个。
        okBtn.Margin = new Padding(0, 0, 8, 0);
        cancelBtn.Margin = new Padding(0);
        var buttonsRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
            Padding = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttonsRow.Controls.Add(okBtn, 0, 0);
        buttonsRow.Controls.Add(cancelBtn, 1, 0);

        var bottomBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(16, 10, 16, 12),
            BackColor = BgBottom
        };
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottomBar.Controls.Add(infoLabel, 0, 0);
        bottomBar.Controls.Add(buttonsRow, 1, 0);

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 1,
            BackColor = BorderSoft,
            Margin = new Padding(0)
        };

        var navDivider = new Panel
        {
            Dock = DockStyle.Fill,
            Width = 1,
            BackColor = BorderSoft,
            Margin = new Padding(0)
        };

        // 用三列把导航分隔线画干净：nav | divider | content
        var bodyWithDivider = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        bodyWithDivider.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        bodyWithDivider.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        bodyWithDivider.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bodyWithDivider.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bodyWithDivider.Controls.Add(navShell, 0, 0);
        bodyWithDivider.Controls.Add(navDivider, 1, 0);
        bodyWithDivider.Controls.Add(contentColumn, 2, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = BgApp
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(bodyWithDivider, 0, 0);
        root.Controls.Add(separator, 0, 1);
        root.Controls.Add(bottomBar, 0, 2);
        Controls.Add(root);

        void ApplyScaledMetrics()
        {
            // Path / single-line boxes: lock to PreferredHeight so text is
            // vertically centered inside the FixedSingle border (not top-heavy).
            var pathInputHeight = UiScaleHelper.FitSingleLineTextBox(_tcPathBox);
            UiScaleHelper.FitSingleLineTextBox(_dopusPathBox);
            UiScaleHelper.FitSingleLineTextBox(_everythingPathBox);
            var inputHeight = UiScaleHelper.FitSingleLineTextBox(_hotKeyBox);
            UiScaleHelper.FitSingleLineTextBox(_ocrApiKeyBox);
            UiScaleHelper.FitSingleLineTextBox(_ocrSecretKeyBox);

            var comboHeight = UiScaleHelper.GetInputHeight(_openWithBox, 28);
            _openWithBox.MinimumSize = new Size(0, comboHeight);
            _openWithBox.Height = comboHeight;
            var leftDragComboHeight = UiScaleHelper.GetInputHeight(_leftDragActionBox, 28);
            _leftDragActionBox.MinimumSize = new Size(0, leftDragComboHeight);
            _leftDragActionBox.Height = leftDragComboHeight;

            // Match path-row action buttons to the textbox height so the whole
            // row shares one baseline (label / box / … / 检测).
            var browseButtonSize = UiScaleHelper.GetButtonSize(this, tcBrowseBtn.Text, tcBrowseBtn.Font, 36, 26, horizontalLogicalPadding: 8, verticalLogicalPadding: 4);
            var detectButtonSize = UiScaleHelper.GetButtonSize(this, tcDetectBtn.Text, tcDetectBtn.Font, 52, 26, horizontalLogicalPadding: 10, verticalLogicalPadding: 4);
            var pathBtnH = pathInputHeight;
            var browseW = browseButtonSize.Width;
            var detectW = detectButtonSize.Width;

            void FitPathBtn(Control btn, int width)
            {
                btn.MinimumSize = new Size(width, pathBtnH);
                btn.MaximumSize = new Size(width, pathBtnH);
                btn.Size = new Size(width, pathBtnH);
                btn.Anchor = AnchorStyles.None;
            }

            FitPathBtn(tcBrowseBtn, browseW);
            FitPathBtn(doBrowseBtn, browseW);
            FitPathBtn(everythingBrowseBtn, browseW);
            FitPathBtn(tcDetectBtn, detectW);
            FitPathBtn(doDetectBtn, detectW);
            FitPathBtn(everythingDetectBtn, detectW);

            var dialogButtonSize = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 92, 34, horizontalLogicalPadding: 14);
            var copyBookmarkletButtonSize = UiScaleHelper.GetButtonSize(this, _copyBookmarkletBtn.Text, _copyBookmarkletBtn.Font, 150, 34, horizontalLogicalPadding: 12);
            var repairProtocolButtonSize = UiScaleHelper.GetButtonSize(this, _repairProtocolBtn.Text, _repairProtocolBtn.Font, 120, 34, horizontalLogicalPadding: 12);
            var openAiSettingsButtonSize = UiScaleHelper.GetButtonSize(this, _openAiSettingsBtn.Text, _openAiSettingsBtn.Font, 128, 34, horizontalLogicalPadding: 12);
            var manageSearchToolsButtonSize = UiScaleHelper.GetButtonSize(this, _manageWebSearchToolsBtn.Text, _manageWebSearchToolsBtn.Font, 148, 30, horizontalLogicalPadding: 12);

            var cancelButtonSize = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 92, 34, horizontalLogicalPadding: 14);
            okBtn.MinimumSize = dialogButtonSize;
            okBtn.Size = dialogButtonSize;
            cancelBtn.MinimumSize = cancelButtonSize;
            cancelBtn.Size = cancelButtonSize;
            okBtn.Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 8), 0);
            cancelBtn.Margin = new Padding(0);
            _copyBookmarkletBtn.Size = copyBookmarkletButtonSize;
            _repairProtocolBtn.Size = repairProtocolButtonSize;
            _openAiSettingsBtn.Size = openAiSettingsButtonSize;
            _manageWebSearchToolsBtn.Size = manageSearchToolsButtonSize;

            buttonsRow.PerformLayout();

            var navWidth = UiScaleHelper.Scale(this, 156);
            bodyWithDivider.ColumnStyles[0].Width = navWidth;
            _navList.ItemHeight = UiScaleHelper.Scale(this, 40);
            navHeader.Height = UiScaleHelper.Scale(this, 44);
            navHeader.Padding = new Padding(UiScaleHelper.Scale(this, 18), 0, 0, 0);
            navBody.Padding = UiScaleHelper.ScalePadding(this, new Padding(8, 4, 8, 8));
            _navList.Invalidate();

            var labelWidth = UiScaleHelper.Scale(this, 88);
            var pathLabelScaled = UiScaleHelper.Scale(this, pathLabelWidth);
            foreach (var row in new[] { hotKeyRow, leftDragActionRow, searchToolsRow, gestureMetricsRow, ocrAkRow, ocrSkRow })
            {
                if (row.ColumnStyles.Count > 0 && row.ColumnStyles[0].SizeType == SizeType.Absolute)
                    row.ColumnStyles[0].Width = labelWidth;
                if (row == gestureMetricsRow && row.ColumnStyles.Count > 2)
                    row.ColumnStyles[2].Width = labelWidth;
            }

            foreach (var row in new[] { tcPathRow, dopusPathRow, everythingPathRow, openRow })
            {
                if (row.ColumnStyles.Count > 0 && row.ColumnStyles[0].SizeType == SizeType.Absolute)
                    row.ColumnStyles[0].Width = pathLabelScaled;
            }

            // NumericUpDown: keep height close to single-line text so digits stay centered.
            var numH = Math.Max(inputHeight, _gestureDistanceBox.PreferredSize.Height);
            _gestureDistanceBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 72), numH);
            _gestureDistanceBox.Height = numH;
            _gestureToleranceBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 72), numH);
            _gestureToleranceBox.Height = numH;

            // 剪贴板历史选项行：四个控件强制同一 Height + Label MiddleLeft，保证中线对齐
            var histMaxWidth = UiScaleHelper.Scale(this, 72);
            // NumericUpDown PreferredSize 才是原生框高；不要用文本 PreferredHeight 硬拔高
            var histNumH = Math.Max(
                _clipboardHistoryMaxBox.PreferredSize.Height,
                TextRenderer.MeasureText("88", _clipboardHistoryMaxBox.Font).Height + UiScaleHelper.Scale(this, 8));
            var histRowH = histNumH;

            _clipboardHistoryMaxBox.MinimumSize = new Size(histMaxWidth, histRowH);
            _clipboardHistoryMaxBox.MaximumSize = new Size(histMaxWidth, histRowH);
            _clipboardHistoryMaxBox.Size = new Size(histMaxWidth, histRowH);
            _clipboardHistoryMaxBox.Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 6), 0);

            // Label 与数字框同高 + 文字垂直居中（关键：AutoSize=false）
            var maxLabelW = TextRenderer.MeasureText(histMaxLabel.Text, histMaxLabel.Font).Width
                            + UiScaleHelper.Scale(this, 4);
            histMaxLabel.AutoSize = false;
            histMaxLabel.Size = new Size(maxLabelW, histRowH);
            histMaxLabel.TextAlign = ContentAlignment.MiddleLeft;
            histMaxLabel.Margin = new Padding(0, 0, UiScaleHelper.Scale(this, 8), 0);

            var unitLabelW = TextRenderer.MeasureText(histUnitLabel.Text, histUnitLabel.Font).Width
                             + UiScaleHelper.Scale(this, 4);
            histUnitLabel.AutoSize = false;
            histUnitLabel.Size = new Size(unitLabelW, histRowH);
            histUnitLabel.TextAlign = ContentAlignment.MiddleLeft;
            histUnitLabel.Margin = new Padding(0);

            var clearHistBtnSize = UiScaleHelper.GetButtonSize(
                this, _clipboardHistoryClearBtn.Text, _clipboardHistoryClearBtn.Font,
                minLogicalWidth: 88, minLogicalHeight: 28, horizontalLogicalPadding: 14,
                verticalLogicalPadding: 4);
            // 清空按钮高度也对齐同一行
            var clearH = histRowH;
            var clearW = clearHistBtnSize.Width;
            _clipboardHistoryClearBtn.MinimumSize = new Size(clearW, clearH);
            _clipboardHistoryClearBtn.MaximumSize = new Size(clearW, clearH);
            _clipboardHistoryClearBtn.Size = new Size(clearW, clearH);
            _clipboardHistoryClearBtn.Margin = new Padding(UiScaleHelper.Scale(this, 12), 0, 0, 0);

            histOptionsRow.MinimumSize = new Size(0, histRowH);
            histOptionsRow.Height = histRowH;
            histOptionsRow.PerformLayout();

            var contentPad = UiScaleHelper.Scale(this, 22) + UiScaleHelper.Scale(this, 22) + UiScaleHelper.Scale(this, 28);
            var contentWidth = Math.Max(
                UiScaleHelper.Scale(this, 400),
                ClientSize.Width - navWidth - contentPad);
            foreach (var hint in new[] { _websiteHintLabel, gestureHintLabel, aiHintLabel, _ocrHintLabel, histHint })
                hint.MaximumSize = new Size(contentWidth, 0);

            var bottomH = dialogButtonSize.Height + UiScaleHelper.Scale(this, 24);
            root.RowStyles[2].Height = bottomH;
            bottomBar.Padding = UiScaleHelper.ScalePadding(this, new Padding(16, 10, 16, 12));
            contentColumn.Padding = UiScaleHelper.ScalePadding(this, new Padding(22, 16, 22, 12));

            var pagePadding = UiScaleHelper.ScalePadding(this, new Padding(0, 8, 0, 4));
            foreach (var page in _pages)
                page.Padding = pagePadding;

            infoLabel.Height = Math.Max(
                UiScaleHelper.Scale(this, 24),
                TextRenderer.MeasureText(infoLabel.Text, infoLabel.Font).Height + UiScaleHelper.Scale(this, 6));

            var minW = UiScaleHelper.Scale(this, 720);
            var minH = UiScaleHelper.Scale(this, 520);
            MinimumSize = SizeFromClientSize(new Size(minW, minH));
            if (ClientSize.Width < minW || ClientSize.Height < minH)
                ClientSize = new Size(Math.Max(ClientSize.Width, minW), Math.Max(ClientSize.Height, minH));
        }

        ApplyScaledMetrics();
        DpiChanged += (_, _) => ApplyScaledMetrics();

        Text = $"Quickstart 设置  ·  v{productVersion}";
    }

    private static string GetProductVersion()
        => string.IsNullOrWhiteSpace(Application.ProductVersion)
            ? "未知"
            : Application.ProductVersion.Split('+')[0];

    private int _navHoverIndex = -1;

    private void OnNavDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var hovered = !selected && e.Index == _navHoverIndex;

        var bg = selected ? AccentSoft : hovered ? NavHover : BgNav;
        var fg = selected ? AccentText : TextSecondary;
        var font = selected
            ? new Font(UiFont, 9.5f, FontStyle.Bold)
            : new Font(UiFont, 9.5f, FontStyle.Regular);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillRectangle(new SolidBrush(BgNav), e.Bounds);

        var inset = UiScaleHelper.Scale(this, 4);
        var pill = Rectangle.Inflate(e.Bounds, -inset, -UiScaleHelper.Scale(this, 2));
        using (var path = CreateRoundRect(pill, UiScaleHelper.Scale(this, 8)))
        using (var brush = new SolidBrush(bg))
            e.Graphics.FillPath(brush, path);

        if (selected)
        {
            var barW = UiScaleHelper.Scale(this, 3);
            var barH = Math.Max(UiScaleHelper.Scale(this, 14), pill.Height - UiScaleHelper.Scale(this, 12));
            var bar = new Rectangle(
                pill.X + UiScaleHelper.Scale(this, 6),
                pill.Y + (pill.Height - barH) / 2,
                barW,
                barH);
            using var accentBrush = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accentBrush, bar);
        }

        var text = _navList.Items[e.Index]?.ToString() ?? string.Empty;
        var textPadL = UiScaleHelper.Scale(this, selected ? 18 : 14);
        var textRect = new Rectangle(
            pill.X + textPadL,
            pill.Y,
            pill.Width - textPadL - UiScaleHelper.Scale(this, 8),
            pill.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            font,
            textRect,
            fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        font.Dispose();
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DetectPath(Func<string?> detect, TextBox target, string productName, string notFoundMessage)
    {
        var detected = detect();
        if (detected != null)
        {
            target.Text = detected;
            DialogPresenter.ShowMessage(this, $"检测到 {productName}:\n{detected}", "自动检测",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            DialogPresenter.ShowMessage(this, notFoundMessage, "自动检测",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Length)
            return;

        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == index;

        if (index < _pagesMeta.Length)
        {
            _pageTitleLabel.Text = _pagesMeta[index].Title;
            _pageSubtitleLabel.Text = _pagesMeta[index].Subtitle;
        }
    }

    private static Panel CreatePagePanel(params Control[] controls)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < controls.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controls[i].Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(controls[i], 0, i);
        }

        var page = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BgContent,
            Padding = new Padding(0, 8, 0, 4),
            Visible = false
        };
        page.Controls.Add(layout);
        return page;
    }

    private static Control CreateCard(string title, params Control[] children)
        => CreateCard(title, compact: false, children);

    private static Control CreateCard(string title, bool compact, params Control[] children)
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = compact ? new Padding(14, 8, 14, 8) : new Padding(14, 12, 14, 12),
            BackColor = BgCard
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(UiFont, 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, compact ? 6 : 10)
        };
        body.Controls.Add(titleLabel, 0, 0);

        for (var i = 0; i < children.Length; i++)
        {
            var bottom = compact
                ? (i == children.Length - 1 ? 0 : 4)
                : Math.Max(children[i].Margin.Bottom, 6);
            children[i].Margin = children[i].Margin with { Bottom = bottom };
            body.Controls.Add(children[i], 0, i + 1);
        }

        // 外层带边框的壳
        var shell = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = BorderCard,
            Padding = new Padding(1),
            Margin = new Padding(0)
        };
        shell.Controls.Add(body);
        return shell;
    }

    private static TextBox CreateTextBox(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            // Keep native PreferredHeight (via FitSingleLineTextBox) so text is
            // vertically centered inside the border; forcing a taller height
            // makes single-line TextBox text look top-aligned and can clip edges.
            AutoSize = false,
            Margin = new Padding(0),
            Font = new Font(UiFont, 9f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = TextPrimary
        };

    private static CheckBox CreateCheckBox(string text, bool isChecked)
        => new()
        {
            Text = text,
            AutoSize = true,
            Checked = isChecked,
            ForeColor = TextPrimary,
            Font = new Font(UiFont, 9f),
            Margin = new Padding(0, 2, 0, 4),
            Cursor = Cursors.Hand
        };

    private static Label CreateHintLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font(UiFont, 8.5f),
            Margin = new Padding(0, 0, 0, 8)
        };

    private static NumericUpDown CreateNumericUpDown(decimal min, decimal max, decimal inc, decimal value)
        => new()
        {
            Minimum = min,
            Maximum = max,
            Increment = inc,
            Value = value,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Font = new Font(UiFont, 9f),
            BorderStyle = BorderStyle.FixedSingle
        };

    private static RoundedButton CreateBrowseButton()
    {
        var btn = new RoundedButton
        {
            Text = "…",
            Font = new Font(UiFont, 9f),
            Margin = new Padding(6, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(btn);
        return btn;
    }

    private static RoundedButton CreateDetectButton()
    {
        var btn = new RoundedButton
        {
            Text = "检测",
            Font = new Font(UiFont, 9f),
            Margin = new Padding(4, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(btn);
        return btn;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var hotKey = _hotKeyBox.Text.Trim();
        if (!GlobalHotKey.TryParse(hotKey, out _, out _, out var hotKeyError))
        {
            DialogPresenter.ShowMessage(
                this,
                hotKeyError,
                "快捷键格式不正确",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _navList.SelectedIndex = 1; // 快捷启动
            _hotKeyBox.Focus();
            _hotKeyBox.SelectAll();
            return;
        }

        var everythingPath = _everythingPathBox.Text.Trim();
        if (_leftDragActionBox.SelectedIndex == 1 && !File.Exists(everythingPath))
        {
            DialogPresenter.ShowMessage(
                this,
                "启用 Everything 左滑搜索前，请自动检测或手动指定有效的 Everything.exe。",
                "Everything 路径无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _navList.SelectedIndex = 0; // 常规
            _everythingPathBox.Focus();
            _everythingPathBox.SelectAll();
            return;
        }

        var config = _configManager.Config;
        config.TotalCommanderPath = _tcPathBox.Text.Trim();
        config.DirectoryOpusPath = _dopusPathBox.Text.Trim();
        config.EverythingPath = everythingPath;
        config.DefaultOpenWith = _openWithBox.SelectedIndex switch
        {
            2 => OpenWith.DirectoryOpus,
            1 => OpenWith.Explorer,
            _ => OpenWith.TotalCommander
        };
        config.HotKey = hotKey;
        config.RightDragEnabled = _rightDragCheck.Checked;
        config.LeftDragAction = _leftDragActionBox.SelectedIndex == 1
            ? LeftDragAction.EverythingSearch
            : LeftDragAction.AiActionPicker;
        config.RightDragTriggerDistance = Decimal.ToInt32(_gestureDistanceBox.Value);
        config.RightDragVerticalTolerance = Decimal.ToInt32(_gestureToleranceBox.Value);
        config.RememberLastView = _rememberLastViewCheck.Checked;
        config.SortByRecentUsage = _sortRecentCheck.Checked;

        config.Ocr ??= new OcrConfig();
        config.Ocr.Enabled = _ocrEnabledCheck.Checked;
        if (string.IsNullOrWhiteSpace(config.Ocr.Provider))
            config.Ocr.Provider = "baidu";
        if (string.IsNullOrWhiteSpace(config.Ocr.LanguageType))
            config.Ocr.LanguageType = "CHN_ENG";

        config.ClipboardHistory ??= new ClipboardHistoryConfig();
        config.ClipboardHistory.Enabled = _clipboardHistoryEnabledCheck.Checked;
        config.ClipboardHistory.Persist = _clipboardHistoryPersistCheck.Checked;
        config.ClipboardHistory.MaxItems = (int)_clipboardHistoryMaxBox.Value;

        try
        {
            var ak = _ocrApiKeyBox.Text.Trim();
            var sk = _ocrSecretKeyBox.Text.Trim();
            var keyChanged = false;
            if (!string.IsNullOrEmpty(ak))
            {
                AiSecretStore.SaveApiKey(OcrConfig.BaiduApiKeySecretId, ak);
                keyChanged = true;
            }

            if (!string.IsNullOrEmpty(sk))
            {
                AiSecretStore.SaveApiKey(OcrConfig.BaiduSecretKeySecretId, sk);
                keyChanged = true;
            }

            if (keyChanged)
                BaiduOcrClient.ClearTokenCache();
        }
        catch (Exception ex)
        {
            DialogPresenter.ShowMessage(
                this,
                $"OCR 密钥保存失败：{ex.Message}",
                "截图 OCR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _navList.SelectedIndex = 4; // 截图 OCR
            return;
        }

        var startupChanged = config.StartWithWindows != _startupCheck.Checked;
        config.StartWithWindows = _startupCheck.Checked;
        if (startupChanged)
            SetStartup(_startupCheck.Checked);

        var shellChanged = config.ShellMenuEnabled != _shellMenuCheck.Checked;
        config.ShellMenuEnabled = _shellMenuCheck.Checked;

        var exePath = Application.ExecutablePath;
        ShellIntegration.RegisterProtocol(exePath);
        if (_shellMenuCheck.Checked)
        {
            ShellIntegration.Register(exePath);
        }
        else if (shellChanged || ShellIntegration.IsRegistered())
        {
            ShellIntegration.Unregister();
        }

        _configManager.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;

            if (enable)
                key.SetValue("Quickstart", $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue("Quickstart", throwOnMissingValue: false);
        }
        catch { }
    }

    private static Label CreateFieldLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            // No Top/Bottom → TableLayoutPanel vertically centers in the row.
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextLabel,
            Font = new Font(UiFont, 9f),
            Margin = new Padding(0, 0, 10, 0)
        };

    private static TableLayoutPanel CreateLabeledFieldRow(
        string labelText,
        Control control,
        int labelWidth = 88,
        DockStyle controlDock = DockStyle.Fill)
    {
        control.Dock = DockStyle.None;
        control.Margin = new Padding(0);
        // Left|Right stretches horizontally; omit Top/Bottom so the control is
        // vertically centered in the TableLayout cell (same baseline as label).
        control.Anchor = controlDock == DockStyle.Fill
            ? AnchorStyles.Left | AnchorStyles.Right
            : AnchorStyles.Left;

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(CreateFieldLabel(labelText), 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private static TableLayoutPanel CreatePathFieldRow(
        string labelText,
        TextBox pathBox,
        Control browseBtn,
        Control detectBtn,
        int labelWidth = 112)
    {
        pathBox.Dock = DockStyle.None;
        pathBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pathBox.Margin = new Padding(0);

        browseBtn.Dock = DockStyle.None;
        browseBtn.Anchor = AnchorStyles.None;
        detectBtn.Dock = DockStyle.None;
        detectBtn.Anchor = AnchorStyles.None;

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.None,
            // Vertically center the … / 检测 buttons with the path box.
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        actions.Controls.Add(browseBtn);
        actions.Controls.Add(detectBtn);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(CreateFieldLabel(labelText), 0, 0);
        row.Controls.Add(pathBox, 1, 0);
        row.Controls.Add(actions, 2, 0);
        return row;
    }
}
