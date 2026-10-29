namespace PasteHatch.Core;

/// <summary>Windows shell operations (default app, Explorer, Recycle Bin, clipboard write).</summary>
public interface IShellService
{
    /// <summary>Opens a file with its default Windows application (Explorer double-click equivalent).</summary>
    void OpenItem(string path);

    /// <summary>Opens a folder in Windows Explorer.</summary>
    void OpenFolder(string path);

    /// <summary>Moves a file to the Windows Recycle Bin (undoable, recoverable).</summary>
    void RecycleFile(string path);

    /// <summary>Moves a folder and its contents to the Windows Recycle Bin.</summary>
    void RecycleDirectory(string path);

    /// <summary>Puts a plain-text value on the clipboard.</summary>
    void CopyText(string text);

    /// <summary>Puts the picture of an image file on the clipboard.</summary>
    void CopyImageToClipboard(string path);

    /// <summary>
    /// Puts files and folders on the clipboard as a standard file drop, so they can also be
    /// pasted into Explorer. <paramref name="move"/> marks a cut (Ctrl+X) instead of a copy.
    /// </summary>
    void CopyFilesToClipboard(IReadOnlyList<string> paths, bool move);
}
