using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using PasteHatch.Core;

namespace PasteHatch.Ui;

public sealed class BasketRow : INotifyPropertyChanged
{
    private string? _editText;

    public BasketRow(BasketTab tab, bool canClose)
    {
        Tab = tab;
        CanClose = canClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BasketTab Tab { get; }

    public string Uid => Tab.Uid;

    public string Name => Tab.DisplayName;

    public bool CanClose { get; }

    public Visibility CloseVisibility => CanClose ? Visibility.Visible : Visibility.Collapsed;

    public bool IsEditing => _editText is not null;

    public string EditText
    {
        get => _editText ?? Name;
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

    public void BeginEdit()
    {
        _editText = Name;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
