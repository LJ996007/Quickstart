namespace Quickstart.Mac.Platform;

using System;
using System.Threading.Tasks;
using static Quickstart.Mac.Platform.MacInterop;

/// <summary>
/// macOS 选区捕获：向前台应用合成 Cmd+C，使选中文本进入剪贴板，随后由调用方读取剪贴板。
/// 需"辅助功能"权限。仅在 macOS 上调用。
/// </summary>
public static class MacSelectionCapture
{
    public static async Task CopySelectionAsync()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            var down = CGEventCreateKeyboardEvent(IntPtr.Zero, KeyCodeC, true);
            CGEventSetFlags(down, FlagMaskCommand);
            CGEventPost(HidEventTap, down);
            CFRelease(down);

            var up = CGEventCreateKeyboardEvent(IntPtr.Zero, KeyCodeC, false);
            CGEventSetFlags(up, FlagMaskCommand);
            CGEventPost(HidEventTap, up);
            CFRelease(up);
        }
        catch
        {
            // 合成失败（权限/环境）时忽略；调用方读取剪贴板仍可能拿到已有内容
        }

        // 等待目标应用处理复制并写入剪贴板
        await Task.Delay(180);
    }
}
