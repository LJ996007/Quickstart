// Run: dotnet run --file scripts/check-popup-performance.cs -p:NuGetAudit=false
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.UI;

static class PopupPerformanceChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(MainPopup).Assembly;
    static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Flags)!.Invoke(owner, args);
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Flags)!.GetValue(owner)!;
    static T Measure<T>(string label, Func<T> action)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var result = action();
        Console.WriteLine($"PERF {label}: {clock.Elapsed.TotalMilliseconds:F1} ms, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024d:F1} KB");
        return result;
    }
    static void Measure(string label, Action action) => Measure(label, () => { action(); return 0; });
    static int CountLayouts(Control root, Action count)
    {
        root.Layout += (_, _) => count();
        var controls = 1;
        foreach (Control child in root.Controls) controls += CountLayouts(child, count);
        return controls;
    }

    [STAThread]
    static void Main(string[] args)
    {
        AppPaths.SetRootForTests(Path.GetFullPath("artifacts/debug/checks/popup-performance/config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.Entries.Clear();
        for (var i = 0; i < 60; i++) config.Config.Entries.Add(new QuickEntry
            { Name = $"性能测试目录 {i:000}", Path = AppPaths.Root, Type = EntryType.Folder, Group = i % 2 == 0 ? "项目甲" : "项目乙" });
        var launcher = new ProcessLauncher(config);
        using var source = new Form { Text = "Popup performance fixture", Width = 400, Height = 180 };
        source.Shown += async (_, _) =>
        {
            await Task.Delay(100);
            using var main = Measure("main constructor", () => new MainPopup(config, launcher));
            using var actions = Measure("actions constructor", () => (Form)Activator.CreateInstance(App.GetType("Quickstart.UI.AiActionPickerPopup")!, config)!);
            using var host = Measure("middle constructor", () => (Form)Activator.CreateInstance(App.GetType("Quickstart.UI.MiddleClickPopup")!)!);
            var layouts = 0;
            CountLayouts(main, () => layouts++); CountLayouts(actions, () => layouts++); CountLayouts(host, () => layouts++);
            var drawRows = 0;
            Field<ListView>(main, "_listView").DrawItem += (_, _) => drawRows++;
            var point = new System.Drawing.Point(800, 400);
            if (args.Contains("--prewarm"))
                Measure("hidden prewarm", () => Call(host, "Prewarm", main, actions, point));
            var first = new List<double>();
            for (var i = 0; i < 7; i++)
            {
                layouts = drawRows = 0;
                var clock = Stopwatch.StartNew();
                Measure($"middle show {i}", () => Call(host, "ShowPanels", main, actions, point, source.Handle));
                first.Add(clock.Elapsed.TotalMilliseconds);
                await Task.Delay(50);
                Console.WriteLine($"  layout events={layouts}, row paints={drawRows}");
                Call(host, "HidePanels");
                await Task.Delay(30);
            }
            Console.WriteLine($"RESULT middle warm median={first.Skip(1).Order().ElementAt(3):F1} ms");
            Measure("phase prepare group", () => Call(host, "PreparePanels", main, actions, point, source.Handle));
            host.GetType().GetField("_arranging", Flags)!.SetValue(host, true);
            Measure("phase attach", () => { Call(host, "Attach", main); Call(host, "Attach", actions); });
            Measure("phase show bar", host.Show);
            Measure("phase show main", main.Show);
            Measure("phase show actions", actions.Show);
            Measure("phase interactive", () => Call(actions, "EnterInteractiveMode"));
            Measure("phase focus", () =>
            {
                App.GetType("Quickstart.Utils.WindowActivator")!.GetMethod("TryForceForeground")!.Invoke(null, [host.Handle]);
                Field<TextBox>(host, "_query").Focus();
            });
            host.GetType().GetField("_arranging", Flags)!.SetValue(host, false);
            Call(host, "HidePanels");
            for (var i = 0; i < 3; i++)
            {
                Measure($"main prepare {i}", () => Call(main, "PrepareAtGesturePoint", point, source.Handle));
                Measure($"actions prepare {i}", () => Call(actions, "PrepareAtGesturePoint", point));
            }
            using var search = Measure("search constructor", () => new FileSearchPopup(config, launcher));
            var warmup = App.GetType("Quickstart.Utils.PopupWarmup")!;
            var foreground = GetForegroundWindow();
            Measure("search hidden handles", () => warmup.GetMethod("Run")!.Invoke(null, [new Action(() =>
                warmup.GetMethod("CreateHiddenHandles")!.Invoke(null, [search]))]));
            if (search.Visible || GetForegroundWindow() != foreground)
                throw new Exception("Hidden search warmup displayed a window or changed foreground");
            Field<CheckBox>(search, "_previewEnabled").Checked = false;
            for (var i = 0; i < 4; i++)
            {
                Measure($"search show {i}", () => search.ShowSearch(detectDialog: false));
                await Task.Delay(50);
                search.Hide();
            }
            using var radial = (Form)Activator.CreateInstance(App.GetType("Quickstart.UI.RadialMenuPopup")!, config, launcher, null)!;
            Call(radial, "ShowAtGesturePoint", point, source.Handle);
            Field<System.Windows.Forms.Timer>(radial, "_animTimer").Stop();
            var handles = new HashSet<IntPtr>();
            Measure("radial 60 frames", () =>
            {
                for (var frame = 0; frame < 60; frame++)
                {
                    Call(radial, "RenderLayered", false);
                    handles.Add(Field<IntPtr>(radial, "_hLayerBmp"));
                }
            });
            Console.WriteLine($"RESULT radial native bitmap handles={handles.Count}");
            if (handles.Count != 1) throw new Exception("Radial rendering allocated a new native bitmap per frame");
            radial.Hide();
            source.Close();
        };
        Application.Run(source);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

}
