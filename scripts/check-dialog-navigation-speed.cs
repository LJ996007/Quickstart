// Isolated standard Open/Save dialog integration; does not submit files.
// dotnet run --file scripts/check-dialog-navigation-speed.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Quickstart.Core;

static class NavigationSpeedCheck
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--host")
        {
            using FileDialog dialog = args[2] == "save" ? new SaveFileDialog() : new OpenFileDialog();
            dialog.Title = "Quickstart navigation fixture " + args[2];
            dialog.InitialDirectory = args[1];
            dialog.FileName = "保留原文件名.txt";
            dialog.Filter = "文本文件|*.txt|所有文件|*.*";
            return dialog.ShowDialog() == DialogResult.Cancel ? 0 : 3;
        }
        try { RunAsync().GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static async Task RunAsync()
    {
        var root = Path.GetFullPath("artifacts/debug/checks/dialog-navigation-speed");
        var destinations = new[] { Path.Combine(root, "目标 #100% 中文"), Path.Combine(root, "第二个目录") };
        foreach (var path in destinations) Directory.CreateDirectory(path);
        FileDialogNavigator.NavigationDiagnostic = Console.WriteLine;
        foreach (var kind in new[] { "open", "save" })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var arg in new[] { "--host", root, kind }) start.ArgumentList.Add(arg);
            using var host = Process.Start(start)!;
            IntPtr window = IntPtr.Zero;
            try
            {
                for (var i = 0; i < 100 && window == IntPtr.Zero; i++)
                { await Task.Delay(100); window = FindWindow("#32770", "Quickstart navigation fixture " + kind); }
                if (window == IntPtr.Zero) throw new Exception("No fixture dialog");
                await Task.Delay(600); // Wait for the initial Shell view to finish creating controls.
                FileDialogTarget? target = null;
                for (var i = 0; i < 10 && target == null; i++)
                {
                    target = await FileDialogNavigator.DetectAsync(window, diagnostic: Console.WriteLine);
                    if (target == null) await Task.Delay(300);
                }
                if (target == null) throw new Exception($"Detection failed: hwnd={window}, possible={FileDialogNavigator.IsPossibleDialog(window)}, host={host.Id}");
                var filename = (IntPtr)typeof(FileDialogTarget).GetProperty("FileNameWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;
                var original = ReadText(filename);
                for (var handle = filename; handle != window && handle != IntPtr.Zero; handle = GetParent(handle))
                {
                    var cls = new StringBuilder(128); GetClassName(handle, cls, 128);
                    Console.WriteLine($"Filename native: {cls}, id={GetDlgCtrlID(handle)}");
                }
                if (original is not ("保留原文件名.txt" or "保留原文件名")) throw new Exception($"Fixture filename missing: hwnd={filename}, value={original}");
                foreach (var path in destinations.Concat(destinations))
                {
                    var clock = Stopwatch.StartNew();
                    await FileDialogNavigator.NavigateAsync(target, path);
                    if (ReadText(filename) != original || host.HasExited) throw new Exception("Filename changed/dialog submitted");
                    Console.WriteLine($"PASS {kind}: {clock.ElapsedMilliseconds}ms, preserved filename, {path}");
                }
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try { await FileDialogNavigator.NavigateAsync(target, destinations[0], cancelled.Token); throw new Exception("Cancelled navigation accepted"); }
                catch (OperationCanceledException) { }
                try { await FileDialogNavigator.NavigateAsync(target, Path.Combine(root, "absent")); throw new Exception("Missing folder accepted"); }
                catch (DirectoryNotFoundException) { }
                if (ReadText(filename) != original || host.HasExited) throw new Exception("Rejected navigation changed the dialog");
                Console.WriteLine($"PASS {kind}: cancellation and missing folder leave filename/dialog intact");
            }
            finally
            {
                if (window != IntPtr.Zero) PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                if (!host.HasExited && !host.WaitForExit(3000)) host.Kill();
            }
        }
    }
    static IntPtr FindFilename(IntPtr parent)
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, (window, _) =>
        {
            var name = new StringBuilder(64); GetClassName(window, name, 64);
            if (name.ToString() == "Edit" && (GetDlgCtrlID(window) is 1148 or 1152 || GetDlgCtrlID(GetParent(window)) == 1148))
            { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    static string ReadText(IntPtr window)
    { var text = new StringBuilder(2048); SendMessage(window, 0xD, (IntPtr)text.Capacity, text); return text.ToString(); }
    delegate bool EnumProc(IntPtr hwnd, IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr arg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, StringBuilder text);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
}
