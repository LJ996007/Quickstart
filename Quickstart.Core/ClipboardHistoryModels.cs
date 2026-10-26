namespace Quickstart.Core;

using System.Text.Json.Serialization;

public enum ClipboardHistoryKind { Text, Files, Image }

/// <summary>剪贴板历史；旧记录未指定 Kind 时仍按文本读取。</summary>
public sealed class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;
    public ClipboardHistoryKind Kind { get; set; }
    public string[] FilePaths { get; set; } = [];
    public byte[] ImagePng { get; set; } = [];
    [JsonIgnore] public byte[] ThumbnailPng { get; set; } = [];
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public DateTime CopiedAt { get; set; } = DateTime.Now;

    [JsonIgnore] public int CharCount => Text?.Length ?? 0;
    [JsonIgnore] public string Details => Kind switch
    {
        ClipboardHistoryKind.Files => $"{FilePaths.Length} 个文件/文件夹",
        ClipboardHistoryKind.Image => $"图片 · {ImageWidth} × {ImageHeight}",
        _ => $"{CharCount} 字"
    };
    [JsonIgnore] public string SearchText => Kind == ClipboardHistoryKind.Files ? string.Join("\n", FilePaths)
        : Kind == ClipboardHistoryKind.Image ? Details : Text;

    public bool HasSameContent(ClipboardHistoryItem other) => Kind == other.Kind && Kind switch
    {
        ClipboardHistoryKind.Files => FilePaths.SequenceEqual(other.FilePaths, StringComparer.OrdinalIgnoreCase),
        ClipboardHistoryKind.Image => ImagePng.AsSpan().SequenceEqual(other.ImagePng),
        _ => string.Equals(Text, other.Text, StringComparison.Ordinal)
    };

    /// <summary>列表主文案：首行预览，过长截断。</summary>
    public string Preview(int maxChars = 56)
    {
        if (Kind == ClipboardHistoryKind.Image) return Details;
        var content = Kind == ClipboardHistoryKind.Files
            ? $"文件 · {string.Join("、", FilePaths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))))}" : Text;
        if (string.IsNullOrEmpty(content))
            return "(空)";

        var oneLine = content.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
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
