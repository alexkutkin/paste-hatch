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

    private FileTransfer Create() => new(_harness.Temp.BasketsRoot, _harness.Shell, _harness.Text);

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

        var result = Create().Execute([source, missing], basket, target, intent);

        Assert.Equal(1, result.Completed);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(target, "source.txt"), "payload");
        Assert.Single(result.Problems);
        Assert.Equal(intent != FileTransferIntent.Move, File.Exists(source));
        Assert.Collection(result.Items,
            item => Assert.Equal(intent == FileTransferIntent.Move ? TransferOutcome.Moved : TransferOutcome.Copied, item.Outcome),
            item => Assert.Equal(TransferOutcome.Failed, item.Outcome));
    }

    [Fact]
    public void Copy_AllowsDuplicatingAFileAlreadyInTheTarget()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, FileTransferIntent.Copy);

        Assert.Equal(1, result.Completed);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(basket, "note.txt"), "note");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(basket, "note (2).txt"), "note");
    }

    [Fact]
    public void Move_IntoTheSameFolder_IsANoOp()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, FileTransferIntent.Move);

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
            [folder], basket, Path.Combine(folder, "child"), FileTransferIntent.Move);

        Assert.Equal(0, result.Completed);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void Import_SkipsItemsAlreadyInsideTheBasket()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        File.WriteAllText(Path.Combine(basket, "note.txt"), "note");

        var result = Create().Execute([Path.Combine(basket, "note.txt")], basket, basket, FileTransferIntent.Import);

        Assert.Equal(0, result.Completed);
        Assert.Single(Directory.GetFiles(basket));
    }

    [Theory]
    [InlineData("import")]
    [InlineData("copy")]
    public void InterruptedFolderCopy_ReportsThePartialDestinationWithoutDeletingIt(string intentName)
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var source = _harness.Temp.CreateSourceDirectory("folder");
        File.WriteAllText(Path.Combine(source, "first.txt"), "first");
        var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested);
        var lockedPath = Path.Combine(nested, "locked.txt");
        File.WriteAllText(lockedPath, "locked");
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = Create().Execute([source], basket, basket,
            Enum.Parse<FileTransferIntent>(intentName, ignoreCase: true));

        var item = Assert.Single(result.Items);
        Assert.Equal(TransferOutcome.Partial, item.Outcome);
        Assert.Equal(Path.Combine(basket, "folder"), item.Destination);
        Assert.Equal(0, result.Completed);
        Assert.Contains(item.Destination!, Assert.Single(result.Problems));
        FileAssert.HasText(Path.Combine(basket, "folder", "first.txt"), "first");
        FileAssert.HasText(Path.Combine(source, "first.txt"), "first");
    }

    [Fact]
    public void ExternalFolderMove_PreservesCopyAndRecycleBehavior()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var source = _harness.Temp.CreateSourceDirectory("folder");
        File.WriteAllText(Path.Combine(source, "note.txt"), "note");

        var result = Create().Execute([source], basket, basket, FileTransferIntent.Move);

        Assert.Equal(TransferOutcome.Moved, Assert.Single(result.Items).Outcome);
        Assert.Equal(1, result.Completed);
        Assert.Equal(source, Assert.Single(_harness.Shell.RecycledDirectories));
        Assert.False(Directory.Exists(source));
        FileAssert.HasText(Path.Combine(basket, "folder", "note.txt"), "note");
    }

    [Fact]
    public void ExternalFolderRecycleFailure_ReportsUnknownSourceEffectAndKeepsTheCopy()
    {
        var basket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var source = _harness.Temp.CreateSourceDirectory("folder");
        File.WriteAllText(Path.Combine(source, "note.txt"), "note");
        _harness.Shell.DirectoryRecycleFailure = new IOException("Recycle unavailable.");

        var result = Create().Execute([source], basket, basket, FileTransferIntent.Move);

        var item = Assert.Single(result.Items);
        Assert.Equal(TransferOutcome.Unknown, item.Outcome);
        Assert.Equal(source, item.Source);
        Assert.Equal(Path.Combine(basket, "folder"), item.Destination);
        Assert.Equal(0, result.Completed);
        Assert.Contains(source, Assert.Single(result.Problems));
        Assert.Contains(item.Destination!, Assert.Single(result.Problems));
        FileAssert.HasText(Path.Combine(source, "note.txt"), "note");
        FileAssert.HasText(Path.Combine(basket, "folder", "note.txt"), "note");
    }

    [Fact]
    public void SameVolumeDirectoryMoveAccessDenied_DoesNotFallBackToCopyAndRecycle()
    {
        var sourceBasket = _harness.Temp.CreateBasketDirectory("BASKET1");
        var targetBasket = _harness.Temp.CreateBasketDirectory("BASKET2");
        var source = Path.Combine(sourceBasket, "folder");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "locked.txt");
        File.WriteAllText(file, "safe");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = Create().Execute([source], targetBasket, targetBasket, FileTransferIntent.Move);

        Assert.Equal(TransferOutcome.Failed, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, result.Completed);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(targetBasket, "folder")));
        Assert.Empty(_harness.Shell.RecycledDirectories);
        FileAssert.HasText(file, "safe");
    }
}
