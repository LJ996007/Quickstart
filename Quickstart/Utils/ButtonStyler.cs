namespace Quickstart.Utils;

using System.Drawing.Drawing2D;

/// <summary>
/// Provides consistent rounded button styling across the application.
/// </summary>
public static class ButtonStyler
{
    private const int CornerRadius = 6;

    public static void ApplyPrimary(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Color.FromArgb(59, 130, 246);
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(49, 120, 236);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 110, 226);
        ApplyRounded(btn);
    }

    public static void ApplySecondary(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Color.FromArgb(245, 245, 245);
        btn.ForeColor = Color.FromArgb(55, 65, 81);
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 239, 243);
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 229, 235);
        ApplyRounded(btn, drawBorder: true);
    }

    private static void ApplyRounded(Button btn, bool drawBorder = false)
    {
        UpdateRegion(btn);
        btn.Resize += (_, _) => UpdateRegion(btn);
        if (drawBorder)
            btn.Paint += DrawRoundedBorder;
    }

    private static void UpdateRegion(Control ctrl)
    {
        if (ctrl.Width > 0 && ctrl.Height > 0)
            ctrl.Region = CreateRoundedRegion(ctrl, ctrl.Size);
    }

    private static void DrawRoundedBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Button btn) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedPath(btn, new Rectangle(0, 0, btn.Width - 1, btn.Height - 1));
        using var pen = new Pen(Color.FromArgb(200, 200, 200));
        e.Graphics.DrawPath(pen, path);
    }

    private static Region CreateRoundedRegion(Control control, Size size)
    {
        using var path = CreateRoundedPath(control, new Rectangle(0, 0, size.Width, size.Height));
        return new Region(path);
    }

    private static GraphicsPath CreateRoundedPath(Control control, Rectangle rect)
    {
        int r = Math.Min(UiScaleHelper.Scale(control, CornerRadius), Math.Min(rect.Width, rect.Height) / 2);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, r, r, 180, 90);
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
        path.CloseAllFigures();
        return path;
    }
}
