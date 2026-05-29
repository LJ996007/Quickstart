namespace Quickstart.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? ShowMainWindow;
    public event Action? ShowSettings;
    public event Action? ShowAiSettings;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Quickstart - 快捷启动",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMainWindow?.Invoke();
        };
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("设置(&S)");
        settingsItem.Click += (_, _) => ShowSettings?.Invoke();
        menu.Items.Add(settingsItem);

        var aiSettingsItem = new ToolStripMenuItem("AI 设置(&A)");
        aiSettingsItem.Click += (_, _) => ShowAiSettings?.Invoke();
        menu.Items.Add(aiSettingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon LoadAppIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Quickstart.Resources.app.ico");
        if (stream == null)
            return SystemIcons.Application;

        // Load a large frame for best quality
        using var source = new Icon(stream, new Size(64, 64));
        using var bmp = source.ToBitmap();

        // Find non-transparent content bounds（LockBits 一次性扫描，避免 GetPixel 的逐像素开销）
        int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
        var bounds = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    var row = scan0 + (long)y * data.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        // 32bppArgb 内存布局为 BGRA，alpha 在每像素第 4 字节
                        if (row[x * 4 + 3] > 10)
                        {
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        if (maxX <= minX || maxY <= minY)
            return new Icon(asm.GetManifestResourceStream("Quickstart.Resources.app.ico")!);

        // Crop to content and scale to fill 32x32
        var contentRect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var targetSize = new Size(32, 32);
        using var scaled = new Bitmap(targetSize.Width, targetSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, new Rectangle(Point.Empty, targetSize), contentRect, GraphicsUnit.Pixel);
        }

        return Icon.FromHandle(scaled.GetHicon());
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2000, title, text, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
