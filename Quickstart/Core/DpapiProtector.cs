namespace Quickstart.Core;

using System.Runtime.InteropServices;
using System.Text;

public static class DpapiProtector
{
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(ProtectBytes(bytes));
    }

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
            return string.Empty;

        try
        {
            var bytes = Convert.FromBase64String(protectedText);
            return Encoding.UTF8.GetString(UnprotectBytes(bytes));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] ProtectBytes(byte[] bytes)
    {
        var input = ToDataBlob(bytes);
        var output = new DataBlob();

        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                return [];

            return FromDataBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
            FreeOutputBlob(output);
        }
    }

    private static byte[] UnprotectBytes(byte[] bytes)
    {
        var input = ToDataBlob(bytes);
        var output = new DataBlob();

        try
        {
            if (!CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                return [];

            return FromDataBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
            FreeOutputBlob(output);
        }
    }

    private static DataBlob ToDataBlob(byte[] bytes)
    {
        var blob = new DataBlob
        {
            cbData = bytes.Length,
            pbData = Marshal.AllocHGlobal(bytes.Length)
        };
        Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
        return blob;
    }

    private static byte[] FromDataBlob(DataBlob blob)
    {
        if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
            return [];

        var bytes = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        return bytes;
    }

    private static void FreeInputBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero)
            Marshal.FreeHGlobal(blob.pbData);
    }

    private static void FreeOutputBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero)
            LocalFree(blob.pbData);
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        string? ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }
}

/// <summary>Windows 平台的密钥保护器实现（DPAPI）。</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => DpapiProtector.Protect(plainText);
    public string Unprotect(string protectedText) => DpapiProtector.Unprotect(protectedText);
}
