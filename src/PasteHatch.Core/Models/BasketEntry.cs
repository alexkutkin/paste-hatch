namespace PasteHatch.Core;

/// <summary>
/// One item in the current basket folder: a subfolder or a material file. The file system stays
/// the source of truth; this is a snapshot taken when the folder is read.
/// </summary>
public sealed record BasketEntry(
    string Name,
    string FullPath,
    string RelativePath,
    bool IsFolder,
    MaterialKind Kind)
{
    public string TypeLabel => IsFolder ? "Папка" : MaterialKindLabels.For(Kind);

    public long Size { get; init; }

    public DateTimeOffset Modified { get; init; }
}
