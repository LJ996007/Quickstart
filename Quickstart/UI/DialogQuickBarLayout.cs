namespace Quickstart.UI;

/// <summary>Keep the input row anchored while the result list grows into available space.</summary>
internal readonly record struct DialogQuickBarLayout(Rectangle Bounds, int InputOffset, int ResultsTop, int ResultsHeight)
{
    public static DialogQuickBarLayout Calculate(Rectangle dialog, Rectangle work, int dpi, bool expanded)
    {
        int S(int value) => (int)Math.Round(value * dpi / 96d);
        var width = Math.Min(work.Width, Math.Min(dialog.Width, S(640)));
        var compactHeight = Math.Min(work.Height, S(92));
        var x = Math.Clamp(dialog.Left + S(16), work.Left, work.Right - width);
        // Anchor using ONLY the compact bar's height. Expanding must never move the input.
        var anchor = Math.Clamp(dialog.Bottom + S(4), work.Top, work.Bottom - compactHeight);
        var above = anchor - work.Top;
        var below = work.Bottom - anchor - compactHeight;
        var desired = S(218);
        var growUp = below < desired && above > below;
        var extra = expanded ? Math.Min(desired, growUp ? above : below) : 0;
        var offset = growUp ? extra : 0;
        var bounds = new Rectangle(x, anchor - offset, width, compactHeight + extra);
        var listTop = growUp ? S(6) : S(76);
        var listHeight = growUp ? extra - S(12) : bounds.Height - S(102);
        return new(bounds, offset, listTop, expanded ? Math.Max(0, listHeight) : 0);
    }
}
