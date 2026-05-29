namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

internal sealed class AiActionPickerPopup : Form
{
    private static readonly Size PickerLogicalSize = new(420, 320);
    private const string PromptHeader = "Prompt";
    private const string SkillHeader = "Skill";

    private readonly ConfigManager _configManager;
    private readonly TableLayoutPanel _root;
    private readonly FlowLayoutPanel _promptList;
    private readonly FlowLayoutPanel _skillList;
    private readonly List<ActionItemControl> _items = [];

    public AiActionPickerPopup(ConfigManager configManager)
    {
        _configManager = configManager;

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(250, 250, 250);
        Padding = new Padding(1);
        SetStyle(ControlStyles.ResizeRedraw, true);
        FormStyler.ApplyRounded(this);

        var border = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(220, 220, 220)
        };

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 250, 250),
            ColumnCount = 2,
            RowCount = 2,
            Padding = UiScaleHelper.ScalePadding(this, new Padding(10)),
            Margin = new Padding(0)
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _root.Controls.Add(CreateHeader(PromptHeader), 0, 0);
        _root.Controls.Add(CreateHeader(SkillHeader), 1, 0);

        _promptList = CreateListPanel();
        _skillList = CreateListPanel();
        _root.Controls.Add(_promptList, 0, 1);
        _root.Controls.Add(_skillList, 1, 1);

        border.Controls.Add(_root);
        Controls.Add(border);
    }

    public bool HasActions => _items.Count > 0;

    public void ShowAtGesturePoint(Point screenPt)
    {
        RefreshActions();
        if (!HasActions)
            return;

        var screen = Screen.FromPoint(screenPt);
        EnsurePopupSizeForScreen(screen);
        var workingArea = screen.WorkingArea;
        var margin = UiScaleHelper.Scale(this, 8);
        var x = Math.Max(workingArea.Left + margin, Math.Min(screenPt.X - Width + margin, workingArea.Right - Width - margin));
        var y = Math.Max(workingArea.Top + margin, Math.Min(screenPt.Y - (Height / 2), workingArea.Bottom - Height - margin));
        Location = new Point(x, y);

        Show();
        HighlightAtScreenPoint(screenPt);
    }

    public void HighlightAtScreenPoint(Point screenPt)
    {
        var active = GetItemAtScreenPoint(screenPt);
        foreach (var item in _items)
            item.SetHighlighted(ReferenceEquals(item, active));
    }

    public AiActionSelection? TryReleaseAtScreenPoint(Point screenPt)
    {
        if (!Bounds.Contains(screenPt))
        {
            Hide();
            return null;
        }

        var item = GetItemAtScreenPoint(screenPt);
        if (item == null)
        {
            item = _items.FirstOrDefault();
            if (item == null)
            {
                Hide();
                return null;
            }
        }

        var selection = item.Selection;
        Hide();
        return selection;
    }

    private void RefreshActions()
    {
        _items.Clear();
        _promptList.Controls.Clear();
        _skillList.Controls.Clear();

        var config = _configManager.Config.Ai;
        foreach (var prompt in config.PromptPresets)
        {
            var selection = new AiActionSelection
            {
                Kind = AiActionKind.Prompt,
                Id = prompt.Id,
                Name = prompt.Name
            };
            AddActionItem(_promptList, selection);
        }

        foreach (var skill in config.Skills)
        {
            var selection = new AiActionSelection
            {
                Kind = AiActionKind.Skill,
                Id = skill.Id,
                Name = skill.Name,
                StepCount = skill.Steps.Count
            };
            AddActionItem(_skillList, selection);
        }

        if (_promptList.Controls.Count == 0)
            _promptList.Controls.Add(CreateEmptyLabel("暂无 Prompt"));
        if (_skillList.Controls.Count == 0)
            _skillList.Controls.Add(CreateEmptyLabel("暂无 Skill"));
    }

    private void AddActionItem(FlowLayoutPanel host, AiActionSelection selection)
    {
        var item = new ActionItemControl(selection)
        {
            Width = Math.Max(UiScaleHelper.Scale(this, 160), host.ClientSize.Width - UiScaleHelper.Scale(this, 4)),
            Height = UiScaleHelper.Scale(this, selection.IsSkill ? 58 : 48),
            Margin = new Padding(0, 0, 0, UiScaleHelper.Scale(this, 6))
        };
        _items.Add(item);
        host.Controls.Add(item);
    }

    private ActionItemControl? GetItemAtScreenPoint(Point screenPt)
        => _items.FirstOrDefault(item => item.RectangleToScreen(item.ClientRectangle).Contains(screenPt));

    private void EnsurePopupSizeForScreen(Screen screen)
    {
        var preferred = UiScaleHelper.ScaleSize(this, PickerLogicalSize);
        var margin = UiScaleHelper.Scale(this, 8);
        Size = new Size(
            Math.Min(preferred.Width, screen.WorkingArea.Width - margin * 2),
            Math.Min(preferred.Height, screen.WorkingArea.Height - margin * 2));
        _root.PerformLayout();
        _promptList.PerformLayout();
        _skillList.PerformLayout();

        foreach (var item in _items)
        {
            var hostWidth = item.Parent?.ClientSize.Width ?? item.Width;
            item.Width = Math.Max(UiScaleHelper.Scale(this, 160), hostWidth - UiScaleHelper.Scale(this, 4));
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var pen = new Pen(Color.FromArgb(210, 210, 210));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private static Label CreateHeader(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
        ForeColor = Color.FromArgb(45, 45, 45),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 8)
    };

    private static FlowLayoutPanel CreateListPanel() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(0),
        Margin = new Padding(0)
    };

    private static Label CreateEmptyLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 36,
        Dock = DockStyle.Top,
        ForeColor = Color.FromArgb(140, 140, 140),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0)
    };

    private sealed class ActionItemControl : Control
    {
        private bool _highlighted;

        public ActionItemControl(AiActionSelection selection)
        {
            Selection = selection;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        public AiActionSelection Selection { get; }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;

            _highlighted = highlighted;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.White);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var backBrush = new SolidBrush(_highlighted ? Color.FromArgb(45, 45, 45) : Color.FromArgb(247, 247, 248));
            using var borderPen = new Pen(_highlighted ? Color.FromArgb(45, 45, 45) : Color.FromArgb(220, 224, 230));
            using var path = CreateRoundRect(rect, 6);
            e.Graphics.FillPath(backBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            var titleColor = _highlighted ? Color.White : Color.FromArgb(35, 35, 35);
            var metaColor = _highlighted ? Color.FromArgb(220, 220, 220) : Color.FromArgb(120, 120, 120);
            var paddingX = LogicalScale(10);
            var titleTop = LogicalScale(9);
            var metaTop = LogicalScale(32);
            var titleHeight = Selection.IsSkill ? LogicalScale(22) : Height - LogicalScale(18);
            var titleBounds = new Rectangle(paddingX, titleTop, Width - paddingX * 2, titleHeight);
            TextRenderer.DrawText(
                e.Graphics,
                Selection.Name,
                Font,
                titleBounds,
                titleColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (Selection.IsSkill)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    $"{Selection.StepCount} 步",
                    new Font(Font.FontFamily, Math.Max(7f, Font.Size - 1f)),
                    new Rectangle(paddingX, metaTop, Width - paddingX * 2, LogicalScale(18)),
                    metaColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private int LogicalScale(int logicalPixels)
            => (int)Math.Round(logicalPixels * DeviceDpi / 96f, MidpointRounding.AwayFromZero);

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRect(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
