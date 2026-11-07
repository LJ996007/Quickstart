// Run: dotnet run --file scripts/check-display-environment.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Reflection;
using System.Collections;
using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.UI;

static class DisplayChecks
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(MainPopup).Assembly;
    static Type AppType(string name) => App.GetType("Quickstart." + name)!;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Instance)!.Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target)!;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [STAThread]
    static void Main()
    {
        AppPaths.SetRootForTests(Path.GetFullPath("artifacts/debug/checks/display-environment/config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        CheckResolutionMatrix();
        foreach (var screen in Screen.AllScreens)
            Console.WriteLine($"SCREEN {screen.DeviceName} bounds={screen.Bounds} work={screen.WorkingArea} dpi={AppType("Utils.UiScaleHelper").GetMethod("GetDpiForScreen", Static)!.Invoke(null, [screen])}");

        using var config = new ConfigManager();
        config.Config.Entries.Clear();
        for (var i = 1; i <= 12; i++)
            config.Config.Entries.Add(new Quickstart.Models.QuickEntry { Name = $"显示测试 {i}", Type = Quickstart.Models.EntryType.Folder, Path = AppPaths.Root });
        using var main = new MainPopup(config, new ProcessLauncher(config));
        using var actions = (Form)Activator.CreateInstance(AppType("UI.AiActionPickerPopup"), config)!;
        using var host = (Form)Activator.CreateInstance(AppType("UI.MiddleClickPopup"))!;
        using var aiClient = new AiClient();
        using var ai = new AiPopup(config, new AiInputCaptureService(), new AiFileContentReader(), aiClient);
        using var radial = (Form)Activator.CreateInstance(AppType("UI.RadialMenuPopup"), config, new ProcessLauncher(config), null)!;
        _ = main.Handle;
        // Simulate a hidden window that retained 100% DPI, then receives the current
        // monitor's DPI when shown again. Does not change Windows display settings.
        foreach (var priorDpi in new[] { 96, 144, 168, 192, 240, 96 })
        {
            ChangeDpi(main, priorDpi);
            ChangeDpi(main, (int)GetDpiForWindow(main.Handle));
            var scaledButton = Field<Button>(main, "_addButton");
            Check(Math.Abs(scaledButton.Height - 28 * main.DeviceDpi / 96d) <= 1,
                $"Toolbar scaled twice after DPI change: height={scaledButton.Height}, dpi={main.DeviceDpi}");
            _ = actions.Handle;
            ChangeDpi(actions, priorDpi);
            ChangeDpi(actions, (int)GetDpiForWindow(actions.Handle));
            var items = Field<IEnumerable>(actions, "_items").Cast<Control>().ToArray();
            Check(items.Length > 0 && items.All(item => Math.Abs(item.Height - 36 * actions.DeviceDpi / 96d) <= 1), "Action rows scaled twice");
            foreach (var popup in new Form[] { ai, radial })
            {
                _ = popup.Handle;
                Call(popup, "HandleDisplayEnvironmentChanged");
                var expected = popup.Size;
                ChangeDpi(popup, priorDpi);
                ChangeDpi(popup, (int)GetDpiForWindow(popup.Handle));
                Check(popup.Size == expected, $"{popup.GetType().Name} retained the suggested DPI rectangle instead of its content size");
            }
            Check(Math.Abs(Field<Button>(ai, "_closeButton").Height - 30 * ai.DeviceDpi / 96d) <= 1, "AI popup controls scaled twice");
        }
        Console.WriteLine("PASS repeated DPI transitions: 100%, 150%, 175%, 200%, 250% -> current monitor");
        foreach (var screen in Screen.AllScreens.Reverse().Concat(Screen.AllScreens))
        {
            var point = new Point(screen.WorkingArea.Left + screen.WorkingArea.Width / 2, screen.WorkingArea.Top + screen.WorkingArea.Height / 2);
            Call(host, "ShowPanels", main, actions, point, IntPtr.Zero);
            Application.DoEvents();
            foreach (var form in new Form[] { main, actions, host })
            {
                Console.WriteLine($"{form.GetType().Name}: bounds={form.Bounds} dpi={form.DeviceDpi}");
                Check(screen.WorkingArea.Contains(form.Bounds), $"{form.GetType().Name} outside target working area");
            }
            var addButton = Field<Button>(main, "_addButton");
            Check(Math.Abs(addButton.Height - 28 * main.DeviceDpi / 96d) <= 1, $"Toolbar scaled twice: height={addButton.Height}, dpi={main.DeviceDpi}");
            SaveRender(main, "launcher");
            SaveRender(actions, "actions");
            SaveRender(host, "search-bar");
            Call(host, "HidePanels");

            using var overlay = (Form)Activator.CreateInstance(AppType("UI.RegionCaptureOverlay"), Instance, null, [screen.Bounds], null)!;
            overlay.Show();
            Application.DoEvents();
            Console.WriteLine($"OVERLAY bounds={overlay.Bounds} client={overlay.ClientSize} dpi={overlay.DeviceDpi}");
            Check(overlay.Bounds == screen.Bounds && overlay.ClientSize == screen.Bounds.Size, "OCR overlay must cover all physical screen pixels");
            // Paint deterministic test data into the frozen desktop. The output
            // must contain every edge pixel, without the overlay's mask / border.
            var snapshot = Field<Bitmap>(overlay, "_snapshot");
            using (var graphics = Graphics.FromImage(snapshot))
            {
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Red, 0, 0, 20, 20);
                graphics.FillRectangle(Brushes.Blue, snapshot.Width - 20, snapshot.Height - 20, 20, 20);
                graphics.DrawString("OCR 全屏选区测试 12345", SystemFonts.DefaultFont, Brushes.Black, 50, 50);
            }
            Call(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Call(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, snapshot.Width - 1, snapshot.Height - 1, 0));
            SaveRender(overlay, "ocr-selection");
            Call(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, snapshot.Width - 1, snapshot.Height - 1, 0));
            using var captured = Field<Bitmap>(overlay, "_result");
            Check(captured.Size == screen.Bounds.Size, "Full-screen selection omitted edge pixels");
            Check(captured.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && captured.GetPixel(captured.Width - 1, captured.Height - 1).ToArgb() == Color.Blue.ToArgb(), "Capture includes mask, border, or shifted pixels");
            captured.Save(Path.GetFullPath("artifacts/debug/checks/display-environment/ocr-result.png"));

            using var reverse = (Form)Activator.CreateInstance(AppType("UI.RegionCaptureOverlay"), Instance, null, [screen.Bounds], null)!;
            reverse.Show();
            Application.DoEvents();
            Call(reverse, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 300, 240, 0));
            Call(reverse, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, -20, -10, 0));
            using var reverseResult = Field<Bitmap>(reverse, "_result");
            Check(reverseResult.Size == new Size(300, 240), "Reverse / out-of-bounds selection not clipped to desktop");

            foreach (var message in new[] { 0x007E, 0x02E0 })
            {
                using var cancelled = (Form)Activator.CreateInstance(AppType("UI.RegionCaptureOverlay"), Instance, null, [screen.Bounds], null)!;
                cancelled.Show();
                Application.DoEvents();
                if (message == 0x02E0) ChangeDpi(cancelled, cancelled.DeviceDpi + 24);
                else { var rect = new Rect(); SendMessage(cancelled.Handle, message, IntPtr.Zero, ref rect); }
                Check(cancelled.DialogResult == DialogResult.Cancel && !cancelled.Visible, $"Display change {message:X} must discard the stale OCR surface");
            }
        }
        // Render a stacked layout in a small work area without changing the OS
        // display settings. All categories must remain reachable by scrolling.
        var compactArea = new Rectangle(Screen.PrimaryScreen!.WorkingArea.Location, new Size(740, 600));
        var compact = AppType("Utils.PopupScreenLayout").GetMethod("Arrange", Static)!.Invoke(null,
            [compactArea, compactArea.Location, main.DeviceDpi, main.Size, actions.Size, host.Height])!;
        Rectangle CompactBounds(string name) => (Rectangle)compact.GetType().GetProperty(name)!.GetValue(compact)!;
        var compactMain = CompactBounds("Launcher");
        Call(main, "FitMiddleClickPanel", compactMain.Size);
        main.Bounds = compactMain;
        main.Show();
        Application.DoEvents();
        var tabs = Field<TableLayoutPanel>(main, "_tabLayout");
        var tabViewport = Field<Panel>(main, "_tabViewport");
        Check(tabViewport.VerticalScroll.Visible, "Compact category list must be scrollable");
        Check(!tabViewport.HorizontalScroll.Visible && tabViewport.ClientSize.Width >= Field<int>(main, "_tabContentWidth"), "Category scrollbar must not clip the category text");
        Check(tabViewport.ClientSize.Width == Field<int>(main, "_tabContentWidth"), "Category viewport must not reserve unused horizontal space");
        var compactList = Field<ListView>(main, "_listView");
        Check(compactList.Columns[0].Width <= compactList.ClientSize.Width, "Compact list must not require horizontal scrolling");
        tabViewport.AutoScrollPosition = new Point(0, tabs.Height);
        var lastTab = tabs.Controls[tabs.Controls.Count - 1];
        Check(tabViewport.RectangleToScreen(tabViewport.ClientRectangle).Contains(lastTab.RectangleToScreen(lastTab.ClientRectangle)), "Last category is unreachable on a small screen");
        SaveRender(main, "compact-launcher");
        main.Hide();
        var compactActions = CompactBounds("Actions");
        Call(actions, "FitMiddleClickPanel", compactActions.Size);
        actions.Bounds = compactActions;
        actions.Show();
        Application.DoEvents();
        SaveRender(actions, "compact-actions");
        Check(Field<FlowLayoutPanel>(actions, "_toolList").VerticalScroll.Visible, "Compact actions must be scrollable");
        actions.Hide();
        Console.WriteLine("PASS display transitions and full-screen OCR bounds");
    }

    static void SaveRender(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle);
        bitmap.Save(Path.GetFullPath($"artifacts/debug/checks/display-environment/{name}.png"));
    }

    static void CheckResolutionMatrix()
    {
        var layoutType = AppType("Utils.PopupScreenLayout");
        var arrange = layoutType.GetMethod("Arrange", Static)!;
        var fit = layoutType.GetMethod("Fit", Static)!;
        var cases = 0;
        foreach (var resolution in new[] { new Size(1024, 768), new Size(1280, 720), new Size(1366, 768), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160), new Size(1080, 1920) })
        foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
        foreach (var origin in new[] { Point.Empty, new Point(-resolution.Width, -resolution.Height) })
        {
            int S(int value) => (int)Math.Round(value * dpi / 96d, MidpointRounding.AwayFromZero);
            var area = new Rectangle(origin, new Size(resolution.Width, resolution.Height - S(48)));
            foreach (var anchor in new[] { area.Location, new Point(area.Right - 1, area.Bottom - 1), new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) })
            {
                var result = arrange.Invoke(null, [area, anchor, dpi, new Size(S(380), S(440)), new Size(S(460), S(720)), S(36)])!;
                Rectangle Read(string name) => (Rectangle)result.GetType().GetProperty(name)!.GetValue(result)!;
                var bar = Read("Bar"); var launcher = Read("Launcher"); var actions = Read("Actions");
                foreach (var bounds in new[] { bar, launcher, actions })
                    Check(bounds.Width > 0 && bounds.Height > 0 && area.Contains(bounds), $"Panel outside {resolution} at {dpi} DPI: {bounds}; area={area}");
                Check(!launcher.IntersectsWith(actions) && !bar.IntersectsWith(launcher) && !bar.IntersectsWith(actions), "Panel group overlaps");
                cases++;
            }
            var preferred = new Size(S(620), S(640));
            var fitted = (Size)fit.Invoke(null, [preferred, area.Size])!;
            Check(fitted.Width <= area.Width && fitted.Height <= area.Height, "Standalone popup exceeds the screen");
        }
        Console.WriteLine($"PASS {cases} resolution/DPI/negative-origin/edge-anchor combinations (1024×768 through 4K, 100–300%)");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref Rect rect);
    static void ChangeDpi(Form form, int dpi)
    {
        var scale = dpi / (double)form.DeviceDpi;
        var bounds = form.Bounds;
        var rect = new Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Left + (int)Math.Round(bounds.Width * scale), Bottom = bounds.Top + (int)Math.Round(bounds.Height * scale) };
        SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref rect);
    }
}
