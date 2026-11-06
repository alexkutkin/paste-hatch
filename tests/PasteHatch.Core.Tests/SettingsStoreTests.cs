namespace PasteHatch.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Save_CreatesMissingDirectoryAndRestoresAllPreferencesOnRestart()
    {
        var path = Path.Combine(_temp.Root, "nested", "settings.json");
        var store = new SettingsStore(path);
        Assert.False(store.Exists);

        store.Save(new AppSettings
        {
            StartInTray = false,
            RememberWindowPlacement = false,
            AlwaysOnTop = true,
            ConfirmBasketDeletion = false,
            Language = "ru",
        });

        var restarted = new SettingsStore(path);
        var loaded = restarted.Load();
        Assert.True(restarted.Exists);
        Assert.False(loaded.StartInTray);
        Assert.False(loaded.RememberWindowPlacement);
        Assert.True(loaded.AlwaysOnTop);
        Assert.False(loaded.ConfirmBasketDeletion);
        Assert.Equal("ru", loaded.Language);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_AcceptsOlderPropertyCasingAndKeepsDefaultsForMissingPreferences()
    {
        var path = Path.Combine(_temp.Root, "settings.json");
        File.WriteAllText(path, """{"Language":"ru","StartInTray":false}""");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal("ru", loaded.Language);
        Assert.False(loaded.StartInTray);
        Assert.True(loaded.RememberWindowPlacement);
        Assert.True(loaded.ConfirmBasketDeletion);
    }

    [Fact]
    public void Save_WhenTemporaryWriteFails_ReportsFailureAndPreservesSavedPreferences()
    {
        var path = Path.Combine(_temp.Root, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Language = "ru" });
        Directory.CreateDirectory(path + ".tmp");

        var error = Record.Exception(() => store.Save(new AppSettings { Language = "en" }));

        Assert.True(error is IOException or UnauthorizedAccessException);

        Assert.Equal("ru", new SettingsStore(path).Load().Language);
    }
}
