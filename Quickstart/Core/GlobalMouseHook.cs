namespace Quickstart.Core;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public enum RightDragDirection
{
    Left,
    Right
}

public sealed class GlobalMouseHook : IDisposable
{
    private const int DragTriggerDxLogical = 120;
    private const int DragTolerateDyLogical = 50;

    // 标记合成事件，避免钩子递归
    private static readonly UIntPtr SyntheticTag = (UIntPtr)0x51534854u; // 'QSHT'

    private enum GestureState { Idle, Tracking, PopupShown }
    private GestureState _state = GestureState.Idle;
    private Point _startPt;
    private RightDragDirection _direction = RightDragDirection.Right;
    private IntPtr _sourceWindow;
    private int _dragTriggerDx = DragTriggerDxLogical;
    private int _dragTolerateDy = DragTolerateDyLogical;
    private bool _downSuppressed;
    private bool _disposed;
    private bool _enabled = true;
    private int _dragTriggerDxLogical = DragTriggerDxLogical;
    private int _dragTolerateDyLogical = DragTolerateDyLogical;

    public event Action<RightDragDirection, Point, IntPtr>? GestureTriggered;
    public event Action<RightDragDirection, Point>? GestureMove;
    public event Action<RightDragDirection, Point>? GestureReleased;
    public event Action? GestureCancelled;

    private readonly IntPtr _hookHandle;
    private readonly LowLevelMouseProc _hookProc;

    public GlobalMouseHook()
    {
        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException($"鼠标钩子安装失败: {Marshal.GetLastWin32Error()}");
    }

    public void UpdateSettings(bool enabled, int triggerDistance, int verticalTolerance)
    {
        _enabled = enabled;
        _dragTriggerDxLogical = Math.Clamp(triggerDistance, 40, 600);
        _dragTolerateDyLogical = Math.Clamp(verticalTolerance, 10, 300);

        if (!enabled && _state != GestureState.Idle)
        {
            _state = GestureState.Idle;
            _downSuppressed = false;
        }
    }

    private unsafe IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;

            // 空闲态的鼠标移动是全系统最高频事件。此处提前返回，避免对每次移动都
            // 做结构体封送/分配（合成事件只会是右键 DOWN/UP，绝不会是 MOUSEMOVE，
            // 故空闲态 MOUSEMOVE 提前返回是安全的）。这是降低全局鼠标延迟的关键。
            if (msg == WM_MOUSEMOVE && _state == GestureState.Idle)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            // 直接从非托管内存读取（结构体为 blittable），避免 Marshal.PtrToStructure 的开销
            ref readonly var info = ref Unsafe.AsRef<MSLLHOOKSTRUCT>((void*)lParam);

            // 忽略自己合成的事件，避免递归
            if (info.dwExtraInfo == SyntheticTag)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var pt = new Point(info.pt.x, info.pt.y);

            switch (msg)
            {
                case WM_RBUTTONDOWN:
                    if (!_enabled)
                        break;

                    _startPt = pt;
                    _sourceWindow = GetForegroundWindow();
                    var dpi = _sourceWindow == IntPtr.Zero ? 96u : GetDpiForWindow(_sourceWindow);
                    if (dpi == 0) dpi = 96;
                    _dragTriggerDx = Math.Max(1, (int)Math.Round(_dragTriggerDxLogical * dpi / 96d));
                    _dragTolerateDy = Math.Max(1, (int)Math.Round(_dragTolerateDyLogical * dpi / 96d));
                    _state = GestureState.Tracking;
                    _downSuppressed = true;
                    return (IntPtr)1; // 吞掉：下层窗口不感知按下

                case WM_RBUTTONUP:
                    if (_downSuppressed)
                    {
                        _downSuppressed = false;
                        var savedState = _state;
                        _state = GestureState.Idle;

                        if (savedState == GestureState.PopupShown)
                        {
                            GestureReleased?.Invoke(_direction, pt);
                        }
                        else if (savedState == GestureState.Tracking)
                        {
                            // 未达手势阈值 → 视为普通右键点击，重放 DOWN+UP
                            SynthesizeRightClick();
                        }
                        // Idle: 被 L/M 打断取消过，只吞掉配对 UP
                        return (IntPtr)1;
                    }
                    break;

                case WM_MOUSEMOVE when _state == GestureState.Tracking:
                    var dx = pt.X - _startPt.X;
                    if (Math.Abs(dx) >= _dragTriggerDx &&
                        Math.Abs(pt.Y - _startPt.Y) <= _dragTolerateDy)
                    {
                        _direction = dx < 0 ? RightDragDirection.Left : RightDragDirection.Right;
                        _state = GestureState.PopupShown;
                        GestureTriggered?.Invoke(_direction, pt, _sourceWindow);
                    }
                    break;

                case WM_MOUSEMOVE when _state == GestureState.PopupShown:
                    GestureMove?.Invoke(_direction, pt);
                    break;

                case WM_LBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    if (_state != GestureState.Idle)
                    {
                        _state = GestureState.Idle;
                        GestureCancelled?.Invoke();
                    }
                    break;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static void SynthesizeRightClick()
    {
        // 在当前光标位置合成 DOWN+UP，不加 MOVE 避免光标跳回
        var inputs = new INPUT[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_RIGHTDOWN,
                        dwExtraInfo = SyntheticTag
                    }
                }
            },
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_RIGHTUP,
                        dwExtraInfo = SyntheticTag
                    }
                }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_hookHandle != IntPtr.Zero)
                UnhookWindowsHookEx(_hookHandle);
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
}
