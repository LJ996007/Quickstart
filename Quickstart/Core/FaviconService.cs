namespace Quickstart.Core;

using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 直接从目标网站抓取 favicon（不经第三方服务），带内存 + 磁盘缓存。
/// 抓取顺序：网站根目录 /favicon.ico → 解析首页 HTML 的 &lt;link rel=icon&gt;。
/// 抓不到的域名写入 .miss 标记，避免反复请求。
/// </summary>
public sealed class FaviconService : IDisposable
{
    private const int MaxIconSize = 64;
    private static readonly TimeSpan HitTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissTtl = TimeSpan.FromDays(7);

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart", "favicons");

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloadGate = new(4);
    // value 为 null 表示"已查询但无图标"，避免重复下载
    private readonly ConcurrentDictionary<string, Image?> _memCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Image?>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public FaviconService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) QuickstartFavicon/1.0");
    }

    public static string? GetHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var candidate = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;
    }

    /// <summary>仅查内存缓存，不触盘，可安全在 UI 线程同步调用。</summary>
    public Image? TryGetMemoryCached(string url)
    {
        var host = GetHost(url);
        if (host == null)
            return null;

        return _memCache.TryGetValue(host, out var cached) ? cached : null;
    }

    /// <summary>是否已知该 host 无图标（内存 miss 标记）。</summary>
    public bool IsKnownMiss(string url)
    {
        var host = GetHost(url);
        if (host == null)
            return true;

        return _memCache.TryGetValue(host, out var cached) && cached == null;
    }

    /// <summary>同步查询缓存（内存 / 磁盘），不发起网络请求。</summary>
    public Image? TryGetCached(string url)
    {
        var host = GetHost(url);
        if (host == null)
            return null;

        if (_memCache.TryGetValue(host, out var cached))
            return cached;

        var png = CacheFilePath(host);
        if (File.Exists(png) && IsFresh(png, HitTtl))
        {
            try
            {
                var bytes = File.ReadAllBytes(png);
                using var ms = new MemoryStream(bytes);
                var img = new Bitmap(ms);
                _memCache[host] = img;
                return img;
            }
            catch
            {
                // 损坏的缓存文件，忽略并尝试重新下载
            }
        }

        var miss = MissFilePath(host);
        if (File.Exists(miss) && IsFresh(miss, MissTtl))
        {
            _memCache[host] = null; // 已知抓取失败，阻止再次下载
            return null;
        }

        return null;
    }

    /// <summary>异步获取 favicon（命中缓存立即返回，否则下载并缓存）。</summary>
    public Task<Image?> GetFaviconAsync(string url)
    {
        var host = GetHost(url);
        if (host == null)
            return Task.FromResult<Image?>(null);

        if (_memCache.TryGetValue(host, out var cached))
            return Task.FromResult(cached);

        // 查磁盘缓存（命中会写入 _memCache）
        TryGetCached(url);
        if (_memCache.TryGetValue(host, out var afterDisk))
            return Task.FromResult(afterDisk);

        return _inflight.GetOrAdd(host, _ => DownloadAndCacheAsync(url, host));
    }

    private async Task<Image?> DownloadAndCacheAsync(string url, string host)
    {
        await _downloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var scheme = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http" : "https";

            // 1. 直接尝试 /favicon.ico
            var image = await TryDownloadIconAsync($"{scheme}://{host}/favicon.ico").ConfigureAwait(false);

            // 2. 回退：解析首页 HTML 的 <link rel=icon>
            if (image == null)
            {
                var iconUrl = await TryFindIconUrlFromHtmlAsync($"{scheme}://{host}/").ConfigureAwait(false);
                if (iconUrl != null)
                    image = await TryDownloadIconAsync(iconUrl).ConfigureAwait(false);
            }

            // 3. 第三方兜底：直连被拒（如 ChatGPT 的 Cloudflare 403）时，用 Google favicon 服务
            //    （返回 PNG，可解码）。代价是把域名发给 Google。
            if (image == null)
            {
                var googleUrl = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64";
                image = await TryDownloadIconAsync(googleUrl).ConfigureAwait(false);
            }

            if (image != null)
                SaveToDisk(host, image);
            else
                MarkMiss(host);

            _memCache[host] = image;
            return image;
        }
        catch
        {
            MarkMiss(host);
            _memCache[host] = null;
            return null;
        }
        finally
        {
            _downloadGate.Release();
            _inflight.TryRemove(host, out _);
        }
    }

    private async Task<string?> TryFindIconUrlFromHtmlAsync(string pageUrl)
    {
        try
        {
            using var resp = await _http.GetAsync(pageUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[65536];
            var read = 0;
            int n;
            while (read < buffer.Length
                && (n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read)).ConfigureAwait(false)) > 0)
            {
                read += n;
            }

            var html = Encoding.UTF8.GetString(buffer, 0, read);

            foreach (Match link in Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = link.Value;
                if (!Regex.IsMatch(tag, "rel\\s*=\\s*[\"']?[^\"'>]*icon", RegexOptions.IgnoreCase))
                    continue;

                var href = Regex.Match(tag, "href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                if (!href.Success)
                    continue;

                if (Uri.TryCreate(new Uri(pageUrl), href.Groups[1].Value, out var resolved))
                    return resolved.ToString();
            }
        }
        catch
        {
            // 忽略，回退到 /favicon.ico 已处理
        }

        return null;
    }

    private async Task<Bitmap?> TryDownloadIconAsync(string iconUrl)
    {
        try
        {
            using var resp = await _http.GetAsync(iconUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
                return null; // System.Drawing 不支持 SVG

            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 1_000_000)
                return null;

            return DecodeIcon(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? DecodeIcon(byte[] bytes)
    {
        // 优先按 .ico 解析（请求较大尺寸以取到最高分辨率的帧）
        try
        {
            using var ms = new MemoryStream(bytes);
            using var icon = new Icon(ms, new Size(MaxIconSize, MaxIconSize));
            using var bmp = icon.ToBitmap();
            return CopyCapped(bmp);
        }
        catch
        {
            // 不是 ico，按通用图片解析
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return CopyCapped(img);
        }
        catch
        {
            return null;
        }
    }

    // 保留原生分辨率（仅对超大图等比降采样到 MaxIconSize），不放大小图标，避免二次重采样导致模糊
    private static Bitmap CopyCapped(Image src)
    {
        var w = Math.Max(1, src.Width);
        var h = Math.Max(1, src.Height);
        var scale = Math.Min(1.0, (double)MaxIconSize / Math.Max(w, h));
        var tw = Math.Max(1, (int)Math.Round(w * scale));
        var th = Math.Max(1, (int)Math.Round(h * scale));

        var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, tw, th));
        return bmp;
    }

    private static void SaveToDisk(string host, Image image)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            image.Save(CacheFilePath(host), ImageFormat.Png);
            var miss = MissFilePath(host);
            if (File.Exists(miss))
                File.Delete(miss);
        }
        catch
        {
            // 缓存写入失败不影响显示
        }
    }

    private static void MarkMiss(string host)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(MissFilePath(host), []);
        }
        catch
        {
            // 忽略
        }
    }

    private static bool IsFresh(string path, TimeSpan ttl)
        => DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < ttl;

    private static string SafeName(string host)
        => Regex.Replace(host, "[^a-zA-Z0-9.-]", "_");

    private static string CacheFilePath(string host)
        => Path.Combine(CacheDir, SafeName(host) + ".png");

    private static string MissFilePath(string host)
        => Path.Combine(CacheDir, SafeName(host) + ".miss");

    public void Dispose()
    {
        _http.Dispose();
        _downloadGate.Dispose();
        foreach (var image in _memCache.Values)
            image?.Dispose();
        _memCache.Clear();
    }
}
