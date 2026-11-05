namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceRenameTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("My Basket", "My_Basket")]
    [InlineData("Мои заметки", "Мои_заметки")]
    public void RenameTab_RenamesTheBasketFolderWithUnderscoresBetweenWords(string name, string expectedFolder)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(tab.DirectoryPath, "notes.txt"), "kept");

        Assert.True(workspace.RenameTab(tab.Uid, name));

        var renamed = workspace.RequireActiveTab();
        Assert.Equal(tab.Uid, renamed.Uid);
        Assert.Equal(name, renamed.DisplayName);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, expectedFolder), renamed.DirectoryPath);
        FileAssert.HasText(Path.Combine(renamed.DirectoryPath, "notes.txt"), "kept");
        Assert.False(Directory.Exists(tab.DirectoryPath));
    }

    [Theory]
    [InlineData("Fix: bug #12?", "Fix_bug_12")]
    [InlineData("  spaced   out  ", "spaced_out")]
    [InlineData("CON", "CON_")]
    public void RenameTab_AppliesWindowsFolderNameRules(string name, string expectedFolder)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();

        Assert.True(workspace.RenameTab(tab.Uid, name));

        Assert.Equal(expectedFolder, Path.GetFileName(workspace.RequireActiveTab().DirectoryPath));
        Assert.True(Directory.Exists(Path.Combine(_harness.Temp.BasketsRoot, expectedFolder)));
    }

    [Fact]
    public void RenameTab_SurvivesRestartWithoutDuplicatingOrReadoptingTheBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        Assert.True(workspace.RenameTab(tab.Uid, "My Basket"));
        workspace.Refresh();
        Assert.Single(workspace.Tabs);

        var restarted = _harness.CreateInitializedWorkspace();

        var restored = Assert.Single(restarted.Tabs);
        Assert.Equal(tab.Uid, restored.Uid);
        Assert.Equal("My Basket", restored.DisplayName);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, "My_Basket"), restored.DirectoryPath);
        var saved = Assert.Single(_harness.ReadSavedState().Tabs);
        Assert.Equal(tab.Uid, saved.Uid);
        Assert.Equal("My_Basket", saved.Folder);
    }

    [Fact]
    public void Initialize_UpgradesOldStateWhereTheUidWasTheFolderNameWithoutRenamingFolders()
    {
        var directory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        Directory.CreateDirectory(Path.Combine(directory, "docs"));
        var imported = _harness.Temp.CreateBasketDirectory("imported folder");
        File.WriteAllText(_harness.Temp.StateFile, """
            {
              "version": 3,
              "activeUid": "AB2CD3",
              "tabs": [
                { "uid": "AB2CD3", "name": "Old basket", "selectedFolder": "docs" },
                { "uid": "imported folder", "name": "Imported basket" }
              ]
            }
            """);

        var workspace = _harness.CreateInitializedWorkspace();

        Assert.Collection(
            workspace.Tabs,
            tab =>
            {
                Assert.Equal("AB2CD3", tab.Uid);
                Assert.Equal("Old basket", tab.DisplayName);
                Assert.Equal(directory, tab.DirectoryPath);
            },
            tab =>
            {
                Assert.Equal("imported folder", tab.Uid);
                Assert.Equal("Imported basket", tab.DisplayName);
                Assert.Equal(imported, tab.DirectoryPath);
            });
        Assert.Equal(2, Directory.GetDirectories(_harness.Temp.BasketsRoot).Length);
        workspace.Refresh();
        workspace.ActivateTab("imported folder");
        var saved = _harness.ReadSavedState();
        Assert.Equal(WorkspaceState.CurrentVersion, saved.Version);
        Assert.Equal(["AB2CD3", "imported folder"], saved.Tabs.Select(tab => tab.Folder));
        Assert.Equal("docs", saved.Tabs[0].SelectedFolder);
    }

    [Fact]
    public void RenameTab_KeepsTheCurrentFolderAndListsEntriesFromTheRenamedFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        Directory.CreateDirectory(Path.Combine(tab.DirectoryPath, "docs"));
        File.WriteAllText(Path.Combine(tab.DirectoryPath, "docs", "a.txt"), "a");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        navigation.Select(Assert.Single(workspace.Folders(string.Empty)));

        Assert.True(workspace.RenameTab(tab.Uid, "Мои заметки"));

        Assert.Equal("docs", navigation.CurrentFolder);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, "Мои_заметки", "docs"), navigation.CurrentFolderPath);
        var file = Assert.Single(workspace.Files("docs"));
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, "Мои_заметки", "docs", "a.txt"), file.FullPath);
    }

    [Fact]
    public void RenameTab_ToAFolderNameOwnedByAnotherBasket_FailsWithoutMerging()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        var second = workspace.CreateAndRequireTab();
        Assert.True(workspace.RenameTab(second.Uid, "Reports"));
        File.WriteAllText(Path.Combine(first.DirectoryPath, "first.txt"), "first");

        Assert.False(workspace.RenameTab(first.Uid, "Reports"));

        Assert.Contains("Reports", workspace.RequireError());
        Assert.Equal(first, Assert.Single(workspace.Tabs, tab => tab.Uid == first.Uid));
        FileAssert.HasText(Path.Combine(first.DirectoryPath, "first.txt"), "first");
        Assert.Empty(Directory.GetFiles(Path.Combine(_harness.Temp.BasketsRoot, "Reports")));
        Assert.Equal(first.DisplayName, _harness.ReadSavedState().Tabs.Single(tab => tab.Uid == first.Uid).Name);
    }

    [Fact]
    public void RenameTab_ToANameOccupiedByAFile_FailsWithoutOverwriting()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        var occupied = Path.Combine(_harness.Temp.BasketsRoot, "Notes");
        File.WriteAllText(occupied, "loose file");

        Assert.False(workspace.RenameTab(tab.Uid, "Notes"));

        Assert.Equal(tab, workspace.RequireActiveTab());
        FileAssert.HasText(occupied, "loose file");
        Assert.True(Directory.Exists(tab.DirectoryPath));
    }

    [Fact]
    public void RenameTab_CaseOnlyChange_RenamesTheFolderCase()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        Assert.True(workspace.RenameTab(tab.Uid, "reports"));

        Assert.True(workspace.RenameTab(tab.Uid, "Reports"));

        var directory = Assert.Single(new DirectoryInfo(_harness.Temp.BasketsRoot).GetDirectories());
        Assert.Equal("Reports", directory.Name);
        Assert.Equal(directory.FullName, workspace.RequireActiveTab().DirectoryPath);
        Assert.Equal("Reports", workspace.RequireActiveTab().DisplayName);
    }

    [Fact]
    public void RenameTab_WithoutAnyLetterOrDigit_IsRejected()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();

        Assert.False(workspace.RenameTab(tab.Uid, "!!! ???"));

        Assert.NotEmpty(workspace.RequireError());
        Assert.Equal(tab, workspace.RequireActiveTab());
        Assert.True(Directory.Exists(tab.DirectoryPath));
    }

    [Fact]
    public void RenameTab_WhenTheFolderCannotBeMoved_KeepsTheBasketUnchanged()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        var lockedFile = Path.Combine(tab.DirectoryPath, "locked.txt");
        File.WriteAllText(lockedFile, "locked");

        bool renamed;
        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            renamed = workspace.RenameTab(tab.Uid, "My Basket");
        }

        Assert.False(renamed);
        Assert.NotEmpty(workspace.RequireError());
        Assert.Equal(tab, workspace.RequireActiveTab());
        FileAssert.HasText(lockedFile, "locked");
        Assert.False(Directory.Exists(Path.Combine(_harness.Temp.BasketsRoot, "My_Basket")));
        var restarted = _harness.CreateInitializedWorkspace();
        Assert.Equal(tab.DirectoryPath, Assert.Single(restarted.Tabs).DirectoryPath);
    }

    [Fact]
    public void RenamedBasket_IsRecoveredFromDiskWhenTheStateFileIsLost()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        Assert.True(workspace.RenameTab(tab.Uid, "My Basket"));
        File.WriteAllText(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "kept.txt"), "kept");
        File.Delete(_harness.Temp.StateFile);

        var restarted = _harness.CreateInitializedWorkspace();

        var recovered = Assert.Single(restarted.Tabs);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, "My_Basket"), recovered.DirectoryPath);
        Assert.Equal("My_Basket", recovered.DisplayName);
        FileAssert.HasText(Path.Combine(recovered.DirectoryPath, "kept.txt"), "kept");
    }
}
