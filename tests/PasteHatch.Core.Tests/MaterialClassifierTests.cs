namespace PasteHatch.Core.Tests;

public sealed class MaterialClassifierTests
{
    [Theory]
    [InlineData("screenshot.png", MaterialKind.Image)]
    [InlineData("photo.JPG", MaterialKind.Image)]
    [InlineData("animation.gif", MaterialKind.Image)]
    [InlineData("paste.jpeg", MaterialKind.Image)]
    [InlineData("icon.webp", MaterialKind.Image)]
    [InlineData("shortcut.url", MaterialKind.Link)]
    [InlineData("PASTED.URL", MaterialKind.Link)]
    [InlineData("notes.txt", MaterialKind.Text)]
    [InlineData("README.md", MaterialKind.Text)]
    [InlineData("payload.json", MaterialKind.Text)]
    [InlineData("data.csv", MaterialKind.Text)]
    [InlineData("archive.zip", MaterialKind.File)]
    [InlineData("installer.exe", MaterialKind.File)]
    [InlineData("document.pdf", MaterialKind.File)]
    [InlineData("no-extension", MaterialKind.File)]
    [InlineData("name.", MaterialKind.File)]
    public void Classify_MapsFileNameExtensionToKind(string fileName, MaterialKind expectedKind)
    {
        Assert.Equal(expectedKind, MaterialClassifier.Classify(fileName));
    }

    [Fact]
    public void Classify_FileNameWithoutExtension_IsFile()
    {
        Assert.Equal(MaterialKind.File, MaterialClassifier.Classify("just-a-name"));
    }
}
