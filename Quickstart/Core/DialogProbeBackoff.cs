namespace Quickstart.Core;

/// <summary>Per-window cooldown survives focus changes and unrelated window events.</summary>
public sealed class DialogProbeBackoff
{
    private readonly Dictionary<(IntPtr, uint), (int Misses, long RetryAt)> _entries = new();
    public bool CanProbe(IntPtr window, uint pid, long now)
        => !_entries.TryGetValue((window, pid), out var value) || now >= value.RetryAt;

    public void Failed(IntPtr window, uint pid, long now, bool providerFailure = false)
    {
        var key = (window, pid);
        _entries.TryGetValue(key, out var previous);
        var misses = Math.Min(previous.Misses + 1, 5);
        var delay = providerFailure ? 60_000 : misses switch { 1 => 500, 2 => 1000, 3 => 2000, 4 => 10_000, _ => 60_000 };
        _entries[key] = (misses, now + delay);
    }
    public void Succeeded(IntPtr window, uint pid) => _entries.Remove((window, pid));
    public void RemoveClosed(Func<IntPtr, uint, bool> alive)
    {
        foreach (var key in _entries.Keys.ToArray())
            if (!alive(key.Item1, key.Item2)) _entries.Remove(key);
    }
}
