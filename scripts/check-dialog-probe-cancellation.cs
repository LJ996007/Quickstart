// Run while an external file dialog is foreground. This only reads the dialog.
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Diagnostics;
using Quickstart.Core;

var source = FileDialogNavigator.CaptureForegroundWindow();
if (!FileDialogNavigator.IsPossibleDialog(source)) throw new Exception("Foreground must be a file dialog");
var workerPath = Path.Combine(AppContext.BaseDirectory, "Quickstart.exe");
using (var cancel = new CancellationTokenSource(100))
{
    try { await FileDialogNavigator.DetectAsync(source, cancel.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: probe cancelled"); }
}
foreach (var process in Process.GetProcessesByName("Quickstart"))
{
    using (process)
        if (string.Equals(process.MainModule?.FileName, workerPath, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Reader leaked after cancellation");
}
Console.WriteLine("PASS: no abandoned reader process");
var target = await FileDialogNavigator.DetectAsync(source, diagnostic: Console.WriteLine);
if (target == null) throw new Exception("Detector did not recover after cancellation");
Console.WriteLine($"PASS: detector recovered, IsSave={target.IsSave}");
