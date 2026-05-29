namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class AiSkillEditForm : Form
{
    private readonly AiConfig _config;
    private readonly AiSkill _skill;
    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _providerBox;
    private readonly TextBox _modelBox;
    private readonly ListBox _stepsBox;
    private readonly List<AiSkillStep> _steps;

    public AiSkillEditForm(AiConfig config, AiSkill skill)
    {
        _config = config;
        _skill = skill;
        _steps = skill.Steps
            .Select(step => new AiSkillStep { PromptId = step.PromptId, InlineTemplate = step.InlineTemplate })
            .ToList();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = string.IsNullOrWhiteSpace(skill.Name) ? "添加 Skill" : "编辑 Skill";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = UiScaleHelper.ScalePadding(this, new Padding(14));
        ClientSize = new Size(760, 640);
        MinimumSize = SizeFromClientSize(new Size(680, 560));
        FormStyler.ApplyRounded(this);

        var inputHeight = UiScaleHelper.GetInputHeight(this, 32);
        _nameBox = new TextBox { Text = skill.Name, Dock = DockStyle.Fill, MinimumSize = new Size(0, inputHeight) };
        _descriptionBox = new TextBox { Text = skill.Description, Dock = DockStyle.Fill, MinimumSize = new Size(0, inputHeight) };
        _providerBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _providerBox.Items.Add("(使用面板当前 Provider)");
        foreach (var provider in config.Providers)
            _providerBox.Items.Add(provider);
        var providerIndex = config.Providers.FindIndex(provider => provider.Id == skill.ProviderId);
        _providerBox.SelectedIndex = providerIndex >= 0 ? providerIndex + 1 : 0;

        _modelBox = new TextBox
        {
            Text = skill.Model,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, inputHeight),
            PlaceholderText = "留空使用面板当前模型"
        };

        _stepsBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        RefreshSteps();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, 104)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, 64)));

        root.Controls.Add(CreateField("名称", _nameBox), 0, 0);
        root.Controls.Add(CreateField("说明", _descriptionBox), 0, 1);
        root.Controls.Add(CreateTwoColumnField("Provider", _providerBox, "模型", _modelBox), 0, 2);
        root.Controls.Add(new Label
        {
            Text = "步骤",
            AutoSize = true,
            ForeColor = Color.FromArgb(72, 72, 72),
            Margin = new Padding(0, 2, 0, 5)
        }, 0, 3);
        root.Controls.Add(_stepsBox, 0, 4);

        var stepButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            Margin = new Padding(0)
        };
        AddStepButton(stepButtons, "添加 Prompt", (_, _) => AddPromptStep());
        AddStepButton(stepButtons, "添加自定义", (_, _) => AddInlineStep());
        AddStepButton(stepButtons, "编辑", (_, _) => EditSelectedStep());
        AddStepButton(stepButtons, "上移", (_, _) => MoveSelectedStep(-1));
        AddStepButton(stepButtons, "下移", (_, _) => MoveSelectedStep(1));
        AddStepButton(stepButtons, "删除", (_, _) => RemoveSelectedStep());
        root.Controls.Add(stepButtons, 0, 5);

        var okBtn = new RoundedButton { Text = "保存", Margin = new Padding(8, 0, 0, 0) };
        var cancelBtn = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        ButtonStyler.ApplyPrimary(okBtn);
        ButtonStyler.ApplySecondary(cancelBtn);
        okBtn.Size = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 112, 38, horizontalLogicalPadding: 16);
        cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 112, 38, horizontalLogicalPadding: 16);
        root.RowStyles[6].Height = Math.Max(okBtn.Height, cancelBtn.Height) + UiScaleHelper.Scale(this, 24);
        okBtn.Click += OnSave;
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, UiScaleHelper.Scale(this, 10), 0, UiScaleHelper.Scale(this, 6)),
            Margin = new Padding(0)
        };
        buttons.Controls.Add(cancelBtn);
        buttons.Controls.Add(okBtn);
        root.Controls.Add(buttons, 0, 6);

        Controls.Add(root);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            DialogPresenter.ShowMessage(this, "Skill 名称不能为空。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (_steps.Count == 0)
        {
            DialogPresenter.ShowMessage(this, "Skill 至少需要一个步骤。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _skill.Name = _nameBox.Text.Trim();
        _skill.Description = _descriptionBox.Text.Trim();
        _skill.ProviderId = _providerBox.SelectedItem is AiProviderConfig provider ? provider.Id : string.Empty;
        _skill.Model = _modelBox.Text.Trim();
        _skill.Steps = _steps
            .Select(step => new AiSkillStep { PromptId = step.PromptId, InlineTemplate = step.InlineTemplate })
            .ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddPromptStep()
    {
        using var form = new PromptStepSelectForm(_config.PromptPresets);
        if (DialogPresenter.ShowModal(form, this) != DialogResult.OK || form.SelectedPrompt == null)
            return;

        _steps.Add(new AiSkillStep { PromptId = form.SelectedPrompt.Id });
        RefreshSteps(_steps.Count - 1);
    }

    private void AddInlineStep()
    {
        using var form = new InlineStepEditForm(new AiSkillStep());
        if (DialogPresenter.ShowModal(form, this) != DialogResult.OK)
            return;

        _steps.Add(form.Step);
        RefreshSteps(_steps.Count - 1);
    }

    private void EditSelectedStep()
    {
        var index = _stepsBox.SelectedIndex;
        if (index < 0 || index >= _steps.Count)
            return;

        var step = _steps[index];
        if (!string.IsNullOrWhiteSpace(step.InlineTemplate))
        {
            using var form = new InlineStepEditForm(step);
            if (DialogPresenter.ShowModal(form, this) == DialogResult.OK)
            {
                _steps[index] = form.Step;
                RefreshSteps(index);
            }

            return;
        }

        using var selector = new PromptStepSelectForm(_config.PromptPresets, step.PromptId);
        if (DialogPresenter.ShowModal(selector, this) == DialogResult.OK && selector.SelectedPrompt != null)
        {
            step.PromptId = selector.SelectedPrompt.Id;
            RefreshSteps(index);
        }
    }

    private void RemoveSelectedStep()
    {
        var index = _stepsBox.SelectedIndex;
        if (index < 0 || index >= _steps.Count)
            return;

        _steps.RemoveAt(index);
        RefreshSteps(Math.Min(index, _steps.Count - 1));
    }

    private void MoveSelectedStep(int delta)
    {
        var index = _stepsBox.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _steps.Count)
            return;

        (_steps[index], _steps[target]) = (_steps[target], _steps[index]);
        RefreshSteps(target);
    }

    private void RefreshSteps(int selectedIndex = -1)
    {
        _stepsBox.Items.Clear();
        for (var i = 0; i < _steps.Count; i++)
            _stepsBox.Items.Add($"{i + 1}. {GetStepDisplayName(_steps[i])}");

        if (selectedIndex >= 0 && selectedIndex < _stepsBox.Items.Count)
            _stepsBox.SelectedIndex = selectedIndex;
    }

    private string GetStepDisplayName(AiSkillStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.InlineTemplate))
            return "自定义步骤";

        return _config.PromptPresets.FirstOrDefault(prompt => prompt.Id == step.PromptId)?.Name
            ?? $"Prompt: {step.PromptId}";
    }

    private void AddStepButton(FlowLayoutPanel panel, string text, EventHandler click)
    {
        var button = new RoundedButton { Text = text, Margin = new Padding(0, 0, 8, 8) };
        ButtonStyler.ApplySecondary(button);
        button.Size = UiScaleHelper.GetButtonSize(this, text, button.Font, 96, 34, horizontalLogicalPadding: 10);
        button.Click += click;
        panel.Controls.Add(button);
    }

    private static Control CreateField(string labelText, Control input)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = Color.FromArgb(72, 72, 72),
            Margin = new Padding(0, 0, 0, 5)
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static Control CreateTwoColumnField(string leftLabel, Control left, string rightLabel, Control right)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.Controls.Add(new Label { Text = leftLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        panel.Controls.Add(left, 1, 0);
        panel.Controls.Add(new Label { Text = rightLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 8, 0) }, 2, 0);
        panel.Controls.Add(right, 3, 0);
        return panel;
    }

    private sealed class PromptStepSelectForm : Form
    {
        private readonly ComboBox _promptBox;
        public AiPromptPreset? SelectedPrompt { get; private set; }

        public PromptStepSelectForm(List<AiPromptPreset> prompts, string selectedPromptId = "")
        {
            Text = "选择 Prompt";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(460, 180);
            Padding = new Padding(14);
            FormStyler.ApplyRounded(this);

            _promptBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                MinimumSize = new Size(0, 32)
            };
            foreach (var prompt in prompts)
                _promptBox.Items.Add(prompt);
            _promptBox.DisplayMember = "Name";
            var index = prompts.FindIndex(prompt => prompt.Id == selectedPromptId);
            _promptBox.SelectedIndex = index >= 0 ? index : (_promptBox.Items.Count > 0 ? 0 : -1);

            var okBtn = new RoundedButton { Text = "确定", DialogResult = DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
            var cancelBtn = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
            ButtonStyler.ApplyPrimary(okBtn);
            ButtonStyler.ApplySecondary(cancelBtn);
            okBtn.Size = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            okBtn.Click += (_, _) =>
            {
                SelectedPrompt = _promptBox.SelectedItem as AiPromptPreset;
                if (SelectedPrompt == null)
                    DialogResult = DialogResult.None;
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(okBtn.Height, cancelBtn.Height) + UiScaleHelper.Scale(this, 24)));
            root.Controls.Add(new Label
            {
                Text = "选择要加入 Skill 的 Prompt",
                AutoSize = true,
                ForeColor = Color.FromArgb(72, 72, 72),
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 0);
            root.Controls.Add(_promptBox, 0, 1);
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
        }
    }

    private sealed class InlineStepEditForm : Form
    {
        private readonly TextBox _templateBox;
        public AiSkillStep Step { get; private set; }

        public InlineStepEditForm(AiSkillStep step)
        {
            Step = new AiSkillStep { PromptId = step.PromptId, InlineTemplate = step.InlineTemplate };
            Text = "自定义步骤";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(640, 440);
            MinimumSize = SizeFromClientSize(new Size(560, 380));
            Padding = new Padding(14);
            FormStyler.ApplyRounded(this);

            _templateBox = new TextBox
            {
                Text = Step.InlineTemplate,
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical
            };

            var okBtn = new RoundedButton { Text = "确定", DialogResult = DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
            var cancelBtn = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
            ButtonStyler.ApplyPrimary(okBtn);
            ButtonStyler.ApplySecondary(cancelBtn);
            okBtn.Size = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 108, 38, horizontalLogicalPadding: 16);
            okBtn.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_templateBox.Text)
                    || !_templateBox.Text.Contains(PromptRenderer.InputPlaceholder, StringComparison.Ordinal))
                {
                    DialogPresenter.ShowMessage(this, "自定义步骤必须包含 {文本}。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }

                Step = new AiSkillStep { InlineTemplate = _templateBox.Text.Trim() };
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(okBtn.Height, cancelBtn.Height) + UiScaleHelper.Scale(this, 24)));
            root.Controls.Add(new Label
            {
                Text = "自定义步骤模板（必须包含 {文本}）",
                AutoSize = true,
                ForeColor = Color.FromArgb(72, 72, 72),
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 0);
            root.Controls.Add(_templateBox, 0, 1);
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
        }
    }
}
