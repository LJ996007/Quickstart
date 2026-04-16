namespace Quickstart.Core;

using Microsoft.Win32;

public static class DopusDetector
{
    private static readonly string[] CommonPaths =
    [
        @"C:\Program Files\GPSoftware\Directory Opus\dopus.exe",
        @"C:\Program Files (x86)\GPSoftware\Directory Opus\dopus.exe",
    ];

    public static string? Detect()
    {
        // 1. Check registry
        var regPath = TryRegistry();
        if (regPath != null) return regPath;

        // 2. Check common install paths
        foreach (var p in CommonPaths)
        {
            if (File.Exists(p)) return p;
        }

        return null;
    }

    private static string? TryRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GPSoftware\Directory Opus");
            if (key != null)
            {
                var exePath = key.GetValue("PathToExe") as string;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    return exePath;
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\GPSoftware\Directory Opus");
            if (key != null)
            {
                var exePath = key.GetValue("PathToExe") as string;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    return exePath;
            }
        }
        catch { }

        return null;
    }
}
