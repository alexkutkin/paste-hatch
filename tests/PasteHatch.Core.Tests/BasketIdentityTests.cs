namespace PasteHatch.Core.Tests;

public sealed class BasketIdentityTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("My Basket", "My_Basket")]
    [InlineData("Мои заметки", "Мои_заметки")]
    public void Rename_MovesMaterialsAndRestoresStableIdentity(string name, string directoryName)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var original = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(original.DirectoryPath, "note.txt"), "unchanged");

        Assert.True(workspace.RenameTab(original.Uid, name));

        var renamed = workspace.RequireActiveTab();
        Assert.Equal(original.Uid, renamed.Uid);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, directoryName), renamed.DirectoryPath);
        Assert.False(Directory.Exists(original.DirectoryPath));
        FileAssert.HasText(Path.Combine(renamed.DirectoryPath, "note.txt"), "unchanged");
        workspace.Refresh();
        var restored = _harness.CreateInitializedWorkspace();
        Assert.Equal(renamed, Assert.Single(restored.Tabs));
        Assert.Equal(renamed, restored.ActiveTab);
    }

    [Fact]
    public void Load_UpgradesOldStateWithoutRenamingDirectories()
    {
        var directory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        File.WriteAllText(_harness.Temp.StateFile, """
            {"version":3,"activeUid":"AB2CD3","tabs":[{"uid":"AB2CD3","name":"Old basket"}]}
            """);

        var workspace = _harness.CreateInitializedWorkspace();

        Assert.Equal(directory, workspace.RequireActiveTab().DirectoryPath);
        Assert.Equal("AB2CD3", workspace.RequireActiveTab().Uid);
        var saved = _harness.ReadSavedState();
        Assert.Equal(4, saved.Version);
        Assert.Equal("AB2CD3", Assert.Single(saved.Tabs).DirectoryName);
        Assert.True(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("bad:name")]
    [InlineData("bad/name")]
    [InlineData("..")]
    [InlineData("trailing.")]
    public void Rename_InvalidFolderNameKeepsBasketAndMaterials(string name)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(tab.DirectoryPath, "note.txt"), "safe");

        Assert.False(workspace.RenameTab(tab.Uid, name));

        Assert.Equal(tab, workspace.RequireActiveTab());
        FileAssert.HasText(Path.Combine(tab.DirectoryPath, "note.txt"), "safe");
        workspace.RequireError();
    }

    [Fact]
    public void Rename_OccupiedDestinationDoesNotMergeOrOverwrite()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        var occupied = _harness.Temp.CreateBasketDirectory("Existing_basket");
        File.WriteAllText(Path.Combine(tab.DirectoryPath, "source.txt"), "source");
        File.WriteAllText(Path.Combine(occupied, "target.txt"), "target");

        Assert.False(workspace.RenameTab(tab.Uid, "Existing basket"));

        Assert.Equal(tab, workspace.RequireActiveTab());
        FileAssert.HasText(Path.Combine(tab.DirectoryPath, "source.txt"), "source");
        FileAssert.HasText(Path.Combine(occupied, "target.txt"), "target");
        Assert.False(File.Exists(Path.Combine(occupied, "source.txt")));
        workspace.RequireError();
    }

    [Fact]
    public void Rename_CaseOnlyPreservesIdentityAndExactDirectoryCase()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var uid = workspace.RequireActiveTab().Uid;
        Assert.True(workspace.RenameTab(uid, "Basket"));

        Assert.True(workspace.RenameTab(uid, "BASKET"));

        Assert.Equal("BASKET", Path.GetFileName(Assert.Single(Directory.GetDirectories(_harness.Temp.BasketsRoot))));
        Assert.Equal(uid, _harness.CreateInitializedWorkspace().RequireActiveTab().Uid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rename_StateWriteFailureReportsFailureAndRestoresOneStableBasket(bool moved)
    {
        var store = new FailingStore(_harness.Store, state => moved
            ? state.PendingRename is null && state.Tabs.Any(tab => tab.DirectoryName == "Saved_basket")
            : state.PendingRename is not null);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();
        var before = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(before.DirectoryPath, "note.txt"), "recoverable");

        Assert.False(workspace.RenameTab(before.Uid, "Saved basket"));

        workspace.RequireError();
        var restored = _harness.CreateInitializedWorkspace();
        var basket = Assert.Single(restored.Tabs);
        Assert.Equal(before.Uid, basket.Uid);
        Assert.Equal(moved ? "Saved_basket" : Path.GetFileName(before.DirectoryPath), Path.GetFileName(basket.DirectoryPath));
        FileAssert.HasText(Path.Combine(basket.DirectoryPath, "note.txt"), "recoverable");
        Assert.Null(_harness.ReadSavedState().PendingRename);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_InterruptedCaseRenamePreservesIdentityAndActualDirectory(bool movedToTemporary)
    {
        var directory = _harness.Temp.CreateBasketDirectory(movedToTemporary ? "~rename.tmp" : "Basket");
        File.WriteAllText(Path.Combine(directory, "note.txt"), "safe");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", Name = "Basket", DirectoryName = "Basket" }],
            PendingRename = new BasketRenameState
            {
                Uid = "stable", DirectoryName = "BASKET", Name = "BASKET", TemporaryDirectoryName = "~rename.tmp",
            },
        });

        var workspace = _harness.CreateInitializedWorkspace();

        var basket = Assert.Single(workspace.Tabs);
        Assert.Equal("stable", basket.Uid);
        var expectedName = movedToTemporary ? "BASKET" : "Basket";
        Assert.Equal(expectedName, Path.GetFileName(basket.DirectoryPath));
        Assert.Equal(expectedName, basket.DisplayName);
        Assert.Equal(expectedName, Path.GetFileName(Assert.Single(Directory.GetDirectories(_harness.Temp.BasketsRoot))));
        FileAssert.HasText(Path.Combine(basket.DirectoryPath, "note.txt"), "safe");
    }

    [Fact]
    public void Rename_LockedDirectoryReportsFailureWithoutChangingBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var basket = workspace.RequireActiveTab();
        var path = Path.Combine(basket.DirectoryPath, "locked.txt");
        File.WriteAllText(path, "safe");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(workspace.RenameTab(basket.Uid, "Locked basket"));

        Assert.Equal(basket, workspace.RequireActiveTab());
        workspace.RequireError();
        Assert.True(Directory.Exists(basket.DirectoryPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rename_InterruptedCaseMoveKeepsTemporaryBasketIdentityThroughRefreshSaveAndRestart(bool failRecoverySave)
    {
        var setup = _harness.CreateInitializedWorkspace();
        var uid = setup.RequireActiveTab().Uid;
        Assert.True(setup.RenameTab(uid, "Basket"));
        var original = setup.RequireActiveTab();
        File.WriteAllText(Path.Combine(original.DirectoryPath, "note.txt"), "recoverable");
        var store = new InterruptedCaseMoveStore(_harness.Store, original.DirectoryPath, failRecoverySave);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();

        Assert.False(workspace.RenameTab(uid, "BASKET"));

        workspace.RequireError();
        var stranded = Assert.Single(workspace.Tabs);
        Assert.Equal(uid, stranded.Uid);
        Assert.Equal("Basket", stranded.DisplayName);
        Assert.True(Directory.Exists(stranded.DirectoryPath));
        Assert.NotEqual(original.DirectoryPath, stranded.DirectoryPath);
        using var locked = new FileStream(Path.Combine(stranded.DirectoryPath, "note.txt"),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        workspace.Refresh();
        workspace.SaveState();
        Assert.Equal(stranded, Assert.Single(workspace.Tabs));
        Assert.Equal(failRecoverySave, _harness.ReadSavedState().PendingRename is not null);

        var restored = _harness.CreateInitializedWorkspace();

        Assert.Equal(stranded, Assert.Single(restored.Tabs));
        Assert.Equal(stranded, restored.ActiveTab);
        var material = Assert.Single(restored.GetEntries(string.Empty));
        Assert.Equal("note.txt", material.Name);
        FileAssert.HasText(material.FullPath, "recoverable");
        restored.Refresh();
        restored.SaveState();
        Assert.Equal(stranded, Assert.Single(_harness.CreateInitializedWorkspace().Tabs));
    }

    [Fact]
    public void Rename_KeepsNestedSelectionAndUsesNewPathsAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var before = workspace.RequireActiveTab();
        Directory.CreateDirectory(Path.Combine(before.DirectoryPath, "nested"));
        File.WriteAllText(Path.Combine(before.DirectoryPath, "nested", "note.txt"), "safe");
        var navigation = workspace.Navigation;
        navigation.Reconcile(null, []);
        var selected = Assert.Single(workspace.GetEntries(string.Empty));
        navigation.Select(selected);

        Assert.True(workspace.RenameTab(before.Uid, "New basket"));
        navigation.Reconcile(selected, ["nested"]);

        var expected = Path.Combine(_harness.Temp.BasketsRoot, "New_basket", "nested");
        Assert.Equal(expected, navigation.CurrentFolderPath);
        workspace.CopyActiveBasketPath(navigation.CurrentFolder);
        Assert.Equal(expected, Assert.Single(_harness.Shell.CopiedTexts));
        FileAssert.HasText(Assert.Single(workspace.GetEntries("nested")).FullPath, "safe");
        var restored = _harness.CreateInitializedWorkspace();
        Assert.Equal("nested", restored.SavedFolderFor(before.Uid));
        Assert.Equal(expected, Path.Combine(restored.RequireActiveTab().DirectoryPath, restored.SavedFolderFor(before.Uid)));
    }

    [Fact]
    public void Load_AfterStateLossAdoptsRenamedFolderWithoutChangingMaterials()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Assert.True(workspace.RenameTab(workspace.RequireActiveTab().Uid, "Мои заметки"));
        var path = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(path, "note.txt"), "safe");
        File.Delete(_harness.Temp.StateFile);

        var restored = _harness.CreateInitializedWorkspace();

        Assert.Equal(path, Assert.Single(restored.Tabs).DirectoryPath);
        FileAssert.HasText(Path.Combine(path, "note.txt"), "safe");
        Assert.Single(Directory.GetDirectories(_harness.Temp.BasketsRoot));
    }

    private sealed class InterruptedCaseMoveStore(ITabStateStore inner, string source, bool failRecoverySave) : ITabStateStore
    {
        private bool _interrupted;
        public WorkspaceState Load() => inner.Load();
        public void Save(WorkspaceState state)
        {
            if (_interrupted && failRecoverySave) throw new IOException("State writer unavailable.");
            inner.Save(state);
            if (state.PendingRename is not { } pending || _interrupted) return;
            // Reproduce a stranded temporary directory through the public storage boundary,
            // without depending on a watcher racing the two synchronous filesystem moves.
            Directory.Move(source, Path.Combine(Path.GetDirectoryName(source)!, pending.TemporaryDirectoryName));
            _interrupted = true;
        }
    }

    private sealed class FailingStore(ITabStateStore inner, Func<WorkspaceState, bool> shouldFail) : ITabStateStore
    {
        private bool _failed;
        public WorkspaceState Load() => inner.Load();
        public void Save(WorkspaceState state)
        {
            if (!_failed && shouldFail(state))
            {
                _failed = true;
                throw new IOException("State writer unavailable.");
            }
            inner.Save(state);
        }
    }
}
