namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

internal sealed class WebSearchToolsForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly DataGridView _grid;

    public WebSearchToolsForm(ConfigManager configManager)
    {
        _configManager = configManager;

        Text = "网页查询工具";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 420);
        MinimumSize = new Size(620, 340);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(12);
        BackColor = Color.FromArgb(248, 249, 251);
        FormStyler.ApplyRounded(this);

        var hint = new Label
        {
            Text = "网址模板必须包含 {query}，查询时会替换为经过 URL 编码的选中文字。",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "启用",
            Width = 56,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称",
            Width = 160,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "网址模板",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        LoadTools(_configManager.Config.WebSearchTools);

        var addButton = new RoundedButton { Text = "＋ 添加", Margin = new Padding(0, 0, 8, 0) };
        var deleteButton = new RoundedButton { Text = "删除", Margin = new Padding(0, 0, 8, 0) };
        var defaultsButton = new RoundedButton { Text = "恢复预置", Margin = new Padding(0) };
        var saveButton = new RoundedButton
        {
            Text = "保存",
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(8, 0, 0, 0)
        };
        var cancelButton = new RoundedButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0)
        };

        ButtonStyler.ApplySecondary(addButton);
        ButtonStyler.ApplyDangerSecondary(deleteButton);
        ButtonStyler.ApplySecondary(defaultsButton);
        ButtonStyler.ApplyPrimary(saveButton);
        ButtonStyler.ApplySecondary(cancelButton);
        saveButton.CornerRadius = FormStyler.StandardCornerRadius;

        addButton.Click += (_, _) =>
        {
            var index = _grid.Rows.Add(true, "新查询", "https://www.google.com/search?q={query}");
            _grid.Rows[index].Tag = Guid.NewGuid().ToString("N")[..8];
            _grid.CurrentCell = _grid.Rows[index].Cells[1];
            _grid.BeginEdit(selectAll: true);
        };
        deleteButton.Click += (_, _) =>
        {
            if (_grid.CurrentRow != null)
                _grid.Rows.Remove(_grid.CurrentRow);
        };
        defaultsButton.Click += (_, _) => LoadTools(WebSearchToolConfig.CreateDefaults());
        saveButton.Click += OnSave;

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var leftButtons = new FlowLayoutPanel
        {
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Left,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(248, 249, 251)
        };
        leftButtons.Controls.Add(addButton);
        leftButtons.Controls.Add(deleteButton);
        leftButtons.Controls.Add(defaultsButton);

        var rightButtons = new FlowLayoutPanel
        {
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Right,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(248, 249, 251)
        };
        rightButtons.Controls.Add(saveButton);
        rightButtons.Controls.Add(cancelButton);

        var buttonRow = new Panel
        {
            Dock = DockStyle.Bottom,
            BackColor = Color.FromArgb(248, 249, 251)
        };
        var footerSeparator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(225, 228, 232)
        };
        buttonRow.Controls.Add(leftButtons);
        buttonRow.Controls.Add(rightButtons);
        buttonRow.Controls.Add(footerSeparator);

        Controls.Add(_grid);
        Controls.Add(buttonRow);
        Controls.Add(hint);

        void ApplyScaledMetrics()
        {
            var buttonHeight = UiScaleHelper.Scale(this, 36);
            addButton.Size = new Size(UiScaleHelper.Scale(this, 92), buttonHeight);
            deleteButton.Size = new Size(UiScaleHelper.Scale(this, 84), buttonHeight);
            defaultsButton.Size = new Size(UiScaleHelper.Scale(this, 108), buttonHeight);
            cancelButton.Size = new Size(UiScaleHelper.Scale(this, 88), buttonHeight);
            saveButton.Size = new Size(UiScaleHelper.Scale(this, 96), buttonHeight);

            var footerPaddingTop = UiScaleHelper.Scale(this, 10);
            var footerPaddingBottom = UiScaleHelper.Scale(this, 8);
            buttonRow.Height = buttonHeight + footerPaddingTop + footerPaddingBottom + 1;
            buttonRow.Padding = new Padding(0, footerPaddingTop, 0, footerPaddingBottom);
            leftButtons.Size = new Size(
                addButton.Width + deleteButton.Width + defaultsButton.Width + UiScaleHelper.Scale(this, 16),
                buttonHeight);
            rightButtons.Size = new Size(
                cancelButton.Width + saveButton.Width + UiScaleHelper.Scale(this, 16),
                buttonHeight);
        }

        ApplyScaledMetrics();
        DpiChanged += (_, _) => ApplyScaledMetrics();
    }

    private void LoadTools(IEnumerable<WebSearchToolConfig> tools)
    {
        _grid.Rows.Clear();
        foreach (var tool in tools)
        {
            var index = _grid.Rows.Add(tool.Enabled, tool.Name, tool.UrlTemplate);
            _grid.Rows[index].Tag = tool.Id;
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        var tools = new List<WebSearchToolConfig>();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var name = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
            var template = Convert.ToString(row.Cells[2].Value)?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowValidationError(row, 1, "工具名称不能为空。");
                return;
            }

            if (!template.Contains("{query}", StringComparison.OrdinalIgnoreCase))
            {
                ShowValidationError(row, 2, "网址模板必须包含 {query}。");
                return;
            }

            var testUrl = template.Replace("{query}", "test", StringComparison.OrdinalIgnoreCase);
            if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                ShowValidationError(row, 2, "请输入有效的 HTTP 或 HTTPS 网址模板。");
                return;
            }

            tools.Add(new WebSearchToolConfig
            {
                Id = row.Tag as string ?? Guid.NewGuid().ToString("N")[..8],
                Enabled = row.Cells[0].Value is bool enabled && enabled,
                Name = name,
                UrlTemplate = template
            });
        }

        _configManager.Config.WebSearchTools = tools;
        _configManager.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowValidationError(DataGridViewRow row, int columnIndex, string message)
    {
        DialogPresenter.ShowMessage(
            this,
            message,
            "网页查询工具",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        _grid.CurrentCell = row.Cells[columnIndex];
        _grid.BeginEdit(selectAll: true);
    }
}
