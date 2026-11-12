namespace Quickstart.UI;

using System.Globalization;
using Quickstart.Core;

/// <summary>Keep both the folder and its parent identifiable within a label's pixel budget.</summary>
internal static class FolderPathLabel
{
    private const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    public static string ParentName(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(parent) ? "" : FolderDirectoryService.FromPath(parent).Name;
    }

    public static string Format(string name, string path, Font font, int width)
    {
        var parent = ParentName(path);
        if (parent.Length == 0) return Compact(name, font, width);
        const string separator = " · ";
        var available = width - Measure(separator, font);
        if (available < Measure("……", font)) return Compact(name, font, width);
        var nameWidth = Math.Min(Measure(name, font), (int)(available * .45));
        var parentWidth = Math.Min(Measure(parent, font), available - nameWidth);
        nameWidth = Math.Min(Measure(name, font), available - parentWidth);
        return Compact(name, font, nameWidth) + separator + Compact(parent, font, available - nameWidth, preferPrefix: true);
    }

    public static string Compact(string text, Font font, int width, bool preferPrefix = false)
    {
        if (Measure(text, font) <= width) return text;
        if (Measure("…", font) > width) return "";
        // Preserve both ends and never split a surrogate pair or combining character.
        var elements = StringInfo.ParseCombiningCharacters(text);
        string Candidate(int count)
        {
            var head = preferPrefix ? (count * 2 + 2) / 3 : (count + 1) / 2;
            var tail = count - head;
            return text[..elements[head]] + "…" + (tail == 0 ? "" : text[elements[^tail]..]);
        }
        var low = 0;
        var high = Math.Max(0, elements.Length - 1);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Measure(Candidate(middle), font) <= width) low = middle;
            else high = middle - 1;
        }
        return Candidate(low);
    }

    private static int Measure(string text, Font font)
        => TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Flags).Width;
}
