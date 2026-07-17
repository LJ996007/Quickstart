using System.Text;

namespace Quickstart.Core;

internal static class AppReleaseNotes
{
    private static readonly ReleaseNote[] Releases =
    [
        new(
            "1.0.7",
            "2026-07-17",
            [
                "修复托盘右键「设置」无法弹出并导致界面严重卡顿的问题。",
                "设置窗体改为在托盘菜单关闭后再打开，避免与菜单模态消息循环冲突。",
                "修复设置页内容区行列布局写反，导致右侧页面内容无法正常显示的问题。",
                "无主窗体时模态对话框使用隐藏 owner，保证从托盘打开时能正确前置激活。"
            ]),
        new(
            "1.0.6",
            "2026-07-17",
            [
                "粘贴为纯文本时，自动删除文本末尾连续的换行符和回车符。",
                "保留正文内部换行以及末尾其他空白字符，避免粘贴后意外换行。"
            ]),
        new(
            "1.0.5",
            "2026-07-17",
            [
                "Windows 最近使用记录的显示上限由 100 项调整为 20 项。",
                "减少首次进入“最近”标签时的快捷方式和图标加载量，提升右滑窗口响应速度。"
            ]),
        new(
            "1.0.4",
            "2026-07-17",
            [
                "使用 Directory Opus 打开文件夹时，改为复用已打开的 Opus 窗口并新增标签页。",
                "通过 Opus 内部 Go 命令打开目录，避免每次操作都创建新的 Opus 窗口。"
            ]),
        new(
            "1.0.3",
            "2026-07-17",
            [
                "修复单文件发布后“最近”标签始终显示无记录的问题。",
                "改为直接显示并打开 Windows Recent 快捷方式，移除与单文件裁剪不兼容的 COM 解析。",
                "补充说明：Windows Recent 主要记录最近文档，资源管理器访问的文件夹不一定会被系统收录。"
            ]),
        new(
            "1.0.2",
            "2026-07-17",
            [
                "右滑主窗口新增“最近”标签，读取 Windows 最近使用的文件和文件夹。",
                "支持右键拖动到最近项目后松开直接打开，也支持双击或按 Enter 打开。",
                "最近项目支持名称、路径和拼音首字母搜索，并自动忽略已失效的记录。"
            ]),
        new(
            "1.0.1",
            "2026-07-17",
            [
                "设置中新增独立的“程序信息”页面，可查看当前程序版本。",
                "新增程序内版本说明，方便了解每个版本的修改内容。",
                "建立代码调整时同步递增版本号并补充版本说明的项目规则。"
            ])
    ];

    public static string GetDisplayText()
    {
        var text = new StringBuilder();

        foreach (var release in Releases)
        {
            if (text.Length > 0)
                text.AppendLine();

            text.Append('v').Append(release.Version).Append("  ·  ").AppendLine(release.Date);
            foreach (var item in release.Items)
                text.Append("• ").AppendLine(item);
        }

        return text.ToString().TrimEnd();
    }

    private sealed record ReleaseNote(string Version, string Date, string[] Items);
}
