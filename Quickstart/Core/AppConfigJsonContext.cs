namespace Quickstart.Core;

using System.Text.Json.Serialization;

[JsonSerializable(typeof(AppConfig))]
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
internal partial class AppConfigJsonContext : JsonSerializerContext;
