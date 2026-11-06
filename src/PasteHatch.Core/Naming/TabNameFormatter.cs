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
