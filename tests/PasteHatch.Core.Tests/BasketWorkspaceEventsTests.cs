namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceEventsTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Initialize_RaisesChangedOnce()
    {
        var workspace = _harness.CreateWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.Initialize();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void CreateTab_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.CreateTab();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ActivateTab_WhenSwitchingBaskets_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.ActivateTab(firstTab.Uid);

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void AddFromClipboard_WhenTextIsPasted_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("текст");
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.Equal(1, workspace.AddFromClipboard());

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void AddQuickNote_WhenNoteIsCreated_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.NotNull(workspace.AddQuickNote("заметка"));

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void RenameMaterial_WhenRenameSucceeds_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("текст");
        Assert.Equal(1, workspace.AddFromClipboard());
        var material = workspace.RequireSingleMaterial();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.True(workspace.RenameMaterial(material, "переименованный"));

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void DeleteMaterial_WhenConfirmed_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("текст");
        Assert.Equal(1, workspace.AddFromClipboard());
        var material = workspace.RequireSingleMaterial();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.True(workspace.DeleteMaterial(material));

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void CopyActiveBasketPath_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.CopyActiveBasketPath();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void CopyMaterialPath_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromText("текст");
        Assert.Equal(1, workspace.AddFromClipboard());
        var material = workspace.RequireSingleMaterial();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.CopyMaterialPath(material);

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void CloseTab_WhenConfirmed_RaisesChangedOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.True(workspace.CloseTab(firstTab.Uid));

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void CloseTab_WhenConfirmationDeclined_DoesNotRaiseChanged()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        _harness.Confirmations.ConfirmBasketResult = false;
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.False(workspace.CloseTab(firstTab.Uid));

        Assert.Equal(0, changedCount);
    }
}
