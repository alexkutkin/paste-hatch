using System.Text;

namespace PasteHatch.Core;

/// <summary>
/// Text helpers for basket materials: a short first-lines excerpt and the bounded text used by the
/// preview pane. Only plain text and Markdown are previewed; other materials keep their type label.
/// </summary>
public static class MaterialPreview
{
    /// <summary>How many non-empty lines of the file the excerpt keeps.</summary>
    public const int MaxLines = 2;

    /// <summary>Upper bound of the excerpt length before the trailing ellipsis is added.</summary>
    public const int MaxCharacters = 200;

    /// <summary>Returns true when a file with <paramref name="fileName"/> can produce a preview.</summary>
    public static bool SupportsPreview(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return PreviewExtensions.Contains(extension);
    }

    /// <summary>
    /// Returns the first non-empty lines of a text or Markdown material, or null when there is
    /// nothing to preview: unsupported type, missing or unreadable file, or no non-empty lines.
    /// </summary>
    public static string? Read(string? fullPath, string? fileName)
    {
        if (!SupportsPreview(fileName) || string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        string sample;

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Encoding.UTF8 uses replacement fallback, so invalid bytes never throw; the BOM is
            // detected and stripped so it cannot leak into the excerpt.
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            // Bounded sample: basket files may be large, and a preview only needs the start.
            var buffer = new char[MaxSampleCharacters];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            sample = new string(buffer, 0, read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // A malformed path (for example one containing a NUL character) is as unusable
            // as a missing file.
            return null;
        }

        return BuildExcerpt(sample);
    }

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

    private static string? BuildExcerpt(string sample)
    {
        var kept = new List<string>(MaxLines);
        var lineStart = 0;

        for (var index = 0; index <= sample.Length && kept.Count < MaxLines; index++)
        {
            if (index < sample.Length && sample[index] != '\n')
            {
                continue;
            }

            // Trim removes the '\r' of CRLF line endings and any surrounding whitespace; a line
            // that is only whitespace is skipped, including blank lines at the start of the file.
            var line = sample.AsSpan(lineStart, index - lineStart).Trim();
            lineStart = index + 1;

            if (line.IsEmpty)
            {
                continue;
            }

            kept.Add(line.ToString());
        }

        if (kept.Count == 0)
        {
            return null;
        }

        var excerpt = string.Join('\n', kept);
        if (excerpt.Length <= MaxCharacters)
        {
            return excerpt;
        }

        // The text is cut mid-line, so make the truncation visible instead of ending mid-word.
        var cut = excerpt[..MaxCharacters].TrimEnd();
        return cut + TruncationEllipsis;
    }

    private const int MaxSampleCharacters = 2048;

    private const char TruncationEllipsis = '\u2026';

    private static readonly HashSet<string> PreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown",
    };
}
