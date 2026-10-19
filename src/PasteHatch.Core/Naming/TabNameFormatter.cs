using System.Globalization;

namespace PasteHatch.Core;

/// <summary>
/// Produces the default basket/material names from a moment in time, e.g. "Пн, 13:02".
/// Russian culture is fixed on purpose: the brief specifies this exact shape and the app UI
/// is Russian, so the name must not change with the operating-system display language.
/// </summary>
public sealed class TabNameFormatter
{
    public static readonly TabNameFormatter Default = new();

    private readonly CultureInfo _culture;

    public TabNameFormatter()
        : this(CultureInfo.GetCultureInfo("ru-RU"))
    {
    }

    public TabNameFormatter(CultureInfo culture) => _culture = culture;

    public string Format(DateTimeOffset moment)
    {
        var local = moment.LocalDateTime;
        var weekday = _culture.DateTimeFormat.AbbreviatedDayNames[(int)local.DayOfWeek];
        var capitalized = Capitalize(weekday);
        return $"{capitalized}, {local.ToString("HH:mm", _culture)}";
    }

    /// <summary>
    /// Default base name (without extension) for a note, pasted text or image. Colon is legal in
    /// a Windows tab title but not in a file name (NTFS treats it as an alternate-data-stream
    /// separator), so the file variant replaces every character forbidden by the filesystem:
    /// "Пн, 13:02" becomes "Пн, 13-02".
    /// </summary>
    public string FormatAsFileName(DateTimeOffset moment) => SanitizeFileName(Format(moment));

    /// <summary>Replaces characters that <see cref="Path.GetInvalidFileNameChars"/> forbids with '-'.</summary>
    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (Array.IndexOf(invalid, characters[i]) >= 0)
            {
                characters[i] = '-';
            }
        }

        return new string(characters);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length == 1
            ? value.ToUpperInvariant()
            : string.Concat(value[..1].ToUpperInvariant(), value.AsSpan(1));
    }
}
