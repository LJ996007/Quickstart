namespace Quickstart.Core;

using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Quickstart.Utils;

public enum AiCapturedInputKind
{
    Empty,
    Text,
    Files
}

public sealed class AiCapturedInput
{
    public AiCapturedInputKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public List<string> FilePaths { get; init; } = [];
    public string Warning { get; init; } = string.Empty;

    public bool HasContent => Kind != AiCapturedInputKind.Empty;
}

public sealed class AiInputCaptureService
{
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMs = 80;
    private const int ActivateRetryCount = 8;
    private const int ActivateRetryDelayMs = 30;
    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;

    // 激活来源窗口并捕获其选中内容（供左滑等场景复用，无需弹出 AI 面板）。在 UI 线程调用。
    public async Task<AiCapturedInput> CaptureFromSourceAsync(IntPtr sourceWindow, CancellationToken token, bool restoreClipboard = true)
    {
        await WaitForRightButtonReleaseAsync(token);
        token.ThrowIfCancellationRequested();
        await ActivateWindowAsync(sourceWindow, token);
        token.ThrowIfCancellationRequested();
        return await CaptureSelectionAsync(token, restoreClipboard);
    }

    private static async Task WaitForRightButtonReleaseAsync(CancellationToken token)
    {
        const int maxWaitMs = 500;
        const int stepMs = 20;
        for (var waited = 0; waited < maxWaitMs; waited += stepMs)
        {
            if ((Control.MouseButtons & MouseButtons.Right) == 0)
                return;
            await Task.Delay(stepMs, token);
        }
    }

    private static async Task ActivateWindowAsync(IntPtr sourceWindow, CancellationToken token)
    {
        if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow))
            return;

        // 先释放前台限制，再用 AttachThreadInput 强制切回微信等来源窗。
        WindowActivator.AllowAnyForeground();
        WindowActivator.TryForceForeground(sourceWindow);

        for (var attempt = 0; attempt < ActivateRetryCount; attempt++)
        {
            if (GetForegroundWindow() == sourceWindow)
                break;

            WindowActivator.TryForceForeground(sourceWindow);
            await Task.Delay(ActivateRetryDelayMs, token);
        }

        // 给目标应用一点时间处理 WM_ACTIVATE，避免立刻发 Ctrl+C 被丢弃。
        await Task.Delay(60, token);
    }

    // 在 UI 线程（STA）上调用：用 await Task.Delay 让出消息泵，避免冻结弹窗。
    public async Task<AiCapturedInput> CaptureSelectionAsync(CancellationToken token, bool restoreClipboard = true)
    {
        IDataObject? original = null;
        var shouldRestore = false;

        try
        {
            if (restoreClipboard)
            {
                original = TryGetClipboardDataObject();
                shouldRestore = original != null;
            }

            Clipboard.Clear();
            // 用 SendInput 模拟 Ctrl+C，比 SendKeys 更稳（微信等自定义控件尤其明显）
            SendCopyShortcut();
            await Task.Delay(180, token);

            var captured = await ReadClipboardContentAsync(token);
            return captured.HasContent
                ? captured
                : new AiCapturedInput
                {
                    Kind = AiCapturedInputKind.Empty,
                    Warning = "没有捕获到选中文本或文件，请手动输入。"
                };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiCapturedInput
            {
                Kind = AiCapturedInputKind.Empty,
                Warning = $"自动捕获失败：{ex.Message}"
            };
        }
        finally
        {
            if (shouldRestore && original != null)
                TryRestoreClipboard(original);
        }
    }

    private static void SendCopyShortcut()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VkControl, keyUp: false),
            CreateKeyboardInput(VkC, keyUp: false),
            CreateKeyboardInput(VkC, keyUp: true),
            CreateKeyboardInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法发送复制快捷键。");
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        type = InputKeyboard,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = keyUp ? KeyEventKeyUp : 0
            }
        }
    };

    private static async Task<AiCapturedInput> ReadClipboardContentAsync(CancellationToken token)
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var paths = Clipboard.GetFileDropList()
                        .Cast<string>()
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (paths.Count > 0)
                        return new AiCapturedInput { Kind = AiCapturedInputKind.Files, FilePaths = paths };
                }

                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (!string.IsNullOrWhiteSpace(text))
                        return new AiCapturedInput { Kind = AiCapturedInputKind.Text, Text = text };
                }
            }
            catch (ExternalException)
            {
                await Task.Delay(ClipboardRetryDelayMs, token);
                continue;
            }

            await Task.Delay(ClipboardRetryDelayMs, token);
        }

        return new AiCapturedInput { Kind = AiCapturedInputKind.Empty };
    }

    private static IDataObject? TryGetClipboardDataObject()
    {
        try
        {
            return Clipboard.GetDataObject();
        }
        catch
        {
            return null;
        }
    }

    private static void TryRestoreClipboard(IDataObject original)
    {
        try
        {
            Clipboard.SetDataObject(original, copy: true);
        }
        catch
        {
            try
            {
                if (original.GetDataPresent(DataFormats.UnicodeText)
                    && original.GetData(DataFormats.UnicodeText) is string text)
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                }
                else if (original.GetDataPresent(DataFormats.FileDrop)
                    && original.GetData(DataFormats.FileDrop) is string[] files)
                {
                    var collection = new StringCollection();
                    collection.AddRange(files);
                    Clipboard.SetFileDropList(collection);
                }
            }
            catch
            {
                // Clipboard restoration is best-effort; capture result is still usable.
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

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

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
}
