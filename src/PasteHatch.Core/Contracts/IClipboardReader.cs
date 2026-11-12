namespace PasteHatch.Core;

/// <summary>
/// Reads the Windows clipboard once per Ctrl+V and returns a platform-neutral payload.
/// Implementations retry when another process holds the clipboard open. A successful empty
/// read returns <see cref="PastePayload.None"/>; a failed read throws without implying replacement.
/// </summary>
public interface IClipboardReader
{
    PastePayload Read();
}
