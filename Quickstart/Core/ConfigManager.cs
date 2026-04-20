namespace Quickstart.Core;

using System.Text.Json;
using Quickstart.Models;

public sealed class ConfigManager
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart");

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.json");
    private static readonly string BackupPath = Path.Combine(AppDataDir, "config.json.bak");

    private static readonly JsonSerializerOptions JsonOpts =
        AppConfigJsonContext.Default.Options;

    private AppConfig _config = new();
    private readonly object _lock = new();

    public AppConfig Config
    {
        get { lock (_lock) return _config; }
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(AppDataDir);
                _config = new AppConfig();
                Save();
                return;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig) ?? new AppConfig();
                NormalizeConfig();
            }
            catch
            {
                // Try backup
                if (File.Exists(BackupPath))
                {
                    try
                    {
                        var json = File.ReadAllText(BackupPath);
                        _config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig) ?? new AppConfig();
                        NormalizeConfig();
                    }
                    catch
                    {
                        _config = new AppConfig();
                    }
                }
                else
                {
                    _config = new AppConfig();
                }
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(AppDataDir);

            var json = JsonSerializer.Serialize(_config, AppConfigJsonContext.Default.AppConfig);
            var tempPath = ConfigPath + ".tmp";

            // Atomic write: write to temp file, then rename
            File.WriteAllText(tempPath, json);

            // Backup current config
            if (File.Exists(ConfigPath))
            {
                File.Copy(ConfigPath, BackupPath, overwrite: true);
            }

            File.Move(tempPath, ConfigPath, overwrite: true);
        }
    }

    public void AddEntry(QuickEntry entry)
    {
        lock (_lock)
        {
            // Avoid duplicate paths (case-insensitive)
            if (_config.Entries.Any(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            entry.SortOrder = _config.Entries.Count;
            _config.Entries.Add(entry);
            Save();
        }
    }

    public void UpdateEntry(QuickEntry entry)
    {
        lock (_lock)
        {
            var idx = _config.Entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0)
            {
                _config.Entries[idx] = entry;
                Save();
            }
        }
    }

    public void RemoveEntry(string id)
    {
        lock (_lock)
        {
            _config.Entries.RemoveAll(e => e.Id == id);
            Save();
        }
    }

    public void TouchEntry(string id)
    {
        lock (_lock)
        {
            var entry = _config.Entries.Find(e => e.Id == id);
            if (entry != null)
            {
                entry.LastUsedAt = DateTime.Now;
                Save();
            }
        }
    }

    public void TouchGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return;

        lock (_lock)
        {
            _config.GroupLastUsedAt[group.Trim()] = DateTime.Now;
            Save();
        }
    }

    private void NormalizeConfig()
    {
        _config.Entries ??= [];
        _config.GroupLastUsedAt = _config.GroupLastUsedAt == null
            ? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DateTime>(_config.GroupLastUsedAt, StringComparer.OrdinalIgnoreCase);
    }
}
