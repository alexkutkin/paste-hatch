using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PasteHatch.Ui;
using Windows.ApplicationModel.DataTransfer;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private readonly List<string> _internalDragPaths = [];

    private void OnTreeDragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs e)
    {
        _internalDragPaths.Clear();
        if (_selectedNode is { IsRoot: false } selected)
        {
            _internalDragPaths.Add(selected.FullPath);
        }

        if (_internalDragPaths.Count > 0)
        {
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void OnTreeDragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        _internalDragPaths.Clear();
    }

    private void OnTreeEntryDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not TreeViewNode node ||
            node.Content is not EntryNode { IsFolder: true } folder)
        {
            return;
        }

        e.AcceptedOperation = _internalDragPaths.Count > 0
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _internalDragPaths.Count > 0
            ? Localization.Text.Format("Drag_MoveToFolder", folder.Name)
            : Localization.Text.Format("Drag_CopyToFolder", folder.Name);
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void OnTreeEntryDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not TreeViewNode node ||
            node.Content is not EntryNode { IsFolder: true } folder)
        {
            return;
        }

        e.Handled = true;
        await DropIntoFolderAsync(e, folder.RelativePath);
    }

    private void OnRootDragEnter(object sender, DragEventArgs e) => UpdateDropHighlight(true);

    private void OnRootDragLeave(object sender, DragEventArgs e) => UpdateDropHighlight(false);

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (_internalDragPaths.Count > 0)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = Localization.Text.Get("Drag_MoveToCurrent");
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true;
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Localization.Text.Get("Drag_CopyToBasket");
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true;
            UpdateDropHighlight(true);
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        UpdateDropHighlight(false);
        await DropIntoFolderAsync(e, CurrentFolder);
    }

    private async Task DropIntoFolderAsync(DragEventArgs e, string targetFolder)
    {
        if (_internalDragPaths.Count > 0)
        {
            e.Handled = true;
            var paths = new List<string>(_internalDragPaths);
            _internalDragPaths.Clear();
            _workspace.MoveFiles(paths, targetFolder);
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        var targetBasketUid = _workspace.ActiveTab?.Uid;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (_workspace.ActiveTab?.Uid != targetBasketUid)
            {
                return;
            }

            var paths = items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            if (paths.Count > 0)
            {
                _workspace.AddFiles(paths, targetFolder);
            }
        }
        catch (Exception ex)
        {
            ShowDialogError(Localization.Text.Format("Error_DropFiles", ex.Message));
        }
    }

    private void UpdateDropHighlight(bool active)
    {
        FileTree.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        FileTree.BorderBrush = active
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : null;
    }
}
