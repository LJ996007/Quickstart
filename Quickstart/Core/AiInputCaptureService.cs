namespace Quickstart.Core;

using System.Collections.Specialized;
using System.Runtime.InteropServices;

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
    private const int ClipboardRetryCount = 5;
    private const int ClipboardRetryDelayMs = 80;

    // 在 UI 线程（STA）上调用：用 await Task.Delay 让出消息泵，避免冻结弹窗。
    public async Task<AiCapturedInput> CaptureSelectionAsync(CancellationToken token)
    {
        IDataObject? original = null;
        var restoreClipboard = false;

        try
        {
            original = TryGetClipboardDataObject();
            restoreClipboard = original != null;

            Clipboard.Clear();
            SendKeys.SendWait("^c");
            await Task.Delay(150, token);

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
            if (restoreClipboard && original != null)
                TryRestoreClipboard(original);
        }
    }

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
}
