namespace PasteHatch.Core;

/// <summary>
/// Reads the Windows clipboard once per Ctrl+V and returns a platform-neutral payload.
/// Implementations must retry when another process holds the clipboard open and must never
/// throw for "clipboard is empty" — that is <see cref="PastePayload.None"/>.
/// </summary>
public interface IClipboardReader
{
    PastePayload Read();
}
