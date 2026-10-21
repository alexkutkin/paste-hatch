namespace PasteHatch.Core.Tests;

public sealed class BasketTransferTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void CopySelection_CanBePastedRepeatedlyWithoutLosingIntent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: false);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(1, workspace.AddFromClipboard());
        Assert.Equal(1, workspace.AddFromClipboard());
        Assert.Equal(3, Directory.GetFiles(root).Length);
        Assert.All(Directory.GetFiles(root), p => Assert.Equal("original", File.ReadAllText(p)));
    }

    [Fact]
    public void CutSelection_IsHandledByWorkspaceWithoutWindowState()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        Directory.CreateDirectory(Path.Combine(root, "dest"));
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries(""), e => !e.IsFolder), move: true);
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(1, workspace.AddFromClipboard("dest"));
        Assert.False(File.Exists(source));
        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "dest", "note.txt")));
    }

    [Fact]
    public void ClipboardReplacement_DiscardsOldCopyIntent()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "original");
        workspace.CopyEntryToClipboard(Assert.Single(workspace.GetEntries("")), move: false);
        _harness.Clipboard.Payload = PastePayload.FromText("replacement");
        workspace.AddFromClipboard();
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(0, workspace.AddFromClipboard());
        Assert.False(File.Exists(Path.Combine(root, "note (2).txt")));
    }

    [Theory]
    [InlineData("import")]
    [InlineData("copy")]
    [InlineData("move")]
    public void PartialTransfer_PublishesFinalErrorAndKeepsSuccessfulFiles(string intent)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var root = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("source.txt", "payload");
        var changes = new List<(bool Error, WorkspaceChangeKind Kind)>();
        workspace.Changed += (_, e) => changes.Add((workspace.StatusIsError, e.Kind));
        string[] paths = [source, Path.Combine(_harness.Temp.SourceRoot, "missing.txt")];

        var count = intent == "import" ? workspace.AddFiles(paths) : workspace.PasteFiles(paths, "", intent == "move");

        Assert.Equal(1, count);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(root, "source.txt")));
        Assert.Single(changes);
        Assert.Equal((true, WorkspaceChangeKind.Contents), changes[0]);
        Assert.Equal(intent != "move", File.Exists(source));
    }
}
