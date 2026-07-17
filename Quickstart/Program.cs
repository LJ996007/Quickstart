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
        var openSettingsOnStart = false;
        if (args.Length >= 2 && args[0] == "--add")
        {
            externalRequest = args[1];
        }
        else if (args.Length >= 1 && args[0] is "--settings" or "/settings")
        {
            openSettingsOnStart = true;
            externalRequest = "__SETTINGS__";
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

        // Auto-detect Everything on first run. A stale path is detected again when a search runs.
        if (string.IsNullOrEmpty(configManager.Config.EverythingPath))
        {
            var detectedEverything = EverythingDetector.Detect();
            if (detectedEverything != null)
            {
                configManager.Config.EverythingPath = detectedEverything;
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
        IntPtr leftGestureSourceWindow = IntPtr.Zero;
        CancellationTokenSource? everythingSearchCts = null;
        var settingsDialogOpen = false;
        var trayIcon = new TrayIcon();
        using var uiDispatcher = new Control();
        _ = uiDispatcher.Handle;
        var mouseHook = new GlobalMouseHook();
        using var globalHotKey = new GlobalHotKey();
        using var clipboardHistory = new ClipboardHistoryService(configManager);
        clipboardHistory.Start(uiDispatcher);

        MainPopup EnsureMainPopup()
        {
            if (mainPopup == null || mainPopup.IsDisposed)
            {
                mainPopup = new MainPopup(configManager, launcher, clipboardHistory);
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
                aiActionPicker.ActionSelected += ExecuteLeftAction;
                aiActionPicker.ShowSettings += ShowSettings;
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

        // 绝不能在 ContextMenuStrip 的 Click 栈上同步 ShowDialog，否则会与菜单的
        // 模态消息循环嵌套冲突：设置窗体出不来，UI 线程卡住；本进程的 WH_MOUSE_LL
        // 钩子无法及时返回，表现为整机鼠标严重卡顿。必须等菜单关闭后再弹出。
        trayIcon.ShowSettings += () => PostToUi(ShowSettings);

        trayIcon.ExitRequested += () =>
        {
            trayIcon.Dispose();
            aiClient.Dispose();
            Application.Exit();
        };

        // Listen for IPC messages from other instances. The pipe continuation may run on a
        // worker thread, so create and touch WinForms controls only after dispatching to UI.
        singleInstance.ArgumentReceived += message =>
        {
            if (uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated)
                return;

            try
            {
                uiDispatcher.BeginInvoke(() =>
                {
                    if (message == "__SETTINGS__")
                    {
                        ShowSettings();
                        return;
                    }

                    var popup = EnsureMainPopup();
                    if (message == "__SHOW__")
                        popup.ShowPopup();
                    else
                        popup.HandleExternalRequest(message);
                });
            }
            catch (InvalidOperationException)
            {
                // 应用正在退出。
            }
        };
        singleInstance.StartListening();

        // Handle external request on first launch（等消息循环就绪后再弹，避免偶发不显示）
        if (openSettingsOnStart)
        {
            void OpenSettingsOnce(object? sender, EventArgs e)
            {
                Application.Idle -= OpenSettingsOnce;
                trayIcon.ShowBalloon("Quickstart", "程序已启动，正在打开设置…", ToolTipIcon.Info);
                ShowSettings();
            }

            Application.Idle += OpenSettingsOnce;
        }
        else if (externalRequest != null)
        {
            EnsureMainPopup().HandleExternalRequest(externalRequest);
        }

        // Global right-drag gesture: hold right button, drag right to show popup
        var gestureMoveTimer = new System.Windows.Forms.Timer { Interval = 16 };
        var latestGesturePoint = Point.Empty;
        var latestGestureDirection = RightDragDirection.Right;
        var gestureMovePending = false;
        var initialUiPrewarmed = false;
        var aiPopupPrewarmTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        void PostToUi(Action action)
        {
            if (uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated)
                return;

            try
            {
                uiDispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // 应用退出、句柄销毁期间丢弃尚未处理的消息。
            }
        }

        void PostGestureAction(Action action) => PostToUi(action);

        void ProcessPendingGestureMove()
        {
            if (!gestureMovePending)
                return;

            gestureMovePending = false;
            var direction = latestGestureDirection;
            var point = latestGesturePoint;

            if (direction == RightDragDirection.Right && mainPopup is { Visible: true })
                mainPopup.HighlightAtScreenPoint(point);
            else if (direction == RightDragDirection.Left && aiActionPicker is { Visible: true })
                aiActionPicker.HighlightAtScreenPoint(point);
        }

        gestureMoveTimer.Tick += (_, _) => ProcessPendingGestureMove();

        globalHotKey.Pressed += () =>
        {
            var popup = EnsureMainPopup();
            if (popup.Visible)
                popup.Hide();
            else
                popup.ShowPopup();
        };

        void ApplyRuntimeInputSettings(bool showRegistrationError)
        {
            var config = configManager.Config;
            mouseHook.UpdateSettings(
                config.RightDragEnabled,
                config.RightDragTriggerDistance,
                config.RightDragVerticalTolerance);

            if (!globalHotKey.TryRegister(config.HotKey, out var error)
                && showRegistrationError
                && !string.IsNullOrWhiteSpace(config.HotKey))
            {
                trayIcon.ShowBalloon("Quickstart", error, ToolTipIcon.Warning);
            }
        }

        ApplyRuntimeInputSettings(showRegistrationError: true);

        mouseHook.GestureTriggered += (direction, pt, sourceWindow) =>
        {
            // 低级鼠标钩子必须尽快返回。所有窗口创建、布局和显示都投递到 UI 消息队列。
            PostGestureAction(() =>
            {
                if (!gestureMoveTimer.Enabled)
                    gestureMoveTimer.Start();

                if (direction == RightDragDirection.Left)
                {
                    if (mainPopup is { Visible: true }) mainPopup.Hide();
                    if (aiPopup is { Visible: true }) aiPopup.Hide();

                    leftGestureSourceWindow = sourceWindow;
                    if (configManager.Config.LeftDragAction == LeftDragAction.EverythingSearch)
                    {
                        if (aiActionPicker is { Visible: true }) aiActionPicker.Hide();
                    }
                    else
                    {
                        var picker = EnsureAiActionPicker();
                        picker.ShowAtGesturePoint(pt);
                        if (!picker.HasActions)
                            EnsureAiPopup().ShowAtGesturePoint(pt, sourceWindow);
                    }
                }
                else
                {
                    if (aiPopup is { Visible: true }) aiPopup.Hide();
                    if (aiActionPicker is { Visible: true }) aiActionPicker.Hide();
                    EnsureMainPopup().ShowAtGesturePoint(pt);
                }
            });
        };
        mouseHook.GestureMove += (direction, pt) =>
        {
            // 只保留最新坐标，由 16 ms UI 定时器统一处理，避免高回报率鼠标淹没消息泵。
            latestGestureDirection = direction;
            latestGesturePoint = pt;
            gestureMovePending = true;
        };
        mouseHook.GestureReleased += (direction, pt) =>
        {
            PostGestureAction(() =>
            {
                latestGestureDirection = direction;
                latestGesturePoint = pt;
                gestureMovePending = true;
                ProcessPendingGestureMove();

                if (direction == RightDragDirection.Right && mainPopup is { Visible: true })
                    mainPopup.TryReleaseAtScreenPoint(pt);
                else if (direction == RightDragDirection.Left
                    && configManager.Config.LeftDragAction == LeftDragAction.EverythingSearch)
                {
                    _ = SearchSelectedTextInEverythingAsync(leftGestureSourceWindow);
                }
                else if (direction == RightDragDirection.Left && aiActionPicker is { Visible: true })
                {
                    var selection = aiActionPicker.TryReleaseAtScreenPoint(pt);
                    if (selection != null)
                        ExecuteLeftAction(selection, pt);
                    else
                        // 未选中动作：面板停靠在屏幕上，进入可拖动/ESC 关闭的交互模式。
                        aiActionPicker.EnterInteractiveMode();
                }

                gestureMovePending = false;
                gestureMoveTimer.Stop();
            });
        };
        mouseHook.GestureCancelled += () =>
        {
            PostGestureAction(() =>
            {
                gestureMovePending = false;
                gestureMoveTimer.Stop();
                if (mainPopup is { Visible: true }) mainPopup.Hide();
                if (aiActionPicker is { Visible: true }) aiActionPicker.Hide();
            });
        };
        Application.ApplicationExit += (_, _) =>
        {
            gestureMoveTimer.Stop();
            gestureMoveTimer.Dispose();
            aiPopupPrewarmTimer.Stop();
            aiPopupPrewarmTimer.Dispose();
            mouseHook.Dispose();
            everythingSearchCts?.Cancel();
            everythingSearchCts?.Dispose();
            aiClient.Dispose();
            configManager.Dispose(); // 落盘任何挂起的防抖写入
        };

        aiPopupPrewarmTimer.Tick += (_, _) =>
        {
            aiPopupPrewarmTimer.Stop();
            _ = EnsureAiPopup();
        };
        Application.Idle += PrewarmInitialUi;

        void ExecuteLeftAction(AiActionSelection selection, Point point)
        {
            if (selection.IsPlainTextPaste)
            {
                _ = PasteClipboardAsPlainTextAsync(leftGestureSourceWindow);
            }
            else if (selection.IsScreenshotOcr)
            {
                _ = RunScreenshotOcrAsync(point);
            }
            else if (selection.IsEverythingSearch)
            {
                _ = SearchSelectedTextInEverythingAsync(leftGestureSourceWindow);
            }
            else if (selection.IsWebSearch)
            {
                _ = SearchSelectedTextOnWebAsync(selection, leftGestureSourceWindow);
            }
            else
            {
                var webPrompt = selection.IsPrompt
                    ? configManager.Config.Ai.PromptPresets.FirstOrDefault(p => p.Id == selection.Id)
                    : null;
                if (webPrompt is { Target: AiPromptTarget.Web })
                    _ = SendPromptToWebAsync(webPrompt, leftGestureSourceWindow);
                else
                    EnsureAiPopup().ShowForSelectedAction(point, leftGestureSourceWindow, selection, autoRun: true);
            }
        }

        async Task RunScreenshotOcrAsync(Point gesturePoint)
        {
            try
            {
                if (!ScreenshotOcrService.HasCredentials())
                {
                    trayIcon.ShowBalloon(
                        "Quickstart",
                        "请先在设置中填写百度 OCR 的 API Key / Secret Key。",
                        ToolTipIcon.Warning);
                    ShowSettings();
                    return;
                }

                var screen = Screen.FromPoint(gesturePoint);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var ocrConfig = configManager.Config.Ocr ?? new OcrConfig();

                // 框选是同步模态；完成后后台识别
                var text = await ScreenshotOcrService.CaptureAndRecognizeAsync(
                    ocrConfig,
                    screen,
                    status: null,
                    cts.Token);

                if (text == null)
                    return; // 取消框选

                if (string.IsNullOrWhiteSpace(text))
                {
                    trayIcon.ShowBalloon("Quickstart", "未识别到文字。", ToolTipIcon.Warning);
                    return;
                }

                await ScreenshotOcrService.SetClipboardPlainTextAsync(text, cts.Token);
                var preview = text.Length > 40 ? text[..40] + "…" : text;
                var lines = text.Split('\n').Length;
                trayIcon.ShowBalloon(
                    "Quickstart",
                    $"已复制 {text.Length} 字（{lines} 行），可直接粘贴。\n{preview}",
                    ToolTipIcon.Info);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloon("Quickstart", ex.Message, ToolTipIcon.Error);
            }
        }

        void PrewarmInitialUi(object? sender, EventArgs e)
        {
            if (initialUiPrewarmed)
                return;

            initialUiPrewarmed = true;
            Application.Idle -= PrewarmInitialUi;
            // 主弹窗和动作选择器体积较小，先创建句柄以消除第一次手势的控件初始化抖动。
            _ = EnsureMainPopup();
            _ = EnsureAiActionPicker();
            // AI 编辑窗口较重，延迟预热，避免与托盘和鼠标钩子的启动就绪争抢 UI 线程。
            aiPopupPrewarmTimer.Start();
        }

        // Run without a main form — tray icon keeps the app alive
        Application.Run();

        async Task PasteClipboardAsPlainTextAsync(IntPtr sourceWindow)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var pasted = await PlainTextPasteService.PasteAsync(sourceWindow, cts.Token);
                if (!pasted)
                {
                    trayIcon.ShowBalloon(
                        "Quickstart",
                        "剪贴板中没有可粘贴的文字。",
                        ToolTipIcon.Warning);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloon("Quickstart", $"纯文本粘贴失败：{ex.Message}", ToolTipIcon.Error);
            }
        }

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

        async Task SearchSelectedTextInEverythingAsync(IntPtr sourceWindow)
        {
            everythingSearchCts?.Cancel();
            everythingSearchCts?.Dispose();
            // 捕获选区 + 启动/前置 Everything 需要更长时间（微信切前台 + 单实例 IPC）
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            everythingSearchCts = cts;

            // 捕获期间抑制剪贴板历史：避免 Clear/Ctrl+C/Restore 触发监听与捕获抢同一把剪贴板锁
            clipboardHistory.BeginCaptureSuppress();
            try
            {
                var captured = await aiInputCapture.CaptureFromSourceAsync(
                    sourceWindow,
                    cts.Token,
                    restoreClipboard: true);

                if (captured.Kind == AiCapturedInputKind.Files)
                {
                    trayIcon.ShowBalloon("Quickstart", "请选择文本后再使用 Everything 搜索。", ToolTipIcon.Warning);
                    return;
                }

                if (captured.Kind != AiCapturedInputKind.Text || string.IsNullOrWhiteSpace(captured.Text))
                {
                    trayIcon.ShowBalloon("Quickstart", "未捕获到选中文字。", ToolTipIcon.Warning);
                    return;
                }

                var everythingPath = configManager.Config.EverythingPath;
                if (string.IsNullOrWhiteSpace(everythingPath) || !File.Exists(everythingPath))
                {
                    everythingPath = EverythingDetector.Detect();
                    if (everythingPath == null)
                    {
                        trayIcon.ShowBalloon(
                            "Quickstart",
                            "未找到 Everything.exe，请在设置中指定路径。",
                            ToolTipIcon.Warning);
                        return;
                    }

                    configManager.Config.EverythingPath = everythingPath;
                    configManager.Save();
                }

                // 文本捕获会把前台切回微信；启动 Everything 前先恢复抢前台权限，并异步等待窗口出现。
                // 必须在 UI 线程上完成 ClaimForegroundRights / Process.Start，否则单实例窗口常不前置。
                await EverythingLauncher.SearchAsync(everythingPath, captured.Text, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloon("Quickstart", $"Everything 搜索失败：{ex.Message}", ToolTipIcon.Error);
            }
            finally
            {
                clipboardHistory.EndCaptureSuppress();
                if (ReferenceEquals(everythingSearchCts, cts))
                    everythingSearchCts = null;
                cts.Dispose();
            }
        }

        async Task SearchSelectedTextOnWebAsync(AiActionSelection selection, IntPtr sourceWindow)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var captured = await aiInputCapture.CaptureFromSourceAsync(
                    sourceWindow,
                    cts.Token,
                    restoreClipboard: true);

                if (captured.Kind != AiCapturedInputKind.Text || string.IsNullOrWhiteSpace(captured.Text))
                {
                    trayIcon.ShowBalloon("Quickstart", "未捕获到选中文字。", ToolTipIcon.Warning);
                    return;
                }

                var query = Uri.EscapeDataString(EverythingLauncher.NormalizeQuery(captured.Text));
                var url = selection.UrlTemplate.Replace(
                    "{query}",
                    query,
                    StringComparison.OrdinalIgnoreCase);

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https"))
                {
                    trayIcon.ShowBalloon("Quickstart", $"“{selection.Name}”的网址模板无效。", ToolTipIcon.Warning);
                    return;
                }

                ProcessLauncher.OpenUrl(url);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloon("Quickstart", $"网页查询失败：{ex.Message}", ToolTipIcon.Error);
            }
        }

        void ShowSettings()
        {
            // 防止托盘/快捷方式/IPC 重复触发时叠多个设置窗
            if (settingsDialogOpen)
            {
                foreach (Form form in Application.OpenForms)
                {
                    if (form is SettingsForm { IsDisposed: false })
                    {
                        form.BringToFront();
                        form.Activate();
                        break;
                    }
                }
                return;
            }

            settingsDialogOpen = true;
            try
            {
                using var settingsForm = new SettingsForm(configManager, clipboardHistory);
                if (DialogPresenter.ShowModal(settingsForm) == DialogResult.OK)
                {
                    ApplyRuntimeInputSettings(showRegistrationError: true);
                    clipboardHistory.ApplyConfigLimits();
                    if (mainPopup is { IsDisposed: false })
                        mainPopup.RefreshList();
                }
            }
            finally
            {
                settingsDialogOpen = false;
            }
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
