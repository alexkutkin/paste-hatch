namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceTreeTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void GetEntries_AreSortedFoldersFirstThenFilesWithNaturalNumbers()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "folder10"));
        Directory.CreateDirectory(Path.Combine(directory, "folder2"));
        Directory.CreateDirectory(Path.Combine(directory, "Alpha"));
        File.WriteAllText(Path.Combine(directory, "file10.txt"), "10");
        File.WriteAllText(Path.Combine(directory, "file2.txt"), "2");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "a");

        var entries = workspace.GetEntries(string.Empty);

        Assert.Equal(
            ["Alpha", "folder2", "folder10", "a.txt", "file2.txt", "file10.txt"],
            entries.Select(entry => entry.Name));
        Assert.True(entries[0].IsFolder);
        Assert.False(entries[3].IsFolder);
    }

    [Fact]
    public void FolderPaths_OutsideTheBasket_AreRejected()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.Empty(workspace.GetEntries(".."));
        Assert.Empty(workspace.GetEntries("sub\\..\\.."));
        Assert.Empty(workspace.GetEntries(Path.GetTempPath()));
        Assert.Null(workspace.CreateFolder("new", ".."));
        workspace.RequireError();
    }

    [Fact]
    public void CurrentFolder_PersistsAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub"));
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        navigation.Select(Assert.Single(workspace.GetEntries(string.Empty)));
        workspace.SaveState();

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();

        Assert.Equal("sub", _harness.TreeStateFor(restarted).Reconcile(null, []).Folder);
        Assert.Equal("sub", _harness.ReadSavedState().Tabs[0].SelectedFolder);
    }

    [Fact]
    public void CreateFolder_CreatesFolderAndReportsItInEntries()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        var folder = workspace.CreateFolder("New folder", string.Empty);

        Assert.NotNull(folder);
        Assert.True(Directory.Exists(Path.Combine(directory, "New folder")));
        Assert.Contains(workspace.GetEntries(string.Empty), entry => entry.IsFolder && entry.Name == "New folder");
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void CreateFolder_WhenNameExists_ShowsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub"));

        Assert.Null(workspace.CreateFolder("sub", string.Empty));

        workspace.RequireError();
    }

    [Fact]
    public void CreateFolder_EnsureUnique_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "New folder"));

        var folder = workspace.CreateFolder("New folder", string.Empty, ensureUnique: true);

        Assert.NotNull(folder);
        Assert.Equal("New folder (2)", folder.Name);
        Assert.True(Directory.Exists(Path.Combine(directory, "New folder (2)")));
    }

    [Fact]
    public void RenameEntry_KeepsContentsAndRenamesFolderOnDisk()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "old"));
        File.WriteAllText(Path.Combine(directory, "old", "inside.txt"), "inside");
        var folder = Assert.Single(workspace.Folders(string.Empty));

        Assert.True(workspace.RenameEntry(folder, "new"));

        Assert.False(Directory.Exists(Path.Combine(directory, "old")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "new", "inside.txt"), "inside");
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void RenameEntry_WhenTargetExists_ShowsErrorAndKeepsBoth()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "one"));
        Directory.CreateDirectory(Path.Combine(directory, "two"));
        var folder = Assert.Single(workspace.Folders(string.Empty), entry => entry.Name == "one");

        Assert.False(workspace.RenameEntry(folder, "two"));

        workspace.RequireError();
        Assert.True(Directory.Exists(Path.Combine(directory, "one")));
        Assert.True(Directory.Exists(Path.Combine(directory, "two")));
    }

    [Fact]
    public void DeleteEntry_WhenConfirmed_RecyclesFolderAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "trash"));
        File.WriteAllText(Path.Combine(directory, "trash", "inside.txt"), "inside");
        var folder = Assert.Single(workspace.Folders(string.Empty));

        Assert.True(workspace.DeleteEntry(folder));

        Assert.Contains(directory, _harness.Shell.RecycledDirectories[0]);
        Assert.False(Directory.Exists(Path.Combine(directory, "trash")));
        Assert.Empty(workspace.GetEntries(string.Empty));
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void DeleteEntry_WhenConfirmationDeclined_KeepsFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "keep"));
        var folder = Assert.Single(workspace.Folders(string.Empty));
        _harness.Confirmations.ConfirmFolderResult = false;

        Assert.False(workspace.DeleteEntry(folder));

        Assert.True(Directory.Exists(Path.Combine(directory, "keep")));
        Assert.Empty(_harness.Shell.RecycledDirectories);
    }

    [Fact]
    public void MoveFiles_MovesFileIntoSubfolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "dest"));
        File.WriteAllText(Path.Combine(directory, "note.txt"), "note");
        var file = Assert.Single(workspace.GetEntries(string.Empty), entry => !entry.IsFolder);

        Assert.Equal(1, workspace.MoveFiles([file.FullPath], "dest"));

        Assert.False(File.Exists(Path.Combine(directory, "note.txt")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "dest", "note.txt"), "note");
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void GetEntries_ReturnsFoldersAndFiles()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "sub"));
        File.WriteAllText(Path.Combine(directory, "a.txt"), "a");
        File.WriteAllText(Path.Combine(directory, "sub", "inside.txt"), "inside");

        var rootEntries = workspace.GetEntries(string.Empty);
        var subEntries = workspace.GetEntries("sub");

        Assert.Equal(["sub", "a.txt"], rootEntries.Select(entry => entry.Name));
        Assert.Equal(["inside.txt"], subEntries.Select(entry => entry.Name));
        Assert.True(workspace.HasEntries("sub"));
        Assert.True(workspace.HasEntries(string.Empty));
    }

    [Fact]
    public void CreateFolder_ThenSelect_PersistsTheFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var folder = workspace.CreateFolder("work", string.Empty);

        Assert.NotNull(folder);
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        navigation.Select(folder);
        workspace.SaveState();

        Assert.Equal("work", navigation.CurrentFolder);
        Assert.Equal("work", _harness.ReadSavedState().Tabs[0].SelectedFolder);
    }
}
