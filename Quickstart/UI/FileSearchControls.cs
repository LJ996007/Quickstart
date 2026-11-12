namespace Quickstart.UI;

using System.Drawing.Drawing2D;
using Quickstart.Utils;

/// <summary>Centers a native, accessible text input inside a flat field.</summary>
internal sealed class FileSearchField : Panel
{
    private readonly TextBox _input;
    private readonly bool _search;

    public FileSearchField(TextBox input, bool search)
    {
        _input = input;
        _search = search;
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = search ? UiTheme.Card : UiTheme.SurfaceAlt;
        input.BorderStyle = BorderStyle.None;
        input.Dock = DockStyle.None;
        input.BackColor = BackColor;
        input.ForeColor = UiTheme.TextPrimary;
        input.Margin = Padding.Empty;
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
        Controls.Add(input);
        MouseDown += (_, _) => input.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var scale = DeviceDpi / 96f;
        var left = (int)Math.Round((_search ? 36 : 12) * scale);
        var right = (int)Math.Round(12 * scale);
        _input.SetBounds(left, Math.Max(0, (Height - _input.PreferredHeight) / 2),
            Math.Max(0, Width - left - right), _input.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(_input.Focused ? UiTheme.Accent : UiTheme.BorderSubtle);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        if (!_search) return;
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(UiTheme.TextSecondary, 1.5f * scale);
        var x = 12 * scale;
        var y = Height / 2f - 6 * scale;
        e.Graphics.DrawEllipse(pen, x, y, 10 * scale, 10 * scale);
        e.Graphics.DrawLine(pen, x + 8 * scale, y + 8 * scale, x + 13 * scale, y + 13 * scale);
    }
}

internal sealed class FileSearchComboBox : ComboBox
{
    public FileSearchComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = UiTheme.Card;
        ForeColor = UiTheme.TextPrimary;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using var background = new SolidBrush(selected ? UiTheme.SelectionFill : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -(int)Math.Round(8 * DeviceDpi / 96d), 0);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds,
            selected ? UiTheme.AccentText : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal enum FileSearchGlyph { Back, Up, Maximize, Restore, Close, Pin }

/// <summary>Small vector buttons retain native Button keyboard and accessibility behavior.</summary>
internal sealed class FileSearchIconButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal FileSearchGlyph Glyph { get; set; }
    private bool _hovered;
    private bool _pressed;

    public FileSearchIconButton(FileSearchGlyph glyph, string name)
    {
        Glyph = glyph;
        AccessibleName = name;
        Text = "";
        DoubleBuffered = true;
        UiTheme.StyleFlatButton(this, UiTheme.Surface);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = _pressed ? UiTheme.ButtonPressedFill : _hovered ? UiTheme.ButtonHoverFill : BackColor;
        e.Graphics.Clear(background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var x = Width / 2f;
        var y = Height / 2f;
        var d = 5 * scale;
        using var pen = new Pen(Enabled ? UiTheme.TextSecondary : UiTheme.TextDisabled, 1.5f * scale);
        switch (Glyph)
        {
            case FileSearchGlyph.Pin:
                e.Graphics.DrawLine(pen, x - d, y - d, x + d, y - d);
                e.Graphics.DrawLines(pen, new PointF[] { new(x - d / 2, y - d), new(x - d / 2, y), new(x - d, y + d / 2), new(x + d, y + d / 2), new(x + d / 2, y), new(x + d / 2, y - d) });
                e.Graphics.DrawLine(pen, x, y + d / 2, x, y + d * 1.5f);
                break;
            case FileSearchGlyph.Back:
                e.Graphics.DrawLines(pen, new PointF[] { new(x, y - d), new(x - d, y), new(x, y + d) });
                e.Graphics.DrawLine(pen, x - d, y, x + d, y);
                break;
            case FileSearchGlyph.Up:
                e.Graphics.DrawLines(pen, new PointF[] { new(x - d, y), new(x, y - d), new(x + d, y) });
                e.Graphics.DrawLine(pen, x, y - d, x, y + d);
                break;
            case FileSearchGlyph.Close:
                e.Graphics.DrawLine(pen, x - d, y - d, x + d, y + d);
                e.Graphics.DrawLine(pen, x + d, y - d, x - d, y + d);
                break;
            case FileSearchGlyph.Restore:
                e.Graphics.DrawRectangle(pen, x - d + 3 * scale, y - d, 8 * scale, 8 * scale);
                using (var fill = new SolidBrush(background)) e.Graphics.FillRectangle(fill, x - d, y - d + 3 * scale, 8 * scale, 8 * scale);
                e.Graphics.DrawRectangle(pen, x - d, y - d + 3 * scale, 8 * scale, 8 * scale);
                break;
            default:
                e.Graphics.DrawRectangle(pen, x - d, y - d, d * 2, d * 2);
                break;
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}
