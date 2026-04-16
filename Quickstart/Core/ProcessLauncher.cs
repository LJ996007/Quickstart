namespace Quickstart.Core;

using System.Diagnostics;
using Quickstart.Models;

public sealed class ProcessLauncher(ConfigManager configManager)
{
    public void Open(QuickEntry entry, OpenWith? overrideWith = null)
    {
        var config = configManager.Config;
        var openWith = overrideWith ?? config.DefaultOpenWith;

        configManager.TouchEntry(entry.Id);

        if (entry.Type == EntryType.File)
        {
            OpenFile(entry.Path);
            return;
        }

        // Folder
        switch (openWith)
        {
            case OpenWith.TotalCommander:
                if (!string.IsNullOrEmpty(config.TotalCommanderPath) && File.Exists(config.TotalCommanderPath))
                {
                    OpenInTotalCommander(entry.Path, config.TotalCommanderPath);
                }
                else
                {
                    OpenInExplorer(entry.Path);
                }
                break;
            case OpenWith.DirectoryOpus:
                if (!string.IsNullOrEmpty(config.DirectoryOpusPath) && File.Exists(config.DirectoryOpusPath))
                {
                    OpenInDirectoryOpus(entry.Path, config.DirectoryOpusPath);
                }
                else
                {
                    OpenInExplorer(entry.Path);
                }
                break;
            case OpenWith.Explorer:
            default:
                OpenInExplorer(entry.Path);
                break;
        }
    }

    public void OpenInTotalCommander(string path, string tcPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tcPath,
                Arguments = $"/O /T /L=\"{path}\"",
                UseShellExecute = false
            });
        }
        catch
        {
            OpenInExplorer(path);
        }
    }

    public void OpenInDirectoryOpus(string path, string dopusPath)
    {
        try
        {
            // dopusrt.exe lives alongside dopus.exe
            var dopusrt = Path.Combine(Path.GetDirectoryName(dopusPath)!, "dopusrt.exe");
            var exe = File.Exists(dopusrt) ? dopusrt : dopusPath;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"/open \"{path}\"",
                UseShellExecute = false
            });
        }
        catch
        {
            OpenInExplorer(path);
        }
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = false
                });
            }
        }
        catch { }
    }

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
