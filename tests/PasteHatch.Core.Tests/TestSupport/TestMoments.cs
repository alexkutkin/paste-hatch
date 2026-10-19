namespace PasteHatch.Core.Tests;

/// <summary>
/// Fixed local moments used for clock-driven naming. The dates are deliberately chosen so the
/// weekday is known: 2026-10-19 is a Monday and 2026-10-22 is a Thursday. Building the
/// <see cref="DateTimeOffset"/> from a <see cref="DateTimeKind.Local"/> value means the wall-clock
/// time is preserved on every machine time zone, so formatted names are deterministic.
/// </summary>
internal static class TestMoments
{
    public static DateTimeOffset MondayAfternoon { get; } = Local(2026, 10, 19, 13, 2);

    public static DateTimeOffset TuesdayAfternoon { get; } = Local(2026, 10, 20, 14, 30);

    public static DateTimeOffset WednesdayMorning { get; } = Local(2026, 10, 21, 9, 15);

    /// <summary>Just past midnight on a Thursday; the short name must be "Чт, 00:05".</summary>
    public static DateTimeOffset ThursdayMidnight { get; } = Local(2026, 10, 22, 0, 5);

    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
        => new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));
}
