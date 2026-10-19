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
    public string TypeLabel => MaterialKindLabels.For(Kind);
}

public static class MaterialKindLabels
{
    public static string For(MaterialKind kind) => kind switch
    {
        MaterialKind.Image => "Изображение",
        MaterialKind.Text => "Текст",
        MaterialKind.Link => "Ссылка",
        _ => "Файл",
    };
}
