using System.Text;

namespace PasteHatch.Core;

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

        using var reader = OpenReader(fullPath);

        var buffer = new char[maxCharacters];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    public static string ReadAllText(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        using var reader = OpenReader(fullPath);
        return reader.ReadToEnd();
    }

    private static StreamReader OpenReader(string fullPath)
    {
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        // Detect BOMs; malformed UTF-8 uses replacement characters rather than failing preview.
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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
