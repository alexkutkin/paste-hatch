namespace PasteHatch.Core.Tests;

internal sealed class FakeClipboardReader : IClipboardReader
{
    public PastePayload Payload { get; set; } = PastePayload.None;

    public int ReadCount { get; private set; }

    public Exception? FailureToThrow { get; set; }

    public PastePayload Read()
    {
        ReadCount++;
        if (FailureToThrow is not null)
        {
            throw FailureToThrow;
        }

        return Payload;
    }
}
