namespace Quickstart;

using System.Text;
using Quickstart.Core;
using Quickstart.UI;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Register GB2312 encoding for pinyin helper
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Parse command line
        string? externalRequest = null;
        if (args.Length >= 2 && args[0] == "--add")
        {
            externalRequest = args[1];
        }
        else if (args.Length >= 1 && QuickstartProtocol.IsProtocolUri(args[0]))
        {
            externalRequest = args[0];
        }

        // Single instance check
        using var singleInstance = new SingleInstance();
        if (!singleInstance.TryAcquire())
        {
            // Already running — send request via pipe
            if (externalRequest != null)
                SingleInstance.SendToRunningInstance(externalRequest);
            else
                SingleInstance.SendToRunningInstance("__SHOW__");
            return;
        }

        ApplicationConfiguration.Initialize();

        // Core services
        var configManager = new ConfigManager();
        configManager.Load();

        var exePath = Application.ExecutablePath;
        if (!ShellIntegration.IsProtocolRegistered(exePath))
            ShellIntegration.RegisterProtocol(exePath);

        if (configManager.Config.ShellMenuEnabled)
        {
            if (!ShellIntegration.IsRegistered(exePath))
                ShellIntegration.Register(exePath);
        }

        // Auto-detect TC on first run
        if (string.IsNullOrEmpty(configManager.Config.TotalCommanderPath))
        {
            var detectedTc = TcDetector.Detect();
            if (detectedTc != null)
            {
                configManager.Config.TotalCommanderPath = detectedTc;
                configManager.Save();
            }
        }

        // Auto-detect Directory Opus on first run
        if (string.IsNullOrEmpty(configManager.Config.DirectoryOpusPath))
        {
            var detectedDopus = DopusDetector.Detect();
            if (detectedDopus != null)
            {
                configManager.Config.DirectoryOpusPath = detectedDopus;
                configManager.Save();
            }
        }

        var launcher = new ProcessLauncher(configManager);

        // UI
        MainPopup? mainPopup = null;
        var trayIcon = new TrayIcon();

        MainPopup EnsureMainPopup()
        {
            if (mainPopup == null || mainPopup.IsDisposed)
            {
                mainPopup = new MainPopup(configManager, launcher);
                mainPopup.ShowSettings += ShowSettings;
                _ = mainPopup.Handle;
            }

            return mainPopup;
        }

        trayIcon.ShowMainWindow += () =>
        {
            var popup = EnsureMainPopup();
            if (popup.Visible)
                popup.Hide();
            else
                popup.ShowPopup();
        };

        trayIcon.ShowSettings += ShowSettings;

        trayIcon.ExitRequested += () =>
        {
            trayIcon.Dispose();
            Application.Exit();
        };

        // Listen for IPC messages from other instances
        singleInstance.StartListening();
        singleInstance.ArgumentReceived += message =>
        {
            var popup = EnsureMainPopup();
            popup.Invoke(() =>
            {
                if (message == "__SHOW__")
                {
                    popup.ShowPopup();
                }
                else
                {
                    popup.HandleExternalRequest(message);
                }
            });
        };

        // Handle external request on first launch
        if (externalRequest != null)
        {
            EnsureMainPopup().HandleExternalRequest(externalRequest);
        }

        // Global right-drag gesture: hold right button, drag right to show popup
        var mouseHook = new GlobalMouseHook();
        mouseHook.GestureTriggered += pt =>
        {
            var popup = EnsureMainPopup();
            popup.ShowAtGesturePoint(pt);
        };
        mouseHook.GestureMove += pt =>
        {
            if (mainPopup is { Visible: true })
                mainPopup.HighlightAtScreenPoint(pt);
        };
        mouseHook.GestureReleased += pt =>
        {
            if (mainPopup is { Visible: true })
                mainPopup.TryReleaseAtScreenPoint(pt);
        };
        mouseHook.GestureCancelled += () =>
        {
            if (mainPopup is { Visible: true }) mainPopup.Hide();
        };
        Application.ApplicationExit += (_, _) => mouseHook.Dispose();

        // Run without a main form — tray icon keeps the app alive
        Application.Run();

        void ShowSettings()
        {
            using var settingsForm = new SettingsForm(configManager);
            settingsForm.ShowDialog();
        }
    }
}
