using System.Text;

namespace PasteHatch.Core.Tests;

/// <summary>
/// Shared arrangement for workspace tests: a unique temp directory, controllable fakes and the
/// real <see cref="TabStateStore"/> pointed at a temp state file (so persistence is exercised for
/// real, just against throw-away storage).
/// </summary>
internal sealed class WorkspaceHarness : IDisposable
{
    private readonly List<BasketWorkspace> _workspaces = [];

    public WorkspaceHarness()
    {
        Temp = new TempDirectory();
        Store = new TabStateStore(Temp.StateFile);
    }

    public TempDirectory Temp { get; }

    public FakeClock Clock { get; } = new();

    public FakeClipboardReader Clipboard { get; } = new();

    public FakeShellService Shell { get; } = new();

    public FakeConfirmationService Confirmations { get; } = new();

    public TabStateStore Store { get; }

    /// <summary>Creates a workspace without calling <see cref="BasketWorkspace.Initialize"/>.</summary>
    public BasketWorkspace CreateWorkspace()
    {
        var workspace = new BasketWorkspace(
            Temp.BasketsRoot,
            Store,
            Clipboard,
            Shell,
            Confirmations,
            Clock);
        _workspaces.Add(workspace);
        return workspace;
    }

    public BasketWorkspace CreateInitializedWorkspace()
    {
        var workspace = CreateWorkspace();
        workspace.Initialize();
        return workspace;
    }

    /// <summary>Reads the persisted state with a fresh store instance, as the next launch would.</summary>
    public WorkspaceState ReadSavedState() => new TabStateStore(Temp.StateFile).Load();

    public void Dispose() => Temp.Dispose();
}

internal static class WorkspaceAssertions
{
    public static BasketTab RequireActiveTab(this BasketWorkspace workspace)
        => workspace.ActiveTab ?? throw new InvalidOperationException("The workspace has no active tab.");

    /// <summary>Creates a new basket and fails the test if the workspace refused to create one.</summary>
    public static BasketTab CreateAndRequireTab(this BasketWorkspace workspace)
        => workspace.CreateTab() ?? throw new InvalidOperationException("Expected a new basket tab to be created.");

    public static Material RequireSingleMaterial(this BasketWorkspace workspace)
        => Assert.Single(workspace.Materials);
}

internal static class FileAssert
{
    public static void Exists(string path) => Assert.True(File.Exists(path), $"Expected file to exist: {path}");

    public static void DoesNotExist(string path) => Assert.False(File.Exists(path), $"Expected file to be absent: {path}");

    /// <summary>Asserts exact UTF-8 bytes, which also proves the file has no BOM.</summary>
    public static void HasUtf8TextWithoutBom(string path, string expectedText)
    {
        Exists(path);
        Assert.Equal(Encoding.UTF8.GetBytes(expectedText), File.ReadAllBytes(path));
    }

    public static void HasText(string path, string expectedText)
    {
        Exists(path);
        Assert.Equal(expectedText, File.ReadAllText(path, Encoding.UTF8));
    }
}
