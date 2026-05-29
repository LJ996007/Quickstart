namespace Quickstart.Mac.Platform;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// macOS 原生 API（CoreGraphics / CoreFoundation）的 P/Invoke 声明。
/// 仅在 macOS 上调用；非 mac 平台不会触达这些入口。
/// </summary>
internal static class MacInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // CGEventTapLocation / placement / options
    public const uint SessionEventTap = 1;        // kCGSessionEventTap
    public const uint HeadInsertEventTap = 0;     // kCGHeadInsertEventTap
    public const uint EventTapOptionDefault = 0;     // 可消费/修改事件（需辅助功能权限）
    public const uint EventTapOptionListenOnly = 1;  // 只监听，不打断正常输入（v1 采用）

    // CGEventType
    public const uint LeftMouseDown = 1;
    public const uint RightMouseDown = 3;
    public const uint RightMouseUp = 4;
    public const uint RightMouseDragged = 27;
    public const uint OtherMouseDown = 25;
    public const uint TapDisabledByTimeout = 0xFFFFFFFE;
    public const uint TapDisabledByUserInput = 0xFFFFFFFF;

    // CGEventFlags / keycodes
    public const ulong FlagMaskCommand = 0x100000;
    public const ushort KeyCodeC = 8;             // ANSI 'C'
    public const uint HidEventTap = 0;            // kCGHIDEventTap (post location)

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventTapCreate(
        uint tap, uint place, uint options, ulong eventsOfInterest,
        CGEventTapCallBack callback, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    public static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CoreGraphics)]
    public static extern uint CGEventGetType(IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern CGPoint CGEventGetLocation(IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetFlags(IntPtr cgEvent, ulong flags);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPost(uint tap, IntPtr cgEvent);

    // 辅助功能权限（输入监控/控制）。options 传入 kAXTrustedCheckOptionPrompt=true 会弹授权框。
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    // CoreFoundation
    [DllImport(CoreFoundation)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, IntPtr order);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);

    /// <summary>读取 CoreFoundation 导出的 CFString 常量（如 kCFRunLoopCommonModes）。</summary>
    public static IntPtr GetCFConstant(string name)
    {
        var handle = NativeLibrary.Load(CoreFoundation);
        var export = NativeLibrary.GetExport(handle, name);
        return Marshal.ReadIntPtr(export);
    }
}
