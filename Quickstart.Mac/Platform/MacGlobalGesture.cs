namespace Quickstart.Mac.Platform;

using System;
using System.Threading;
using static Quickstart.Mac.Platform.MacInterop;

public enum RightDragDirection
{
    Left,
    Right
}

/// <summary>
/// macOS 全局右键拖拽手势：在独立线程上用 CGEventTap（监听模式）观察右键按下/拖拽，
/// 横向位移超阈值即触发（右滑→主弹窗，左滑→AI）。需"输入监控/辅助功能"权限。
/// v1 采用监听模式，不打断正常右键；后续可改主动模式以吞掉右键菜单。
/// </summary>
public sealed class MacGlobalGesture : IDisposable
{
    private const int DragTriggerDx = 120;
    private const int DragTolerateDy = 50;

    private readonly CGEventTapCallBack _callback; // 防止委托被 GC
    private IntPtr _tap;
    private bool _disposed;

    private enum State { Idle, Tracking, Triggered }
    private State _state = State.Idle;
    private CGPoint _start;

    public event Action<RightDragDirection>? Triggered;

    public MacGlobalGesture() => _callback = HookCallback;

    public bool Start()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var thread = new Thread(RunTapLoop) { IsBackground = true, Name = "MacGestureTap" };
        thread.Start();
        return true;
    }

    private void RunTapLoop()
    {
        var mask = (1UL << (int)RightMouseDown)
            | (1UL << (int)RightMouseUp)
            | (1UL << (int)RightMouseDragged)
            | (1UL << (int)LeftMouseDown);

        _tap = CGEventTapCreate(SessionEventTap, HeadInsertEventTap, EventTapOptionListenOnly, mask, _callback, IntPtr.Zero);
        if (_tap == IntPtr.Zero)
            return; // 权限不足或创建失败：菜单栏/托盘仍可用

        var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, IntPtr.Zero);
        var mode = GetCFConstant("kCFRunLoopCommonModes");
        CFRunLoopAddSource(CFRunLoopGetCurrent(), source, mode);
        CGEventTapEnable(_tap, true);
        CFRunLoopRun(); // 阻塞本线程
    }

    private IntPtr HookCallback(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo)
    {
        if (type == TapDisabledByTimeout || type == TapDisabledByUserInput)
        {
            CGEventTapEnable(_tap, true);
            return cgEvent;
        }

        switch (type)
        {
            case var t when t == RightMouseDown:
                _start = CGEventGetLocation(cgEvent);
                _state = State.Tracking;
                break;

            case var t when t == RightMouseDragged && _state == State.Tracking:
                var pt = CGEventGetLocation(cgEvent);
                var dx = pt.X - _start.X;
                if (Math.Abs(dx) >= DragTriggerDx && Math.Abs(pt.Y - _start.Y) <= DragTolerateDy)
                {
                    _state = State.Triggered;
                    Triggered?.Invoke(dx < 0 ? RightDragDirection.Left : RightDragDirection.Right);
                }
                break;

            case var t when t == RightMouseUp:
                _state = State.Idle;
                break;

            case var t when t == LeftMouseDown:
                _state = State.Idle;
                break;
        }

        return cgEvent; // 监听模式：原样放行
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_tap != IntPtr.Zero)
            CGEventTapEnable(_tap, false);
    }
}
