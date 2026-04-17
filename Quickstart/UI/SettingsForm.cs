namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class SettingsForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly TextBox _tcPathBox;
    private readonly TextBox _dopusPathBox;
    private readonly ComboBox _openWithBox;
    private readonly CheckBox _startupCheck;
    private readonly CheckBox _shellMenuCheck;

    public SettingsForm(ConfigManager configManager)
    {
        _configManager = configManager;
        var config = configManager.Config;

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Quickstart 设置";
        ClientSize = new Size(580, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(14);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tcLabel = new Label
        {
            Text = "Total Commander 路径:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        _tcPathBox = new TextBox
        {
            Text = config.TotalCommanderPath,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        var tcBrowseBtn = new Button
        {
            Text = "...",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(tcBrowseBtn);
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

        var tcDetectBtn = new Button
        {
            Text = "自动检测",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(tcDetectBtn);
        tcDetectBtn.Click += (_, _) =>
        {
            var detected = TcDetector.Detect();
            if (detected != null)
            {
                _tcPathBox.Text = detected;
                MessageBox.Show($"检测到 Total Commander:\n{detected}", "自动检测",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("未检测到 Total Commander，请手动指定路径。", "自动检测",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var doLabel = new Label
        {
            Text = "Directory Opus 路径:",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8)
        };

        _dopusPathBox = new TextBox
        {
            Text = config.DirectoryOpusPath,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        var doBrowseBtn = new Button
        {
            Text = "...",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(doBrowseBtn);
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

        var doDetectBtn = new Button
        {
            Text = "自动检测",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(doDetectBtn);
        doDetectBtn.Click += (_, _) =>
        {
            var detected = DopusDetector.Detect();
            if (detected != null)
            {
                _dopusPathBox.Text = detected;
                MessageBox.Show($"检测到 Directory Opus:\n{detected}", "自动检测",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("未检测到 Directory Opus，请手动指定路径。", "自动检测",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var openLabel = new Label
        {
            Text = "默认打开方式:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0)
        };

        _openWithBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _openWithBox.Items.AddRange(["Total Commander", "资源管理器", "Directory Opus"]);
        _openWithBox.SelectedIndex = config.DefaultOpenWith switch
        {
            OpenWith.DirectoryOpus => 2,
            OpenWith.Explorer => 1,
            _ => 0
        };

        _startupCheck = new CheckBox
        {
            Text = "开机自动启动",
            AutoSize = true,
            Checked = config.StartWithWindows,
            Margin = new Padding(0, 8, 0, 0)
        };

        _shellMenuCheck = new CheckBox
        {
            Text = "在右键菜单中显示 \"添加到 Quickstart\"",
            AutoSize = true,
            Checked = config.ShellMenuEnabled,
            Margin = new Padding(0, 8, 0, 0)
        };

        var okBtn = new Button
        {
            Text = "保存",
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplyPrimary(okBtn);
        okBtn.Click += OnSave;

        var cancelBtn = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(cancelBtn);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart", "config.json");
        var infoLabel = new Label
        {
            Text = $"配置文件位置: {configPath}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Margin = new Padding(0, 10, 0, 0)
        };

        var pathActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        pathActions.Controls.Add(tcBrowseBtn);
        pathActions.Controls.Add(tcDetectBtn);

        var tcPathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        tcPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tcPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tcPathRow.Controls.Add(_tcPathBox, 0, 0);
        tcPathRow.Controls.Add(pathActions, 1, 0);

        var doPathActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        doPathActions.Controls.Add(doBrowseBtn);
        doPathActions.Controls.Add(doDetectBtn);

        var dopusPathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        dopusPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dopusPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dopusPathRow.Controls.Add(_dopusPathBox, 0, 0);
        dopusPathRow.Controls.Add(doPathActions, 1, 0);

        var openRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        openRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        openRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        openRow.Controls.Add(openLabel, 0, 0);
        openRow.Controls.Add(_openWithBox, 1, 0);

        var buttonsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0, 14, 0, 0)
        };
        buttonsRow.Controls.Add(cancelBtn);
        buttonsRow.Controls.Add(okBtn);

        root.Controls.Add(tcLabel, 0, 0);
        root.Controls.Add(tcPathRow, 0, 1);
        root.Controls.Add(doLabel, 0, 2);
        root.Controls.Add(dopusPathRow, 0, 3);
        root.Controls.Add(openRow, 0, 4);
        root.Controls.Add(_startupCheck, 0, 5);
        root.Controls.Add(_shellMenuCheck, 0, 6);
        root.Controls.Add(buttonsRow, 0, 8);
        root.Controls.Add(infoLabel, 0, 9);

        Controls.Add(root);

        void ApplyScaledMetrics()
        {
            Padding = UiScaleHelper.ScalePadding(this, new Padding(14));

            var inputHeight = UiScaleHelper.GetInputHeight(_tcPathBox, 30);
            var browseButtonSize = UiScaleHelper.GetButtonSize(this, tcBrowseBtn.Text, tcBrowseBtn.Font, 44, 30, horizontalLogicalPadding: 10);
            var detectButtonSize = UiScaleHelper.GetButtonSize(this, tcDetectBtn.Text, tcDetectBtn.Font, 96, 30, horizontalLogicalPadding: 12);
            var dialogButtonSize = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 84, 34, horizontalLogicalPadding: 12);

            _tcPathBox.MinimumSize = new Size(0, inputHeight);
            _dopusPathBox.MinimumSize = new Size(0, inputHeight);
            _openWithBox.MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(_openWithBox, 32));

            tcBrowseBtn.Size = new Size(browseButtonSize.Width, inputHeight);
            doBrowseBtn.Size = new Size(browseButtonSize.Width, inputHeight);
            tcDetectBtn.Size = new Size(detectButtonSize.Width, inputHeight);
            doDetectBtn.Size = new Size(detectButtonSize.Width, inputHeight);

            okBtn.Size = dialogButtonSize;
            cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 84, 34, horizontalLogicalPadding: 12);

            buttonsRow.Padding = new Padding(0, 0, UiScaleHelper.Scale(this, 10), 0);
            buttonsRow.Height = dialogButtonSize.Height + UiScaleHelper.Scale(this, 8);

            infoLabel.Height = Math.Max(
                UiScaleHelper.Scale(this, 24),
                TextRenderer.MeasureText(infoLabel.Text, infoLabel.Font).Height + UiScaleHelper.Scale(this, 6));

            root.PerformLayout();
            var preferred = root.GetPreferredSize(new Size(Math.Max(ClientSize.Width, UiScaleHelper.Scale(this, 580)), 0));
            var minClientWidth = Math.Max(UiScaleHelper.Scale(this, 620), preferred.Width);
            var minClientHeight = Math.Max(UiScaleHelper.Scale(this, 460), preferred.Height + UiScaleHelper.Scale(this, 8));
            MinimumSize = SizeFromClientSize(new Size(minClientWidth, minClientHeight));
            ClientSize = new Size(
                Math.Max(UiScaleHelper.Scale(this, 580), preferred.Width),
                Math.Max(UiScaleHelper.Scale(this, 420), preferred.Height));
        }

        ApplyScaledMetrics();
        DpiChanged += (_, _) => ApplyScaledMetrics();

        var exePath = Application.ExecutablePath;
        var buildTag = exePath.Contains("\\Debug\\", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : exePath.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase)
                ? "Release"
                : exePath.Contains("\\publish\\", StringComparison.OrdinalIgnoreCase)
                    ? "Publish"
                    : "Custom";
        Text = $"Quickstart 设置 [{buildTag}]";
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var config = _configManager.Config;
        config.TotalCommanderPath = _tcPathBox.Text.Trim();
        config.DirectoryOpusPath = _dopusPathBox.Text.Trim();
        config.DefaultOpenWith = _openWithBox.SelectedIndex switch
        {
            2 => OpenWith.DirectoryOpus,
            1 => OpenWith.Explorer,
            _ => OpenWith.TotalCommander
        };

        var startupChanged = config.StartWithWindows != _startupCheck.Checked;
        config.StartWithWindows = _startupCheck.Checked;
        if (startupChanged)
            SetStartup(_startupCheck.Checked);

        var shellChanged = config.ShellMenuEnabled != _shellMenuCheck.Checked;
        config.ShellMenuEnabled = _shellMenuCheck.Checked;
        if (shellChanged)
        {
            if (_shellMenuCheck.Checked)
            {
                var exePath = Application.ExecutablePath;
                ShellIntegration.Register(exePath);
            }
            else
            {
                ShellIntegration.Unregister();
            }
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
            {
                key.SetValue("Quickstart", $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue("Quickstart", throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
