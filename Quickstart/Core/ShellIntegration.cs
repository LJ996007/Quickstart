namespace Quickstart.Core;

using Microsoft.Win32;

public static class ShellIntegration
{
    private const string FileKeyPath = @"Software\Classes\*\shell\AddToQuickstart";
    private const string DirKeyPath = @"Software\Classes\Directory\shell\AddToQuickstart";
    private const string DirBgKeyPath = @"Software\Classes\Directory\Background\shell\AddToQuickstart";
    private const string MenuText = "添加到 Quickstart";

    public static void Register(string exePath)
    {
        var command = $"\"{exePath}\" --add \"%V\"";

        RegisterKey(FileKeyPath, command, exePath);
        RegisterKey(DirKeyPath, command, exePath);
        RegisterKey(DirBgKeyPath, command, exePath);
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
