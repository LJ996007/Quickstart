namespace Quickstart.Core;

public sealed class SkillRunProgress
{
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class SkillRunner
{
    private readonly AiClient _client;

    public SkillRunner(AiClient client)
    {
        _client = client;
    }

    public async Task<string> RunPromptAsync(
        AiProviderConfig provider,
        string model,
        AiPromptPreset prompt,
        string input,
        CancellationToken cancellationToken)
    {
        var rendered = PromptRenderer.Render(prompt.Template, input);
        return await _client.CompleteAsync(provider, model, rendered, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunSkillAsync(
        AiConfig config,
        AiSkill skill,
        AiProviderConfig provider,
        string model,
        string input,
        IProgress<SkillRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (skill.Steps.Count == 0)
            throw new InvalidOperationException("该 Skill 没有配置任何步骤。");

        var currentInput = input;
        var results = new List<string>();
        for (var i = 0; i < skill.Steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = skill.Steps[i];
            var template = ResolveStepTemplate(config, step);
            var stepName = ResolveStepName(config, step, i + 1);
            progress?.Report(new SkillRunProgress
            {
                CurrentStep = i + 1,
                TotalSteps = skill.Steps.Count,
                Message = $"正在执行：{stepName}"
            });

            var rendered = PromptRenderer.Render(template, currentInput);
            var output = await _client.CompleteAsync(provider, model, rendered, cancellationToken).ConfigureAwait(false);
            results.Add($"## {stepName}\r\n\r\n{output}");
            currentInput = output;
        }

        return string.Join("\r\n\r\n", results);
    }

    private static string ResolveStepTemplate(AiConfig config, AiSkillStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.InlineTemplate))
            return step.InlineTemplate;

        var prompt = config.PromptPresets.FirstOrDefault(item => item.Id == step.PromptId);
        if (prompt == null)
            throw new InvalidOperationException($"找不到 Skill 步骤引用的 Prompt：{step.PromptId}");

        return prompt.Template;
    }

    private static string ResolveStepName(AiConfig config, AiSkillStep step, int index)
    {
        var prompt = config.PromptPresets.FirstOrDefault(item => item.Id == step.PromptId);
        if (prompt != null)
            return prompt.Name;

        return string.IsNullOrWhiteSpace(step.PromptId)
            ? $"步骤 {index}"
            : step.PromptId;
    }
}
