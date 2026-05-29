namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class EntryEditForm : Form
{
    private readonly QuickEntry _entry;
    private readonly IReadOnlyDictionary<EntryType, List<string>>? _groupSuggestions;
    private readonly TextBox _nameBox;
    private readonly TextBox _pathBox;
    private readonly ComboBox _typeBox;
    private readonly ComboBox _groupBox;
    private readonly Button _browseBtn;
    private readonly Label _pathLabel;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private readonly TableLayoutPanel _root;
    private readonly TableLayoutPanel _pathInputLayout;
    private readonly FlowLayoutPanel _buttonRow;
    private readonly TableLayoutPanel _iconRow;
    private readonly PictureBox _iconPreview;
    private readonly Button _chooseIconBtn;
    private readonly Button _clearIconBtn;
    private Image? _pendingIconImage;
    private Image? _placeholderImage;
    private bool _customIconChanged;
    private bool _removeCustomIcon;

    public EntryEditForm(QuickEntry entry, IReadOnlyDictionary<EntryType, List<string>>? groupSuggestions = null)
    {
        _entry = entry;
        _groupSuggestions = groupSuggestions;

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
        _typeBox.Items.AddRange(["文件夹", "文件", "网页", "文本"]);
        _typeBox.SelectedIndex = entry.Type switch
        {
            EntryType.Folder => 0,
            EntryType.File => 1,
            EntryType.Url => 2,
            EntryType.Text => 3,
            _ => 0
        };

        var groupLabel = CreateFieldLabel("分组:");
        _groupBox = new ComboBox
        {
            Text = entry.Group,
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Margin = new Padding(0)
        };

        var iconLabel = CreateFieldLabel("图标:");
        _iconPreview = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 10, 0)
        };
        _chooseIconBtn = new RoundedButton { Text = "选择图片...", Margin = new Padding(0, 0, 8, 0) };
        ButtonStyler.ApplySecondary(_chooseIconBtn);
        _chooseIconBtn.Click += OnChooseIcon;
        _clearIconBtn = new RoundedButton { Text = "使用网站图标", Margin = new Padding(0) };
        ButtonStyler.ApplySecondary(_clearIconBtn);
        _clearIconBtn.Click += OnClearIcon;

        _iconRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Margin = new Padding(0, 6, 0, 0)
        };
        _iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _iconRow.Controls.Add(iconLabel, 0, 0);
        _iconRow.Controls.Add(_iconPreview, 1, 0);
        _iconRow.Controls.Add(_chooseIconBtn, 2, 0);
        _iconRow.Controls.Add(_clearIconBtn, 3, 0);

        _placeholderImage = LoadWebPlaceholder();
        var existingIcon = CustomIconStore.TryLoad(entry.CustomIconPath);
        if (existingIcon != null)
        {
            _pendingIconImage = existingIcon;
            _iconPreview.Image = existingIcon;
        }
        else
        {
            _iconPreview.Image = _placeholderImage;
        }

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
                DialogPresenter.ShowMessage(this, hint, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // 应用自定义图标更改（仅网页条目有意义）
            if (_customIconChanged && currentType == EntryType.Url)
            {
                if (_removeCustomIcon)
                {
                    CustomIconStore.Remove(_entry.Id);
                    _entry.CustomIconPath = null;
                }
                else if (_pendingIconImage != null)
                {
                    _entry.CustomIconPath = CustomIconStore.Save(_entry.Id, _pendingIconImage);
                }
            }
        };

        _typeBox.SelectedIndexChanged += (_, _) =>
        {
            AdjustLayoutForType();
            UpdateGroupSuggestions();
        };

        _pathBox.TextChanged += (_, _) =>
        {
            var currentType = GetSelectedEntryType();
            if (currentType is EntryType.Url or EntryType.Text) return;

            var path = _pathBox.Text.Trim();
            if (Directory.Exists(path))
                _typeBox.SelectedIndex = 0;
            else if (File.Exists(path))
                _typeBox.SelectedIndex = 1;

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
        _root.Controls.Add(_iconRow, 0, 3);
        _root.Controls.Add(_buttonRow, 0, 4);

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
        UpdateGroupSuggestions();
        DpiChanged += (_, _) =>
        {
            ApplyScaledMetrics();
            AdjustLayoutForType();
        };
    }

    // 用当前选中类型下已有的分组名填充下拉项，保留用户当前输入/选择的文本
    private void UpdateGroupSuggestions()
    {
        var current = _groupBox.Text;
        _groupBox.BeginUpdate();
        _groupBox.Items.Clear();
        if (_groupSuggestions != null
            && _groupSuggestions.TryGetValue(GetSelectedEntryType(), out var groups))
        {
            foreach (var group in groups)
                _groupBox.Items.Add(group);
        }
        _groupBox.EndUpdate();
        _groupBox.Text = current;
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
        _ => EntryType.Folder
    };

    private void ApplyScaledMetrics()
    {
        Padding = UiScaleHelper.ScalePadding(this, new Padding(10));

        var inputHeight = UiScaleHelper.GetInputHeight(_nameBox, 30);
        var comboHeight = UiScaleHelper.GetInputHeight(_typeBox, 32);
        var browseButtonSize = UiScaleHelper.GetButtonSize(this, _browseBtn.Text, _browseBtn.Font, 44, 30, horizontalLogicalPadding: 10);

        _nameBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), inputHeight);
        _groupBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 160), comboHeight);
        _pathBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 340), inputHeight);
        _typeBox.MinimumSize = new Size(UiScaleHelper.Scale(this, 128), comboHeight);
        _browseBtn.Size = new Size(browseButtonSize.Width, inputHeight);

        _okBtn.Size = UiScaleHelper.GetButtonSize(this, _okBtn.Text, _okBtn.Font, 88, 34, horizontalLogicalPadding: 12);
        _cancelBtn.Size = UiScaleHelper.GetButtonSize(this, _cancelBtn.Text, _cancelBtn.Font, 88, 34, horizontalLogicalPadding: 12);

        var previewSize = UiScaleHelper.Scale(this, 40);
        _iconPreview.Size = new Size(previewSize, previewSize);
        _chooseIconBtn.Size = UiScaleHelper.GetButtonSize(this, _chooseIconBtn.Text, _chooseIconBtn.Font, 96, 30, horizontalLogicalPadding: 12);
        _clearIconBtn.Size = UiScaleHelper.GetButtonSize(this, _clearIconBtn.Text, _clearIconBtn.Font, 108, 30, horizontalLogicalPadding: 12);

        var minClientWidth = UiScaleHelper.Scale(this, 520);
        var preferredHeight = _root.GetPreferredSize(new Size(minClientWidth, 0)).Height;
        MinimumSize = SizeFromClientSize(new Size(minClientWidth, preferredHeight));
    }

    private void AdjustLayoutForType()
    {
        var type = GetSelectedEntryType();
        _iconRow.Visible = type == EntryType.Url; // 自定义图标仅对网页条目开放
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

    private void OnChooseIcon(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择图标图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif|所有文件|*.*"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            using var ms = new MemoryStream(bytes);
            var image = new Bitmap(ms);
            SetPendingIcon(image);
        }
        catch (Exception ex)
        {
            DialogPresenter.ShowMessage(this, $"无法加载该图片：{ex.Message}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnClearIcon(object? sender, EventArgs e)
    {
        _iconPreview.Image = _placeholderImage;
        if (_pendingIconImage != null && !ReferenceEquals(_pendingIconImage, _placeholderImage))
            _pendingIconImage.Dispose();
        _pendingIconImage = null;
        _customIconChanged = true;
        _removeCustomIcon = true;
    }

    private void SetPendingIcon(Image image)
    {
        _iconPreview.Image = image;
        if (_pendingIconImage != null && !ReferenceEquals(_pendingIconImage, image) && !ReferenceEquals(_pendingIconImage, _placeholderImage))
            _pendingIconImage.Dispose();
        _pendingIconImage = image;
        _customIconChanged = true;
        _removeCustomIcon = false;
    }

    private static Image? LoadWebPlaceholder()
    {
        try
        {
            using var stream = typeof(EntryEditForm).Assembly.GetManifestResourceStream("Quickstart.Resources.web-url.png");
            if (stream == null)
                return null;

            using var original = Image.FromStream(stream);
            return new Bitmap(original);
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_pendingIconImage != null && !ReferenceEquals(_pendingIconImage, _placeholderImage))
                _pendingIconImage.Dispose();
            _placeholderImage?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        if (_typeBox.SelectedIndex == 0)
        {
            using var dlg = new FolderBrowserDialog();
            if (!string.IsNullOrEmpty(_pathBox.Text) && Directory.Exists(_pathBox.Text))
                dlg.SelectedPath = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.SelectedPath;
        }
        else
        {
            using var dlg = new OpenFileDialog { Filter = "所有文件|*.*" };
            if (!string.IsNullOrEmpty(_pathBox.Text) && File.Exists(_pathBox.Text))
                dlg.FileName = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.FileName;
        }
    }
}
