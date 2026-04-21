namespace Quickstart.Utils;

using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

/// <summary>
/// Applies a consistent rounded-corner treatment to application-owned forms.
/// </summary>
public static class FormStyler
{
    public const int StandardCornerRadius = 10;
    private const int DwmWindowCornerPreferenceAttribute = 33;

    public static void ApplyRounded(Form form, int logicalCornerRadius = StandardCornerRadius)
    {
        ArgumentNullException.ThrowIfNull(form);

        void RefreshRoundedAppearance()
        {
            bool appliedBySystem = TryApplySystemRoundedCorners(form.Handle);
            if (appliedBySystem)
            {
                ClearRegion(form);
            }
            else
            {
                UpdateRoundedRegion(form, logicalCornerRadius);
            }
        }

        form.HandleCreated += (_, _) => RefreshRoundedAppearance();
        form.Resize += (_, _) => UpdateRoundedRegionIfNeeded(form, logicalCornerRadius);
        form.DpiChanged += (_, _) => RefreshRoundedAppearance();
        form.FormClosed += (_, _) => ClearRegion(form);

        if (form.IsHandleCreated)
            RefreshRoundedAppearance();
    }

    private static void UpdateRoundedRegionIfNeeded(Form form, int logicalCornerRadius)
    {
        if (TryApplySystemRoundedCorners(form.Handle))
            return;

        UpdateRoundedRegion(form, logicalCornerRadius);
    }

    private static void UpdateRoundedRegion(Form form, int logicalCornerRadius)
    {
        if (form.Width <= 0 || form.Height <= 0)
            return;

        int radius = Math.Min(
            UiScaleHelper.Scale(form, logicalCornerRadius),
            Math.Min(form.Width, form.Height) / 2);

        if (radius <= 0)
        {
            ClearRegion(form);
            return;
        }

        using var path = CreateRoundedPath(form.ClientRectangle, radius);
        SetRegion(form, new Region(path));
    }

    private static void ClearRegion(Form form)
    {
        var oldRegion = form.Region;
        form.Region = null;
        oldRegion?.Dispose();
    }

    private static void SetRegion(Form form, Region region)
    {
        var oldRegion = form.Region;
        form.Region = region;
        oldRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool TryApplySystemRoundedCorners(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return false;

        int preference = (int)DwmWindowCornerPreference.Round;
        return DwmSetWindowAttribute(handle, DwmWindowCornerPreferenceAttribute, ref preference, sizeof(int)) == 0;
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
