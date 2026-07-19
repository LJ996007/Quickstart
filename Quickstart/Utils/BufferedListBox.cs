namespace Quickstart.Utils;

/// <summary>
/// 减轻 OwnerDraw ListBox 重绘闪烁的 ListBox。
/// <para>
/// 注意：不要给 ListBox 加 <c>WS_EX_COMPOSITED</c>。原生 LISTBOX 在部分 DPI / 嵌套
/// TableLayout 场景下会因此 CreateWindowEx 失败，弹出「创建窗口句柄时出错」。
/// 防抖主要依赖调用方减少无效重绘 + 抑制背景擦除。
/// </para>
/// </summary>
public sealed class BufferedListBox : ListBox
{
    private const int WmEraseBkgnd = 0x0014;

    public BufferedListBox()
    {
        // 仅开受支持的双缓冲；ListBox 非完全 UserPaint，勿叠加 AllPaintingInWmPaint。
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void WndProc(ref Message m)
    {
        // OwnerDraw 已自行铺满 item 背景，跳过默认擦除可减少局部重绘时的闪白
        if (m.Msg == WmEraseBkgnd)
        {
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }
}
