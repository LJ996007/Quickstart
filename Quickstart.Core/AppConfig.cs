namespace Quickstart.Core;

using Quickstart.Models;

public enum OpenWith
{
    TotalCommander,
    Explorer,
    DirectoryOpus,
    LastUsed
}

public enum AiPromptTarget
{
    /// <summary>调用 API，在 AI 面板内显示结果（默认）。</summary>
    Api,
    /// <summary>把"模板+选中文字"渲染后填入网页对话框（如 DeepSeek 网页版）。</summary>
    Web
}

public enum LeftDragAction
{
    AiActionPicker,
    EverythingSearch
}

public sealed class AppConfig
{
    public List<QuickEntry> Entries { get; set; } = [];
    public Dictionary<string, DateTime> GroupLastUsedAt { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<WebSearchToolConfig> WebSearchTools { get; set; } = WebSearchToolConfig.CreateDefaults();
    public AiConfig Ai { get; set; } = AiConfig.CreateDefault();
    public OcrConfig Ocr { get; set; } = new();
    public string TotalCommanderPath { get; set; } = string.Empty;
    public string DirectoryOpusPath { get; set; } = string.Empty;
    public string EverythingPath { get; set; } = string.Empty;
    /// <summary>
    /// 上次对 TC/DOpus/Everything 做过自动探测的应用版本号。
    /// 本版本探测过且未找到则不再重复探测；升级版本后重试一次。设置页「检测」按钮仍强制探测。
    /// </summary>
    public string ToolDetectionAttemptedVersion { get; set; } = string.Empty;
    public OpenWith DefaultOpenWith { get; set; } = OpenWith.TotalCommander;
    public bool StartWithWindows { get; set; }
    public bool ShellMenuEnabled { get; set; }
    public string HotKey { get; set; } = string.Empty;
    public bool RightDragEnabled { get; set; } = true;
    public LeftDragAction LeftDragAction { get; set; } = LeftDragAction.AiActionPicker;
    public int RightDragTriggerDistance { get; set; } = 120;
    public int RightDragVerticalTolerance { get; set; } = 50;
    public bool RememberLastView { get; set; } = true;
    public string LastViewTab { get; set; } = "Folders";
    public string LastViewGroup { get; set; } = "全部";
    public bool SortByRecentUsage { get; set; }
    /// <summary>右滑主面板左侧分类标签的显示顺序。</summary>
    public List<string> MainPopupTabOrder { get; set; } = [];
    public ClipboardHistoryConfig ClipboardHistory { get; set; } = new();
}

/// <summary>右滑主弹窗「历史」Tab：系统剪贴板文本历史。</summary>
public sealed class ClipboardHistoryConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxItems { get; set; } = 50;
    public int MaxTextLength { get; set; } = 20_000;
    /// <summary>true = 退出后保存到本地 JSON；false = 仅内存。</summary>
    public bool Persist { get; set; } = true;
    public bool IgnoreDuplicates { get; set; } = true;
}

/// <summary>截图 OCR（百度通用文字识别等）配置。密钥走 AiSecretStore，不进明文 config。</summary>
public sealed class OcrConfig
{
    public const string BaiduApiKeySecretId = "baidu-ocr-ak";
    public const string BaiduSecretKeySecretId = "baidu-ocr-sk";

    /// <summary>是否在左滑工具列显示「截图 OCR」。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>提供方：baidu（预留 local 等）。</summary>
    public string Provider { get; set; } = "baidu";

    /// <summary>百度 language_type，默认中英混合。</summary>
    public string LanguageType { get; set; } = "CHN_ENG";

    /// <summary>识别后是否自动 Ctrl+V 到手势来源窗（首期默认关）。</summary>
    public bool AutoPasteAfterOcr { get; set; }
}

public sealed class WebSearchToolConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public static List<WebSearchToolConfig> CreateDefaults() =>
    [
        new()
        {
            Id = "google",
            Name = "Google 查询",
            UrlTemplate = "https://www.google.com/search?q={query}"
        },
        new()
        {
            Id = "ccgp",
            Name = "政府采购网查询",
            UrlTemplate = "https://www.google.com/search?q=site%3Accgp.gov.cn+{query}"
        },
        new()
        {
            Id = "creditchina",
            Name = "信用中国查询",
            UrlTemplate = "https://www.google.com/search?q=site%3Acreditchina.gov.cn+{query}"
        }
    ];
}

