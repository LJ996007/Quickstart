namespace Quickstart.Core;

using System.Text.Json.Serialization;

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(WebSearchToolConfig))]
[JsonSerializable(typeof(OcrConfig))]
[JsonSerializable(typeof(ClipboardHistoryConfig))]
[JsonSerializable(typeof(ClipboardHistoryItem))]
[JsonSerializable(typeof(ClipboardHistoryFile))]
[JsonSerializable(typeof(AiConfig))]
[JsonSerializable(typeof(AiProviderConfig))]
[JsonSerializable(typeof(AiPromptPreset))]
[JsonSerializable(typeof(AiSkill))]
[JsonSerializable(typeof(AiSkillStep))]
[JsonSerializable(typeof(AiSecretsFile))]
[JsonSerializable(typeof(AiSecretRecord))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppConfigJsonContext : JsonSerializerContext;
