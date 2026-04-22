namespace Quickstart.Core;

using Quickstart.Models;

public static class EntryClassifier
{
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".xlsm",
        ".ppt",
        ".pptx",
        ".pdf"
    };

    public const string DocumentFileDialogFilter =
        "文档文件|*.doc;*.docx;*.xls;*.xlsx;*.xlsm;*.ppt;*.pptx;*.pdf|所有文件|*.*";

    public static bool IsDocumentPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && DocumentExtensions.Contains(Path.GetExtension(path));

    public static EntryType ClassifyPath(string path)
    {
        if (Directory.Exists(path))
            return EntryType.Folder;

        return IsDocumentPath(path) ? EntryType.Document : EntryType.File;
    }
}
