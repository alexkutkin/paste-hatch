namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceMaterialTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void RenameEntry_KeepsExtensionAndRenamesFileOnDisk()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "original text");

        var renamed = workspace.RenameEntry(material, "note");

        Assert.True(renamed);
        var renamedPath = Path.Combine(directory, "note.txt");
        FileAssert.HasUtf8TextWithoutBom(renamedPath, "original text");
        FileAssert.DoesNotExist(Path.Combine(directory, "Mon, 13-02.txt"));
        var refreshed = workspace.RequireSingleFile();
        Assert.Equal("note.txt", refreshed.Name);
        Assert.Equal(renamedPath, refreshed.FullPath);
        Assert.Equal(MaterialKind.Text, refreshed.Kind);
    }

    [Fact]
    public void RenameEntry_ForImage_KeepsImageExtension()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 };
        _harness.Clipboard.Payload = PastePayload.FromImage(imageBytes);
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        var material = workspace.RequireSingleFile();

        Assert.True(workspace.RenameEntry(material, "shot"));

        var renamedPath = Path.Combine(directory, "shot.png");
        Assert.Equal(imageBytes, File.ReadAllBytes(renamedPath));
        var refreshed = workspace.RequireSingleFile();
        Assert.Equal("shot.png", refreshed.Name);
        Assert.Equal(MaterialKind.Image, refreshed.Kind);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    public void RenameEntry_ForInvalidName_ShowsErrorAndKeepsFile(string newName)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "text");

        var renamed = workspace.RenameEntry(material, newName);

        Assert.False(renamed);
        var error = workspace.RequireError();
        Assert.NotNull(error);
        Assert.NotNull(workspace.StatusMessage);
        FileAssert.Exists(Path.Combine(directory, "Mon, 13-02.txt"));
        Assert.Equal("Mon, 13-02.txt", workspace.RequireSingleFile().Name);
    }

    [Fact]
    public void RenameEntry_WhenTargetNameAlreadyExists_ShowsErrorAndKeepsBothFiles()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var first = PasteText(workspace, "first");
        Assert.True(workspace.RenameEntry(first, "report"));
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;
        _harness.Clipboard.Payload = PastePayload.FromText("second");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        var second = Assert.Single(workspace.Files(string.Empty), material => material.Name == "Tue, 14-30.txt");

        var renamed = workspace.RenameEntry(second, "report");

        Assert.False(renamed);
        workspace.RequireError();
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "report.txt"), "first");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Tue, 14-30.txt"), "second");
    }

    [Fact]
    public void RenameEntry_WhenSourceFileWasRemovedExternally_ShowsErrorAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        File.Delete(material.FullPath);

        var renamed = workspace.RenameEntry(material, "new-name");

        Assert.False(renamed);
        workspace.RequireError();
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void RenameEntry_WithUnchangedName_ReturnsTrueWithoutRenaming()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "text");

        Assert.True(workspace.RenameEntry(material, "Mon, 13-02"));

        FileAssert.Exists(Path.Combine(directory, "Mon, 13-02.txt"));
        Assert.Equal("Mon, 13-02.txt", workspace.RequireSingleFile().Name);
    }

    [Fact]
    public void RenameEntry_ForCaseOnlyChange_RenamesTheFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "text");
        Assert.Equal("Mon, 13-02.txt", material.Name);

        var renamed = workspace.RenameEntry(material, "MON, 13-02");

        Assert.True(renamed);
        var refreshed = workspace.RequireSingleFile();
        Assert.Equal("MON, 13-02.txt", refreshed.Name);
        Assert.Equal("MON, 13-02.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(directory, "*.txt"))));
        FileAssert.HasUtf8TextWithoutBom(refreshed.FullPath, "text");
        Assert.Empty(Directory.GetFiles(directory, "~*.tmp"));
        Assert.False(workspace.StatusIsError);
    }

    [Fact]
    public void DeleteEntry_WhenConfirmationDeclined_KeepsFileAndDoesNotRecycle()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        _harness.Confirmations.ConfirmMaterialResult = false;

        var deleted = workspace.DeleteEntry(material);

        Assert.False(deleted);
        FileAssert.Exists(material.FullPath);
        Assert.Empty(_harness.Shell.RecycledFiles);
        Assert.Single(workspace.Files(string.Empty));
        Assert.Equal(material.Name, Assert.Single(_harness.Confirmations.MaterialConfirmations));
    }

    [Fact]
    public void DeleteEntry_WhenConfirmed_RecyclesFileAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        _harness.Confirmations.ConfirmMaterialResult = true;

        var deleted = workspace.DeleteEntry(material);

        Assert.True(deleted);
        Assert.Equal(new[] { material.FullPath }, _harness.Shell.RecycledFiles);
        FileAssert.DoesNotExist(material.FullPath);
        Assert.Empty(workspace.Files(string.Empty));
        Assert.Contains(material.Name, workspace.StatusMessage);
        Assert.NotNull(workspace.StatusMessage);
    }

    [Fact]
    public void DeleteEntry_WhenRecycleFails_KeepsFileAndReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        _harness.Shell.FileRecycleFailure = new IOException("file is locked");

        var deleted = workspace.DeleteEntry(material);

        Assert.False(deleted);
        FileAssert.Exists(material.FullPath);
        Assert.Single(workspace.Files(string.Empty));
        workspace.RequireError();
    }

    [Fact]
    public void OpenEntry_ForExistingFile_CallsShellOpenItem()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");

        workspace.OpenEntry(material);

        Assert.Equal(new[] { material.FullPath }, _harness.Shell.OpenedItems);
    }

    [Fact]
    public void OpenEntry_ForMissingFile_ReportsErrorAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        File.Delete(material.FullPath);

        workspace.OpenEntry(material);

        workspace.RequireError();
        Assert.Empty(_harness.Shell.OpenedItems);
        Assert.Empty(workspace.Files(string.Empty));
    }

    [Fact]
    public void CopyActiveBasketPath_CopiesActiveFolderPath()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        workspace.CopyActiveBasketPath(string.Empty);

        Assert.Equal(new[] { directory }, _harness.Shell.CopiedTexts);
        Assert.False(workspace.StatusIsError);
    }

    [Fact]
    public void CopyActiveBasketPath_WithTwoBaskets_CopiesTheActiveOne()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var secondTab = workspace.CreateAndRequireTab();
        Assert.Equal(secondTab.Uid, workspace.RequireActiveTab().Uid);

        workspace.CopyActiveBasketPath(string.Empty);

        Assert.Equal(new[] { secondTab.DirectoryPath }, _harness.Shell.CopiedTexts);
    }

    [Fact]
    public void CopyActiveBasketPath_WhenShellFails_ReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Shell.CopyTextFailure = new InvalidOperationException("clipboard is busy");

        workspace.CopyActiveBasketPath(string.Empty);

        workspace.RequireError();
        Assert.Empty(_harness.Shell.CopiedTexts);
    }

    [Fact]
    public void CopyEntryPath_CopiesTheFullPathOfThatFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");

        workspace.CopyEntryPath(material);

        Assert.Equal(new[] { material.FullPath }, _harness.Shell.CopiedTexts);
        Assert.False(workspace.StatusIsError);
    }

    [Fact]
    public void CopyEntryPath_WithTwoMaterials_CopiesTheRequestedOne()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = PasteText(workspace, "first");
        _harness.Clipboard.Payload = PastePayload.FromText("second");
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        var second = workspace.Files(string.Empty).First(material => material.FullPath != first.FullPath);

        workspace.CopyEntryPath(first);

        Assert.Equal(new[] { first.FullPath }, _harness.Shell.CopiedTexts);
        Assert.NotEqual(second.FullPath, Assert.Single(_harness.Shell.CopiedTexts));
    }

    [Fact]
    public void CopyEntryPath_WhenShellFails_ReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "text");
        _harness.Shell.CopyTextFailure = new InvalidOperationException("clipboard is busy");

        workspace.CopyEntryPath(material);

        workspace.RequireError();
        Assert.Empty(_harness.Shell.CopiedTexts);
    }

    [Fact]
    public void OpenActiveBasketFolder_OpensTheActiveBasketDirectory()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        workspace.OpenActiveBasketFolder(string.Empty);

        Assert.Equal(new[] { directory }, _harness.Shell.OpenedFolders);
    }

    [Fact]
    public void OpenActiveBasketFolder_WhenDirectoryWasDeleted_RecreatesIt()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.Delete(directory, recursive: true);

        workspace.OpenActiveBasketFolder(string.Empty);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(new[] { directory }, _harness.Shell.OpenedFolders);
    }

    [Fact]
    public void Refresh_PublishesViewChangeAndSeesExternallyAddedFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(directory, "external.md"), "# external file");
        var changes = new List<WorkspaceChangeKind>();
        workspace.Changed += (_, change) => changes.Add(change.Kind);

        workspace.Refresh();

        var material = workspace.RequireSingleFile();
        Assert.Equal("external.md", material.Name);
        Assert.Equal(MaterialKind.Text, material.Kind);
        Assert.Equal([WorkspaceChangeKind.View], changes);
    }

    [Fact]
    public void Refresh_WhenBasketDirectoryWasDeleted_ReadsNothingWithoutThrowing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        PasteText(workspace, "text");
        Directory.Delete(directory, recursive: true);

        workspace.Refresh();

        Assert.Empty(workspace.Files(string.Empty));
    }

    private BasketEntry PasteText(BasketWorkspace workspace, string text)
    {
        _harness.Clipboard.Payload = PastePayload.FromText(text);
        Assert.Equal(1, workspace.AddFromClipboard(string.Empty));
        return Assert.Single(workspace.Files(string.Empty));
    }
}
