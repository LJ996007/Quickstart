namespace Quickstart.Mac.Views;

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Quickstart.Models;

public partial class EntryEditWindow : Window
{
    private readonly QuickEntry _entry = new();
    private readonly IReadOnlyDictionary<EntryType, List<string>> _groupSuggestions =
        new Dictionary<EntryType, List<string>>();

    // 设计期需要的无参构造
    public EntryEditWindow() => InitializeComponent();

    public EntryEditWindow(QuickEntry entry, IReadOnlyDictionary<EntryType, List<string>> groupSuggestions)
    {
        InitializeComponent();
        _entry = entry;
        _groupSuggestions = groupSuggestions;

        Title = string.IsNullOrEmpty(entry.Name) ? "添加条目" : "编辑条目";
        TypeBox.ItemsSource = new[] { "文件夹", "文件", "网页", "文本" };
        TypeBox.SelectedIndex = entry.Type switch
        {
            EntryType.File => 1,
            EntryType.Url => 2,
            EntryType.Text => 3,
            _ => 0
        };
        NameBox.Text = entry.Name;
        PathBox.Text = entry.Path;
        GroupBox.Text = entry.Group;

        AdjustForType();
        UpdateGroupSuggestions();

        TypeBox.SelectionChanged += (_, _) =>
        {
            AdjustForType();
            UpdateGroupSuggestions();
        };
        BrowseButton.Click += OnBrowse;
        OkButton.Click += OnOk;
        CancelButton.Click += (_, _) => Close(false);
    }

    private EntryType SelectedType => TypeBox.SelectedIndex switch
    {
        1 => EntryType.File,
        2 => EntryType.Url,
        3 => EntryType.Text,
        _ => EntryType.Folder
    };

    private void AdjustForType()
    {
        var type = SelectedType;
        PathLabel.Text = type switch
        {
            EntryType.Url => "网址:",
            EntryType.Text => "内容:",
            _ => "路径:"
        };
        BrowseButton.IsVisible = type is EntryType.Folder or EntryType.File;
        PathBox.AcceptsReturn = type == EntryType.Text;
    }

    private void UpdateGroupSuggestions()
    {
        var current = GroupBox.Text;
        GroupBox.ItemsSource = _groupSuggestions.TryGetValue(SelectedType, out var groups)
            ? groups
            : [];
        GroupBox.Text = current;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (SelectedType == EntryType.Folder)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0)
                PathBox.Text = folders[0].Path.LocalPath;
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            if (files.Count > 0)
                PathBox.Text = files[0].Path.LocalPath;
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var path = (PathBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            PathBox.Focus();
            return;
        }

        var type = SelectedType;
        _entry.Path = path;
        _entry.Name = string.IsNullOrWhiteSpace(NameBox.Text)
            ? (type is EntryType.Url or EntryType.Text ? path[..Math.Min(path.Length, 30)] : Path.GetFileName(path))
            : NameBox.Text!.Trim();
        _entry.Type = type;
        _entry.Group = (GroupBox.Text ?? string.Empty).Trim();

        Close(true);
    }
}
