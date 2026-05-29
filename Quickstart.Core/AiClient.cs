namespace Quickstart.Core;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class AiClient : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> CompleteAsync(
        AiProviderConfig provider,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = AiSecretStore.GetApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"请先在 AI 设置中填写 {provider.Name} 的 API Key。");

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            throw new InvalidOperationException("Provider Base URL 不能为空。");

        var selectedModel = string.IsNullOrWhiteSpace(model) ? provider.DefaultModel : model.Trim();
        if (string.IsNullOrWhiteSpace(selectedModel))
            throw new InvalidOperationException("模型名称不能为空。");

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(provider.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new JsonObject
        {
            ["model"] = selectedModel,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = "你是 Quickstart 托盘工具里的 AI 助手。请用清晰、实用、简洁的中文回答。"
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            }
        };

        ApplyProviderOptions(provider, body);

        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(provider.TimeoutSeconds, 10, 300)));

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"AI 接口返回错误 {(int)response.StatusCode}：{ExtractErrorMessage(json)}");

            return ExtractContent(json);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("AI 请求超时，请检查网络或调大超时时间。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"AI 请求失败：{ex.Message}", ex);
        }
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed);

        return new Uri($"{trimmed}/chat/completions");
    }

    private static void ApplyProviderOptions(AiProviderConfig provider, JsonObject body)
    {
        if (!IsDeepSeekProvider(provider))
            return;

        switch (provider.DeepSeekThinkingEffort)
        {
            case "disabled":
                body["thinking"] = new JsonObject { ["type"] = "disabled" };
                break;

            case "high":
            case "max":
                body["thinking"] = new JsonObject { ["type"] = "enabled" };
                body["reasoning_effort"] = provider.DeepSeekThinkingEffort;
                break;
        }
    }

    private static bool IsDeepSeekProvider(AiProviderConfig provider)
        => provider.Id.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
            || provider.Name.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    private static string ExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new InvalidOperationException($"AI 返回了空内容：{SummarizeJson(json)}");

            var message = choices[0].GetProperty("message");
            var content = ExtractTextFromMessage(message, "content");
            if (!string.IsNullOrWhiteSpace(content))
                return content;

            var reasoningContent = ExtractTextFromMessage(message, "reasoning_content");
            if (!string.IsNullOrWhiteSpace(reasoningContent))
                return reasoningContent;

            throw new InvalidOperationException($"AI 返回了空内容：{SummarizeJson(json)}");
        }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException && ex.Message.StartsWith("AI 返回了空内容", StringComparison.Ordinal))
                throw;

            throw new InvalidOperationException($"AI 响应解析失败：{ex.Message}", ex);
        }
    }

    private static string ExtractTextFromMessage(JsonElement message, string propertyName)
    {
        if (!message.TryGetProperty(propertyName, out var property))
            return string.Empty;

        if (property.ValueKind == JsonValueKind.String)
            return property.GetString() ?? string.Empty;

        if (property.ValueKind != JsonValueKind.Array)
            return property.ToString();

        var parts = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("text", out var textProperty)
                    && textProperty.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(textProperty.GetString()))
                {
                    parts.Add(textProperty.GetString()!);
                }
                else if (item.TryGetProperty("content", out var contentProperty)
                    && contentProperty.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(contentProperty.GetString()))
                {
                    parts.Add(contentProperty.GetString()!);
                }
            }
        }

        return string.Join("\r\n", parts);
    }

    private static string SummarizeJson(string json)
        => string.IsNullOrWhiteSpace(json)
            ? "无响应内容"
            : json.Length > 800 ? json[..800] + "..." : json;

    private static string ExtractErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "无响应内容";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? json;
                }

                return error.ToString();
            }
        }
        catch
        {
            // Fall through to raw response.
        }

        return json.Length > 500 ? json[..500] + "..." : json;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
