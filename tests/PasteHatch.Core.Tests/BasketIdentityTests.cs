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
    public void Rename_UnchangedNameDoesNotWriteIntentOrRenameDirectory()
    {
        var setup = _harness.CreateInitializedWorkspace();
        Assert.True(setup.RenameTab(setup.RequireActiveTab().Uid, "Unchanged basket"));
        var store = new FailingStore(_harness.Store, state => state.PendingRename is not null);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();
        var before = workspace.RequireActiveTab();

        Assert.True(workspace.RenameTab(before.Uid, before.DisplayName));

        Assert.Equal(before, workspace.RequireActiveTab());
        Assert.False(workspace.StatusIsError);
        Assert.Null(_harness.ReadSavedState().PendingRename);
        Assert.True(Directory.Exists(before.DirectoryPath));
    }

    [Fact]
    public void Rename_SameDisplayNameStillRenamesNoncanonicalDirectory()
    {
        var directory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", DirectoryName = "AB2CD3", Name = "Named basket" }],
        });
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.True(workspace.RenameTab("stable", "Named basket"));

        Assert.Equal("Named_basket", Path.GetFileName(workspace.RequireActiveTab().DirectoryPath));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Rename_DisplayOnlyChangeDoesNotWriteDiskIntent()
    {
        var setup = _harness.CreateInitializedWorkspace();
        Assert.True(setup.RenameTab(setup.RequireActiveTab().Uid, "Named basket"));
        var store = new FailingStore(_harness.Store, state => state.PendingRename is not null);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();
        var before = workspace.RequireActiveTab();

        Assert.True(workspace.RenameTab(before.Uid, "Named   basket"));

        Assert.Equal(before.DirectoryPath, workspace.RequireActiveTab().DirectoryPath);
        Assert.Equal("Named   basket", workspace.RequireActiveTab().DisplayName);
        Assert.Null(_harness.ReadSavedState().PendingRename);
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
    public void Rename_FinalSaveFailurePreservesIntentAcrossAmbiguityAndOrdinarySaves()
    {
        var store = new FailingStore(_harness.Store, state => state.PendingRename is null &&
            state.Tabs.Any(tab => tab.DirectoryName == "Saved_basket"));
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();
        var original = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(original.DirectoryPath, "note.txt"), "original");
        Assert.False(workspace.RenameTab(original.Uid, "Saved basket"));
        Directory.CreateDirectory(original.DirectoryPath);
        File.WriteAllText(Path.Combine(original.DirectoryPath, "replacement.txt"), "replacement");

        workspace.SaveState();

        Assert.NotNull(_harness.ReadSavedState().PendingRename);
        workspace.Refresh();
        Assert.Equal(original.Uid, Assert.Single(workspace.Tabs).Uid);
        Assert.False(workspace.RenameTab(original.Uid, "Another rename"));
        Assert.NotNull(_harness.ReadSavedState().PendingRename);
        var restored = _harness.CreateInitializedWorkspace();
        var recovered = Assert.Single(restored.Tabs);
        Assert.Equal(original.Uid, recovered.Uid);
        Assert.Equal("Saved_basket", Path.GetFileName(recovered.DirectoryPath));
        FileAssert.HasText(Path.Combine(recovered.DirectoryPath, "note.txt"), "original");
        FileAssert.HasText(Path.Combine(original.DirectoryPath, "replacement.txt"), "replacement");
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
    public void Load_LegacyTemporaryBasketKeepsIdentityThroughFailedRecoveryAndLaterCompletes(bool failRecoverySave)
    {
        var temporary = _harness.Temp.CreateBasketDirectory("~rename.tmp");
        var materialPath = Path.Combine(temporary, "note.txt");
        File.WriteAllText(materialPath, "recoverable");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", DirectoryName = "Basket", Name = "Basket" }],
            PendingRename = new BasketRenameState
            {
                Uid = "stable", DirectoryName = "BASKET", Name = "BASKET", TemporaryDirectoryName = "~rename.tmp",
            },
        });
        using (var locked = new FileStream(materialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ITabStateStore store = failRecoverySave ? new UnavailableStateStore(_harness.Store) : _harness.Store;
            var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
                _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
            workspace.Initialize();
            workspace.RequireError();
            var stranded = Assert.Single(workspace.Tabs);
            Assert.Equal("stable", stranded.Uid);
            Assert.Equal("Basket", stranded.DisplayName);
            Assert.Equal(temporary, stranded.DirectoryPath);
            workspace.Refresh();
            workspace.SaveState();
            Assert.Equal(stranded, Assert.Single(workspace.Tabs));
            Assert.NotNull(_harness.ReadSavedState().PendingRename);
            var restored = _harness.CreateInitializedWorkspace();
            Assert.Equal(stranded, Assert.Single(restored.Tabs));
            Assert.Equal(stranded, restored.ActiveTab);
            var material = Assert.Single(restored.GetEntries(string.Empty));
            Assert.Equal("note.txt", material.Name);
            FileAssert.HasText(material.FullPath, "recoverable");
        }
        var recovered = _harness.CreateInitializedWorkspace();
        var basket = Assert.Single(recovered.Tabs);
        Assert.Equal("stable", basket.Uid);
        Assert.Equal("BASKET", Path.GetFileName(basket.DirectoryPath));
        FileAssert.HasText(Path.Combine(basket.DirectoryPath, "note.txt"), "recoverable");
        Assert.Null(_harness.ReadSavedState().PendingRename);
    }

    [Fact]
    public void Initialize_BlockedRootDoesNotOverwriteMetadataAndRefreshRestoresOriginalIdentity()
    {
        var source = _harness.Temp.CreateBasketDirectory("Source");
        var target = _harness.Temp.CreateBasketDirectory("Target");
        File.WriteAllText(Path.Combine(source, "note.txt"), "original");
        File.WriteAllText(Path.Combine(target, "other.txt"), "separate");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", DirectoryName = "Source", Name = "Original name" }],
            PendingRename = new BasketRenameState
            {
                Uid = "stable", SourceDirectoryName = "Source", DirectoryName = "Target", Name = "Proposed name",
            },
        });
        var originalMetadata = File.ReadAllBytes(_harness.Temp.StateFile);
        var movedRoot = Path.Combine(_harness.Temp.Root, "Baskets-before-startup");
        Directory.Move(_harness.Temp.BasketsRoot, movedRoot);
        File.WriteAllText(_harness.Temp.BasketsRoot, "Blocked before Initialize.");
        var workspace = _harness.CreateWorkspace();
        try
        {
            Assert.Throws<IOException>(workspace.Initialize);

            workspace.SaveState();

            Assert.Equal(originalMetadata, File.ReadAllBytes(_harness.Temp.StateFile));
            Assert.False(workspace.Refresh());
            Assert.False(workspace.IsInitialized);
            Assert.Equal(originalMetadata, File.ReadAllBytes(_harness.Temp.StateFile));
            Assert.False(workspace.CanCreateTab);
        }
        finally
        {
            File.Delete(_harness.Temp.BasketsRoot);
            Directory.Move(movedRoot, _harness.Temp.BasketsRoot);
        }
        Assert.Null(workspace.CreateTab());
        Assert.Equal(originalMetadata, File.ReadAllBytes(_harness.Temp.StateFile));

        Assert.True(workspace.Refresh());

        var restored = Assert.Single(workspace.Tabs);
        Assert.Equal("stable", restored.Uid);
        Assert.Equal("Original name", restored.DisplayName);
        Assert.Equal(source, restored.DirectoryPath);
        Assert.NotNull(_harness.ReadSavedState().PendingRename);
        Assert.True(workspace.CanCreateTab);
        FileAssert.HasText(Path.Combine(source, "note.txt"), "original");
        FileAssert.HasText(Path.Combine(target, "other.txt"), "separate");
    }

    [Fact]
    public void Initialize_PartiallyLoadedModelCannotBeExposedOrSavedUntilRetryCompletes()
    {
        var first = _harness.Temp.CreateBasketDirectory("First");
        _harness.Temp.CreateBasketDirectory("Second");
        File.WriteAllText(Path.Combine(first, "note.txt"), "safe");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "first",
            Tabs =
            [
                new TabState { Uid = "first", DirectoryName = "First", Name = "First basket" },
                new TabState { Uid = "second", DirectoryName = "Second", Name = "" },
            ],
        });
        var originalMetadata = File.ReadAllBytes(_harness.Temp.StateFile);
        var failFormatting = true;
        var formatter = new TabNameFormatter(() => failFormatting
            ? throw new IOException("Default-name formatting unavailable.") : _harness.Text.Culture);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, _harness.Store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, formatter, _harness.Text);

        Assert.Throws<IOException>(workspace.Initialize);
        Assert.False(workspace.IsInitialized);
        Assert.Empty(workspace.Tabs);
        Assert.Null(workspace.ActiveTab);
        Assert.Null(workspace.AddQuickNote("must not write through partial model", string.Empty));
        Assert.False(workspace.RenameTab("first", "Changed"));
        Assert.False(workspace.ActivateTab("first"));
        Assert.False(workspace.ReorderTabs(["second", "first"]));
        Assert.False(workspace.MoveTab("first", 1));
        workspace.SaveState();
        Assert.Equal(originalMetadata, File.ReadAllBytes(_harness.Temp.StateFile));

        failFormatting = false;
        Assert.True(workspace.Refresh());

        Assert.True(workspace.IsInitialized);
        Assert.Equal(new[] { "first", "second" }, workspace.Tabs.Select(tab => tab.Uid));
        Assert.Equal("First basket", workspace.RequireActiveTab().DisplayName);
        FileAssert.HasText(Assert.Single(workspace.GetEntries(string.Empty)).FullPath, "safe");
    }

    [Fact]
    public void Load_MissingRenamePathsPreserveIdentityAndDoNotRecreateEvidence()
    {
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", DirectoryName = "Source", Name = "Old basket" }],
            PendingRename = new BasketRenameState
            {
                Uid = "stable", SourceDirectoryName = "Source", DirectoryName = "Target", Name = "New basket",
            },
        });
        var workspace = _harness.CreateInitializedWorkspace();
        Assert.Equal("stable", Assert.Single(workspace.Tabs).Uid);
        Assert.NotNull(_harness.ReadSavedState().PendingRename);

        Assert.Null(workspace.AddQuickNote("must not create missing source", string.Empty));
        Assert.Null(workspace.CreateFolder("child", string.Empty));
        Assert.False(Directory.Exists(Path.Combine(_harness.Temp.BasketsRoot, "Source")));
        var independent = workspace.CreateAndRequireTab();
        Assert.NotNull(workspace.AddQuickNote("independent work", string.Empty));
        Assert.False(workspace.CloseTab("stable"));
        Assert.True(Directory.Exists(independent.DirectoryPath));

        var target = _harness.Temp.CreateBasketDirectory("Target");
        File.WriteAllText(Path.Combine(target, "note.txt"), "restored");
        workspace.Refresh();

        Assert.Null(_harness.ReadSavedState().PendingRename);
        var recovered = Assert.Single(workspace.Tabs, tab => tab.Uid == "stable");
        Assert.Equal(target, recovered.DirectoryPath);
        Assert.Equal("New basket", recovered.DisplayName);
        FileAssert.HasText(Path.Combine(recovered.DirectoryPath, "note.txt"), "restored");
        Assert.Equal(2, workspace.Tabs.Count);
    }

    [Fact]
    public void Load_UnreadableRenameObservationKeepsJournalUntilRootIsRestored()
    {
        var source = _harness.Temp.CreateBasketDirectory("Source");
        File.WriteAllText(Path.Combine(source, "note.txt"), "safe");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "stable",
            Tabs = [new TabState { Uid = "stable", DirectoryName = "Source", Name = "Old basket" }],
            PendingRename = new BasketRenameState
            {
                Uid = "stable", SourceDirectoryName = "Source", DirectoryName = "Target", Name = "New basket",
            },
        });
        var movedRoot = Path.Combine(_harness.Temp.Root, "Baskets-away");
        var store = new RootReplacedOnLoadStore(_harness.Store, _harness.Temp.BasketsRoot, movedRoot);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        try
        {
            workspace.Initialize();
            Assert.Equal("stable", Assert.Single(workspace.Tabs).Uid);
            workspace.SaveState();
            Assert.NotNull(_harness.ReadSavedState().PendingRename);
            workspace.RequireError();
        }
        finally
        {
            File.Delete(_harness.Temp.BasketsRoot);
            Directory.Move(movedRoot, _harness.Temp.BasketsRoot);
        }
        workspace.Refresh();
        Assert.Null(_harness.ReadSavedState().PendingRename);
        Assert.Equal(source, Assert.Single(workspace.Tabs).DirectoryPath);
        FileAssert.HasText(Path.Combine(source, "note.txt"), "safe");
    }

    [Fact]
    public void Rename_NewIntentRecordsOriginalDirectoryWithoutTemporaryPath()
    {
        var store = new RecordingStore(_harness.Store);
        var workspace = new BasketWorkspace(_harness.Temp.BasketsRoot, store, _harness.Clipboard,
            _harness.Shell, _harness.Confirmations, _harness.Clock, text: _harness.Text);
        workspace.Initialize();
        var original = workspace.RequireActiveTab();

        Assert.True(workspace.RenameTab(original.Uid, "Renamed basket"));

        var pending = Assert.Single(store.Saved, state => state.PendingRename is not null).PendingRename!;
        Assert.Equal(Path.GetFileName(original.DirectoryPath), pending.SourceDirectoryName);
        Assert.Equal("Renamed_basket", pending.DirectoryName);
        Assert.Null(pending.TemporaryDirectoryName);
        Assert.Null(_harness.ReadSavedState().PendingRename);
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

    private sealed class UnavailableStateStore(ITabStateStore inner) : ITabStateStore
    {
        public WorkspaceState Load() => inner.Load();
        public void Save(WorkspaceState state) => throw new IOException("State writer unavailable.");
    }

    private sealed class RecordingStore(ITabStateStore inner) : ITabStateStore
    {
        public List<WorkspaceState> Saved { get; } = [];
        public WorkspaceState Load() => inner.Load();
        public void Save(WorkspaceState state)
        {
            Saved.Add(state);
            inner.Save(state);
        }
    }

    private sealed class RootReplacedOnLoadStore(ITabStateStore inner, string root, string movedRoot) : ITabStateStore
    {
        public WorkspaceState Load()
        {
            var state = inner.Load();
            Directory.Move(root, movedRoot);
            File.WriteAllText(root, "Root temporarily replaced by a file.");
            return state;
        }
        public void Save(WorkspaceState state) => inner.Save(state);
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
