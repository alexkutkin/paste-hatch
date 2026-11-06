namespace PasteHatch.Core;

/// <summary>Content of the tab-state JSON file in <c>%LOCALAPPDATA%\PasteHatch\state.json</c>.</summary>
public sealed class WorkspaceState
{
    /// <summary>
    /// Version 4 separates stable basket identity from its mutable directory component.
    /// Older directory associations are upgraded by the state reader without moving files.
    /// </summary>
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>UID of the tab that was active when the app last saved; null falls back to the first tab.</summary>
    public string? ActiveUid { get; set; }

    public List<TabState> Tabs { get; set; } = [];

    /// <summary>Literal filesystem-path copy events, newest first; duplicate paths are retained.</summary>
    public List<string> RecentCopiedPaths { get; set; } = [];

    /// <summary>Durable intent used to recover a basket rename interrupted between disk and state writes.</summary>
    public BasketRenameState? PendingRename { get; set; }

    public WindowPlacementState? Window { get; set; }

    /// <summary>Preferred preview width in device-independent pixels; absent in older state files.</summary>
    public double? PreviewWidth { get; set; }
}

public sealed class TabState
{
    /// <summary>Stable internal basket identity, independent of its directory name.</summary>
    public string Uid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>One directory component under the baskets root.</summary>
    public string DirectoryName { get; set; } = string.Empty;

    /// <summary>
    /// Folder currently open in this basket, relative to the basket root. An empty string is the
    /// basket root. The folder is re-validated on start, so a folder deleted outside the app falls
    /// back to the root instead of hiding the basket.
    /// </summary>
    public string SelectedFolder { get; set; } = string.Empty;
}

/// <summary>Last floating-window rectangle in physical pixels, restored on the next launch.</summary>
public sealed class WindowPlacementState
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class BasketRenameState
{
    public string Uid { get; set; } = string.Empty;
    public string DirectoryName { get; set; } = string.Empty;
    public string TemporaryDirectoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
