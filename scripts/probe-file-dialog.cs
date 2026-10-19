// Read-only diagnosis of the currently foreground file dialog through the product detector.
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj
using Quickstart.Core;
using System.Runtime.InteropServices;
using System.Text;
var source = args.Length > 0 ? new IntPtr(long.Parse(args[0])) : FileDialogNavigator.CaptureForegroundWindow();
var resolved = FileDialogNavigator.ResolveDialogWindow(source);
var caption = new StringBuilder(512);
var className = new StringBuilder(256);
Native.GetWindowText(resolved, caption, caption.Capacity);
Native.GetClassName(resolved, className, className.Capacity);
Native.GetWindowThreadProcessId(resolved, out var pid);
Console.WriteLine($"Process={pid}, class={className}, caption=[{caption}]");
Console.WriteLine($"Source={source}, resolved={FileDialogNavigator.ResolveDialogWindow(source)}, candidate={FileDialogNavigator.IsPossibleDialog(source)}");
try { var target = await FileDialogNavigator.DetectAsync(source, diagnostic: Console.WriteLine); Console.WriteLine(target?.ToString() ?? "NOT DETECTED"); }
catch(Exception ex) { Console.WriteLine(ex.ToString()); }
if (args.Contains("--tree")) await Task.Run(() =>
{
    var root = System.Windows.Automation.AutomationElement.FromHandle(resolved);
    var elements = root.FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
    foreach (System.Windows.Automation.AutomationElement element in elements)
    {
        var c = element.Current;
        if (!c.IsOffscreen) Console.WriteLine($"{c.ControlType.ProgrammaticName}: class={c.ClassName}, name=[{c.Name}]");
    }
});
static class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
