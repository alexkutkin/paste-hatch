namespace PasteHatch.Core.Tests;

/// <summary>
/// Records every shell operation and emulates the two that remove something: recycling really
/// removes the file / folder from disk so the workspace refresh sees the same disk as the app.
/// </summary>
internal sealed class FakeShellService : IShellService
{
    public List<string> OpenedItems { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public List<string> RecycledFiles { get; } = [];

    public List<string> RecycledDirectories { get; } = [];

    public List<string> CopiedTexts { get; } = [];

    public List<string> CopiedFilePathSets { get; } = [];

    public List<bool> CopiedFileMoves { get; } = [];

    public Exception? OpenItemFailure { get; set; }

    public Exception? OpenFolderFailure { get; set; }

    public Exception? FileRecycleFailure { get; set; }

    public Exception? DirectoryRecycleFailure { get; set; }

    public Exception? CopyTextFailure { get; set; }

    public void OpenItem(string path)
    {
        if (OpenItemFailure is not null)
        {
            throw OpenItemFailure;
        }

        OpenedItems.Add(path);
    }

    public void OpenFolder(string path)
    {
        if (OpenFolderFailure is not null)
        {
            throw OpenFolderFailure;
        }

        OpenedFolders.Add(path);
    }

    public void RecycleFile(string path)
    {
        if (FileRecycleFailure is not null)
        {
            throw FileRecycleFailure;
        }

        RecycledFiles.Add(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void RecycleDirectory(string path)
    {
        if (DirectoryRecycleFailure is not null)
        {
            throw DirectoryRecycleFailure;
        }

        RecycledDirectories.Add(path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void CopyText(string text)
    {
        if (CopyTextFailure is not null)
        {
            throw CopyTextFailure;
        }

        CopiedTexts.Add(text);
    }

    public void CopyFilesToClipboard(IReadOnlyList<string> paths, bool move)
    {
        if (CopyTextFailure is not null)
        {
            throw CopyTextFailure;
        }

        CopiedFilePathSets.Add(string.Join('\u001f', paths));
        CopiedFileMoves.Add(move);
    }
}
