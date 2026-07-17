namespace Quickstart.Core;

using System.Diagnostics;
using Microsoft.Win32;

public static class EverythingDetector
{
    public static string? Detect()
    {
        var runningPath = TryRunningProcess();
        if (runningPath != null)
            return runningPath;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            var registryPath = TryAppPath(hive);
            if (registryPath != null)
                return registryPath;
        }

        foreach (var path in GetCommonPaths())
        {
            if (File.Exists(path))
                return path;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim().Trim('"'), "Everything.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }

    private static string? TryRunningProcess()
    {
        foreach (var process in Process.GetProcessesByName("Everything"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return path;
                }
                catch
                {
                    // Access to another process can be denied; continue with other methods.
                }
            }
        }

        return null;
    }

    private static string? TryAppPath(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Everything.exe");
            var value = key?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value.Trim().Trim('"')))
                return value.Trim().Trim('"');
        }
        catch
        {
            // Registry detection is best-effort.
        }

        return null;
    }

    private static IEnumerable<string> GetCommonPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "Everything", "Everything.exe");

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return Path.Combine(programFilesX86, "Everything", "Everything.exe");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            yield return Path.Combine(localAppData, "Everything", "Everything.exe");
    }
}
