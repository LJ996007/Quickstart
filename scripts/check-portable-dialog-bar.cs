// Read-only integration check: start a standalone renamed portable, focus an external
// file dialog, then run: dotnet run --file scripts/check-portable-dialog-bar.cs -- <exe>
// No UI input, file opening/saving, or application configuration changes.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1) throw new ArgumentException("Supply the running portable EXE path.");
var path = Path.GetFullPath(args[0]);
if (Path.GetFileName(path).Equals("Quickstart.exe", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Use a renamed/versioned portable to exercise the regression.");
if (File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Quickstart.exe")))
    throw new Exception("Test directory must not contain a companion Quickstart.exe.");
using var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path))
    .Single(p => string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase));
var found = false;
var clock = Stopwatch.StartNew();
while (!found && clock.Elapsed < TimeSpan.FromSeconds(15))
{
    Native.EnumWindows((window, _) =>
    {
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid != process.Id || !Native.IsWindowVisible(window)) return true;
        var title = new StringBuilder(256);
        Native.GetWindowText(window, title, title.Capacity);
        found = title.ToString() == "Quickstart · 对话框目录助手";
        return !found;
    }, IntPtr.Zero);
    if (!found) await Task.Delay(150);
}
if (!found) throw new Exception("No visible directory bar owned by the standalone portable.");
Console.WriteLine("PASS: standalone renamed portable shows its directory bar without a sibling Quickstart.exe.");

static class Native
{
    internal delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
}
