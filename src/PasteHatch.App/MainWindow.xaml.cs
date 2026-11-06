using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PasteHatch.Core;
using PasteHatch.Platform;
using PasteHatch.Ui;
using Windows.Graphics;

namespace PasteHatch;

public sealed partial class MainWindow : Window
{
    private readonly ConfirmationService _confirmations;
    private readonly BasketWorkspace _workspace;
    private readonly SettingsStore _settingsStore;
    private readonly AppWindow _appWindow;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _placementTimer;
    private readonly WorkspaceRefreshCoordinator _liveRefresh;

    private AppSettings _settings;
    private TrayIcon? _tray;
    private string? _presentedError;
    private string? _startupError;
    private bool _startupErrorReported;
    private bool _isUpdatingUi;
    private bool _isExiting;
    private readonly bool _testOffscreen;
    private IntPtr _windowHandle = IntPtr.Zero;

    public MainWindow()
    {
        InitializeComponent();
        BasketsList.ItemsSource = _basketRows;

        _testOffscreen = string.Equals(
            Environment.GetEnvironmentVariable("PASTEHATCH_TEST_OFFSCREEN"), "1", StringComparison.Ordinal);
        var testNoTray = string.Equals(
            Environment.GetEnvironmentVariable("PASTEHATCH_TEST_NO_TRAY"), "1", StringComparison.Ordinal);

        _appWindow = AppWindow;
        _settingsStore = SettingsStore.CreateDefault();
        _settings = _settingsStore.Load();

        var clipboard = new ClipboardReader();
        var shell = new ShellService();
        _confirmations = new ConfirmationService(GetWindowHandle);

        _workspace = new BasketWorkspace(
            PasteHatchPaths.BasketsRoot,
            TabStateStore.CreateDefault(),
            clipboard,
            shell,
            _confirmations,
            formatter: new TabNameFormatter(() => Localization.Culture),
            text: Localization.Text,
            confirmBasketDeletion: () => _settings.ConfirmBasketDeletion);
        _treeNavigation = _workspace.Navigation;
        _liveRefresh = new WorkspaceRefreshCoordinator(_workspace, new FolderWatcher(), new FolderWatcher(),
            action => DispatcherQueue.TryEnqueue(() => action()), refresh => RefreshAll(refresh.RebuildTree), Localization.Text);

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
            _previewWidth = _workspace.Geometry.PreviewWidth ?? PreviewWidth;
        }
        catch (Exception ex)
        {
            _startupError = Localization.Text.Format("Error_StartupInit", ex.Message);
        }

        _tray = testNoTray
            ? null
            : new TrayIcon(ToggleWindowVisibility, ShowFromTray, OpenSettings, ExitApplication,
                () => _workspace.RecentCopiedPaths, path => _workspace.CopyFilesystemPath(path),
                path => TrayPathLabel.Format(path, _workspace.BasketsRoot, _workspace.Tabs));

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
        => ShowDialogError(Localization.Text.Format("Error_Unexpected", exception.Message));

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

    private void RefreshAll(bool refreshTree = true)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            _tray?.RefreshHistory();
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
        BasketPathText.Text = tab is null ? string.Empty : _treeNavigation.CurrentFolderPath;
        ToolTipService.SetToolTip(BasketPathText, _treeNavigation.CurrentFolderPath);
        Title = tab is null ? "PasteHatch" : $"{tab.DisplayName} — PasteHatch";

        var hasTab = tab is not null;
        PasteButton.IsEnabled = hasTab;
        NewFolderButton.IsEnabled = hasTab;
        OpenFolderButton.IsEnabled = hasTab;
        CopyPathButton.IsEnabled = hasTab;
        UpdateAddButtonState();
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
            if (!string.Equals(_presentedError, status, StringComparison.Ordinal))
            {
                _presentedError = status;
                ShowDialogError(status);
            }
        }
        else
        {
            _presentedError = null;
            StatusInfoBar.IsOpen = false;
        }
    }

    private void ShowDialogError(string message)
    {
        try
        {
            _confirmations.ShowError(message);
        }
        catch (Exception)
        {
            // A broken error dialog must not take the application down.
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

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

    private static T? FindDescendant<T>(DependencyObject root)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
