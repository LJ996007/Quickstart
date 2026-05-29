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

        // 注册 Windows 平台的密钥保护器（DPAPI）
        AiSecretStore.Protector = new DpapiSecretProtector();

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
        var aiClient = new AiClient();
        var aiInputCapture = new AiInputCaptureService();
        var aiFileReader = new AiFileContentReader();

        // UI
        MainPopup? mainPopup = null;
        AiPopup? aiPopup = null;
        AiActionPickerPopup? aiActionPicker = null;
        IntPtr aiGestureSourceWindow = IntPtr.Zero;
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

        AiPopup EnsureAiPopup()
        {
            if (aiPopup == null || aiPopup.IsDisposed)
            {
                aiPopup = new AiPopup(configManager, aiInputCapture, aiFileReader, aiClient);
                aiPopup.ShowAiSettings += ShowAiSettings;
                _ = aiPopup.Handle;
            }

            return aiPopup;
        }

        AiActionPickerPopup EnsureAiActionPicker()
        {
            if (aiActionPicker == null || aiActionPicker.IsDisposed)
            {
                aiActionPicker = new AiActionPickerPopup(configManager);
                _ = aiActionPicker.Handle;
            }

            return aiActionPicker;
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
        trayIcon.ShowAiSettings += () => ShowAiSettings(null);

        trayIcon.ExitRequested += () =>
        {
            trayIcon.Dispose();
            aiClient.Dispose();
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
        mouseHook.GestureTriggered += (direction, pt, sourceWindow) =>
        {
            if (direction == RightDragDirection.Left)
            {
                if (mainPopup is { Visible: true }) mainPopup.Hide();
                if (aiPopup is { Visible: true }) aiPopup.Hide();

                aiGestureSourceWindow = sourceWindow;
                var picker = EnsureAiActionPicker();
                picker.ShowAtGesturePoint(pt);
                if (!picker.HasActions)
                    EnsureAiPopup().ShowAtGesturePoint(pt, sourceWindow);
            }
            else
            {
                if (aiPopup is { Visible: true }) aiPopup.Hide();
                if (aiActionPicker is { Visible: true }) aiActionPicker.Hide();
                EnsureMainPopup().ShowAtGesturePoint(pt);
            }
        };
        mouseHook.GestureMove += (direction, pt) =>
        {
            if (direction == RightDragDirection.Right && mainPopup is { Visible: true })
                mainPopup.HighlightAtScreenPoint(pt);
            else if (direction == RightDragDirection.Left && aiActionPicker is { Visible: true })
                aiActionPicker.HighlightAtScreenPoint(pt);
        };
        mouseHook.GestureReleased += (direction, pt) =>
        {
            if (direction == RightDragDirection.Right && mainPopup is { Visible: true })
                mainPopup.TryReleaseAtScreenPoint(pt);
            else if (direction == RightDragDirection.Left && aiActionPicker is { Visible: true })
            {
                var selection = aiActionPicker.TryReleaseAtScreenPoint(pt);
                if (selection != null)
                {
                    var webPrompt = selection.IsPrompt
                        ? configManager.Config.Ai.PromptPresets.FirstOrDefault(p => p.Id == selection.Id)
                        : null;
                    if (webPrompt is { Target: AiPromptTarget.Web })
                        _ = SendPromptToWebAsync(webPrompt, aiGestureSourceWindow);
                    else
                        EnsureAiPopup().ShowForSelectedAction(pt, aiGestureSourceWindow, selection, autoRun: true);
                }
            }
        };
        mouseHook.GestureCancelled += () =>
        {
            if (mainPopup is { Visible: true }) mainPopup.Hide();
            if (aiActionPicker is { Visible: true }) aiActionPicker.Hide();
        };
        Application.ApplicationExit += (_, _) =>
        {
            mouseHook.Dispose();
            aiClient.Dispose();
            configManager.Dispose(); // 落盘任何挂起的防抖写入
        };

        var aiPopupPrewarmed = false;
        Application.Idle += PrewarmAiPopup;

        void PrewarmAiPopup(object? sender, EventArgs e)
        {
            if (aiPopupPrewarmed)
                return;

            aiPopupPrewarmed = true;
            Application.Idle -= PrewarmAiPopup;
            _ = EnsureAiPopup();
            _ = EnsureAiActionPicker();
        }

        // Run without a main form — tray icon keeps the app alive
        Application.Run();

        async Task SendPromptToWebAsync(AiPromptPreset prompt, IntPtr sourceWindow)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                // 不恢复原始剪贴板：随后要写入渲染后的文本，少一次写操作可避免 OLE 剪贴板冲突
                var captured = await aiInputCapture.CaptureFromSourceAsync(sourceWindow, cts.Token, restoreClipboard: false);

                var input = captured.Kind == AiCapturedInputKind.Files
                    ? aiFileReader.ReadFiles(captured.FilePaths, configManager.Config.Ai.MaxFileBytes).Text
                    : captured.Text;

                if (string.IsNullOrWhiteSpace(input))
                {
                    trayIcon.ShowBalloon("Quickstart", "未捕获到选中文字，已取消发送。", ToolTipIcon.Warning);
                    return;
                }

                var rendered = PromptRenderer.Render(prompt.Template, input);
                var url = string.IsNullOrWhiteSpace(configManager.Config.Ai.WebChatUrl)
                    ? "https://chat.deepseek.com/"
                    : configManager.Config.Ai.WebChatUrl;
                await WebPromptSender.SendAsync(rendered, url, autoPaste: true, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloon("Quickstart", $"发送到网页失败：{ex.Message}", ToolTipIcon.Error);
            }
        }

        void ShowSettings()
        {
            using var settingsForm = new SettingsForm(configManager);
            DialogPresenter.ShowModal(settingsForm);
        }

        void ShowAiSettings(Form? owner)
        {
            using var settingsForm = new AiSettingsForm(configManager);
            try
            {
                DialogPresenter.ShowModal(settingsForm, owner);
            }
            finally
            {
                if (aiPopup is { IsDisposed: false })
                {
                    aiPopup.RefreshSelectors();
                    if (owner is AiPopup)
                        aiPopup.Activate();
                }
            }
        }
    }
}
