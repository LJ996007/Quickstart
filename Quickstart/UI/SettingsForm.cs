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
        MinimumSize = new Size(620, 460);
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

        // TC Path
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
            Width = 44,
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

        // Keep textbox and path action buttons visually aligned across DPI scales.
        var pathRowHeight = Math.Max(_tcPathBox.PreferredHeight + 2, 30);
        _tcPathBox.MinimumSize = new Size(0, pathRowHeight);
        tcBrowseBtn.Size = new Size(44, pathRowHeight);
        var detectTextWidth = TextRenderer.MeasureText(tcDetectBtn.Text, tcDetectBtn.Font).Width;
        tcDetectBtn.Size = new Size(Math.Max(104, detectTextWidth + 24), pathRowHeight);

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

        // Directory Opus Path
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
            Width = 44,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(doBrowseBtn);
        _dopusPathBox.MinimumSize = new Size(0, pathRowHeight);
        doBrowseBtn.Size = new Size(44, pathRowHeight);
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
        doDetectBtn.Size = new Size(tcDetectBtn.Width, pathRowHeight);
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

        // Default open with
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

        // Start with Windows
        _startupCheck = new CheckBox
        {
            Text = "开机自动启动",
            AutoSize = true,
            Checked = config.StartWithWindows,
            Margin = new Padding(0, 8, 0, 0)
        };

        // Shell menu integration
        _shellMenuCheck = new CheckBox
        {
            Text = "在右键菜单中显示 \"添加到 Quickstart\"",
            AutoSize = true,
            Checked = config.ShellMenuEnabled,
            Margin = new Padding(0, 8, 0, 0)
        };

        // Buttons
        var okBtn = new Button
        {
            Text = "保存",
            Width = 84,
            Height = 34,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplyPrimary(okBtn);
        okBtn.Click += OnSave;

        var cancelBtn = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 84,
            Height = 34,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(cancelBtn);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        // Info label
        var infoLabel = new Label
        {
            Text = $"配置文件位置: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart", "config.json")}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 22,
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
            Height = 40,
            Margin = new Padding(0, 14, 0, 0),
            Padding = new Padding(0, 0, 10, 0)
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

        // Handle startup
        var startupChanged = config.StartWithWindows != _startupCheck.Checked;
        config.StartWithWindows = _startupCheck.Checked;
        if (startupChanged)
            SetStartup(_startupCheck.Checked);

        // Handle shell menu
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
