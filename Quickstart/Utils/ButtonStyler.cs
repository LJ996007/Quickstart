namespace Quickstart.Utils;

/// <summary>
/// Provides consistent rounded button styling across the application.
/// </summary>
public static class ButtonStyler
{
    private const int PillCornerRadius = 999;

    public static void ApplyPrimary(Button btn)
    {
        ConfigureButton(
            btn,
            backColor: Color.FromArgb(96, 170, 255),
            foreColor: Color.White,
            hoverBackColor: Color.FromArgb(88, 162, 247),
            pressedBackColor: Color.FromArgb(76, 149, 233),
            borderColor: Color.FromArgb(42, 255, 255, 255),
            hoverBorderColor: Color.FromArgb(52, 255, 255, 255),
            pressedBorderColor: Color.FromArgb(30, 255, 255, 255),
            highlightColor: Color.FromArgb(30, 255, 255, 255),
            cornerRadius: PillCornerRadius);
    }

    public static void ApplySecondary(Button btn)
    {
        ConfigureButton(
            btn,
            backColor: Color.FromArgb(247, 247, 248),
            foreColor: Color.FromArgb(55, 65, 81),
            hoverBackColor: Color.FromArgb(240, 242, 245),
            pressedBackColor: Color.FromArgb(232, 236, 240),
            borderColor: Color.FromArgb(120, 205, 209, 214),
            hoverBorderColor: Color.FromArgb(136, 198, 203, 209),
            pressedBorderColor: Color.FromArgb(150, 188, 194, 201),
            highlightColor: Color.Transparent,
            cornerRadius: FormStyler.StandardCornerRadius);
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
