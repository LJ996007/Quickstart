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
        // OwnerDraw 会铺满每个 item，但不会绘制最后一项下方的空闲区域。
        // 只补绘这块背景，既避免列表底部残留系统默认白色，又不重新擦除
        // item 区域，从而保留切换导航项时的低闪烁效果。
        if (m.Msg == WmEraseBkgnd)
        {
            var emptyTop = 0;
            if (Items.Count > 0)
            {
                var lastItemBounds = GetItemRectangle(Items.Count - 1);
                emptyTop = Math.Clamp(lastItemBounds.Bottom, 0, ClientSize.Height);
            }

            if (emptyTop < ClientSize.Height && m.WParam != IntPtr.Zero)
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                using var brush = new SolidBrush(BackColor);
                graphics.FillRectangle(
                    brush,
                    new Rectangle(0, emptyTop, ClientSize.Width, ClientSize.Height - emptyTop));
            }

            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }
}
