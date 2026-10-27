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
    private readonly Dictionary<BasketWorkspace, BasketTreeNavigation> _treeStates = new();

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

    public ReswTextProvider Text { get; } = ReswTextProvider.English;

    public TabStateStore Store { get; }

    /// <summary>Creates a workspace without calling <see cref="BasketWorkspace.Initialize"/>.</summary>
    public BasketWorkspace CreateWorkspace()
    {
        BasketWorkspace? workspace = null;
        workspace = new BasketWorkspace(
            Temp.BasketsRoot,
            Store,
            Clipboard,
            Shell,
            Confirmations,
            Clock,
            new TabNameFormatter(Text.Culture),
            Text,
            currentFolderOf: tab => TreeStateFor(workspace!).FolderFor(tab.Uid));
        _workspaces.Add(workspace);
        return workspace;
    }

    /// <summary>Tree state of a workspace, created on demand and wired as its folder source.</summary>
    public BasketTreeNavigation TreeStateFor(BasketWorkspace workspace)
    {
        if (!_treeStates.TryGetValue(workspace, out var treeState))
        {
            treeState = new BasketTreeNavigation(workspace);
            _treeStates[workspace] = treeState;
        }

        return treeState;
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

    /// <summary>The file entries of a folder in the active basket, folders excluded.</summary>
    public static IReadOnlyList<BasketEntry> Files(this BasketWorkspace workspace, string? relativeFolder)
        => workspace.GetEntries(relativeFolder).Where(entry => !entry.IsFolder).ToList();

    /// <summary>Subfolder entries of a folder in the active basket.</summary>
    public static IReadOnlyList<BasketEntry> Folders(this BasketWorkspace workspace, string? relativeFolder)
        => workspace.GetEntries(relativeFolder).Where(entry => entry.IsFolder).ToList();

    /// <summary>The one file entry of the basket root; fails when there is not exactly one.</summary>
    public static BasketEntry RequireSingleFile(this BasketWorkspace workspace)
        => Assert.Single(workspace.Files(string.Empty));

    /// <summary>The recorded error message; fails when the workspace recorded no error.</summary>
    public static string RequireError(this BasketWorkspace workspace)
    {
        Assert.True(workspace.StatusIsError, $"Expected an error status, got: {workspace.StatusMessage}");
        return workspace.StatusMessage ?? string.Empty;
    }
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
