using System;
using System.Drawing;
using System.Windows.Forms;

namespace PasteHatch.Platform;

/// <summary>
/// Windows notification-area icon with standard tray behaviour: a left click shows or hides the
/// main window, right click opens the standard WinForms context menu ("Показать окно" as the
/// bold default, then "Настройки" and "Выход"), and double-click shows the window.
/// Must be created and disposed on the UI (STA) thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Icon _icon;
    private readonly Font _boldFont;
    private bool _disposed;

    public TrayIcon(Action onToggleWindow, Action onShowWindow, Action onOpenSettings, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onToggleWindow);
        ArgumentNullException.ThrowIfNull(onShowWindow);
        ArgumentNullException.ThrowIfNull(onOpenSettings);
        ArgumentNullException.ThrowIfNull(onExit);

        _icon = AppIcon.LoadTrayIcon();
        var menuFont = SystemFonts.MenuFont;
        _boldFont = menuFont is null
            ? new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold)
            : new Font(menuFont, FontStyle.Bold);

        var showItem = new ToolStripMenuItem("Показать окно") { Font = _boldFont };
        showItem.Click += (_, _) => onShowWindow();

        var settingsItem = new ToolStripMenuItem("Настройки");
        settingsItem.Click += (_, _) => onOpenSettings();

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => onExit();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(showItem);
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "PasteHatch",
            Icon = _icon,
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };

        // Single left click is the tray-app convention for showing/hiding the window.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                onToggleWindow();
            }
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _boldFont.Dispose();
        _icon.Dispose();
    }
}
