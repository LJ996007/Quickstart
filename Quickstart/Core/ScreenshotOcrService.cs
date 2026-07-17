namespace Quickstart.Core;

using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Quickstart.UI;

/// <summary>
/// 框选截图 → 百度 OCR → 纯文本写入剪贴板。
/// </summary>
internal static class ScreenshotOcrService
{
    private const int MaxEdgePixels = 2000;
    private const long JpegQuality = 85L;

    public static bool HasCredentials()
        => AiSecretStore.HasApiKeyById(OcrConfig.BaiduApiKeySecretId)
           && AiSecretStore.HasApiKeyById(OcrConfig.BaiduSecretKeySecretId);

    /// <summary>
    /// 同步框选 + 异步 OCR。取消框选返回 null；无文字返回空字符串。
    /// </summary>
    public static async Task<string?> CaptureAndRecognizeAsync(
        OcrConfig ocrConfig,
        Screen? screen,
        IProgress<string>? status,
        CancellationToken token)
    {
        status?.Report("请框选要识别的区域…");
        using var bitmap = RegionCaptureOverlay.CaptureRegion(screen);
        if (bitmap == null)
            return null; // 用户取消

        token.ThrowIfCancellationRequested();
        status?.Report("识别中…");

        var apiKey = AiSecretStore.GetApiKeyById(OcrConfig.BaiduApiKeySecretId);
        var secretKey = AiSecretStore.GetApiKeyById(OcrConfig.BaiduSecretKeySecretId);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("请先在设置中填写百度 OCR 的 API Key 和 Secret Key。");

        var imageBytes = EncodeImage(bitmap);
        using var client = new BaiduOcrClient();
        var text = await client.RecognizeGeneralBasicAsync(
            imageBytes,
            apiKey,
            secretKey,
            ocrConfig.LanguageType,
            token);

        return text ?? string.Empty;
    }

    public static async Task SetClipboardPlainTextAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 剪贴板需 STA；与 WebPromptSender 类似做重试
        for (var attempt = 0; attempt < 6; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return;
                }

                await SetClipboardOnStaThreadAsync(text, token);
                return;
            }
            catch (ExternalException)
            {
                await Task.Delay(40 * (attempt + 1), token);
            }
        }

        throw new InvalidOperationException("无法写入剪贴板，请稍后重试。");
    }

    private static Task SetClipboardOnStaThreadAsync(string text, CancellationToken token)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Quickstart OCR Clipboard"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.WaitAsync(token);
    }

    private static byte[] EncodeImage(Bitmap source)
    {
        using var prepared = DownscaleIfNeeded(source, MaxEdgePixels);
        using var ms = new MemoryStream();

        // JPEG 更小，适合上传；屏幕文字用 85 质量足够
        var encoder = GetJpegEncoder();
        if (encoder != null)
        {
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            prepared.Save(ms, encoder, ep);
        }
        else
        {
            prepared.Save(ms, ImageFormat.Png);
        }

        return ms.ToArray();
    }

    private static Bitmap DownscaleIfNeeded(Bitmap source, int maxEdge)
    {
        var maxDim = Math.Max(source.Width, source.Height);
        if (maxDim <= maxEdge)
        {
            // 复制一份，避免调用方 Dispose 原图时影响
            return new Bitmap(source);
        }

        var scale = maxEdge / (double)maxDim;
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(w, h);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, w, h);
        return resized;
    }

    private static ImageCodecInfo? GetJpegEncoder()
        => ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
}
