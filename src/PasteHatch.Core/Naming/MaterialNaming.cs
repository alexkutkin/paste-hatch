using System.Text;

namespace PasteHatch.Core;

/// <summary>
/// Naming rules for material files inside a basket: collision-free suffixes, the fixed
/// ".txt"/".url"/".png" mapping for pasted content, URL detection and rename validation.
/// </summary>
public static class MaterialNaming
{
    public const int MaxBaseNameLength = 160;

    public static string UniqueFileName(string directory, string desiredFileName, ITextProvider text)
    {
        var stem = Path.GetFileNameWithoutExtension(desiredFileName);
        if (string.IsNullOrEmpty(stem)) stem = text.Get("Name_DefaultMaterial");
        return UniqueName(directory, desiredFileName, stem, Path.GetExtension(desiredFileName),
            "Error_UniqueFileName", text);
    }

    public static string UniqueDirectoryName(string directory, string desiredName, ITextProvider text)
        => UniqueName(directory, desiredName, desiredName, string.Empty, "Error_UniqueFolderName", text);

    private static string UniqueName(string directory, string desiredName, string stem, string extension,
        string errorKey, ITextProvider text)
    {
        var candidate = desiredName;
        for (var counter = 2; File.Exists(Path.Combine(directory, candidate)) ||
            Directory.Exists(Path.Combine(directory, candidate)); counter++)
        {
            if (counter > 9999) throw new IOException(text.Format(errorKey, desiredName));
            candidate = $"{stem} ({counter}){extension}";
        }
        return candidate;
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (Array.IndexOf(invalid, characters[i]) >= 0) characters[i] = '-';
        }
        return new string(characters);
    }

    public static string DefaultFileName(string directory, string format, string extension, ITextProvider text)
    {
        // A colon in a Windows display name would create an NTFS alternate data stream.
        var desired = $"{SanitizeFileName(format)}{extension}";
        return UniqueFileName(directory, desired, text);
    }

    /// <summary>Names raw pasted text from its first five whitespace-separated words.</summary>
    public static string PastedTextFileName(string directory, string content, ITextProvider text)
    {
        var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(5);
        var baseName = SanitizeFileName(string.Join('_', words));
        baseName = baseName[..Math.Min(baseName.Length, MaxBaseNameLength)].TrimEnd(' ', '.');
        if (baseName.Length > 0 && char.IsHighSurrogate(baseName[^1]))
        {
            baseName = baseName[..^1];
        }

        if (baseName.Trim('-', '_', '.').Length == 0)
        {
            baseName = text.Get("Name_DefaultMaterial");
        }

        if (IsReservedDeviceName(baseName))
        {
            baseName = "_" + baseName[..Math.Min(baseName.Length, MaxBaseNameLength - 1)];
            if (char.IsHighSurrogate(baseName[^1]))
            {
                baseName = baseName[..^1];
            }

            baseName = baseName.TrimEnd(' ', '.');
        }

        return UniqueFileName(directory, $"{baseName}.txt", text);
    }

    public static bool TryGetLink(string? text, out Uri? link)
    {
        link = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.IndexOfAny(['\r', '\n', '\t']) >= 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        link = uri;
        return true;
    }

    /// <summary>Serialized Windows Internet Shortcut (.url) contents for a link.</summary>
    public static string CreateInternetShortcutContent(Uri link)
        => $"[InternetShortcut]\r\nURL={link.OriginalString}\r\n";

    /// <summary>
    /// Validates the editable part of a file name (extension excluded) before a rename.
    /// Returns the trimmed value or null with a human-readable error.
    /// </summary>
    public static string? ValidateRenameBase(string? input, string extension, ITextProvider text, out string? error)
        => ValidateName(input, extension, isFolder: false, text, out error);

    /// <summary>Validates a folder name with folder-specific error messages.</summary>
    public static string? ValidateFolderName(string? input, ITextProvider text, out string? error)
        => ValidateName(input, string.Empty, isFolder: true, text, out error);

    private static string? ValidateName(string? input, string extension, bool isFolder, ITextProvider text, out string? error)
    {
        error = null;
        var prefix = isFolder ? "FolderName" : "FileName";

        if (string.IsNullOrWhiteSpace(input))
        {
            error = text.Get($"Error_{prefix}Empty");
            return null;
        }

        // Checked on the raw input: Trim() would otherwise make trailing spaces disappear
        // silently instead of telling the user why the name is not accepted.
        if (input.EndsWith(' ') || input.EndsWith('.'))
        {
            error = text.Get($"Error_{prefix}Trailing");
            return null;
        }

        var trimmed = input.Trim();

        if (trimmed.Length > MaxBaseNameLength)
        {
            error = text.Format($"Error_{prefix}TooLong", MaxBaseNameLength);
            return null;
        }

        var invalid = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalid) >= 0)
        {
            error = text.Get($"Error_{prefix}InvalidChars");
            return null;
        }

        if (IsReservedDeviceName(trimmed))
        {
            error = text.Format("Error_NameReserved", trimmed);
            return null;
        }

        var fullLength = trimmed.Length + extension.Length;
        if (fullLength > 255)
        {
            error = text.Get($"Error_{prefix}TotalTooLong");
            return null;
        }

        return trimmed;
    }

    private static bool IsReservedDeviceName(string name)
    {
        var candidate = name.TrimEnd(' ', '.');
        var dot = candidate.IndexOf('.');
        if (dot >= 0)
        {
            candidate = candidate[..dot];
        }

        return candidate.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => true,
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" => true,
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9" => true,
            "COM¹" or "COM²" or "COM³" or "LPT¹" or "LPT²" or "LPT³" => true,
            _ => false,
        };
    }

    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
