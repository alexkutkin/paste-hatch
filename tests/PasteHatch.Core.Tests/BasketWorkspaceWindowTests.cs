namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceWindowTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Initialize_WithoutSavedWindow_LeavesPlacementUnset()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.Null(workspace.WindowPlacement);
    }

    [Fact]
    public void SaveWindowPlacement_UsablePlacement_IsPersistedAndRestored()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var placement = new WindowPlacementState { Left = 120, Top = 80, Width = 900, Height = 700 };

        workspace.SaveWindowPlacement(placement);

        AssertSamePlacement(placement, workspace.WindowPlacement);
        AssertSamePlacement(placement, _harness.ReadSavedState().Window);

        var nextLaunch = _harness.CreateWorkspace();
        nextLaunch.Initialize();
        AssertSamePlacement(placement, nextLaunch.WindowPlacement);
    }

    [Theory]
    [InlineData(399, 300)]
    [InlineData(400, 299)]
    [InlineData(399, 299)]
    public void SaveWindowPlacement_TooSmallPlacement_IsIgnored(int width, int height)
    {
        var workspace = _harness.CreateInitializedWorkspace();

        workspace.SaveWindowPlacement(new WindowPlacementState { Width = width, Height = height });

        Assert.Null(workspace.WindowPlacement);
        Assert.Null(_harness.ReadSavedState().Window);
    }

    [Fact]
    public void SaveWindowPlacement_NullPlacement_IsIgnored()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        workspace.SaveWindowPlacement(null);

        Assert.Null(workspace.WindowPlacement);
        Assert.Null(_harness.ReadSavedState().Window);
    }

    [Fact]
    public void SaveWindowPlacement_MinimumUsableSize_IsAccepted()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var placement = new WindowPlacementState { Left = 0, Top = 0, Width = 400, Height = 300 };

        workspace.SaveWindowPlacement(placement);

        AssertSamePlacement(placement, workspace.WindowPlacement);
    }

    [Fact]
    public void SaveWindowPlacement_UnusablePlacement_DoesNotReplaceGoodPlacement()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var goodPlacement = new WindowPlacementState { Left = 50, Top = 60, Width = 800, Height = 600 };
        workspace.SaveWindowPlacement(goodPlacement);

        workspace.SaveWindowPlacement(new WindowPlacementState { Width = 100, Height = 100 });

        AssertSamePlacement(goodPlacement, workspace.WindowPlacement);
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
}
