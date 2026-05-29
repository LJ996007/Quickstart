namespace Quickstart.Mac;

using System.ComponentModel;
using Avalonia.Media.Imaging;
using Quickstart.Models;

/// <summary>列表项视图模型：包裹 QuickEntry，并支持图标异步加载后即时刷新。</summary>
public sealed class EntryItem(QuickEntry entry) : INotifyPropertyChanged
{
    public QuickEntry Entry { get; } = entry;

    public string Name => Entry.Name;
    public string Path => Entry.Path;

    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
