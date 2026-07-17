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
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // 这些扩展名图标通常嵌在文件自身（或快捷方式目标），不能按扩展名共用
    private static readonly HashSet<string> PerFileIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ocx", ".cpl", ".scr", ".sys",
        ".ico", ".cur", ".ani",
        ".lnk", ".url",
        ".msi", ".msc", ".appref-ms"
    };

    private static readonly Dictionary<string, Icon> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否应按实际路径取图标（而不是按扩展名共享通用图标）。
    /// </summary>
    public static bool NeedsPerFileIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && PerFileIconExtensions.Contains(ext);
    }

    public static Icon? GetIcon(string path, bool isDirectory, bool useLargeIcon = false)
    {
        var sizeSuffix = useLargeIcon ? "@L" : "@S";

        if (isDirectory)
        {
            var dirKey = "<DIR>" + sizeSuffix;
            if (_cache.TryGetValue(dirKey, out var dirCached))
                return dirCached;

            var dirIcon = QueryShellIcon("folder", FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: true, useLargeIcon);
            if (dirIcon != null)
                _cache[dirKey] = dirIcon;
            return dirIcon;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var needsPerFile = NeedsPerFileIcon(path);

        // 可执行文件/快捷方式等：按完整路径缓存并读取真实文件图标
        if (needsPerFile)
        {
            var pathKey = path + sizeSuffix;
            if (_cache.TryGetValue(pathKey, out var pathCached))
                return pathCached;

            // 优先读真实路径（拿到 .exe 自带图标）；失败再退回扩展名通用图标
            Icon? realIcon = null;
            try
            {
                if (File.Exists(path))
                    realIcon = QueryShellIcon(path, FILE_ATTRIBUTE_NORMAL, useFileAttributes: false, useLargeIcon);
            }
            catch
            {
                // 网络盘/权限问题等：继续回退
            }

            if (realIcon != null)
            {
                _cache[pathKey] = realIcon;
                return realIcon;
            }

            // 回退：通用 .exe 等类型图标（不访问路径）
            var fallback = GetTypeIcon(extension, useLargeIcon);
            if (fallback != null)
                _cache[pathKey] = fallback;
            return fallback;
        }

        // 普通文档：同一扩展名共用一个图标，避免反复访问磁盘
        return GetTypeIcon(extension, useLargeIcon);
    }

    private static Icon? GetTypeIcon(string extension, bool useLargeIcon)
    {
        var sizeSuffix = useLargeIcon ? "@L" : "@S";
        var cacheKey = (string.IsNullOrEmpty(extension) ? "<NOEXT>" : extension) + sizeSuffix;
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var lookupPath = string.IsNullOrEmpty(extension) ? "file" : $"file{extension}";
        var icon = QueryShellIcon(lookupPath, FILE_ATTRIBUTE_NORMAL, useFileAttributes: true, useLargeIcon);
        if (icon != null)
            _cache[cacheKey] = icon;
        return icon;
    }

    private static Icon? QueryShellIcon(string lookupPath, uint attrs, bool useFileAttributes, bool useLargeIcon)
    {
        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | (useLargeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        if (useFileAttributes)
            flags |= SHGFI_USEFILEATTRIBUTES;

        var result = SHGetFileInfo(
            lookupPath,
            attrs, ref shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return (Icon)Icon.FromHandle(shfi.hIcon).Clone();
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
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

        try
        {
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            _cache[cacheKey] = icon;
            return icon;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
