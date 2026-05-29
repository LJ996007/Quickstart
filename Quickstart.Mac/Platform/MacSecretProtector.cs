namespace Quickstart.Mac.Platform;

using System;
using System.Diagnostics;
using Quickstart.Core;

/// <summary>
/// macOS 密钥保护：用系统 Keychain（经 `security` 命令）存取 AI API Key。
/// Protect 时生成随机账户名，把明文存入 Keychain，返回 "kc:{account}" 作为持久化标记；
/// Unprotect 时按标记从 Keychain 取回。仅在 macOS 上注册使用。
/// </summary>
public sealed class MacSecretProtector : ISecretProtector
{
    private const string Service = "Quickstart-AI";
    private const string Prefix = "kc:";

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var account = Guid.NewGuid().ToString("N");
        // -U：若已存在则更新；-w：密码值
        var ok = Run(["add-generic-password", "-U", "-s", Service, "-a", account, "-w", plainText], out _);
        return ok ? Prefix + account : string.Empty;
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText) || !protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            return string.Empty;

        var account = protectedText[Prefix.Length..];
        return Run(["find-generic-password", "-s", Service, "-a", account, "-w"], out var output)
            ? output.TrimEnd('\r', '\n')
            : string.Empty;
    }

    private static bool Run(string[] arguments, out string output)
    {
        output = string.Empty;
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
