namespace Quickstart.Utils;

using System.Runtime.InteropServices;

internal static class UiScaleHelper
{
    private const float BaseDpi = 96f;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static int GetDpi(Control control)
    {
        if (control.IsHandleCreated)
            return (int)GetDpiForWindow(control.Handle);

        return (int)GetDpiForSystem();
    }

    public static int Scale(Control control, int logicalPixels)
        => Scale(logicalPixels, GetDpi(control));

    public static int Scale(int logicalPixels, int dpi)
        => (int)Math.Round(logicalPixels * dpi / BaseDpi, MidpointRounding.AwayFromZero);

    public static Size ScaleSize(Control control, int logicalWidth, int logicalHeight)
        => new(Scale(control, logicalWidth), Scale(control, logicalHeight));

    public static Size ScaleSize(Control control, Size logicalSize)
        => ScaleSize(control, logicalSize.Width, logicalSize.Height);

    public static Padding ScalePadding(Control control, Padding logicalPadding)
        => new(
            Scale(control, logicalPadding.Left),
            Scale(control, logicalPadding.Top),
            Scale(control, logicalPadding.Right),
            Scale(control, logicalPadding.Bottom));

    public static int GetInputHeight(Control control, int minLogicalHeight = 30)
    {
        var minHeight = Scale(control, minLogicalHeight);

        return control switch
        {
            // Single-line TextBox paints text with a fixed top margin; if Height >
            // PreferredHeight the text looks top-aligned (not vertically centered)
            // and FixedSingle can clip the bottom border. Always use PreferredHeight.
            TextBoxBase { Multiline: false } textBox => textBox.PreferredHeight,
            TextBoxBase textBox => Math.Max(minHeight, textBox.PreferredHeight),
            ComboBox comboBox => Math.Max(minHeight, comboBox.PreferredSize.Height),
            _ => minHeight
        };
    }

    /// <summary>
    /// Locks a single-line TextBox to its native PreferredHeight so text sits
    /// correctly inside the border (vertical centering of the glyph box).
    /// </summary>
    public static int FitSingleLineTextBox(TextBox textBox)
    {
        if (textBox.Multiline)
            return textBox.Height;

        var height = textBox.PreferredHeight;
        textBox.AutoSize = false;
        textBox.MinimumSize = new Size(Math.Max(0, textBox.MinimumSize.Width), height);
        textBox.MaximumSize = textBox.MaximumSize.Width > 0
            ? new Size(textBox.MaximumSize.Width, height)
            : Size.Empty;
        textBox.Height = height;
        return height;
    }

    public static Size GetButtonSize(
        Control control,
        string text,
        Font font,
        int minLogicalWidth = 84,
        int minLogicalHeight = 34,
        int horizontalLogicalPadding = 12,
        int verticalLogicalPadding = 6)
    {
        var measured = TextRenderer.MeasureText(text, font);
        var minWidth = Scale(control, minLogicalWidth);
        var minHeight = Scale(control, minLogicalHeight);
        var width = Math.Max(minWidth, measured.Width + Scale(control, horizontalLogicalPadding * 2));
        var height = Math.Max(minHeight, measured.Height + Scale(control, verticalLogicalPadding * 2));
        return new Size(width, height);
    }

    public static int GetIconSize(Control control, int logicalSize = 16)
        => Math.Max(16, Scale(control, logicalSize));
}
