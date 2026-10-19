namespace Quickstart.Core;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

/// <summary>UIA providers are untrusted synchronous calls. Kill only our reader process on timeout.</summary>
internal static class FileDialogProbe
{
    private const string Switch = "--file-dialog-probe";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // IntPtr is not supported by System.Text.Json. Keep the cross-process contract explicit.
    internal sealed record Result(long Window, uint ProcessId, bool IsWps, bool IsSave, string Title, int[] RuntimeId, long FileNameWindow);

    internal static bool HandleCommandLine(string[] args)
    {
        if (args.Length == 0 || args[0] != Switch) return false;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            if (args.Length != 3 || !long.TryParse(args[1], CultureInfo.InvariantCulture, out var handle)
                || !uint.TryParse(args[2], out var pid)) return true;
            var window = new IntPtr(handle);
            if (!FileDialogNavigator.IsLiveWindow(new(window, pid, false, false, ""))) return true;
            var target = FileDialogNavigator.DetectInWorkerAsync(window).GetAwaiter().GetResult();
            if (target != null && target.ProcessId == pid)
                Console.Write(JsonSerializer.Serialize(new Result(target.Window.ToInt64(), target.ProcessId,
                    target.IsWps, target.IsSave, target.Title, target.FileNameRuntimeId, target.FileNameWindow.ToInt64())));
        }
        catch (OperationCanceledException) { Environment.ExitCode = 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name); Environment.ExitCode = 1; }
        return true;
    }

    internal static async Task<FileDialogTarget?> DetectAsync(IntPtr source, CancellationToken token, Action<string>? diagnostic)
    {
        source = FileDialogNavigator.ResolveDialogWindow(source);
        if (!FileDialogNavigator.IsPossibleDialog(source)) return null;
        if (!await Gate.WaitAsync(0, token)) return null;
        try
        {
            // In integration harnesses use the referenced application's executable, not the harness itself.
            var executable = Path.Combine(AppContext.BaseDirectory, "Quickstart.exe");
            var pid = FileDialogNavigator.GetProcessId(source);
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Switch);
            info.ArgumentList.Add(source.ToInt64().ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            using var process = new Process { StartInfo = info };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var started = false;
            try
            {
                process.Start();
                started = true;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(deadline.Token);
                var json = await output;
                var diagnosticText = await error;
                if (!string.IsNullOrWhiteSpace(diagnosticText)) diagnostic?.Invoke(diagnosticText.Trim());
                if (process.ExitCode == 2) throw new TimeoutException("Dialog provider exceeded its query deadline.");
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json)) return null;
                var result = JsonSerializer.Deserialize<Result>(json);
                if (result == null || result.Window != source.ToInt64() || result.ProcessId != pid) return null;
                var target = new FileDialogTarget(source, pid, result.IsWps, result.IsSave, result.Title)
                    { FileNameRuntimeId = result.RuntimeId, FileNameWindow = new IntPtr(result.FileNameWindow) };
                return FileDialogNavigator.IsLiveWindow(target) ? target : null;
            }
            catch (OperationCanceledException)
            {
                diagnostic?.Invoke("Dialog probe timed out/cancelled; terminating isolated UIA reader.");
                throw;
            }
            finally
            {
                // WaitAsync alone cannot interrupt UIA. Never leave an abandoned reader behind.
                if (started && !process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                }
            }
        }
        finally { Gate.Release(); }
    }
}
