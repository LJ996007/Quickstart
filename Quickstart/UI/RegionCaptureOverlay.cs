namespace Quickstart.UI;

using Quickstart.Utils;

/// <summary>
/// 全屏半透明框选截图。Esc / 右键取消；拖拽松手返回选区 Bitmap。
/// </summary>
internal sealed class RegionCaptureOverlay : Form
{
    private readonly Rectangle _screenBounds;
    private readonly Bitmap _snapshot;
    private bool _ready;
    private Point _start;
    private Point _current;
    private bool _dragging;
    private Rectangle _selection;
    private Bitmap? _result;

    private RegionCaptureOverlay(Rectangle bounds)
    {
        _screenBounds = bounds;
        _snapshot = CaptureScreenRect(bounds);
        // Desktop bounds, mouse coordinates and bitmap pixels use the same units.
        // This surface must never be resized by WinForms' DPI autoscaling.
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>
    /// 在指定屏幕上框选；取消返回 null。调用方负责 Dispose 返回的 Bitmap。
    /// </summary>
    public static Bitmap? CaptureRegion(Screen? screen = null)
    {
        var target = screen ?? Screen.FromPoint(Cursor.Position);
        // Screen instances retain their bounds. Resolve a fresh instance in case
        // Win+P / hot-plug changed the topology since the action was requested.
        var bounds = target.Bounds;
        var current = Screen.FromPoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        using var overlay = new RegionCaptureOverlay(current.Bounds);
        var result = overlay.ShowDialog();
        if (result != DialogResult.OK || overlay._result == null)
            return null;
        return overlay._result;
    }

    protected override void OnShown(EventArgs e)
    {
        Bounds = _screenBounds;
        _ready = true;
        base.OnShown(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Even AutoScaleMode.None accepts the OS suggested rectangle by default.
        // A captured desktop is immutable; never stretch it to a new DPI.
        e.Cancel = true;
        base.OnDpiChanged(e);
        if (_ready) CancelCapture();
    }

    protected override void WndProc(ref Message m)
    {
        const int WmDisplayChange = 0x007E;
        base.WndProc(ref m);
        if (m.Msg == WmDisplayChange && _ready) CancelCapture();
    }

    private void CancelCapture()
    {
        _dragging = false;
        Capture = false;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _snapshot.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        _dragging = true;
        Capture = true;
        _start = ClampSelectionPoint(e.Location);
        _current = _start;
        _selection = Rectangle.Empty;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            _current = ClampSelectionPoint(e.Location);
            _selection = NormalizeRect(_start, _current);
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
        {
            base.OnMouseUp(e);
            return;
        }

        _dragging = false;
        Capture = false;
        _selection = NormalizeRect(_start, ClampSelectionPoint(e.Location));

        // 过小视为误触
        var minSize = Math.Max(8, UiScaleHelper.Scale(this, 8));
        if (_selection.Width < minSize || _selection.Height < minSize)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        try
        {
            _result = _snapshot.Clone(_selection, _snapshot.PixelFormat);
            DialogResult = DialogResult.OK;
        }
        catch
        {
            _result?.Dispose();
            _result = null;
            DialogResult = DialogResult.Cancel;
        }

        Close();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawImageUnscaled(_snapshot, Point.Empty);
        using (var shade = new SolidBrush(Color.FromArgb(90, Color.Black)))
        using (var dimmed = new Region(ClientRectangle))
        {
            if (!_selection.IsEmpty) dimmed.Exclude(_selection);
            e.Graphics.FillRegion(shade, dimmed);
        }
        if (_selection.Width <= 0 || _selection.Height <= 0)
            return;

        using var pen = new Pen(Color.FromArgb(55, 138, 221), Math.Max(1f, UiScaleHelper.Scale(this, 2)));
        e.Graphics.DrawRectangle(pen, _selection.X, _selection.Y, _selection.Width - 1, _selection.Height - 1);

        var label = $"{_selection.Width} × {_selection.Height}";
        using var font = new Font("Segoe UI", 9f);
        var size = TextRenderer.MeasureText(label, font);
        var labelRect = new Rectangle(
            _selection.Left,
            Math.Max(0, _selection.Top - size.Height - 4),
            size.Width + 8,
            size.Height + 2);
        using var labelBg = new SolidBrush(Color.FromArgb(200, 30, 30, 30));
        e.Graphics.FillRectangle(labelBg, labelRect);
        TextRenderer.DrawText(e.Graphics, label, font, labelRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static Rectangle NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }

    private Point ClampSelectionPoint(Point point)
        => new(
            point.X >= ClientSize.Width - 1 ? ClientSize.Width : Math.Max(0, point.X),
            point.Y >= ClientSize.Height - 1 ? ClientSize.Height : Math.Max(0, point.Y));

    private static Bitmap CaptureScreenRect(Rectangle screenRect)
    {
        var bmp = new Bitmap(screenRect.Width, screenRect.Height);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }
}
