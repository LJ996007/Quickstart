namespace Quickstart.UI;

using Quickstart.Core;

public sealed class SettingsForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly TextBox _tcPathBox;
    private readonly ComboBox _openWithBox;
    private readonly CheckBox _startupCheck;
    private readonly CheckBox _shellMenuCheck;

    public SettingsForm(ConfigManager configManager)
    {
        _configManager = configManager;
        var config = configManager.Config;

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Quickstart 设置";
        ClientSize = new Size(580, 340);
        MinimumSize = new Size(620, 380);
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
            RowCount = 8,
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
            Margin = new Padding(0)
        };
        var tcBrowseBtn = new Button
        {
            Text = "...",
            Width = 44,
            Height = 30,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
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
            Width = 90,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(244, 246, 248),
            ForeColor = Color.FromArgb(55, 65, 81)
        };
        tcDetectBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        tcDetectBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 239, 243);
        tcDetectBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 229, 235);
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
        _openWithBox.Items.AddRange(["Total Commander", "资源管理器"]);
        _openWithBox.SelectedIndex = config.DefaultOpenWith == OpenWith.TotalCommander ? 0 : 1;

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
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        okBtn.FlatAppearance.BorderSize = 0;
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

        var tcPathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        tcPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tcPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tcPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tcPathRow.Controls.Add(_tcPathBox, 0, 0);
        tcPathRow.Controls.Add(tcBrowseBtn, 1, 0);
        tcPathRow.Controls.Add(tcDetectBtn, 2, 0);

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
        root.Controls.Add(openRow, 0, 2);
        root.Controls.Add(_startupCheck, 0, 3);
        root.Controls.Add(_shellMenuCheck, 0, 4);
        root.Controls.Add(buttonsRow, 0, 6);
        root.Controls.Add(infoLabel, 0, 7);

        Controls.Add(root);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var config = _configManager.Config;
        config.TotalCommanderPath = _tcPathBox.Text.Trim();
        config.DefaultOpenWith = _openWithBox.SelectedIndex == 0 ? OpenWith.TotalCommander : OpenWith.Explorer;

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
