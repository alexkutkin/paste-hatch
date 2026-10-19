namespace PasteHatch.Core;

/// <summary>
/// User-tunable application settings. Defaults follow normal Windows tray-app behaviour:
/// the app starts in the notification area and remembers the floating window position.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Start hidden in the notification area instead of opening the window.</summary>
    public bool StartInTray { get; set; } = true;

    /// <summary>Restore the window position and size from the previous session.</summary>
    public bool RememberWindowPlacement { get; set; } = true;

    /// <summary>Keep the window above all other windows.</summary>
    public bool AlwaysOnTop { get; set; }

    public AppSettings Clone() => new()
    {
        StartInTray = StartInTray,
        RememberWindowPlacement = RememberWindowPlacement,
        AlwaysOnTop = AlwaysOnTop,
    };
}
