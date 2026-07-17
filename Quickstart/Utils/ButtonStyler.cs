namespace Quickstart.Utils;

/// <summary>
/// WinUI 3 / Windows 11 inspired flat button styling.
/// No gradient highlights, no heavy borders — just clean background tints
/// with subtle hover/pressed states.
/// </summary>
public static class ButtonStyler
{
    private const int FlatCornerRadius = 4;

    public static void ApplyPrimary(Button btn)
    {
        ConfigureButton(
            btn,
            backColor: Color.FromArgb(0, 103, 192),
            foreColor: Color.White,
            hoverBackColor: Color.FromArgb(0, 89, 171),
            pressedBackColor: Color.FromArgb(0, 78, 152),
            borderColor: Color.Transparent,
            hoverBorderColor: Color.Transparent,
            pressedBorderColor: Color.Transparent,
            highlightColor: Color.Transparent,
            cornerRadius: FlatCornerRadius);
    }

    public static void ApplySecondary(Button btn)
    {
        ConfigureButton(
            btn,
            backColor: Color.FromArgb(249, 249, 249),
            foreColor: Color.FromArgb(48, 48, 48),
            hoverBackColor: Color.FromArgb(238, 238, 238),
            pressedBackColor: Color.FromArgb(229, 229, 229),
            borderColor: Color.Transparent,
            hoverBorderColor: Color.FromArgb(220, 220, 220),
            pressedBorderColor: Color.FromArgb(210, 210, 210),
            highlightColor: Color.Transparent,
            cornerRadius: FlatCornerRadius);
    }

    public static void ApplyDangerSecondary(Button btn)
    {
        ConfigureButton(
            btn,
            backColor: Color.FromArgb(253, 247, 247),
            foreColor: Color.FromArgb(196, 43, 28),
            hoverBackColor: Color.FromArgb(251, 235, 235),
            pressedBackColor: Color.FromArgb(248, 223, 223),
            borderColor: Color.Transparent,
            hoverBorderColor: Color.FromArgb(243, 200, 200),
            pressedBorderColor: Color.FromArgb(238, 180, 180),
            highlightColor: Color.Transparent,
            cornerRadius: FlatCornerRadius);
    }

    private static void ConfigureButton(
        Button btn,
        Color backColor,
        Color foreColor,
        Color hoverBackColor,
        Color pressedBackColor,
        Color borderColor,
        Color hoverBorderColor,
        Color pressedBorderColor,
        Color highlightColor,
        int cornerRadius)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.UseVisualStyleBackColor = false;
        btn.BackColor = backColor;
        btn.ForeColor = foreColor;
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = backColor;
        btn.FlatAppearance.MouseDownBackColor = backColor;

        if (btn is RoundedButton roundedButton)
        {
            roundedButton.CornerRadius = cornerRadius;
            roundedButton.NormalBackColor = backColor;
            roundedButton.HoverBackColor = hoverBackColor;
            roundedButton.PressedBackColor = pressedBackColor;
            roundedButton.NormalBorderColor = borderColor;
            roundedButton.HoverBorderColor = hoverBorderColor;
            roundedButton.PressedBorderColor = pressedBorderColor;
            roundedButton.HighlightColor = highlightColor;
            roundedButton.Invalidate();
        }
    }
}
