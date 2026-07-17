namespace Quickstart.Utils;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowActivator
{
    private const uint ASFW_ANY = 0xFFFFFFFF;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const int SW_SHOWDEFAULT = 10;
    private const uint GA_ROOT = 2;
    private const uint LSFW_UNLOCK = 2;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // 在启动进程前立即调用（需在 UI 线程调用，此时仍有前台权限）
    public static void AllowAnyForeground()
        => AllowSetForegroundWindow(ASFW_ANY);

    /// <summary>
    /// 当前台已切到来源窗（微信等）后，Quickstart 不再拥有前台权限，
    /// AllowSetForegroundWindow 会失败。通过 AttachThreadInput 临时挂接前台线程，
    /// 重新拿到“允许任意进程抢前台”的能力，便于随后拉起 Everything 等窗口。
    /// </summary>
    public static void ClaimForegroundRights()
    {
        try
        {
            var foreground = GetForegroundWindow();
            var currentThread = GetCurrentThreadId();
            var foregroundThread = foreground != IntPtr.Zero
                ? GetWindowThreadProcessId(foreground, out _)
                : 0u;

            var attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);

                AllowSetForegroundWindow(ASFW_ANY);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        catch
        {
            try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
        }
    }

    /// <summary>
    /// 强制把目标窗口拉到前台。用于捕获选中文本前切回来源窗（微信等）。
    /// </summary>
    public static bool TryForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;

        // 若传入的是子窗口，提升到顶层窗口再激活
        var root = GetAncestor(hwnd, GA_ROOT);
        if (root != IntPtr.Zero)
            hwnd = root;

        if (IsForegroundRoot(hwnd) && IsWindowVisible(hwnd) && !IsIconic(hwnd))
            return true;

        try
        {
            ClaimForegroundRights();

            if (IsIconic(hwnd) || !IsWindowVisible(hwnd))
            {
                ShowWindowAsync(hwnd, SW_SHOW);
                ShowWindowAsync(hwnd, SW_RESTORE);
            }
            else
            {
                ShowWindowAsync(hwnd, SW_SHOW);
            }

            var foreground = GetForegroundWindow();
            var targetThread = GetWindowThreadProcessId(hwnd, out _);
            var foregroundThread = foreground != IntPtr.Zero
                ? GetWindowThreadProcessId(foreground, out _)
                : 0u;
            var currentThread = GetCurrentThreadId();

            var attachedToForeground = false;
            var attachedToTarget = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attachedToForeground = AttachThreadInput(currentThread, foregroundThread, true);

                if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                    attachedToTarget = AttachThreadInput(currentThread, targetThread, true);

                LockSetForegroundWindow(LSFW_UNLOCK);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SetActiveWindow(hwnd);
                SetFocus(hwnd);
            }
            finally
            {
                if (attachedToTarget)
                    AttachThreadInput(currentThread, targetThread, false);
                if (attachedToForeground)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (IsForegroundRoot(hwnd))
                return true;

            // 用户主动触发打开后，Alt 按下/抬起可释放 Windows 的前台锁。
            SendAltKeyPulse();
            BringWindowToTop(hwnd);
            ShowWindowAsync(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);

            if (IsForegroundRoot(hwnd))
                return true;

            // 瞬时提升 Z 序后立即取消置顶，避免留下永久 TopMost 状态。
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            return IsForegroundRoot(hwnd);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForegroundRoot(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        var foregroundRoot = GetAncestor(foreground, GA_ROOT);
        if (foregroundRoot == IntPtr.Zero)
            foregroundRoot = foreground;

        var targetRoot = GetAncestor(hwnd, GA_ROOT);
        if (targetRoot == IntPtr.Zero)
            targetRoot = hwnd;

        return foregroundRoot == targetRoot;
    }

    private static void SendAltKeyPulse()
    {
        try
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
            // 继续使用 Z 序兜底。
        }
    }

    // 启动后异步等待窗口出现并置前台
    // windowClass: Win32 窗口类名（优先）；procName: 进程名兜底（不含.exe）
    public static void BringToFrontAsync(Process? seedProcess, string? windowClass, string? procName = null)
    {
        _ = BringToFrontWaitAsync(seedProcess, windowClass, procName);
    }

    /// <summary>
    /// 等待目标窗口出现并强制前置。适合 Everything 这类单实例：
    /// Process.Start 可能立刻返回/无主窗口，需要按类名轮询。
    /// </summary>
    public static async Task<bool> BringToFrontWaitAsync(
        Process? seedProcess,
        string? windowClass,
        string? procName = null,
        int timeoutMs = 3000,
        int initialDelayMs = 120,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (initialDelayMs > 0)
                await Task.Delay(initialDelayMs, cancellationToken).ConfigureAwait(true);

            var deadline = Environment.TickCount64 + Math.Max(200, timeoutMs);
            IntPtr hwnd = IntPtr.Zero;

            while (Environment.TickCount64 < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hwnd = ResolveWindow(seedProcess, windowClass, procName, preferVisible: true);
                if (hwnd == IntPtr.Zero)
                    hwnd = ResolveWindow(seedProcess, windowClass, procName, preferVisible: false);

                if (hwnd != IntPtr.Zero)
                {
                    // 回到调用方同步上下文（UI 线程）再抢前台，成功率明显高于线程池
                    ClaimForegroundRights();
                    if (TryForceForeground(hwnd))
                        return true;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(true);
            }

            // 最后再试一次宽松匹配
            hwnd = ResolveWindow(seedProcess, windowClass, procName, preferVisible: false);
            if (hwnd == IntPtr.Zero)
                return false;
            ClaimForegroundRights();
            return TryForceForeground(hwnd);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr ResolveWindow(
        Process? seedProcess,
        string? windowClass,
        string? procName,
        bool preferVisible)
    {
        // 1. seed process 的主窗口（TC 等直接启动的程序）
        if (seedProcess != null)
        {
            try
            {
                if (!seedProcess.HasExited)
                {
                    seedProcess.Refresh();
                    var h = seedProcess.MainWindowHandle;
                    if (IsUsableWindow(h, preferVisible))
                        return h;
                }
            }
            catch { }
        }

        // 2. 窗口类名查找（Explorer: CabinetWClass / Everything: EVERYTHING）
        if (!string.IsNullOrEmpty(windowClass))
        {
            var byClass = FindBestWindowByClass(windowClass!, preferVisible);
            if (byClass != IntPtr.Zero)
                return byClass;
        }

        // 3. 进程名查找兜底（DOpus: dopus.exe 由 dopusrt 拉起）
        if (!string.IsNullOrEmpty(procName))
        {
            foreach (var p in Process.GetProcessesByName(procName))
            {
                try
                {
                    // 跳过会话 0 服务进程（Everything 服务）
                    if (p.SessionId == 0)
                        continue;

                    p.Refresh();
                    var h = p.MainWindowHandle;
                    if (IsUsableWindow(h, preferVisible))
                        return h;
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return IntPtr.Zero;
    }

    private static bool IsUsableWindow(IntPtr hwnd, bool preferVisible)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || IsHungAppWindow(hwnd))
            return false;
        if (!preferVisible)
            return true;
        return IsWindowVisible(hwnd) || IsIconic(hwnd);
    }

    private static IntPtr FindBestWindowByClass(string windowClass, bool preferVisible)
    {
        // 先走 FindWindow 快路径
        var quick = FindWindow(windowClass, null);
        if (IsUsableWindow(quick, preferVisible))
            return quick;

        // 枚举同名类窗口，优先可见/面积更大者（避免点到托盘/隐藏窗）
        var best = IntPtr.Zero;
        var bestScore = int.MinValue;

        EnumWindows((hwnd, _) =>
        {
            var cls = GetWindowClassName(hwnd);
            if (!cls.Equals(windowClass, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!IsWindow(hwnd) || IsHungAppWindow(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (proc.SessionId == 0)
                    return true;
            }
            catch
            {
                // 取不到进程信息时仍继续评估
            }

            var visible = IsWindowVisible(hwnd);
            var iconic = IsIconic(hwnd);
            if (preferVisible && !visible && !iconic)
                return true;

            var score = 0;
            if (visible) score += 1000;
            if (iconic) score += 500;
            if (GetWindowRect(hwnd, out var rect))
            {
                var area = Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                score += Math.Min(area, 500_000) / 1000;
            }

            var title = GetWindowTitle(hwnd);
            if (!string.IsNullOrWhiteSpace(title))
                score += 50;

            if (score > bestScore)
            {
                bestScore = score;
                best = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        if (best != IntPtr.Zero)
            return best;

        // 宽松：即使不可见也返回 FindWindow 结果，交给 ShowWindow 恢复
        return quick;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
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
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockSetForegroundWindow(uint uLockCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
