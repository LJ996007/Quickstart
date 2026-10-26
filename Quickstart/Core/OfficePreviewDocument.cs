namespace Quickstart.Core;

using System.IO.Compression;

/// <summary>A bounded, immutable snapshot. No renderer receives an arbitrary local path.</summary>
public sealed record OfficePreviewDocument(string Token, string Kind, string Name, byte[] Bytes)
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public string ResourcePath => $"/document/{Token}.{Kind}";

    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".docx" or ".xlsx" or ".xls" or ".pdf";

    public static async Task<OfficePreviewDocument> ReadAsync(string path, CancellationToken token)
    {
        if (!IsSupported(path)) throw new NotSupportedException("仅支持 DOCX、XLSX、XLS 和 PDF 预览。");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("文件超过 64 MB，请用默认程序打开。");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        var kind = Path.GetExtension(path)[1..].ToLowerInvariant();
        if (kind is "docx" or "xlsx")
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
            if (zip.Entries.Count > 20000) throw new InvalidDataException("文档结构过大，请用默认程序打开。");
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                expanded += entry.Length;
                if (expanded > 256L * 1024 * 1024) throw new InvalidDataException("文档解压后过大，请用默认程序打开。");
            }
            if (zip.GetEntry(kind == "docx" ? "word/document.xml" : "xl/workbook.xml") == null)
                throw new InvalidDataException("文件格式不正确，请用默认程序打开。");
        }
        token.ThrowIfCancellationRequested();
        return new(Guid.NewGuid().ToString("N"), kind, Path.GetFileName(path), bytes);
    }
}
