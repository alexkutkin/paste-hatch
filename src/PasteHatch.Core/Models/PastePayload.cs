namespace PasteHatch.Core;

/// <summary>What a single Ctrl+V produced, already normalized by the platform layer.</summary>
public enum PasteContentKind
{
    None,
    Files,
    Image,
    Text,
}

/// <summary>
/// Clipboard contents in a framework-neutral shape. PNG bytes are used for images because
/// that is what gets stored on disk; text may later be classified as a link by the workspace.
/// </summary>
public sealed class PastePayload
{
    public static readonly PastePayload None = new(PasteContentKind.None);

    private PastePayload(
        PasteContentKind kind,
        IReadOnlyList<string>? files = null,
        byte[]? imagePng = null,
        string? text = null,
        Guid? localTransferToken = null)
    {
        Kind = kind;
        Files = files ?? [];
        ImagePng = imagePng;
        Text = text;
        LocalTransferToken = localTransferToken;
    }

    public PasteContentKind Kind { get; }

    public IReadOnlyList<string> Files { get; }

    public byte[]? ImagePng { get; }

    public string? Text { get; }

    /// <summary>Identity written with a local FileDrop; absent for ordinary external imports.</summary>
    public Guid? LocalTransferToken { get; }

    public static PastePayload FromFiles(IReadOnlyList<string> paths, Guid? localTransferToken = null) =>
        new(PasteContentKind.Files, files: paths, localTransferToken: localTransferToken);

    public static PastePayload FromImage(byte[] pngBytes) => new(PasteContentKind.Image, imagePng: pngBytes);

    public static PastePayload FromText(string text) => new(PasteContentKind.Text, text: text);
}
