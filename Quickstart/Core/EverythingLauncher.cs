namespace Quickstart.Core;

using System.Diagnostics;
using Quickstart.Utils;

public static class EverythingLauncher
{
    private const int MaxQueryLength = 2048;
    private const string WindowClass = "EVERYTHING";
    private const string ProcessName = "Everything";
    private static readonly SemaphoreSlim SearchGate = new(1, 1);

    public static void Search(string executablePath, string selectedText)
    {
        // 同步入口保留兼容；内部走同一套启动逻辑。
        SearchAsync(executablePath, selectedText).GetAwaiter().GetResult();
    }

    public static async Task SearchAsync(string executablePath, string selectedText, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new FileNotFoundException("未找到 Everything.exe。", executablePath);

        var query = NormalizeQuery(selectedText);
        if (query.Length == 0)
            throw new ArgumentException("搜索内容不能为空。", nameof(selectedText));

        // 连续手势会取消上一次搜索；串行化 IPC，避免两个 Everything relay
        // 同时争抢单实例窗口，造成首次正常、后续只更新查询却不前置。
        await SearchGate.WaitAsync(token);
        try
        {
            // 捕获文本后前台通常在微信等来源窗上，这里先夺回“允许抢前台”的权限。
            WindowActivator.ClaimForegroundRights();
            WindowActivator.AllowAnyForeground();

            using var searchRelay = StartCommand(executablePath, "-search", query);

            // 单实例时 relay 常会立即退出，不能用它的 MainWindowHandle；按窗口类查找。
            var brought = await WindowActivator.BringToFrontWaitAsync(
                seedProcess: null,
                windowClass: WindowClass,
                procName: ProcessName,
                timeoutMs: 2200,
                initialDelayMs: 120,
                cancellationToken: token);

            if (!brought)
            {
                // Everything 1.4/1.5 的正式显示参数是 -show-window；旧代码中的
                // -show 并非有效开关，导致隐藏后的窗口一直无法恢复。
                WindowActivator.ClaimForegroundRights();
                WindowActivator.AllowAnyForeground();
                using var showRelay = StartCommand(executablePath, "-show-window");

                brought = await WindowActivator.BringToFrontWaitAsync(
                    seedProcess: null,
                    windowClass: WindowClass,
                    procName: ProcessName,
                    timeoutMs: 2200,
                    initialDelayMs: 80,
                    cancellationToken: token);
            }

            if (!brought)
                throw new InvalidOperationException("Everything 已收到搜索请求，但窗口无法激活。请检查窗口是否未响应。");
        }
        finally
        {
            SearchGate.Release();
        }
    }

    internal static string NormalizeQuery(string text)
    {
        var query = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return query.Length <= MaxQueryLength ? query : query[..MaxQueryLength];
    }

    private static Process StartCommand(string executablePath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Everything 未返回启动进程。");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Everything 启动失败：{ex.Message}", ex);
        }
    }
}
