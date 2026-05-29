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

public sealed class AppConfig
{
    public List<QuickEntry> Entries { get; set; } = [];
    public Dictionary<string, DateTime> GroupLastUsedAt { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AiConfig Ai { get; set; } = AiConfig.CreateDefault();
    public string TotalCommanderPath { get; set; } = string.Empty;
    public string DirectoryOpusPath { get; set; } = string.Empty;
    public OpenWith DefaultOpenWith { get; set; } = OpenWith.TotalCommander;
    public bool StartWithWindows { get; set; }
    public bool ShellMenuEnabled { get; set; }
    public string HotKey { get; set; } = string.Empty;
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
