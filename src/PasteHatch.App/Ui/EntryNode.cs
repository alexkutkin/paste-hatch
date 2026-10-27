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
    private bool _isEditing;
    private string _editText;
    private bool _actionsPointerOver;
    private bool _actionsFocused;

    public EntryNode(BasketEntry entry)
    {
        Entry = entry;
        _editText = entry.IsFolder ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
        Material = entry.IsFolder
            ? null
            : new Material(entry.Name, entry.FullPath, entry.Kind)
            {
                RelativePath = entry.RelativePath,
                Size = entry.Size,
                Modified = entry.Modified,
            };
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

    public Material? Material { get; }

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

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameVisibility));
            OnPropertyChanged(nameof(EditVisibility));
        }
    }

    public string EditText
    {
        get => _editText;
        set
        {
            if (_editText == value)
            {
                return;
            }

            _editText = value;
            OnPropertyChanged();
        }
    }

    public Visibility NameVisibility => _isEditing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditVisibility => _isEditing ? Visibility.Visible : Visibility.Collapsed;

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
        EditText = IsFolder ? Name : Path.GetFileNameWithoutExtension(Name);
        IsEditing = true;
    }

    public void EndEdit() => IsEditing = false;

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
