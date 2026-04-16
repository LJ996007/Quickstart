namespace Quickstart.Utils;

using System.Runtime.InteropServices;

public static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    private static readonly Dictionary<string, Icon> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static Icon? GetIcon(string path, bool isDirectory)
    {
        var cacheKey = isDirectory ? "<DIR>" : Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrEmpty(cacheKey)) cacheKey = "<NOEXT>";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attrs = 0;

        if (isDirectory)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = FILE_ATTRIBUTE_DIRECTORY;
        }

        var result = SHGetFileInfo(
            isDirectory ? "folder" : (File.Exists(path) ? path : $"x{cacheKey}"),
            attrs, ref shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        var icon = (Icon)Icon.FromHandle(shfi.hIcon).Clone();
        DestroyIcon(shfi.hIcon);

        _cache[cacheKey] = icon;
        return icon;
    }
}
