namespace PasteHatch.Core;

/// <summary>Thin wrapper the app needs from an external clock; keeps naming tests deterministic.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset Now => DateTimeOffset.Now;
}
