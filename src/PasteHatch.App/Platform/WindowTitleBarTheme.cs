using System;
using Microsoft.UI.Xaml;
using PasteHatch.Platform;
using Windows.UI;

namespace PasteHatch.Platform;

/// <summary>
/// Keeps the standard WinUI title bar in sync with the app theme. WinUI 3 does not always apply
/// the immersive dark title bar for unpackaged apps, which left a light title bar above dark
/// content; setting the DWM attribute and the caption colours explicitly fixes that.
/// </summary>
public static class WindowTitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window, ElementTheme actualTheme)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = NativeWindow.GetHandle(window);
        var dark = actualTheme == ElementTheme.Dark;
        NativeWindow.SetDarkTitleBar(hwnd, dark);

        var titleBar = window.AppWindow.TitleBar;
        var transparent = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = dark
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 120, 120, 120);
        titleBar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(30, 255, 255, 255)
            : Color.FromArgb(30, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(50, 255, 255, 255)
            : Color.FromArgb(50, 0, 0, 0);
    }
}

