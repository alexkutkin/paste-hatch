namespace PasteHatch.Core.Tests;

/// <summary>
/// Tab order is part of the persisted workspace: dragging baskets in the TabView must survive a
/// restart and must never lose a tab, even if the drag payload is stale or incomplete.
/// </summary>
public sealed class BasketWorkspaceTabOrderTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void ReorderTabs_PersistsOrderAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, second, third) = CreateThreeTabs(workspace);

        Assert.True(workspace.ReorderTabs([third.Uid, first.Uid, second.Uid]));

        Assert.Equal([third.Uid, first.Uid, second.Uid], TabUids(workspace));
        Assert.Equal(third.Uid, workspace.ActiveTab?.Uid);

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();

        Assert.Equal([third.Uid, first.Uid, second.Uid], TabUids(restarted));
        Assert.Equal(third.Uid, restarted.ActiveTab?.Uid);
    }

    [Fact]
    public void ReorderTabs_IsCaseSensitiveAndIgnoresUnknownUids()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, second, third) = CreateThreeTabs(workspace);

        Assert.True(workspace.ReorderTabs([third.Uid, "NOPE", second.Uid]));

        // Unknown ids are dropped; tabs missing from the list keep their relative order at the end.
        Assert.Equal([third.Uid, second.Uid, first.Uid], TabUids(workspace));
    }

    [Fact]
    public void ReorderTabs_WithTheSameOrder_ReportsNoChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, second, third) = CreateThreeTabs(workspace);
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.False(workspace.ReorderTabs([first.Uid, second.Uid, third.Uid]));
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void MoveTab_MovesOnePositionAndPersists()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, second, third) = CreateThreeTabs(workspace);

        Assert.True(workspace.MoveTab(second.Uid, -1));
        Assert.Equal([second.Uid, first.Uid, third.Uid], TabUids(workspace));

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();
        Assert.Equal([second.Uid, first.Uid, third.Uid], TabUids(restarted));
        Assert.Equal(third.Uid, restarted.ActiveTab?.Uid);
    }

    [Fact]
    public void MoveTab_ChangesTheActiveTabOrderButNotTheActiveTab()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, second, third) = CreateThreeTabs(workspace);

        Assert.True(workspace.MoveTab(third.Uid, -1));

        Assert.Equal([first.Uid, third.Uid, second.Uid], TabUids(workspace));
        Assert.Equal(third.Uid, workspace.ActiveTab?.Uid);
    }

    [Fact]
    public void MoveTab_AtTheEdge_LeavesOrderUntouched()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var (first, _, third) = CreateThreeTabs(workspace);

        Assert.False(workspace.MoveTab(first.Uid, -1));
        Assert.False(workspace.MoveTab(third.Uid, 1));
        Assert.False(workspace.MoveTab("NOPE", 1));
    }

    private static (BasketTab First, BasketTab Second, BasketTab Third) CreateThreeTabs(BasketWorkspace workspace)
    {
        var first = workspace.RequireActiveTab();
        var second = workspace.CreateAndRequireTab();
        var third = workspace.CreateAndRequireTab();
        return (first, second, third);
    }

    private static string[] TabUids(BasketWorkspace workspace)
        => workspace.Tabs.Select(tab => tab.Uid).ToArray();
}
