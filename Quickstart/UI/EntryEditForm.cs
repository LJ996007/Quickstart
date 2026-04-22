namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class EntryEditForm : Form
{
    private readonly QuickEntry _entry;
    private readonly TextBox _nameBox;
    private readonly TextBox _pathBox;
    private readonly ComboBox _typeBox;
    private readonly TextBox _groupBox;
    private readonly Button _browseBtn;
    private readonly Label _pathLabel;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private readonly TableLayoutPanel _root;
    private readonly TableLayoutPanel _pathInputLayout;
    private readonly FlowLayoutPanel _buttonRow;

    public EntryEditForm(QuickEntry entry)
    {
        _entry = entry;

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = string.IsNullOrEmpty(entry.Name) ? "添加条目" : "编辑条目";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(10);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormStyler.ApplyRounded(this);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var nameLabel = CreateFieldLabel("名称:");
        _nameBox = new TextBox
        {
            Text = entry.Name,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        _pathLabel = CreateFieldLabel("路径:");
        _pathBox = new TextBox
        {
            Text = entry.Path,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        _browseBtn = new RoundedButton
        {
            Text = "...",
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(8, 0, 0, 0)
        };
        _browseBtn.Click += OnBrowse;
        ButtonStyler.ApplySecondary(_browseBtn);

        _pathInputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(0)
        };
        _pathInputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _pathInputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pathInputLayout.Controls.Add(_pathBox, 0, 0);
        _pathInputLayout.Controls.Add(_browseBtn, 1, 0);

        var typeLabel = CreateFieldLabel("类型:");
        _typeBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0)
        };
        _typeBox.Items.AddRange(["文件夹", "文件", "网页", "文本", "文档"]);
        _typeBox.SelectedIndex = entry.Type switch
        {
            EntryType.Folder => 0,
            EntryType.File => 1,
            EntryType.Url => 2,
            EntryType.Text => 3,
            EntryType.Document => 4,
            _ => 0
        };

        var groupLabel = CreateFieldLabel("分组:");
        _groupBox = new TextBox
        {
            Text = entry.Group,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        _okBtn = new RoundedButton
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplyPrimary(_okBtn);

        _cancelBtn = new RoundedButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0)
        };
        ButtonStyler.ApplySecondary(_cancelBtn);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;

        _okBtn.Click += (_, _) =>
        {
            var currentType = GetSelectedEntryType();
            var pathText = _pathBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(pathText))
            {
                var hint = currentType switch
                {
                    EntryType.Url => "请输入网址。",
                    EntryType.Text => "请输入内容。",
                    _ => "请输入路径。"
                };
                MessageBox.Show(hint, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            _entry.Path = pathText;
            _entry.Name = string.IsNullOrWhiteSpace(_nameBox.Text)
                ? (currentType == EntryType.Url || currentType == EntryType.Text
                    ? pathText[..Math.Min(pathText.Length, 30)]
                    : Path.GetFileName(_entry.Path))
                : _nameBox.Text.Trim();
            _entry.Type = currentType;
            _entry.Group = _groupBox.Text.Trim();
        };

        _typeBox.SelectedIndexChanged += (_, _) => AdjustLayoutForType();

        _pathBox.TextChanged += (_, _) =>
        {
            var path = _pathBox.Text.Trim();
            UpdateTypeFromPath(path);

            if (string.IsNullOrWhiteSpace(_nameBox.Text) || _nameBox.Text == _entry.Name)
                _nameBox.Text = Path.GetFileName(path);
        };

        _buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 14, 0, 0)
        };
        _buttonRow.Controls.Add(_cancelBtn);
        _buttonRow.Controls.Add(_okBtn);

        var metaRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Margin = new Padding(0, 6, 0, 0)
        };
        metaRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        metaRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        metaRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        metaRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        metaRow.Controls.Add(typeLabel, 0, 0);
        metaRow.Controls.Add(_typeBox, 1, 0);
        metaRow.Controls.Add(groupLabel, 2, 0);
        metaRow.Controls.Add(_groupBox, 3, 0);

        _root.Controls.Add(CreateFieldRow(nameLabel, _nameBox), 0, 0);
        _root.Controls.Add(CreateFieldRow(_pathLabel, _pathInputLayout), 0, 1);
        _root.Controls.Add(metaRow, 0, 2);
        _root.Controls.Add(_buttonRow, 0, 3);

        Controls.Add(_root);

        _pathBox.AllowDrop = true;
        _pathBox.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Link;
        };
        _pathBox.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                _pathBox.Text = files[0];
        };

        ApplyScaledMetrics();
        AdjustLayoutForType();
        DpiChanged += (_, _) =>
        {
            ApplyScaledMetrics();
            AdjustLayoutForType();
        };
    }

    private Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 10, 0)
    };

    private static TableLayoutPanel CreateFieldRow(Label label, Control input)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(input, 1, 0);
        return row;
    }

    private EntryType GetSelectedEntryType() => _typeBox.SelectedIndex switch
    {
        0 => EntryType.Folder,
        1 => EntryType.File,
        2 => EntryType.Url,
        3 => EntryType.Text,
        4 => EntryType.Document,
        _ => EntryType.Folder
    };

    private void UpdateTypeFromPath(string path)
    {
        var currentType = GetSelectedEntryType();
        if (currentType is EntryType.Url or EntryType.Text)
            return;

        var targetIndex = _typeBox.SelectedIndex;
        if (Directory.Exists(path))
            targetIndex = 0;
        else if (EntryClassifier.IsDocumentPath(path))
            targetIndex = 4;
        else if (!string.IsNullOrWhiteSpace(path))
            targetIndex = 1;

        if (_typeBox.SelectedIndex != targetIndex)
            _typeBox.SelectedIndex = targetIndex;
    }

    private void ApplyScaledMetrics()
    {
        Padding = UiScaleHelper.ScalePadding(this, new Padding(10));

        var inputHeight = UiScaleHelper.GetInputHeight(_nameBox, 30);
        var comboHeight = UiScaleHelper.GetInputHeight(_typeBox, 32);
        var browseButtonSize = UiScaleHelper.GetButtonSize(this, _browseBtn.Text, _browseBtn.Font, 44, 30, horizontalLogicalPadding: 10);

        _nameBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), inputHeight);
        _groupBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 160), inputHeight);
        _pathBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), inputHeight);
        _typeBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 128), comboHeight);
        _browseBtn.Size = new Size(browseButtonSize.Width, inputHeight);

        _okBtn.Size = UiScaleHelper.GetButtonSize(this, _okBtn.Text, _okBtn.Font, 88, 34, horizontalLogicalPadding: 12);
        _cancelBtn.Size = UiScaleHelper.GetButtonSize(this, _cancelBtn.Text, _cancelBtn.Font, 88, 34, horizontalLogicalPadding: 12);

        var minClientWidth = UiScaleHelper.Scale(this, 520);
        var preferredHeight = _root.GetPreferredSize(new Size(minClientWidth, 0)).Height;
        MinimumSize = SizeFromClientSize(new Size(minClientWidth, preferredHeight));
    }

    private void AdjustLayoutForType()
    {
        var type = GetSelectedEntryType();
        var singleLineHeight = UiScaleHelper.GetInputHeight(_pathBox, 30);
        var multiLineHeight = UiScaleHelper.Scale(this, 120);

        _pathBox.AcceptsReturn = false;
        _pathBox.WordWrap = false;
        _pathBox.ScrollBars = ScrollBars.None;

        switch (type)
        {
            case EntryType.Url:
                _pathLabel.Text = "网址:";
                _pathBox.Multiline = false;
                _pathBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), singleLineHeight);
                _browseBtn.Visible = false;
                break;

            case EntryType.Text:
                _pathLabel.Text = "内容:";
                _pathBox.Multiline = true;
                _pathBox.AcceptsReturn = true;
                _pathBox.WordWrap = true;
                _pathBox.ScrollBars = ScrollBars.Vertical;
                _pathBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 360), multiLineHeight);
                _browseBtn.Visible = false;
                break;

            case EntryType.Document:
            default:
                _pathLabel.Text = "路径:";
                _pathBox.Multiline = false;
                _pathBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), singleLineHeight);
                _browseBtn.Visible = true;
                break;
        }

        _pathInputLayout.PerformLayout();
        _root.PerformLayout();
        var minClientWidth = UiScaleHelper.Scale(this, 520);
        var preferred = _root.GetPreferredSize(new Size(minClientWidth, 0));
        MinimumSize = SizeFromClientSize(new Size(minClientWidth, preferred.Height));
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        var currentType = GetSelectedEntryType();
        if (currentType == EntryType.Folder)
        {
            using var dlg = new FolderBrowserDialog();
            if (!string.IsNullOrEmpty(_pathBox.Text) && Directory.Exists(_pathBox.Text))
                dlg.SelectedPath = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.SelectedPath;
        }
        else
        {
            using var dlg = new OpenFileDialog
            {
                Filter = currentType == EntryType.Document
                    ? EntryClassifier.DocumentFileDialogFilter
                    : "所有文件|*.*"
            };
            if (!string.IsNullOrEmpty(_pathBox.Text) && File.Exists(_pathBox.Text))
                dlg.FileName = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.FileName;
        }
    }
}
