namespace Quickstart.Mac.Views;

using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Quickstart.Core;
using Quickstart.Models;
using Quickstart.Utils;

public partial class MainWindow : Window
{
    private readonly ConfigManager _config = new();
    private List<QuickEntry> _folders = [];

    public MainWindow()
    {
        InitializeComponent();

        _config.Load();
        _folders = _config.Config.Entries
            .Where(e => e.Type == EntryType.Folder)
            .OrderBy(e => e.SortOrder)
            .ToList();

        SearchBox.TextChanged += (_, _) => RefreshList();
        RefreshList();
    }

    private void RefreshList()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var items = string.IsNullOrEmpty(query)
            ? _folders
            : _folders
                .Where(e => PinyinHelper.MatchesPinyin(e.Name, query) || PinyinHelper.MatchesPinyin(e.Path, query))
                .ToList();

        EntryList.ItemsSource = items;
    }
}
