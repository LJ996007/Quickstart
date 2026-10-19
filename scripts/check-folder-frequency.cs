// Run: dotnet run --file scripts/check-folder-frequency.cs
#:project ../Quickstart.Core/Quickstart.Core.csproj
using Quickstart.Core;
using Quickstart.Models;

static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
AppPaths.SetRootForTests(Path.GetFullPath("artifacts/folder-frequency-check"));
using var config = new ConfigManager();
var first = new QuickEntry { Name = "原先第一", Path = @"C:\first", SortOrder = 0 };
var frequent = new QuickEntry { Name = "常用", Path = @"C:\frequent", SortOrder = 10 };
var newer = new QuickEntry { Name = "更新", Path = @"C:\newer", SortOrder = 20 };
config.Config.Entries.AddRange([first, frequent, newer]);
config.TouchEntry(frequent.Id); config.TouchEntry(frequent.Id); config.TouchEntry(newer.Id);
Check(frequent.UseCount == 2 && newer.UseCount == 1, "One increment per use");
Check(EntryQueries.ByUsageFrequency(config.Config.Entries).First() == frequent, "Frequency outranks both manual order and recency");
newer.UseCount = 2; newer.LastUsedAt = frequent.LastUsedAt.AddSeconds(1);
Check(EntryQueries.ByUsageFrequency(config.Config.Entries).First() == newer, "Recency breaks equal-count ties");
newer.LastUsedAt = frequent.LastUsedAt;
Check(EntryQueries.ByUsageFrequency(config.Config.Entries).First() == frequent, "Manual order stabilizes equal timestamps");
frequent.UseCount = long.MaxValue; config.TouchEntry(frequent.Id);
Check(frequent.UseCount == long.MaxValue, "Counter saturates instead of overflowing");
config.FlushPendingSave();
using var restored = new ConfigManager(); restored.Load();
Check(restored.Config.Entries.Single(e => e.Id == frequent.Id).UseCount == long.MaxValue, "Count persists across reload");
Check(restored.Config.SortFoldersByFrequency, "Enabled by default for existing configurations");
Console.WriteLine("PASS usage counts, frequency/recency/manual tie ordering, persistence, overflow and default setting.");
