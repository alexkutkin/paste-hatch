namespace PasteHatch.Core;

internal enum FileTransferIntent { Import, Copy, Move }

/// <summary>Owns collision rules, disk effects and per-item results, without clipboard lifecycle.</summary>
internal sealed class FileTransfer(string workspaceRoot, IShellService shell, ITextProvider text)
{
    private static readonly EnumerationOptions CopyEntriesOptions = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };
    public TransferResult Execute(IReadOnlyList<string> paths, string basket, string target, FileTransferIntent intent)
    {
        var items = new List<TransferItemResult>();
        foreach (var path in paths)
        {
            string? destination = null;
            var isFolder = false;
            var directMove = false;
            var removingSource = false;
            try
            {
                var attributes = File.GetAttributes(path);
                isFolder = attributes.HasFlag(FileAttributes.Directory);

                if (intent == FileTransferIntent.Move && SameDirectory(
                    Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), target))
                {
                    items.Add(new(path, null, isFolder, TransferOutcome.NoOp));
                    continue;
                }
                if (intent == FileTransferIntent.Import && IsSameOrInside(path, basket))
                {
                    items.Add(new(path, null, isFolder, TransferOutcome.Skipped));
                    continue;
                }
                if (isFolder && attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException(text.Get("Error_LinkNotCopied"));
                if (isFolder && IsSameOrInside(target, path))
                    throw new IOException(text.Get("Error_FolderIntoItself"));

                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (name.Length == 0) throw new IOException(text.Get("Error_DriveRoot"));
                destination = Path.Combine(target, isFolder
                    ? MaterialNaming.UniqueDirectoryName(target, name, text)
                    : MaterialNaming.UniqueFileName(target, name, text));
                if (isFolder)
                {
                    if (intent == FileTransferIntent.Move && CanMoveDirectly(path, basket, target))
                    {
                        directMove = true;
                        Directory.Move(path, destination);
                    }
                    if (!directMove)
                    {
                        CopyDirectory(path, destination);
                        if (intent == FileTransferIntent.Move)
                        {
                            removingSource = true;
                            shell.RecycleDirectory(path);
                        }
                    }
                }
                else
                {
                    if (intent == FileTransferIntent.Move)
                    {
                        directMove = true;
                        File.Move(path, destination);
                    }
                    else File.Copy(path, destination, overwrite: false);
                }
                items.Add(new(path, destination, isFolder,
                    intent == FileTransferIntent.Move ? TransferOutcome.Moved : TransferOutcome.Copied));
            }
            catch (Exception ex)
            {
                var outcome = TransferOutcome.Failed;
                if (destination is not null)
                {
                    var destinationState = Observe(destination);
                    // Recycling may have removed only part of a source folder. Existing source
                    // and destination names alone cannot establish what remains safe to retry.
                    if (removingSource || destinationState == DiskPresence.Unreadable)
                        outcome = TransferOutcome.Unknown;
                    else if (destinationState == DiskPresence.Present)
                        outcome = directMove ? TransferOutcome.Unknown : TransferOutcome.Partial;
                    else if (directMove && Observe(path) != DiskPresence.Present)
                        outcome = TransferOutcome.Unknown;
                }
                var error = outcome switch
                {
                    TransferOutcome.Partial => text.Format("Error_TransferPartial", destination, ex.Message),
                    TransferOutcome.Unknown => text.Format("Error_TransferUnknown", path, destination, ex.Message),
                    _ => ex.Message,
                };
                items.Add(new(path, destination, isFolder, outcome, error));
            }
        }
        return new TransferResult(items);
    }

    private bool CanMoveDirectly(string source, string basket, string target)
    {
        if (IsSameOrInside(source, basket)) return true;
        if (!IsSameOrInside(source, workspaceRoot) || !IsSameOrInside(target, workspaceRoot)) return false;
        return HasOrdinaryAncestors(Path.GetDirectoryName(source)!) && HasOrdinaryAncestors(target);
    }

    private bool HasOrdinaryAncestors(string directory)
    {
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (SameDirectory(ancestor.FullName, workspaceRoot)) return true;
            // A saved basket may have been replaced externally by a junction to another volume.
            // Keep its established copy/recycle behavior without attempting a direct move first.
            if (File.GetAttributes(ancestor.FullName).HasFlag(FileAttributes.ReparsePoint)) return false;
        }
        return false;
    }

    private enum DiskPresence { Present, Missing, Unreadable }

    private static DiskPresence Observe(string path)
    {
        try { File.GetAttributes(path); return DiskPresence.Present; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return DiskPresence.Missing; }
        catch (Exception) { return DiskPresence.Unreadable; }
    }

    internal static bool SameDirectory(string? left, string right) => left is not null &&
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string path, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", CopyEntriesOptions))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", CopyEntriesOptions))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

internal enum TransferOutcome { Skipped, NoOp, Copied, Moved, Failed, Partial, Unknown }

internal sealed record TransferItemResult(
    string Source, string? Destination, bool IsFolder, TransferOutcome Outcome, string? Error = null)
{
    public bool Completed => Outcome is TransferOutcome.Copied or TransferOutcome.Moved;
}

internal sealed record TransferResult(IReadOnlyList<TransferItemResult> Items)
{
    public int Completed => Items.Count(item => item.Completed);

    public IReadOnlyList<string> Problems => Items.Where(item => item.Error is not null)
        .Select(item => $"{Path.GetFileName(item.Source)}: {item.Error}").ToArray();

    public string FormatStatus(FileTransferIntent intent, string relativeTarget, ITextProvider text)
    {
        if (Items.Count == 0) return text.Get("Status_NoFilesToAdd");
        var completed = Completed;
        var skipped = Items.Count(item => item.Outcome == TransferOutcome.Skipped);
        var folders = Items.Count(item => item.Completed && item.IsFolder);
        var folder = relativeTarget.Length == 0 ? text.Get("Folder_Root") : relativeTarget;
        return intent switch
        {
            FileTransferIntent.Move => completed == 0
                ? text.Get("Status_NothingMoved")
                : completed == 1
                    ? text.Format("Status_OneItemMoved", folder)
                    : text.Format("Status_ItemsMoved", text.Plural("Count_Items", completed), folder),
            FileTransferIntent.Copy => completed == 0
                ? text.Get("Status_NothingCopied")
                : text.Format("Status_ItemsCopied", text.Plural("Count_Items", completed), folder),
            _ => folders > 0
                ? text.Format("Status_Added", text.Plural("Count_Items", completed))
                : (completed, skipped) switch
                {
                    (0, > 0) => text.Get("Status_AlreadyInBasket"),
                    (0, _) => text.Get("Status_NothingAdded"),
                    (1, 0) => text.Get("Status_OneFileAdded"),
                    (1, _) => text.Get("Status_OneFileAddedPartly"),
                    (_, 0) => text.Format("Status_Added", text.Plural("Count_Files", completed)),
                    _ => text.Format("Status_AddedPartly", text.Plural("Count_Files", completed)),
                },
        };
    }
}
