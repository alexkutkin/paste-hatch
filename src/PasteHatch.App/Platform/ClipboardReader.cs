using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// Reads the Windows clipboard once per Ctrl+V and normalizes it into a
/// <see cref="PastePayload"/>. The clipboard is retried for a short while when another
/// process holds it open (<see cref="System.Runtime.InteropServices.ExternalException"/>); any other failure degrades to
/// <see cref="PastePayload.None"/> because pasting must never surface an exception.
/// </summary>
public sealed class ClipboardReader : IClipboardReader
{
    public PastePayload Read()
    {
        try
        {
            return ClipboardAccess.Retry(ReadClipboard);
        }
        catch (Exception)
        {
            // Clipboard failures leave nothing pasteable; writes report their errors instead.
            return PastePayload.None;
        }
    }

    private static PastePayload ReadClipboard()
    {
        var data = Clipboard.GetDataObject();
        if (data is null)
        {
            return PastePayload.None;
        }

        if (data.GetDataPresent(DataFormats.FileDrop, autoConvert: true)
            && data.GetData(DataFormats.FileDrop, autoConvert: true) is string[] { Length: > 0 } droppedPaths)
        {
            var paths = new List<string>(droppedPaths.Length);
            foreach (var path in droppedPaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }

            if (paths.Count > 0)
            {
                return PastePayload.FromFiles(paths);
            }
        }

        // GetImage understands every bitmap flavor WinForms supports (CF_BITMAP, CF_DIB, PNG
        // data objects, ...), which is what screenshots and "Copy image" from browsers use.
        using (var image = Clipboard.GetImage())
        {
            if (image is not null)
            {
                using var pngStream = new MemoryStream();
                image.Save(pngStream, ImageFormat.Png);
                return PastePayload.FromImage(pngStream.ToArray());
            }
        }

        if (data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true)
            && data.GetData(DataFormats.UnicodeText, autoConvert: true) is string { Length: > 0 } unicodeText)
        {
            return PastePayload.FromText(unicodeText);
        }

        if (data.GetDataPresent(DataFormats.Text, autoConvert: true)
            && data.GetData(DataFormats.Text, autoConvert: true) is string { Length: > 0 } ansiText)
        {
            return PastePayload.FromText(ansiText);
        }

        return PastePayload.None;
    }
}
