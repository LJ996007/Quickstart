namespace Quickstart.Core;

using System.Runtime.InteropServices;

/// <summary>Read Windows Recent shortcuts without running or modifying their targets.</summary>
internal static class WindowsRecentFolders
{
    // Called on an STA worker. Resolve the shortcut without opening its target.
    internal static string ReadShortcutTarget(string linkPath)
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return "";
            shell = Activator.CreateInstance(type);
            return shell == null ? "" : FolderDirectoryService.NormalizePath(ReadShortcutTarget(shell, linkPath));
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { return ""; }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }

    private static string ReadShortcutTarget(object shell, string linkPath)
    {
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(linkPath);
            return ((dynamic)shortcut).TargetPath;
        }
        finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
    }

    public static Task<IReadOnlyList<FolderDirectory>> ReadAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<FolderDirectory>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
                shell = Activator.CreateInstance(type)!;
                var result = new List<FolderDirectory>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var link in new DirectoryInfo(recent).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(300))
                {
                    try
                    {
                        var target = ReadShortcutTarget(shell, link.FullName);
                        var path = FolderDirectoryService.NormalizePath(Directory.Exists(target) ? target : File.Exists(target) ? Path.GetDirectoryName(target) : "");
                        if (path.Length > 0 && seen.Add(path)) result.Add(FolderDirectoryService.FromPath(path));
                        if (result.Count == 20) break;
                    }
                    catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { }
                }
                completion.TrySetResult(result);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }) { IsBackground = true, Name = "Quickstart.WindowsRecentFolders" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
