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

            // 官方命令通道是 dopusrt.exe /cmd <内部命令>。
            // 若对 dopus.exe 使用 ArgumentList 把 "Go" 单独作为参数，
            // 主程序会把 "Go" 当成相对路径打开，落到安装目录下的
            // "...\Directory Opus\Go"，所有文件夹都会指到那里。
            var runner = ResolveDopusCommandRunner(dopusPath);
            var escapedPath = path.Replace("\"", "\"\"", StringComparison.Ordinal);
            // PATH= 可正确处理空格；NEWTAB=deflister,tofront 复用默认窗口并新建标签
            var arguments = $"/cmd Go PATH=\"{escapedPath}\" NEWTAB=deflister,tofront";

            WindowActivator.ClaimForegroundRights();
            WindowActivator.AllowAnyForeground();

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = runner,
                Arguments = arguments,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Directory Opus 未返回启动进程。");

            WindowActivator.BringToFrontAsync(process, windowClass: null, procName: "dopus");
        }
        catch
        {
            OpenInExplorer(path);
        }
    }

    /// <summary>
    /// 解析用于发送 /cmd 的可执行文件：优先同目录 dopusrt.exe，其次用户配置路径。
    /// </summary>
    private static string ResolveDopusCommandRunner(string dopusPath)
    {
        if (string.IsNullOrWhiteSpace(dopusPath))
            return dopusPath;

        if (string.Equals(Path.GetFileName(dopusPath), "dopusrt.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(dopusPath))
        {
            return dopusPath;
        }

        var dir = Path.GetDirectoryName(dopusPath);
        if (!string.IsNullOrEmpty(dir))
        {
            var dopusrt = Path.Combine(dir, "dopusrt.exe");
            if (File.Exists(dopusrt))
                return dopusrt;
        }

        return dopusPath;
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
