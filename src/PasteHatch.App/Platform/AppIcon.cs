using System;
using System.Drawing;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace PasteHatch.Platform;

/// <summary>
/// Application icon loading. The tray icon and the window icon both use
/// <c>Assets\PasteHatch.ico</c> next to the executable (a content item copied on build); if that
/// file is unavailable the icon embedded in the executable is used instead.
/// </summary>
public static class AppIcon
{
    private const string AssetsFolderName = "Assets";
    private const string IconFileName = "PasteHatch.ico";

    /// <summary>
    /// Loads the tray icon. The caller owns (and must dispose) the returned icon.
    /// </summary>
    public static Icon LoadTrayIcon()
    {
        var iconPath = ResolveIconPath();
        if (iconPath is not null)
        {
            try
            {
                return new Icon(iconPath);
            }
            catch (Exception)
            {
                // A corrupt icon file must not prevent the tray icon from appearing.
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            try
            {
                var extractedIcon = Icon.ExtractAssociatedIcon(processPath);
                if (extractedIcon is not null)
                {
                    return extractedIcon;
                }
            }
            catch (Exception)
            {
                // Fall through to the generic system icon below.
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>
    /// Applies the application icon to the WinUI window (taskbar and title bar) through
    /// <see cref="AppWindow.SetIcon(string)"/>. The icon is cosmetic: a failure here leaves the
    /// default icon in place and must not stop the window from opening.
    /// </summary>
    public static void ApplyToWindow(WinUIWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var iconPath = ResolveIconPath();
        if (iconPath is null)
        {
            return;
        }

        try
        {
            var hwnd = NativeWindow.GetHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.SetIcon(iconPath);
        }
        catch (Exception)
        {
            // Keep the default window icon rather than failing window creation.
        }
    }

    private static string? ResolveIconPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, AssetsFolderName, IconFileName);
        return File.Exists(path) ? path : null;
    }
}
