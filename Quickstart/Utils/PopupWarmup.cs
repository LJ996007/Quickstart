namespace Quickstart.Utils;

using System.Runtime.InteropServices;

/// <summary>Prepare native controls while hidden and leave the user's foreground window alone.</summary>
internal static class PopupWarmup
{
    public static void Run(Action prepare)
    {
        var foreground = GetForegroundWindow();
        try { prepare(); }
        finally
        {
            var current = GetForegroundWindow();
            GetWindowThreadProcessId(current, out var processId);
            // Undo only activation of our hidden native handles, never a user's
            // switch to another app or to one of our visible interactive windows.
            if (foreground != IntPtr.Zero && current != foreground && processId == Environment.ProcessId
                && !IsWindowVisible(current))
                WindowActivator.TryForceForeground(foreground);
        }
    }

    public static void CreateHiddenHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
}
