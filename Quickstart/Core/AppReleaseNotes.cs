using System.Text;

namespace Quickstart.Core;

internal static class AppReleaseNotes
{
    private static readonly ReleaseNote[] Releases =
    [
        
        new(
            "1.0.15",
            "2026-07-22",
            [
                "右滑用 Directory Opus 打开收藏文件夹时，若 DOpus 已在运行，改为通过 `dopusrt.exe /acmd Go \"路径\" NEWTAB=deflister,findexisting,tofront` 在现有文件窗口新建标签打开，不再额外启动一个新的 DOpus 窗口；路径含 `#`、空格、中文均可正确处理（含 `%` 的目录名仍回退 `dopus.exe` 按字面打开）。"
            ]),
        new(
            "1.0.14",
            "2026-07-21",
            [
                "修复右滑主面板用 Directory Opus 打开收藏文件夹时失败的问题：路径中的 `#`（常见于 WPS 云盘目录名）会被 `dopusrt /cmd Go PATH=` 命令解析截断，改为直接把路径交给 `dopus.exe` 打开。",
                "右滑列表左键单击文件夹/文件/网页/最近项也可直接打开（与文本、历史一致）；拖拽排序不受影响。"
            ]),
        new(
            "1.0.13",
            "2026-07-19",
            [
                "修复设置页依次点击多个导航标签后旧标签仍保持高亮的问题。",
                "切换页面时显式重绘旧选中项和新选中项，确保始终只有当前标签高亮。"
            ]),
        new(
            "1.0.12",
            "2026-07-19",
            [
                "修复设置页左侧导航高亮随鼠标悬停移动并闪动的问题。",
                "导航仅高亮当前激活页面，鼠标经过其他标签时不再触发高亮和局部重绘。"
            ]),
        new(
            "1.0.11",
            "2026-07-19",
            [
                "修复 v1.0.10 打开设置页时「创建窗口句柄时出错」的崩溃。",
                "移除导航 ListBox 上不安全的 WS_EX_COMPOSITED 扩展样式（原生 LISTBOX 在嵌套布局下会 CreateWindowEx 失败）。",
                "保留悬停局部重绘、双缓冲与字体缓存，继续抑制导航闪抖。"
            ]),
        new(
            "1.0.10",
            "2026-07-19",
            [
                "修复设置页左侧导航在鼠标移入不同标签时整体闪抖的问题。",
                "导航 ListBox 启用双缓冲并抑制背景擦除；悬停仅重绘相关项，避免整表 Invalidate。",
                "缓存导航字体，统一选中/未选中文字左内边距，减少绘制抖动。"
            ]),
        new(
            "1.0.9",
            "2026-07-17",
            [
                "修复使用 Directory Opus 打开收藏文件夹时始终跳到安装目录下 Go 路径的问题。",
                "改为通过同目录 dopusrt.exe /cmd 发送 Go PATH=… NEWTAB 命令，正确在现有窗口新建标签并打开目标文件夹。"
            ]),
        new(
            "1.0.8",
            "2026-07-17",
            [
                "全局鼠标钩子迁到专用高优先级线程，UI 卡顿不再拖慢整机鼠标，降低钩子被系统静默摘除的风险。",
                "首次弹窗图标改为通用占位 + 后台补真图，冷盘/网络路径条目不再阻塞列表显示。",
                "Shell 注册表检查、TC/DOpus/Everything 探测、剪贴板历史落盘加载后移到空闲后台，托盘更快就绪。",
                "本版本探测未找到的外部工具会记住结果，避免每次启动重复全盘扫描；设置页「检测」仍强制探测。",
                "空闲预热时提前 RefreshList；主列表开启双缓冲；搜索防抖 200ms→120ms；配置写盘移出 UI 线程。",
                "按需预热 AiPopup；截断文本/拼音缓存加上限；菜单加粗字体静态复用。",
                "支持环境变量 QUICKSTART_PERF=1 输出启动打点日志（%LOCALAPPDATA%\\Quickstart\\startup-trace.log）。"
            ]),
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
