namespace PasteHatch.Core.Tests;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = TestMoments.MondayAfternoon;
}
