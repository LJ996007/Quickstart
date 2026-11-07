// Run: dotnet run --file scripts/check-web-search-tools.cs
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../Quickstart.Core/Quickstart.Core.csproj

using System.Text.Json;
using Quickstart.Core;

AppPaths.SetRootForTests(Path.GetFullPath("artifacts/debug/checks/bing-search/config"));
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Write(object value) => File.WriteAllText(AppPaths.ConfigPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
void WaitSaved(ConfigManager config)
{
    var expected = JsonSerializer.Serialize(config.Config, AppConfigJsonContext.Default.AppConfig);
    Check(SpinWait.SpinUntil(() => { try { return File.ReadAllText(AppPaths.ConfigPath) == expected; } catch (IOException) { return false; } }, 5000), "Async configuration save timed out");
}
Write(new { WebSearchTools = new[] { new { Id = "google", Name = "Google 查询", UrlTemplate = "https://www.google.com/search?q={query}", Enabled = true } } });
using (var config = new ConfigManager())
{
    config.Load();
    Check(config.Config.WebSearchTools.Select(t => t.Id).SequenceEqual(new[] { "google", "bing" }), "Legacy migration/order");
    WaitSaved(config);
    config.Load();
    Check(config.Config.WebSearchTools.Count(t => t.Id == "bing") == 1, "Repeated load duplicates Bing");
    WaitSaved(config);
    config.Config.WebSearchTools.RemoveAll(t => t.Id == "bing");
    config.Save(); WaitSaved(config); config.Load();
    Check(config.Config.WebSearchTools.All(t => t.Id != "bing"), "Deleted Bing restored");
}
Write(new { WebSearchTools = new[] { new { Id = "custom-bing", Name = "我的必应", UrlTemplate = "https://cn.bing.com/search?q={query}", Enabled = false } } });
using (var config = new ConfigManager())
{
    config.Load();
    Check(config.Config.WebSearchTools.Count == 1 && !config.Config.WebSearchTools[0].Enabled, "Custom disabled Bing changed");
    WaitSaved(config);
}
var bing = WebSearchToolConfig.CreateDefaults().Single(t => t.Id == "bing");
var query = "中文 A&B + #";
var url = bing.UrlTemplate.Replace("{query}", Uri.EscapeDataString(query));
Check(new Uri(url).Host == "www.bing.com" && Uri.UnescapeDataString(new Uri(url).Query[3..]) == query, "Query encoding");
Console.WriteLine("PASS Bing defaults, existing configuration migration, order, no duplication, deletion, custom disabled search and Chinese/special characters.");
