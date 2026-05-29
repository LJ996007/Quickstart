namespace Quickstart.Core;

/// <summary>
/// 平台相关的密钥保护抽象。Windows 用 DPAPI 实现，macOS 可用 Keychain 实现。
/// </summary>
public interface ISecretProtector
{
    /// <summary>加密明文，返回可持久化的字符串；不支持时返回空字符串。</summary>
    string Protect(string plainText);

    /// <summary>解密；失败或不支持时返回空字符串。</summary>
    string Unprotect(string protectedText);
}

/// <summary>
/// 默认保护器：不加密（直接失败/返回空），使 AiSecretStore 退回到 plain-local 存储。
/// 各平台应在启动时把 <see cref="AiSecretStore.Protector"/> 替换为真实实现。
/// </summary>
public sealed class NullSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => string.Empty;
    public string Unprotect(string protectedText) => string.Empty;
}
