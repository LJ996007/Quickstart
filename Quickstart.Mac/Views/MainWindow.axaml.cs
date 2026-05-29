namespace Quickstart.Mac.Views;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Quickstart.Core;
using Quickstart.Models;

public partial class MainWindow : Window
{
    private enum TabKind { Folders, Files, Urls, Texts }

    private static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush InactiveBg = new SolidColorBrush(Color.Parse("#F0F0F0"));
    private static readonly IBrush ActiveFg = Brushes.White;
    private static readonly IBrush InactiveFg = new SolidColorBrush(Color.Parse("#505050"));

    private readonly ConfigManager _config = new();
    private TabKind _activeTab = TabKind.Folders;
    private string _activeGroup = EntryQueries.AllGroupsLabel;

    private readonly List<(TabKind Kind, Border Border, TextBlock Text)> _tabs = [];
    private readonly List<(string Group, Border Border, TextBlock Text)> _groups = [];

    public MainWindow()
    {
        InitializeComponent();
        _config.Load();

        BuildTabs();
        SearchBox.TextChanged += (_, _) => RefreshList();
        EntryList.DoubleTapped += (_, _) => OpenSelected();
        EntryList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                await EditSelectedAsync();
                e.Handled = true;
            }
        };

        var editItem = new MenuItem { Header = "编辑" };
        editItem.Click += async (_, _) => await EditSelectedAsync();
        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => DeleteSelected();
        EntryList.ContextMenu = new ContextMenu { ItemsSource = new[] { editItem, deleteItem } };

        AddButton.Click += async (_, _) => await AddEntryAsync();
        SettingsButton.Click += (_, _) => { }; // 设置窗口：后续阶段

        SwitchTab(TabKind.Folders);
    }

    private Dictionary<EntryType, List<string>> BuildGroupSuggestions()
    {
        var dict = new Dictionary<EntryType, List<string>>();
        foreach (var type in new[] { EntryType.Folder, EntryType.File, EntryType.Url, EntryType.Text })
            dict[type] = EntryQueries.OrderedGroupNames(EntryQueries.ByType(_config.Config.Entries, type));
        return dict;
    }

    private async System.Threading.Tasks.Task AddEntryAsync()
    {
        var entry = new QuickEntry { Type = ActiveType };
        var ok = await new EntryEditWindow(entry, BuildGroupSuggestions()).ShowDialog<bool>(this);
        if (!ok) return;

        if (_config.AddEntry(entry))
        {
            _activeTab = entry.Type switch
            {
                EntryType.File => TabKind.Files,
                EntryType.Url => TabKind.Urls,
                EntryType.Text => TabKind.Texts,
                _ => TabKind.Folders
            };
            SwitchTab(_activeTab);
            EntryList.SelectedItem = (EntryList.ItemsSource as IEnumerable<QuickEntry>)?.FirstOrDefault(e => e.Id == entry.Id);
        }
    }

    private async System.Threading.Tasks.Task EditSelectedAsync()
    {
        if (EntryList.SelectedItem is not QuickEntry entry)
            return;

        var ok = await new EntryEditWindow(entry, BuildGroupSuggestions()).ShowDialog<bool>(this);
        if (!ok) return;

        _config.UpdateEntry(entry);
        RebuildGroups();
        RefreshList();
        EntryList.SelectedItem = (EntryList.ItemsSource as IEnumerable<QuickEntry>)?.FirstOrDefault(e => e.Id == entry.Id);
    }

    private void DeleteSelected()
    {
        if (EntryList.SelectedItem is not QuickEntry entry)
            return;

        _config.RemoveEntry(entry.Id);
        RebuildGroups();
        RefreshList();
    }

    private static string VerticalText(string text)
        => string.Join("\n", text.Select(c => c.ToString()));

    private void BuildTabs()
    {
        (TabKind kind, string label)[] defs =
        [
            (TabKind.Folders, "文件夹"), (TabKind.Files, "文件"),
            (TabKind.Urls, "网页"), (TabKind.Texts, "文本")
        ];

        foreach (var (kind, label) in defs)
        {
            var text = new TextBlock
            {
                Text = VerticalText(label),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            var border = new Border
            {
                Child = text,
                Padding = new Thickness(6, 10),
                MinWidth = 34,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            var captured = kind;
            border.PointerPressed += (_, _) => SwitchTab(captured);
            _tabs.Add((kind, border, text));
            TabsPanel.Children.Add(border);
        }
    }

    private void SwitchTab(TabKind kind)
    {
        _activeTab = kind;
        _activeGroup = EntryQueries.AllGroupsLabel;

        foreach (var (tabKind, border, text) in _tabs)
        {
            var active = tabKind == kind;
            border.Background = active ? ActiveBg : InactiveBg;
            text.Foreground = active ? ActiveFg : InactiveFg;
        }

        SearchBox.Watermark = kind switch
        {
            TabKind.Files => "搜索要打开的文件... (拼音首字母也可)",
            TabKind.Urls => "搜索网页...",
            TabKind.Texts => "搜索文本...",
            _ => "搜索文件夹... (拼音首字母也可)"
        };

        RebuildGroups();
        RefreshList();
    }

    private EntryType ActiveType => _activeTab switch
    {
        TabKind.Files => EntryType.File,
        TabKind.Urls => EntryType.Url,
        TabKind.Texts => EntryType.Text,
        _ => EntryType.Folder
    };

    private List<QuickEntry> TypeEntries() => EntryQueries.ByType(_config.Config.Entries, ActiveType);

    private void RebuildGroups()
    {
        _groups.Clear();
        GroupsPanel.Children.Clear();

        var names = new List<string> { EntryQueries.AllGroupsLabel };
        names.AddRange(EntryQueries.OrderedGroupNames(TypeEntries()));

        foreach (var name in names)
        {
            var text = new TextBlock
            {
                Text = VerticalText(name),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12
            };
            var border = new Border
            {
                Child = text,
                Padding = new Thickness(5, 8),
                MinWidth = 32,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            var captured = name;
            border.PointerPressed += (_, _) => SwitchGroup(captured);
            _groups.Add((name, border, text));
            GroupsPanel.Children.Add(border);
        }

        ApplyGroupStyles();
    }

    private void SwitchGroup(string group)
    {
        _activeGroup = string.IsNullOrWhiteSpace(group) ? EntryQueries.AllGroupsLabel : group;
        ApplyGroupStyles();
        RefreshList();
    }

    private void ApplyGroupStyles()
    {
        foreach (var (group, border, text) in _groups)
        {
            var active = string.Equals(group, _activeGroup, StringComparison.OrdinalIgnoreCase);
            border.Background = active ? ActiveBg : InactiveBg;
            text.Foreground = active ? ActiveFg : InactiveFg;
        }
    }

    private void RefreshList()
    {
        var items = EntryQueries.FilterAndSort(TypeEntries(), _activeGroup, SearchBox.Text);
        EntryList.ItemsSource = items;
        CountLabel.Text = $"{items.Count} 项";
    }

    private void OpenSelected()
    {
        if (EntryList.SelectedItem is not QuickEntry entry)
            return;

        try
        {
            if (entry.Type == EntryType.Text)
            {
                _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(entry.Path);
                return;
            }

            // 跨平台打开（Windows/macOS 均经系统默认处理；macOS 走 open）
            Process.Start(new ProcessStartInfo { FileName = entry.Path, UseShellExecute = true });
        }
        catch
        {
            // 打开失败忽略；TC/Dopus/Finder 等平台特定打开方式后续阶段补充
        }
    }
}
