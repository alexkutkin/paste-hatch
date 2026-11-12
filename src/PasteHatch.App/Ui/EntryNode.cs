using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using PasteHatch.Core;

namespace PasteHatch.Ui;

/// <summary>
/// Content of one file-tree node: a folder or a material. It carries display and inline-rename
/// state only; the file system remains the source of truth.
/// </summary>
public sealed class EntryNode : INotifyPropertyChanged
{
    private string? _editText;
    private bool _actionsPointerOver;
    private bool _actionsFocused;

    public EntryNode(BasketEntry entry)
    {
        Entry = entry;
    }

    public EntryNode(string name, string relativePath, string fullPath, bool isFolder, long size = 0, DateTimeOffset modified = default)
        : this(new BasketEntry(name, fullPath, relativePath, isFolder, MaterialKind.File)
        {
            Size = size,
            Modified = modified,
        })
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BasketEntry Entry { get; }

    public bool IsFolder => Entry.IsFolder;

    public bool IsRoot => RelativePath.Length == 0;

    public string Name => Entry.Name;

    public string FullPath => Entry.FullPath;

    public string RelativePath => Entry.RelativePath;

    public string Extension => IsFolder ? string.Empty : Path.GetExtension(Name);

    public string TypeLabel => Localization.Text.Get(IsFolder
        ? "Kind_Folder"
        : Entry.Kind switch
        {
            MaterialKind.Image => "Kind_Image",
            MaterialKind.Link => "Kind_Link",
            MaterialKind.Text => "Kind_Text",
            _ => "Kind_File",
        });

    public string SizeText => IsFolder ? string.Empty : FormatSize(Entry.Size);

    public string SecondaryText => IsFolder ? TypeLabel : $"{TypeLabel} · {SizeText}";

    public string Glyph => IsFolder
        ? "\uE8B7"
        : Entry.Kind switch
        {
            MaterialKind.Image => "\uE8B9",
            MaterialKind.Link => "\uE71B",
            MaterialKind.Text => "\uE8A5",
            _ => "\uE7C3",
        };

    public bool IsEditing => _editText is not null;

    public string EditText
    {
        get => _editText ?? (IsFolder ? Name : Path.GetFileNameWithoutExtension(Name));
        set
        {
            if (_editText is null || _editText == value)
            {
                return;
            }

            _editText = value;
            OnPropertyChanged();
        }
    }

    public Visibility NameVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentActionVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public double ActionsOpacity => _actionsPointerOver || _actionsFocused ? 1 : 0;

    public void SetActionsPointerOver(bool value)
    {
        _actionsPointerOver = value;
        OnPropertyChanged(nameof(ActionsOpacity));
    }

    public void SetActionsFocused(bool value)
    {
        _actionsFocused = value;
        OnPropertyChanged(nameof(ActionsOpacity));
    }

    public void BeginEdit()
    {
        _editText = IsFolder ? Name : Path.GetFileNameWithoutExtension(Name);
        NotifyEditState();
    }

    public void EndEdit()
    {
        if (_editText is null) return;
        _editText = null;
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(EditText));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(NameVisibility));
        OnPropertyChanged(nameof(EditVisibility));
    }

    public override string ToString() => Name;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 0 => string.Empty,
        < 1024 => Localization.Text.Format("Size_Bytes", bytes),
        < 1024 * 1024 => Localization.Text.Format("Size_KB", bytes / 1024d),
        < 1024L * 1024 * 1024 => Localization.Text.Format("Size_MB", bytes / (1024d * 1024)),
        _ => Localization.Text.Format("Size_GB", bytes / (1024d * 1024 * 1024)),
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
