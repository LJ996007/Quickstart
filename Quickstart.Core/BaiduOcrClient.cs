namespace Quickstart.Core;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// 百度智能云通用文字识别（标准版 general_basic）。
/// Access Token 内存缓存；密钥由调用方从 AiSecretStore 传入。
/// </summary>
public sealed class BaiduOcrClient : IDisposable
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private static readonly object TokenLock = new();
    private static string? CachedToken;
    private static DateTimeOffset CachedTokenExpireAt = DateTimeOffset.MinValue;
    private static string? CachedTokenKeyFingerprint;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public BaiduOcrClient(HttpClient? httpClient = null)
    {
        if (httpClient == null)
        {
            _httpClient = SharedHttp;
            _ownsClient = false;
        }
        else
        {
            _httpClient = httpClient;
            _ownsClient = true;
        }
    }

    public async Task<string> RecognizeGeneralBasicAsync(
        byte[] imageBytes,
        string apiKey,
        string secretKey,
        string languageType = "CHN_ENG",
        CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            throw new InvalidOperationException("截图为空，无法识别。");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("请先在设置中填写百度 OCR 的 API Key 和 Secret Key。");

        var token = await GetAccessTokenAsync(apiKey.Trim(), secretKey.Trim(), cancellationToken);
        var base64 = Convert.ToBase64String(imageBytes);
        var body = new StringBuilder()
            .Append("image=").Append(Uri.EscapeDataString(base64))
            .Append("&language_type=").Append(Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(languageType) ? "CHN_ENG" : languageType.Trim()));

        var url = "https://aip.baidubce.com/rest/2.0/ocr/v1/general_basic?access_token="
                  + Uri.EscapeDataString(token);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error_code", out var errorCodeEl))
        {
            var code = errorCodeEl.ValueKind == JsonValueKind.Number
                ? errorCodeEl.GetInt32().ToString()
                : errorCodeEl.GetString() ?? "";
            var msg = root.TryGetProperty("error_msg", out var msgEl)
                ? msgEl.GetString() ?? "未知错误"
                : "未知错误";
            throw new InvalidOperationException(MapBaiduError(code, msg));
        }

        if (!root.TryGetProperty("words_result", out var words) || words.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var lines = new List<string>();
        foreach (var item in words.EnumerateArray())
        {
            if (item.TryGetProperty("words", out var w))
            {
                var text = w.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(text.TrimEnd());
            }
        }

        return string.Join("\n", lines);
    }

    public async Task<string> GetAccessTokenAsync(
        string apiKey,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = apiKey + "\0" + secretKey;
        lock (TokenLock)
        {
            if (!string.IsNullOrEmpty(CachedToken)
                && string.Equals(CachedTokenKeyFingerprint, fingerprint, StringComparison.Ordinal)
                && DateTimeOffset.UtcNow < CachedTokenExpireAt)
            {
                return CachedToken;
            }
        }

        var url = "https://aip.baidubce.com/oauth/2.0/token"
                  + "?grant_type=client_credentials"
                  + "&client_id=" + Uri.EscapeDataString(apiKey)
                  + "&client_secret=" + Uri.EscapeDataString(secretKey);

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            var desc = root.TryGetProperty("error_description", out var d)
                ? d.GetString()
                : err.GetString();
            throw new InvalidOperationException($"获取百度 Access Token 失败：{desc}");
        }

        if (!root.TryGetProperty("access_token", out var tokenEl))
            throw new InvalidOperationException("获取百度 Access Token 失败：响应无 access_token。");

        var token = tokenEl.GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("获取百度 Access Token 失败：token 为空。");

        // 百度 token 默认约 30 天；提前 1 天刷新
        var expiresIn = 29 * 24 * 3600;
        if (root.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var sec) && sec > 3600)
            expiresIn = Math.Max(3600, sec - 24 * 3600);

        lock (TokenLock)
        {
            CachedToken = token;
            CachedTokenKeyFingerprint = fingerprint;
            CachedTokenExpireAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        }

        return token;
    }

    public static void ClearTokenCache()
    {
        lock (TokenLock)
        {
            CachedToken = null;
            CachedTokenKeyFingerprint = null;
            CachedTokenExpireAt = DateTimeOffset.MinValue;
        }
    }

    private static string MapBaiduError(string code, string message)
    {
        // 常见额度/鉴权错误
        if (code is "17" or "18" or "19")
            return "百度 OCR 免费额度已用尽或请求过于频繁，请稍后重试或在控制台开通付费。";
        if (code is "100" or "110" or "111")
            return "百度 OCR 鉴权失败，请检查 API Key / Secret Key 是否正确。";
        if (code is "216201" or "216202")
            return "截图格式或内容无效，请重新框选。";
        return $"OCR 请求失败（{code}）：{message}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Quickstart-OCR/1.0");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
