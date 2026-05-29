namespace Quickstart.UI;

using Quickstart.Core;
using Quickstart.Utils;

public sealed class AiSettingsForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly AiConfig _config;
    private readonly ListBox _providerList;
    private readonly ListBox _promptList;
    private readonly ListBox _skillList;

    public AiSettingsForm(ConfigManager configManager)
    {
        _configManager = configManager;
        _config = configManager.Config.Ai;

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "AI 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = UiScaleHelper.ScalePadding(this, new Padding(14));
        ClientSize = new Size(780, 600);
        MinimumSize = SizeFromClientSize(new Size(700, 540));
        FormStyler.ApplyRounded(this);

        _providerList = CreateListBox();
        _promptList = CreateListBox();
        _skillList = CreateListBox();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScaleHelper.Scale(this, 64)));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        tabs.TabPages.Add(BuildProviderTab());
        tabs.TabPages.Add(BuildPromptTab());
        tabs.TabPages.Add(BuildSkillTab());

        var closeBtn = new RoundedButton { Text = "关闭", Margin = new Padding(8, 0, 0, 0) };
        ButtonStyler.ApplyPrimary(closeBtn);
        closeBtn.Size = UiScaleHelper.GetButtonSize(this, closeBtn.Text, closeBtn.Font, 112, 38, horizontalLogicalPadding: 16);
        root.RowStyles[1].Height = closeBtn.Height + UiScaleHelper.Scale(this, 24);
        closeBtn.Click += (_, _) => Close();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, UiScaleHelper.Scale(this, 10), 0, UiScaleHelper.Scale(this, 6)),
            Margin = new Padding(0)
        };
        bottom.Controls.Add(closeBtn);

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(bottom, 0, 1);
        Controls.Add(root);

        RefreshAllLists();
    }

    private TabPage BuildProviderTab()
    {
        var tab = new TabPage("Provider");
        var actions = CreateActionPanel();
        actions.Controls.Add(CreateButton("添加", (_, _) => AddProvider()));
        actions.Controls.Add(CreateButton("编辑", (_, _) => EditSelectedProvider()));
        actions.Controls.Add(CreateButton("删除", (_, _) => DeleteSelectedProvider()));
        actions.Controls.Add(CreateButton("设为默认", (_, _) => SetDefaultProvider()));

        tab.Controls.Add(CreateTabLayout(_providerList, actions));
        return tab;
    }

    private TabPage BuildPromptTab()
    {
        var tab = new TabPage("Prompt");
        var actions = CreateActionPanel();
        actions.Controls.Add(CreateButton("添加", (_, _) => AddPrompt()));
        actions.Controls.Add(CreateButton("编辑", (_, _) => EditSelectedPrompt()));
        actions.Controls.Add(CreateButton("删除", (_, _) => DeleteSelectedPrompt()));
        actions.Controls.Add(CreateButton("设为默认", (_, _) => SetDefaultPrompt()));

        tab.Controls.Add(CreateTabLayout(_promptList, actions));
        return tab;
    }

    private TabPage BuildSkillTab()
    {
        var tab = new TabPage("Skill");
        var actions = CreateActionPanel();
        actions.Controls.Add(CreateButton("添加", (_, _) => AddSkill()));
        actions.Controls.Add(CreateButton("编辑", (_, _) => EditSelectedSkill()));
        actions.Controls.Add(CreateButton("删除", (_, _) => DeleteSelectedSkill()));
        actions.Controls.Add(CreateButton("设为默认", (_, _) => SetDefaultSkill()));

        tab.Controls.Add(CreateTabLayout(_skillList, actions));
        return tab;
    }

    private void AddProvider()
    {
        var provider = new AiProviderConfig();
        using var form = new AiProviderEditForm(provider);
        if (DialogPresenter.ShowModal(form, this) != DialogResult.OK)
            return;

        _config.Providers.Add(provider);
        _config.CurrentProviderId = provider.Id;
        SaveAndRefresh();
    }

    private void EditSelectedProvider()
    {
        if (_providerList.SelectedItem is not AiProviderConfig provider)
            return;

        using var form = new AiProviderEditForm(provider);
        if (DialogPresenter.ShowModal(form, this) == DialogResult.OK)
            SaveAndRefresh();
    }

    private void DeleteSelectedProvider()
    {
        if (_providerList.SelectedItem is not AiProviderConfig provider)
            return;

        if (_config.Providers.Count <= 1)
        {
            DialogPresenter.ShowMessage(this, "至少保留一个 Provider。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (DialogPresenter.ShowMessage(this, $"确定删除 \"{provider.Name}\" 吗？", "AI 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _config.Providers.Remove(provider);
        if (_config.CurrentProviderId == provider.Id)
            _config.CurrentProviderId = _config.Providers.First().Id;
        SaveAndRefresh();
    }

    private void SetDefaultProvider()
    {
        if (_providerList.SelectedItem is not AiProviderConfig provider)
            return;

        _config.CurrentProviderId = provider.Id;
        SaveAndRefresh();
    }

    private void AddPrompt()
    {
        var prompt = new AiPromptPreset { Template = PromptRenderer.InputPlaceholder };
        using var form = new AiPromptEditForm(prompt);
        if (DialogPresenter.ShowModal(form, this) != DialogResult.OK)
            return;

        _config.PromptPresets.Add(prompt);
        _config.DefaultPromptId = prompt.Id;
        SaveAndRefresh();
    }

    private void EditSelectedPrompt()
    {
        if (_promptList.SelectedItem is not AiPromptPreset prompt)
            return;

        using var form = new AiPromptEditForm(prompt);
        if (DialogPresenter.ShowModal(form, this) == DialogResult.OK)
            SaveAndRefresh();
    }

    private void DeleteSelectedPrompt()
    {
        if (_promptList.SelectedItem is not AiPromptPreset prompt)
            return;

        if (_config.PromptPresets.Count <= 1)
        {
            DialogPresenter.ShowMessage(this, "至少保留一个 Prompt。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_config.Skills.Any(skill => skill.Steps.Any(step => step.PromptId == prompt.Id)))
        {
            DialogPresenter.ShowMessage(this, "该 Prompt 正被 Skill 使用，不能删除。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (DialogPresenter.ShowMessage(this, $"确定删除 \"{prompt.Name}\" 吗？", "AI 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _config.PromptPresets.Remove(prompt);
        if (_config.DefaultPromptId == prompt.Id)
            _config.DefaultPromptId = _config.PromptPresets.First().Id;
        SaveAndRefresh();
    }

    private void SetDefaultPrompt()
    {
        if (_promptList.SelectedItem is not AiPromptPreset prompt)
            return;

        _config.DefaultPromptId = prompt.Id;
        SaveAndRefresh();
    }

    private void AddSkill()
    {
        var skill = new AiSkill();
        using var form = new AiSkillEditForm(_config, skill);
        if (DialogPresenter.ShowModal(form, this) != DialogResult.OK)
            return;

        _config.Skills.Add(skill);
        _config.DefaultSkillId = skill.Id;
        SaveAndRefresh();
    }

    private void EditSelectedSkill()
    {
        if (_skillList.SelectedItem is not AiSkill skill)
            return;

        using var form = new AiSkillEditForm(_config, skill);
        if (DialogPresenter.ShowModal(form, this) == DialogResult.OK)
            SaveAndRefresh();
    }

    private void DeleteSelectedSkill()
    {
        if (_skillList.SelectedItem is not AiSkill skill)
            return;

        if (DialogPresenter.ShowMessage(this, $"确定删除 \"{skill.Name}\" 吗？", "AI 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _config.Skills.Remove(skill);
        if (_config.DefaultSkillId == skill.Id)
            _config.DefaultSkillId = _config.Skills.FirstOrDefault()?.Id ?? string.Empty;
        SaveAndRefresh();
    }

    private void SetDefaultSkill()
    {
        if (_skillList.SelectedItem is not AiSkill skill)
            return;

        _config.DefaultSkillId = skill.Id;
        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        _configManager.Save();
        RefreshAllLists();
    }

    private void RefreshAllLists()
    {
        RefreshList(_providerList, _config.Providers, provider => provider.Id == _config.CurrentProviderId, provider =>
            $"{provider.Name}  |  {provider.DefaultModel}{(provider.Id == _config.CurrentProviderId ? "  [默认]" : "")}");

        RefreshList(_promptList, _config.PromptPresets, prompt => prompt.Id == _config.DefaultPromptId, prompt =>
            $"{prompt.Name}{(prompt.Id == _config.DefaultPromptId ? "  [默认]" : "")}");

        RefreshList(_skillList, _config.Skills, skill => skill.Id == _config.DefaultSkillId, skill =>
            $"{skill.Name}  |  {skill.Steps.Count} 步{(skill.Id == _config.DefaultSkillId ? "  [默认]" : "")}");
    }

    private static void RefreshList<T>(ListBox listBox, List<T> items, Func<T, bool> shouldSelect, Func<T, string> display)
    {
        listBox.Format -= OnListFormat;
        listBox.Tag = display;
        listBox.DisplayMember = string.Empty;
        listBox.FormattingEnabled = true;
        listBox.Format += OnListFormat;
        listBox.Items.Clear();
        foreach (var item in items)
        {
            if (item != null)
                listBox.Items.Add(item);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (shouldSelect(items[i]))
            {
                listBox.SelectedIndex = i;
                break;
            }
        }
    }

    private static void OnListFormat(object? sender, ListControlConvertEventArgs e)
    {
        if (sender is ListBox { Tag: Delegate formatter })
            e.Value = formatter.DynamicInvoke(e.ListItem) as string ?? e.ListItem?.ToString();
    }

    private Button CreateButton(string text, EventHandler click)
    {
        var button = new RoundedButton { Text = text, Margin = new Padding(0, 0, 8, 0) };
        ButtonStyler.ApplySecondary(button);
        button.Size = UiScaleHelper.GetButtonSize(this, text, button.Font, 106, 34, horizontalLogicalPadding: 14);
        button.Click += click;
        return button;
    }

    private static ListBox CreateListBox() => new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        Margin = new Padding(0)
    };

    private static FlowLayoutPanel CreateActionPanel() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Height = 48,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(0, 8, 0, 0),
        Margin = new Padding(0)
    };

    private static Control CreateTabLayout(Control list, Control actions)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        return layout;
    }
}
