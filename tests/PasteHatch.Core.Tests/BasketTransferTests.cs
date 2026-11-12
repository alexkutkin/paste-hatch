namespace PasteHatch.Core.Tests;

public sealed class BasketTransferTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void CopySelection_CanBePastedRepeatedlyWithoutLosingIntent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: false);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        Assert.Equal(3, Directory.GetFiles(root).Length);
        Assert.All(Directory.GetFiles(root), p => Assert.Equal("original", File.ReadAllText(p)));
    }

    [Fact]
    public void CutSelection_IsHandledByWorkspaceWithoutWindowState()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        Directory.CreateDirectory(Path.Combine(root, "dest"));
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries(""), e => !e.IsFolder), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);

        Assert.Equal(1, workspace.AddFromClipboard("dest"));
        Assert.False(File.Exists(source));
        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "dest", "note.txt")));
    }

    [Fact]
    public void ClipboardReplacement_DiscardsOldCopyIntent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: false);
        _harness.Clipboard.Payload = PastePayload.FromText("replacement");
        workspace.AddFromClipboard(string.Empty);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
        Assert.False(File.Exists(Path.Combine(root, "note (2).txt")));
    }

    [Fact]
    public void PartialImport_PublishesFinalErrorAndKeepsSuccessfulFiles()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("source.txt", "payload");
        var changes = new List<(bool Error, WorkspaceChangeKind Kind)>();
        workspace.Changed += (_, e) => changes.Add((workspace.StatusIsError, e.Kind));
        string[] paths = [source, Path.Combine(_harness.Temp.SourceRoot, "missing.txt")];

        var count = workspace.AddFiles(paths, string.Empty);

        Assert.Equal(1, count);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(root, "source.txt")));
        Assert.Single(changes);
        Assert.Equal((true, WorkspaceChangeKind.Contents), changes[0]);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void CopyFileContent_AfterCut_ReplacesMoveIntentWithCopy()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "archive.zip");
        File.WriteAllText(source, "payload");
        Directory.CreateDirectory(Path.Combine(root, "dest"));
        var entry = Assert.Single(workspace.GetEntries(""), item => !item.IsFolder);
        workspace.CopyEntryToClipboard(entry, move: true);
        workspace.CopyEntryContent(entry);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);

        Assert.Equal(1, workspace.AddFromClipboard("dest"));

        FileAssert.HasText(source, "payload");
        FileAssert.HasText(Path.Combine(root, "dest", "archive.zip"), "payload");
    }

    [Fact]
    public void ExternalCopyOfTheSamePaths_DoesNotReuseLocalCutIntent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasText(source, "original");
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void SuccessfulCut_DoesNotMoveANewFileAtTheOldPathOnRepeatPaste()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        Directory.CreateDirectory(Path.Combine(root, "dest"));
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries(""), e => !e.IsFolder), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        Assert.Equal(1, workspace.AddFromClipboard("dest"));
        File.WriteAllText(source, "new file");

        Assert.Equal(0, workspace.AddFromClipboard("dest"));

        FileAssert.HasText(source, "new file");
        Assert.False(File.Exists(Path.Combine(root, "dest", "note (2).txt")));
    }

    [Fact]
    public void CrossBasketFolderCut_UsesDirectMoveWithoutRecyclingTheSource()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "note.txt"), "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        _harness.Shell.DirectoryRecycleFailure = new IOException("Recycle Bin unavailable.");

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.False(Directory.Exists(source));
        FileAssert.HasText(Path.Combine(target, "folder", "note.txt"), "original");
        Assert.Empty(_harness.Shell.RecycledDirectories);
    }

    [Fact]
    public void ClipboardReadFailure_PreservesThePendingCut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        _harness.Clipboard.FailureToThrow = new IOException("Clipboard unavailable.");

        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
        workspace.RequireError();
        FileAssert.HasText(source, "original");
        _harness.Clipboard.FailureToThrow = null;

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        Assert.False(File.Exists(source));
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void SuccessfulEmptyRead_InvalidatesThePendingCut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        var oldSnapshot = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        _harness.Clipboard.Payload = PastePayload.None;
        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        _harness.Clipboard.Payload = oldSnapshot;

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasText(source, "original");
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void FailedClipboardWrite_PreservesThePreviousCutSnapshot()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "first.txt");
        File.WriteAllText(source, "first");
        File.WriteAllText(Path.Combine(root, "second.txt"), "second");
        workspace.CopyEntryToClipboard(workspace.GetEntries("").Single(e => e.Name == "first.txt"), move: true);
        var oldSnapshot = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        _harness.Shell.CopyTextFailure = new IOException("Clipboard unavailable.");
        workspace.CopyEntryToClipboard(workspace.GetEntries("").Single(e => e.Name == "second.txt"), move: false);
        workspace.RequireError();
        _harness.Shell.CopyTextFailure = null;
        _harness.Clipboard.Payload = oldSnapshot;
        var target = workspace.CreateAndRequireTab().DirectoryPath;

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.False(File.Exists(source));
        FileAssert.HasText(Path.Combine(root, "second.txt"), "second");
        FileAssert.HasText(Path.Combine(target, "first.txt"), "first");
    }

    [Fact]
    public void SameFolderCutNoOp_DoesNotConsumeThePendingMove()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);

        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.False(File.Exists(source));
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void FailedMoveWithoutEffects_CanBeRetriedAfterTheSourceIsUnlocked()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        using (var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
            workspace.RequireError();
            Assert.Empty(Directory.GetFiles(target));
        }

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.False(File.Exists(source));
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void UnknownLocalToken_DoesNotAuthorizeAMove()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], Guid.NewGuid());
        var target = workspace.CreateAndRequireTab().DirectoryPath;

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasText(source, "original");
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void SuccessfulPathCopy_InvalidatesCutBeforeAnOldClipboardSnapshotIsRestored()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        var oldSnapshot = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);

        workspace.CopyFilesystemPath(source);
        _harness.Clipboard.Payload = oldSnapshot;
        var target = workspace.CreateAndRequireTab().DirectoryPath;
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasText(source, "original");
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void FailedPathCopy_PreservesThePendingCut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: true);
        var snapshot = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        _harness.Shell.CopyTextFailure = new IOException("Clipboard unavailable.");
        workspace.CopyFilesystemPath(source);
        workspace.RequireError();
        _harness.Shell.CopyTextFailure = null;
        _harness.Clipboard.Payload = snapshot;
        var target = workspace.CreateAndRequireTab().DirectoryPath;

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.False(File.Exists(source));
        FileAssert.HasText(Path.Combine(target, "note.txt"), "original");
    }

    [Fact]
    public void UncertainSourceRemoval_IsNotRetriedByTheSameCut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var source = _harness.Temp.CreateSourceDirectory("folder");
        File.WriteAllText(Path.Combine(source, "first.txt"), "first");
        File.WriteAllText(Path.Combine(source, "second.txt"), "second");
        // The workspace's public transfer interface also supports explicit external moves.
        var entry = new BasketEntry("folder", source, "folder", IsFolder: true, MaterialKind.File);
        workspace.CopyEntryToClipboard(entry, move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source], _harness.Shell.LastFileTransferToken);
        var removalAttempts = 0;
        _harness.Shell.RecycleDirectoryOverride = path =>
        {
            removalAttempts++;
            File.Delete(Path.Combine(path, "first.txt"));
            throw new IOException("Source removal interrupted.");
        };
        var root = workspace.RequireActiveTab().DirectoryPath;

        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));
        var error = workspace.RequireError();
        Assert.Contains(source, error);
        Assert.Contains(Path.Combine(root, "folder"), error);
        Assert.Equal(0, workspace.AddFromClipboard(string.Empty));

        Assert.Equal(1, removalAttempts);
        FileAssert.HasText(Path.Combine(source, "second.txt"), "second");
        FileAssert.HasText(Path.Combine(root, "folder", "first.txt"), "first");
        FileAssert.HasText(Path.Combine(root, "folder", "second.txt"), "second");
        Assert.False(Directory.Exists(Path.Combine(root, "folder (2)")));
    }
}
