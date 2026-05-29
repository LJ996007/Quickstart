namespace Quickstart.UI;

internal enum AiActionKind
{
    Prompt,
    Skill
}

internal sealed class AiActionSelection
{
    public AiActionKind Kind { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int StepCount { get; init; }

    public bool IsPrompt => Kind == AiActionKind.Prompt;
    public bool IsSkill => Kind == AiActionKind.Skill;
}
