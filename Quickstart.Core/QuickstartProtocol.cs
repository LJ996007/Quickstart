namespace Quickstart.Core;

public sealed record AddUrlRequest(string Url, string Title);

public static class QuickstartProtocol
{
    public const string Scheme = "quickstart";
    public const string Bookmarklet = "javascript:(()=>{const u=encodeURIComponent(location.href);const t=encodeURIComponent(document.title||location.hostname);location.href='quickstart://add-url?url='+u+'&title='+t;})();";

    public static bool IsProtocolUri(string value)
        => TryCreateQuickstartUri(value, out _);

    public static bool TryParseAddUrlRequest(string value, out AddUrlRequest? request)
    {
        request = null;
        if (!TryCreateQuickstartUri(value, out var uri))
            return false;

        var target = !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : uri.AbsolutePath.Trim('/');
        if (!string.Equals(target, "add-url", StringComparison.OrdinalIgnoreCase))
            return false;

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var urlUri)
            || (urlUri.Scheme != Uri.UriSchemeHttp && urlUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        query.TryGetValue("title", out var title);
        request = new AddUrlRequest(url, string.IsNullOrWhiteSpace(title) ? urlUri.Host : title.Trim());
        return true;
    }

    private static bool TryCreateQuickstartUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!)
            && string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                result[Decode(pair)] = string.Empty;
                continue;
            }

            var key = Decode(pair[..separatorIndex]);
            var value = Decode(pair[(separatorIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string Decode(string value)
        => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
}
