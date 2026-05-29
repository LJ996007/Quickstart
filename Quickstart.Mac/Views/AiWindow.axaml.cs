namespace Quickstart.Mac.Views;

using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Quickstart.Core;

public partial class AiWindow : Window
{
    private readonly ConfigManager _config = new();
    private readonly AiClient _aiClient = new();
    private readonly SkillRunner _skillRunner;
    private CancellationTokenSource? _runCts;

    public AiWindow() : this(new ConfigManager()) { }

    public AiWindow(ConfigManager config)
    {
        InitializeComponent();
        _config = config;
        _skillRunner = new SkillRunner(_aiClient);

        ModeBox.ItemsSource = new[] { "Prompt", "Skill" };
        ModeBox.SelectedIndex = 0;
        ModeBox.SelectionChanged += (_, _) => RefreshActions();

        RunButton.Click += OnRun;
        CancelButton.Click += (_, _) => _runCts?.Cancel();
        CopyButton.Click += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(ResultBox.Text) && Clipboard is { } cb)
                await cb.SetTextAsync(ResultBox.Text);
        };
        SettingsButton.Click += (_, _) => { }; // AI 设置窗口：后续阶段

        Closed += (_, _) => _aiClient.Dispose();

        RefreshActions();
    }

    private void RefreshActions()
    {
        var ai = _config.Config.Ai;
        if (ModeBox.SelectedIndex == 1)
        {
            ActionBox.ItemsSource = ai.Skills;
            var idx = ai.Skills.FindIndex(s => s.Id == ai.DefaultSkillId);
            ActionBox.SelectedIndex = idx >= 0 ? idx : (ai.Skills.Count > 0 ? 0 : -1);
        }
        else
        {
            ActionBox.ItemsSource = ai.PromptPresets;
            var idx = ai.PromptPresets.FindIndex(p => p.Id == ai.DefaultPromptId);
            ActionBox.SelectedIndex = idx >= 0 ? idx : (ai.PromptPresets.Count > 0 ? 0 : -1);
        }
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        var input = (InputBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusLabel.Text = "请输入要处理的内容。";
            return;
        }

        var ai = _config.Config.Ai;
        var provider = ai.Providers.FirstOrDefault(p => p.Id == ai.CurrentProviderId)
            ?? ai.Providers.FirstOrDefault();
        if (provider == null)
        {
            StatusLabel.Text = "请先配置 AI Provider。";
            return;
        }

        SetRunning(true);
        ResultBox.Text = string.Empty;
        _runCts = new CancellationTokenSource();

        try
        {
            var model = provider.DefaultModel.Trim();
            string output;

            if (ModeBox.SelectedIndex == 1)
            {
                if (ActionBox.SelectedItem is not AiSkill skill)
                    throw new InvalidOperationException("请选择 Skill。");

                var skillProvider = string.IsNullOrWhiteSpace(skill.ProviderId)
                    ? provider
                    : ai.Providers.FirstOrDefault(p => p.Id == skill.ProviderId) ?? provider;
                var skillModel = string.IsNullOrWhiteSpace(skill.Model) ? model : skill.Model;

                output = await _skillRunner.RunSkillAsync(
                    ai, skill, skillProvider, skillModel, input,
                    new Progress<SkillRunProgress>(p => StatusLabel.Text = $"{p.Message} ({p.CurrentStep}/{p.TotalSteps})"),
                    _runCts.Token);
            }
            else
            {
                if (ActionBox.SelectedItem is not AiPromptPreset prompt)
                    throw new InvalidOperationException("请选择 Prompt。");

                StatusLabel.Text = "正在请求 AI...";
                output = await _skillRunner.RunPromptAsync(provider, model, prompt, input, _runCts.Token);
            }

            ResultBox.Text = output;
            StatusLabel.Text = "完成";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "已取消";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "失败";
            ResultBox.Text = ex.Message;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        RunButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        ModeBox.IsEnabled = !running;
        ActionBox.IsEnabled = !running;
    }
}
