using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PasteHatch.Core;
using PasteHatch.Platform;
using PasteHatch.Ui;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace PasteHatch;

/// <summary>
/// Three-column workspace: a vertical basket rail, one file tree and a preview pane. All
/// behaviour stays in <see cref="BasketWorkspace"/>; the window only uses default Fluent controls
/// and hides to the notification area on close.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidthDip = 1200;
    private const int DefaultHeightDip = 800;
    private const double RailWidth = 220;
    private const double PreviewWidth = 320;
    private const double SplitterWidth = 8;
    private const double MinRailWidth = 160;
    private const double MinTreeWidth = 160;
    private const double MinPreviewWidth = 200;
    private const int MaxTextPreviewCharacters = 64 * 1024;

    private readonly ConfirmationService _confirmations;
    private readonly ShellService _shell;
    private readonly BasketWorkspace _workspace;
    private readonly SettingsStore _settingsStore;
    private readonly AppWindow _appWindow;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _placementTimer;
    private readonly WorkspaceRefreshCoordinator _liveRefresh;
    private readonly ObservableCollection<BasketRow> _basketRows = new();
    private readonly List<string> _internalDragPaths = [];

    private AppSettings _settings;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private EntryNode? _selectedNode;
    private readonly BasketTreeNavigation _treeNavigation;
    private string? _basketSignature;
    private string? _editingBasketUid;
    private string? _startupError;
    private bool _startupErrorReported;
    private bool _isBasketDrag;
    private bool _isUpdatingUi;
    private bool _isExiting;
    private double _railWidth = RailWidth;
    private double _previewWidth = PreviewWidth;
    private int _previewSequence;
    private readonly bool _testOffscreen;
    private readonly bool _testNoTray;
    private IntPtr _windowHandle = IntPtr.Zero;

    public MainWindow()
    {
        InitializeComponent();
        BasketsList.ItemsSource = _basketRows;

        _testOffscreen = string.Equals(
            Environment.GetEnvironmentVariable("PASTEHATCH_TEST_OFFSCREEN"), "1", StringComparison.Ordinal);
        _testNoTray = string.Equals(
            Environment.GetEnvironmentVariable("PASTEHATCH_TEST_NO_TRAY"), "1", StringComparison.Ordinal);

        _appWindow = AppWindow;
        _settingsStore = SettingsStore.CreateDefault();
        _settings = _settingsStore.Load();

        var clipboard = new ClipboardReader();
        _shell = new ShellService();
        _confirmations = new ConfirmationService(GetWindowHandle);

        _workspace = new BasketWorkspace(
            PasteHatchPaths.BasketsRoot,
            TabStateStore.CreateDefault(),
            clipboard,
            _shell,
            _confirmations,
            formatter: new TabNameFormatter(() => Localization.Culture),
            text: Localization.Text);
        _treeNavigation = new BasketTreeNavigation(_workspace);
        _liveRefresh = new WorkspaceRefreshCoordinator(_workspace, new FolderWatcher(),
            action => DispatcherQueue.TryEnqueue(() => action()), RefreshAll, Localization.Text);

        ConfigureWindowShell();

        _placementTimer = DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(700);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += (_, _) => PersistWindowPlacement();

        RootGrid.Loaded += OnRootLoaded;
        RootGrid.ActualThemeChanged += (_, _) => ApplyTheme();
        _appWindow.Closing += OnWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;

        try
        {
            _workspace.Initialize();
            _previewWidth = _workspace.PreviewWidth ?? PreviewWidth;
        }
        catch (Exception ex)
        {
            _startupError = Localization.Text.Format("Error_StartupInit", ex.Message);
        }

        _tray = _testNoTray
            ? null
            : new TrayIcon(ToggleWindowVisibility, ShowFromTray, OpenSettings, ExitApplication);

        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _tray?.RefreshTexts();
        _workspace.ReportStatus(string.Empty);
        RefreshAll();
    }

    public void InitializeAtStartup(bool forceShow)
    {
        ConfigureWindowPlacement();

        if (_testOffscreen)
        {
            MoveOffscreenForTests();
        }

        if (_settings.AlwaysOnTop)
        {
            NativeWindow.SetAlwaysOnTop(GetWindowHandle(), true);
        }

        var isFirstLaunch = !_settingsStore.Exists;
        if (isFirstLaunch)
        {
            _settingsStore.Save(_settings);
        }

        if (forceShow || _testOffscreen || !_settings.StartInTray || isFirstLaunch)
        {
            ShowFromTray();
        }
    }

    private void MoveOffscreenForTests()
    {
        try
        {
            NativeWindow.HideFromTaskbar(GetWindowHandle());
            _appWindow.Move(new PointInt32(-32000, -32000));
        }
        catch (Exception)
        {
            // Off-screen placement is a test convenience only.
        }
    }

    // ---- Public window operations ------------------------------------------------------

    public void ShowFromTray()
    {
        if (_isExiting)
        {
            return;
        }

        if (_testOffscreen)
        {
            NativeWindow.ShowWithoutActivation(GetWindowHandle());
            _liveRefresh.SetVisible(true);
            return;
        }

        _appWindow.Show();
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        NativeWindow.BringToForeground(GetWindowHandle());
        _liveRefresh.SetVisible(true);
    }

    public void ReportUnexpectedError(Exception exception)
    {
        try
        {
            _confirmations.ShowError(Localization.Text.Format("Error_Unexpected", exception.Message));
        }
        catch (Exception)
        {
            // A broken error dialog must not take the application down.
        }
    }

    private void ToggleWindowVisibility()
    {
        var handle = GetWindowHandle();

        if (NativeWindow.IsVisible(handle) && !NativeWindow.IsMinimized(handle))
        {
            HideToTray();
        }
        else
        {
            ShowFromTray();
        }
    }

    private void HideToTray()
    {
        _liveRefresh.SetVisible(false);
        _workspace.SaveState();
        PersistWindowPlacement();
        _appWindow.Hide();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _liveRefresh.SetVisible(false);
        _workspace.SaveState();
        PersistWindowPlacement();
        _isExiting = true;
        _placementTimer.Stop();

        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }

        _liveRefresh.Dispose();
        Application.Current.Exit();
    }

    // ---- Window configuration ----------------------------------------------------------

    private void ConfigureWindowShell()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        try
        {
            AppIcon.ApplyToWindow(this);
        }
        catch (Exception)
        {
            // The window icon is cosmetic; never fail startup because of it.
        }
    }

    private void ConfigureWindowPlacement()
    {
        try
        {
            var scale = NativeWindow.GetDpiScale(GetWindowHandle());
            var workingArea = NativeWindow.GetWorkingArea(GetWindowHandle());
            var placement = _settings.RememberWindowPlacement ? _workspace.WindowPlacement : null;

            if (placement is not null)
            {
                var width = Math.Clamp(placement.Width, 400, workingArea.Width);
                var height = Math.Clamp(placement.Height, 300, workingArea.Height);
                var left = Math.Clamp(placement.Left, workingArea.Left, workingArea.Right - width);
                var top = Math.Clamp(placement.Top, workingArea.Top, workingArea.Bottom - height);
                _appWindow.MoveAndResize(new RectInt32(left, top, width, height));
                return;
            }

            var defaultWidth = (int)Math.Round(DefaultWidthDip * scale);
            var defaultHeight = (int)Math.Round(DefaultHeightDip * scale);
            defaultWidth = Math.Min(defaultWidth, workingArea.Width);
            defaultHeight = Math.Min(defaultHeight, workingArea.Height);

            _appWindow.MoveAndResize(new RectInt32(
                workingArea.Left + (workingArea.Width - defaultWidth) / 2,
                workingArea.Top + (workingArea.Height - defaultHeight) / 2,
                defaultWidth,
                defaultHeight));
        }
        catch (Exception)
        {
            // Placement is a convenience; the default OS placement is still usable.
        }
    }

    private void PersistWindowPlacement()
    {
        if (_isExiting || !_settings.RememberWindowPlacement)
        {
            return;
        }

        try
        {
            var position = _appWindow.Position;
            var size = _appWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            _workspace.SaveWindowPlacement(new WindowPlacementState
            {
                Left = position.X,
                Top = position.Y,
                Width = size.Width,
                Height = size.Height,
            });
        }
        catch (Exception)
        {
            // Losing the window rectangle is not worth interrupting the user.
        }
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPaneWidths();
        ApplyTheme();
        RefreshAll();
        UpdateAddButtonState();
        _liveRefresh.SetVisible(NativeWindow.IsVisible(GetWindowHandle()));

        if (_startupError is not null && !_startupErrorReported)
        {
            _startupErrorReported = true;
            ShowError(_startupError);
        }
    }

    private void ApplyTheme()
    {
        try
        {
            WindowTitleBarTheme.Apply(this, RootGrid.ActualTheme);
        }
        catch (Exception)
        {
            // The title bar theme is cosmetic.
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        _placementTimer.Stop();
        _placementTimer.Start();
    }

    private void OnRailSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _railWidth += e.HorizontalChange;
        ApplyPaneWidths();
    }

    private void OnPreviewSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _previewWidth = PreviewColumn.ActualWidth - e.HorizontalChange;
        ApplyPaneWidths();
        _previewWidth = PreviewColumn.Width.Value;
    }

    private void OnPreviewSplitterDragCompleted(object sender, DragCompletedEventArgs e)
        => _workspace.SavePreviewWidth(_previewWidth);

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => ApplyPaneWidths();

    private void ApplyPaneWidths()
    {
        var available = WorkspaceGrid.ActualWidth - 2 * SplitterWidth;
        if (available <= 0) return;

        var railMaximum = Math.Max(MinRailWidth, Math.Min(360, available - MinTreeWidth - MinPreviewWidth));
        _railWidth = Math.Clamp(_railWidth, MinRailWidth, railMaximum);
        var previewMaximum = Math.Max(MinPreviewWidth, available - _railWidth - MinTreeWidth);
        RailColumn.Width = new GridLength(_railWidth);
        PreviewColumn.Width = new GridLength(Math.Clamp(_previewWidth, MinPreviewWidth, previewMaximum));
    }

    // ---- Settings ----------------------------------------------------------------------

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore, _settings.Clone(), ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        NativeWindow.SetAlwaysOnTop(GetWindowHandle(), settings.AlwaysOnTop);
        Localization.SetLanguage(settings.Language);
    }

    // ---- Presentation ---------------------------------------------------------------

    private void RefreshAll(bool refreshTree = true)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            if (_editingBasketUid is null)
            {
                RefreshBaskets();
            }

            RefreshHeader();
            if (refreshTree) RefreshTree();
            RefreshStatus();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void RefreshHeader()
    {
        var tab = _workspace.ActiveTab;
        BasketPathText.Text = tab is null ? string.Empty : _workspace.SelectedFolderPath;
        ToolTipService.SetToolTip(BasketPathText, _workspace.SelectedFolderPath);
        Title = tab is null ? "PasteHatch" : $"{tab.DisplayName} — PasteHatch";

        var hasTab = tab is not null;
        PasteButton.IsEnabled = hasTab;
        NewFolderButton.IsEnabled = hasTab;
        OpenFolderButton.IsEnabled = hasTab;
        CopyPathButton.IsEnabled = hasTab;
        AddBasketButton.IsEnabled = _workspace.CanCreateTab;
        ToolTipService.SetToolTip(
            AddBasketButton,
            Localization.Text.Get(_workspace.CanCreateTab ? "ToolTip_CreateBasket" : "Status_BasketLimit"));
    }

    private void RefreshStatus()
    {
        var status = _workspace.StatusMessage;
        StatusText.Text = status ?? string.Empty;

        if (_workspace.StatusIsError && !string.IsNullOrWhiteSpace(status))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = status;
            StatusInfoBar.IsOpen = true;
        }
        else
        {
            StatusInfoBar.IsOpen = false;
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    // ---- Basket rail -------------------------------------------------------------------

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
        var activeUid = _workspace.ActiveTab?.Uid;
        _basketRows.Clear();

        foreach (var tab in _workspace.Tabs)
        {
            _basketRows.Add(new BasketRow(
                tab,
                string.Equals(tab.Uid, activeUid, StringComparison.Ordinal),
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

    private void UpdateAddButtonState() => AddBasketButton.IsEnabled = _workspace.CanCreateTab;

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
                _workspace.OpenActiveBasketFolder();
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

    // ---- File tree ---------------------------------------------------------------------

    private void RefreshTree()
    {
        _treeNavigation.Capture(_selectedNode?.Entry, CollectExpandedPaths());
        var view = _treeNavigation.Restore();
        FileTree.RootNodes.Clear();

        var tab = _workspace.ActiveTab;
        if (tab is null)
        {
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
        BasketPathText.Text = _workspace.SelectedFolderPath;
        ToolTipService.SetToolTip(BasketPathText, _workspace.SelectedFolderPath);
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
        if (entry.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
        }
        else if (entry.Material is not null)
        {
            _workspace.OpenMaterial(entry.Material);
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
                if (_selectedNode.IsFolder && FileTree.SelectedNode is { } selectedNode)
                {
                    selectedNode.IsExpanded = !selectedNode.IsExpanded;
                }
                else if (_selectedNode.Material is not null)
                {
                    _workspace.OpenMaterial(_selectedNode.Material);
                }

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
        open.Click += (_, _) =>
        {
            if (node.IsFolder && FileTree.SelectedNode is { } selectedNode)
            {
                selectedNode.IsExpanded = !selectedNode.IsExpanded;
            }
            else if (node.Material is not null)
            {
                _workspace.OpenMaterial(node.Material);
            }
        };

        var explore = new MenuFlyoutItem { Text = Localization.Text.Get("Menu_OpenInExplorer") };
        explore.Click += (_, _) =>
        {
            if (node.IsFolder)
            {
                _workspace.OpenFolderInExplorer(node.Entry);
            }
            else
            {
                _workspace.OpenActiveBasketFolder();
            }
        };

        var copyPath = new MenuFlyoutItem { Text = Localization.Text.Get("Command_CopyPath") };
        copyPath.Click += (_, _) => _workspace.CopyEntryPath(node.Entry);

        flyout.Items.Add(open);
        flyout.Items.Add(explore);
        flyout.Items.Add(copyPath);

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

        if (node.IsFolder)
        {
            _workspace.DeleteFolder(node.Entry);
        }
        else if (node.Material is not null)
        {
            _workspace.DeleteMaterial(node.Material);
        }
    }

    private void BeginNodeRename(EntryNode node)
    {
        if (node.IsRoot)
        {
            return;
        }

        node.BeginEdit();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FileTree.SelectedNode is not { } treeNode)
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
        var success = node.IsFolder
            ? _workspace.RenameFolder(node.Entry, node.EditText)
            : node.Material is not null && _workspace.RenameMaterial(node.Material, node.EditText);

        if (!success)
        {
            node.EndEdit();
        }
    }

    // ---- Preview -----------------------------------------------------------------------

    private void ShowPreview(EntryNode? node)
    {
        _previewSequence++;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Text = Localization.Text.Get("Preview_Placeholder");
        PreviewPlaceholder.Visibility = Visibility.Visible;

        if (node is null)
        {
            PreviewTitle.Text = string.Empty;
            PreviewMeta.Text = string.Empty;
            return;
        }

        PreviewTitle.Text = node.Name;
        PreviewMeta.Text = node.IsFolder
            ? Localization.Text.Get("Kind_Folder")
            : $"{node.TypeLabel} · {node.SizeText} · {node.Entry.Modified.LocalDateTime.ToString("g", Localization.Culture)}";

        if (node.IsFolder)
        {
            PreviewPlaceholder.Text = Localization.Text.Get(
                FileTree.SelectedNode?.IsExpanded == true
                    ? "Preview_FolderExpanded"
                    : "Preview_FolderCollapsed");
            return;
        }

        if (node.Entry.Kind == MaterialKind.Image)
        {
            _ = LoadPreviewImageAsync(node.FullPath, _previewSequence);
            return;
        }

        if (node.Entry.Kind is MaterialKind.Text or MaterialKind.Link)
        {
            try
            {
                var text = MaterialPreview.ReadText(node.FullPath, MaxTextPreviewCharacters);
                if (text.Length == MaxTextPreviewCharacters)
                {
                    text += Environment.NewLine + "…";
                }

                PreviewText.Text = text;
                PreviewText.Visibility = Visibility.Visible;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PreviewPlaceholder.Text = Localization.Text.Format("Preview_ReadError", ex.Message);
            }
        }
        else
        {
            PreviewPlaceholder.Text = Localization.Text.Get("Preview_Unsupported");
        }
    }

    private async Task LoadPreviewImageAsync(string path, int sequence)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = 480 };
            await bitmap.SetSourceAsync(stream);

            if (sequence != _previewSequence)
            {
                return;
            }

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (sequence == _previewSequence)
            {
                PreviewPlaceholder.Text = Localization.Text.Format("Preview_ImageError", ex.Message);
            }
        }
    }

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

        _workspace.RememberFolder(CurrentFolder);
        if (_workspace.AddQuickNote(text) is not null)
        {
            QuickNoteTextBox.Text = string.Empty;
        }
    }

    // ---- New folder, refresh, header commands ------------------------------------------

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
        _workspace.RememberFolder(CurrentFolder);
        var folder = _workspace.CreateFolder(
            Localization.Text.Get("Default_NewFolderName"), ensureUnique: true);
        if (folder is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (FileTree.RootNodes.Count == 0)
            {
                return;
            }

            var node = SelectTreePath(FileTree.RootNodes[0], folder.RelativePath, select: true);
            if (node?.Content is EntryNode entry)
            {
                BeginNodeRename(entry);
            }
        });
    }

    private void OnRefreshAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _workspace.Refresh();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        _workspace.RememberFolder(CurrentFolder);
        _workspace.OpenActiveBasketFolder();
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        _workspace.RememberFolder(CurrentFolder);
        _workspace.CopyActiveBasketPath();
    }

    // ---- Drag and drop -----------------------------------------------------------------

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
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
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
            _confirmations.ShowError(Localization.Text.Format("Error_DropFiles", ex.Message));
        }
    }

    private void UpdateDropHighlight(bool active)
    {
        FileTree.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        FileTree.BorderBrush = active
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : null;
    }

    // ---- Helpers -----------------------------------------------------------------------

    private static BasketRow? FindBasketRowFromSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is BasketRow row)
            {
                return row;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static TreeViewNode? FindTreeViewNodeFromSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is TreeViewNode node)
            {
                return node;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private IntPtr GetWindowHandle()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = NativeWindow.GetHandle(this);
        }

        return _windowHandle;
    }

    private static T? FindDescendant<T>(DependencyObject root, string? name = null)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && (name is null || string.Equals(typed.Name, name, StringComparison.Ordinal)))
            {
                return typed;
            }

            var nested = FindDescendant<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
