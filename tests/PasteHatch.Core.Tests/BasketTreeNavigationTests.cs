namespace PasteHatch.Core.Tests;

public sealed class BasketTreeNavigationTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void SwitchingBaskets_SelectsFirstRootFileInsteadOfPreviousSelection()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(first.DirectoryPath, "note10.txt"), "ten");
        File.WriteAllText(Path.Combine(first.DirectoryPath, "note2.txt"), "two");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        var selected = workspace.GetEntries("").Single(entry => entry.Name == "note10.txt");
        navigation.Select(selected);
        workspace.CreateAndRequireTab();
        navigation.Reconcile(selected, []);

        workspace.ActivateTab(first.Uid);
        var returned = navigation.Reconcile(null, []);

        Assert.Equal("note2.txt", returned.Path);
        Assert.Equal("", navigation.CurrentFolder);
        Assert.True(returned.IsBasketSwitch);
    }

    [Fact]
    public void Select_FolderOrFile_DerivesTheCurrentFolder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(root, "same"));
        File.WriteAllText(Path.Combine(root, "same", "note.txt"), "note");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);

        navigation.Select(Assert.Single(workspace.GetEntries("same")));
        Assert.Equal("same", navigation.CurrentFolder);

        navigation.Select(Assert.Single(workspace.GetEntries(string.Empty), e => e.IsFolder));
        Assert.Equal("same", navigation.CurrentFolder);

        navigation.Select(null);
        Assert.Equal(string.Empty, navigation.CurrentFolder);
    }

    [Fact]
    public void SwitchingBaskets_SelectsFirstRootFolderAndPreservesExpandedPaths()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        Directory.CreateDirectory(Path.Combine(first.DirectoryPath, "same"));
        File.WriteAllText(Path.Combine(first.DirectoryPath, "same", "first.txt"), "first");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        var selected = Assert.Single(workspace.GetEntries("same"));
        navigation.Select(selected);

        var second = workspace.CreateAndRequireTab();
        Directory.CreateDirectory(Path.Combine(second.DirectoryPath, "same"));
        var secondView = navigation.Reconcile(selected, ["", "same"]);
        Assert.Equal("same", secondView.Path);
        Assert.True(secondView.IsBasketSwitch);
        Assert.Empty(secondView.ExpandedPaths);

        workspace.ActivateTab(first.Uid);
        var firstView = navigation.Reconcile(null, [""]);

        Assert.Equal("same", firstView.Path);
        Assert.True(firstView.IsBasketSwitch);
        Assert.Contains("same", firstView.ExpandedPaths);
        Assert.Equal("same", navigation.CurrentFolder);
        Assert.Equal("same", navigation.FolderFor(first.Uid));
    }

    [Fact]
    public void SameBasketRefresh_PreservesSelectionWhenAnEarlierFileAppears()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(root, "second.txt"), "second");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        var selected = Assert.Single(workspace.GetEntries(""));
        navigation.Select(selected);
        File.WriteAllText(Path.Combine(root, "first.txt"), "first");

        var view = navigation.Reconcile(selected, [""]);

        Assert.Equal("second.txt", view.Path);
        Assert.False(view.IsBasketSwitch);
    }

    [Fact]
    public void SwitchingToEmptyBasket_SelectsRootWithoutRepeatingSwitchOnRefresh()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        workspace.CreateAndRequireTab();

        var switched = navigation.Reconcile(null, []);
        var refreshed = navigation.Reconcile(null, []);

        Assert.Equal("", switched.Path);
        Assert.Equal("", switched.Folder);
        Assert.True(switched.IsBasketSwitch);
        Assert.False(refreshed.IsBasketSwitch);
    }

    [Fact]
    public void SwitchingBaskets_UsesFoldersFirstNaturalOrderWithoutDescendingIntoThem()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        Directory.CreateDirectory(Path.Combine(first.DirectoryPath, "folder10"));
        Directory.CreateDirectory(Path.Combine(first.DirectoryPath, "folder2"));
        File.WriteAllText(Path.Combine(first.DirectoryPath, "folder2", "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(first.DirectoryPath, "a.txt"), "file before folder alphabetically");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        workspace.CreateAndRequireTab();
        navigation.Reconcile(null, []);

        workspace.ActivateTab(first.Uid);
        var view = navigation.Reconcile(null, []);

        Assert.Equal("folder2", view.Path);
        Assert.Equal("folder2", view.Folder);
    }

    [Fact]
    public void DeletedSelectedFileAndParents_FallsBackToNearestExistingAncestor()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(root, "parent", "child"));
        File.WriteAllText(Path.Combine(root, "parent", "child", "file.txt"), "test");
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        var selected = Assert.Single(workspace.GetEntries(Path.Combine("parent", "child")));
        navigation.Select(selected);
        Directory.Delete(Path.Combine(root, "parent", "child"), recursive: true);

        var view = navigation.Reconcile(selected, ["parent", Path.Combine("parent", "child")]);

        Assert.Equal("parent", view.Path);
        Assert.Equal("parent", navigation.CurrentFolder);
        Assert.Equal(new[] { "parent" }, view.ExpandedPaths);
        Assert.Equal("", navigation.ParentFolder);
        Assert.Equal("parent", navigation.Breadcrumbs()[1].RelativePath);
    }

    [Fact]
    public void SelectedFolder_DeletedOutside_FallsBackToTheNearestParent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(root, "parent", "child"));
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        var child = Assert.Single(workspace.GetEntries("parent"));
        navigation.Select(child);
        Directory.Delete(Path.Combine(root, "parent", "child"), recursive: true);

        var view = navigation.Reconcile(child, []);

        Assert.Equal("parent", view.Path);
        Assert.Equal("parent", navigation.CurrentFolder);
    }

    [Fact]
    public void FirstRestore_UsesPersistedFolder_WithoutWalkingEntireTree()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "saved", "nested"));
        var navigation = _harness.TreeStateFor(workspace);
        navigation.Reconcile(null, []);
        navigation.Select(Assert.Single(workspace.GetEntries("saved")));
        workspace.SaveState();

        var restarted = _harness.CreateInitializedWorkspace();
        var restartedNavigation = _harness.TreeStateFor(restarted);

        Assert.Equal(Path.Combine("saved", "nested"), restartedNavigation.Reconcile(null, []).Path);
        Assert.Equal(new[] { "", "saved", Path.Combine("saved", "nested") },
            restartedNavigation.Breadcrumbs().Select(c => c.RelativePath));
    }
}
