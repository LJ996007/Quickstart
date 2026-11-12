// Run: dotnet run --file scripts/check-radial-display.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.Models;

static class RadialDisplayCheck
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type PopupType = typeof(ProcessLauncher).Assembly.GetType("Quickstart.UI.RadialMenuPopup")!;
    static object Get(object popup, string name) => PopupType.GetField(name, Flags)!.GetValue(popup)!;
    static object? Call(object popup, string name, params object[] args) => PopupType.GetMethod(name, Flags)!.Invoke(popup, args);
    static object[] Items(object popup) => ((IEnumerable)Get(popup, "_items")).Cast<object>().ToArray();
    static T Property<T>(object item, string name) => (T)item.GetType().GetProperty(name)!.GetValue(item)!;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    static void FinishAnimation(object popup)
    {
        foreach (var item in Items(popup)) item.GetType().GetProperty("AnimT")!.SetValue(item, 1f);
    }

    [STAThread]
    static void Main()
    {
        var root = Path.GetFullPath("artifacts/debug/checks/radial-display");
        AppPaths.SetRootForTests(Path.Combine(root, "config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.MainPopupTabOrder = ["Urls", "RecentItems", "Folders", "ClipboardHistory", "Texts", "Files"];
        config.Config.Entries.Clear();
        foreach (var type in new[] { EntryType.Url, EntryType.Folder, EntryType.Text, EntryType.File })
            for (var i = 0; i < 14; i++)
                config.Config.Entries.Add(new QuickEntry { Name = $"显示测试 {i} / Display item", Type = type,
                    Group = i % 2 == 0 ? "工作" : "A", Path = "" });
        using var popup = (Form)Activator.CreateInstance(PopupType, config, new ProcessLauncher(config), null)!;
        var screen = Screen.PrimaryScreen!;
        PopupType.GetMethod("ShowAtGesturePoint")!.Invoke(popup, [new Point(screen.WorkingArea.Left + 5, screen.WorkingArea.Top + 5), IntPtr.Zero]);
        Application.DoEvents();
        Call(popup, "RenderLayered", false);
        var bitmap = (Bitmap)Get(popup, "_layerBmp");
        var font = (Font)Get(popup, "_itemFont");
        Console.WriteLine($"screen={screen.Bounds} work={screen.WorkingArea} form={popup.Bounds} deviceDpi={popup.DeviceDpi} scale={Get(popup, "_dpiScale")} bitmapDpi={bitmap.HorizontalResolution} font={font} fontHeight={font.GetHeight(bitmap.VerticalResolution)}");
        Check(screen.WorkingArea.Contains(popup.Bounds), "First show escaped work area");
        FinishAnimation(popup);
        Call(popup, "RenderLayered", false);
        ((Bitmap)Get(popup, "_layerBmp")).Save(Path.Combine(root, "radial-current.png"));
        var nativeBitmap = (IntPtr)Get(popup, "_hLayerBmp");
        var frames = System.Diagnostics.Stopwatch.StartNew();
        for (var frame = 0; frame < 60; frame++)
        {
            Call(popup, "RenderLayered", false);
            Check((IntPtr)Get(popup, "_hLayerBmp") == nativeBitmap, "Animation reallocates its native bitmap every frame");
        }
        Check(((Bitmap)Get(popup, "_layerBmp")).GetPixel(0, 0).A == 0, "Layer background is no longer transparent");
        Console.WriteLine($"PASS reusable native layer: 60 frames in {frames.Elapsed.TotalMilliseconds:F1}ms, transparent background preserved");
        popup.Hide();

        var cases = 0;
        foreach (var grouped in new[] { true, false })
        {
            foreach (var entry in config.Config.Entries) entry.Group = grouped ? "工作" : "";
            foreach (var available in new[] { new Size(2560, 1380), new Size(1920, 1040), new Size(1366, 728),
                new Size(1024, 728), new Size(800, 560), new Size(640, 440), new Size(543, 508) })
            foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240 })
            {
                Call(popup, "ApplyContentSizeForArea", available, dpi);
                PopupType.GetField("_center", Flags)!.SetValue(popup, Call(popup, "ComputeWheelCenter"));
                Call(popup, "RelayoutGeometry");
                Check(popup.Width <= available.Width && popup.Height <= available.Height, $"Window overflow: {available}/{dpi}");
                var client = new RectangleF(PointF.Empty, popup.ClientSize);
                var panel = (RectangleF)Get(popup, "_listPanelBounds");
                Check(client.Contains(panel), $"List clipped: {available}/{dpi} {panel}");
                var center = (PointF)Get(popup, "_center");
                var outer = (float)Get(popup, "_contentOuterR");
                var hub = (float)Get(popup, "_hubR");
                Check(center.X - hub >= 0 && center.Y - outer >= 0 && center.Y + outer <= popup.Height,
                    $"Wheel clipped: {available}/{dpi}");
                Check(panel.Left > center.X + outer, "List overlaps wheel");
                var capacity = (int)Get(popup, "_visibleItemCapacity");
                Check(capacity is >= 1 and <= 8, "Invalid viewport capacity");
                var entries = Items(popup);
                Check(entries.Count(i => !Property<RectangleF>(i, "TargetBounds").IsEmpty) == Math.Min(capacity, entries.Length),
                    "Visible row count differs from scroll capacity");
                foreach (var item in entries.Where(i => !Property<RectangleF>(i, "TargetBounds").IsEmpty))
                    Check(panel.Contains(Property<RectangleF>(item, "TargetBounds")), "Row escaped panel");

                // Reproduce startup bitmap DPI remaining at 175% while geometry
                // follows a monitor changed to 125%, and exercise the reverse.
                foreach (var name in new[] { "_segmentTitleFont", "_segmentCountFont", "_groupFont", "_itemFont", "_itemSubFont", "_hubTitleFont", "_hubCountFont" })
                {
                    var currentFont = (Font)Get(popup, name);
                    Check(currentFont.Unit == GraphicsUnit.Pixel, "Layer font still depends on startup DPI");
                    SizeF? expected = null;
                    foreach (var bitmapDpi in new[] { 96, 120, 168, 240 })
                    {
                        using var sample = new Bitmap(300, 100);
                        sample.SetResolution(bitmapDpi, bitmapDpi);
                        using var graphics = Graphics.FromImage(sample);
                        var measured = graphics.MeasureString("剪贴板 50 项", currentFont);
                        if (expected.HasValue) Check(Math.Abs(expected.Value.Width - measured.Width) < .01f
                            && Math.Abs(expected.Value.Height - measured.Height) < .01f, "Bitmap DPI changed text geometry");
                        expected = measured;
                    }
                }
                Call(popup, "TryScrollList", 1000, null!);
                FinishAnimation(popup);
                var last = Property<RectangleF>(entries[^1], "TargetBounds");
                Check(!last.IsEmpty && panel.Contains(last), "Last entry is unreachable");
                object?[] hitArgs = [Point.Round(new PointF(last.Left + last.Width / 2, last.Top + last.Height / 2)), null, null, -1];
                PopupType.GetMethod("HitTest", Flags)!.Invoke(popup, hitArgs);
                Check((int)hitArgs[3]! == entries.Length - 1, "Last visible entry cannot be clicked");
                Call(popup, "TryScrollList", -1000, null!);
                cases++;
                if (grouped && available == new Size(543, 508) && dpi == 120)
                {
                    FinishAnimation(popup);
                    Call(popup, "RenderLayered", false);
                    ((Bitmap)Get(popup, "_layerBmp")).Save(Path.Combine(root, "radial-compact.png"));
                }
            }
        }
        Console.WriteLine($"PASS {cases} resolution/DPI/group combinations; startup bounds, bitmap DPI independence, full wheel/list, viewport scrolling and last-row hit testing.");
    }
}
