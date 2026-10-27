using System;
using System.Drawing;
using System.Windows.Forms;

namespace PasteHatch.Platform;

/// <summary>
/// Windows notification-area icon with standard tray behaviour: a left click shows or hides the
/// main window, right click opens the standard WinForms context menu (the show command is the
/// bold default, then settings and exit), and double-click shows the window.
/// Must be created and disposed on the UI (STA) thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;
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

        _showItem = new ToolStripMenuItem { Font = _boldFont };
        _showItem.Click += (_, _) => onShowWindow();

        _settingsItem = new ToolStripMenuItem();
        _settingsItem.Click += (_, _) => onOpenSettings();

        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => onExit();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_showItem);
        _contextMenu.Items.Add(_settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "PasteHatch",
            Icon = _icon,
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };

        RefreshTexts();

        // Single left click is the tray-app convention for showing/hiding the window.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                onToggleWindow();
            }
        };
    }

    /// <summary>Re-reads the menu labels after the UI language changed.</summary>
    public void RefreshTexts()
    {
        _showItem.Text = Localization.Text.Get("Tray_ShowWindow");
        _settingsItem.Text = Localization.Text.Get("Command_Settings");
        _exitItem.Text = Localization.Text.Get("Tray_Exit");
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
