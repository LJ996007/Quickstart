namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class AiPromptEditForm : Form
{
    private readonly AiPromptPreset _prompt;
    private readonly TextBox _nameBox;
    private readonly TextBox _templateBox;
    private readonly ComboBox _targetBox;

    public AiPromptEditForm(AiPromptPreset prompt)
    {
        _prompt = prompt;

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = string.IsNullOrWhiteSpace(prompt.Name) ? "添加 Prompt" : "编辑 Prompt";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = UiScaleHelper.ScalePadding(this, new Padding(14));
        ClientSize = new Size(660, 520);
        MinimumSize = SizeFromClientSize(new Size(560, 460));
        FormStyler.ApplyRounded(this);

        _nameBox = new TextBox
        {
            Text = prompt.Name,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(this, 32))
        };

        _templateBox = new TextBox
        {
            Text = prompt.Template,
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical
        };

        _targetBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            MinimumSize = new Size(0, UiScaleHelper.GetInputHeight(this, 32))
        };
        _targetBox.Items.AddRange(["调用 API（在面板内显示结果）", "发送到网页 DeepSeek（自动填入输入框）"]);
        _targetBox.SelectedIndex = prompt.Target == AiPromptTarget.Web ? 1 : 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 标题
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 名称
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 目标
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 模板
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 提示
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, 64))); // 按钮

        root.Controls.Add(new Label
        {
            Text = "Prompt 配置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 35, 35),
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 12))
        }, 0, 0);
        root.Controls.Add(CreateField("名称", _nameBox), 0, 1);
        root.Controls.Add(CreateField("目标", _targetBox), 0, 2);
        root.Controls.Add(CreateField("模板（必须包含 {文本}）", _templateBox), 0, 3);

        var hint = new Label
        {
            Text = "运行时会把 {文本} 替换成选中的文本或文件内容。\r\n“发送到网页”会复制并打开网页、自动粘贴到输入框（由你回车发送）。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(0, UiScaleHelper.Scale(this, 6), 0, 0)
        };
        root.Controls.Add(hint, 0, 4);

        var okBtn = new RoundedButton { Text = "保存", Margin = new Padding(8, 0, 0, 0) };
        var cancelBtn = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        ButtonStyler.ApplyPrimary(okBtn);
        ButtonStyler.ApplySecondary(cancelBtn);
        okBtn.Size = UiScaleHelper.GetButtonSize(this, okBtn.Text, okBtn.Font, 112, 38, horizontalLogicalPadding: 16);
        cancelBtn.Size = UiScaleHelper.GetButtonSize(this, cancelBtn.Text, cancelBtn.Font, 112, 38, horizontalLogicalPadding: 16);
        root.RowStyles[5].Height = Math.Max(okBtn.Height, cancelBtn.Height) + UiScaleHelper.Scale(this, 24);
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
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text) || string.IsNullOrWhiteSpace(_templateBox.Text))
        {
            DialogPresenter.ShowMessage(this, "名称和模板不能为空。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (!_templateBox.Text.Contains(PromptRenderer.InputPlaceholder, StringComparison.Ordinal))
        {
            DialogPresenter.ShowMessage(this, "模板必须包含 {文本} 占位符。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _prompt.Name = _nameBox.Text.Trim();
        _prompt.Template = _templateBox.Text.Trim();
        _prompt.Target = _targetBox.SelectedIndex == 1 ? AiPromptTarget.Web : AiPromptTarget.Api;
        DialogResult = DialogResult.OK;
        Close();
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
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }
}