public sealed class AiConfig
{
    public string CurrentProviderId { get; set; } = string.Empty;
    public string DefaultPromptId { get; set; } = string.Empty;
    public string DefaultSkillId { get; set; } = string.Empty;
    public int MaxFileBytes { get; set; } = 256 * 1024;
    /// <summary>"发送到网页"目标的对话页地址（默认 DeepSeek 网页版）。</summary>
    public string WebChatUrl { get; set; } = "https://chat.deepseek.com/";
    public List<AiProviderConfig> Providers { get; set; } = [];
    public List<AiPromptPreset> PromptPresets { get; set; } = [];
    public List<AiSkill> Skills { get; set; } = [];
    /// <summary>
    /// 左滑动作面板最近使用的动作键（格式 Kind:Id，如 Prompt:summarize），最近的在前，最多 6 个。
    /// </summary>
    public List<string> RecentAiActionIds { get; set; } = [];

    public static AiConfig CreateDefault()
    {
        const string summarizePromptId = "summarize";
        const string translatePromptId = "translate-zh";
        const string explainPromptId = "explain";
        const string reviewSkillId = "review";

        return new AiConfig
        {
            CurrentProviderId = "deepseek",
            DefaultPromptId = summarizePromptId,
            DefaultSkillId = reviewSkillId,
            Providers =
            [
                new()
                {
                    Id = "deepseek",
                    Name = "DeepSeek",
                    BaseUrl = "https://api.deepseek.com",
                    DefaultModel = "deepseek-v4-flash",
                    Models = ["deepseek-v4-flash", "deepseek-v4-pro"]
                },
                new()
                {
                    Id = "qwen",
                    Name = "Qwen",
                    BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                    DefaultModel = "qwen-plus",
                    Models = ["qwen-plus", "qwen-max", "qwen-vl-plus"]
                },
                new()
                {
                    Id = "glm",
                    Name = "GLM",
                    BaseUrl = "https://open.bigmodel.cn/api/paas/v4/",
                    DefaultModel = "glm-5.1",
                    Models = ["glm-5.1", "glm-4.7", "glm-4.5-air"]
                }
            ],
            PromptPresets =
            [
                new()
                {
                    Id = summarizePromptId,
                    Name = "总结要点",
                    Template = "请用中文总结下面内容，提炼关键结论和待办事项：\r\n\r\n{文本}"
                },
                new()
                {
                    Id = translatePromptId,
                    Name = "翻译成中文",
                    Template = "请把下面内容翻译成自然、准确的中文，保留专业术语：\r\n\r\n{文本}"
                },
                new()
                {
                    Id = explainPromptId,
                    Name = "解释内容",
                    Template = "请解释下面内容的含义、背景和可能影响，用清晰的中文回答：\r\n\r\n{文本}"
                }
            ],
            Skills =
            [
                new()
                {
                    Id = reviewSkillId,
                    Name = "分析并给建议",
                    Description = "先总结输入，再给出风险和行动建议。",
                    Steps =
                    [
                        new() { PromptId = summarizePromptId },
                        new()
                        {
                            PromptId = "advice",
                            InlineTemplate = "基于下面的总结，请列出主要风险、机会和下一步建议：\r\n\r\n{文本}"
                        }
                    ]
                }
            ]
        };
    }
}

public sealed class AiProviderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
    public List<string> Models { get; set; } = [];
    public string ApiKeyProtected { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public string DeepSeekThinkingEffort { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class AiPromptPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    /// <summary>运行目标：调用 API 或发送到网页对话框。</summary>
    public AiPromptTarget Target { get; set; } = AiPromptTarget.Api;

    public override string ToString() => Name;
}

public sealed class AiSkill
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<AiSkillStep> Steps { get; set; } = [];

    public override string ToString() => Name;
}

public sealed class AiSkillStep
{
    public string PromptId { get; set; } = string.Empty;
    public string InlineTemplate { get; set; } = string.Empty;
}
