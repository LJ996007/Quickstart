// Run: dotnet run --file scripts/check-hotkey-conflicts.cs
// Registers temporary hotkeys without sending input or changing user settings.
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Reflection;
using Quickstart.UI;

static class HotKeyChecks
{
    [STAThread]
    static void Main()
    {
        var type = typeof(FileSearchPopup).Assembly.GetType("Quickstart.Core.GlobalHotKey")!;
        using var owner = (IDisposable)Activator.CreateInstance(type)!;
        using var subject = (IDisposable)Activator.CreateInstance(type)!;
        bool Register(object target, string gesture)
        {
            object?[] args = [gesture, null];
            return (bool)type.GetMethod("TryRegister")!.Invoke(target, args)!;
        }
        (bool ok, string effective, string error) Fallback(string gesture, params string[] candidates)
        {
            object?[] args = [gesture, candidates, null, null];
            var ok = (bool)type.GetMethod("TryRegisterWithFallback")!.Invoke(subject, args)!;
            return (ok, (string)args[2]!, (string)args[3]!);
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        const string occupied = "Ctrl+Alt+Shift+F22";
        const string free = "Ctrl+Alt+Shift+F23";
        Check(Register(owner, occupied), "Reserve test hotkey");
        var recovered = Fallback(occupied, occupied, free);
        Check(recovered.ok && recovered.effective == free, "Skip occupied candidates and recover");
        Check(!Register(owner, free), "Fallback really owns the registered hotkey");
        Check(Register(owner, occupied), "Restore conflict");
        var custom = Fallback(occupied);
        Check(!custom.ok && custom.effective == occupied && custom.error.Contains("1409")
            && custom.error.Contains(occupied), "Custom binding preserved and error includes gesture");
        Check(Register(owner, free), "Previous binding released on reconfiguration");
        var disabled = Fallback("", free);
        Check(disabled.ok && disabled.effective == "", "Disabled binding stays disabled");
        var invalid = Fallback("not-a-key", "Ctrl+Alt+Shift+F24");
        Check(!invalid.ok && invalid.effective == "not-a-key", "Invalid syntax does not trigger fallback");
        var preferred = Fallback("Ctrl+Alt+Shift+F24", free);
        Check(preferred.ok && preferred.effective == "Ctrl+Alt+Shift+F24", "Available preferred key retained");
        Register(subject, "");
        Register(owner, "");
        foreach (var gesture in new[] { "Ctrl+Alt+Space", "Ctrl+Shift+Space", "Ctrl+Alt+F" })
            Console.WriteLine($"Availability: {gesture} = {Register(subject, gesture)}");
        Console.WriteLine("PASS: native conflict, fallback ownership, custom binding, disable, invalid syntax, reconfiguration.");
    }
}
