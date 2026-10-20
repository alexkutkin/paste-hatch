namespace PasteHatch.Core;

/// <summary>Content of the tab-state JSON file in <c>%LOCALAPPDATA%\PasteHatch\state.json</c>.</summary>
public sealed class WorkspaceState
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>UID of the tab that was active when the app last saved; null falls back to the first tab.</summary>
    public string? ActiveUid { get; set; }

    public List<TabState> Tabs { get; set; } = [];

    public WindowPlacementState? Window { get; set; }
}

public sealed class TabState
{
    /// <summary>Folder name of the basket inside the baskets root.</summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>User-visible tab name; the hyphenated UID remains the on-disk identity.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User's order of material file names inside this basket. Files on disk remain the source of
    /// truth: names that no longer exist are ignored, new files are appended after known ones.
    /// </summary>
    public List<string> MaterialOrder { get; set; } = [];
}

/// <summary>Last floating-window rectangle in physical pixels, restored on the next launch.</summary>
public sealed class WindowPlacementState
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
