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
    private Size? _manualSize;
    private Size? _imagePixels;
    private Rectangle _folderBounds;
    private const int ResizeGrip = 6;

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
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        ForeColor = UiTheme.TextPrimary;
        FormStyler.ApplyRounded(this);
        FormStyler.DisablePopupTransitions(this);
        Controls.Add(_pane);
        _pane.UseStandaloneToolbar();
        _pane.TitleDragRequested += BeginTitleDrag;
        _pane.ImageDecoded += pixels =>
        {
            _imagePixels = pixels;
            if (Visible && _manualSize == null && _path != null)
                ApplyBounds(_path, _folderBounds);
        };

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
        try
        {
            var wasVisible = Visible;
            base.SetVisibleCore(false);
            if (wasVisible)
            {
                _path = null;
                _imagePixels = null;
                _pane.Clear();
            }
        }
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

    internal Task WarmUpAsync(string path, Rectangle ownerBounds)
    {
        if (!Visible)
            PopupWarmup.Run(() =>
            {
                ApplyBounds(path, ownerBounds);
                PopupWarmup.CreateHiddenHandles(this);
            });
        return _pane.WarmUpAsync();
    }

    public void ShowFile(string path, Rectangle folderBounds)
    {
        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) _imagePixels = null;
        _path = path;
        _folderBounds = folderBounds;
        // Keep a manually arranged preview in place while switching documents.
        if (!Visible || _manualSize == null) ApplyBounds(path, folderBounds);
        _ = _pane.PreviewAsync(path);
        if (!Visible) Show();
    }

    public void FollowOwner(Rectangle folderBounds)
    {
        _folderBounds = folderBounds;
        if (Visible && _path != null && !Pinned && _manualSize == null) ApplyBounds(_path, folderBounds);
    }

    internal bool WouldCoverClick(string path, Rectangle folderBounds, Point click)
    {
        if (Visible && _manualSize != null) return Bounds.Contains(click);
        var bounds = ComputeBounds(path, folderBounds);
        if (bounds.Contains(click)) return true;
        // A new image can grow after decoding. Preserve the double-click target
        // on small screens until its final dimensions are known.
        return _manualSize == null && OfficePreviewDocument.IsImageKind(Path.GetExtension(path).TrimStart('.').ToLowerInvariant())
            && ComputeBounds(path, folderBounds, new Size(1100, 960)).Contains(click);
    }

    private void ApplyBounds(string path, Rectangle folderBounds)
    {
        var screen = Screen.FromRectangle(folderBounds);
        ApplyFrameMetrics(UiScaleHelper.GetDpiForScreen(screen), screen.WorkingArea.Size);
        Bounds = ComputeBounds(path, folderBounds);
    }

    private void ApplyFrameMetrics(int dpi, Size available)
    {
        int S(int value) => UiScaleHelper.Scale(value, dpi);
        // Reserve real client space so the WebView's child HWND doesn't consume edge hits.
        Padding = new Padding(S(ResizeGrip));
        MinimumSize = new Size(Math.Min(S(420), available.Width), Math.Min(S(300), available.Height));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyFrameMetrics(e.DeviceDpiNew, Screen.FromRectangle(e.SuggestedRectangle).WorkingArea.Size);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _manualSize = new Size((int)Math.Round(Width * 96d / DeviceDpi), (int)Math.Round(Height * 96d / DeviceDpi));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiTheme.Card);
        using var border = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    private void BeginTitleDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00040000; // WS_THICKFRAME: enable the OS sizing loop.
            return cp;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        Dismiss();
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x83) // WM_NCCALCSIZE: keep the entire borderless window as client area.
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg != 0x84 || m.Result != (IntPtr)1) return; // WM_NCHITTEST, HTCLIENT
        var packed = m.LParam.ToInt64();
        var p = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
        m.Result = (IntPtr)ResizeHit(p, ClientSize, Padding.Left);
    }

    internal static int ResizeHit(Point p, Size size, int grip)
    {
        var left = p.X < grip;
        var right = p.X >= size.Width - grip;
        var top = p.Y < grip;
        var bottom = p.Y >= size.Height - grip;
        return top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
            : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

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

    private Rectangle ComputeBounds(string path, Rectangle folderBounds, Size? wantedSize = null)
    {
        var screen = Screen.FromRectangle(folderBounds);
        var dpi = UiScaleHelper.GetDpiForScreen(screen);
        int S(int value) => UiScaleHelper.Scale(value, dpi);
        var area = screen.WorkingArea;
        var margin = S(8);
        var available = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        var wanted = _manualSize ?? wantedSize ?? WantedSize(path, dpi);
        var min = _manualSize.HasValue ? MinimumSize : new Size(S(MinLogicalWidth), S(MinLogicalHeight));
        var size = new Size(
            Math.Clamp(S(wanted.Width), Math.Min(min.Width, available.Width), available.Width),
            Math.Clamp(S(wanted.Height), Math.Min(min.Height, available.Height), available.Height));
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
    private Size WantedSize(string path, int dpi)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" => new Size(980, 780),
            ".pdf" => new Size(820, 940),
            ".docx" or ".md" or ".markdown" => new Size(780, 940),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" =>
                _imagePixels is { } pixels && string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)
                    ? ImageSize(pixels, dpi) : new Size(820, 820),
            _ => new Size(760, 700)
        };
    }

    private static Size ImageSize(Size pixels, int dpi)
    {
        var logicalWidth = pixels.Width * 96f / dpi;
        var logicalHeight = pixels.Height * 96f / dpi;
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
}
