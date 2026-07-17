namespace Quickstart.Core;

using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>
/// 将剪贴板中的文字转换为纯文本并粘贴到手势发起窗口。
/// 剪贴板工作放到独立 STA 线程，避免延迟渲染的 HTML/RTF 等格式阻塞 UI 和全局鼠标钩子。
/// </summary>
internal static class PlainTextPasteService
{
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMs = 60;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private static readonly SemaphoreSlim PasteGate = new(1, 1);

    public static async Task<bool> PasteAsync(IntPtr sourceWindow, CancellationToken token)
    {
        await PasteGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();

            if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow))
                throw new InvalidOperationException("手势发起窗口已经关闭。");

            // Clipboard 可能要求来源程序即时渲染数据。放到后台 STA，即使来源响应慢，
            // 也不会堵塞安装低级鼠标钩子的 UI 消息线程。
            var hasText = await ConvertClipboardToPlainTextAsync(token);
            if (!hasText)
                return false;

            token.ThrowIfCancellationRequested();
            Quickstart.Utils.WindowActivator.AllowAnyForeground();
            Quickstart.Utils.WindowActivator.TryForceForeground(sourceWindow);
            for (var attempt = 0; attempt < 8 && GetForegroundWindow() != sourceWindow; attempt++)
            {
                Quickstart.Utils.WindowActivator.TryForceForeground(sourceWindow);
                await Task.Delay(25, token);
            }

            if (GetForegroundWindow() != sourceWindow)
                throw new InvalidOperationException("无法切换回手势发起窗口。");

            SendPasteShortcut();
            return true;
        }
        finally
        {
            PasteGate.Release();
        }
    }

    private static async Task<bool> ConvertClipboardToPlainTextAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(ConvertClipboardToPlainText(token));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Quickstart 纯文本剪贴板"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await completion.Task.WaitAsync(token);
    }

    private static bool ConvertClipboardToPlainText(CancellationToken token)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    return false;

                token.ThrowIfCancellationRequested();
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(text))
                    return false;

                // 复制整行或代码块时，来源程序常会在末尾附加 CR/LF。
                // 纯文本粘贴前只清除末尾连续的回车和换行，正文内部换行保持不变。
                text = text.TrimEnd('\r', '\n');
                if (text.Length == 0)
                    return false;

                token.ThrowIfCancellationRequested();
                // 有意只保留 Unicode 文本。恢复完整 IDataObject 会同步复制 HTML、RTF、
                // 图片等延迟渲染格式，是此前造成严重卡顿的根因。
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }

            if (token.WaitHandle.WaitOne(ClipboardRetryDelayMs))
                token.ThrowIfCancellationRequested();
        }

        throw new InvalidOperationException("剪贴板正被其他程序占用，请稍后重试。", lastError);
    }

    private static void SendPasteShortcut()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VkControl, keyUp: false),
            CreateKeyboardInput(VkV, keyUp: false),
            CreateKeyboardInput(VkV, keyUp: true),
            CreateKeyboardInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法发送粘贴快捷键。");
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
