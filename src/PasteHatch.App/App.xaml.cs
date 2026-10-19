using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PasteHatch.Platform;

namespace PasteHatch;

/// <summary>
/// Application entry point. PasteHatch lives in the notification area: a normal launch starts
/// hidden in the tray (the very first launch opens the window once), a second launch asks the
/// running instance to show its window, and <c>--show</c> forces the window open.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private SingleInstanceHost? _singleInstance;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _singleInstance = SingleInstanceHost.Acquire(() =>
        {
            dispatcher?.TryEnqueue(() => (_window as MainWindow)?.ShowFromTray());
        });

        if (_singleInstance is null)
        {
            Exit();
            return;
        }

        var forceShow = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase));

        _window = new MainWindow();
        ((MainWindow)_window).InitializeAtStartup(forceShow);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the tray utility alive and surface the failure in a native dialog instead of
        // terminating silently in the background.
        e.Handled = true;
        (_window as MainWindow)?.ReportUnexpectedError(e.Exception);
    }
}
