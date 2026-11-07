namespace Quickstart.Core;

/// <summary>Lists one directory from disk, including files that have not entered the search index.</summary>
public static class LocalFolderBrowser
{
    public static Task<FileSearchPage> ReadAsync(string folder, string filter, int kind, uint offset,
        uint sort, CancellationToken token, string? selectedPath = null)
        => Task.Run(() => Read(folder, filter, kind, offset, sort, token, selectedPath), token);

    private static FileSearchPage Read(string folder, string filter, int kind, uint offset,
        uint sort, CancellationToken token, string? selectedPath = null)
    {
        token.ThrowIfCancellationRequested();
        var items = new List<FileSearchResult>();
        var directory = new DirectoryInfo(folder);
        // Root errors must reach the caller; silently ignoring them would look like an empty folder.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };
        foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            if (!entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                entry.Refresh();
                if (!entry.Exists) continue;
                var isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
                if ((kind == 1 && isFolder) || (kind == 2 && !isFolder)) continue;
                items.Add(new FileSearchResult(entry.FullName, isFolder,
                    isFolder ? null : (entry as FileInfo ?? new FileInfo(entry.FullName)).Length, entry.LastWriteTime));
            }
            catch (IOException) { /* A child may disappear while the folder is being enumerated. */ }
            catch (UnauthorizedAccessException) { /* One unreadable entry must not hide its siblings. */ }
        }

        token.ThrowIfCancellationRequested();
        var ordered = items.OrderByDescending(item => item.IsFolder);
        ordered = sort switch
        {
            14 => ordered.ThenByDescending(item => item.ModifiedAt),
            6 => ordered.ThenByDescending(item => item.Size),
            _ => ordered
        };
        var sorted = ordered.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FullPath, StringComparer.Ordinal).ToArray();
        token.ThrowIfCancellationRequested();
        var total = (uint)sorted.Length;
        // Up/back/refresh must keep the selected child visible even if its sorted position moved pages.
        if (selectedPath != null)
        {
            var selectedIndex = Array.FindIndex(sorted, item =>
                string.Equals(item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
                offset = (uint)(selectedIndex / EverythingIpcProtocol.PageSize * EverythingIpcProtocol.PageSize);
        }
        if (offset >= total)
            offset = total == 0 ? 0 : (total - 1) / EverythingIpcProtocol.PageSize * EverythingIpcProtocol.PageSize;
        return new FileSearchPage(total, offset,
            sorted.Skip((int)offset).Take(EverythingIpcProtocol.PageSize).ToArray());
    }
}
