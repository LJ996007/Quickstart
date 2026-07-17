namespace Quickstart.Core;

using System.Diagnostics;

/// <summary>
/// 把渲染好的提示词送到网页对话框：写入剪贴板 → 打开网址 →（可选）等待页面加载后
/// 模拟 Ctrl+V 自动粘贴。无法直接操作浏览器 DOM，自动粘贴属尽力而为；写入剪贴板做了
/// 重试 + 读回校验 + STA 兜底，写入失败会抛异常以便上层提示。必须在 UI 线程发起调用。
/// </summary>
public static class WebPromptSender
{
    // 打开网页到输入框获得焦点的等待时间（毫秒）。网页加载/登录态不同可能需要更久。
    private const int PasteDelayMs = 3000;
    // 写入前的沉淀时间：让此前的剪贴板操作（捕获选区等）彻底释放，避免 OLE 冲突
    private const int SettleDelayMs = 150;

    public static async Task SendAsync(string text, string url, bool autoPaste, CancellationToken token)
    {
        await Task.Delay(SettleDelayMs, token);

        await SetClipboardTextAsync(text, token); // 失败会抛异常，由上层提示
        OpenUrl(url);

        if (!autoPaste)
            return;

        // 等待浏览器打开并加载、输入框自动聚焦，再模拟粘贴
        await Task.Delay(PasteDelayMs, token);
        TryPaste();
    }

    private static async Task SetClipboardTextAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 剪贴板访问要求 STA；若当前线程不是 STA，则在专用 STA 线程执行
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            await SetClipboardTextCoreAsync(text, token);
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staThread = new Thread(() =>
        {
            try
            {
                SetClipboardTextCore(text);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        await completion.Task.WaitAsync(token);
    }

    private static async Task SetClipboardTextCoreAsync(string text, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                if (Clipboard.ContainsText())
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(120, token);
        }

        throw new InvalidOperationException(
            last == null ? "写入剪贴板失败（校验未通过）。" : $"写入剪贴板失败：{last.Message}", last);
    }

    private static void SetClipboardTextCore(string text)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                // 读回校验：确认剪贴板确实存在文本（SetText 未抛即视为成功）
                if (Clipboard.ContainsText())
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            Thread.Sleep(120);
        }

        throw new InvalidOperationException(
            last == null ? "写入剪贴板失败（校验未通过）。" : $"写入剪贴板失败：{last.Message}", last);
    }

    private static void TryPaste()
    {
        try
        {
            SendKeys.SendWait("^v");
        }
        catch
        {
            // 自动粘贴失败不致命：文本已在剪贴板，用户可手动 Ctrl+V
        }
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // 打开失败忽略
        }
    }
}
