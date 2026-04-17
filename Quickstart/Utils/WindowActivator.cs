namespace Quickstart.Utils;

using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class WindowActivator
{
    private const uint ASFW_ANY = 0xFFFFFFFF;
    private const int SW_RESTORE = 9;

    // 在启动进程前立即调用（需在 UI 线程调用，此时仍有前台权限）
    public static void AllowAnyForeground()
        => AllowSetForegroundWindow(ASFW_ANY);

    // 启动后异步等待窗口出现并置前台
    // windowClass: Win32 窗口类名（优先）；procName: 进程名兜底（不含.exe）
    public static void BringToFrontAsync(Process? seedProcess, string? windowClass, string? procName = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400);

                IntPtr hwnd = IntPtr.Zero;
                var deadline = Environment.TickCount64 + 3000;

                while (hwnd == IntPtr.Zero && Environment.TickCount64 < deadline)
                {
                    hwnd = ResolveWindow(seedProcess, windowClass, procName);
                    if (hwnd == IntPtr.Zero)
                        await Task.Delay(150);
                }

                if (hwnd != IntPtr.Zero)
                    ForceToFront(hwnd);
            }
            catch { }
        });
    }

    private static IntPtr ResolveWindow(Process? seedProcess, string? windowClass, string? procName)
    {
        // 1. seed process 的主窗口（TC 等直接启动的程序）
        if (seedProcess != null && !seedProcess.HasExited)
        {
            try
            {
                seedProcess.Refresh();
                var h = seedProcess.MainWindowHandle;
                if (h != IntPtr.Zero) return h;
            }
            catch { }
        }

        // 2. 窗口类名查找（Explorer: CabinetWClass）
        if (!string.IsNullOrEmpty(windowClass))
        {
            var h = FindWindow(windowClass, null);
            if (h != IntPtr.Zero) return h;
        }

        // 3. 进程名查找兜底（DOpus: dopus.exe 由 dopusrt 拉起）
        if (!string.IsNullOrEmpty(procName))
        {
            foreach (var p in Process.GetProcessesByName(procName))
            {
                try
                {
                    p.Refresh();
                    var h = p.MainWindowHandle;
                    if (h != IntPtr.Zero) return h;
                }
                catch { }
            }
        }

        return IntPtr.Zero;
    }

    private static void ForceToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
