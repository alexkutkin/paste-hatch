namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceMaterialTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void RenameMaterial_KeepsExtensionAndRenamesFileOnDisk()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "исходный текст");

        var renamed = workspace.RenameMaterial(material, "заметка");

        Assert.True(renamed);
        var renamedPath = Path.Combine(directory, "заметка.txt");
        FileAssert.HasUtf8TextWithoutBom(renamedPath, "исходный текст");
        FileAssert.DoesNotExist(Path.Combine(directory, "Пн, 13-02.txt"));
        var refreshed = workspace.RequireSingleMaterial();
        Assert.Equal("заметка.txt", refreshed.FileName);
        Assert.Equal(renamedPath, refreshed.FullPath);
        Assert.Equal(MaterialKind.Text, refreshed.Kind);
    }

    [Fact]
    public void RenameMaterial_TrimsLeadingWhitespaceFromTheNewBaseName()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "текст");

        Assert.True(workspace.RenameMaterial(material, "  итог"));

        FileAssert.Exists(Path.Combine(directory, "итог.txt"));
        Assert.Equal("итог.txt", workspace.RequireSingleMaterial().FileName);
    }

    [Fact]
    public void RenameMaterial_ForImage_KeepsImageExtension()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 };
        _harness.Clipboard.Payload = PastePayload.FromImage(imageBytes);
        Assert.Equal(1, workspace.AddFromClipboard());
        var material = workspace.RequireSingleMaterial();

        Assert.True(workspace.RenameMaterial(material, "скрин"));

        var renamedPath = Path.Combine(directory, "скрин.png");
        Assert.Equal(imageBytes, File.ReadAllBytes(renamedPath));
        var refreshed = workspace.RequireSingleMaterial();
        Assert.Equal("скрин.png", refreshed.FileName);
        Assert.Equal(MaterialKind.Image, refreshed.Kind);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    public void RenameMaterial_ForInvalidName_ShowsErrorAndKeepsFile(string newName)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "текст");

        var renamed = workspace.RenameMaterial(material, newName);

        Assert.False(renamed);
        var error = Assert.Single(_harness.Confirmations.Errors);
        Assert.Contains("недопустимые символы", error);
        Assert.Contains("недопустимые символы", workspace.StatusMessage);
        FileAssert.Exists(Path.Combine(directory, "Пн, 13-02.txt"));
        Assert.Equal("Пн, 13-02.txt", workspace.RequireSingleMaterial().FileName);
    }

    [Fact]
    public void RenameMaterial_ForEmptyName_ShowsErrorAndKeepsFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "текст");

        Assert.False(workspace.RenameMaterial(material, "   "));

        Assert.Contains("пустым", Assert.Single(_harness.Confirmations.Errors));
        FileAssert.Exists(Path.Combine(directory, "Пн, 13-02.txt"));
    }

    [Fact]
    public void RenameMaterial_ForNameWithTrailingSpace_ShowsErrorAndKeepsFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");

        Assert.False(workspace.RenameMaterial(material, "отчёт "));

        Assert.Contains("пробелом", Assert.Single(_harness.Confirmations.Errors));
    }

    [Fact]
    public void RenameMaterial_WhenTargetNameAlreadyExists_ShowsErrorAndKeepsBothFiles()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var first = PasteText(workspace, "первый");
        Assert.True(workspace.RenameMaterial(first, "отчёт"));
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;
        _harness.Clipboard.Payload = PastePayload.FromText("второй");
        Assert.Equal(1, workspace.AddFromClipboard());
        var second = Assert.Single(workspace.Materials, material => material.FileName == "Вт, 14-30.txt");

        var renamed = workspace.RenameMaterial(second, "отчёт");

        Assert.False(renamed);
        Assert.Contains("уже существует", Assert.Single(_harness.Confirmations.Errors));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "отчёт.txt"), "первый");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Вт, 14-30.txt"), "второй");
    }

    [Fact]
    public void RenameMaterial_WhenSourceFileWasRemovedExternally_ShowsErrorAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        File.Delete(material.FullPath);

        var renamed = workspace.RenameMaterial(material, "новое");

        Assert.False(renamed);
        Assert.Contains("не найден", Assert.Single(_harness.Confirmations.Errors));
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void RenameMaterial_WithUnchangedName_ReturnsTrueWithoutRenaming()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "текст");

        Assert.True(workspace.RenameMaterial(material, "Пн, 13-02"));

        FileAssert.Exists(Path.Combine(directory, "Пн, 13-02.txt"));
        Assert.Equal("Пн, 13-02.txt", workspace.RequireSingleMaterial().FileName);
    }

    [Fact]
    public void RenameMaterial_ForCaseOnlyChange_RenamesTheFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var material = PasteText(workspace, "текст");
        Assert.Equal("Пн, 13-02.txt", material.FileName);

        var renamed = workspace.RenameMaterial(material, "ПН, 13-02");

        Assert.True(renamed);
        var refreshed = workspace.RequireSingleMaterial();
        Assert.Equal("ПН, 13-02.txt", refreshed.FileName);
        Assert.Equal("ПН, 13-02.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(directory, "*.txt"))));
        FileAssert.HasUtf8TextWithoutBom(refreshed.FullPath, "текст");
        Assert.Empty(Directory.GetFiles(directory, "~*.tmp"));
        Assert.Empty(_harness.Confirmations.Errors);
    }

    [Fact]
    public void DeleteMaterial_WhenConfirmationDeclined_KeepsFileAndDoesNotRecycle()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        _harness.Confirmations.ConfirmMaterialResult = false;

        var deleted = workspace.DeleteMaterial(material);

        Assert.False(deleted);
        FileAssert.Exists(material.FullPath);
        Assert.Empty(_harness.Shell.RecycledFiles);
        Assert.Single(workspace.Materials);
        Assert.Equal(material.FileName, Assert.Single(_harness.Confirmations.MaterialConfirmations));
    }

    [Fact]
    public void DeleteMaterial_WhenConfirmed_RecyclesFileAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        _harness.Confirmations.ConfirmMaterialResult = true;

        var deleted = workspace.DeleteMaterial(material);

        Assert.True(deleted);
        Assert.Equal(new[] { material.FullPath }, _harness.Shell.RecycledFiles);
        FileAssert.DoesNotExist(material.FullPath);
        Assert.Empty(workspace.Materials);
        Assert.Contains(material.FileName, workspace.StatusMessage);
        Assert.Contains("Корзину Windows", workspace.StatusMessage);
    }

    [Fact]
    public void DeleteMaterial_WhenRecycleFails_KeepsFileAndReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        _harness.Shell.FileRecycleFailure = new IOException("file is locked");

        var deleted = workspace.DeleteMaterial(material);

        Assert.False(deleted);
        FileAssert.Exists(material.FullPath);
        Assert.Single(workspace.Materials);
        Assert.Contains("Не удалось отправить файл", Assert.Single(_harness.Confirmations.Errors));
    }

    [Fact]
    public void OpenMaterial_ForExistingFile_CallsShellOpenItem()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");

        workspace.OpenMaterial(material);

        Assert.Equal(new[] { material.FullPath }, _harness.Shell.OpenedItems);
    }

    [Fact]
    public void OpenMaterial_ForMissingFile_ReportsErrorAndRefreshes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        File.Delete(material.FullPath);

        workspace.OpenMaterial(material);

        Assert.Contains("не найден", Assert.Single(_harness.Confirmations.Errors));
        Assert.Empty(_harness.Shell.OpenedItems);
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void CopyActiveBasketPath_CopiesActiveFolderPath()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        workspace.CopyActiveBasketPath();

        Assert.Equal(new[] { directory }, _harness.Shell.CopiedTexts);
        Assert.Equal("Путь к папке корзины скопирован в буфер обмена.", workspace.StatusMessage);
    }

    [Fact]
    public void CopyActiveBasketPath_WithTwoBaskets_CopiesTheActiveOne()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var secondTab = workspace.CreateAndRequireTab();
        Assert.Equal(secondTab.Uid, workspace.RequireActiveTab().Uid);

        workspace.CopyActiveBasketPath();

        Assert.Equal(new[] { secondTab.DirectoryPath }, _harness.Shell.CopiedTexts);
    }

    [Fact]
    public void CopyActiveBasketPath_WhenShellFails_ReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Shell.CopyTextFailure = new InvalidOperationException("clipboard is busy");

        workspace.CopyActiveBasketPath();

        Assert.Contains("Не удалось скопировать путь", Assert.Single(_harness.Confirmations.Errors));
        Assert.Empty(_harness.Shell.CopiedTexts);
    }

    [Fact]
    public void CopyMaterialPath_CopiesTheFullPathOfThatFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");

        workspace.CopyMaterialPath(material);

        Assert.Equal(new[] { material.FullPath }, _harness.Shell.CopiedTexts);
        Assert.Equal("Путь к файлу скопирован в буфер обмена.", workspace.StatusMessage);
    }

    [Fact]
    public void CopyMaterialPath_WithTwoMaterials_CopiesTheRequestedOne()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var first = PasteText(workspace, "первый");
        _harness.Clipboard.Payload = PastePayload.FromText("второй");
        Assert.Equal(1, workspace.AddFromClipboard());
        var second = workspace.Materials.First(material => material.FullPath != first.FullPath);

        workspace.CopyMaterialPath(first);

        Assert.Equal(new[] { first.FullPath }, _harness.Shell.CopiedTexts);
        Assert.NotEqual(second.FullPath, Assert.Single(_harness.Shell.CopiedTexts));
    }

    [Fact]
    public void CopyMaterialPath_WhenShellFails_ReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var material = PasteText(workspace, "текст");
        _harness.Shell.CopyTextFailure = new InvalidOperationException("clipboard is busy");

        workspace.CopyMaterialPath(material);

        Assert.Contains("Не удалось скопировать путь", Assert.Single(_harness.Confirmations.Errors));
        Assert.Empty(_harness.Shell.CopiedTexts);
    }

    [Fact]
    public void OpenActiveBasketFolder_OpensTheActiveBasketDirectory()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        workspace.OpenActiveBasketFolder();

        Assert.Equal(new[] { directory }, _harness.Shell.OpenedFolders);
    }

    [Fact]
    public void OpenActiveBasketFolder_WhenDirectoryWasDeleted_RecreatesIt()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        Directory.Delete(directory, recursive: true);

        workspace.OpenActiveBasketFolder();

        Assert.True(Directory.Exists(directory));
        Assert.Equal(new[] { directory }, _harness.Shell.OpenedFolders);
    }

    [Fact]
    public void Refresh_PicksUpExternallyAddedFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        File.WriteAllText(Path.Combine(directory, "external.md"), "# внешний файл");
        Assert.Empty(workspace.Materials);

        workspace.Refresh();

        var material = workspace.RequireSingleMaterial();
        Assert.Equal("external.md", material.FileName);
        Assert.Equal(MaterialKind.Text, material.Kind);
    }

    [Fact]
    public void Refresh_WhenBasketDirectoryWasDeleted_ClearsMaterialsWithoutThrowing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        PasteText(workspace, "текст");
        Directory.Delete(directory, recursive: true);

        workspace.Refresh();

        Assert.Empty(workspace.Materials);
    }

    private Material PasteText(BasketWorkspace workspace, string text)
    {
        _harness.Clipboard.Payload = PastePayload.FromText(text);
        Assert.Equal(1, workspace.AddFromClipboard());
        return Assert.Single(workspace.Materials);
    }
}
