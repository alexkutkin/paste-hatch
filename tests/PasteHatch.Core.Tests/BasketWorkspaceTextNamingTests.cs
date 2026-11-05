namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceTextNamingTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("Release notes for the spring update and more", "Release_notes_for_the_spring.txt")]
    [InlineData("Привет мир, это заметка для агента", "Привет_мир_это_заметка_для.txt")]
    [InlineData("  \r\n\tTwo\t\twords  \r\n", "Two_words.txt")]
    [InlineData("Hello, world! (draft) — v2.", "Hello_world_draft_v2.txt")]
    [InlineData("don't stop-the-music now", "don't_stop-the-music_now.txt")]
    [InlineData(@"a/b\c:d*e?f""g<h>i|j", "a_b_c_d_e.txt")]
    [InlineData("CON", "CON_.txt")]
    [InlineData("nul\nsecond line", "nul_second_line.txt")]
    public void PastedText_IsNamedFromItsFirstFiveWords(string text, string expectedName)
    {
        var workspace = _harness.CreateInitializedWorkspace();

        var file = PasteText(workspace, text);

        Assert.Equal(expectedName, file.Name);
        FileAssert.HasUtf8TextWithoutBom(file.FullPath, text);
    }

    [Theory]
    [InlineData("!!! ??? ...")]
    [InlineData("— * —")]
    public void PastedText_WithoutUsableWords_FallsBackToTheTimestampName(string text)
    {
        var workspace = _harness.CreateInitializedWorkspace();

        var file = PasteText(workspace, text);

        Assert.Equal("Mon, 13-02.txt", file.Name);
        FileAssert.HasUtf8TextWithoutBom(file.FullPath, text);
    }

    [Fact]
    public void PastedText_WithAVeryLongWord_IsBoundedToTheMaximumFileNameLength()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var text = new string('ж', 400) + " tail";

        var file = PasteText(workspace, text);

        Assert.Equal(new string('ж', MaterialNaming.MaxBaseNameLength) + ".txt", file.Name);
        FileAssert.HasUtf8TextWithoutBom(file.FullPath, text);
    }

    [Fact]
    public void PastedText_WithTheSameFirstWords_GetsCollisionFreeNames()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        PasteText(workspace, "Meeting notes for Monday morning: first");
        _harness.Clipboard.Payload = PastePayload.FromText("Meeting notes for Monday morning: second");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Meeting_notes_for_Monday_morning.txt"), "Meeting notes for Monday morning: first");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Meeting_notes_for_Monday_morning (2).txt"), "Meeting notes for Monday morning: second");
    }

    [Fact]
    public void EditingPastedText_DoesNotRenameTheDocument()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var file = PasteText(workspace, "Original first words here");

        File.WriteAllText(file.FullPath, "Completely different content");
        workspace.Refresh();

        Assert.Equal("Original_first_words_here.txt", workspace.RequireSingleFile().Name);
    }

    [Fact]
    public void PastedUrl_KeepsTheTimestampShortcutName()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        var file = PasteText(workspace, "https://example.com/some/page");

        Assert.Equal("Mon, 13-02.url", file.Name);
    }

    [Fact]
    public void ImportedTextFiles_KeepTheirNames()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var source = _harness.Temp.CreateSourceFile("My notes.txt", "Some different words inside");
        _harness.Clipboard.Payload = PastePayload.FromFiles([source]);

        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));

        Assert.Equal("My notes.txt", workspace.RequireSingleFile().Name);
    }

    private BasketEntry PasteText(BasketWorkspace workspace, string text)
    {
        _harness.Clipboard.Payload = PastePayload.FromText(text);
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        return Assert.Single(workspace.Files(string.Empty));
    }
}
