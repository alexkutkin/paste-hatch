namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceTreeTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Entries_AreSortedFoldersFirstThenFilesWithNaturalNumbers()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "folder10"));
        Directory.CreateDirectory(Path.Combine(directory, "folder2"));
        Directory.CreateDirectory(Path.Combine(directory, "Alpha"));
        File.WriteAllText(Path.Combine(directory, "file10.txt"), "10");
        File.WriteAllText(Path.Combine(directory, "file2.txt"), "2");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "a");

        workspace.Refresh();

        Assert.Equal(
            ["Alpha", "folder2", "folder10", "a.txt", "file2.txt", "file10.txt"],
            workspace.Entries.Select(entry => entry.Name));
        Assert.True(workspace.Entries[0].IsFolder);
        Assert.False(workspace.Entries[3].IsFolder);
    }

    [Fact]
    public void NavigateTo_ShowsOnlyTheSelectedFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "sub"));
        File.WriteAllText(Path.Combine(directory, "root.txt"), "root");
        File.WriteAllText(Path.Combine(directory, "sub", "inside.txt"), "inside");

        Assert.True(workspace.NavigateTo("sub"));

        Assert.Equal("sub", workspace.SelectedFolder);
        Assert.Equal(Path.Combine(directory, "sub"), workspace.SelectedFolderPath);
        var entry = Assert.Single(workspace.Entries);
        Assert.Equal("inside.txt", entry.Name);
        Assert.Equal(Path.Combine("sub", "inside.txt"), entry.RelativePath);
        Assert.Single(workspace.Materials);
    }

    [Fact]
    public void NavigateTo_OutsideTheBasket_IsRejected()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.False(workspace.NavigateTo(".."));
        Assert.False(workspace.NavigateTo("sub\\..\\.."));
        Assert.False(workspace.NavigateTo(Path.GetTempPath()));
        Assert.Equal(string.Empty, workspace.SelectedFolder);
    }

    [Fact]
    public void SelectedFolder_PersistsAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub"));
        Assert.True(workspace.NavigateTo("sub"));

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();

        Assert.Equal("sub", restarted.SelectedFolder);
        Assert.Equal("sub", _harness.ReadSavedState().Tabs[0].SelectedFolder);
    }

    [Fact]
    public void SelectedFolder_DeletedOutside_FallsBackToRoot()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub");
        Directory.CreateDirectory(directory);
        Assert.True(workspace.NavigateTo("sub"));
        Directory.Delete(directory);

        workspace.Refresh();

        Assert.Equal(string.Empty, workspace.SelectedFolder);
        Assert.Empty(workspace.Entries);
    }

    [Fact]
    public void CreateFolder_CreatesFolderAndReportsItInEntries()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        var folder = workspace.CreateFolder("Новая папка");

        Assert.NotNull(folder);
        Assert.True(Directory.Exists(Path.Combine(directory, "Новая папка")));
        Assert.Contains(workspace.Entries, entry => entry.IsFolder && entry.Name == "Новая папка");
        Assert.Contains("создана", workspace.StatusMessage);
    }

    [Fact]
    public void CreateFolder_WhenNameExists_ShowsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub"));

        Assert.Null(workspace.CreateFolder("sub"));

        Assert.Contains("уже существует", Assert.Single(_harness.Confirmations.Errors));
    }

    [Fact]
    public void CreateFolder_EnsureUnique_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "Новая папка"));

        var folder = workspace.CreateFolder("Новая папка", ensureUnique: true);

        Assert.NotNull(folder);
        Assert.Equal("Новая папка (2)", folder.Name);
        Assert.True(Directory.Exists(Path.Combine(directory, "Новая папка (2)")));
    }

    [Fact]
    public void RenameFolder_KeepsContentsAndRenamesOnDisk()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "old"));
        File.WriteAllText(Path.Combine(directory, "old", "inside.txt"), "inside");
        var folder = Assert.Single(workspace.GetSubfolders(string.Empty));

        Assert.True(workspace.RenameFolder(folder, "new"));

        Assert.False(Directory.Exists(Path.Combine(directory, "old")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "new", "inside.txt"), "inside");
        Assert.Contains("переименована", workspace.StatusMessage);
    }

    [Fact]
    public void RenameFolder_WhenTargetExists_ShowsErrorAndKeepsBoth()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "one"));
        Directory.CreateDirectory(Path.Combine(directory, "two"));
        var folder = Assert.Single(workspace.GetSubfolders(string.Empty), entry => entry.Name == "one");

        Assert.False(workspace.RenameFolder(folder, "two"));

        Assert.Contains("уже существует", Assert.Single(_harness.Confirmations.Errors));
        Assert.True(Directory.Exists(Path.Combine(directory, "one")));
        Assert.True(Directory.Exists(Path.Combine(directory, "two")));
    }

    [Fact]
    public void DeleteFolder_WhenConfirmed_RecyclesFolderAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "trash"));
        File.WriteAllText(Path.Combine(directory, "trash", "inside.txt"), "inside");
        var folder = Assert.Single(workspace.GetSubfolders(string.Empty));

        Assert.True(workspace.DeleteFolder(folder));

        Assert.Contains(directory, _harness.Shell.RecycledDirectories[0]);
        Assert.False(Directory.Exists(Path.Combine(directory, "trash")));
        Assert.Empty(workspace.Entries);
        Assert.Contains("Корзину Windows", workspace.StatusMessage);
    }

    [Fact]
    public void DeleteFolder_WhenConfirmationDeclined_KeepsFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "keep"));
        var folder = Assert.Single(workspace.GetSubfolders(string.Empty));
        _harness.Confirmations.ConfirmFolderResult = false;

        Assert.False(workspace.DeleteFolder(folder));

        Assert.True(Directory.Exists(Path.Combine(directory, "keep")));
        Assert.Empty(_harness.Shell.RecycledDirectories);
    }

    [Fact]
    public void MoveEntry_MovesFileIntoSubfolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "dest"));
        File.WriteAllText(Path.Combine(directory, "note.txt"), "note");
        workspace.Refresh();
        var file = Assert.Single(workspace.Entries, entry => !entry.IsFolder);

        Assert.True(workspace.MoveEntry(file, "dest"));

        Assert.False(File.Exists(Path.Combine(directory, "note.txt")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "dest", "note.txt"), "note");
        Assert.Contains("перемещён", workspace.StatusMessage);
    }

    [Fact]
    public void MoveEntry_RefusesToMoveFolderInsideItself()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "parent", "child"));
        workspace.NavigateTo("parent");
        var folder = Assert.Single(workspace.GetSubfolders("parent"));

        Assert.False(workspace.MoveEntry(folder, Path.Combine("parent", "child")));
        Assert.Contains("внутрь себя", Assert.Single(_harness.Confirmations.Errors));
    }

    [Fact]
    public void PasteFiles_Copy_AllowsDuplicatingAFileAlreadyInTheBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(directory, "note.txt"), "note");
        workspace.Refresh();

        var added = workspace.PasteFiles([Path.Combine(directory, "note.txt")], string.Empty, move: false);

        Assert.Equal(1, added);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "note.txt"), "note");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "note (2).txt"), "note");
    }

    [Fact]
    public void PasteFiles_Cut_MovesFileIntoSubfolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "dest"));
        File.WriteAllText(Path.Combine(directory, "note.txt"), "note");
        workspace.Refresh();

        var moved = workspace.PasteFiles([Path.Combine(directory, "note.txt")], "dest", move: true);

        Assert.Equal(1, moved);
        Assert.False(File.Exists(Path.Combine(directory, "note.txt")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "dest", "note.txt"), "note");
    }

    [Fact]
    public void PasteFiles_CutIntoTheSameFolder_IsANoOp()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(directory, "note.txt"), "note");
        workspace.Refresh();

        var moved = workspace.PasteFiles([Path.Combine(directory, "note.txt")], string.Empty, move: true);

        Assert.Equal(0, moved);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "note.txt"), "note");
        Assert.False(File.Exists(Path.Combine(directory, "note (2).txt")));
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
    public void GetSubfolders_ReturnsOnlyDirectoriesSortedNaturally()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(directory, "b"));
        Directory.CreateDirectory(Path.Combine(directory, "a10"));
        Directory.CreateDirectory(Path.Combine(directory, "a2"));
        File.WriteAllText(Path.Combine(directory, "file.txt"), "x");

        var folders = workspace.GetSubfolders(string.Empty);

        Assert.Equal(["a2", "a10", "b"], folders.Select(folder => folder.Name));
        Assert.All(folders, folder => Assert.True(folder.IsFolder));
    }

    [Fact]
    public void CreateFolder_ThenNavigate_ShowsNewFolderAsSelected()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var folder = workspace.CreateFolder("work");

        Assert.NotNull(folder);
        Assert.True(workspace.NavigateTo(folder.RelativePath));
        Assert.Equal("work", workspace.SelectedFolder);
        Assert.Equal("work", _harness.ReadSavedState().Tabs[0].SelectedFolder);
    }
}
