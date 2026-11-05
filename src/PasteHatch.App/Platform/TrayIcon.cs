using System;
using System.Collections.Generic;
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
    private readonly Func<IReadOnlyList<string>> _recentCopiedPaths;
    private readonly Action<string> _copyPath;
    private readonly Func<string, string> _pathLabel;
    private readonly List<ToolStripItem> _historyItems = [];
    private bool _disposed;

    public TrayIcon(Action onToggleWindow, Action onShowWindow, Action onOpenSettings, Action onExit,
        Func<IReadOnlyList<string>> recentCopiedPaths, Action<string> copyPath, Func<string, string> pathLabel)
    {
        ArgumentNullException.ThrowIfNull(onToggleWindow);
        ArgumentNullException.ThrowIfNull(onShowWindow);
        ArgumentNullException.ThrowIfNull(onOpenSettings);
        ArgumentNullException.ThrowIfNull(onExit);
        _recentCopiedPaths = recentCopiedPaths ?? throw new ArgumentNullException(nameof(recentCopiedPaths));
        _copyPath = copyPath ?? throw new ArgumentNullException(nameof(copyPath));
        _pathLabel = pathLabel ?? throw new ArgumentNullException(nameof(pathLabel));

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
        RefreshHistory();
    }

    public void RefreshHistory()
    {
        var paths = _recentCopiedPaths();
        var heading = Localization.Text.Get("Tray_RecentCopiedPaths");
        var unchanged = paths.Count == 0
            ? _historyItems.Count == 0
            : _historyItems.Count == paths.Count + 2 && _historyItems[1].Text == heading;
        for (var i = 0; unchanged && i < paths.Count; i++)
        {
            unchanged = _historyItems[i + 2] is ToolStripMenuItem item &&
                item.ToolTipText == paths[i] &&
                item.Text == _pathLabel(paths[i]).Replace("&", "&&", StringComparison.Ordinal);
        }
        if (unchanged) return;

        foreach (var item in _historyItems)
        {
            _contextMenu.Items.Remove(item);
            item.Dispose();
        }
        _historyItems.Clear();
        if (paths.Count == 0) return;

        _historyItems.Add(new ToolStripSeparator());
        _historyItems.Add(new ToolStripLabel(heading));
        foreach (var path in paths)
        {
            var label = _pathLabel(path);
            var item = new ToolStripMenuItem(label)
            {
                ToolTipText = path,
                AccessibleName = path,
            };
            // Paths containing ampersands are literal labels, not keyboard mnemonics.
            item.Text = label.Replace("&", "&&", StringComparison.Ordinal);
            item.Click += (_, _) => _copyPath(path);
            _historyItems.Add(item);
        }
        // The existing separator before Exit also separates the end of the history group.
        var insertionIndex = _contextMenu.Items.IndexOf(_settingsItem) + 1;
        foreach (var item in _historyItems) _contextMenu.Items.Insert(insertionIndex++, item);
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
