using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using PasteHatch.Core;

namespace PasteHatch.Ui;

public sealed class BasketRow : INotifyPropertyChanged
{
    private bool _isEditing;
    private string _editText;

    public BasketRow(BasketTab tab, bool canClose)
    {
        Tab = tab;
        _editText = tab.DisplayName;
        CanClose = canClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BasketTab Tab { get; }

    public string Uid => Tab.Uid;

    public string Name => Tab.DisplayName;

    public bool CanClose { get; }

    public Visibility CloseVisibility => CanClose ? Visibility.Visible : Visibility.Collapsed;

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
        EditText = Name;
        IsEditing = true;
    }

    public void EndEdit() => IsEditing = false;

    public override string ToString() => Name;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
