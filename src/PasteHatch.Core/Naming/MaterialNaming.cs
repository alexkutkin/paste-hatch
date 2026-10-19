using System.Text;

namespace PasteHatch.Core;

/// <summary>
/// Naming rules for material files inside a basket: collision-free suffixes, the fixed
/// ".txt"/".url"/".png" mapping for pasted content, URL detection and rename validation.
/// </summary>
public static class MaterialNaming
{
    public const int MaxBaseNameLength = 160;

    /// <summary>Returns a file name that does not exist yet in <paramref name="directory"/>.</summary>
    public static string UniqueFileName(string directory, string desiredFileName)
    {
        var extension = Path.GetExtension(desiredFileName);
        var baseName = Path.GetFileNameWithoutExtension(desiredFileName);

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "Материал";
        }

        var candidate = desiredFileName;
        var counter = 2;

        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName} ({counter}){extension}";
            counter++;

            if (counter > 9999)
            {
                throw new IOException($"Не удалось подобрать свободное имя файла для «{desiredFileName}».");
            }
        }

        return candidate;
    }

    public static string DefaultFileName(string directory, string format, string extension)
    {
        // Defence in depth: callers normally pass TabNameFormatter.FormatAsFileName, but a colon
        // anywhere in the desired name would silently create an NTFS alternate data stream
        // instead of a file, so the base is sanitized for the filesystem here as well.
        var desired = $"{TabNameFormatter.SanitizeFileName(format)}{extension}";
        return UniqueFileName(directory, desired);
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
        => $"[InternetShortcut]{AuxiliaryLineBreak}URL={link.OriginalString}{AuxiliaryLineBreak}";

    /// <summary>
    /// Validates the editable part of a file name (extension excluded) before a rename.
    /// Returns the trimmed value or null with a human-readable Russian error.
    /// </summary>
    public static string? ValidateRenameBase(string? input, string extension, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Имя файла не может быть пустым.";
            return null;
        }

        // Checked on the raw input: Trim() would otherwise make trailing spaces disappear
        // silently instead of telling the user why the name is not accepted.
        if (input.EndsWith(' ') || input.EndsWith('.'))
        {
            error = "Имя файла не может заканчиваться точкой или пробелом.";
            return null;
        }

        var trimmed = input.Trim();

        if (trimmed.Length > MaxBaseNameLength)
        {
            error = $"Имя файла не должно превышать {MaxBaseNameLength} символов.";
            return null;
        }

        var invalid = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalid) >= 0)
        {
            error = "Имя файла содержит недопустимые символы: \\ / : * ? \" < > |.";
            return null;
        }

        if (trimmed.EndsWith('.'))
        {
            error = "Имя файла не может заканчиваться точкой.";
            return null;
        }

        if (IsReservedDeviceName(trimmed))
        {
            error = $"«{trimmed}» — служебное имя Windows, его нельзя использовать.";
            return null;
        }

        var fullLength = trimmed.Length + extension.Length;
        if (fullLength > 255)
        {
            error = "Слишком длинное имя файла.";
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
            _ => false,
        };
    }

    private static readonly string AuxiliaryLineBreak = "\r\n";

    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
