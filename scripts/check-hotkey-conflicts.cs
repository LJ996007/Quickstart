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
        using var secondOwner = (IDisposable)Activator.CreateInstance(type)!;
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
        Check(Register(secondOwner, occupied), "Reserve requested key alongside its alternative");
        var exhausted = Fallback(occupied, free);
        Check(!exhausted.ok && exhausted.error.Contains(occupied) && !exhausted.error.Contains(free),
            "When all candidates conflict, name the user's requested shortcut");
        Register(secondOwner, "");
        var disabled = Fallback("", free);
        Check(disabled.ok && disabled.effective == "", "Disabled binding stays disabled");
        var invalid = Fallback("not-a-key", "Ctrl+Alt+Shift+F24");
        Check(!invalid.ok && invalid.effective == "not-a-key", "Invalid syntax does not trigger fallback");
        var preferred = Fallback("Ctrl+Alt+Shift+F24", free);
        Check(preferred.ok && preferred.effective == "Ctrl+Alt+Shift+F24", "Available preferred key retained");
        Register(subject, "");
        Register(owner, "");
        const string legacyPreset = "Ctrl+Shift+Space";
        var presets = new[] { "Ctrl+Alt+Space", legacyPreset, "Ctrl+Alt+F" };
        var availability = presets.ToDictionary(gesture => gesture, gesture => Register(subject, gesture));
        Register(subject, "");
        foreach (var (gesture, available) in availability)
            Console.WriteLine($"Availability: {gesture} = {available}");
        if (presets.Any(gesture => gesture != legacyPreset && availability[gesture]))
        {
            if (availability[legacyPreset])
                Check(Register(owner, legacyPreset), "Reserve the legacy search preset");
            object?[] args = [legacyPreset, null, null];
            var ok = (bool)type.GetMethod("TryRegisterFileSearch")!.Invoke(subject, args)!;
            Check(ok && args[1] is string fallback && fallback != legacyPreset && availability[fallback],
                "A previously stored fallback must recover when it later conflicts");
            Console.WriteLine($"PASS: legacy search preset recovered with {args[1]}");
        }
        else
        {
            Console.WriteLine("All alternative search presets are already in use.");
        }
        Console.WriteLine("PASS: native conflict, fallback ownership, custom binding, disable, invalid syntax, reconfiguration.");
    }
}
