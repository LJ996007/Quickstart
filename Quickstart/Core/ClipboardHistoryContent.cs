namespace Quickstart.Core;

using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>Materializes clipboard content on a worker STA; never keeps a live IDataObject.</summary>
internal static class ClipboardHistoryContent
{
    internal const int MaxImageBytes = 16 * 1024 * 1024;
    internal const long MaxImagePixels = 32_000_000;

    internal static ClipboardHistoryItem? Read(IDataObject data)
    {
        // Explorer also offers text and thumbnails. Keep the actual file drop first.
        if (data.GetDataPresent(DataFormats.FileDrop)
            && data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            return new() { Kind = ClipboardHistoryKind.Files, FilePaths = paths.ToArray() };

        if (data.GetDataPresent("PNG"))
        {
            var png = data.GetData("PNG");
            using var stream = png switch
            {
                byte[] bytes when bytes.Length <= MaxImageBytes => new MemoryStream(bytes),
                Stream source when source.Length <= MaxImageBytes => CopyStream(source),
                _ => null
            };
            if (stream != null)
            {
                try
                {
                    using var image = Image.FromStream(stream);
                    if ((long)image.Width * image.Height > MaxImagePixels) return null;
                    return new() { Kind = ClipboardHistoryKind.Image, ImagePng = stream.ToArray(),
                        ThumbnailPng = RenderThumbnail(image), ImageWidth = image.Width, ImageHeight = image.Height };
                }
                catch (ArgumentException) { /* A malformed custom PNG can still offer a valid bitmap. */ }
            }
        }
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is Image bitmap)
        {
            using (bitmap) return FromImage(bitmap);
        }
        var text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
        return string.IsNullOrWhiteSpace(text) ? null : new() { Text = text };
    }

    private static MemoryStream CopyStream(Stream source)
    {
        var copy = new MemoryStream();
        var position = source.Position;
        try { source.Position = 0; source.CopyTo(copy); copy.Position = 0; return copy; }
        finally { source.Position = position; }
    }

    internal static ClipboardHistoryItem? FromImage(Image image)
    {
        if ((long)image.Width * image.Height > MaxImagePixels) return null;
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        if (stream.Length > MaxImageBytes) return null;
        return new() { Kind = ClipboardHistoryKind.Image, ImagePng = stream.ToArray(),
            ThumbnailPng = RenderThumbnail(image), ImageWidth = image.Width, ImageHeight = image.Height };
    }

    // Run during capture/load/copy on a worker, so showing a history of large screenshots
    // only decodes tiny previews on the UI thread.
    internal static bool PreparePreview(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardHistoryKind.Image || item.ThumbnailPng.Length > 0) return true;
        try
        {
            using var stream = new MemoryStream(item.ImagePng, writable: false);
            using var image = Image.FromStream(stream);
            if ((long)image.Width * image.Height > MaxImagePixels) return false;
            item.ImageWidth = image.Width; item.ImageHeight = image.Height;
            item.ThumbnailPng = RenderThumbnail(image);
            return true;
        }
        catch { return false; }
    }

    private static byte[] RenderThumbnail(Image image)
    {
        const int size = 48;
        using var thumbnail = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var scale = Math.Min((float)size / image.Width, (float)size / image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        graphics.DrawImage(image, (size - width) / 2, (size - height) / 2, width, height);
        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    internal static Image? CreateThumbnail(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardHistoryKind.Image || item.ThumbnailPng.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(item.ThumbnailPng, writable: false);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch { return null; }
    }

    internal static void Write(ClipboardHistoryItem item)
    {
        if (item.Kind == ClipboardHistoryKind.Files)
        {
            if (item.FilePaths.Length == 0 || item.FilePaths.Any(p => !File.Exists(p) && !Directory.Exists(p)))
                throw new IOException("原文件已移动或删除，无法复制。");
            var paths = new StringCollection();
            paths.AddRange(item.FilePaths);
            var data = new DataObject();
            data.SetFileDropList(paths);
            // History always copies; replaying a previous cut must never move source files.
            using var effect = new MemoryStream(BitConverter.GetBytes(1));
            data.SetData("Preferred DropEffect", effect);
            Clipboard.SetDataObject(data, true);
        }
        else if (item.Kind == ClipboardHistoryKind.Image)
        {
            using var stream = new MemoryStream(item.ImagePng, writable: false);
            using var image = Image.FromStream(stream);
            // GDI+ may seek its source stream while rendering the bitmap format. Keep
            // the PNG clipboard stream independent so OLE never publishes a suffix.
            using var png = new MemoryStream(item.ImagePng, writable: false);
            var data = new DataObject();
            data.SetImage(image);
            data.SetData("PNG", false, png);
            Clipboard.SetDataObject(data, true);
        }
        else Clipboard.SetText(item.Text, TextDataFormat.UnicodeText);
    }

    internal static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    try { completion.TrySetResult(operation()); return; }
                    catch (ExternalException) when (attempt < 5)
                    { if (token.WaitHandle.WaitOne(40 * (attempt + 1))) token.ThrowIfCancellationRequested(); }
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Quickstart ClipboardHistory STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
