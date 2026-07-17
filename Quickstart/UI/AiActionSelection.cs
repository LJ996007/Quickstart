namespace Quickstart.UI;

internal enum AiActionKind
{
    Prompt,
    Skill,
    PlainTextPaste,
    EverythingSearch,
    WebSearch,
    ScreenshotOcr
}

internal sealed class AiActionSelection
{
    public AiActionKind Kind { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int StepCount { get; init; }
    public string UrlTemplate { get; init; } = string.Empty;

    public bool IsPrompt => Kind == AiActionKind.Prompt;
    public bool IsSkill => Kind == AiActionKind.Skill;
    public bool IsPlainTextPaste => Kind == AiActionKind.PlainTextPaste;
    public bool IsEverythingSearch => Kind == AiActionKind.EverythingSearch;
    public bool IsWebSearch => Kind == AiActionKind.WebSearch;
    public bool IsScreenshotOcr => Kind == AiActionKind.ScreenshotOcr;

    /// <summary>配置中 RecentAiActionIds 使用的键：Kind:Id。</summary>
    public string RecentKey => $"{Kind}:{Id}";

    public static bool TryParseRecentKey(string key, out AiActionKind kind, out string id)
    {
        kind = default;
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var sep = key.IndexOf(':');
        if (sep <= 0 || sep >= key.Length - 1)
            return false;

        if (!Enum.TryParse(key[..sep], ignoreCase: true, out kind))
            return false;

        id = key[(sep + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(id);
    }
}
