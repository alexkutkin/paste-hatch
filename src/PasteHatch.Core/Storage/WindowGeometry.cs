namespace PasteHatch.Core;

/// <summary>
/// Window geometry remembered between launches: the window rectangle and the preview pane width.
/// The window owns the values and the workspace records them in the state file. Unusable values are
/// ignored instead of persisted, and they never replace a previously saved one.
/// </summary>
public sealed class WindowGeometry
{
    /// <summary>Last usable window rectangle in physical pixels, or null when none was saved.</summary>
    public WindowPlacementState? Placement { get; private set; }

    /// <summary>Preferred preview width in device-independent pixels, or null when none was saved.</summary>
    public double? PreviewWidth { get; private set; }

    /// <summary>Restores saved values; an unusable one is treated as never saved.</summary>
    public void Restore(WindowPlacementState? placement, double? previewWidth)
    {
        Placement = IsUsablePlacement(placement) ? placement : null;
        PreviewWidth = IsUsablePreviewWidth(previewWidth) ? previewWidth : null;
    }

    /// <summary>Records a completed move or resize; returns true when the value changed.</summary>
    public bool Save(WindowPlacementState? placement)
    {
        if (!IsUsablePlacement(placement))
        {
            return false;
        }

        Placement = placement;
        return true;
    }

    /// <summary>Records a completed preview resize; returns true when the value changed.</summary>
    public bool SavePreviewWidth(double width)
    {
        if (!IsUsablePreviewWidth(width) || PreviewWidth == width)
        {
            return false;
        }

        PreviewWidth = width;
        return true;
    }

    private static bool IsUsablePlacement(WindowPlacementState? placement)
        => placement is { Width: >= 400, Height: >= 300 };

    private static bool IsUsablePreviewWidth(double? width)
        => width is > 0 && double.IsFinite(width.Value);
}
