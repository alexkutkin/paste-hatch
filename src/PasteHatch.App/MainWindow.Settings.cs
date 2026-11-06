using Microsoft.UI.Xaml;
using PasteHatch.Core;
using PasteHatch.Platform;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private SettingsWindow? _settingsWindow;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            ShowSettingsWindow();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore, _settings.Clone(), ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        if (_testOffscreen)
        {
            NativeWindow.ShowWithoutActivation(NativeWindow.GetHandle(_settingsWindow));
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        NativeWindow.SetAlwaysOnTop(GetWindowHandle(), settings.AlwaysOnTop);
        Localization.SetLanguage(settings.Language);
    }
}
