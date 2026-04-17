namespace Quickstart.UI;

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

    // Baseline positions for File/Folder mode
    private const int PathLabelY = 56;
    private const int TypeRowY = 92;
    private const int ButtonY = 205;
    private const int FormHeight = 310;

    // Expanded positions for Text mode (multiline)
    private const int TextBoxHeight = 120;
    private const int TextTypeRowY = 56 + TextBoxHeight + 12; // pathBox top + height + gap
    private const int TextButtonY = TextTypeRowY + 36;
    private const int TextFormHeight = TextButtonY + 80;

    public EntryEditForm(QuickEntry entry)
    {
        _entry = entry;

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = string.IsNullOrEmpty(entry.Name) ? "添加条目" : "编辑条目";
        Size = new Size(500, FormHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        var nameLabel = new Label { Text = "名称:", Location = new Point(16, 20), AutoSize = true };
        _nameBox = new TextBox
        {
            Text = entry.Name,
            Location = new Point(80, 17),
            Width = 380
        };

        _pathLabel = new Label { Text = "路径:", Location = new Point(16, PathLabelY), AutoSize = true };
        _pathBox = new TextBox
        {
            Text = entry.Path,
            Location = new Point(80, 53),
            Width = 330
        };

        _browseBtn = new Button
        {
            Text = "...",
            Location = new Point(418, 51),
            Width = 42,
            Height = 30,
            Font = new Font("Segoe UI", 9f)
        };
        _browseBtn.Click += OnBrowse;
        ButtonStyler.ApplySecondary(_browseBtn);

        var typeLabel = new Label { Text = "类型:", Location = new Point(16, TypeRowY), AutoSize = true };
        _typeBox = new ComboBox
        {
            Location = new Point(80, TypeRowY - 3),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _typeBox.Items.AddRange(["文件夹", "文件", "网页", "文本"]);
        _typeBox.SelectedIndex = entry.Type switch
        {
            EntryType.Folder => 0,
            EntryType.File   => 1,
            EntryType.Url    => 2,
            EntryType.Text   => 3,
            _ => 0
        };

        var groupLabel = new Label { Text = "分组:", Location = new Point(220, TypeRowY), AutoSize = true };
        _groupBox = new TextBox
        {
            Text = entry.Group,
            Location = new Point(270, TypeRowY - 3),
            Width = 190
        };

        _okBtn = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(278, ButtonY),
            Width = 88,
            Height = 34,
            Font = new Font("Segoe UI", 9f)
        };
        ButtonStyler.ApplyPrimary(_okBtn);

        _cancelBtn = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(372, ButtonY),
            Width = 88,
            Height = 34,
            Font = new Font("Segoe UI", 9f)
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
                    EntryType.Url  => "请输入网址。",
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

        // Type change → adjust layout dynamically
        _typeBox.SelectedIndexChanged += (_, _) => AdjustLayoutForType();

        // Auto-detect type when path changes (only for file/folder types)
        _pathBox.TextChanged += (_, _) =>
        {
            var currentType = GetSelectedEntryType();
            if (currentType is EntryType.Url or EntryType.Text) return;

            var p = _pathBox.Text.Trim();
            if (Directory.Exists(p))
                _typeBox.SelectedIndex = 0;
            else if (File.Exists(p))
                _typeBox.SelectedIndex = 1;

            if (string.IsNullOrWhiteSpace(_nameBox.Text) || _nameBox.Text == _entry.Name)
            {
                _nameBox.Text = Path.GetFileName(p);
            }
        };

        Controls.AddRange([nameLabel, _nameBox, _pathLabel, _pathBox, _browseBtn,
            typeLabel, _typeBox, groupLabel, _groupBox, _okBtn, _cancelBtn]);

        // Enable drag-drop on path box (file/folder mode only)
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

        // Apply initial layout for the current type
        AdjustLayoutForType();
    }

    private EntryType GetSelectedEntryType() => _typeBox.SelectedIndex switch
    {
        0 => EntryType.Folder,
        1 => EntryType.File,
        2 => EntryType.Url,
        3 => EntryType.Text,
        _ => EntryType.Folder
    };

    private void AdjustLayoutForType()
    {
        var type = GetSelectedEntryType();

        switch (type)
        {
            case EntryType.Url:
                _pathLabel.Text = "网址:";
                _pathBox.Multiline = false;
                _pathBox.Height = 23;  // default single-line height
                _pathBox.Width = 380;
                _pathBox.ScrollBars = ScrollBars.None;
                _browseBtn.Visible = false;
                ResetStandardLayout();
                break;

            case EntryType.Text:
                _pathLabel.Text = "内容:";
                _pathBox.Multiline = true;
                _pathBox.Height = TextBoxHeight;
                _pathBox.Width = 380;
                _pathBox.ScrollBars = ScrollBars.Vertical;
                _pathBox.WordWrap = true;
                _pathBox.AcceptsReturn = true;
                _browseBtn.Visible = false;
                // Expand form and shift type row + buttons down
                var typeLabel2 = Controls.OfType<Label>().FirstOrDefault(l => l.Text == "类型:");
                var groupLabel2 = Controls.OfType<Label>().FirstOrDefault(l => l.Text == "分组:");
                if (typeLabel2 != null) typeLabel2.Top = TextTypeRowY;
                if (groupLabel2 != null) groupLabel2.Top = TextTypeRowY;
                _typeBox.Top = TextTypeRowY - 3;
                _groupBox.Top = TextTypeRowY - 3;
                _okBtn.Top = TextButtonY;
                _cancelBtn.Top = TextButtonY;
                Size = new Size(500, TextFormHeight);
                break;

            default: // Folder or File
                _pathLabel.Text = "路径:";
                _pathBox.Multiline = false;
                _pathBox.Height = 23;
                _pathBox.Width = 330;
                _pathBox.ScrollBars = ScrollBars.None;
                _browseBtn.Visible = true;
                ResetStandardLayout();
                break;
        }
    }

    private void ResetStandardLayout()
    {
        var typeLabel2 = Controls.OfType<Label>().FirstOrDefault(l => l.Text == "类型:");
        var groupLabel2 = Controls.OfType<Label>().FirstOrDefault(l => l.Text == "分组:");
        if (typeLabel2 != null) typeLabel2.Top = TypeRowY;
        if (groupLabel2 != null) groupLabel2.Top = TypeRowY;
        _typeBox.Top = TypeRowY - 3;
        _groupBox.Top = TypeRowY - 3;
        _okBtn.Top = ButtonY;
        _cancelBtn.Top = ButtonY;
        Size = new Size(500, FormHeight);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        if (_typeBox.SelectedIndex == 0) // Folder
        {
            using var dlg = new FolderBrowserDialog();
            if (!string.IsNullOrEmpty(_pathBox.Text) && Directory.Exists(_pathBox.Text))
                dlg.SelectedPath = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.SelectedPath;
        }
        else // File
        {
            using var dlg = new OpenFileDialog { Filter = "所有文件|*.*" };
            if (!string.IsNullOrEmpty(_pathBox.Text) && File.Exists(_pathBox.Text))
                dlg.FileName = _pathBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _pathBox.Text = dlg.FileName;
        }
    }
}
