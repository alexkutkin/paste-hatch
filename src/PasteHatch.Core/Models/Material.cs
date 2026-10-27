namespace PasteHatch.Core;

/// <summary>Supported material categories, used for the icon and the type label in the list.</summary>
public enum MaterialKind
{
    Image,
    Text,
    Link,
    File,
}

/// <summary>One file stored inside a basket folder. The file on disk is the source of truth.</summary>
public sealed record Material(string FileName, string FullPath, MaterialKind Kind)
{
    /// <summary>Path relative to the basket root, so files in subfolders identify themselves.</summary>
    public string RelativePath { get; init; } = FileName;

    public long Size { get; init; }

    public DateTimeOffset Modified { get; init; }
}
