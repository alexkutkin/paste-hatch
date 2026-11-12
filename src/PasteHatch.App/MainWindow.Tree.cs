using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PasteHatch.Core;
using PasteHatch.Platform;
using PasteHatch.Ui;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private EntryNode? _selectedNode;
    private readonly BasketTreeNavigation _treeNavigation;
    private (string Uid, string Path)? _pendingBasketFocus;

    private void RefreshTree()
    {
        var view = _treeNavigation.Reconcile(_selectedNode?.Entry, CollectExpandedPaths());
        FileTree.RootNodes.Clear();

        var tab = _workspace.ActiveTab;
        if (tab is null)
        {
            _pendingBasketFocus = null;
            _selectedNode = null;
            EmptyStatePanel.Visibility = Visibility.Visible;
            ShowPreview(null);
            return;
        }

        var rootNode = new TreeViewNode
        {
            Content = new EntryNode(tab.DisplayName, string.Empty, tab.DirectoryPath, isFolder: true),
            IsExpanded = true,
            HasUnrealizedChildren = _workspace.HasEntries(string.Empty),
        };

        FileTree.RootNodes.Add(rootNode);
        rootNode.IsExpanded = true;
        FillTreeNode(rootNode);
        rootNode.IsExpanded = true;

        foreach (var path in view.ExpandedPaths)
        {
            SelectTreePath(rootNode, path, select: false);
        }

        var selected = SelectTreePath(rootNode, view.Path, select: true) ?? rootNode;

        FileTree.SelectedNode = selected;
        _selectedNode = selected.Content as EntryNode;
        UpdateSelection();
        EmptyStatePanel.Visibility = rootNode.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (view.IsBasketSwitch)
        {
            _pendingBasketFocus = _selectedNode is { IsRoot: false } ? (tab.Uid, view.Path) : null;
        }
        QueueBasketSelectionFocus();
    }

    private void QueueBasketSelectionFocus()
    {
        if (_pendingBasketFocus is not { } request) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_isExiting || _pendingBasketFocus != request || _workspace.ActiveTab?.Uid != request.Uid) return;
            if (FileTree.SelectedNode is not { Content: EntryNode entry } selected || entry.RelativePath != request.Path ||
                !NativeWindow.IsForegroundWindow(GetWindowHandle()))
            {
                _pendingBasketFocus = null;
                return;
            }

            FileTree.UpdateLayout();
            FindDescendant<ScrollViewer>(FileTree)?.ChangeView(0, 0, null, disableAnimation: true);
            FileTree.UpdateLayout();
            if (FileTree.ContainerFromNode(selected) is Control container)
            {
                container.StartBringIntoView();
                if (container.Focus(FocusState.Programmatic)) _pendingBasketFocus = null;
            }
        });
    }

    private void FillTreeNode(TreeViewNode node)
    {
        if (node.Content is not EntryNode folder || !folder.IsFolder)
        {
            return;
        }

        node.Children.Clear();
        foreach (var entry in _workspace.GetEntries(folder.RelativePath))
        {
            node.Children.Add(new TreeViewNode
            {
                Content = new EntryNode(entry),
                HasUnrealizedChildren = entry.IsFolder && _workspace.HasEntries(entry.RelativePath),
            });
        }

        node.HasUnrealizedChildren = false;
    }

    private TreeViewNode? SelectTreePath(TreeViewNode root, string? relativePath, bool select)
    {
        var normalized = relativePath ?? string.Empty;
        if (normalized.Length == 0)
        {
            if (select)
            {
                FileTree.SelectedNode = root;
            }

            return root;
        }

        var current = root;
        var prefix = string.Empty;
        foreach (var part in normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            prefix = prefix.Length == 0 ? part : Path.Combine(prefix, part);
            if (current.HasUnrealizedChildren)
            {
                FillTreeNode(current);
            }

            var next = current.Children
                .OfType<TreeViewNode>()
                .FirstOrDefault(node => node.Content is EntryNode entry &&
                    string.Equals(entry.RelativePath, prefix, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            if (next.Content is EntryNode { IsFolder: true })
            {
                next.IsExpanded = true;
            }

            current = next;
        }

        if (select)
        {
            FileTree.SelectedNode = current;
        }

        return current;
    }

    private HashSet<string> CollectExpandedPaths()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in FileTree.RootNodes)
        {
            CollectExpandedPaths(root, expanded);
        }

        return expanded;
    }

    private static void CollectExpandedPaths(TreeViewNode node, HashSet<string> expanded)
    {
        if (node.Content is EntryNode { IsFolder: true } entry && node.IsExpanded)
        {
            expanded.Add(entry.RelativePath);
        }

        foreach (var child in node.Children)
        {
            CollectExpandedPaths(child, expanded);
        }
    }

    private void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.HasUnrealizedChildren)
        {
            FillTreeNode(args.Node);
        }
    }

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _pendingBasketFocus = null;
        _selectedNode = FileTree.SelectedNode?.Content as EntryNode;
        if (FileTree.SelectedNode is { HasUnrealizedChildren: true } selectedNode)
        {
            // Prime the first level so expanding a folder works even when the expand comes from
            // UI Automation or another programmatic source.
            FillTreeNode(selectedNode);
        }

        UpdateSelection();
    }

    private string CurrentFolder => _treeNavigation.CurrentFolder;

    private void UpdateSelection()
    {
        _treeNavigation.Select(_selectedNode?.Entry);
        var folder = CurrentFolder;
        BasketPathText.Text = _treeNavigation.CurrentFolderPath;
        ToolTipService.SetToolTip(BasketPathText, _treeNavigation.CurrentFolderPath);
        UpButton.IsEnabled = folder.Length > 0;
        FileCountText.Text = Localization.Text.Plural("Count_Items", CurrentFolderItemCount());

        RefreshBreadcrumb();
        ShowPreview(_selectedNode);
    }

    private int CurrentFolderItemCount()
    {
        var node = FileTree.SelectedNode;
        if (node is null)
        {
            return 0;
        }

        var folderNode = node.Content is EntryNode { IsFolder: true } ? node : node.Parent;
        return folderNode?.Children.Count ?? 0;
    }

    private void RefreshBreadcrumb()
    {
        FolderBreadcrumb.ItemsSource = _treeNavigation.Breadcrumbs();
    }

    private void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderCrumb crumb && FileTree.RootNodes.Count > 0)
        {
            _selectedNode = null;
            SelectTreePath(FileTree.RootNodes[0], crumb.RelativePath, select: true);
        }
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => SelectParentFolder();

    private void OnUpAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        SelectParentFolder();
    }

    private void SelectParentFolder()
    {
        var folder = CurrentFolder;
        if (folder.Length == 0 || FileTree.RootNodes.Count == 0)
        {
            return;
        }

        _selectedNode = null;
        SelectTreePath(FileTree.RootNodes[0], _treeNavigation.ParentFolder, select: true);
    }

    private void OnEntryNameDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } node } &&
            !entry.IsFolder)
        {
            e.Handled = true;
            FileTree.SelectedNode = node;
            BeginNodeRename(entry);
        }
    }

    private void OnTreeEntryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (sender is not FrameworkElement element ||
            element.DataContext is not TreeViewNode node ||
            node.Content is not EntryNode entry)
        {
            return;
        }

        e.Handled = true;
        if (entry.IsEditing)
        {
            return;
        }

        OpenNode(entry, node);
    }

    private void OpenNode(EntryNode entry, TreeViewNode? treeNode)
    {
        if (entry.IsFolder && treeNode is not null)
        {
            treeNode.IsExpanded = !treeNode.IsExpanded;
        }
        else
        {
            _workspace.OpenEntry(entry.Entry);
        }
    }

    private void OnEntryPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            entry.SetActionsPointerOver(true);
    }

    private void OnEntryPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            entry.SetActionsPointerOver(false);
    }

    private void OnEntryActionGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            entry.SetActionsFocused(true);
    }

    private void OnEntryActionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            entry.SetActionsFocused(false);
    }

    private void OnOpenEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            _workspace.OpenEntry(entry.Entry);
    }

    private void OnCopyEntryPathClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            _workspace.CopyEntryPath(entry.Entry);
    }

    private void OnCopyEntryContentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeViewNode { Content: EntryNode entry } })
            _workspace.CopyEntryContent(entry.Entry);
    }

    private void OnEntryActionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;

    private void OnTreeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || _selectedNode is null)
        {
            return;
        }

        var shiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.F2:
                if (_selectedNode.IsRoot)
                {
                    break;
                }

                e.Handled = true;
                BeginNodeRename(_selectedNode);
                break;

            case VirtualKey.Delete:
                if (_selectedNode.IsRoot)
                {
                    break;
                }

                e.Handled = true;
                DeleteNode(_selectedNode);
                break;

            case VirtualKey.Enter:
                e.Handled = true;
                OpenNode(_selectedNode, FileTree.SelectedNode);

                break;

            case VirtualKey.Application:
            case VirtualKey.F10 when shiftDown:
                e.Handled = true;
                ShowNodeContextMenu(_selectedNode, GetSelectedNodePosition());
                break;
        }
    }

    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = FindTreeViewNodeFromSource(e.OriginalSource as DependencyObject);
        if (node?.Content is not EntryNode entry)
        {
            return;
        }

        e.Handled = true;
        FileTree.SelectedNode = node;
        _selectedNode = entry;
        ShowNodeContextMenu(entry, e.GetPosition(FileTree));
    }

    private void ShowNodeContextMenu(EntryNode node, Point position)
    {
        var flyout = new MenuFlyout();

        var open = new MenuFlyoutItem
        {
            Text = Localization.Text.Get(node.IsFolder ? "Menu_ExpandCollapse" : "Menu_Open"),
        };
        open.Click += (_, _) => OpenNode(node, FileTree.SelectedNode);

        var explore = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_OpenInExplorer") };
        explore.Click += (_, _) =>
        {
            if (node.IsFolder)
            {
                _workspace.OpenEntry(node.Entry);
            }
            else
            {
                _workspace.OpenActiveBasketFolder(CurrentFolder);
            }
        };

        var copyPath = new MenuFlyoutItem { Text = Localization.Text.Get("Command_CopyPath") };
        copyPath.Click += (_, _) => _workspace.CopyEntryPath(node.Entry);

        flyout.Items.Add(open);
        flyout.Items.Add(explore);
        flyout.Items.Add(copyPath);

        if (!node.IsFolder)
        {
            var copyContent = new MenuFlyoutItem { Text = Localization.Text.Get("Command_CopyContent") };
            copyContent.Click += (_, _) => _workspace.CopyEntryContent(node.Entry);
            flyout.Items.Add(copyContent);
        }

        if (!node.IsRoot)
        {
            var rename = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_Rename") };
            rename.Click += (_, _) => BeginNodeRename(node);

            var cut = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_Cut") };
            cut.Click += (_, _) => CopyOrCutNode(node, move: true);

            var copy = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_Copy") };
            copy.Click += (_, _) => CopyOrCutNode(node, move: false);

            var delete = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_Delete") };
            delete.Click += (_, _) => DeleteNode(node);

            flyout.Items.Add(rename);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(cut);
            flyout.Items.Add(copy);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(delete);
        }

        flyout.ShowAt(FileTree, new FlyoutShowOptions { Position = position });
    }

    private Point GetSelectedNodePosition()
    {
        if (FileTree.SelectedNode is { } node &&
            FileTree.ContainerFromNode(node) is TreeViewItem container)
        {
            try
            {
                var topLeft = container.TransformToVisual(FileTree).TransformPoint(new Point(0, 0));
                return new Point(topLeft.X + 24, topLeft.Y + Math.Max(8, container.ActualHeight / 2));
            }
            catch (Exception)
            {
                // Fall back to a corner of the tree.
            }
        }

        return new Point(16, 16);
    }

    private void DeleteNode(EntryNode node)
    {
        if (node.IsRoot)
        {
            return;
        }

        _workspace.DeleteEntry(node.Entry);
    }

    private void BeginNodeRename(EntryNode node)
    {
        if (node.IsRoot || FileTree.SelectedNode is not { } treeNode ||
            !ReferenceEquals(treeNode.Content, node))
        {
            return;
        }

        var basketUid = _workspace.ActiveTab?.Uid;
        var relativePath = node.RelativePath;
        node.BeginEdit();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isExiting || !node.IsEditing || _workspace.ActiveTab?.Uid != basketUid ||
                !ReferenceEquals(FileTree.SelectedNode, treeNode) ||
                treeNode.Content is not EntryNode selected || selected.RelativePath != relativePath ||
                !ReferenceEquals(selected, node))
            {
                return;
            }

            var container = FileTree.ContainerFromNode(treeNode) as TreeViewItem;
            var editor = container is null ? null : FindDescendant<TextBox>(container);
            if (editor is not null)
            {
                editor.Focus(FocusState.Programmatic);
                editor.SelectAll();
            }
        });
    }

    private void OnNodeRenameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is TreeViewNode node &&
            node.Content is EntryNode entry)
        {
            entry.EditText = textBox.Text;
        }
    }

    private void OnNodeRenameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not TreeViewNode node ||
            node.Content is not EntryNode entry)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            entry.EditText = textBox.Text;
            CommitNodeRename(entry);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            entry.EndEdit();
        }
    }

    private void OnNodeRenameSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TreeViewNode node &&
            node.Content is EntryNode entry)
        {
            CommitNodeRename(entry);
        }
    }

    private void OnNodeRenameCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TreeViewNode node &&
            node.Content is EntryNode entry)
        {
            entry.EndEdit();
        }
    }

    private void CommitNodeRename(EntryNode node)
    {
        if (!node.IsEditing) return;
        var name = node.EditText;
        node.EndEdit();
        _workspace.RenameEntry(node.Entry, name);
    }
}
