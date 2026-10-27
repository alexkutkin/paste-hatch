namespace PasteHatch.Core.Tests;

/// <summary>
/// Window geometry is recorded in the same state file as the baskets: usable values survive a
/// restart and unusable ones never replace a saved value.
/// </summary>
public sealed class BasketWorkspaceWindowTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Initialize_WithoutSavedWindow_LeavesPlacementUnset()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.Null(workspace.Geometry.Placement);
    }

    [Fact]
    public void SaveWindowPlacement_UsablePlacement_IsPersistedAndRestored()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var placement = new WindowPlacementState { Left = 120, Top = 80, Width = 900, Height = 700 };

        Assert.True(workspace.Geometry.Save(placement));
        workspace.SaveState();

        AssertSamePlacement(placement, workspace.Geometry.Placement);
        AssertSamePlacement(placement, _harness.ReadSavedState().Window);

        var nextLaunch = _harness.CreateWorkspace();
        nextLaunch.Initialize();
        AssertSamePlacement(placement, nextLaunch.Geometry.Placement);
    }

    [Theory]
    [InlineData(399, 300)]
    [InlineData(400, 299)]
    [InlineData(399, 299)]
    public void SaveWindowPlacement_TooSmallPlacement_IsIgnored(int width, int height)
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.False(workspace.Geometry.Save(new WindowPlacementState { Width = width, Height = height }));

        Assert.Null(workspace.Geometry.Placement);
        Assert.Null(_harness.ReadSavedState().Window);
    }

    [Fact]
    public void SaveWindowPlacement_MinimumUsableSize_IsAccepted()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var placement = new WindowPlacementState { Left = 0, Top = 0, Width = 400, Height = 300 };

        Assert.True(workspace.Geometry.Save(placement));

        AssertSamePlacement(placement, workspace.Geometry.Placement);
    }

    [Fact]
    public void SaveWindowPlacement_UnusablePlacement_DoesNotReplaceGoodPlacement()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var goodPlacement = new WindowPlacementState { Left = 50, Top = 60, Width = 800, Height = 600 };
        workspace.Geometry.Save(goodPlacement);
        workspace.SaveState();

        workspace.Geometry.Save(new WindowPlacementState { Width = 100, Height = 100 });

        AssertSamePlacement(goodPlacement, workspace.Geometry.Placement);
        AssertSamePlacement(goodPlacement, _harness.ReadSavedState().Window);
    }

    private static void AssertSamePlacement(WindowPlacementState expected, WindowPlacementState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Top, actual.Top);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
    }

    [Fact]
    public void PreviewWidth_PersistsAcrossOtherChangesAndRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        Assert.True(workspace.Geometry.SavePreviewWidth(560.5));
        workspace.SaveState();
        workspace.CreateTab();
        workspace.AddQuickNote("note", string.Empty);

        var restarted = _harness.CreateInitializedWorkspace();

        Assert.Equal(560.5, restarted.Geometry.PreviewWidth);
        Assert.Equal(560.5, _harness.ReadSavedState().PreviewWidth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PreviewWidth_InvalidResizeDoesNotReplaceSavedWidth(double width)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        workspace.Geometry.SavePreviewWidth(600);
        workspace.SaveState();

        Assert.False(workspace.Geometry.SavePreviewWidth(width));

        Assert.Equal(600, _harness.CreateInitializedWorkspace().Geometry.PreviewWidth);
    }

}
