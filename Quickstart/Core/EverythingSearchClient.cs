namespace Quickstart.Core;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>UI-thread-owned reply window; all blocking IPC and process discovery run off the UI thread.</summary>
public sealed class EverythingSearchClient : NativeWindow, IDisposable
{
    private readonly Dictionary<uint, TaskCompletionSource<byte[]>> _pending = [];
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private uint _nextId;
    private bool _disposed;

    public EverythingSearchClient() => CreateHandle(new CreateParams { Caption = "Quickstart.Everything.Replies" });

    public async Task<FileSearchPage> SearchAsync(string executable, string query, uint offset, uint sort,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        var id = ++_nextId;
        var replyWindow = Handle;
        var bytes = EverythingIpcProtocol.BuildQuery(replyWindow.ToInt64(), id, query, offset, sort);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add(id, completion);
        try
        {
            await _sendGate.WaitAsync(ct);
            try
            {
                await Task.Run(async () =>
                {
                    var target = FindServer();
                    if (target == IntPtr.Zero)
                    {
                        if (!File.Exists(executable)) executable = EverythingDetector.Detect() ?? "";
                        if (!File.Exists(executable))
                            throw new InvalidOperationException("未找到 Everything，请在设置 → 常规中指定 Everything.exe。");
                        ct.ThrowIfCancellationRequested();
                        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                        start.ArgumentList.Add("-startup");
                        using var process = Process.Start(start);
                        for (var i = 0; i < 30 && target == IntPtr.Zero; i++)
                        {
                            await Task.Delay(150, ct);
                            target = FindServer();
                        }
                    }
                    if (target == IntPtr.Zero)
                        throw new InvalidOperationException("Everything 尚未就绪。请确认客户端已运行并支持 IPC（非 Lite 版）。");
                    ct.ThrowIfCancellationRequested();
                    Send(target, replyWindow, bytes);
                }, ct);
            }
            finally { _sendGate.Release(); }
            var reply = await completion.Task.WaitAsync(ct);
            return EverythingIpcProtocol.ParseResults(reply);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Everything 查询超时，请等待索引加载完成后重试。");
        }
        finally { _pending.Remove(id); }
    }

    private static IntPtr FindServer()
    {
        var standard = FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null);
        if (standard != IntPtr.Zero) return standard;
        // Includes named instances such as Everything 1.5a.
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var name = new System.Text.StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (!name.ToString().StartsWith("EVERYTHING_TASKBAR_NOTIFICATION_(", StringComparison.Ordinal)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void Send(IntPtr target, IntPtr reply, byte[] bytes)
    {
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var data = new CopyData { Kind = (UIntPtr)18, Length = bytes.Length, Data = buffer };
            if (SendMessageTimeout(target, 0x004A, reply, ref data, 0x0002, 1500, out var accepted) == IntPtr.Zero
                || accepted == UIntPtr.Zero)
                throw new InvalidOperationException("Everything 未接受查询，请检查版本（需 1.4.1+）及两个程序的运行权限是否一致。");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x004A && m.LParam != IntPtr.Zero)
        {
            var data = Marshal.PtrToStructure<CopyData>(m.LParam);
            if (_pending.TryGetValue(unchecked((uint)data.Kind.ToUInt64()), out var completion))
            {
                if (data.Length is >= 20 and <= 16_777_216 && data.Data != IntPtr.Zero)
                {
                    var bytes = new byte[data.Length];
                    Marshal.Copy(data.Data, bytes, 0, bytes.Length);
                    completion.TrySetResult(bytes);
                }
                else completion.TrySetException(new InvalidDataException("Everything 返回的数据大小无效。"));
                m.Result = (IntPtr)1;
                return;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pending in _pending.Values) pending.TrySetCanceled();
        _pending.Clear();
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData { public UIntPtr Kind; public int Length; public IntPtr Data; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message,
        IntPtr wParam, ref CopyData data, uint flags, uint timeout, out UIntPtr result);
}
