namespace Quickstart.Mac;

using System;
using System.Text;
using Avalonia;
using Quickstart.Core;
using Quickstart.Mac.Platform;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 拼音搜索需要 GB2312（与 Windows 端一致）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // macOS：AI 密钥用 Keychain 加密存储（非 mac 平台退回 plain-local）
        if (OperatingSystem.IsMacOS())
            AiSecretStore.Protector = new MacSecretProtector();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
