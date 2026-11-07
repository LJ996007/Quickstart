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
        {
            var files = new ClipboardHistoryItem { Kind = ClipboardHistoryKind.Files, FilePaths = paths.ToArray() };
            PreparePreview(files);
            return files;
        }

        foreach (var format in new[] { "PNG", "image/png", DataFormats.GetFormat(17).Name, DataFormats.Dib })
        {
            if (!data.GetDataPresent(format, false)) continue;
            var isDib = format == DataFormats.Dib || format == DataFormats.GetFormat(17).Name;
            try
            {
                var bytes = ReadBytes(data.GetData(format, false), isDib ? ClipboardDibDecoder.MaxBytes : MaxImageBytes);
                if (bytes == null) continue;
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = isDib ? ClipboardDibDecoder.Decode(bytes) : Image.FromStream(stream);
                if (image == null || (long)image.Width * image.Height > MaxImagePixels) continue;
                if (isDib) return FromImage(image);
                return new() { Kind = ClipboardHistoryKind.Image, ImagePng = bytes,
                    ThumbnailPng = RenderThumbnail(image), ImageWidth = image.Width, ImageHeight = image.Height };
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
            { /* A malformed custom format must not hide a valid fallback. */ }
        }
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is Image bitmap)
        {
            using (bitmap) return FromImage(bitmap);
        }
        var text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
        return string.IsNullOrWhiteSpace(text) ? null : new() { Text = text };
    }

    private static byte[]? ReadBytes(object? value, int limit)
    {
        if (value is byte[] bytes) return bytes.Length <= limit ? bytes : null;
        if (value is not Stream source) return null;
        if (source.CanSeek && source.Length > limit) return null;
        var position = source.CanSeek ? source.Position : 0;
        try
        {
            if (source.CanSeek) source.Position = 0;
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (copy.Length + count > limit) return null;
                copy.Write(buffer, 0, count);
            }
            return copy.ToArray();
        }
        finally { if (source.CanSeek) source.Position = position; }
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
        if (item.Kind == ClipboardHistoryKind.Files && item.ThumbnailPng.Length == 0)
        {
            // Only inspect the first local file, off the UI thread. Do not wake network
            // shares or shell thumbnail providers just to open the clipboard panel.
            var path = item.FilePaths.FirstOrDefault();
            if (path != null && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\")
                && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".tif" or ".tiff")
            {
                try
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (file.Length <= MaxImageBytes)
                    {
                        using var image = Image.FromStream(file);
                        if ((long)image.Width * image.Height <= MaxImagePixels) item.ThumbnailPng = RenderThumbnail(image);
                    }
                }
                catch { /* A missing file still belongs in history and can be removed there. */ }
            }
            return true;
        }
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
        const int size = 96;
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
        if (item.ThumbnailPng.Length == 0) return null;
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
