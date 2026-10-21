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
        const string text = "Привет, мир!\r\nВторая строка — тире";
        _harness.Clipboard.Payload = PastePayload.FromText(text);

        var added = workspace.AddFromClipboard();

        Assert.Equal(1, added);
        var filePath = Path.Combine(directory, "Пн, 13-02.txt");
        FileAssert.HasUtf8TextWithoutBom(filePath, text);
        var material = workspace.RequireSingleMaterial();
        Assert.Equal("Пн, 13-02.txt", material.FileName);
        Assert.Equal(MaterialKind.Text, material.Kind);
        Assert.Equal("Текст добавлен.", workspace.StatusMessage);
    }

    [Fact]
    public void AddFromClipboard_SecondTextPasteWithinSameMinute_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        _harness.Clipboard.Payload = PastePayload.FromText("первый");
        Assert.Equal(1, workspace.AddFromClipboard());
        _harness.Clipboard.Payload = PastePayload.FromText("второй");
        Assert.Equal(1, workspace.AddFromClipboard());
        _harness.Clipboard.Payload = PastePayload.FromText("третий");
        Assert.Equal(1, workspace.AddFromClipboard());

        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Пн, 13-02.txt"), "первый");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Пн, 13-02 (2).txt"), "второй");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Пн, 13-02 (3).txt"), "третий");
        Assert.Equal(3, workspace.Materials.Count);
        Assert.All(workspace.Materials, material => Assert.Equal(MaterialKind.Text, material.Kind));
    }

    [Fact]
    public void AddFromClipboard_HttpsLinkPaste_CreatesInternetShortcut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromText("https://example.com/page?q=1");

        var added = workspace.AddFromClipboard();

        Assert.Equal(1, added);
        var urlPath = Path.Combine(directory, "Пн, 13-02.url");
        Assert.Equal(
            "[InternetShortcut]\r\nURL=https://example.com/page?q=1\r\n",
            File.ReadAllText(urlPath, Encoding.UTF8));
        var material = workspace.RequireSingleMaterial();
        Assert.Equal("Пн, 13-02.url", material.FileName);
        Assert.Equal(MaterialKind.Link, material.Kind);
        Assert.Contains("Добавлена ссылка", workspace.StatusMessage);
        Assert.Contains("example.com", workspace.StatusMessage);
    }

    [Fact]
    public void AddFromClipboard_HttpLinkPaste_CreatesInternetShortcut()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromText("http://example.com/path");

        Assert.Equal(1, workspace.AddFromClipboard());

        var content = File.ReadAllText(Path.Combine(directory, "Пн, 13-02.url"), Encoding.UTF8);
        Assert.Equal("[InternetShortcut]\r\nURL=http://example.com/path\r\n", content);
        Assert.Equal(MaterialKind.Link, workspace.RequireSingleMaterial().Kind);
    }

    [Fact]
    public void AddFromClipboard_ImagePaste_WritesExactPngBytes()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromImage(PngBytes);

        var added = workspace.AddFromClipboard();

        Assert.Equal(1, added);
        var filePath = Path.Combine(directory, "Пн, 13-02.png");
        FileAssert.Exists(filePath);
        Assert.Equal(PngBytes, File.ReadAllBytes(filePath));
        var material = workspace.RequireSingleMaterial();
        Assert.Equal("Пн, 13-02.png", material.FileName);
        Assert.Equal(MaterialKind.Image, material.Kind);
        Assert.Contains("скриншот", workspace.StatusMessage);
    }

    [Fact]
    public void AddFromClipboard_SecondImagePaste_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        _harness.Clipboard.Payload = PastePayload.FromImage(PngBytes);
        Assert.Equal(1, workspace.AddFromClipboard());
        _harness.Clipboard.Payload = PastePayload.FromImage([0xAA, 0xBB]);
        Assert.Equal(1, workspace.AddFromClipboard());

        Assert.Equal(PngBytes, File.ReadAllBytes(Path.Combine(directory, "Пн, 13-02.png")));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, File.ReadAllBytes(Path.Combine(directory, "Пн, 13-02 (2).png")));
        Assert.Equal(2, workspace.Materials.Count);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardHoldsNoSupportedData_ReturnsZeroAndSetsStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.None;

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Equal("В буфере обмена нет поддерживаемых данных.", workspace.StatusMessage);
        Assert.Empty(workspace.Materials);
        Assert.Equal(1, _harness.Clipboard.ReadCount);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardHoldsOnlyWhitespaceText_ReturnsZeroAndCreatesNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        _harness.Clipboard.Payload = PastePayload.FromText("   ");

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Equal("В буфере обмена нет текста.", workspace.StatusMessage);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardHoldsEmptyImage_ReturnsZeroAndSetsStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromImage([]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Equal("В буфере обмена нет изображения.", workspace.StatusMessage);
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void AddFromClipboard_WhenClipboardReaderThrows_ReportsErrorAndReturnsZero()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.FailureToThrow = new InvalidOperationException("clipboard is busy");

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Contains("Не удалось прочитать буфер обмена", workspace.StatusMessage);
        Assert.Single(_harness.Confirmations.Errors);
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void AddFromClipboard_FilePayload_CopiesFilesIntoActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var binarySource = _harness.Temp.CreateSourceFile("alpha.bin", "binary payload");
        var textSource = _harness.Temp.CreateSourceFile("beta.txt", "бета");
        _harness.Clipboard.Payload = PastePayload.FromFiles([binarySource, textSource]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(2, added);
        Assert.Equal("binary payload", File.ReadAllText(Path.Combine(directory, "alpha.bin")));
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "beta.txt"), "бета");
        Assert.Equal(2, workspace.Materials.Count);
        Assert.Equal("Добавлено 2 файла.", workspace.StatusMessage);
        Assert.True(File.Exists(binarySource));
        Assert.True(File.Exists(textSource));
    }

    [Fact]
    public void AddFromClipboard_FilePayloadWithNameCollision_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var firstSource = _harness.Temp.CreateSourceFile("report.txt", "первый");
        _harness.Clipboard.Payload = PastePayload.FromFiles([firstSource]);
        Assert.Equal(1, workspace.AddFromClipboard());
        var secondSource = _harness.Temp.CreateSourceFile(Path.Combine("other", "report.txt"), "второй");
        _harness.Clipboard.Payload = PastePayload.FromFiles([secondSource]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(1, added);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "report.txt"), "первый");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "report (2).txt"), "второй");
        Assert.Equal(2, workspace.Materials.Count);
    }

    [Fact]
    public void AddFromClipboard_FileAlreadyInsideBasket_IsSkipped()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("report.txt", "данные");
        Assert.Equal(1, workspace.AddFiles([source]));
        _harness.Clipboard.Payload = PastePayload.FromFiles([Path.Combine(directory, "report.txt")]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Equal("Файлы уже находятся в этой корзине.", workspace.StatusMessage);
        Assert.Single(workspace.Materials);
    }

    [Fact]
    public void AddFromClipboard_MixedInsideAndOutsideFiles_AddsOnlyOutsideFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var source = _harness.Temp.CreateSourceFile("report.txt", "данные");
        Assert.Equal(1, workspace.AddFiles([source]));
        var outside = _harness.Temp.CreateSourceFile("other.txt", "другое");
        _harness.Clipboard.Payload = PastePayload.FromFiles([Path.Combine(directory, "report.txt"), outside]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(1, added);
        Assert.Equal("Добавлен 1 файл (часть файлов уже была в корзине).", workspace.StatusMessage);
        Assert.Equal(2, workspace.Materials.Count);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "other.txt"), "другое");
    }

    [Fact]
    public void AddFromClipboard_FilePayloadWithEmptyList_ReturnsZeroAndSetsStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clipboard.Payload = PastePayload.FromFiles([]);

        var added = workspace.AddFromClipboard();

        Assert.Equal(0, added);
        Assert.Equal("Нет файлов для добавления.", workspace.StatusMessage);
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void AddFiles_ForDirectoryPath_CopiesFolderWithItsContents()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directoryPath = _harness.Temp.CreateSourceDirectory("dropped-folder");
        _harness.Temp.CreateSourceFile(Path.Combine("dropped-folder", "nested", "note.txt"), "внутри");

        var added = workspace.AddFiles([directoryPath]);

        Assert.Equal(1, added);
        var basketDirectory = workspace.RequireActiveTab().DirectoryPath;
        FileAssert.HasUtf8TextWithoutBom(
            Path.Combine(basketDirectory, "dropped-folder", "nested", "note.txt"),
            "внутри");
        Assert.Single(workspace.Entries, entry => entry.IsFolder && entry.Name == "dropped-folder");
        Assert.Empty(_harness.Confirmations.Errors);
        Assert.Contains("Добавлено", workspace.StatusMessage);
    }

    [Fact]
    public void AddFiles_ForFolderAlreadyInsideBasket_IsSkipped()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var source = _harness.Temp.CreateSourceDirectory("source");
        _harness.Temp.CreateSourceFile(Path.Combine("source", "file.txt"), "x");
        Assert.Equal(1, workspace.AddFiles([source]));
        var copiedFolder = Path.Combine(workspace.RequireActiveTab().DirectoryPath, "source");

        var added = workspace.AddFiles([copiedFolder]);

        Assert.Equal(0, added);
        Assert.Contains("уже", workspace.StatusMessage);
    }

    [Fact]
    public void AddFiles_ForMissingFile_ReportsProblemAndAddsNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var activeDirectory = workspace.RequireActiveTab().DirectoryPath;
        var missingPath = Path.Combine(_harness.Temp.Root, "missing.bin");

        var added = workspace.AddFiles([missingPath]);

        Assert.Equal(0, added);
        Assert.Empty(workspace.Materials);
        var error = Assert.Single(_harness.Confirmations.Errors);
        Assert.Contains("Файл не найден", error);
        Assert.Contains("Файл не найден", workspace.StatusMessage);
        Assert.Empty(Directory.GetFiles(activeDirectory));
    }

    [Fact]
    public void AddFiles_AddsOnlyToTheActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstDirectory = workspace.RequireActiveTab().DirectoryPath;
        var secondTab = workspace.CreateAndRequireTab();
        var source = _harness.Temp.CreateSourceFile("context.txt", "контекст");

        var added = workspace.AddFiles([source]);

        Assert.Equal(1, added);
        FileAssert.Exists(Path.Combine(secondTab.DirectoryPath, "context.txt"));
        Assert.Empty(Directory.GetFiles(firstDirectory));
    }

    [Fact]
    public void AddQuickNote_CreatesTxtNamedByClock()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        const string noteText = "Заметка: купить хлеб";

        var note = workspace.AddQuickNote(noteText);

        Assert.NotNull(note);
        Assert.Equal("Пн, 13-02.txt", note.FileName);
        Assert.Equal(noteText, File.ReadAllText(note.FullPath, Encoding.UTF8));
        Assert.Equal(Encoding.UTF8.GetBytes(noteText), File.ReadAllBytes(note.FullPath));
        Assert.Equal(MaterialKind.Text, note.Kind);
        Assert.Equal(note.FullPath, workspace.RequireSingleMaterial().FullPath);
    }

    [Fact]
    public void AddQuickNote_SecondNoteAtSameMoment_GetsNumberedSuffix()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var directory = workspace.RequireActiveTab().DirectoryPath;

        var first = workspace.AddQuickNote("первая заметка");
        var second = workspace.AddQuickNote("вторая заметка");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Пн, 13-02.txt", first.FileName);
        Assert.Equal("Пн, 13-02 (2).txt", second.FileName);
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Пн, 13-02.txt"), "первая заметка");
        FileAssert.HasUtf8TextWithoutBom(Path.Combine(directory, "Пн, 13-02 (2).txt"), "вторая заметка");
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

        var note = workspace.AddQuickNote(noteText);

        Assert.Null(note);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Empty(workspace.Materials);
    }

    [Fact]
    public void AddQuickNote_UsesTheClockAtCallTime()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clock.Now = TestMoments.ThursdayMidnight;

        var note = workspace.AddQuickNote("ночная заметка");

        Assert.NotNull(note);
        Assert.Equal("Чт, 00-05.txt", note.FileName);
    }
}
