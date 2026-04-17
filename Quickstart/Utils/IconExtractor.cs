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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static readonly Dictionary<string, Icon> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static Icon? GetIcon(string path, bool isDirectory, bool useLargeIcon = false)
    {
        var sizeSuffix = useLargeIcon ? "@L" : "@S";
        var cacheKey = isDirectory ? "<DIR>" : Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrEmpty(cacheKey)) cacheKey = "<NOEXT>";
        cacheKey += sizeSuffix;

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON;
        uint attrs = 0;

        if (!useLargeIcon)
            flags |= SHGFI_SMALLICON;

        if (isDirectory)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = FILE_ATTRIBUTE_DIRECTORY;
        }
        else if (!File.Exists(path))
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = FILE_ATTRIBUTE_NORMAL;
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

    /// <summary>Get an icon from shell32.dll by index.</summary>
    public static Icon? GetShellIcon(int index, bool useLargeIcon = false)
    {
        var cacheKey = $"<SHELL:{index}:{(useLargeIcon ? "L" : "S")}>";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var hIcon = ExtractIcon(IntPtr.Zero, shell32, index);
        if (hIcon == IntPtr.Zero || hIcon == (IntPtr)1)
            return null;

        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        _cache[cacheKey] = icon;
        return icon;
    }
}
