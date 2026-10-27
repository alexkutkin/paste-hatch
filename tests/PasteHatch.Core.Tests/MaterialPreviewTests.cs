namespace PasteHatch.Core.Tests;

public sealed class MaterialPreviewTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_temp.Root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadText_ReturnsBoundedStartOfTheFileAndStripsBom()
    {
        var path = Path.Combine(_temp.Root, "bom.txt");
        File.WriteAllText(path, "\uFEFFfirst line\nsecond line");

        var text = MaterialPreview.ReadText(path, 10);

        Assert.Equal("first line", text);
    }

    [Fact]
    public void ReadText_ForVeryLargeFile_StopsAtTheBound()
    {
        var path = CreateFile("huge.txt", new string('x', 100_000));

        var text = MaterialPreview.ReadText(path, 64 * 1024);

        Assert.Equal(64 * 1024, text.Length);
        Assert.Equal(new string('x', 64 * 1024), text);
    }

    [Fact]
    public void ReadText_ForFileOpenInAnotherApp_StillReads()
    {
        var path = CreateFile("shared.txt", "first line");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("first line", MaterialPreview.ReadText(path, 100));
    }

    [Fact]
    public void ReadText_ForMissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => MaterialPreview.ReadText(Path.Combine(_temp.Root, "missing.txt"), 100));
    }
}
