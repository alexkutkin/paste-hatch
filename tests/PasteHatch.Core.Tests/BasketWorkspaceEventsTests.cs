namespace PasteHatch.Core.Tests;

/// <summary>
/// One user action must not flood the refresh coordinator, which coalesces anyway: these are burst
/// regressions, plus the publication rules of the single publish point (errors travel as state,
/// a declined confirmation changes nothing, and every mutation publishes).
/// </summary>
public sealed class BasketWorkspaceEventsTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Initialize_PublishesOneChange()
    {
        var workspace = _harness.CreateWorkspace();
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        workspace.Initialize();

        Assert.Equal([WorkspaceChangeKind.View], kinds);
    }

    [Fact]
    public void PasteText_PublishesOneChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("text");
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.Equal([WorkspaceChangeKind.Contents], kinds);
    }

    [Fact]
    public void AddQuickNote_PublishesOneChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.NotNull(workspace.AddQuickNote("note", string.Empty));

        Assert.Equal([WorkspaceChangeKind.Contents], kinds);
    }

    [Fact]
    public void RenameEntry_PublishesOneChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("text");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        var material = workspace.RequireSingleFile();
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.True(workspace.RenameEntry(material, "renamed"));

        Assert.Equal([WorkspaceChangeKind.Contents], kinds);
    }

    [Fact]
    public void ErrorPath_PublishesTheErrorStatusOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "sub"));
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.Null(workspace.CreateFolder("sub", string.Empty));

        Assert.Equal([WorkspaceChangeKind.Status], kinds);
        workspace.RequireError();
    }

    [Fact]
    public void DeclinedConfirmation_PublishesNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        _harness.Confirmations.ConfirmBasketResult = false;
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.False(workspace.CloseTab(firstTab.Uid));

        Assert.Empty(kinds);
    }

    [Fact]
    public void ReorderTabs_PublishesViewChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = workspace.RequireActiveTab();
        var second = workspace.CreateAndRequireTab();
        var kinds = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => kinds.Add(change.Kind);

        Assert.True(workspace.ReorderTabs([second.Uid, first.Uid]));

        Assert.Equal([WorkspaceChangeKind.View], kinds);
    }
}
