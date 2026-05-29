namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class AiProviderEditForm : Form
{
    private readonly AiProviderConfig _provider;
    private readonly TextBox _nameBox;
    private readonly TextBox _baseUrlBox;
    private readonly ComboBox _modelListBox;
    private readonly List<string> _models;
    private readonly TextBox _apiKeyBox;
    private readonly ComboBox _deepSeekThinkingBox;
    private readonly NumericUpDown _timeoutBox;
    private readonly ToolTip _toolTip = new();

    public AiProviderEditForm(AiProviderConfig provider)
    {
        _provider = provider;

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = string.IsNullOrWhiteSpace(provider.Name) ? "添加 Provider" : "编辑 Provider";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = UiScaleHelper.ScalePadding(this, new Padding(14));
        ClientSize = new Size(640, 440);
        MinimumSize = SizeFromClientSize(new Size(580, 400));
        FormStyler.ApplyRounded(this);

        _nameBox = CreateTextBox(provider.Name);
        _baseUrlBox = CreateTextBox(provider.BaseUrl);
        _models = BuildInitialModels(provider);
        _modelListBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0),
            MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(this, 32))
        };
        RefreshModelList(provider.DefaultModel);

        _apiKeyBox = CreateTextBox(string.Empty);
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.PlaceholderText = !AiSecretStore.HasApiKey(provider) && string.IsNullOrWhiteSpace(provider.ApiKeyProtected)
            ? "输入 API Key"
            : "留空保持当前 API Key";

        _deepSeekThinkingBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0)
        };
        _deepSeekThinkingBox.Items.AddRange(["默认", "关闭", "高", "最大"]);
        _deepSeekThinkingBox.SelectedIndex = provider.DeepSeekThinkingEffort switch
        {
            "disabled" => 1,
            "high" => 2,
            "max" => 3,
            _ => 0
        };

        _timeoutBox = new NumericUpDown
        {
            Minimum = 10,
            Maximum = 300,
            Value = Math.Clamp(provider.TimeoutSeconds <= 0 ? 60 : provider.TimeoutSeconds, 10, 300),
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, 58)));

        var header = new Label
        {
            Text = "Provider 配置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 35, 35),
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 12))
        };
        root.Controls.Add(header, 0, 0);

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(CreateField("名称", _nameBox));
        fields.Controls.Add(CreateField("Base URL", _baseUrlBox));
        fields.Controls.Add(CreateModelField());
        fields.Controls.Add(CreateField("API Key", _apiKeyBox));
        fields.Controls.Add(CreateTwoColumnField(
            "DeepSeek 思考程度（仅 DeepSeek 生效）",
            _deepSeekThinkingBox,
            "超时秒数",
            _timeoutBox));
        contentPanel.Controls.Add(fields);
        root.Controls.Add(contentPanel, 0, 1);

        var okBtn = CreateDialogButton("保存", primary: true);
        var cancelBtn = CreateDialogButton("取消", primary: false);
        cancelBtn.DialogResult = DialogResult.Cancel;
        okBtn.Click += OnSave;
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        var buttons = CreateFooter();
        buttons.Controls.Add(cancelBtn);
        buttons.Controls.Add(okBtn);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);

        _nameBox.TextChanged += (_, _) => UpdateDeepSeekThinkingState();
        _baseUrlBox.TextChanged += (_, _) => UpdateDeepSeekThinkingState();
        UpdateDeepSeekThinkingState();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text)
            || string.IsNullOrWhiteSpace(_baseUrlBox.Text)
            || _modelListBox.SelectedItem is not string selectedModel
            || string.IsNullOrWhiteSpace(selectedModel))
        {
            DialogPresenter.ShowMessage(this, "名称、Base URL 和默认模型都不能为空。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _provider.Name = _nameBox.Text.Trim();
        _provider.BaseUrl = _baseUrlBox.Text.Trim();
        _provider.DefaultModel = selectedModel.Trim();
        _provider.Models = _models
            .Select(model => model.Trim())
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_provider.Models.All(model => !string.Equals(model, _provider.DefaultModel, StringComparison.OrdinalIgnoreCase)))
            _provider.Models.Insert(0, _provider.DefaultModel);

        if (!string.IsNullOrWhiteSpace(_apiKeyBox.Text))
        {
            try
            {
                AiSecretStore.SaveApiKey(_provider.Id, _apiKeyBox.Text.Trim());
                _provider.ApiKeyProtected = string.Empty;
            }
            catch (Exception ex)
            {
                DialogPresenter.ShowMessage(this, $"API Key 保存失败：{ex.Message}", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.None;
                return;
            }
        }

        _provider.DeepSeekThinkingEffort = _deepSeekThinkingBox.SelectedIndex switch
        {
            1 => "disabled",
            2 => "high",
            3 => "max",
            _ => string.Empty
        };
        _provider.TimeoutSeconds = (int)_timeoutBox.Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateDeepSeekThinkingState()
    {
        var isDeepSeek = _nameBox.Text.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || _baseUrlBox.Text.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
        _deepSeekThinkingBox.Enabled = isDeepSeek;
    }

    private void AddModel()
    {
        var model = PromptForModelName("添加模型", string.Empty);
        if (model == null)
            return;

        if (ContainsModel(model))
        {
            DialogPresenter.ShowMessage(this, "模型名称已存在。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _models.Add(model);
        RefreshModelList(model);
    }

    private void RenameSelectedModel()
    {
        if (_modelListBox.SelectedItem is not string currentModel)
            return;

        var model = PromptForModelName("重命名模型", currentModel);
        if (model == null || string.Equals(model, currentModel, StringComparison.Ordinal))
            return;

        if (_models.Any(existing => !string.Equals(existing, currentModel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing, model, StringComparison.OrdinalIgnoreCase)))
        {
            DialogPresenter.ShowMessage(this, "模型名称已存在。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var index = _models.FindIndex(existing => string.Equals(existing, currentModel, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _models[index] = model;
        RefreshModelList(model);
    }

    private void DeleteSelectedModel()
    {
        if (_modelListBox.SelectedItem is not string currentModel)
            return;

        if (_models.Count <= 1)
        {
            DialogPresenter.ShowMessage(this, "至少保留一个模型。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var index = _models.FindIndex(existing => string.Equals(existing, currentModel, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        _models.RemoveAt(index);
        var nextIndex = Math.Clamp(index, 0, _models.Count - 1);
        RefreshModelList(_models[nextIndex]);
    }

    private bool ContainsModel(string model)
        => _models.Any(existing => string.Equals(existing, model, StringComparison.OrdinalIgnoreCase));

    private void RefreshModelList(string? selectedModel = null)
    {
        _modelListBox.Items.Clear();
        foreach (var model in _models)
            _modelListBox.Items.Add(model);

        if (_modelListBox.Items.Count == 0)
            return;

        var selectedIndex = _models.FindIndex(model =>
            string.Equals(model, selectedModel, StringComparison.OrdinalIgnoreCase));
        _modelListBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private static List<string> BuildInitialModels(AiProviderConfig provider)
    {
        var models = (provider.Models ?? [])
            .Select(model => model.Trim())
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(provider.DefaultModel)
            && models.All(model => !string.Equals(model, provider.DefaultModel, StringComparison.OrdinalIgnoreCase)))
        {
            models.Insert(0, provider.DefaultModel.Trim());
        }

        if (models.Count == 0)
            models.Add("model-name");

        return models;
    }

    private string? PromptForModelName(string title, string initialValue)
    {
        using var form = new ModelNameEditForm(title, initialValue);
        return DialogPresenter.ShowModal(form, this) == DialogResult.OK
            ? form.ModelName
            : null;
    }

    private TextBox CreateTextBox(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(this, 32))
    };

    private Control CreateField(string labelText, Control input)
    {
        var label = CreateFieldLabel(labelText);
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 14))
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private Control CreateTwoColumnField(string leftLabelText, Control leftInput, string rightLabelText, Control rightInput)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 14))
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.Controls.Add(CreateField(leftLabelText, leftInput), 0, 0);

        var rightWrapper = CreateField(rightLabelText, rightInput);
        rightWrapper.Margin = new Padding(UiScaleHelper.Scale(this, 12), 0, 0, 0);
        layout.Controls.Add(rightWrapper, 1, 0);
        return layout;
    }

    private Control CreateModelField()
    {
        var inputHeight = UiScaleHelper.GetInputHeight(_modelListBox, 32);
        var modelRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            MinimumSize = new Size(0, inputHeight)
        };
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modelRow.RowStyles.Add(new RowStyle(SizeType.Absolute, inputHeight));
        modelRow.Controls.Add(_modelListBox, 0, 0);
        modelRow.Controls.Add(CreateIconButton("\uE710", "添加模型", (_, _) => AddModel()), 1, 0);
        modelRow.Controls.Add(CreateIconButton("\uE70F", "重命名模型", (_, _) => RenameSelectedModel()), 2, 0);
        modelRow.Controls.Add(CreateIconButton("\uE74D", "删除模型", (_, _) => DeleteSelectedModel()), 3, 0);

        return CreateField("默认模型", modelRow);
    }

    private Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9f),
        ForeColor = Color.FromArgb(72, 72, 72),
        Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 5))
    };

    private Button CreateDialogButton(string text, bool primary)
    {
        var button = new RoundedButton { Text = text, Margin = new Padding(UiScaleHelper.Scale(this, 8), 0, 0, 0) };
        if (primary)
            ButtonStyler.ApplyPrimary(button);
        else
            ButtonStyler.ApplySecondary(button);

        button.Size = UiScaleHelper.GetButtonSize(this, text, button.Font, 108, 38, horizontalLogicalPadding: 16);
        return button;
    }

    private Button CreateIconButton(string glyph, string tooltip, EventHandler click)
    {
        var size = UiScaleHelper.Scale(this, 32);
        var button = new RoundedButton
        {
            Text = glyph,
            AccessibleName = tooltip,
            Font = new Font("Segoe MDL2 Assets", 9.5f),
            Margin = new Padding(UiScaleHelper.Scale(this, 8), 0, 0, 0),
            Size = new Size(size, size),
            MinimumSize = new Size(size, size),
            TextAlign = ContentAlignment.MiddleCenter
        };
        ButtonStyler.ApplySecondary(button);
        _toolTip.SetToolTip(button, tooltip);
        button.Click += click;
        return button;
    }

    private FlowLayoutPanel CreateFooter() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        Padding = new Padding(0, UiScaleHelper.Scale(this, 10), 0, UiScaleHelper.Scale(this, 6)),
        Margin = new Padding(0)
    };

    private sealed class ModelNameEditForm : Form
    {
        private readonly TextBox _nameBox;

        public ModelNameEditForm(string title, string initialValue)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            Padding = new Padding(14);
            ClientSize = new Size(460, 190);
            MinimumSize = SizeFromClientSize(new Size(420, 180));
            FormStyler.ApplyRounded(this);

            _nameBox = new TextBox
            {
                Text = initialValue,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(this, 32))
            };

            var okBtn = new RoundedButton { Text = "确定", Margin = new Padding(8, 0, 0, 0) };
            var cancelBtn = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
            ButtonStyler.ApplyPrimary(okBtn);
            ButtonStyler.ApplySecondary(cancelBtn);
            okBtn.Size = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            okBtn.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    DialogPresenter.ShowMessage(this, "模型名称不能为空。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }

                ModelName = _nameBox.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(okBtn.Height, cancelBtn.Height) + UiScaleHelper.Scale(this, 24)));
            root.Controls.Add(new Label
            {
                Text = "模型名称",
                AutoSize = true,
                ForeColor = Color.FromArgb(72, 72, 72),
                Margin = new Padding(0, 0, 0, 5)
            }, 0, 0);
            root.Controls.Add(_nameBox, 0, 1);
            root.Controls.Add(new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, UiScaleHelper.Scale(this, 10), 0, UiScaleHelper.Scale(this, 6)),
                Controls = { cancelBtn, okBtn }
            }, 0, 2);

            Controls.Add(root);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;
            _nameBox.SelectAll();
        }

        public string ModelName { get; private set; } = string.Empty;
    }
}
