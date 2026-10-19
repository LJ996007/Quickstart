namespace Quickstart.Core;

using System.Buffers.Binary;
using System.Text;

public sealed record FileSearchResult(string FullPath, bool IsFolder, long? Size, DateTime? ModifiedAt)
{
    public string Name => Path.GetFileName(FullPath.TrimEnd('\\'));
}

public sealed record FileSearchPage(uint Total, uint Offset, IReadOnlyList<FileSearchResult> Items);

/// <summary>Everything SDK QUERY2W/LIST2 wire format. All protocol fields are packed DWORDs,
/// including the reply HWND on 64-bit Windows. No native SDK DLL is required.</summary>
public static class EverythingIpcProtocol
{
    public const uint RequestFlags = 0x4 | 0x10 | 0x40; // full path, size, modified
    public const int PageSize = 100;

    public static byte[] BuildQuery(long replyWindow, uint replyId, string query, uint offset, uint sort = 1)
    {
        if (query.Length > 8192 || query.Contains('\0'))
            throw new ArgumentException("搜索词过长或包含无效字符。");
        var data = new byte[28 + Encoding.Unicode.GetByteCount(query) + 2];
        uint[] fields = [unchecked((uint)replyWindow), replyId, 0, offset, PageSize, RequestFlags, sort];
        for (var i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), fields[i]);
        Encoding.Unicode.GetBytes(query, data.AsSpan(28));
        return data;
    }

    public static FileSearchPage ParseResults(byte[] data)
    {
        void Require(int at, int length)
        {
            if (at < 0 || length < 0 || at > data.Length - length)
                throw new InvalidDataException("Everything 返回了不完整的数据。");
        }
        uint U32(int at) { Require(at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)); }
        long I64(int at) { Require(at, 8); return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at)); }
        Require(0, 20);
        var count = U32(4);
        var flags = U32(12);
        if (count > PageSize || (flags & 4) == 0 || (flags & ~RequestFlags) != 0)
            throw new InvalidDataException("Everything 返回了不支持的结果格式。");
        var tableEnd = checked(20 + (int)count * 8);
        Require(0, tableEnd);
        var items = new List<FileSearchResult>((int)count);
        for (var i = 0; i < count; i++)
        {
            var itemAt = 20 + i * 8;
            var at = checked((int)U32(itemAt + 4));
            if (at < tableEnd) throw new InvalidDataException("Everything 结果偏移无效。");
            var bytes = checked((int)U32(at) * 2);
            at += 4;
            Require(at, checked(bytes + 2));
            if (data[at + bytes] != 0 || data[at + bytes + 1] != 0)
                throw new InvalidDataException("Everything 路径终止符无效。");
            var path = Encoding.Unicode.GetString(data, at, bytes);
            at += bytes + 2;
            long? size = null;
            DateTime? modified = null;
            if ((flags & 0x10) != 0) { var value = I64(at); at += 8; if (value >= 0) size = value; }
            if ((flags & 0x40) != 0)
            {
                var value = I64(at);
                if (value > 0)
                {
                    try { modified = DateTime.FromFileTimeUtc(value).ToLocalTime(); }
                    catch (ArgumentOutOfRangeException) { /* Unknown/unavailable timestamp. */ }
                }
            }
            items.Add(new FileSearchResult(path, (U32(itemAt) & 1) != 0, size, modified));
        }
        return new FileSearchPage(U32(0), U32(8), items);
    }
}
