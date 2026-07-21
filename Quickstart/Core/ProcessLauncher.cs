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

        if (entry.Type == EntryType.File)
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
            if (!Directory.Exists(path))
            {
                OpenInExplorer(path);
                return;
            }

            // 不要用 dopusrt.exe /cmd Go PATH="..."：
            // 1) 命令行参数拆分容易把 Go / PATH= / NEWTAB= 拆成多条伪命令；
            // 2) 更关键的是路径里的「#」会被 DOpus 命令解析器当成命令码截断
            //    （WPS 云盘等目录常以 # 开头），结果开出标题为 "Go PATH=\" 的无效位置。
            // 直接把文件夹路径作为 dopus.exe 参数，走「打开文件夹」语义，可正确处理
            // #、空格、中文；单实例下会复用已运行的 DOpus。
            var exe = ResolveDopusExecutable(dopusPath);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                OpenInExplorer(path);
                return;
            }

            WindowActivator.ClaimForegroundRights();
            WindowActivator.AllowAnyForeground();

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(path);

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Directory Opus 未返回启动进程。");

            WindowActivator.BringToFrontAsync(process, windowClass: null, procName: "dopus");
        }
        catch
        {
            OpenInExplorer(path);
        }
    }

    /// <summary>
    /// 解析 dopus.exe：配置可能是 dopus.exe 或 dopusrt.exe，打开文件夹需要主程序。
    /// </summary>
    private static string ResolveDopusExecutable(string dopusPath)
    {
        if (string.IsNullOrWhiteSpace(dopusPath))
            return dopusPath;

        if (string.Equals(Path.GetFileName(dopusPath), "dopus.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(dopusPath))
        {
            return dopusPath;
        }

        var dir = Path.GetDirectoryName(dopusPath);
        if (!string.IsNullOrEmpty(dir))
        {
            var dopus = Path.Combine(dir, "dopus.exe");
            if (File.Exists(dopus))
                return dopus;
        }

        return File.Exists(dopusPath) ? dopusPath : string.Empty;
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
