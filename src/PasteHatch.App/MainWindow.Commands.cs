using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PasteHatch.Ui;
using Windows.System;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private void OnPasteClick(object sender, RoutedEventArgs e) => PasteIntoCurrentFolder();

    private void OnPasteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        PasteIntoCurrentFolder();
    }

    private void PasteIntoCurrentFolder()
    {
        _workspace.AddFromClipboard(CurrentFolder);
    }

    private void OnCopyAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused() || _selectedNode is null)
        {
            return;
        }

        args.Handled = true;
        CopyOrCutNode(_selectedNode, move: false);
    }

    private void OnCutAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused() || _selectedNode is null)
        {
            return;
        }

        args.Handled = true;
        CopyOrCutNode(_selectedNode, move: true);
    }

    private void CopyOrCutNode(EntryNode node, bool move)
    {
        _workspace.CopyEntryToClipboard(node.Entry, move);
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox;
    }

    private void OnSaveNoteClick(object sender, RoutedEventArgs e) => SaveQuickNote();

    private void OnQuickNoteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            SaveQuickNote();
        }
    }

    private void SaveQuickNote()
    {
        var text = QuickNoteTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_workspace.AddQuickNote(text, CurrentFolder) is not null)
        {
            QuickNoteTextBox.Text = string.Empty;
        }
    }

    private void OnNewFolderClick(object sender, RoutedEventArgs e) => CreateNewFolder();

    private void OnNewFolderAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        CreateNewFolder();
    }

    private void CreateNewFolder()
    {
        var basket = _workspace.ActiveTab;
        if (basket is null) return;
        var folder = _workspace.CreateFolder(
            Localization.Text.Get("Default_NewFolderName"), CurrentFolder, ensureUnique: true);
        if (folder is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isExiting || _workspace.ActiveTab?.Uid != basket.Uid || FileTree.RootNodes.Count == 0 ||
                FileTree.RootNodes[0].Content is not EntryNode root ||
                !string.Equals(root.FullPath, basket.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var node = SelectTreePath(FileTree.RootNodes[0], folder.RelativePath, select: true);
            if (node?.Content is EntryNode entry &&
                string.Equals(entry.FullPath, folder.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                BeginNodeRename(entry);
            }
        });
    }

    private void OnRefreshAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _liveRefresh.RequestRefresh();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        => _workspace.OpenActiveBasketFolder(CurrentFolder);

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
        => _workspace.CopyActiveBasketPath(CurrentFolder);
}
