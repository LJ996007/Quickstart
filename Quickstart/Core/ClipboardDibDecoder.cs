namespace Quickstart.Core;

using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>Decodes the packed CF_DIB / CF_DIBV5 used by screenshot tools (including PixPin).
/// These contain a bitmap info header, not a BMP file header; WinForms does not reliably
/// convert them to CF_BITMAP. See learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapv5header.
/// </summary>
internal static class ClipboardDibDecoder
{
    internal const int MaxBytes = (int)ClipboardHistoryContent.MaxImagePixels * 4 + 4096;

    internal static Bitmap? Decode(byte[] data)
    {
        if (data.Length < 40 || data.Length > MaxBytes) return null;
        int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        var header = I32(0);
        if (header is not (40 or 52 or 56 or 108 or 124) || header > data.Length) return null;
        var width = I32(4); var signedHeight = I32(8);
        var height = Math.Abs((long)signedHeight);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14, 2));
        var compression = U32(16);
        if (width <= 0 || height == 0 || width * height > ClipboardHistoryContent.MaxImagePixels
            || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12, 2)) != 1
            || bits is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 3 or 6)) return null;

        var masks = header == 40 ? compression == 3 ? 12 : compression == 6 ? 16 : 0 : 0;
        var colors = U32(32);
        if (bits <= 8 && colors == 0) colors = 1u << bits;
        var pixelOffset = (long)header + masks + colors * 4L;
        var stride = ((long)width * bits + 31) / 32 * 4;
        // Packed V5 DIBs may carry a color profile before the pixel array.
        if (header == 124 && U32(112) == pixelOffset)
            pixelOffset += U32(116);
        if (pixelOffset > data.Length || stride * height > data.Length - pixelOffset) return null;

        // Decode common screenshot BGRA ourselves: GDI+ discards the V5 alpha mask.
        var standardMasks = compression == 0 || (header + masks >= 52
            && U32(40) == 0x00ff0000 && U32(44) == 0x0000ff00 && U32(48) == 0x000000ff);
        if (bits == 32 && standardMasks)
        {
            var hasAlphaMask = header + masks >= 56 && U32(52) == 0xff000000;
            var bitmap = new Bitmap(width, (int)height, PixelFormat.Format32bppArgb);
            var locked = bitmap.LockBits(new Rectangle(0, 0, width, (int)height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[width * 4];
                for (var y = 0; y < height; y++)
                {
                    var sourceY = signedHeight < 0 ? y : height - y - 1;
                    data.AsSpan((int)(pixelOffset + sourceY * stride), row.Length).CopyTo(row);
                    // BI_RGB's fourth byte is reserved, not alpha. Only an explicit mask
                    // makes zero alpha meaningful (a fully transparent V5 image is valid).
                    if (!hasAlphaMask)
                        for (var x = 3; x < row.Length; x += 4) row[x] = 255;
                    Marshal.Copy(row, 0, locked.Scan0 + (int)y * locked.Stride, row.Length);
                }
            }
            catch { bitmap.UnlockBits(locked); bitmap.Dispose(); throw; }
            bitmap.UnlockBits(locked);
            return bitmap;
        }

        // Let the system image decoder handle palettes, 16-bit masks and 24-bit RGB.
        using var stream = new MemoryStream(data.Length + 14);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x4d42); writer.Write(data.Length + 14); writer.Write(0); writer.Write((int)pixelOffset + 14);
        writer.Write(data); stream.Position = 0;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
