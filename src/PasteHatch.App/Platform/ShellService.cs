using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// Windows shell integration: default application launches, Explorer, the Windows Recycle Bin
/// and clipboard writes. Operations that can fail (missing files, locked clipboard, Recycle Bin
/// errors) let their exception propagate so the core layer can report it to the user; only the
/// transient "clipboard is busy" case is retried here.
/// </summary>
public sealed class ShellService : IShellService
{
    private const int ClipboardRetryAttempts = 10;
    private const int ClipboardRetryDelayMilliseconds = 40;

    /// <inheritdoc />
    public void OpenItem(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            Arguments = $"\"{path}\"",
        };
        using var process = Process.Start(startInfo);
    }

    /// <inheritdoc />
    public void RecycleFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // OnlyErrorDialogs: Windows shows a dialog only when the deletion fails, never its own
        // "are you sure?" prompt — PasteHatch already asked with its own task dialog.
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    /// <inheritdoc />
    public void RecycleDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    /// <inheritdoc />
    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (ExternalException) when (attempt < ClipboardRetryAttempts - 1)
            {
                // Another application owns the clipboard for a moment; retry, then surface the error.
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }
        }
    }

    /// <inheritdoc />
    public void CopyFilesToClipboard(IReadOnlyList<string> paths, bool move)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths.ToArray());

        // Preferred DropEffect is what Explorer reads to decide between copy and cut.
        var effect = new MemoryStream(BitConverter.GetBytes(move ? 2u : 1u));
        data.SetData("Preferred DropEffect", effect);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (ExternalException) when (attempt < ClipboardRetryAttempts - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }
        }
    }
}
