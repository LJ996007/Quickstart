namespace Quickstart.Mac;

using System.Text;
using Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 拼音搜索需要 GB2312（与 Windows 端一致）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
