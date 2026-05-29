namespace Quickstart.Core;

using System.Text.Json;
using Quickstart.Models;

public sealed class ConfigManager : IDisposable
{
    private const int LegacyFileEntryType = 4;
    private const int SaveDebounceMs = 1500;

    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart");

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.json");
    private static readonly string BackupPath = Path.Combine(AppDataDir, "config.json.bak");

    private static readonly JsonSerializerOptions JsonOpts =
        AppConfigJsonContext.Default.Options;

    private AppConfig _config = new();
    private readonly object _lock = new();

    // 高频低价值写入（如 LastUsedAt）走防抖，合并多次更新为一次后台写盘，避免阻塞 UI 线程
    private readonly System.Threading.Timer _saveDebounceTimer;
    private bool _savePending;

    public ConfigManager()
    {
        _saveDebounceTimer = new System.Threading.Timer(_ => FlushPendingSave(), null, Timeout.Infinite, Timeout.Infinite);
    }

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
                if (NormalizeConfig())
                    Save();
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
                        if (NormalizeConfig())
                            Save();
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
            SaveInternal();
        }
    }

    // 调用方须持有 _lock
    private void SaveInternal()
    {
        // 立即写盘视为已落地，撤销任何挂起的防抖保存
        _savePending = false;

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

    // 标记有挂起改动并重置防抖计时器；到期后在后台线程写盘
    private void ScheduleSave()
    {
        _savePending = true;
        _saveDebounceTimer.Change(SaveDebounceMs, Timeout.Infinite);
    }

    // 立即落盘任何挂起的防抖改动（退出前调用，避免丢失）
    public void FlushPendingSave()
    {
        lock (_lock)
        {
            if (_savePending)
                SaveInternal();
        }
    }

    public bool AddEntry(QuickEntry entry)
    {
        lock (_lock)
        {
            // Avoid duplicate paths (case-insensitive)
            if (_config.Entries.Any(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                return false;

            entry.SortOrder = GetCategoryEntries(entry.Type).Select(e => e.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            _config.Entries.Add(entry);
            Save();
            return true;
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
            NormalizeSortOrders();
            Save();
        }
    }

    public void ReorderEntries(IEnumerable<string> orderedIds)
    {
        lock (_lock)
        {
            var orderedIdList = orderedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedIdList.Count == 0)
                return;

            var entriesById = _config.Entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < orderedIdList.Count; i++)
            {
                if (entriesById.TryGetValue(orderedIdList[i], out var entry))
                    entry.SortOrder = i;
            }

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
                ScheduleSave();
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
            ScheduleSave();
        }
    }

    public void Dispose()
    {
        _saveDebounceTimer.Dispose();
        FlushPendingSave();
    }

    private bool NormalizeConfig()
    {
        _config.Entries ??= [];
        _config.GroupLastUsedAt = _config.GroupLastUsedAt == null
            ? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DateTime>(_config.GroupLastUsedAt, StringComparer.OrdinalIgnoreCase);

        var migrated = false;
        foreach (var entry in _config.Entries)
        {
            if ((int)entry.Type == LegacyFileEntryType)
            {
                entry.Type = EntryType.File;
                migrated = true;
            }
        }

        if (NormalizeAiConfig())
            migrated = true;

        NormalizeSortOrders();
        return migrated;
    }

    private bool NormalizeAiConfig()
    {
        var changed = false;
        var defaults = AiConfig.CreateDefault();

        if (_config.Ai == null)
        {
            _config.Ai = defaults;
            return true;
        }

        _config.Ai.Providers ??= [];
        _config.Ai.PromptPresets ??= [];
        _config.Ai.Skills ??= [];

        changed |= EnsureDefaultProviders(_config.Ai, defaults);
        changed |= EnsureDefaultPrompts(_config.Ai, defaults);
        changed |= EnsureDefaultSkills(_config.Ai, defaults);

        foreach (var provider in _config.Ai.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id))
            {
                provider.Id = Guid.NewGuid().ToString("N")[..8];
                changed = true;
            }

            provider.Models ??= [];
            if (provider.Models.Count == 0 && !string.IsNullOrWhiteSpace(provider.DefaultModel))
            {
                provider.Models.Add(provider.DefaultModel.Trim());
                changed = true;
            }

            if (provider.Models.Count > 0)
            {
                var normalizedModels = provider.Models
                    .Select(model => model.Trim())
                    .Where(model => !string.IsNullOrWhiteSpace(model))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (normalizedModels.Count != provider.Models.Count
                    || normalizedModels.Where((model, index) => provider.Models[index] != model).Any())
                {
                    provider.Models = normalizedModels;
                    changed = true;
                }
            }

            if (string.IsNullOrWhiteSpace(provider.DefaultModel) && provider.Models.Count > 0)
            {
                provider.DefaultModel = provider.Models[0];
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(provider.DefaultModel)
                && provider.Models.All(model => !string.Equals(model, provider.DefaultModel, StringComparison.OrdinalIgnoreCase)))
            {
                provider.Models.Insert(0, provider.DefaultModel.Trim());
                changed = true;
            }

            if (provider.TimeoutSeconds <= 0)
            {
                provider.TimeoutSeconds = 60;
                changed = true;
            }

            if (provider.DeepSeekThinkingEffort is not ("" or "disabled" or "high" or "max"))
            {
                provider.DeepSeekThinkingEffort = string.Empty;
                changed = true;
            }
        }

        foreach (var prompt in _config.Ai.PromptPresets)
        {
            if (string.IsNullOrWhiteSpace(prompt.Id))
            {
                prompt.Id = Guid.NewGuid().ToString("N")[..8];
                changed = true;
            }
        }

        foreach (var skill in _config.Ai.Skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Id))
            {
                skill.Id = Guid.NewGuid().ToString("N")[..8];
                changed = true;
            }

            skill.Steps ??= [];
        }

        if (_config.Ai.MaxFileBytes <= 0)
        {
            _config.Ai.MaxFileBytes = defaults.MaxFileBytes;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_config.Ai.WebChatUrl))
        {
            _config.Ai.WebChatUrl = defaults.WebChatUrl;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_config.Ai.CurrentProviderId)
            || _config.Ai.Providers.All(provider => provider.Id != _config.Ai.CurrentProviderId))
        {
            _config.Ai.CurrentProviderId = _config.Ai.Providers.FirstOrDefault()?.Id ?? defaults.CurrentProviderId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_config.Ai.DefaultPromptId)
            || _config.Ai.PromptPresets.All(prompt => prompt.Id != _config.Ai.DefaultPromptId))
        {
            _config.Ai.DefaultPromptId = _config.Ai.PromptPresets.FirstOrDefault()?.Id ?? defaults.DefaultPromptId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_config.Ai.DefaultSkillId)
            || _config.Ai.Skills.All(skill => skill.Id != _config.Ai.DefaultSkillId))
        {
            _config.Ai.DefaultSkillId = _config.Ai.Skills.FirstOrDefault()?.Id ?? string.Empty;
            changed = true;
        }

        return changed;
    }

    private static bool EnsureDefaultProviders(AiConfig config, AiConfig defaults)
    {
        var changed = false;
        foreach (var provider in defaults.Providers)
        {
            if (config.Providers.Any(existing => string.Equals(existing.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            config.Providers.Add(provider);
            changed = true;
        }

        return changed;
    }

    private static bool EnsureDefaultPrompts(AiConfig config, AiConfig defaults)
    {
        var changed = false;
        foreach (var prompt in defaults.PromptPresets)
        {
            if (config.PromptPresets.Any(existing => string.Equals(existing.Id, prompt.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            config.PromptPresets.Add(prompt);
            changed = true;
        }

        return changed;
    }

    private static bool EnsureDefaultSkills(AiConfig config, AiConfig defaults)
    {
        var changed = false;
        foreach (var skill in defaults.Skills)
        {
            if (config.Skills.Any(existing => string.Equals(existing.Id, skill.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            config.Skills.Add(skill);
            changed = true;
        }

        return changed;
    }

    private IEnumerable<QuickEntry> GetCategoryEntries(EntryType type)
        => type switch
        {
            EntryType.Folder => _config.Entries.Where(e => e.Type == EntryType.Folder),
            EntryType.File => _config.Entries.Where(e => e.Type == EntryType.File),
            EntryType.Url => _config.Entries.Where(e => e.Type == EntryType.Url),
            EntryType.Text => _config.Entries.Where(e => e.Type == EntryType.Text),
            _ => []
        };

    private void NormalizeSortOrders()
    {
        NormalizeSortOrdersFor(_config.Entries.Where(e => e.Type == EntryType.Folder));
        NormalizeSortOrdersFor(_config.Entries.Where(e => e.Type == EntryType.File));
        NormalizeSortOrdersFor(_config.Entries.Where(e => e.Type == EntryType.Url));
        NormalizeSortOrdersFor(_config.Entries.Where(e => e.Type == EntryType.Text));
    }

    private static void NormalizeSortOrdersFor(IEnumerable<QuickEntry> entries)
    {
        var index = 0;
        foreach (var entry in entries.OrderBy(e => e.SortOrder).ThenBy(e => e.AddedAt))
            entry.SortOrder = index++;
    }
}
