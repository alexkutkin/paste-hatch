namespace PasteHatch.Core.Tests;

public sealed class BasketTreeNavigationTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

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
    public void SwitchingBaskets_RestoresEachSelectionAndExpandedPaths()
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
        Assert.Equal("", secondView.Path);
        Assert.Empty(secondView.ExpandedPaths);

        workspace.ActivateTab(first.Uid);
        var firstView = navigation.Reconcile(null, [""]);

        Assert.Equal(Path.Combine("same", "first.txt"), firstView.Path);
        Assert.Contains("same", firstView.ExpandedPaths);
        Assert.Equal("same", navigation.CurrentFolder);
        Assert.Equal("same", navigation.FolderFor(first.Uid));
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
