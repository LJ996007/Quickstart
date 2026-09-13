namespace Quickstart.Utils;

using System.Runtime.InteropServices;

internal static class UiScaleHelper
{
    private const float BaseDpi = 96f;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    private static readonly object DpiCacheGate = new();
    private static IntPtr _cachedMonitor = IntPtr.Zero;
    private static int _cachedMonitorDpi = -1;

    /// <summary>
    /// 控件当前应有的 DPI。
    /// <para>
    /// 以「窗口所在显示器」的当前 DPI 为准，而不是 <c>GetDpiForWindow</c>：
    /// 隐藏的窗口在系统缩放变化后收不到 <c>WM_DPICHANGED</c>，<c>GetDpiForWindow</c>
    /// 会一直停在旧值（例如缩放从 175% 改到 125% 后仍是 168）。
    /// 那样「窗体尺寸按目标屏算、内部控件按窗口 DPI 算」两个口径不一致，
    /// 表现为内容溢出、错位或被裁切。
    /// </para>
    /// </summary>
    public static int GetDpi(Control control)
    {
        if (!control.IsHandleCreated)
            return (int)GetDpiForSystem();

        var monitor = MonitorFromWindow(control.Handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return (int)GetDpiForWindow(control.Handle);

        return GetMonitorDpiCached(monitor, control.Handle);
    }

    private static int GetMonitorDpiCached(IntPtr monitor, IntPtr fallbackHandle)
    {
        lock (DpiCacheGate)
        {
            if (_cachedMonitor == monitor && _cachedMonitorDpi > 0)
                return _cachedMonitorDpi;
        }

        var dpi = 0;
        if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            dpi = (int)dpiX;

        if (dpi <= 0)
            dpi = (int)GetDpiForWindow(fallbackHandle);

        lock (DpiCacheGate)
        {
            _cachedMonitor = monitor;
            _cachedMonitorDpi = dpi;
        }

        return dpi;
    }

    /// <summary>
    /// 丢弃显示器 DPI 缓存。显示环境（分辨率 / 缩放 / 显示器布局）变化后必须调用，
    /// 否则窗口会继续沿用变化前的 DPI。
    /// </summary>
    public static void InvalidateDeviceDpiCache()
    {
        lock (DpiCacheGate)
        {
            _cachedMonitor = IntPtr.Zero;
            _cachedMonitorDpi = -1;
        }
    }

    /// <summary>
    /// 指定显示器当前生效的 DPI。
    /// 与 <see cref="GetDpi(Control)"/> 的区别：后者读窗口所在显示器，窗口还没搬到目标屏时是旧值。
    /// 跨显示器呼出弹窗时用本方法按目标屏计算尺寸，避免先按旧缩放算一遍再被纠正。
    /// </summary>
    public static int GetDpiForScreen(Screen screen)
    {
        if (screen == null)
            return (int)GetDpiForSystem();

        try
        {
            var bounds = screen.Bounds;
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero
                && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0
                && dpiX > 0)
            {
                return (int)dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // 老系统无 shcore.dll：退回系统 DPI
        }
        catch (EntryPointNotFoundException)
        {
            // 不支持 GetDpiForMonitor：退回系统 DPI
        }

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

    /// <summary>
    /// 窗体当前所在显示器；句柄尚未创建时返回 null（此时窗口还没有 DPI 上下文）。
    /// </summary>
    public static Screen? GetScreenOf(Control control)
    {
        if (!control.IsHandleCreated)
            return null;

        try
        {
            return Screen.FromHandle(control.Handle);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把窗体移入目标显示器的可见工作区。
    /// <para>
    /// 窗口的 DPI 由它所在的显示器决定，所以跨显示器呼出时（尤其是两块屏缩放比例不同）
    /// 必须先把窗口挪到目标屏，系统才会下发 DPI 变更、后续按 DPI 计算的尺寸与字体才正确；
    /// 否则会沿用原显示器的缩放，出现尺寸错位或内容被裁切。
    /// </para>
    /// </summary>
    public static void MoveIntoWorkingArea(Form form, Screen screen, int logicalMargin = 8)
    {
        if (form == null || screen == null || !form.IsHandleCreated)
            return;

        var area = screen.WorkingArea;
        var margin = Scale(form, logicalMargin);
        var x = Math.Max(area.Left + margin, Math.Min(form.Left, area.Right - form.Width - margin));
        var y = Math.Max(area.Top + margin, Math.Min(form.Top, area.Bottom - form.Height - margin));

        var target = new Point(x, y);
        if (form.Location != target)
            form.Location = target;
    }

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
