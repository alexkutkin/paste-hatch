using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PasteHatch.Ui;
using Windows.System;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<BasketRow> _basketRows = new();
    private string? _basketSignature;
    private string? _editingBasketUid;
    private bool _isBasketDrag;

    private void RefreshBaskets()
    {
        var signature = string.Join(
            '\u001f',
            _workspace.Tabs.Select(tab => tab.Uid + '\u001e' + tab.DisplayName)) +
            '\u001f' + _workspace.CanCloseTab +
            '\u001f' + _workspace.ActiveTab?.Uid;

        if (string.Equals(signature, _basketSignature, StringComparison.Ordinal))
        {
            SelectActiveBasketRow();
            return;
        }

        _basketSignature = signature;
        _basketRows.Clear();

        foreach (var tab in _workspace.Tabs)
        {
            _basketRows.Add(new BasketRow(
                tab,
                _workspace.CanCloseTab));
        }

        SelectActiveBasketRow();
    }

    private void SelectActiveBasketRow()
    {
        var activeUid = _workspace.ActiveTab?.Uid;
        var row = _basketRows.FirstOrDefault(candidate =>
            string.Equals(candidate.Uid, activeUid, StringComparison.Ordinal));

        if (row is not null && !ReferenceEquals(BasketsList.SelectedItem, row))
        {
            BasketsList.SelectedItem = row;
        }
    }

    private void UpdateAddButtonState()
    {
        var canCreate = _workspace.CanCreateTab;
        var tooltip = Localization.Text.Get(canCreate ? "ToolTip_CreateBasket" : "Status_BasketLimit");
        foreach (var button in new[] { AddBasketButton, AddBasketFooterButton })
        {
            button.IsEnabled = canCreate;
            ToolTipService.SetToolTip(button, tooltip);
        }
    }

    private void OnAddBasketClick(object sender, RoutedEventArgs e) => _workspace.CreateTab();

    private void OnBasketSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _isBasketDrag)
        {
            return;
        }

        if (BasketsList.SelectedItem is BasketRow row &&
            !string.Equals(_workspace.ActiveTab?.Uid, row.Uid, StringComparison.Ordinal))
        {
            _workspace.ActivateTab(row.Uid);
        }
    }

    private void OnBasketRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is BasketRow row)
        {
            e.Handled = true;
            BeginBasketRename(row);
        }
    }

    private void BeginBasketRename(BasketRow row)
    {
        if (_editingBasketUid is not null)
        {
            return;
        }

        _editingBasketUid = row.Uid;
        row.BeginEdit();

        DispatcherQueue.TryEnqueue(() =>
        {
            var container = BasketsList.ContainerFromItem(row) as ListViewItem;
            var editor = container is null ? null : FindDescendant<TextBox>(container);
            if (editor is not null)
            {
                editor.Focus(FocusState.Programmatic);
                editor.SelectAll();
            }
        });
    }

    private void OnBasketRenameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is BasketRow row)
        {
            row.EditText = textBox.Text;
        }
    }

    private void OnBasketRenameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not BasketRow row)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitBasketRename(row);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelBasketRename(row);
        }
    }

    private void OnBasketRenameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is BasketRow row)
        {
            CommitBasketRename(row);
        }
    }

    private void CommitBasketRename(BasketRow row)
    {
        if (!string.Equals(_editingBasketUid, row.Uid, StringComparison.Ordinal))
        {
            return;
        }

        _editingBasketUid = null;
        row.EndEdit();
        _workspace.RenameTab(row.Uid, row.EditText);
        RefreshAll();
    }

    private void CancelBasketRename(BasketRow row)
    {
        _editingBasketUid = null;
        row.EndEdit();
    }

    private void OnCloseBasketClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is BasketRow row)
        {
            _workspace.CloseTab(row.Uid);
        }
    }

    private void OnBasketsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete || IsTextInputFocused() || e.OriginalSource is TextBox)
        {
            return;
        }

        var row = FindBasketRowFromSource(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        e.Handled = true;
        _workspace.CloseTab(row.Uid);
    }

    private void OnBasketRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var row = FindBasketRowFromSource(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        e.Handled = true;
        BasketsList.SelectedItem = row;

        var flyout = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_OpenInExplorerBasket") };
        open.Click += (_, _) =>
        {
            if (_workspace.ActivateTab(row.Uid))
            {
                _workspace.OpenActiveBasketFolder(_treeNavigation.FolderFor(row.Uid));
            }
        };

        var rename = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_Rename") };
        rename.Click += (_, _) => BeginBasketRename(row);

        var close = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_CloseBasket") };
        close.Click += (_, _) => _workspace.CloseTab(row.Uid);

        flyout.Items.Add(open);
        flyout.Items.Add(rename);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(close);
        flyout.ShowAt(BasketsList, new FlyoutShowOptions { Position = e.GetPosition(BasketsList) });
    }

    private void OnBasketDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _isBasketDrag = true;
    }

    private void OnBasketDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _isBasketDrag = false;
        if (_isUpdatingUi)
        {
            return;
        }

        var order = _basketRows.Select(row => row.Uid).ToList();
        if (order.Count == _workspace.Tabs.Count)
        {
            _workspace.ReorderTabs(order);
        }

        SelectActiveBasketRow();
    }

    private void MoveActiveBasket(int offset)
    {
        var uid = _workspace.ActiveTab?.Uid;
        if (uid is not null)
        {
            _workspace.MoveTab(uid, offset);
        }
    }

    private void OnBasketNumberAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var index = (int)sender.Key - (int)VirtualKey.Number1;
        if (index == 8)
        {
            index = _basketRows.Count - 1;
        }

        if (index >= 0 && index < _basketRows.Count)
        {
            BasketsList.SelectedItem = _basketRows[index];
        }
    }

    private void OnNewBasketAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _workspace.CreateTab();
    }

    private void OnCloseBasketAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var uid = _workspace.ActiveTab?.Uid;
        if (uid is not null)
        {
            _workspace.CloseTab(uid);
        }
    }

    private void OnMoveBasketUpAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        MoveActiveBasket(-1);
    }

    private void OnMoveBasketDownAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        MoveActiveBasket(1);
    }
}
