namespace Quickstart.Core;

using System.Text.Json;

public static class AiSecretStore
{
    private static readonly string SecretPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Quickstart",
        "ai-secrets.local.json");

    private static readonly object Lock = new();

    /// <summary>平台密钥保护器，由各端在启动时设置（Windows=DPAPI，macOS=Keychain）。</summary>
    public static ISecretProtector Protector { get; set; } = new NullSecretProtector();

    public static bool HasApiKey(AiProviderConfig provider)
        => !string.IsNullOrWhiteSpace(GetApiKey(provider));

    public static string GetApiKey(AiProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Id))
            return string.Empty;

        lock (Lock)
        {
            var secrets = Load();
            if (secrets.ProviderApiKeys.TryGetValue(provider.Id, out var record))
            {
                var storedKey = ResolveApiKey(record);
                if (!string.IsNullOrWhiteSpace(storedKey))
                    return storedKey;
            }
        }

        return Protector.Unprotect(provider.ApiKeyProtected);
    }

    public static void SaveApiKey(string providerId, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new InvalidOperationException("Provider Id 不能为空。");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API Key 不能为空。");

        lock (Lock)
        {
            var secrets = Load();
            secrets.ProviderApiKeys[providerId] = CreateRecord(apiKey.Trim());
            Save(secrets);
        }
    }

    private static AiSecretsFile Load()
    {
        if (!File.Exists(SecretPath))
            return new AiSecretsFile();

        try
        {
            var json = File.ReadAllText(SecretPath);
            var secrets = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AiSecretsFile)
                ?? new AiSecretsFile();
            secrets.ProviderApiKeys = new Dictionary<string, AiSecretRecord>(
                secrets.ProviderApiKeys
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null),
                StringComparer.OrdinalIgnoreCase);
            return secrets;
        }
        catch
        {
            return new AiSecretsFile();
        }
    }

    private static void Save(AiSecretsFile secrets)
    {
        var directory = Path.GetDirectoryName(SecretPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(secrets, AppConfigJsonContext.Default.AiSecretsFile);
        var tempPath = SecretPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SecretPath, overwrite: true);

        try
        {
            File.SetAttributes(SecretPath, File.GetAttributes(SecretPath) | FileAttributes.Hidden);
        }
        catch
        {
            // Hiding the local secret file is best-effort.
        }
    }

    private static AiSecretRecord CreateRecord(string apiKey)
    {
        var protectedValue = Protector.Protect(apiKey);
        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            return new AiSecretRecord
            {
                Storage = "dpapi",
                ProtectedValue = protectedValue
            };
        }

        return new AiSecretRecord
        {
            Storage = "plain-local",
            PlainValue = apiKey
        };
    }

    private static string ResolveApiKey(AiSecretRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ProtectedValue))
        {
            var key = Protector.Unprotect(record.ProtectedValue);
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        return record.PlainValue;
    }
}

public sealed class AiSecretsFile
{
    public Dictionary<string, AiSecretRecord> ProviderApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiSecretRecord
{
    public string Storage { get; set; } = string.Empty;
    public string ProtectedValue { get; set; } = string.Empty;
    public string PlainValue { get; set; } = string.Empty;
}
