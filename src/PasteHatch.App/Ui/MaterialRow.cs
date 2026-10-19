using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using PasteHatch.Core;

namespace PasteHatch.Ui;

/// <summary>
/// One row of the basket contents list. It carries display state only; the file on disk is
/// still the single source of truth and every mutating action goes through BasketWorkspace.
/// </summary>
public sealed class MaterialRow : INotifyPropertyChanged
{
    private bool _isEditing;
    private string _editText;

    public MaterialRow(Material material)
    {
        Material = material;
        _editText = Path.GetFileNameWithoutExtension(material.FileName);
        Preview = MaterialPreview.Read(material.FullPath, material.FileName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Material Material { get; }

    public string FileName => Material.FileName;

    public string Name => Material.FileName;

    public string TypeLabel => Material.TypeLabel;

    /// <summary>
    /// First lines of a text or Markdown material, shown instead of the generic type label so a
    /// file with a time-based name still says what it contains. <c>null</c> for other materials.
    /// </summary>
    public string? Preview { get; }

    public Visibility PreviewVisibility => Preview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TypeLabelVisibility => Preview is null ? Visibility.Visible : Visibility.Collapsed;

    public string Extension => Path.GetExtension(Material.FileName);

    public string Glyph => Material.Kind switch
    {
        MaterialKind.Image => "\uE8B9", // Picture
        MaterialKind.Link => "\uE71B", // Link
        MaterialKind.Text => "\uE8A5", // Document
        _ => "\uE7C3", // Page
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

    public void BeginEdit()
    {
        EditText = Path.GetFileNameWithoutExtension(FileName);
        IsEditing = true;
    }

    public void EndEdit() => IsEditing = false;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
