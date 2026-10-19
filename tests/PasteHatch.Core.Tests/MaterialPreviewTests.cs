using System.Text;

namespace PasteHatch.Core.Tests;

public sealed class MaterialPreviewTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("report.txt")]
    [InlineData("report.md")]
    [InlineData("report.markdown")]
    [InlineData("REPORT.TXT")]
    [InlineData("Report.Md")]
    public void SupportsPreview_ForSupportedExtension_ReturnsTrue(string fileName)
    {
        Assert.True(MaterialPreview.SupportsPreview(fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("report")]
    [InlineData("report.pdf")]
    [InlineData("report.png")]
    [InlineData("report.url")]
    [InlineData("report.txt.bak")]
    public void SupportsPreview_ForUnsupportedFileName_ReturnsFalse(string? fileName)
    {
        Assert.False(MaterialPreview.SupportsPreview(fileName));
    }

    [Fact]
    public void Read_ReturnsFirstTwoNonEmptyLinesSkippingLeadingBlankLines()
    {
        var path = _temp.CreateSourceFile(
            "notes.txt",
            "\r\n  \r\nFirst line\r\n  Second line  \r\nThird line\r\n");

        var preview = MaterialPreview.Read(path, "notes.txt");

        Assert.Equal("First line\nSecond line", preview);
    }

    [Fact]
    public void Read_ForSingleLineWithoutTrailingNewline_ReturnsThatLine()
    {
        var path = _temp.CreateSourceFile("notes.txt", "Только одна строка");

        var preview = MaterialPreview.Read(path, "notes.txt");

        Assert.Equal("Только одна строка", preview);
    }

    [Fact]
    public void Read_ForEmptyOrWhitespaceOnlyFile_ReturnsNull()
    {
        var path = _temp.CreateSourceFile("empty.txt", "  \r\n\t\r\n   ");

        Assert.Null(MaterialPreview.Read(path, "empty.txt"));
    }

    [Fact]
    public void Read_ForMissingFile_ReturnsNull()
    {
        var path = Path.Combine(_temp.SourceRoot, "missing.txt");

        Assert.Null(MaterialPreview.Read(path, "missing.txt"));
    }

    [Fact]
    public void Read_ForUnsupportedExtension_ReturnsNullEvenWhenFileExists()
    {
        var path = _temp.CreateSourceFile("data.bin", "plain text inside");

        Assert.Null(MaterialPreview.Read(path, "data.bin"));
    }

    [Fact]
    public void Read_ForMissingPath_ReturnsNull()
    {
        Assert.Null(MaterialPreview.Read(null, "notes.txt"));
        Assert.Null(MaterialPreview.Read(string.Empty, "notes.txt"));
        Assert.Null(MaterialPreview.Read("   ", "notes.txt"));
    }

    [Fact]
    public void Read_DecodesUtf8BomWithoutIncludingBomCharacter()
    {
        var path = Path.Combine(_temp.SourceRoot, "bom.md");
        File.WriteAllText(path, "# Заголовок", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var preview = MaterialPreview.Read(path, "bom.md");

        Assert.Equal("# Заголовок", preview);
        Assert.DoesNotContain("\uFEFF", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TruncatesLongContentToMaxCharactersAndMarksItWithEllipsis()
    {
        var first = new string('о', 150);
        var second = new string('т', 150);
        var path = _temp.CreateSourceFile("long.txt", $"{first}\n{second}");

        var preview = MaterialPreview.Read(path, "long.txt");

        Assert.NotNull(preview);
        Assert.True(
            preview!.Length <= MaterialPreview.MaxCharacters + 1,
            $"Excerpt length {preview.Length} exceeds {MaterialPreview.MaxCharacters + 1}.");
        Assert.True(preview.EndsWith('…'));
        Assert.Equal(first + "\n" + second[..49] + "…", preview);
    }

    [Fact]
    public void Read_ForVeryLongFirstLine_ReturnsBoundedExcerpt()
    {
        var path = _temp.CreateSourceFile("huge.txt", new string('я', 10_000));

        var preview = MaterialPreview.Read(path, "huge.txt");

        Assert.NotNull(preview);
        Assert.True(preview!.Length <= MaterialPreview.MaxCharacters + 1);
        Assert.True(preview.EndsWith('…'));
        Assert.Equal(new string('я', MaterialPreview.MaxCharacters) + "…", preview);
    }

    [Fact]
    public void Read_TrimsTrailingWhitespaceBeforeAppendingEllipsis()
    {
        var path = _temp.CreateSourceFile("spaces.txt", new string('a', 190) + new string(' ', 20) + new string('b', 30));

        var preview = MaterialPreview.Read(path, "spaces.txt");

        Assert.Equal(new string('a', 190) + "…", preview);
    }

    [Fact]
    public void Read_WhenFileIsOpenInAnotherApp_StillReturnsPreview()
    {
        var path = _temp.CreateSourceFile("shared.txt", "Открыт в другом приложении\r\nвторая строка");
        using var holder = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        var preview = MaterialPreview.Read(path, "shared.txt");

        Assert.Equal("Открыт в другом приложении\nвторая строка", preview);
    }
}
