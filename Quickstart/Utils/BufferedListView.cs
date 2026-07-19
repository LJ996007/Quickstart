namespace Quickstart.Utils;

/// <summary>
/// 开启双缓冲的 ListView，消除 OwnerDraw 悬停/滚动时的闪烁。
/// </summary>
public sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
        // 额外打开组合缓冲相关样式，进一步减少重绘闪烁
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}
