namespace PasteHatch.Core.Tests;

public sealed class CopiedPathHistoryTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void CopyPath_RecordsSuccessfulRootFolderAndFileEventsAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var folder = workspace.CreateFolder("Заметки", "")!;
        File.WriteAllText(Path.Combine(folder.FullPath, "note.txt"), "content");
        var file = Assert.Single(workspace.Files(folder.RelativePath));

        workspace.CopyActiveBasketPath("");
        workspace.CopyActiveBasketPath(folder.RelativePath);
        workspace.CopyEntryPath(file);

        Assert.Equal(new[] { file.FullPath, folder.FullPath, root }, workspace.RecentCopiedPaths);
        Assert.Equal(new[] { file.FullPath, folder.FullPath, root },
            _harness.CreateInitializedWorkspace().RecentCopiedPaths);
        Assert.Equal(new[] { root, folder.FullPath, file.FullPath }, _harness.Shell.CopiedTexts);
    }

    [Fact]
    public void CopyPath_FailurePreservesPreviousHistoryAndRestartState()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        workspace.CopyActiveBasketPath("");
        var root = workspace.RequireActiveTab().DirectoryPath;
        _harness.Shell.CopyTextFailure = new IOException("Clipboard is busy");

        workspace.CopyFilesystemPath(@"C:\unavailable.txt");

        workspace.RequireError();
        Assert.Equal(new[] { root }, workspace.RecentCopiedPaths);
        Assert.Equal(new[] { root }, _harness.CreateInitializedWorkspace().RecentCopiedPaths);
    }

    [Fact]
    public void CopyPath_KeepsFiveEventsIncludingRepeatsAndReplayWithoutChangingBasketOrder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        var second = workspace.CreateAndRequireTab();
        workspace.CopyActiveBasketPath("");
        workspace.ActivateTab(first.Uid);
        workspace.CopyActiveBasketPath("");
        workspace.CopyActiveBasketPath("");
        workspace.CopyFilesystemPath(@"C:\one.txt");
        workspace.CopyFilesystemPath(@"C:\two.txt");
        workspace.CopyFilesystemPath(second.DirectoryPath);

        var expected = new[] { second.DirectoryPath, @"C:\two.txt", @"C:\one.txt", first.DirectoryPath, first.DirectoryPath };
        Assert.Equal(expected, workspace.RecentCopiedPaths);
        Assert.Equal(new[] { first.Uid, second.Uid }, workspace.Tabs.Select(tab => tab.Uid));
        Assert.Equal(first.Uid, workspace.RequireActiveTab().Uid);
        Assert.Equal(expected, _harness.CreateInitializedWorkspace().RecentCopiedPaths);
    }

    [Fact]
    public void CopyPath_PreservesLiteralStalePathsAfterRenameDeleteAndReplay()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var originalRoot = workspace.RequireActiveTab().DirectoryPath;
        var folder = workspace.CreateFolder("Старая папка", "")!;
        File.WriteAllText(Path.Combine(folder.FullPath, "file.txt"), "contents");
        var file = Assert.Single(workspace.Files(folder.RelativePath));
        workspace.CopyEntryPath(file);
        workspace.CopyEntryPath(folder);
        workspace.CopyActiveBasketPath("");

        Assert.True(workspace.DeleteEntry(file));
        Assert.True(workspace.RenameEntry(folder, "New folder"));
        Assert.True(workspace.RenameTab(workspace.RequireActiveTab().Uid, "New basket"));
        var restarted = _harness.CreateInitializedWorkspace();
        Assert.Equal(new[] { originalRoot, folder.FullPath, file.FullPath }, restarted.RecentCopiedPaths);

        restarted.CopyFilesystemPath(file.FullPath);

        Assert.Equal(file.FullPath, _harness.Shell.CopiedTexts.Last());
        Assert.Equal(new[] { file.FullPath, originalRoot, folder.FullPath, file.FullPath }, restarted.RecentCopiedPaths);
        Assert.Empty(_harness.Shell.CopiedFilePathSets);
    }

    [Fact]
    public void OtherOperations_DoNotCreatePathCopyEvents()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        workspace.CreateTab();
        workspace.ActivateTab(first.Uid);
        _harness.Clipboard.Payload = PastePayload.FromText("ordinary content");
        workspace.AddFromClipboard("");
        var text = workspace.RequireSingleFile();
        workspace.CopyEntryContent(text);
        workspace.CopyEntryToClipboard(text, move: false);
        _harness.Clipboard.Payload = PastePayload.FromText("https://example.com/path");
        workspace.AddFromClipboard("");
        var link = workspace.Files("").Single(entry => entry.Kind == MaterialKind.Link);
        workspace.CopyEntryContent(link);
        workspace.RenameEntry(text, "edited");
        workspace.Refresh();

        Assert.Empty(workspace.RecentCopiedPaths);
        Assert.Empty(_harness.CreateInitializedWorkspace().RecentCopiedPaths);
    }

    [Fact]
    public void Replay_LongCyrillicPathRemainsCompletePlainTextAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var path = @"C:\Мои материалы\" + new string('я', 140) + @"\конечный файл & заметки.txt";

        workspace.CopyFilesystemPath(path);

        Assert.Equal(path, Assert.Single(_harness.Shell.CopiedTexts));
        Assert.Equal(path, Assert.Single(_harness.CreateInitializedWorkspace().RecentCopiedPaths));
        Assert.Empty(_harness.Shell.CopiedFilePathSets);
    }
}
