using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PasteHatch.Core;
using PasteHatch.Platform;
using PasteHatch.Ui;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace PasteHatch;

/// <summary>
/// The single compact window. It uses standard WinUI controls only (TabView, CommandBar,
/// ListView, TextBox, InfoBar, MenuFlyout) and the standard Windows title bar, keeps all
/// behaviour in <see cref="BasketWorkspace"/>, and hides to the notification area on close.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidthDip = 820;
    private const int DefaultHeightDip = 680;

    private readonly ConfirmationService _confirmations;
    private readonly ShellService _shell;
    private readonly BasketWorkspace _workspace;
    private readonly SettingsStore _settingsStore;
    private readonly AppWindow _appWindow;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _placementTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _statusTimer;

    private AppSettings _settings;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private MaterialRow? _selectedRow;
    private readonly System.Collections.ObjectModel.ObservableCollection<MaterialRow> _materialRows = new();
    private string? _tabsSignature;
    private string? _editingTabUid;
    private string? _editingTabOriginalName;
    private string? _hiddenStatus;
    private string? _startupError;
    private bool _startupErrorReported;
    private bool _refreshScheduled;
    private bool _isUpdatingUi;
    private bool _isExiting;
    private readonly bool _testOffscreen;
    private readonly bool _testNoTray;
    private IntPtr _windowHandle = IntPtr.Zero;

    public MainWindow()
    {
        InitializeComponent();
        MaterialsList.ItemsSource = _materialRows;

        // UI smoke-test isolation: run the window off-screen, without activation and optionally
        // without a tray icon, so automated checks never flash windows or steal focus.
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
            _confirmations);
        _workspace.Changed += OnWorkspaceChanged;

        ConfigureWindowShell();

        _placementTimer = DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(700);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += (_, _) => PersistWindowPlacement();

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(6);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => HideStatusBar();

        RootGrid.Loaded += OnRootLoaded;
        RootGrid.ActualThemeChanged += (_, _) => ApplyTheme();
        _appWindow.Closing += OnWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;

        try
        {
            _workspace.Initialize();
        }
        catch (Exception ex)
        {
            _startupError = $"Не удалось инициализировать хранилище корзин: {ex.Message}";
        }

        _tray = _testNoTray
            ? null
            : new TrayIcon(ToggleWindowVisibility, ShowFromTray, OpenSettings, ExitApplication);
    }

    /// <summary>
    /// Called once by <see cref="App"/> after the window is created. The app normally starts
    /// hidden in the notification area; the very first launch and <c>--show</c> open the window.
    /// </summary>
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

    /// <summary>Places the test window outside every monitor and removes it from taskbar/Alt+Tab.</summary>
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

    /// <summary>Shows and activates the window (tray click, second launch, "Показать окно").</summary>
    public void ShowFromTray()
    {
        if (_isExiting)
        {
            return;
        }

        if (_testOffscreen)
        {
            // Load the XAML tree for UI Automation without activating or flashing a window.
            NativeWindow.ShowWithoutActivation(GetWindowHandle());
            _workspace.Refresh();
            return;
        }

        _appWindow.Show();
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        NativeWindow.BringToForeground(GetWindowHandle());
        _workspace.Refresh();
    }

    public void ReportUnexpectedError(Exception exception)
    {
        try
        {
            _confirmations.ShowError($"Непредвиденная ошибка: {exception.Message}");
        }
        catch (Exception)
        {
            // A broken error dialog must not take the application down.
        }
    }

    private void ToggleWindowVisibility()
    {
        var handle = GetWindowHandle();

        // A minimized window is still "visible" to Win32, but the user cannot see it. Treat it as
        // hidden so a tray click restores it instead of silently hiding it behind the taskbar.
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
        PersistWindowPlacement();
        _appWindow.Hide();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        // Persist before flipping the exit flag: PersistWindowPlacement ignores saves while exiting.
        PersistWindowPlacement();
        _isExiting = true;
        _placementTimer.Stop();
        _statusTimer.Stop();

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

        Application.Current.Exit();
    }

    // ---- Window configuration ----------------------------------------------------------

    private void ConfigureWindowShell()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 440;
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
        ApplyTheme();
        RefreshAll();
        UpdateAddButtonState();

        if (_startupError is not null && !_startupErrorReported)
        {
            _startupErrorReported = true;
            ShowStatus(_startupError, InfoBarSeverity.Error);
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
    }

    // ---- Workspace change handling -----------------------------------------------------

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshScheduled = false;
            RefreshAll();
        });
    }

    private void RefreshAll()
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            if (_editingTabUid is null)
            {
                RefreshTabs();
            }

            RefreshHeader();
            RefreshMaterials();
            RefreshStatus();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void RefreshTabs()
    {
        // Rebuilding TabViewItems re-creates their XAML containers, so it only happens when the
        // tab strip really changed; material updates just re-sync the active selection.
        var signature = string.Join(
            '\u001f',
            _workspace.Tabs.Select(tab => tab.Uid + '\u001e' + tab.DisplayName)) + '\u001f' + _workspace.CanCloseTab;

        if (string.Equals(signature, _tabsSignature, StringComparison.Ordinal))
        {
            SelectActiveTabItem();
            return;
        }

        _tabsSignature = signature;
        var activeUid = _workspace.ActiveTab?.Uid;
        BasketsTabView.TabItems.Clear();

        TabViewItem? activeItem = null;
        foreach (var tab in _workspace.Tabs)
        {
            var item = new TabViewItem
            {
                Header = tab.DisplayName,
                Tag = tab.Uid,
                IsClosable = _workspace.CanCloseTab,
            };
            item.DoubleTapped += OnTabDoubleTapped;
            BasketsTabView.TabItems.Add(item);

            if (string.Equals(tab.Uid, activeUid, StringComparison.Ordinal))
            {
                activeItem = item;
            }
        }

        if (activeItem is not null)
        {
            BasketsTabView.SelectedItem = activeItem;
        }

        DispatcherQueue.TryEnqueue(UpdateAddButtonState);
    }

    private void SelectActiveTabItem()
    {
        var activeUid = _workspace.ActiveTab?.Uid;
        var activeItem = BasketsTabView.TabItems
            .OfType<TabViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, activeUid, StringComparison.Ordinal));

        if (activeItem is not null && !ReferenceEquals(BasketsTabView.SelectedItem, activeItem))
        {
            BasketsTabView.SelectedItem = activeItem;
        }
    }

    private void UpdateAddButtonState()
    {
        var addButton = FindDescendant<Button>(BasketsTabView, "AddButton");
        if (addButton is null)
        {
            return;
        }

        addButton.IsEnabled = _workspace.CanCreateTab;
        ToolTipService.SetToolTip(
            addButton,
            _workspace.CanCreateTab ? "Создать корзину" : "Достигнут предел в 10 корзин");
    }

    private void RefreshHeader()
    {
        var tab = _workspace.ActiveTab;
        BasketPathText.Text = tab?.DirectoryPath ?? string.Empty;
        ToolTipService.SetToolTip(BasketPathText, tab?.DirectoryPath ?? string.Empty);
        Title = tab is null ? "PasteHatch" : $"{tab.DisplayName} — PasteHatch";

        var hasTab = tab is not null;
        PasteButton.IsEnabled = hasTab;
        OpenFolderButton.IsEnabled = hasTab;
        CopyPathButton.IsEnabled = hasTab;
    }

    private void RefreshMaterials()
    {
        var selectedFileName = _selectedRow?.FileName;
        SyncMaterialRows(_workspace.Materials);

        _selectedRow = null;
        if (selectedFileName is not null)
        {
            var match = _materialRows.FirstOrDefault(row =>
                string.Equals(row.FileName, selectedFileName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                MaterialsList.SelectedItem = match;
                _selectedRow = match;
            }
        }

        EmptyStatePanel.Visibility = _materialRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileCountText.Text = RussianPlural.Files(_materialRows.Count);
    }

    /// <summary>
    /// Brings the bound rows in line with the workspace one item at a time instead of replacing
    /// the whole ItemsSource on every paste; this keeps ListView containers stable and avoids
    /// allocation churn when materials are added frequently.
    /// </summary>
    private void SyncMaterialRows(System.Collections.Generic.IReadOnlyList<Material> materials)
    {
        for (var index = 0; index < materials.Count; index++)
        {
            var material = materials[index];
            if (index < _materialRows.Count &&
                string.Equals(_materialRows[index].FileName, material.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            var existingIndex = -1;
            for (var search = index + 1; search < _materialRows.Count; search++)
            {
                if (string.Equals(_materialRows[search].FileName, material.FileName, StringComparison.Ordinal))
                {
                    existingIndex = search;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                _materialRows.Move(existingIndex, index);
            }
            else
            {
                _materialRows.Insert(index, new MaterialRow(material));
            }
        }

        while (_materialRows.Count > materials.Count)
        {
            _materialRows.RemoveAt(_materialRows.Count - 1);
        }
    }

    private void RefreshStatus()
    {
        var status = _workspace.StatusMessage;
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, _hiddenStatus, StringComparison.Ordinal))
        {
            return;
        }

        ShowStatus(status, InfoBarSeverity.Informational);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void HideStatusBar()
    {
        _hiddenStatus = _workspace.StatusMessage;
        StatusInfoBar.IsOpen = false;
    }

    // ---- Tabs --------------------------------------------------------------------------

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        if (BasketsTabView.SelectedItem is TabViewItem item &&
            item.Tag is string uid &&
            !string.Equals(_workspace.ActiveTab?.Uid, uid, StringComparison.Ordinal))
        {
            _workspace.ActivateTab(uid);
        }
    }

    private void OnAddTabButtonClick(TabView sender, object args) => _workspace.CreateTab();

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab?.Tag is string uid)
        {
            _workspace.CloseTab(uid);
        }
    }

    private void OnTabDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is TabViewItem item && item.Tag is string uid)
        {
            e.Handled = true;
            BeginTabRename(item, uid);
        }
    }

    private void BeginTabRename(TabViewItem item, string uid)
    {
        if (_editingTabUid is not null)
        {
            return;
        }

        var current = _workspace.Tabs.FirstOrDefault(tab => tab.Uid == uid)?.DisplayName ?? string.Empty;
        var editor = new TextBox
        {
            Text = current,
            MinWidth = 120,
            MaxWidth = 220,
            Padding = new Thickness(6, 2, 6, 2),
        };

        _editingTabUid = uid;
        _editingTabOriginalName = current;

        editor.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                CommitTabRename(item, uid, editor.Text);
            }
            else if (args.Key == VirtualKey.Escape)
            {
                args.Handled = true;
                CancelTabRename(item, current);
            }
        };
        editor.LostFocus += (_, _) => CommitTabRename(item, uid, editor.Text);

        item.Header = editor;
        editor.Focus(FocusState.Programmatic);
        editor.SelectAll();
    }

    private void CommitTabRename(TabViewItem item, string uid, string text)
    {
        if (!string.Equals(_editingTabUid, uid, StringComparison.Ordinal))
        {
            return;
        }

        _editingTabUid = null;
        item.Header = _editingTabOriginalName ?? text;
        _workspace.RenameTab(uid, text);
        RefreshAll();
    }

    private void CancelTabRename(TabViewItem item, string originalName)
    {
        _editingTabUid = null;
        item.Header = originalName;
    }

    // ---- Materials ---------------------------------------------------------------------

    private void OnMaterialSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _selectedRow = MaterialsList.SelectedItem as MaterialRow;
    }

    private void OnMaterialDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MaterialRow row)
        {
            _workspace.OpenMaterial(row.Material);
            e.Handled = true;
        }
    }

    private void OnCopyMaterialPathClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MaterialRow row)
        {
            _workspace.CopyMaterialPath(row.Material);
        }
    }

    private void OnRenameSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MaterialRow row)
        {
            CommitMaterialRename(row);
        }
    }

    private void OnRenameCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MaterialRow row)
        {
            row.EndEdit();
        }
    }

    private void OnRenameBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is MaterialRow row)
        {
            row.EditText = textBox.Text;
        }
    }

    private void OnRenameBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not MaterialRow row)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            row.EditText = textBox.Text;
            CommitMaterialRename(row);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            row.EndEdit();
        }
    }

    private void CommitMaterialRename(MaterialRow row)
    {
        if (!_workspace.RenameMaterial(row.Material, row.EditText))
        {
            row.EndEdit();
        }
    }

    private void BeginRenameMaterial(MaterialRow row)
    {
        MaterialsList.SelectedItem = row;
        _selectedRow = row;
        row.BeginEdit();

        DispatcherQueue.TryEnqueue(() =>
        {
            var container = MaterialsList.ContainerFromItem(row) as ListViewItem;
            var editor = container is null ? null : FindDescendant<TextBox>(container);
            if (editor is not null)
            {
                editor.Focus(FocusState.Programmatic);
                editor.SelectAll();
            }
        });
    }

    private void OnMaterialsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_selectedRow is null)
        {
            return;
        }

        var shiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.F2:
                e.Handled = true;
                BeginRenameMaterial(_selectedRow);
                break;

            case VirtualKey.Delete:
                e.Handled = true;
                _workspace.DeleteMaterial(_selectedRow.Material);
                break;

            case VirtualKey.Enter:
                if (e.OriginalSource is not TextBox)
                {
                    e.Handled = true;
                    _workspace.OpenMaterial(_selectedRow.Material);
                }

                break;

            case VirtualKey.Application:
            case VirtualKey.F10 when shiftDown:
                e.Handled = true;
                ShowMaterialContextMenu(_selectedRow, GetSelectedRowPosition());
                break;
        }
    }

    private void OnMaterialsRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var row = FindRowFromSource(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        e.Handled = true;
        MaterialsList.SelectedItem = row;
        _selectedRow = row;
        ShowMaterialContextMenu(row, e.GetPosition(MaterialsList));
    }

    private void ShowMaterialContextMenu(MaterialRow row, Point position)
    {
        var flyout = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = "Открыть" };
        openItem.Click += (_, _) => _workspace.OpenMaterial(row.Material);

        var renameItem = new MenuFlyoutItem { Text = "Переименовать" };
        renameItem.Click += (_, _) => BeginRenameMaterial(row);

        // The row button is the fast pointer path; the context menu is the input-agnostic one
        // (keyboard, touch), as Microsoft's contextual commanding guidance requires.
        var copyPathItem = new MenuFlyoutItem { Text = "Скопировать путь" };
        copyPathItem.Click += (_, _) => _workspace.CopyMaterialPath(row.Material);

        var deleteItem = new MenuFlyoutItem { Text = "Удалить…" };
        deleteItem.Click += (_, _) => _workspace.DeleteMaterial(row.Material);

        flyout.Items.Add(openItem);
        flyout.Items.Add(renameItem);
        flyout.Items.Add(copyPathItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(deleteItem);
        flyout.ShowAt(MaterialsList, new FlyoutShowOptions { Position = position });
    }

    private Point GetSelectedRowPosition()
    {
        if (_selectedRow is not null && MaterialsList.ContainerFromItem(_selectedRow) is ListViewItem container)
        {
            try
            {
                var topLeft = container.TransformToVisual(MaterialsList).TransformPoint(new Point(0, 0));
                return new Point(topLeft.X + 24, topLeft.Y + Math.Max(8, container.ActualHeight / 2));
            }
            catch (Exception)
            {
                // Fall back to a corner of the list.
            }
        }

        return new Point(16, 16);
    }

    private static MaterialRow? FindRowFromSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is MaterialRow row)
            {
                return row;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---- Clipboard, drag and drop, quick note -----------------------------------------

    private void OnPasteClick(object sender, RoutedEventArgs e) => _workspace.AddFromClipboard();

    private void OnPasteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        _workspace.AddFromClipboard();
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox;
    }

    private void OnRootDragEnter(object sender, DragEventArgs e) => UpdateDropHighlight(true);

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Добавить в корзину";
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true;
            UpdateDropHighlight(true);
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnRootDragLeave(object sender, DragEventArgs e) => UpdateDropHighlight(false);

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        UpdateDropHighlight(false);
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
                _workspace.AddFiles(paths);
            }
        }
        catch (Exception ex)
        {
            _confirmations.ShowError($"Не удалось добавить перетащенные файлы: {ex.Message}");
        }
    }

    private void UpdateDropHighlight(bool active)
    {
        // Standard Button-style drag feedback: accent the list border while files hover the window.
        MaterialsList.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        MaterialsList.BorderBrush = active
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : null;
    }

    private void OnQuickNoteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            SaveQuickNote();
        }
    }

    private void OnSaveNoteClick(object sender, RoutedEventArgs e) => SaveQuickNote();

    private void SaveQuickNote()
    {
        var text = QuickNoteTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_workspace.AddQuickNote(text) is not null)
        {
            QuickNoteTextBox.Text = string.Empty;
        }
    }

    // ---- Header commands ---------------------------------------------------------------

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => _workspace.OpenActiveBasketFolder();

    private void OnCopyPathClick(object sender, RoutedEventArgs e) => _workspace.CopyActiveBasketPath();

    // ---- Helpers -----------------------------------------------------------------------

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

