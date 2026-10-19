// Run: dotnet run --file scripts/check-radial-navigation.cs
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
using Quickstart.UI;

static class RadialNavigationCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type PopupType = typeof(ProcessLauncher).Assembly.GetType("Quickstart.UI.RadialMenuPopup")!;
    private static object Get(object p, string name) => p.GetType().GetField(name, Private)!.GetValue(p)!;
    private static void Set(object p, string name, object value) => p.GetType().GetField(name, Private)!.SetValue(p, value);
    private static object? Call(object p, string name, params object?[] args) => p.GetType().GetMethod(name, Private)!.Invoke(p, args);
    private static object[] Items(object p) => ((IEnumerable)Get(p, "_items")).Cast<object>().ToArray();
    private static T Property<T>(object item, string name) => (T)item.GetType().GetProperty(name)!.GetValue(item)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static int Hit(object p, Point point)
    {
        object?[] args = [point, null, null, -1];
        Call(p, "HitTest", args);
        return (int)args[3]!;
    }

    [STAThread]
    private static void Main()
    {
        AppPaths.SetRootForTests(Path.Combine(Path.GetTempPath(), "Quickstart-radial-check-" + Guid.NewGuid()));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.MainPopupTabOrder = ["Urls", "RecentItems", "Folders", "ClipboardHistory", "Texts", "Files"];
        var launcher = new ProcessLauncher(config);

        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        foreach (var count in new[] { 2, 97 })
        {
            config.Config.Entries.Clear();
            for (var i = 0; i < count; i++)
                config.Config.Entries.Add(new QuickEntry { Name = $"File {i}", Type = EntryType.File,
                    Group = i % 2 == 0 ? "A" : "B" });
            using var popup = (Form)Activator.CreateInstance(PopupType, config, launcher, null)!;
            popup.Show();
            Set(popup, "_dpiScale", scale);
            popup.Size = new Size((int)(480 * scale), (int)(430 * scale));
            Set(popup, "_center", Call(popup, "ComputeWheelCenter")!);
            Call(popup, "RebuildTabs");
            var center = (PointF)Get(popup, "_center");
            Point Polar(float r, float deg) => Point.Round(new PointF(center.X + scale * r * MathF.Cos(deg * MathF.PI / 180),
                center.Y + scale * r * MathF.Sin(deg * MathF.PI / 180)));
            void Click(Point point, MouseButtons button = MouseButtons.Left) =>
                Call(popup, "OnInteractiveMouseDown", null, new MouseEventArgs(button, 1, point.X, point.Y, 0));
            void Hover(Point point) => Call(popup, "ApplyPointerAtClientPoint", point);
            void Release() => PopupType.GetMethod("EnterInteractiveMode")!.Invoke(popup, null);

            Release();
            Check(popup.Visible && !(bool)Get(popup, "_gestureMode"), "Release must keep popup open");
            Click(Polar(80, 75));
            Check(Get(popup, "_activeTab").ToString() == "Files", "Cannot click Files");
            Check(Items(popup).Length == count, "Items were truncated");
            Check(Items(popup).Count(i => Property<bool>(i, "IconRequested")) == Math.Min(count, 8), "Icons must load only for visible rows");

            // Hovering across the former protection corridor never replaces the selected list.
            Hover(Polar(80, 45));
            Check(Get(popup, "_hoverTab").ToString() == "Texts", "Hover should respond immediately");
            Check(Get(popup, "_activeTab").ToString() == "Files", "Hover changed active category");
            Click(Polar(80, 45));
            Check(Get(popup, "_activeTab").ToString() == "Texts", "Click blocked by stale navigation state");
            Click(Polar(80, 75));

            var list = (RectangleF)Get(popup, "_listPanelBounds");
            Check(Hit(popup, Point.Round(new PointF(list.Left + 20 * scale, list.Top - 20 * scale))) == -1,
                "Blank area above list must not hit a file");
            var first = Property<RectangleF>(Items(popup)[0], "TargetBounds");
            var rowPoint = Point.Round(new PointF(first.Left + 20 * scale, first.Top + 20 * scale));
            Check(Hit(popup, rowPoint) == 0, "Visible first row must be clickable");
            Check(Hit(popup, Point.Round(new PointF(first.Left + 20 * scale, first.Bottom + 2 * scale))) == -1,
                "Row gap must not execute nearest item");
            Click(rowPoint, MouseButtons.Right);
            Check(popup.Visible, "Right click must not execute a radial entry");

            // Repeated edge movement and release do not scroll or execute.
            var blank = Point.Round(new PointF(list.Left + 20 * scale, list.Bottom + 40 * scale));
            for (var i = 0; i < 30; i++) Hover(blank);
            Release();
            Check(popup.Visible && (int)Get(popup, "_scrollIndex") == 0, "Movement or release scrolled the list");
            Call(popup, "TryScrollList", count, null);
            Check((int)Get(popup, "_scrollIndex") == Math.Max(0, count - 8), "Cannot reach last page");
            var last = Property<RectangleF>(Items(popup)[^1], "TargetBounds");
            var lastPoint = Point.Round(new PointF(last.Left + 20 * scale, last.Top + 20 * scale));
            Check(Hit(popup, lastPoint) == count - 1, "Final item cannot be selected");
            Hover(lastPoint);
            Release();
            Check(popup.Visible && (int)Get(popup, "_scrollIndex") == Math.Max(0, count - 8), "Release changed viewport or executed item");

            var groupA = ((IEnumerable)Get(popup, "_groups")).Cast<object>().First(g => Property<string>(g, "Name") == "A");
            Click(Polar(122, Property<float>(groupA, "MidDeg")));
            Check((string)Get(popup, "_activeGroup") == "A" && Items(popup).Length == (count + 1) / 2, "Group filtering failed");
            Hover(Polar(80, 45));
            Check((string)Get(popup, "_activeGroup") == "A", "Hover changed active group");
            Check(((IEnumerable)Get(popup, "_groups")).Cast<object>().All(g => Property<object>(g, "ParentTab").ToString() == "Files"), "Hover replaced group ring");

            // Null payload checks the confirm-and-close path without opening files or using clipboard.
            var item = Items(popup)[0];
            item.GetType().GetProperty("Payload")!.SetValue(item, null);
            first = Property<RectangleF>(item, "TargetBounds");
            Click(Point.Round(new PointF(first.Left + 20 * scale, first.Top + 20 * scale)));
            Check(!popup.Visible, "Left click did not confirm selection");
        }

        using var classic = new MainPopup(config, launcher);
        classic.ShowAtGesturePoint(new Point(500, 350));
        var files = Enum.Parse(classic.GetType().GetNestedType("TabKind", BindingFlags.NonPublic)!, "Files");
        Call(classic, "SwitchTab", files);
        var listView = (ListView)Get(classic, "_listView");
        listView.Items[0].Selected = true;
        var usedBefore = config.Config.Entries[0].LastUsedAt;
        classic.EnterInteractiveMode();
        Check(classic.Visible && config.Config.Entries[0].LastUsedAt == usedBefore, "Classic release executed an item");
        object?[] escape = [new Message(), Keys.Escape];
        Call(classic, "ProcessCmdKey", escape);
        Check(!classic.Visible, "Escape must dismiss classic panel");
        Console.WriteLine("PASS: click categories/groups, release stays open, strict row hits, right-click safety, 97 items, visible icons, stable scrolling and Escape; 100/125/150/200% geometry.");
    }
}
