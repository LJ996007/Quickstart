namespace Quickstart.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? ShowMainWindow;
    public event Action? ShowSettings;
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

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon LoadAppIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream("Quickstart.Resources.app.ico");
        if (stream != null)
            return new Icon(stream);

        // Fallback: generate a simple icon
        return SystemIcons.Application;
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
