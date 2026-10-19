namespace PasteHatch.Core;

/// <summary>Maps material file extensions to the kind shown in the list and its icon.</summary>
public static class MaterialClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".csv", ".tsv", ".xml", ".html", ".htm",
        ".css", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".cs", ".py", ".yml", ".yaml", ".ini",
        ".cfg", ".conf", ".toml", ".sql", ".sh", ".bat", ".cmd", ".ps1", ".rtf",
    };

    public static MaterialKind Classify(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
        {
            return MaterialKind.File;
        }

        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialKind.Link;
        }

        if (ImageExtensions.Contains(extension))
        {
            return MaterialKind.Image;
        }

        if (TextExtensions.Contains(extension))
        {
            return MaterialKind.Text;
        }

        return MaterialKind.File;
    }
}
