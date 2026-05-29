namespace Quickstart.UI;

using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Utils;

public sealed class AiPopup : Form
{
    private static readonly Size PopupLogicalSize = new(620, 640);
    private static readonly Size MinimumPopupLogicalSize = new(480, 420);

    private readonly ConfigManager _configManager;
    private readonly AiInputCaptureService _captureService;
    private readonly AiFileContentReader _fileReader;
    private readonly AiClient _aiClient;
    private readonly SkillRunner _skillRunner;

    private readonly ComboBox _modeBox;
    private readonly ComboBox _actionBox;
    private readonly TextBox _inputBox;
    private readonly RichTextBox _resultBox;
    private readonly Label _statusLabel;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Button _copyButton;
    private readonly Button _settingsButton;
    private readonly Button _closeButton;
    private string _resultMarkdown = string.Empty;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _captureCts;
    private bool _showWithoutActivation;

    public event Action<Form?>? ShowAiSettings;

    public AiPopup(
        ConfigManager configManager,
        AiInputCaptureService captureService,
        AiFileContentReader fileReader,
        AiClient aiClient)
    {
        _configManager = configManager;
        _captureService = captureService;
        _fileReader = fileReader;
        _aiClient = aiClient;
        _skillRunner = new SkillRunner(aiClient);

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = PopupLogicalSize;
        MinimumSize = MinimumPopupLogicalSize;
        BackColor = Color.FromArgb(250, 250, 250);
        TopMost = true;
        Padding = new Padding(1);
        SetStyle(ControlStyles.ResizeRedraw, true);
        FormStyler.ApplyRounded(this);

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 250, 250),
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10)
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleLabel = new Label
        {
            Text = "AI 助手",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            Margin = new Padding(0, 2, 0, 0)
        };
        titleRow.Controls.Add(titleLabel, 0, 0);
        _closeButton = CreateCloseButton();
        _closeButton.Click += (_, _) => Hide();
        titleRow.Controls.Add(_closeButton, 1, 0);
        InstallTitleDragHandlers(titleRow, titleLabel);

        _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _modeBox.Items.AddRange(["Prompt", "Skill"]);
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectedIndexChanged += (_, _) => RefreshActions();

        _actionBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

        var selectorRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectorRow.Controls.Add(CreateInlineLabel("类型"), 0, 0);
        selectorRow.Controls.Add(_modeBox, 1, 0);
        selectorRow.Controls.Add(CreateInlineLabel("动作"), 2, 0);
        selectorRow.Controls.Add(_actionBox, 3, 0);

        _inputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "选中的文本或文件内容会出现在这里，也可以手动输入。"
        };

        _resultBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(32, 33, 36),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            DetectUrls = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0),
            HideSelection = false
        };

        _statusLabel = new Label
        {
            Text = "就绪",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        _runButton = new RoundedButton { Text = "运行", Margin = new Padding(0, 0, 8, 0) };
        _cancelButton = new RoundedButton { Text = "取消", Enabled = false, Margin = new Padding(0, 0, 8, 0) };
        _copyButton = new RoundedButton { Text = "复制结果", Margin = new Padding(0, 0, 8, 0) };
        _settingsButton = new RoundedButton { Text = "AI 设置", Margin = new Padding(0, 0, 8, 0) };
        ButtonStyler.ApplyPrimary(_runButton);
        ButtonStyler.ApplySecondary(_cancelButton);
        ButtonStyler.ApplySecondary(_copyButton);
        ButtonStyler.ApplySecondary(_settingsButton);
        _runButton.Click += async (_, _) => await RunSelectedActionAsync(showInputWarning: true);
        _cancelButton.Click += (_, _) => _runCts?.Cancel();
        _copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_resultMarkdown))
                Clipboard.SetText(_resultMarkdown);
        };
        _settingsButton.Click += (_, _) => ShowAiSettings?.Invoke(this);

        var buttonsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonsRow.Controls.Add(_statusLabel, 0, 0);

        var buttonFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        buttonFlow.Controls.Add(_settingsButton);
        buttonFlow.Controls.Add(_copyButton);
        buttonFlow.Controls.Add(_cancelButton);
        buttonFlow.Controls.Add(_runButton);
        buttonsRow.Controls.Add(buttonFlow, 1, 0);

        inner.Controls.Add(titleRow, 0, 0);
        inner.Controls.Add(selectorRow, 0, 1);
        inner.Controls.Add(CreateLabeledBox("输入", _inputBox), 0, 2);
        inner.Controls.Add(new Label { Height = 8, Dock = DockStyle.Fill }, 0, 3);
        inner.Controls.Add(CreateLabeledBox("结果", _resultBox), 0, 4);
        inner.Controls.Add(buttonsRow, 0, 5);
        Controls.Add(inner);
        InstallResizeHandlers(inner);

        ApplyScaledMetrics();
        DpiChanged += (_, _) => ApplyScaledMetrics();
    }

    public void ShowAtGesturePoint(Point screenPt, IntPtr captureSourceWindow)
        => ShowInternal(screenPt, captureSourceWindow, selectedAction: null, autoRun: false);

    internal void ShowForSelectedAction(Point screenPt, IntPtr captureSourceWindow, AiActionSelection selectedAction, bool autoRun)
        => ShowInternal(screenPt, captureSourceWindow, selectedAction, autoRun);

    private void ShowInternal(Point screenPt, IntPtr captureSourceWindow, AiActionSelection? selectedAction, bool autoRun)
    {
        RefreshSelectors();
        ApplyActionSelection(selectedAction);
        PreparePendingCapture();

        var screen = Screen.FromPoint(screenPt);
        EnsurePopupSizeForScreen(screen);
        var workingArea = screen.WorkingArea;
        var margin = UiScaleHelper.Scale(this, 8);
        var x = Math.Max(workingArea.Left + margin, Math.Min(screenPt.X - Width + margin, workingArea.Right - Width - margin));
        var y = Math.Max(workingArea.Top + margin, Math.Min(screenPt.Y - margin, workingArea.Bottom - Height - margin));
        Location = new Point(x, y);

        _showWithoutActivation = true;
        if (!Visible)
            Show();
        else
            Invalidate();
        Update();
        _showWithoutActivation = false;

        var captureCts = _captureCts;
        if (captureCts != null)
            BeginInvoke(new Action(() => _ = CaptureInputAsync(captureCts, captureSourceWindow, autoRun)));
    }

    public void RefreshSelectors()
    {
        RefreshActions();
    }

    private void PreparePendingCapture()
    {
        _captureCts?.Cancel();
        _captureCts = new CancellationTokenSource();
        _inputBox.Clear();
        _inputBox.Modified = false;
        SetResultMarkdown(string.Empty);
        _statusLabel.Text = "正在读取选中内容...";
    }

    private async Task CaptureInputAsync(CancellationTokenSource captureCts, IntPtr captureSourceWindow, bool autoRun)
    {
        var token = captureCts.Token;
        try
        {
            await WaitForRightButtonReleaseAsync(token);
            token.ThrowIfCancellationRequested();

            await ActivateCaptureSourceAsync(captureSourceWindow, token);
            token.ThrowIfCancellationRequested();

            var captured = await _captureService.CaptureSelectionAsync(token);
            token.ThrowIfCancellationRequested();

            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(captured.Warning))
                warnings.Add(captured.Warning);

            var inputText = string.Empty;
            if (captured.Kind == AiCapturedInputKind.Text)
            {
                inputText = captured.Text;
            }
            else if (captured.Kind == AiCapturedInputKind.Files)
            {
                _statusLabel.Text = "正在读取文件内容...";
                var maxFileBytes = _configManager.Config.Ai.MaxFileBytes;
                var fileResult = await Task.Run(() => _fileReader.ReadFiles(captured.FilePaths, maxFileBytes), token);
                token.ThrowIfCancellationRequested();
                inputText = fileResult.Text;
                warnings.AddRange(fileResult.Warnings);
            }

            if (!Visible || IsDisposed)
                return;

            if (!_inputBox.Modified || string.IsNullOrWhiteSpace(_inputBox.Text))
            {
                _inputBox.Text = inputText;
                _inputBox.Modified = false;
            }

            _statusLabel.Text = warnings.Count == 0
                ? "已读取选中内容"
                : string.Join("  ", warnings);

            if (autoRun && !string.IsNullOrWhiteSpace(_inputBox.Text))
            {
                await RunSelectedActionAsync(showInputWarning: false);
                return;
            }

            ActivateAfterCapture();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!Visible || IsDisposed)
                return;

            _statusLabel.Text = $"自动捕获失败：{ex.Message}";
            ActivateAfterCapture();
        }
        finally
        {
            if (ReferenceEquals(_captureCts, captureCts))
                _captureCts = null;

            captureCts.Dispose();
        }
    }

    private static async Task WaitForRightButtonReleaseAsync(CancellationToken token)
    {
        const int maxWaitMs = 500;
        const int stepMs = 20;

        for (var waited = 0; waited < maxWaitMs; waited += stepMs)
        {
            if ((Control.MouseButtons & MouseButtons.Right) == 0)
                return;

            await Task.Delay(stepMs, token);
        }
    }

    private async Task ActivateCaptureSourceAsync(IntPtr sourceWindow, CancellationToken token)
    {
        if (sourceWindow == IntPtr.Zero || sourceWindow == Handle || !IsWindow(sourceWindow))
            return;

        SetForegroundWindow(sourceWindow);
        await Task.Delay(80, token);
    }

    private void ActivateAfterCapture()
    {
        if (!Visible || IsDisposed)
            return;

        _showWithoutActivation = false;
        Activate();
        _inputBox.Focus();
        if (!string.IsNullOrEmpty(_inputBox.Text))
            _inputBox.SelectAll();
    }

    private async Task RunSelectedActionAsync(bool showInputWarning)
    {
        var input = _inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            if (showInputWarning)
                DialogPresenter.ShowMessage(this, "请输入或选中要分析的内容。", "AI 助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var config = _configManager.Config.Ai;
        var provider = config.Providers.FirstOrDefault(item => item.Id == config.CurrentProviderId)
            ?? config.Providers.FirstOrDefault();
        if (provider == null)
        {
            DialogPresenter.ShowMessage(this, "请先配置 AI Provider。", "AI 助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetRunning(true);
        SetResultMarkdown(string.Empty);
        _runCts = new CancellationTokenSource();

        try
        {
            var model = provider.DefaultModel.Trim();
            string output;
            if (_modeBox.SelectedIndex == 1)
            {
                if (_actionBox.SelectedItem is not AiSkill skill)
                    throw new InvalidOperationException("请选择 Skill。");

                var skillProvider = string.IsNullOrWhiteSpace(skill.ProviderId)
                    ? provider
                    : config.Providers.FirstOrDefault(item => item.Id == skill.ProviderId) ?? provider;
                var skillModel = string.IsNullOrWhiteSpace(skill.Model) ? model : skill.Model;
                output = await _skillRunner.RunSkillAsync(
                    config,
                    skill,
                    skillProvider,
                    skillModel,
                    input,
                    new Progress<SkillRunProgress>(progress => _statusLabel.Text = $"{progress.Message} ({progress.CurrentStep}/{progress.TotalSteps})"),
                    _runCts.Token);
            }
            else
            {
                if (_actionBox.SelectedItem is not AiPromptPreset prompt)
                    throw new InvalidOperationException("请选择 Prompt。");

                _statusLabel.Text = "正在请求 AI...";
                output = await _skillRunner.RunPromptAsync(provider, model, prompt, input, _runCts.Token);
            }

            SetResultMarkdown(output);
            _statusLabel.Text = "完成";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "已取消";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "失败";
            SetResultMarkdown($"```text\r\n{ex.Message}\r\n```");
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        _runButton.Enabled = !running;
        _cancelButton.Enabled = running;
        _modeBox.Enabled = !running;
        _actionBox.Enabled = !running;
    }

    private void RefreshActions()
    {
        var config = _configManager.Config.Ai;
        _actionBox.Items.Clear();
        if (_modeBox.SelectedIndex == 1)
        {
            foreach (var skill in config.Skills)
                _actionBox.Items.Add(skill);
            var index = config.Skills.FindIndex(skill => skill.Id == config.DefaultSkillId);
            _actionBox.SelectedIndex = index >= 0 ? index : (_actionBox.Items.Count > 0 ? 0 : -1);
        }
        else
        {
            foreach (var prompt in config.PromptPresets)
                _actionBox.Items.Add(prompt);
            var index = config.PromptPresets.FindIndex(prompt => prompt.Id == config.DefaultPromptId);
            _actionBox.SelectedIndex = index >= 0 ? index : (_actionBox.Items.Count > 0 ? 0 : -1);
        }
    }

    private void ApplyActionSelection(AiActionSelection? selection)
    {
        if (selection == null)
            return;

        _modeBox.SelectedIndex = selection.IsSkill ? 1 : 0;
        RefreshActions();

        for (var i = 0; i < _actionBox.Items.Count; i++)
        {
            var matches = selection.Kind switch
            {
                AiActionKind.Prompt => _actionBox.Items[i] is AiPromptPreset prompt && prompt.Id == selection.Id,
                AiActionKind.Skill => _actionBox.Items[i] is AiSkill skill && skill.Id == selection.Id,
                _ => false
            };

            if (matches)
            {
                _actionBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void SetResultMarkdown(string markdown)
    {
        _resultMarkdown = markdown;
        RenderMarkdownToResultBox(markdown);
    }

    private void RenderMarkdownToResultBox(string markdown)
    {
        _resultBox.SuspendLayout();
        _resultBox.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            AppendResultLine("AI 输出会显示在这里。", Color.FromArgb(138, 143, 152), italic: true);
            _resultBox.SelectionStart = 0;
            _resultBox.ResumeLayout();
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCode = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                AppendResultLine(line, Color.FromArgb(35, 35, 35), mono: true, backColor: Color.FromArgb(245, 246, 248));
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                AppendResultLine(trimmed[4..].Trim(), Color.FromArgb(31, 41, 55), bold: true, fontSize: 11f);
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                AppendResultLine(trimmed[3..].Trim(), Color.FromArgb(31, 41, 55), bold: true, fontSize: 12f);
            else if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                AppendResultLine(trimmed[2..].Trim(), Color.FromArgb(31, 41, 55), bold: true, fontSize: 13f);
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                AppendResultLine("│ " + StripInlineMarkdown(trimmed[2..].Trim()), Color.FromArgb(75, 85, 99));
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                AppendResultLine("• " + StripInlineMarkdown(trimmed[2..].Trim()), Color.FromArgb(32, 33, 36));
            else if (TryParseOrderedList(trimmed, out var orderedText))
                AppendResultLine(orderedText, Color.FromArgb(32, 33, 36));
            else
                AppendResultLine(StripInlineMarkdown(line), Color.FromArgb(32, 33, 36));
        }

        _resultBox.SelectionStart = 0;
        _resultBox.SelectionLength = 0;
        _resultBox.ResumeLayout();
    }

    private void AppendResultLine(
        string text,
        Color color,
        bool bold = false,
        bool italic = false,
        bool mono = false,
        float fontSize = 9.5f,
        Color? backColor = null)
    {
        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;

        _resultBox.SelectionStart = _resultBox.TextLength;
        _resultBox.SelectionLength = 0;
        _resultBox.SelectionColor = color;
        _resultBox.SelectionBackColor = backColor ?? _resultBox.BackColor;
        _resultBox.SelectionFont = new Font(mono ? "Consolas" : "Microsoft YaHei UI", fontSize, style);
        _resultBox.AppendText(text + Environment.NewLine);
        _resultBox.SelectionBackColor = _resultBox.BackColor;
    }

    private static bool TryParseOrderedList(string text, out string orderedText)
    {
        orderedText = string.Empty;
        var dotIndex = text.IndexOf(". ", StringComparison.Ordinal);
        if (dotIndex <= 0 || !text[..dotIndex].All(char.IsDigit))
            return false;

        orderedText = text[..(dotIndex + 1)] + " " + StripInlineMarkdown(text[(dotIndex + 2)..].Trim());
        return true;
    }

    private static string StripInlineMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal);
    }

    private void ApplyScaledMetrics()
    {
        Padding = UiScaleHelper.ScalePadding(this, new Padding(1));
        _runButton.Size = UiScaleHelper.GetButtonSize(this, _runButton.Text, _runButton.Font, 76, 32, horizontalLogicalPadding: 12);
        _cancelButton.Size = UiScaleHelper.GetButtonSize(this, _cancelButton.Text, _cancelButton.Font, 76, 32, horizontalLogicalPadding: 12);
        _copyButton.Size = UiScaleHelper.GetButtonSize(this, _copyButton.Text, _copyButton.Font, 96, 32, horizontalLogicalPadding: 12);
        _settingsButton.Size = UiScaleHelper.GetButtonSize(this, _settingsButton.Text, _settingsButton.Font, 92, 32, horizontalLogicalPadding: 12);
        _closeButton.Size = new Size(UiScaleHelper.Scale(this, 30), UiScaleHelper.Scale(this, 30));

        if (Visible)
            EnsurePopupSizeForScreen(Screen.FromPoint(Location));
    }

    private void EnsurePopupSizeForScreen(Screen screen)
    {
        var margin = UiScaleHelper.Scale(this, 8);
        var preferred = UiScaleHelper.ScaleSize(this, PopupLogicalSize);
        var minimum = UiScaleHelper.ScaleSize(this, MinimumPopupLogicalSize);
        Size = new Size(
            Math.Min(Math.Max(preferred.Width, minimum.Width), screen.WorkingArea.Width - margin * 2),
            Math.Min(Math.Max(preferred.Height, minimum.Height), screen.WorkingArea.Height - margin * 2));
    }

    private static Label CreateInlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 6, 0)
    };

    private static Control CreateLabeledBox(string labelText, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            ForeColor = Color.FromArgb(80, 80, 80)
        }, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _captureCts?.Cancel();
        _runCts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
            _captureCts?.Cancel();

        base.OnVisibleChanged(e);
    }

    protected override bool ShowWithoutActivation => _showWithoutActivation;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WM_NCHITTEST || m.Result != (IntPtr)HTCLIENT)
            return;

        var screenPoint = GetPointFromLParam(m.LParam);
        var clientPoint = PointToClient(screenPoint);
        if (!ClientRectangle.Contains(clientPoint))
            return;

        var resizeGrip = UiScaleHelper.Scale(this, 8);
        var onLeft = clientPoint.X <= resizeGrip;
        var onRight = clientPoint.X >= ClientSize.Width - resizeGrip;
        var onTop = clientPoint.Y <= resizeGrip;
        var onBottom = clientPoint.Y >= ClientSize.Height - resizeGrip;

        if (onTop && onLeft)
            m.Result = (IntPtr)HTTOPLEFT;
        else if (onTop && onRight)
            m.Result = (IntPtr)HTTOPRIGHT;
        else if (onBottom && onLeft)
            m.Result = (IntPtr)HTBOTTOMLEFT;
        else if (onBottom && onRight)
            m.Result = (IntPtr)HTBOTTOMRIGHT;
        else if (onLeft)
            m.Result = (IntPtr)HTLEFT;
        else if (onRight)
            m.Result = (IntPtr)HTRIGHT;
        else if (onTop)
            m.Result = (IntPtr)HTTOP;
        else if (onBottom)
            m.Result = (IntPtr)HTBOTTOM;
        else if (IsInDraggableTitleArea(screenPoint, clientPoint))
            m.Result = (IntPtr)HTCAPTION;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var pen = new Pen(Color.FromArgb(220, 220, 220));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private bool IsInDraggableTitleArea(Point screenPoint, Point clientPoint)
    {
        if (_closeButton.RectangleToScreen(_closeButton.ClientRectangle).Contains(screenPoint))
            return false;

        return clientPoint.Y <= UiScaleHelper.Scale(this, 44);
    }

    private void InstallTitleDragHandlers(params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                    return;

                BeginSystemMove();
            };
            control.Cursor = Cursors.SizeAll;
        }
    }

    private void InstallResizeHandlers(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (!IsInteractiveControl(child))
            {
                child.MouseMove += (_, e) =>
                {
                    var hitTest = GetResizeHitTest(child.PointToScreen(e.Location));
                    child.Cursor = hitTest == HTCLIENT ? Cursors.Default : GetResizeCursor(hitTest);
                };
                child.MouseLeave += (_, _) => child.Cursor = Cursors.Default;
                child.MouseDown += (_, e) =>
                {
                    if (e.Button != MouseButtons.Left)
                        return;

                    var hitTest = GetResizeHitTest(child.PointToScreen(e.Location));
                    if (hitTest != HTCLIENT)
                        BeginSystemResize(hitTest);
                };
            }

            InstallResizeHandlers(child);
        }
    }

    private int GetResizeHitTest(Point screenPoint)
    {
        var clientPoint = PointToClient(screenPoint);
        if (!ClientRectangle.Contains(clientPoint))
            return HTCLIENT;

        var resizeGrip = UiScaleHelper.Scale(this, 8);
        var onLeft = clientPoint.X <= resizeGrip;
        var onRight = clientPoint.X >= ClientSize.Width - resizeGrip;
        var onTop = clientPoint.Y <= resizeGrip;
        var onBottom = clientPoint.Y >= ClientSize.Height - resizeGrip;

        if (onTop && onLeft)
            return HTTOPLEFT;
        if (onTop && onRight)
            return HTTOPRIGHT;
        if (onBottom && onLeft)
            return HTBOTTOMLEFT;
        if (onBottom && onRight)
            return HTBOTTOMRIGHT;
        if (onLeft)
            return HTLEFT;
        if (onRight)
            return HTRIGHT;
        if (onTop)
            return HTTOP;
        if (onBottom)
            return HTBOTTOM;

        return HTCLIENT;
    }

    private static bool IsInteractiveControl(Control control)
        => control is TextBoxBase
            or ComboBox
            or Button
            or ListControl
            or TabControl;

    private static Cursor GetResizeCursor(int hitTest)
        => hitTest switch
        {
            HTLEFT or HTRIGHT => Cursors.SizeWE,
            HTTOP or HTBOTTOM => Cursors.SizeNS,
            HTTOPLEFT or HTBOTTOMRIGHT => Cursors.SizeNWSE,
            HTTOPRIGHT or HTBOTTOMLEFT => Cursors.SizeNESW,
            _ => Cursors.Default
        };

    private void BeginSystemMove()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void BeginSystemResize(int hitTest)
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
    }

    private static Point GetPointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new Point(x, y);
    }

    private static Button CreateCloseButton()
    {
        var button = new Button
        {
            Text = "×",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f, FontStyle.Regular),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(250, 250, 250),
            ForeColor = Color.FromArgb(60, 60, 60),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 235);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 225, 225);
        return button;
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}
