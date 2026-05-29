namespace Quickstart.Core;

using System.Drawing.Imaging;
using System.Text.RegularExpressions;

/// <summary>
/// 管理网页条目的自定义图标：导入用户选择的图片时统一归一化（等比降采样到上限尺寸），
/// 存放在应用数据目录，按条目 Id 命名。绘制时再缩放到列表的统一尺寸。
/// </summary>
public static class CustomIconStore
{
    private const int MaxSize = 128;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickstart", "custom-icons");

    public static string GetPath(string id)
        => Path.Combine(Dir, SafeName(id) + ".png");

    public static Image? TryLoad(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            // 读入字节再解码，避免锁定文件（便于后续覆盖/删除）
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>归一化并保存自定义图标，返回保存路径。</summary>
    public static string Save(string id, Image source)
    {
        Directory.CreateDirectory(Dir);
        var path = GetPath(id);
        using var normalized = Normalize(source);
        normalized.Save(path, ImageFormat.Png);
        return path;
    }

    public static void Remove(string id)
    {
        try
        {
            var path = GetPath(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删除失败不影响功能
        }
    }

    // 等比降采样到 MaxSize 以内（不放大小图），统一为 32bppArgb
    private static Bitmap Normalize(Image src)
    {
        var w = Math.Max(1, src.Width);
        var h = Math.Max(1, src.Height);
        var scale = Math.Min(1.0, (double)MaxSize / Math.Max(w, h));
        var tw = Math.Max(1, (int)Math.Round(w * scale));
        var th = Math.Max(1, (int)Math.Round(h * scale));

        var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, tw, th));
        return bmp;
    }

    private static string SafeName(string id)
        => Regex.Replace(id, "[^a-zA-Z0-9_.-]", "_");
}
