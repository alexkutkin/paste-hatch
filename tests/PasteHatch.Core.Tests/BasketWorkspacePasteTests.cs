using System.Text;

namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspacePasteTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void AddFromClipboard_TextPaste_CreatesTxtWithExactUtf8Content()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        const string text = "Grüße, 世界!\r\nSecond line - ü";
        _harness.Clipboard.Payload = PastePayload.FromText(text);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(1, added);
        var filePath = Path.Combine(directory, "Mon, 13-02.txt");
        FileAssert.HasUtf8TextWithoutBom(filePath, text);
        var file = workspace.RequireSingleFile();
        Assert.Equal("Mon, 13-02.txt", file.Name);
        Assert.Equal(MaterialKind.Text, file.Kind);
        Assert.False(workspace.StatusIsError);
    }

    [Fact]
    public void AddFromClipboard_SecondTextPasteWithinSameMinute_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        _harness.Clipboard.Payload = PastePayload.FromText("first");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        _harness.Clipboard.Payload = PastePayload.FromText("second");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        _harness.Clipboard.Payload = PastePayload.FromText("third");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Mon, 13-02.txt"), "first");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Mon, 13-02 (2).txt"), "second");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Mon, 13-02 (3).txt"), "third");
        Assert.Equal(3, workspace.Files(string.Empty).Count);
        Assert.All(workspace.Files(string.Empty), file => Assert.Equal(MaterialKind.Text, file.Kind));
    }

    [Fact]
    public void AddFromClipboard_HttpsLinkPaste_CreatesInternetShortcut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromText("https://example.com/page?q=1");

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(1, added);
        var urlPath = Path.Combine(directory, "Mon, 13-02.url");
        Assert.Equal(
            "[InternetShortcut]\r\nURL=https://example.com/page?q=1\r\n",
            File.ReadAllText(urlPath, Encoding.UTF8));
        var file = workspace.RequireSingleFile();
        Assert.Equal("Mon, 13-02.url", file.Name);
        Assert.Equal(MaterialKind.Link, file.Kind);
    }

    [Fact]
    public void AddFromClipboard_ImagePaste_WritesExactPngBytes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromImage(PngBytes);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(1, added);
        var filePath = Path.Combine(directory, "Mon, 13-02.png");
        FileAssert.Exists(filePath);
        Assert.Equal(PngBytes, File.ReadAllBytes(filePath));
        var file = workspace.RequireSingleFile();
        Assert.Equal("Mon, 13-02.png", file.Name);
        Assert.Equal(MaterialKind.Image, file.Kind);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardHoldsNoSupportedData_ReturnsZeroAndSetsStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.None;

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(0, added);
        Assert.NotNull(workspace.StatusMessage);
        Assert.Empty(workspace.Files(string.Empty));
        Assert.Equal(1, _harness.Clipboard.ReadCount);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardHoldsOnlyWhitespaceText_ReturnsZeroAndCreatesNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromText("   ");

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(0, added);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardReaderThrows_ReportsErrorAndReturnsZero()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.FailureToThrow = new InvalidOperationException("clipboard is busy");

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(0, added);
        workspace.RequireError();
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void AddFromClipboard_FilePayload_CopiesFilesIntoActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var binarySource = _harness.Temp.CreateSourceFile("alpha.bin", "binary payload");
        var textSource = _harness.Temp.CreateSourceFile("beta.txt", "beta");
        _harness.Clipboard.Payload = PastePayload.FromFiles([binarySource, textSource]);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(2, added);
        Assert.Equal("binary payload", File.ReadAllText(Path.Combine(directory, "alpha.bin")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "beta.txt"), "beta");
        Assert.Equal(2, workspace.Files(string.Empty).Count);
        Assert.True(File.Exists(binarySource));
        Assert.True(File.Exists(textSource));
    }

    [Fact]
    public void AddFromClipboard_FilePayloadWithNameCollision_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var firstSource = _harness.Temp.CreateSourceFile("report.txt", "first");
        _harness.Clipboard.Payload = PastePayload.FromFiles([firstSource]);
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        var secondSource = _harness.Temp.CreateSourceFile(Path.Combine("other", "report.txt"), "second");
        _harness.Clipboard.Payload = PastePayload.FromFiles([secondSource]);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(1, added);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "report.txt"), "first");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "report (2).txt"), "second");
        Assert.Equal(2, workspace.Files(string.Empty).Count);
    }

    [Fact]
    public void AddFromClipboard_FileAlreadyInsideBasket_IsSkipped()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("report.txt", "data");
        Assert.Equal(1, workspace.AddFiles([source], string.Empty));
        _harness.Clipboard.Payload = PastePayload.FromFiles([Path.Combine(directory, "report.txt")]);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(0, added);
        Assert.Single(workspace.Files(string.Empty));
    }

    [Fact]
    public void AddFromClipboard_MixedInsideAndOutsideFiles_AddsOnlyOutsideFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("report.txt", "data");
        Assert.Equal(1, workspace.AddFiles([source], string.Empty));
        var outside = _harness.Temp.CreateSourceFile("other.txt", "other");
        _harness.Clipboard.Payload = PastePayload.FromFiles([Path.Combine(directory, "report.txt"), outside]);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(1, added);
        Assert.Equal(2, workspace.Files(string.Empty).Count);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "other.txt"), "other");
    }

    [Fact]
    public void AddFromClipboard_FilePayloadWithEmptyList_ReturnsZeroAndSetsStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromFiles([]);

        var added = workspace.AddFromClipboard(string.Empty);

        Assert.Equal(0, added);
        Assert.NotNull(workspace.StatusMessage);
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void AddFiles_ForDirectoryPath_CopiesFolderWithItsContents()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directoryPath = _harness.Temp.CreateSourceDirectory("dropped-folder");
        _harness.Temp.CreateSourceFile(Path.Combine("dropped-folder", "nested", "note.txt"), "inside");

        var added = workspace.AddFiles([directoryPath], string.Empty);

        Assert.Equal(1, added);
        var basketDirectory = workspace.RequireActiveTab().DirectoryPath;
        FileAssert.HasUtf8TextWithoutBom(
            Path.Combine(basketDirectory, "dropped-folder", "nested", "note.txt"),
            "inside");
        Assert.Single(workspace.GetEntries(string.Empty), entry => entry.IsFolder && entry.Name == "dropped-folder");
        Assert.False(workspace.StatusIsError);
    }

    [Fact]
    public void AddFiles_ForFolderAlreadyInsideBasket_IsSkipped()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var source = _harness.Temp.CreateSourceDirectory("source");
        _harness.Temp.CreateSourceFile(Path.Combine("source", "file.txt"), "x");
        Assert.Equal(1, workspace.AddFiles([source], string.Empty));
        var copiedFolder = Path.Combine(workspace.RequireActiveTab().DirectoryPath, "source");

        var added = workspace.AddFiles([copiedFolder], string.Empty);

        Assert.Equal(0, added);
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void AddFiles_ForMissingFile_ReportsProblemAndAddsNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var activeDirectory = workspace.RequireActiveTab().DirectoryPath;
        var missingPath = Path.Combine(_harness.Temp.Root, "missing.bin");

        var added = workspace.AddFiles([missingPath], string.Empty);

        Assert.Equal(0, added);
        Assert.Empty(workspace.Files(string.Empty));
        workspace.RequireError();
        Assert.Empty(Directory.GetFiles(activeDirectory));
    }

    [Fact]
    public void AddFiles_AddsOnlyToTheActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstDirectory = workspace.RequireActiveTab().DirectoryPath;
        var secondTab = workspace.CreateAndRequireTab();
        var source = _harness.Temp.CreateSourceFile("context.txt", "context");

        var added = workspace.AddFiles([source], string.Empty);

        Assert.Equal(1, added);
        FileAssert.Exists(Path.Combine(secondTab.DirectoryPath, "context.txt"));
        Assert.Empty(Directory.GetFiles(firstDirectory));
    }

    [Fact]
    public void AddQuickNote_CreatesTxtNamedByClock()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        const string noteText = "Note: buy bread";

        var note = workspace.AddQuickNote(noteText, string.Empty);

        Assert.NotNull(note);
        Assert.Equal("Mon, 13-02.txt", note.Name);
        Assert.Equal(noteText, File.ReadAllText(note.FullPath, Encoding.UTF8));
        Assert.Equal(Encoding.UTF8.GetBytes(noteText), File.ReadAllBytes(note.FullPath));
        Assert.Equal(MaterialKind.Text, note.Kind);
        Assert.Equal(note.FullPath, workspace.RequireSingleFile().FullPath);
    }

    [Fact]
    public void AddQuickNote_SecondNoteAtSameMoment_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        var first = workspace.AddQuickNote("first note", string.Empty);
        var second = workspace.AddQuickNote("second note", string.Empty);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Mon, 13-02.txt", first.Name);
        Assert.Equal("Mon, 13-02 (2).txt", second.Name);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Mon, 13-02.txt"), "first note");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Mon, 13-02 (2).txt"), "second note");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    public void AddQuickNote_ForBlankText_ReturnsNullAndCreatesNothing(string? noteText)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        var note = workspace.AddQuickNote(noteText, string.Empty);

        Assert.Null(note);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void AddQuickNote_UsesTheClockAtCallTime()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clock.Now = TestMoments.ThursdayMidnight;

        var note = workspace.AddQuickNote("night note", string.Empty);

        Assert.NotNull(note);
        Assert.Equal("Thu, 00-05.txt", note.Name);
    }
}
