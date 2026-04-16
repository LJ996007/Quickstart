namespace Quickstart.UI;

using Quickstart.Models;

public sealed class EntryEditForm : Form
{
    private readonly QuickEntry _entry;
    private readonly TextBox _nameBox;
    private readonly TextBox _pathBox;
    private readonly ComboBox _typeBox;
    private readonly TextBox _groupBox;

    public EntryEditForm(QuickEntry entry)
    {
        _entry = entry;

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = string.IsNullOrEmpty(entry.Name) ? "添加条目" : "编辑条目";
        Size = new Size(500, 270);
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

        var pathLabel = new Label { Text = "路径:", Location = new Point(16, 56), AutoSize = true };
        _pathBox = new TextBox
        {
            Text = entry.Path,
            Location = new Point(80, 53),
            Width = 330
        };

        var browseBtn = new Button
        {
            Text = "...",
            Location = new Point(418, 51),
            Width = 42,
            Height = 30,
            Font = new Font("Segoe UI", 9f)
        };
        browseBtn.Click += OnBrowse;

        var typeLabel = new Label { Text = "类型:", Location = new Point(16, 92), AutoSize = true };
        _typeBox = new ComboBox
        {
            Location = new Point(80, 89),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _typeBox.Items.AddRange(["文件夹", "文件"]);
        _typeBox.SelectedIndex = entry.Type == EntryType.Folder ? 0 : 1;

        var groupLabel = new Label { Text = "分组:", Location = new Point(220, 92), AutoSize = true };
        _groupBox = new TextBox
        {
            Text = entry.Group,
            Location = new Point(270, 89),
            Width = 190
        };

        var okBtn = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(278, 180),
            Width = 88,
            Height = 34,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f)
        };
        okBtn.FlatAppearance.BorderSize = 0;

        var cancelBtn = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(372, 180),
            Width = 88,
            Height = 34,
            Font = new Font("Segoe UI", 9f)
        };

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        okBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_pathBox.Text))
            {
                MessageBox.Show("请输入路径。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            _entry.Path = _pathBox.Text.Trim();
            _entry.Name = string.IsNullOrWhiteSpace(_nameBox.Text)
                ? Path.GetFileName(_entry.Path)
                : _nameBox.Text.Trim();
            _entry.Type = _typeBox.SelectedIndex == 0 ? EntryType.Folder : EntryType.File;
            _entry.Group = _groupBox.Text.Trim();
        };

        // Auto-detect type when path changes
        _pathBox.TextChanged += (_, _) =>
        {
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

        Controls.AddRange([nameLabel, _nameBox, pathLabel, _pathBox, browseBtn,
            typeLabel, _typeBox, groupLabel, _groupBox, okBtn, cancelBtn]);

        // Enable drag-drop on path box
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
