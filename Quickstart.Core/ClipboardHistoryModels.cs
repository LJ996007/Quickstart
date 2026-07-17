namespace Quickstart.Core;

/// <summary>剪贴板历史单条（纯文本）。</summary>
public sealed class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;
    public DateTime CopiedAt { get; set; } = DateTime.Now;

    public int CharCount => Text?.Length ?? 0;

    /// <summary>列表主文案：首行预览，过长截断。</summary>
    public string Preview(int maxChars = 56)
    {
        if (string.IsNullOrEmpty(Text))
            return "(空)";

        var oneLine = Text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (oneLine.Length == 0)
            return "(空白)";
        if (oneLine.Length <= maxChars)
            return oneLine;
        return oneLine[..maxChars] + "…";
    }
}

/// <summary>落盘文件结构（与 config.json 分离）。</summary>
public sealed class ClipboardHistoryFile
{
    public List<ClipboardHistoryItem> Items { get; set; } = [];
}
