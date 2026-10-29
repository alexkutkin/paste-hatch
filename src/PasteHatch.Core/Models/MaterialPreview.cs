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

    /// <summary>Reads a whole text file, detecting a byte order mark like <see cref="ReadText"/>.</summary>
    public static string ReadAllText(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Returns the address stored in an Internet shortcut (<c>URL=</c> line); a file without one
    /// is returned as text.
    /// </summary>
    public static string ReadLinkTarget(string fullPath)
    {
        var text = ReadAllText(fullPath);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[4..];
            }
        }

        return text;
    }
}
