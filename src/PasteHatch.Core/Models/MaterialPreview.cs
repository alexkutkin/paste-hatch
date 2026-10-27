using System.Text;

namespace PasteHatch.Core;

/// <summary>
/// Bounded text reads for the preview pane. Only plain text and Markdown are previewed; other
/// materials keep their type label. The bound keeps a large file from blocking the UI.
/// </summary>
public static class MaterialPreview
{
    /// <summary>
    /// Reads the beginning of a text file for the preview pane. The bounded read keeps a large
    /// file from blocking the UI; the caller can append an ellipsis when the cap was reached.
    /// </summary>
    public static string ReadText(string fullPath, int maxCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // Encoding.UTF8 uses replacement fallback, so invalid bytes never throw; the BOM is
        // detected and stripped so it cannot leak into the preview.
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var buffer = new char[maxCharacters];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }
}
