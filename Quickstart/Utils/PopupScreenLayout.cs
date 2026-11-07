namespace Quickstart.Utils;

/// <summary>Physical-pixel placement, independent of the foreground app and browser zoom.</summary>
internal static class PopupScreenLayout
{
    public static Size Fit(Size preferred, Size available)
        => new(Math.Clamp(preferred.Width, 1, Math.Max(1, available.Width)),
            Math.Clamp(preferred.Height, 1, Math.Max(1, available.Height)));

    internal readonly record struct PanelGroup(Rectangle Bar, Rectangle Launcher, Rectangle Actions, Point AnchorOffset);

    public static PanelGroup Arrange(Rectangle area, Point anchor, int dpi, Size launcher, Size actions, int barHeight)
    {
        int S(int value) => UiScaleHelper.Scale(value, dpi);
        var margin = Math.Min(S(8), Math.Max(0, (Math.Min(area.Width, area.Height) - 1) / 2));
        var width = Math.Max(1, area.Width - margin * 2);
        var height = Math.Max(1, area.Height - margin * 2);
        var gap = Math.Min(S(12), Math.Max(0, (height - 3) / 4));
        barHeight = Math.Clamp(barHeight, 1, Math.Max(1, height - gap - 2));
        var top = barHeight + gap;
        var bodyHeight = Math.Max(1, height - top);
        Rectangle launchRect, actionRect;
        int groupWidth, groupHeight, split;
        if (width >= S(300) + S(320) + gap)
        {
            var rowHeight = Math.Min(bodyHeight, Math.Max(launcher.Height, actions.Height));
            var launchWidth = Math.Clamp(launcher.Width, S(300), width - S(320) - gap);
            var actionWidth = Math.Clamp(actions.Width, S(320), width - launchWidth - gap);
            actionRect = new(0, top, actionWidth, rowHeight);
            launchRect = new(actionWidth + gap, top, launchWidth, rowHeight);
            groupWidth = actionWidth + gap + launchWidth;
            groupHeight = top + rowHeight;
            split = actionWidth + gap / 2;
        }
        else
        {
            // Minimum content heights are preferences, never permission to extend
            // beyond a small monitor. Both lists retain their own scrollbars.
            gap = Math.Min(gap, Math.Max(0, bodyHeight - 2));
            var listHeight = Math.Max(2, bodyHeight - gap);
            var launchHeight = listHeight / 2;
            launchRect = new(0, top, width, launchHeight);
            actionRect = new(0, top + launchHeight + gap, width, listHeight - launchHeight);
            groupWidth = width;
            groupHeight = actionRect.Bottom;
            split = groupWidth / 2;
        }

        var offset = new Point(split, top + S(36));
        var origin = new Point(
            Math.Clamp(anchor.X - offset.X, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - groupWidth)),
            Math.Clamp(anchor.Y - offset.Y, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - margin - groupHeight)));
        launchRect.Offset(origin);
        actionRect.Offset(origin);
        return new(new Rectangle(origin, new Size(groupWidth, barHeight)), launchRect, actionRect, offset);
    }
}
