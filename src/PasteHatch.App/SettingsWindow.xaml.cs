using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PasteHatch.Core;
using PasteHatch.Platform;

namespace PasteHatch;

/// <summary>
/// Small standard settings window. Every change is applied and saved immediately, as the
/// Windows settings guidelines recommend (no OK/Cancel round-trip).
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onChanged;
    private bool _isInitializing;

    public SettingsWindow(SettingsStore store, AppSettings settings, Action<AppSettings> onChanged)
    {
        InitializeComponent();

        _store = store;
        _settings = settings;
        _onChanged = onChanged;

        ConfigureWindow();

        _isInitializing = true;
        StartInTrayToggle.IsOn = _settings.StartInTray;
        RememberPlacementToggle.IsOn = _settings.RememberWindowPlacement;
        AlwaysOnTopToggle.IsOn = _settings.AlwaysOnTop;
        LanguageCombo.SelectedIndex =
            Localization.Normalize(_settings.Language) == Localization.Russian ? 1 : 0;
        _isInitializing = false;

        RefreshTexts();

        SettingsRoot.Loaded += (_, _) => ApplyTheme();
        SettingsRoot.ActualThemeChanged += (_, _) => ApplyTheme();
        Localization.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshTexts();

    private void RefreshTexts()
    {
        Title = Localization.Text.Get("Settings_Title");
        VersionText.Text = Localization.Text.Format(
            "Settings_Version",
            typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0");
    }

    private void ApplyTheme()
    {
        try
        {
            WindowTitleBarTheme.Apply(this, SettingsRoot.ActualTheme);
        }
        catch (Exception)
        {
            // Cosmetic only.
        }
    }

    private void ConfigureWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.IsMinimizable = true;
        }

        try
        {
            AppIcon.ApplyToWindow(this);
        }
        catch (Exception)
        {
            // Cosmetic only.
        }

        if (string.Equals(
            Environment.GetEnvironmentVariable("PASTEHATCH_TEST_OFFSCREEN"), "1", StringComparison.Ordinal))
        {
            // Verification runs must not put a window in front of the person using the computer.
            NativeWindow.HideFromTaskbar(NativeWindow.GetHandle(this));
            AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            return;
        }

        var scale = NativeWindow.GetDpiScale(NativeWindow.GetHandle(this));
        var workingArea = NativeWindow.GetWorkingArea(NativeWindow.GetHandle(this));
        var width = (int)Math.Round(480 * scale);
        var height = (int)Math.Round(560 * scale);
        width = Math.Min(width, workingArea.Width);
        height = Math.Min(height, workingArea.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            workingArea.Left + (workingArea.Width - width) / 2,
            workingArea.Top + (workingArea.Height - height) / 2,
            width,
            height));
    }

    private void OnStartInTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.StartInTray = StartInTrayToggle.IsOn;
        SaveAndApply();
    }

    private void OnRememberPlacementToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.RememberWindowPlacement = RememberPlacementToggle.IsOn;
        SaveAndApply();
    }

    private void OnAlwaysOnTopToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.AlwaysOnTop = AlwaysOnTopToggle.IsOn;
        SaveAndApply();
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing ||
            LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language } ||
            string.Equals(_settings.Language, language, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Language = language;
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception)
        {
            // Settings persistence failure must not break the app; the change still applies
            // for the current session.
        }

        _onChanged(_settings.Clone());
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _settings.StartInTray = defaults.StartInTray;
        _settings.RememberWindowPlacement = defaults.RememberWindowPlacement;
        _settings.AlwaysOnTop = defaults.AlwaysOnTop;
        _settings.Language = defaults.Language;

        _isInitializing = true;
        StartInTrayToggle.IsOn = _settings.StartInTray;
        RememberPlacementToggle.IsOn = _settings.RememberWindowPlacement;
        AlwaysOnTopToggle.IsOn = _settings.AlwaysOnTop;
        LanguageCombo.SelectedIndex = 0;
        _isInitializing = false;

        SaveAndApply();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

