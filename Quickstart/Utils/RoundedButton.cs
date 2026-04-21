namespace Quickstart.Utils;

using System.ComponentModel;
using System.Drawing.Drawing2D;

/// <summary>
/// A lightweight owner-drawn button with anti-aliased rounded corners.
/// </summary>
public sealed class RoundedButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int CornerRadius { get; set; } = FormStyler.StandardCornerRadius;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color NormalBackColor { get; set; } = SystemColors.Control;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color HoverBackColor { get; set; } = SystemColors.ControlLight;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color PressedBackColor { get; set; } = SystemColors.ControlDark;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color NormalBorderColor { get; set; } = Color.Transparent;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color HoverBorderColor { get; set; } = Color.Transparent;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color PressedBorderColor { get; set; } = Color.Transparent;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color HighlightColor { get; set; } = Color.Transparent;

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        OnPaintBackground(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using var path = CreateRoundedPath(rect);
        using var backBrush = new SolidBrush(GetCurrentBackColor());
        e.Graphics.FillPath(backBrush, path);

        var highlightColor = GetCurrentHighlightColor();
        if (highlightColor.A > 0)
        {
            using var highlightBrush = new LinearGradientBrush(
                rect,
                highlightColor,
                Color.FromArgb(0, highlightColor),
                LinearGradientMode.Vertical);
            e.Graphics.FillPath(highlightBrush, path);
        }

        var borderColor = GetCurrentBorderColor();
        if (borderColor.A > 0)
        {
            using var borderPen = new Pen(borderColor);
            e.Graphics.DrawPath(borderPen, path);
        }

        if (Focused && ShowFocusCues)
        {
            using var focusPath = CreateRoundedPath(Rectangle.Inflate(rect, -4, -4));
            using var focusPen = new Pen(Color.FromArgb(120, ForeColor)) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focusPen, focusPath);
        }

        DrawButtonText(e.Graphics);
    }

    private void DrawButtonText(Graphics graphics)
    {
        var textBounds = Rectangle.Inflate(ClientRectangle, -6, -2);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            GetCurrentTextColor(),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix);
    }

    private GraphicsPath CreateRoundedPath(Rectangle rect)
    {
        int radius = Math.Min(UiScaleHelper.Scale(this, CornerRadius), Math.Min(rect.Width, rect.Height) / 2);
        int diameter = radius * 2;
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Color GetCurrentBackColor()
    {
        if (!Enabled)
            return BlendWithBackground(NormalBackColor, 0.42f);

        if (_isPressed)
            return PressedBackColor;

        if (_isHovered)
            return HoverBackColor;

        return NormalBackColor;
    }

    private Color GetCurrentBorderColor()
    {
        var color = _isPressed && PressedBorderColor.A > 0
            ? PressedBorderColor
            : _isHovered && HoverBorderColor.A > 0
                ? HoverBorderColor
                : NormalBorderColor;

        return !Enabled ? BlendWithBackground(color, 0.5f) : color;
    }

    private Color GetCurrentHighlightColor()
    {
        var amount = !Enabled
            ? 0.55f
            : _isPressed
                ? 0.2f
                : _isHovered
                    ? 0.1f
                    : 0f;

        return amount > 0f
            ? BlendWithBackground(HighlightColor, amount)
            : HighlightColor;
    }

    private Color GetCurrentTextColor()
    {
        return Enabled ? ForeColor : BlendWithBackground(ForeColor, 0.55f);
    }

    private Color BlendWithBackground(Color color, float amount)
    {
        if (color.A == 0)
            return color;

        var background = Parent?.BackColor ?? SystemColors.Control;
        amount = Math.Clamp(amount, 0f, 1f);

        int r = (int)Math.Round(color.R + ((background.R - color.R) * amount));
        int g = (int)Math.Round(color.G + ((background.G - color.G) * amount));
        int b = (int)Math.Round(color.B + ((background.B - color.B) * amount));
        int a = (int)Math.Round(color.A + ((255 - color.A) * amount));
        return Color.FromArgb(a, r, g, b);
    }
}
