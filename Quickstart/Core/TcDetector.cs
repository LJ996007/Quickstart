namespace Quickstart.Core;

using Microsoft.Win32;

public static class TcDetector
{
    private static readonly string[] CommonPaths =
    [
        @"C:\totalcmd\TOTALCMD64.EXE",
        @"C:\totalcmd\TOTALCMD.EXE",
        @"C:\Program Files\totalcmd\TOTALCMD64.EXE",
        @"C:\Program Files\totalcmd\TOTALCMD.EXE",
        @"C:\Program Files (x86)\totalcmd\TOTALCMD.EXE",
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
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Ghisler\Total Commander");
            if (key != null)
            {
                var installDir = key.GetValue("InstallDir") as string;
                if (!string.IsNullOrEmpty(installDir))
                {
                    var exe64 = Path.Combine(installDir, "TOTALCMD64.EXE");
                    if (File.Exists(exe64)) return exe64;
                    var exe32 = Path.Combine(installDir, "TOTALCMD.EXE");
                    if (File.Exists(exe32)) return exe32;
                }
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Ghisler\Total Commander");
            if (key != null)
            {
                var installDir = key.GetValue("InstallDir") as string;
                if (!string.IsNullOrEmpty(installDir))
                {
                    var exe64 = Path.Combine(installDir, "TOTALCMD64.EXE");
                    if (File.Exists(exe64)) return exe64;
                    var exe32 = Path.Combine(installDir, "TOTALCMD.EXE");
                    if (File.Exists(exe32)) return exe32;
                }
            }
        }
        catch { }

        return null;
    }
}
