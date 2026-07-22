namespace Quickstart.Core;

using System.Diagnostics;
using System.Text.RegularExpressions;
using Quickstart.Models;
using Quickstart.Utils;

public sealed class ProcessLauncher(ConfigManager configManager)
{
    /// <summary>匹配「%xxx%」形式、可能被 Go 命令当环境变量展开的路径片段。</summary>
    private static readonly Regex ExpandableEnvPattern = new("%[^%]+%", RegexOptions.Compiled);

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

            var exe = ResolveDopusExecutable(dopusPath);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                OpenInExplorer(path);
                return;
            }

            WindowActivator.ClaimForegroundRights();
            WindowActivator.AllowAnyForeground();

            // DOpus 已在运行时，通过同目录 dopusrt.exe 发送 Go NEWTAB 命令，
            // 在现有文件窗口的活动标签栏新建标签打开目标文件夹，而不是再次启动 dopus.exe
            // （直接给 dopus.exe 传路径会新开一个独立窗口）。
            // 说明：
            // 1) 路径必须用双引号包裹，空格、中文、#（WPS 云盘目录常见）均可正确处理——
            //    早期版本失败的根因是命令行拼接不当导致引号丢失，而非 # 本身；
            // 2) 路径含可展开的 %xxx% 片段时回退到 dopus.exe：Go 命令会把 %VAR% 当环境变量
            //    展开，导致形如「%TEMP%」的目录名被错误替换，dopus.exe 则按字面路径打开；
            //    仅含单个 % 的名称（如「100% done」）Go 不会展开，仍走新标签。
            if (!ExpandableEnvPattern.IsMatch(path) && IsDopusRunning() && TryOpenInExistingDopusTab(exe, path))
            {
                return;
            }

            // 回退：DOpus 未运行（冷启动）或路径含 %，直接把路径交给 dopus.exe 打开。
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
    /// 检测当前会话是否有正在运行的 dopus.exe 主进程。
    /// </summary>
    private static bool IsDopusRunning()
    {
        int session;
        using (var self = Process.GetCurrentProcess())
        {
            session = self.SessionId;
        }

        foreach (var p in Process.GetProcessesByName("dopus"))
        {
            try
            {
                if (p.SessionId == session)
                    return true;
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// 通过 dopusrt.exe /acmd 发送 Go NEWTAB 命令，在现有 DOpus 窗口新建标签打开文件夹。
    /// NEWTAB 参数：deflister（无窗口时打开默认窗口）、findexisting（文件夹已打开则激活该标签，避免重复）、
    /// tofront（把文件窗口置前）。
    /// </summary>
    private static bool TryOpenInExistingDopusTab(string dopusExePath, string path)
    {
        var dir = Path.GetDirectoryName(dopusExePath);
        if (string.IsNullOrEmpty(dir))
            return false;

        var rt = Path.Combine(dir, "dopusrt.exe");
        if (!File.Exists(rt))
            return false;

        // Windows 目录名不可能包含双引号，直接用双引号包裹路径即可安全拼接。
        var psi = new ProcessStartInfo
        {
            FileName = rt,
            UseShellExecute = false,
            Arguments = $"/acmd Go \"{path}\" NEWTAB=deflister,findexisting,tofront"
        };

        var process = Process.Start(psi);
        if (process == null)
            return false;

        // dopusrt 是发送完命令即退出的瞬时进程，真正的文件窗口在 dopus 进程，
        // 因此按进程名兜底查找并置前。
        WindowActivator.BringToFrontAsync(process, windowClass: null, procName: "dopus");
        return true;
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
