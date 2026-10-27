using System.Globalization;

namespace PasteHatch.Core;

/// <summary>
/// Produces the default basket/material names from a moment in time, e.g. "Пн, 13:02" or
/// "Mon, 13:02", following the UI culture the app passes in.
/// </summary>
public sealed class TabNameFormatter
{
    private readonly Func<CultureInfo> _culture;

    public TabNameFormatter()
        : this(() => CultureInfo.CurrentUICulture)
    {
    }

    public TabNameFormatter(CultureInfo culture)
        : this(() => culture)
    {
    }

    /// <summary>Resolves the culture on every call, so a live language switch is honoured.</summary>
    public TabNameFormatter(Func<CultureInfo> culture) => _culture = culture;

    public string Format(DateTimeOffset moment)
    {
        var culture = _culture();
        var local = moment.LocalDateTime;
        var weekday = culture.DateTimeFormat.AbbreviatedDayNames[(int)local.DayOfWeek];
        var capitalized = Capitalize(weekday);
        return $"{capitalized}, {local.ToString("HH:mm", culture)}";
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
