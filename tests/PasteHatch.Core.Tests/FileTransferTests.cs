namespace PasteHatch.Core.Tests;

/// <summary>
/// Transfer rules are exercised through their own seam: import, explicit copy and move with
/// collision names, same-folder no-ops and partial results. The workspace tests cover how these
/// results reach the user; the rules themselves live here.
/// </summary>
public sealed class FileTransferTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private FileTransfer Create() => new(_harness.Shell, _harness.Text);

    [Theory]
    [InlineData("import")]
    [InlineData("copy")]
    [InlineData("move")]
    public void PartialTransfer_KeepsSuccessfulFilesAndReportsTheRest(string intentName)
    {
        var intent = Enum.Parse<FileTransferIntent>(intentName, ignoreCase: true);
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var target = Path.Combine(basket, "dest");
        Directory.CreateDirectory(target);
        var source = _harness.Temp.CreateSourceFile("source.txt", "payload");
        var missing = Path.Combine(_harness.Temp.SourceRoot, "missing.txt");

        var result = Create().Execute([source, missing], basket, target, "dest", intent);

        Assert.Equal(1, result.Completed);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(target, "source.txt"), "payload");
        Assert.Single(result.Problems);
        Assert.Equal(intent != FileTransferIntent.Move, File.Exists(source));
    }

    [Fact]
    public void Copy_AllowsDuplicatingAFileAlreadyInTheTarget()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, string.Empty, FileTransferIntent.Copy);

        Assert.Equal(1, result.Completed);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(basket, "note.txt"), "note");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(basket, "note (2).txt"), "note");
    }

    [Fact]
    public void Move_IntoTheSameFolder_IsANoOp()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, string.Empty, FileTransferIntent.Move);

        Assert.Equal(0, result.Completed);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(basket, "note.txt"), "note");
        Assert.False(File.Exists(Path.Combine(basket, "note (2).txt")));
    }

    [Fact]
    public void Move_RefusesToMoveAFolderInsideItself()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var folder = Path.Combine(basket, "parent");
        Directory.CreateDirectory(Path.Combine(folder, "child"));

        var result = Create().Execute(
            [folder], basket, Path.Combine(folder, "child"), Path.Combine("parent", "child"), FileTransferIntent.Move);

        Assert.Equal(0, result.Completed);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void Import_SkipsItemsAlreadyInsideTheBasket()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, string.Empty, FileTransferIntent.Import);

        Assert.Equal(0, result.Completed);
        Assert.Single(Directory.GetFiles(basket));
    }
}
