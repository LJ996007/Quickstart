namespace Quickstart.Core;

using System.Text.Json.Serialization;

[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppConfigJsonContext : JsonSerializerContext;
