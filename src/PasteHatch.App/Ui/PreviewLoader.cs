using Microsoft.UI.Xaml.Media.Imaging;
using PasteHatch.Core;
using Windows.Storage;

namespace PasteHatch.Ui;

/// <summary>What the preview pane shows for one entry.</summary>
public sealed record PreviewContent(string? Text = null, BitmapImage? Image = null);

/// <summary>
/// Loads preview content for tree entries. One request at a time: a newer call cancels the older
/// one and its result is never delivered, so the preview always belongs to the latest selection.
/// </summary>
public sealed class PreviewLoader
{
    private const int MaxTextCharacters = 64 * 1024;
    private const string TruncationEllipsis = "\u2026";

    private CancellationTokenSource? _current;

    /// <summary>Reads the content of a material; returns null when a newer load superseded this one.</summary>
    public async Task<PreviewContent?> LoadAsync(string fullPath, MaterialKind kind)
    {
        var request = new CancellationTokenSource();
        _current?.Cancel();
        _current?.Dispose();
        _current = request;

        try
        {
            if (kind == MaterialKind.Image)
            {
                var image = await LoadImageAsync(fullPath, request.Token);
                return request.IsCancellationRequested ? null : new PreviewContent(Image: image);
            }

            var text = ReadText(fullPath);
            return request.IsCancellationRequested ? null : new PreviewContent(Text: text);
        }
        catch (Exception) when (request.IsCancellationRequested)
        {
            // A superseded load must not surface errors either.
            return null;
        }
    }

    /// <summary>Abandons the load in flight; the content of the next selection wins.</summary>
    public void Cancel()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }

    private static async Task<BitmapImage> LoadImageAsync(string fullPath, CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromPathAsync(fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await file.OpenReadAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static string ReadText(string fullPath)
    {
        var text = MaterialPreview.ReadText(fullPath, MaxTextCharacters);
        return text.Length == MaxTextCharacters ? text + Environment.NewLine + TruncationEllipsis : text;
    }
}
