namespace PasteHatch.Core;

/// <summary>Content of the tab-state JSON file in <c>%LOCALAPPDATA%\PasteHatch\state.json</c>.</summary>
public sealed class WorkspaceState
{
    /// <summary>
    /// Version 3 replaced the flat <c>materialOrder</c> list with a per-tab selected folder.
    /// Version 2 files still load: the order is ignored, the files themselves are untouched.
    /// </summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>UID of the tab that was active when the app last saved; null falls back to the first tab.</summary>
    public string? ActiveUid { get; set; }

    public List<TabState> Tabs { get; set; } = [];

    public WindowPlacementState? Window { get; set; }

    /// <summary>Preferred preview width in device-independent pixels; absent in older state files.</summary>
    public double? PreviewWidth { get; set; }
}

public sealed class TabState
{
    /// <summary>Folder name of the basket inside the baskets root.</summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>User-visible tab name; the hyphenated UID remains the on-disk identity.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Folder currently open in this basket, relative to the basket root. An empty string is the
    /// basket root. The folder is re-validated on start, so a folder deleted outside the app falls
    /// back to the root instead of hiding the basket.
    /// </summary>
    public string SelectedFolder { get; set; } = string.Empty;

    /// <summary>
    /// Legacy version-2 field. Kept only so old state files deserialize without errors; the
    /// manual material order is no longer used and is never written.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MaterialOrder { get; set; }
}

/// <summary>Last floating-window rectangle in physical pixels, restored on the next launch.</summary>
public sealed class WindowPlacementState
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
