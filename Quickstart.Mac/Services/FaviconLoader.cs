namespace Quickstart.Mac.Services;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

/// <summary>
/// macOS/Avalonia 端的网站图标加载：经 Google favicon 服务取 PNG（Avalonia 可直接解码，
/// 规避 .ico 跨平台解码问题），带内存 + 磁盘缓存。后续可升级为"直连优先 + Google 兜底"。
/// </summary>
public sealed class FaviconLoader
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Quickstart", "favicons-mac");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ConcurrentDictionary<string, Bitmap?> _mem = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public static string? GetHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var candidate = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;
    }

    public Task<Bitmap?> GetAsync(string url)
    {
        var host = GetHost(url);
        if (host == null) return Task.FromResult<Bitmap?>(null);
        if (_mem.TryGetValue(host, out var cached)) return Task.FromResult(cached);
        return _inflight.GetOrAdd(host, LoadAsync);
    }

    private async Task<Bitmap?> LoadAsync(string host)
    {
        try
        {
            var bytes = LoadFromDisk(host) ?? await DownloadAsync(host).ConfigureAwait(false);
            Bitmap? bitmap = null;
            if (bytes is { Length: > 0 })
            {
                try { bitmap = new Bitmap(new MemoryStream(bytes)); }
                catch { bitmap = null; }
            }

            _mem[host] = bitmap;
            return bitmap;
        }
        catch
        {
            _mem[host] = null;
            return null;
        }
        finally
        {
            _inflight.TryRemove(host, out _);
        }
    }

    private static byte[]? LoadFromDisk(string host)
    {
        var path = CacheFile(host);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private async Task<byte[]?> DownloadAsync(string host)
    {
        var url = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64";
        using var resp = await _http.GetAsync(url).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (bytes.Length == 0) return null;

        try
        {
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(CacheFile(host), bytes).ConfigureAwait(false);
        }
        catch
        {
            // 缓存写入失败不影响显示
        }

        return bytes;
    }

    private static string CacheFile(string host)
        => Path.Combine(CacheDir, Regex.Replace(host, "[^a-zA-Z0-9.-]", "_") + ".png");
}
