// Run: dotnet run --file scripts/check-middle-click-panel.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Collections;
using System.Reflection;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.UI;

static class MiddleClickChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Type(string name) => typeof(MainPopup).Assembly.GetType("Quickstart.UI." + name)!;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name, Flags)!.GetValue(target)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [STAThread]
    static void Main(string[] args)
    {
        AppPaths.SetRootForTests(Path.GetFullPath("artifacts/debug/checks/middle-click/config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.Entries.Clear();
        for (int i = 0; i < 30; i++)
            config.Config.Entries.Add(new QuickEntry { Name = $"测试目录 {i + 1}", Type = EntryType.Folder, Path = AppPaths.Root });
        var launcher = new ProcessLauncher(config);
        using var source = new Form { Text = "Middle-click test source", Width = 420, Height = 200 };
        using var classic = new MainPopup(config, launcher);
        using var actions = (Form)Activator.CreateInstance(Type("AiActionPickerPopup"), config)!;
        using var host = (Form)Activator.CreateInstance(Type("MiddleClickPopup"))!;
        var showButton = new Button { Text = "中键面板", Dock = DockStyle.Top, Height = 45 };
        showButton.Click += (_, _) => Call(host, "ShowPanels", classic, actions, new Point(500, 300), source.Handle);
        source.Controls.Add(showButton);
        string? receivedQuery = null; IntPtr receivedSource = IntPtr.Zero;
        host.GetType().GetEvent("SearchRequested")!.AddEventHandler(host,
            new Action<string, IntPtr>((q, s) => { receivedQuery = q; receivedSource = s; }));
        source.Shown += async (_, _) =>
        {
            try
            {
                void Show(Form panel) => Call(host, "ShowPanels", panel, actions, new Point(500, 300), source.Handle);
                void AssertLayout(Form panel)
                {
                    Check(host.Visible && panel.Visible && actions.Visible, "All three areas must be visible");
                    var area = Screen.FromControl(host).WorkingArea;
                    foreach (var bounds in new[] { host.Bounds, panel.Bounds, actions.Bounds })
                        Check(area.Contains(bounds), "Group extends outside screen");
                    Check(!panel.Bounds.IntersectsWith(actions.Bounds), $"Panels overlap: {actions.Bounds} {panel.Bounds} bar {host.Bounds}");
                    Check(host.Bottom <= Math.Min(panel.Top, actions.Top), "Search bar must sit above both panels");
                    Check(host.Height < panel.Height && host.Height < actions.Height, "Search bar must be a separate slim bar");
                    Check(actions.Right <= panel.Left, "Actions must open left of quick launch, matching the gestures");
                    Check(actions.Top == panel.Top && actions.Height == panel.Height, "Both columns must share top edge and height");
                    Check(host.Left == actions.Left && host.Right == panel.Right, "Search bar must span both columns");
                    Check(Property<IntPtr>(panel, "SourceWindow") == source.Handle, "Original target window lost");
                }
                foreach (var panel in new Form[] { classic }) // middle-click always uses the list
                {
                    Show(panel);
                    await Task.Delay(1500); // let the slide-in animation finish
                    AssertLayout(panel);
                    Check(Field<TextBox>(host, "_query").Focused, "Search field must initially have focus");
                    foreach (var target in new[] { panel, actions, host })
                    {
                        target.Activate(); await Task.Delay(220);
                        AssertLayout(panel);
                    }
                    var offset = panel.Location - new Size(host.Location);
                    host.Location += new Size(12, 8);
                    Check(panel.Location - new Size(host.Location) == offset, "Dragging host detached child");
                    Field<TextBox>(host, "_query").Text = "  合同 ext:docx  ";
                    Call(host, "SubmitSearch");
                    Check(receivedQuery == "合同 ext:docx" && receivedSource == source.Handle, "Search text or original source lost");
                    Check(!host.Visible && !panel.Visible && !actions.Visible, "Search must dismiss entire group");
                    Check(panel.Owner == null && actions.Owner == null, "Standalone ownership not restored");
                    Show(panel);
                    var items = Field<IEnumerable>(actions, "_items").Cast<object>().ToArray();
                    Check(items.Length > 0, "Complete action list missing");
                    var selection = Property<object>(items[0], "Selection");
                    Call(actions, "RaiseActionSelected", selection, actions.Location);
                    Check(!host.Visible && !panel.Visible && !actions.Visible, "Executing action left other panels open");
                    Show(panel);
                    Call(panel, "ProcessCmdKey", new Message(), Keys.Escape);
                    Check(!host.Visible && !actions.Visible, "Child Escape must dismiss group");
                    Show(panel);
                    source.Activate(); await Task.Delay(350);
                    Check(!host.Visible && !panel.Visible && !actions.Visible, "External click must dismiss group");
                    Console.WriteLine($"PASS {panel.GetType().Name}: layout, focus, source, movement, search, action, Escape, external dismissal");
                }
                if (args.Contains("--interactive"))
                {
                    host.ShowInTaskbar = true;
                    Show(classic);
                    Console.WriteLine("READY: interactive panel (close source window to exit)");
                }
                else source.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; source.Close(); }
        };
        Application.Run(source);
    }
}
