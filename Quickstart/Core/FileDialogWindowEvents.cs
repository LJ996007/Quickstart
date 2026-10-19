namespace Quickstart.Core;

using System.Runtime.InteropServices;

/// <summary>Wake the UI tracker when foreground/top-level dialogs appear; never run UIA in a hook.</summary>
internal sealed class FileDialogWindowEvents : IDisposable
{
    private readonly WinEventProc _callback;
    private readonly IntPtr _foreground, _windows;
    public event Action? Changed;
    public FileDialogWindowEvents()
    {
        _callback = OnEvent;
        _foreground = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 2); // foreground, skip own process
        _windows = SetWinEventHook(0x8000, 0x8003, IntPtr.Zero, _callback, 0, 0, 2); // create/destroy/show/hide
    }
    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (window == IntPtr.Zero || (eventType != 3 && (objectId != 0 || childId != 0))) return;
        if (eventType != 3 && GetAncestor(window, 2) != window) return;
        // Background apps/tooltips must not wake the foreground dialog detector.
        if (eventType != 3)
        {
            GetWindowThreadProcessId(window, out var pid);
            GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
            if (pid == 0 || pid != foregroundPid) return;
        }
        Changed?.Invoke();
    }
    public void Dispose()
    {
        if (_foreground != IntPtr.Zero) UnhookWinEvent(_foreground);
        if (_windows != IntPtr.Zero) UnhookWinEvent(_windows);
        GC.KeepAlive(_callback);
    }
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
