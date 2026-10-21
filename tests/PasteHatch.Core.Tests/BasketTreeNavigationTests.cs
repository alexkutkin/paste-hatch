namespace PasteHatch.Core.Tests;

public sealed class BasketTreeNavigationTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void SwitchingBaskets_RestoresEachSelectionAndExpandedPaths()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        Directory.CreateDirectory(Path.Combine(first.DirectoryPath, "same"));
        File.WriteAllText(Path.Combine(first.DirectoryPath, "same", "first.txt"), "first");
        var navigation = new BasketTreeNavigation(workspace);
        navigation.Restore();
        var selected = Assert.Single(workspace.GetEntries("same"));
        navigation.Select(selected);
        navigation.Capture(selected, ["", "same"]);

        var second = workspace.CreateAndRequireTab();
        Directory.CreateDirectory(Path.Combine(second.DirectoryPath, "same"));
        var secondView = navigation.Restore();
        Assert.Equal("", secondView.Path);
        Assert.Empty(secondView.ExpandedPaths);
        navigation.Capture(null, [""]);
        workspace.ActivateTab(first.Uid);

        var firstView = navigation.Restore();
        Assert.Equal(Path.Combine("same", "first.txt"), firstView.Path);
        Assert.Contains("same", firstView.ExpandedPaths);
        Assert.Equal("same", navigation.CurrentFolder);
        Assert.Equal("same", workspace.SelectedFolder);
    }

    [Fact]
    public void DeletedSelectedFileAndParents_FallsBackToNearestExistingAncestor()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        Directory.CreateDirectory(Path.Combine(root, "parent", "child"));
        File.WriteAllText(Path.Combine(root, "parent", "child", "file.txt"), "test");
        var navigation = new BasketTreeNavigation(workspace);
        navigation.Restore();
        var selected = Assert.Single(workspace.GetEntries(Path.Combine("parent", "child")));
        navigation.Select(selected);
        navigation.Capture(selected, ["parent", Path.Combine("parent", "child")]);
        Directory.Delete(Path.Combine(root, "parent", "child"), recursive: true);

        var view = navigation.Restore();

        Assert.Equal("parent", view.Path);
        Assert.Equal("parent", navigation.CurrentFolder);
        Assert.Equal(new[] { "parent" }, view.ExpandedPaths);
        Assert.Equal("", navigation.ParentFolder);
        Assert.Equal("parent", navigation.Breadcrumbs()[1].RelativePath);
    }

    [Fact]
    public void FirstRestore_UsesPersistedFolder_WithoutWalkingEntireTree()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "saved", "nested"));
        workspace.NavigateTo(Path.Combine("saved", "nested"));
        var restarted = _harness.CreateInitializedWorkspace();
        var navigation = new BasketTreeNavigation(restarted);

        Assert.Equal(Path.Combine("saved", "nested"), navigation.Restore().Path);
        Assert.Equal(new[] { "", "saved", Path.Combine("saved", "nested") },
            navigation.Breadcrumbs().Select(c => c.RelativePath));
    }
}
