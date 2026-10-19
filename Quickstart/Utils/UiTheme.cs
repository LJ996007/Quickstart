namespace Quickstart.Utils;

/// <summary>
/// 弹出面板共用的颜色与字体。主面板、圆环轮、动作面板、对话框底栏都从这里取值，
/// 保证“选中 / 悬停 / 当前分类”在各处看起来一致。
/// </summary>
internal static class UiTheme
{
    public const string FontFamily = "Microsoft YaHei UI";

    // 主色：选中描边、当前分类指示条、拖放插入线
    public static readonly Color Accent = Color.FromArgb(55, 138, 221);
    /// <summary>主色上的文字（选中条目 / 当前分类），比 Accent 深，保证可读。</summary>
    public static readonly Color AccentText = Color.FromArgb(28, 96, 180);
    public static readonly Color AccentMuted = Color.FromArgb(180, 210, 245);

    // 选中 / 悬停
    public static readonly Color SelectionFill = Color.FromArgb(236, 244, 255);
    public static readonly Color HoverFill = Color.FromArgb(245, 249, 255);
    public static readonly Color DropTargetFill = Color.FromArgb(220, 232, 246);

    // 表面
    public static readonly Color Surface = Color.FromArgb(250, 251, 253);
    public static readonly Color SurfaceAlt = Color.FromArgb(243, 245, 248);
    public static readonly Color Card = Color.FromArgb(255, 255, 255);

    // 线条
    public static readonly Color Border = Color.FromArgb(218, 224, 232);
    public static readonly Color BorderSubtle = Color.FromArgb(230, 234, 240);
    public static readonly Color Divider = Color.FromArgb(210, 216, 224);

    // 文字
    public static readonly Color TextPrimary = Color.FromArgb(28, 32, 40);
    public static readonly Color TextSecondary = Color.FromArgb(78, 86, 100);
    public static readonly Color TextMuted = Color.FromArgb(132, 138, 148);
    public static readonly Color TextDisabled = Color.FromArgb(168, 174, 184);

    // 无边框图标按钮
    public static readonly Color ButtonHoverFill = Color.FromArgb(234, 238, 244);
    public static readonly Color ButtonPressedFill = Color.FromArgb(222, 228, 236);

    // 浮层提示（“已复制”）
    public static readonly Color ToastFill = Color.FromArgb(45, 50, 60);

    public static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
        => new(FontFamily, size, style);

    /// <summary>无边框扁平图标按钮的统一悬停 / 按下色。</summary>
    public static void StyleFlatButton(Button button, Color backColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.MouseOverBackColor = ButtonHoverFill;
        button.FlatAppearance.MouseDownBackColor = ButtonPressedFill;
    }
}
