namespace Quickstart.Core;

using System.Diagnostics;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class ProcessLauncher(ConfigManager configManager)
{
    public void Open(QuickEntry entry, OpenWith? overrideWith = null)
    {
        var config = configManager.Config;
        var openWith = overrideWith ?? config.DefaultOpenWith;

        configManager.TouchEntry(entry.Id);

        if (entry.Type is EntryType.File or EntryType.Document)
        {
            OpenFile(entry.Path);
            return;
        }

        if (entry.Type == EntryType.Url)
        {
            OpenUrl(entry.Path);
            return;
        }

        if (entry.Type == EntryType.Text)
        {
            // Text entries are handled by the UI (clipboard copy), nothing to do here
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
            WindowActivator.AllowAnyForeground();
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = tcPath,
                Arguments = $"/O /T /L=\"{path}\"",
                UseShellExecute = false
            });
            // TC 直接启动，用 seed process 追踪主窗口
            WindowActivator.BringToFrontAsync(proc, windowClass: null, procName: null);
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
            var dopusrt = Path.Combine(Path.GetDirectoryName(dopusPath)!, "dopusrt.exe");
            WindowActivator.AllowAnyForeground();

            if (File.Exists(dopusrt))
            {
                // Use the last-active Opus lister when one exists, otherwise open the
                // default lister and create a tab there.
                Process.Start(new ProcessStartInfo
                {
                    FileName = dopusrt,
                    Arguments = $"/acmd Go \"{path}\" NEWTAB=deflister,findexisting,tofront",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dopusPath,
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });
            }

            WindowActivator.BringToFrontAsync(seedProcess: null, windowClass: null, procName: "dopus");
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
                WindowActivator.AllowAnyForeground();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });
                WindowActivator.BringToFrontAsync(seedProcess: null, windowClass: "CabinetWClass");
            }
            else if (File.Exists(path))
            {
                WindowActivator.AllowAnyForeground();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = false
                });
                WindowActivator.BringToFrontAsync(seedProcess: null, windowClass: "CabinetWClass");
            }
        }
        catch { }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
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
