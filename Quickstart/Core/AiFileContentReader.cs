namespace Quickstart.Core;

using System.Text;

public sealed class AiFileReadResult
{
    public string Text { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
}

public sealed class AiFileContentReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".cs", ".json", ".csv", ".log", ".xml", ".yml", ".yaml",
        ".html", ".htm", ".css", ".js", ".ts", ".tsx", ".jsx", ".sql", ".ps1", ".bat",
        ".cmd", ".ini", ".config", ".sln", ".csproj", ".props", ".targets"
    };

    private static readonly HashSet<string> DeferredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff"
    };

    public AiFileReadResult ReadFiles(IEnumerable<string> paths, int maxBytesPerFile)
    {
        var warnings = new List<string>();
        var builder = new StringBuilder();
        var safeMaxBytes = Math.Clamp(maxBytesPerFile, 16 * 1024, 1024 * 1024);

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (Directory.Exists(path))
            {
                warnings.Add($"暂不直接分析文件夹：{path}");
                continue;
            }

            if (!File.Exists(path))
            {
                warnings.Add($"文件不存在：{path}");
                continue;
            }

            var extension = Path.GetExtension(path);
            if (DeferredExtensions.Contains(extension))
            {
                warnings.Add($"暂不直接解析 {extension} 文件，可先复制内容后再分析：{Path.GetFileName(path)}");
                continue;
            }

            if (!SupportedExtensions.Contains(extension))
            {
                warnings.Add($"暂不支持该文件类型：{Path.GetFileName(path)}");
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                var bytesToRead = (int)Math.Min(fileInfo.Length, safeMaxBytes);
                var buffer = new byte[bytesToRead];

                using (var stream = File.OpenRead(path))
                {
                    var read = stream.Read(buffer, 0, bytesToRead);
                    if (read < bytesToRead)
                        Array.Resize(ref buffer, read);
                }

                if (LooksBinary(buffer))
                {
                    warnings.Add($"文件看起来不是纯文本：{Path.GetFileName(path)}");
                    continue;
                }

                var text = DecodeText(buffer);
                builder.AppendLine($"===== 文件：{path} =====");
                builder.AppendLine(text);
                if (fileInfo.Length > safeMaxBytes)
                    builder.AppendLine($"[提示：文件超过 {safeMaxBytes / 1024}KB，已截断前 {safeMaxBytes / 1024}KB。]");
                builder.AppendLine();
            }
            catch (Exception ex)
            {
                warnings.Add($"读取失败：{Path.GetFileName(path)}，{ex.Message}");
            }
        }

        return new AiFileReadResult
        {
            Text = builder.ToString().Trim(),
            Warnings = warnings
        };
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch
        {
            return Encoding.Default.GetString(bytes);
        }
    }

    private static bool LooksBinary(byte[] bytes)
        => bytes.Take(Math.Min(bytes.Length, 4096)).Any(b => b == 0);
}
