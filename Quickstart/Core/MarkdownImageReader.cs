namespace Quickstart.Core;

/// <summary>Resolve only local raster images relative to the selected Markdown file.</summary>
internal static class MarkdownImageReader
{
    internal const long MaxImageBytes = 16L * 1024 * 1024;

    internal static string ResolvePath(string markdownPath, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new IOException("图片路径为空。");
        // Strip URL query/fragment before decoding so %23 remains part of a file name.
        var end = reference.IndexOfAny(['?', '#']);
        if (end >= 0) reference = reference[..end];
        if (reference.Length >= 3 && char.IsAsciiLetter(reference[0]) && reference[1] == ':'
            && reference[2] is '/' or '\\')
            reference = Uri.UnescapeDataString(reference);
        else if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile || uri.IsUnc || !string.IsNullOrEmpty(uri.Host))
                throw new IOException("仅支持本地图片。");
            reference = uri.LocalPath;
        }
        else reference = Uri.UnescapeDataString(reference);
        var path = Path.GetFullPath(reference.Replace('/', Path.DirectorySeparatorChar),
            Path.GetDirectoryName(Path.GetFullPath(markdownPath))!);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.AsSpan(2).Contains(':'))
            throw new IOException("仅支持本地图片。");
        if (Path.GetExtension(path).ToLowerInvariant() is not
            (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico"))
            throw new IOException("不支持的图片格式。");
        return path;
    }

    internal static async Task<OfficePreviewDocument> ReadAsync(string markdownPath, string reference, CancellationToken token)
    {
        var path = ResolvePath(markdownPath, reference);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxImageBytes) throw new IOException("图片超过 16 MB。");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        return new("", Path.GetExtension(path)[1..].ToLowerInvariant(), Path.GetFileName(path), bytes);
    }
}
