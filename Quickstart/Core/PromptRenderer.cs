namespace Quickstart.Core;

public static class PromptRenderer
{
    public const string InputPlaceholder = "{文本}";

    public static string Render(string template, string input)
    {
        var safeTemplate = string.IsNullOrWhiteSpace(template)
            ? InputPlaceholder
            : template.Trim();

        return safeTemplate.Contains(InputPlaceholder, StringComparison.Ordinal)
            ? safeTemplate.Replace(InputPlaceholder, input, StringComparison.Ordinal)
            : $"{safeTemplate}\r\n\r\n{input}";
    }
}
