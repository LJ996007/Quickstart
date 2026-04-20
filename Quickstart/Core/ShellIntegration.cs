namespace Quickstart.Core;

using Microsoft.Win32;

public static class ShellIntegration
{
    private const string FileKeyPath = @"Software\Classes\*\shell\AddToQuickstart";
    private const string DirKeyPath = @"Software\Classes\Directory\shell\AddToQuickstart";
    private const string DirBgKeyPath = @"Software\Classes\Directory\Background\shell\AddToQuickstart";
    private const string ProtocolKeyPath = @"Software\Classes\quickstart";
    private const string MenuText = "添加到 Quickstart";

    public static void Register(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        RegisterKey(FileKeyPath, BuildCommand(exePath, "%1"), exePath);
        RegisterKey(DirKeyPath, BuildCommand(exePath, "%1"), exePath);
        RegisterKey(DirBgKeyPath, BuildCommand(exePath, "%V"), exePath);
    }

    public static void Unregister()
    {
        DeleteKey(FileKeyPath);
        DeleteKey(DirKeyPath);
        DeleteKey(DirBgKeyPath);
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DirKeyPath);
        return key != null;
    }

    public static bool IsRegistered(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        return IsKeyRegistered(FileKeyPath, BuildCommand(exePath, "%1"))
            && IsKeyRegistered(DirKeyPath, BuildCommand(exePath, "%1"))
            && IsKeyRegistered(DirBgKeyPath, BuildCommand(exePath, "%V"));
    }

    public static void RegisterProtocol(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ProtocolKeyPath);
            key.SetValue(null, "URL:Quickstart Protocol");
            key.SetValue("URL Protocol", string.Empty);

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, exePath);

            using var cmdKey = key.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue(null, BuildProtocolCommand(exePath));
        }
        catch { }
    }

    public static bool IsProtocolRegistered(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(ProtocolKeyPath);
        var hasProtocolFlag = key?.GetValue("URL Protocol") is string;
        return hasProtocolFlag
            && IsKeyRegistered($@"{ProtocolKeyPath}\shell\open", BuildProtocolCommand(exePath));
    }

    private static string BuildCommand(string exePath, string argumentPlaceholder)
        => $"\"{exePath}\" --add \"{argumentPlaceholder}\"";

    private static string BuildProtocolCommand(string exePath)
        => $"\"{exePath}\" \"%1\"";

    private static bool IsKeyRegistered(string keyPath, string expectedCommand)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{keyPath}\command");
        var actualCommand = key?.GetValue(null) as string;
        return string.Equals(actualCommand, expectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterKey(string keyPath, string command, string iconPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(null, MenuText);
            key.SetValue("Icon", iconPath);

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue(null, command);
        }
        catch { }
    }

    private static void DeleteKey(string keyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }
}
