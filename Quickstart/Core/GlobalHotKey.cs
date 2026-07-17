namespace Quickstart.Core;

using System.Runtime.InteropServices;

internal sealed class GlobalHotKey : NativeWindow, IDisposable
{
    private const int HotKeyId = 0x5153;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private bool _registered;
    private bool _disposed;

    public event Action? Pressed;

    public GlobalHotKey()
    {
        CreateHandle(new CreateParams
        {
            Caption = "Quickstart.GlobalHotKey",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });
    }

    public bool TryRegister(string? gesture, out string error)
    {
        Unregister();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
            return true;

        if (!TryParse(gesture, out var modifiers, out var key, out error))
            return false;

        if (!RegisterHotKey(Handle, HotKeyId, modifiers | ModNoRepeat, (uint)key))
        {
            error = $"快捷键注册失败，可能已被其他程序占用（错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        _registered = true;
        return true;
    }

    public static bool TryParse(string? gesture, out uint modifiers, out Keys key, out string error)
    {
        modifiers = 0;
        key = Keys.None;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
            return true;

        foreach (var rawPart in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    continue;
                case "SHIFT":
                    modifiers |= ModShift;
                    continue;
                case "ALT":
                    modifiers |= ModAlt;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    continue;
            }

            if (key != Keys.None || !Enum.TryParse(rawPart, ignoreCase: true, out key))
            {
                error = $"无法识别快捷键“{rawPart}”。示例：Ctrl+Shift+Space。";
                return false;
            }

            key &= Keys.KeyCode;
        }

        if (key == Keys.None || key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            error = "快捷键必须包含一个非修饰键，例如 Space、Q 或 F8。";
            return false;
        }

        if (modifiers == 0 && key is < Keys.F1 or > Keys.F24)
        {
            error = "普通按键必须搭配 Ctrl、Shift、Alt 或 Win，避免拦截正常输入。";
            return false;
        }

        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotKeyId)
            Pressed?.Invoke();

        base.WndProc(ref m);
    }

    private void Unregister()
    {
        if (!_registered)
            return;

        UnregisterHotKey(Handle, HotKeyId);
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
