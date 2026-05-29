namespace Quickstart.Mac;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Quickstart.Core;
using Quickstart.Mac.Platform;
using Quickstart.Mac.Views;

public partial class App : Application
{
    private readonly ConfigManager _config = new();
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private MacGlobalGesture? _gesture;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 常驻菜单栏/托盘工具：关闭主窗口不退出，由菜单“退出”显式结束
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config.Load();
            _mainWindow = new MainWindow(_config);
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();

            SetupTray(desktop);

            // macOS：全局右键拖拽手势（右滑→主弹窗，左滑→捕获选区并打开 AI）
            if (OperatingSystem.IsMacOS())
                SetupGesture();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupGesture()
    {
        _gesture = new MacGlobalGesture();
        _gesture.Triggered += direction => Dispatcher.UIThread.Post(async () =>
        {
            if (direction == RightDragDirection.Right)
            {
                _mainWindow?.ShowAndActivate();
                return;
            }

            // 左滑：捕获选中文字并打开 AI 面板预填
            await MacSelectionCapture.CopySelectionAsync();
#pragma warning disable CS0618 // GetTextAsync 在当前 Avalonia 版本仍可用
            string? text = _mainWindow?.Clipboard is { } clipboard
                ? await clipboard.GetTextAsync()
                : null;
#pragma warning restore CS0618

            var ai = new AiWindow(_config);
            ai.Show();
            if (!string.IsNullOrWhiteSpace(text))
                ai.SetInput(text!);
        });
        _gesture.Start();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("显示 Quickstart");
        showItem.Click += (_, _) => _mainWindow?.ShowAndActivate();
        menu.Add(showItem);

        var aiItem = new NativeMenuItem("AI 助手");
        aiItem.Click += (_, _) => new AiWindow(_config).Show();
        menu.Add(aiItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("退出");
        quitItem.Click += (_, _) =>
        {
            _mainWindow?.AllowClose();
            desktop.Shutdown();
        };
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Quickstart - 快捷启动",
            IsVisible = true,
            Menu = menu
        };
        _trayIcon.Clicked += (_, _) => _mainWindow?.ShowAndActivate();

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://Quickstart/Assets/app.ico")));
        }
        catch
        {
            return null;
        }
    }
}
