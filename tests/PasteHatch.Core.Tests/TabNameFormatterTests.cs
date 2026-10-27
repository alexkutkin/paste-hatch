using System.Globalization;

namespace PasteHatch.Core.Tests;

public sealed class TabNameFormatterTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Format_FixedMondayAfternoon_ReturnsShortWeekdayAndTime()
    {
        var formatter = new TabNameFormatter(English);

        var name = formatter.Format(TestMoments.MondayAfternoon);

        Assert.Equal("Mon, 13:02", name);
    }

    [Fact]
    public void Format_JustPastThursdayMidnight_UsesTwoDigitTime()
    {
        var formatter = new TabNameFormatter(English);

        var name = formatter.Format(TestMoments.ThursdayMidnight);

        Assert.Equal("Thu, 00:05", name);
    }

    [Fact]
    public void Format_LateEvening_UsesTwentyFourHourClock()
    {
        var formatter = new TabNameFormatter(English);
        var lateEvening = TestMoments.Local(2026, 10, 19, 23, 59);

        var name = formatter.Format(lateEvening);

        Assert.Equal("Mon, 23:59", name);
    }

    [Fact]
    public void Format_SingleDigitHourAndMinute_AreZeroPadded()
    {
        var formatter = new TabNameFormatter(English);
        var morning = TestMoments.Local(2026, 10, 19, 9, 5);

        var name = formatter.Format(morning);

        Assert.Equal("Mon, 09:05", name);
    }

    [Fact]
    public void FormatAsFileName_ReplacesColonWithDash()
    {
        var formatter = new TabNameFormatter(English);

        Assert.Equal("Mon, 13:02", formatter.Format(TestMoments.MondayAfternoon));
        Assert.Equal("Mon, 13-02", formatter.FormatAsFileName(TestMoments.MondayAfternoon));
    }

    [Fact]
    public void FormatAsFileName_LateEvening_ReturnsFilesystemSafeName()
    {
        var formatter = new TabNameFormatter(English);
        var lateEvening = TestMoments.Local(2026, 10, 19, 23, 59);

        var fileNameBase = formatter.FormatAsFileName(lateEvening);

        Assert.Equal("Mon, 23-59", fileNameBase);
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
        Assert.Equal("note (2)", TabNameFormatter.SanitizeFileName("note (2)"));
        Assert.Equal("Mon, 13-02", TabNameFormatter.SanitizeFileName("Mon, 13-02"));
    }
}
