namespace PasteHatch.Core.Tests;

public sealed class TabStateStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private TabStateStore CreateStore() => new(_temp.StateFile);

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyState()
    {
        var state = CreateStore().Load();

        Assert.Equal(WorkspaceState.CurrentVersion, state.Version);
        Assert.Null(state.ActiveUid);
        Assert.Null(state.Window);
        Assert.Empty(state.Tabs);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTabsActiveTabAndWindow()
    {
        var store = CreateStore();
        store.Save(new WorkspaceState
        {
            ActiveUid = "EF4GH5",
            Tabs =
            [
                new TabState { Uid = "AB2CD3", Name = "Первая корзина" },
                new TabState { Uid = "EF4GH5", Name = "Вторая корзина" },
            ],
            Window = new WindowPlacementState { Left = 120, Top = 80, Width = 900, Height = 700 },
        });

        var loaded = CreateStore().Load();

        Assert.Equal("EF4GH5", loaded.ActiveUid);
        Assert.Collection(
            loaded.Tabs,
            tab =>
            {
                Assert.Equal("AB2CD3", tab.Uid);
                Assert.Equal("Первая корзина", tab.Name);
            },
            tab =>
            {
                Assert.Equal("EF4GH5", tab.Uid);
                Assert.Equal("Вторая корзина", tab.Name);
            });
        Assert.NotNull(loaded.Window);
        Assert.Equal(120, loaded.Window.Left);
        Assert.Equal(80, loaded.Window.Top);
        Assert.Equal(900, loaded.Window.Width);
        Assert.Equal(700, loaded.Window.Height);
    }

    [Fact]
    public void Save_CreatesTheStateDirectoryWhenMissing()
    {
        var nestedDirectory = Path.Combine(_temp.Root, "nested", "deeper");
        var store = new TabStateStore(Path.Combine(nestedDirectory, "state.json"));

        store.Save(new WorkspaceState { ActiveUid = "AB2CD3" });

        Assert.True(File.Exists(store.StateFilePath));
        Assert.Equal("AB2CD3", store.Load().ActiveUid);
    }

    [Fact]
    public void Save_ThenLoadFromNewStoreInstance_PersistsState()
    {
        CreateStore().Save(new WorkspaceState
        {
            ActiveUid = "AB2CD3",
            Tabs = [new TabState { Uid = "AB2CD3", Name = "Корзина" }],
        });

        var loaded = CreateStore().Load();

        var tab = Assert.Single(loaded.Tabs);
        Assert.Equal("AB2CD3", tab.Uid);
        Assert.Equal("Корзина", tab.Name);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyStateWithoutThrowing()
    {
        Directory.CreateDirectory(_temp.Root);
        File.WriteAllText(_temp.StateFile, "{ this is not valid json ");

        var state = CreateStore().Load();

        Assert.Empty(state.Tabs);
        Assert.Null(state.ActiveUid);
    }

    [Fact]
    public void Load_JsonLiteralNull_ReturnsEmptyState()
    {
        File.WriteAllText(_temp.StateFile, "null");

        var state = CreateStore().Load();

        Assert.Empty(state.Tabs);
        Assert.Null(state.ActiveUid);
    }

    [Fact]
    public void Load_WhenTabsPropertyIsAbsent_NormalizesTabsToEmptyList()
    {
        File.WriteAllText(_temp.StateFile, """{"version":1,"activeUid":"AB2CD3"}""");

        var state = CreateStore().Load();

        Assert.NotNull(state.Tabs);
        Assert.Empty(state.Tabs);
        Assert.Equal("AB2CD3", state.ActiveUid);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var store = CreateStore();
        store.Save(new WorkspaceState { ActiveUid = "AB2CD3" });
        store.Save(new WorkspaceState { ActiveUid = "EF4GH5" });

        var files = Directory.GetFiles(_temp.Root).Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "state.json" }, files);
        Assert.Empty(Directory.GetFiles(_temp.Root, "*.tmp"));
    }

    [Fact]
    public void Save_OverwritesPreviousState()
    {
        var store = CreateStore();
        store.Save(new WorkspaceState
        {
            ActiveUid = "AB2CD3",
            Tabs =
            [
                new TabState { Uid = "AB2CD3", Name = "Старая" },
                new TabState { Uid = "EF4GH5", Name = "Лишняя" },
            ],
        });

        store.Save(new WorkspaceState
        {
            ActiveUid = "IJ6KL7",
            Tabs = [new TabState { Uid = "IJ6KL7", Name = "Новая" }],
        });

        var loaded = CreateStore().Load();
        var tab = Assert.Single(loaded.Tabs);
        Assert.Equal("IJ6KL7", tab.Uid);
        Assert.Equal("Новая", tab.Name);
        Assert.Equal("IJ6KL7", loaded.ActiveUid);
    }

    [Fact]
    public void Save_ThenLoad_PreservesUnicodeNames()
    {
        const string name = "Отчёты — корзина № 1, файлы";
        CreateStore().Save(new WorkspaceState
        {
            Tabs = [new TabState { Uid = "AB2CD3", Name = name }],
        });

        var loaded = CreateStore().Load();

        Assert.Equal(name, Assert.Single(loaded.Tabs).Name);
    }
}
