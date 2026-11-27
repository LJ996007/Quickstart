namespace Quickstart.UI;

using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Utils;

/// <summary>
/// 独立文件预览窗：在目录浏览里单击文件时贴着目录窗弹出，内容复用应用内置的
/// <see cref="OfficePreviewPane"/>。窗口不抢焦点，按文件类型 / 图片比例自动定尺寸，
/// 保证文字和表格看得清。
/// </summary>
internal sealed class FilePreviewPopup : Form
{
    private const int MinLogicalWidth = 760;
    private const int MinLogicalHeight = 700;
    private const int GapLogical = 8;

    private readonly OfficePreviewPane _pane = new() { Dock = DockStyle.Fill };
    private string? _path;
    private Rectangle _folderBounds;

    /// <summary>目录浏览窗当前位置，用于靠边摆放和「点回列表不算关闭」判断。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Rectangle>? OwnerBounds { get; set; }

    /// <summary>用户按 Esc 或点到两个窗口之外，由目录浏览决定一起关闭。</summary>
    public event Action? Dismissed;
    public event Action? OpenRequested;

    public FilePreviewPopup()
    {
        Text = "文件预览";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Border;
        Padding = new Padding(1);
        ForeColor = UiTheme.TextPrimary;
        FormStyler.ApplyRounded(this);
        Controls.Add(_pane);
        _pane.SetPinEnabled(true);

        _pane.OpenRequested += (_, _) => OpenRequested?.Invoke();
        _pane.EscapeRequested += (_, _) => Dismiss();
        _pane.CloseRequested += (_, _) => Dismiss();
        Deactivate += OnPreviewDeactivate;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            Dismiss();
        };
    }

    private bool _dismissing;

    /// <summary>程序主动隐藏（换文件、目录窗关闭等）不算用户关窗，不再触发 Dismissed。</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (value)
        {
            base.SetVisibleCore(true);
            return;
        }
        var wasDismissing = _dismissing;
        _dismissing = true;
        try { base.SetVisibleCore(false); }
        finally { _dismissing = wasDismissing; }
    }

    /// <summary>用户关闭（Esc / 点到外面）：隐藏本窗并通知目录浏览一起关闭。</summary>
    private void Dismiss()
    {
        if (!Visible) return;
        Hide();
        Dismissed?.Invoke();
    }

    /// <summary>前台窗口是不是本预览窗（或它的子窗口、菜单）：WebView2 拿焦点时不让目录窗误关。</summary>
    public bool IsForegroundOrOwnsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && GetAncestor(foreground, GaRoot) == Handle;
    }

    private const uint GaRoot = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    /// <summary>显示（或换文件）时不抢目录窗的激活，避免打开预览就关掉目录列表。</summary>
    protected override bool ShowWithoutActivation => true;

    public bool IsShowing(string path) =>
        Visible && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);

    public void ShowFile(string path, Rectangle folderBounds)
    {
        _folderBounds = folderBounds;
        _path = path;
        var bounds = ComputeBounds(path, folderBounds);
        _pane.Clear();
        Bounds = bounds;
        if (!Visible) Show();
        _ = _pane.PreviewAsync(path);
    }

    /// <summary>目录浏览窗移动 / 缩放后重新贴靠，不重新加载文件。</summary>
    public void FollowOwner(Rectangle folderBounds)
    {
        _folderBounds = folderBounds;
        if (!Visible || _path == null) return;
        Bounds = ComputeBounds(_path, folderBounds);
    }

    /// <summary>是否已经钉在最前面：钉住后不再随失焦或目录窗关闭而隐藏。</summary>
    public bool Pinned => _pane.Pinned;

    /// <summary>目录窗关闭时调用：没钉住就一起关掉，钉住了就留在最前面。</summary>
    public void HideUnlessPinned()
    {
        if (!Pinned) Hide();
    }

    private void OnPreviewDeactivate(object? sender, EventArgs e)
    {
        if (Pinned || _dismissing) return;
        // 光标还在自己身上或回到目录列表（正在换文件）时不关闭。
        if (Bounds.Contains(Cursor.Position)) return;
        if (OwnerBounds?.Invoke().Contains(Cursor.Position) == true) return;
        Dismiss();
    }

    private Rectangle ComputeBounds(string path, Rectangle folderBounds)
    {
        var screen = Screen.FromRectangle(folderBounds);
        var dpi = UiScaleHelper.GetDpiForScreen(screen);
        int S(int value) => UiScaleHelper.Scale(value, dpi);
        var area = screen.WorkingArea;
        var margin = S(8);
        var available = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        var wanted = WantedSize(path, dpi, S);
        var size = new Size(
            Math.Clamp(S(wanted.Width), Math.Min(S(MinLogicalWidth), available.Width), available.Width),
            Math.Clamp(S(wanted.Height), Math.Min(S(MinLogicalHeight), available.Height), available.Height));
        var gap = S(GapLogical);
        var x = folderBounds.Right + gap;
        if (x + size.Width > area.Right - margin) x = folderBounds.Left - gap - size.Width;
        var y = folderBounds.Top;
        return new Rectangle(
            new Point(
                Math.Clamp(x, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - size.Width)),
                Math.Clamp(y, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - margin - size.Height))),
            size);
    }

    /// <summary>预览窗默认大小（逻辑像素）：表格偏宽、文档和 PDF 偏高、图片按自身比例。</summary>
    private static Size WantedSize(string path, int dpi, Func<int, int> S)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" => new Size(980, 780),
            ".pdf" => new Size(820, 940),
            ".docx" or ".md" or ".markdown" => new Size(780, 940),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" => ImageSize(path, dpi),
            _ => new Size(760, 700)
        };
    }

    private static Size ImageSize(string path, int dpi)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            var logicalWidth = image.Width * 96f / dpi;
            var logicalHeight = image.Height * 96f / dpi;
            const int maxWidth = 1100, maxHeight = 960, minWidth = 520, minHeight = 480;
            var width = Math.Clamp(logicalWidth, minWidth, maxWidth);
            var height = width * logicalHeight / Math.Max(1f, logicalWidth);
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * logicalWidth / Math.Max(1f, logicalHeight);
            }
            if (height < minHeight)
            {
                height = minHeight;
                width = height * logicalWidth / Math.Max(1f, logicalHeight);
            }
            return new Size((int)Math.Round(Math.Min(width, maxWidth)), (int)Math.Round(Math.Min(height, maxHeight)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return new Size(820, 820);
        }
    }
}
