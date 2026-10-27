using System.Text;

namespace PasteHatch.Core.Tests;

public sealed class MaterialNamingTests : IDisposable
{
    private static readonly ReswTextProvider Text = ReswTextProvider.English;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void UniqueFileName_WhenNameIsFree_ReturnsDesiredName()
    {
        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "report.txt", Text);

        Assert.Equal("report.txt", fileName);
    }

    [Fact]
    public void UniqueFileName_WhenNameExists_AppendsNumberedSuffix()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "report.txt"), "existing");

        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "report.txt", Text);

        Assert.Equal("report (2).txt", fileName);
    }

    [Fact]
    public void UniqueFileName_WhenFolderHasTheSameName_AppendsNumberedSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "report.txt"));

        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "report.txt", Text);

        Assert.Equal("report (2).txt", fileName);
    }

    [Fact]
    public void UniqueFileName_WhenNameAndSuffixTwoExist_ContinuesNumbering()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "report.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_temp.Root, "report (2).txt"), string.Empty);
        File.WriteAllText(Path.Combine(_temp.Root, "report (3).txt"), string.Empty);

        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "report.txt", Text);

        Assert.Equal("report (4).txt", fileName);
    }

    [Fact]
    public void UniqueFileName_WithoutExtension_AppendsSuffix()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "notes"), string.Empty);
        File.WriteAllText(Path.Combine(_temp.Root, "notes (2)"), string.Empty);

        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "notes", Text);

        Assert.Equal("notes (3)", fileName);
    }

    [Fact]
    public void UniqueFileName_MultiPartExtension_KeepsLastExtension()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "archive.tar.gz"), string.Empty);

        var fileName = MaterialNaming.UniqueFileName(_temp.Root, "archive.tar.gz", Text);

        Assert.Equal("archive.tar (2).gz", fileName);
    }

    [Fact]
    public void DefaultFileName_SanitizesFormatPartAndCombinesItWithExtension()
    {
        var fileName = MaterialNaming.DefaultFileName(_temp.Root, "Mon, 13:02", ".txt", Text);

        Assert.Equal("Mon, 13-02.txt", fileName);
        Assert.DoesNotContain(':', fileName);
    }

    [Fact]
    public void DefaultFileName_WhenFormattedNameAlreadyUsed_GetsSuffixBeforeExtension()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "Mon, 13-02.txt"), string.Empty);

        var fileName = MaterialNaming.DefaultFileName(_temp.Root, "Mon, 13:02", ".txt", Text);

        Assert.Equal("Mon, 13-02 (2).txt", fileName);
    }

    [Fact]
    public void DefaultFileName_SanitizesAnyInvalidCharacterInFormatPart()
    {
        var fileName = MaterialNaming.DefaultFileName(_temp.Root, "a:b*c?d", ".url", Text);

        Assert.Equal("a-b-c-d.url", fileName);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/page?query=1#fragment")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com:8443/path")]
    public void TryGetLink_ForAbsoluteHttpUrl_ReturnsTrue(string text)
    {
        var isLink = MaterialNaming.TryGetLink(text, out var link);

        Assert.True(isLink);
        Assert.NotNull(link);
        Assert.Equal(new Uri(text), link);
    }

    [Fact]
    public void TryGetLink_ForUrlWithSurroundingWhitespace_TrimsBeforeParsing()
    {
        var isLink = MaterialNaming.TryGetLink("  https://example.com/page  ", out var link);

        Assert.True(isLink);
        Assert.Equal("https://example.com/page", link?.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain text")]
    [InlineData("https://example.com with space")]
    [InlineData("https://example.com\nsecond line")]
    [InlineData("https://example.com\r\nsecond line")]
    [InlineData("ftp://files.example.com/file.txt")]
    [InlineData("file:///C:/Temp/file.txt")]
    [InlineData("mailto:user@example.com")]
    [InlineData("www.example.com")]
    [InlineData("example.com")]
    public void TryGetLink_ForUnsupportedContent_ReturnsFalse(string? text)
    {
        var isLink = MaterialNaming.TryGetLink(text, out var link);

        Assert.False(isLink);
        Assert.Null(link);
    }

    [Fact]
    public void CreateInternetShortcutContent_ProducesWindowsShortcutFormat()
    {
        var link = new Uri("https://example.com/page");

        var content = MaterialNaming.CreateInternetShortcutContent(link);

        Assert.Equal("[InternetShortcut]\r\nURL=https://example.com/page\r\n", content);
    }

    [Fact]
    public void Utf8NoBom_DoesNotEmitByteOrderMark()
    {
        Assert.Empty(MaterialNaming.Utf8NoBom.GetPreamble());
        Assert.Equal(new byte[] { 0x41 }, MaterialNaming.Utf8NoBom.GetBytes("A"));
    }

    [Theory]
    [InlineData("report", "report")]
    [InlineData("  report", "report")]
    [InlineData("my report", "my report")]
    [InlineData("v1.5", "v1.5")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("LPT10", "LPT10")]
    public void ValidateRenameBase_ForValidName_ReturnsTrimmedName(string input, string expected)
    {
        var validated = MaterialNaming.ValidateRenameBase(input, ".txt", Text, out var error);

        Assert.Equal(expected, validated);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void ValidateRenameBase_ForEmptyName_ReturnsNullWithError(string? input)
    {
        var validated = MaterialNaming.ValidateRenameBase(input, ".txt", Text, out var error);

        Assert.Null(validated);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void ValidateRenameBase_ForInvalidFileNameCharacters_ReturnsNullWithError(string input)
    {
        var validated = MaterialNaming.ValidateRenameBase(input, ".txt", Text, out var error);

        Assert.Null(validated);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("report.")]
    [InlineData("report .")]
    [InlineData("report. ")]
    [InlineData("report ")]
    [InlineData("report  ")]
    public void ValidateRenameBase_ForTrailingDotOrSpace_ReturnsNullWithError(string input)
    {
        // Checked on the raw input: trailing whitespace must be reported, not silently trimmed away.
        var validated = MaterialNaming.ValidateRenameBase(input, ".txt", Text, out var error);

        Assert.Null(validated);
        Assert.NotNull(error);
        
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("nul")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    public void ValidateRenameBase_ForReservedDeviceName_ReturnsNullWithError(string input)
    {
        var validated = MaterialNaming.ValidateRenameBase(input, ".txt", Text, out var error);

        Assert.Null(validated);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateRenameBase_AtMaxLength_IsAccepted()
    {
        var name = new string('x', MaterialNaming.MaxBaseNameLength);

        var validated = MaterialNaming.ValidateRenameBase(name, ".txt", Text, out var error);

        Assert.Equal(name, validated);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateRenameBase_LongerThanMaxLength_ReturnsNullWithError()
    {
        var name = new string('x', MaterialNaming.MaxBaseNameLength + 1);

        var validated = MaterialNaming.ValidateRenameBase(name, ".txt", Text, out var error);

        Assert.Null(validated);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateRenameBase_DoesNotIncludeExtensionInResult()
    {
        var validated = MaterialNaming.ValidateRenameBase("report", ".txt", Text, out _);

        Assert.Equal("report", validated);
        Assert.DoesNotContain(".txt", validated);
    }

    [Fact]
    public void TryGetLink_RejectsMultilineTextEvenWhenFirstLineIsUrl()
    {
        var isLink = MaterialNaming.TryGetLink("https://example.com\r\nmore text", out var link);

        Assert.False(isLink);
        Assert.Null(link);
    }

    [Fact]
    public void CreateInternetShortcutContent_PreservesOriginalUrlString()
    {
        const string url = "https://example.com/a/b?q=1&x=2";
        var link = new Uri(url);

        var content = MaterialNaming.CreateInternetShortcutContent(link);

        Assert.Equal(
            $"[InternetShortcut]\r\nURL={url}\r\n",
            content);
        Assert.DoesNotContain("\uFEFF", content, StringComparison.Ordinal);
    }
}
