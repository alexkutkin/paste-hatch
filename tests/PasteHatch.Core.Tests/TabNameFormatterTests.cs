using System.Globalization;

namespace PasteHatch.Core.Tests;

public sealed class TabNameFormatterTests
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    [Fact]
    public void Format_FixedMondayAfternoon_ReturnsShortWeekdayAndTime()
    {
        var formatter = new TabNameFormatter(Russian);

        var name = formatter.Format(TestMoments.MondayAfternoon);

        Assert.Equal("Пн, 13:02", name);
    }

    [Fact]
    public void Format_WeekdayAbbreviation_HasNoTrailingDot()
    {
        var formatter = new TabNameFormatter(Russian);

        var name = formatter.Format(TestMoments.MondayAfternoon);

        Assert.DoesNotContain(".", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_JustPastThursdayMidnight_UsesTwoDigitTime()
    {
        var formatter = new TabNameFormatter(Russian);

        var name = formatter.Format(TestMoments.ThursdayMidnight);

        Assert.Equal("Чт, 00:05", name);
    }

    [Fact]
    public void Format_LateEvening_UsesTwentyFourHourClock()
    {
        var formatter = new TabNameFormatter(Russian);
        var lateEvening = TestMoments.Local(2026, 10, 19, 23, 59);

        var name = formatter.Format(lateEvening);

        Assert.Equal("Пн, 23:59", name);
    }

    [Fact]
    public void Format_SingleDigitHourAndMinute_AreZeroPadded()
    {
        var formatter = new TabNameFormatter(Russian);
        var morning = TestMoments.Local(2026, 10, 19, 9, 5);

        var name = formatter.Format(morning);

        Assert.Equal("Пн, 09:05", name);
    }

    [Theory]
    [InlineData(19, "Пн")]
    [InlineData(20, "Вт")]
    [InlineData(21, "Ср")]
    [InlineData(22, "Чт")]
    [InlineData(23, "Пт")]
    [InlineData(24, "Сб")]
    [InlineData(25, "Вс")]
    public void Format_EachWeekday_UsesRussianShortName(int dayOfOctober2026, string expectedWeekday)
    {
        var formatter = new TabNameFormatter(Russian);
        var moment = TestMoments.Local(2026, 10, dayOfOctober2026, 13, 2);

        var name = formatter.Format(moment);

        Assert.Equal($"{expectedWeekday}, 13:02", name);
    }

    [Fact]
    public void Format_NameShape_MatchesExpectedPattern()
    {
        var formatter = new TabNameFormatter(Russian);

        var name = formatter.Format(TestMoments.MondayAfternoon);

        Assert.Matches(@"^[А-ЯЁ][а-яё]+, \d{2}:\d{2}$", name);
    }

    [Fact]
    public void FormatAsFileName_ReplacesColonWithDash()
    {
        var formatter = new TabNameFormatter(Russian);

        Assert.Equal("Пн, 13:02", formatter.Format(TestMoments.MondayAfternoon));
        Assert.Equal("Пн, 13-02", formatter.FormatAsFileName(TestMoments.MondayAfternoon));
    }

    [Fact]
    public void FormatAsFileName_LateEvening_ReturnsFilesystemSafeName()
    {
        var formatter = new TabNameFormatter(Russian);
        var lateEvening = TestMoments.Local(2026, 10, 19, 23, 59);

        var fileNameBase = formatter.FormatAsFileName(lateEvening);

        Assert.Equal("Пн, 23-59", fileNameBase);
        Assert.DoesNotContain(':', fileNameBase);
    }

    [Fact]
    public void SanitizeFileName_ReplacesEveryInvalidFileNameCharacterWithDash()
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var input = new string(invalidCharacters);

        var sanitized = TabNameFormatter.SanitizeFileName(input);

        Assert.Equal(new string('-', invalidCharacters.Length), sanitized);
    }

    [Fact]
    public void SanitizeFileName_LeavesAlreadyValidNameUnchanged()
    {
        Assert.Equal("заметка (2)", TabNameFormatter.SanitizeFileName("заметка (2)"));
        Assert.Equal("Пн, 13-02", TabNameFormatter.SanitizeFileName("Пн, 13-02"));
    }

    [Fact]
    public void SanitizeFileName_ForColonForm_ProducesWindowsSafeBase()
    {
        Assert.Equal("Пн, 13-02", TabNameFormatter.SanitizeFileName("Пн, 13:02"));
    }

    [Fact]
    public void SanitizeFileName_ForEmptyString_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, TabNameFormatter.SanitizeFileName(string.Empty));
    }
}
