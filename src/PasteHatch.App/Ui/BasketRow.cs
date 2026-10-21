using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using PasteHatch.Core;

namespace PasteHatch.Ui;

/// <summary>One row of the vertical basket rail.</summary>
public sealed class BasketRow : INotifyPropertyChanged
{
    private bool _isEditing;
    private string _editText;
    private bool _isActive;
    private bool _canClose;

    public BasketRow(BasketTab tab, bool isActive, bool canClose)
    {
        Tab = tab;
        _editText = tab.DisplayName;
        _isActive = isActive;
        _canClose = canClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BasketTab Tab { get; }

    public string Uid => Tab.Uid;

    public string Name => Tab.DisplayName;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public bool CanClose
    {
        get => _canClose;
        set
        {
            if (_canClose == value)
            {
                return;
            }

            _canClose = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CloseVisibility));
        }
    }

    public Visibility CloseVisibility => _canClose ? Visibility.Visible : Visibility.Collapsed;

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
