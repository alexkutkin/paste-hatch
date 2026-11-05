namespace PasteHatch.Core.Tests;

public sealed class BasketDeletionPreferenceTests
{
    [Fact]
    public void DisabledConfirmationRecyclesWithoutPromptAndChangesApplyImmediately()
    {
        using var harness = new WorkspaceHarness();
        var settings = new AppSettings { ConfirmBasketDeletion = false };
        var workspace = CreateWorkspace(harness, settings);
        workspace.Initialize();
        var removed = workspace.CreateAndRequireTab();
        harness.Confirmations.ConfirmBasketResult = false;

        Assert.True(workspace.CloseTab(removed.Uid));
        Assert.Equal(removed.DirectoryPath, Assert.Single(harness.Shell.RecycledDirectories));
        Assert.Empty(harness.Confirmations.BasketConfirmations);

        var retained = workspace.CreateAndRequireTab();
        settings.ConfirmBasketDeletion = true;
        Assert.False(workspace.CloseTab(retained.Uid));
        Assert.Contains(retained, workspace.Tabs);
        Assert.Single(harness.Confirmations.BasketConfirmations);
    }

    private static BasketWorkspace CreateWorkspace(WorkspaceHarness harness, AppSettings settings)
        => new(harness.Temp.BasketsRoot, harness.Store, harness.Clipboard, harness.Shell,
            harness.Confirmations, text: harness.Text,
            confirmBasketDeletion: () => settings.ConfirmBasketDeletion);

    [Fact]
    public void DisabledConfirmationStillProtectsLastBasket()
    {
        using var harness = new WorkspaceHarness();
        var workspace = CreateWorkspace(harness, new AppSettings { ConfirmBasketDeletion = false });
        workspace.Initialize();
        var last = workspace.RequireActiveTab();

        Assert.False(workspace.CloseTab(last.Uid));
        Assert.Equal(last, Assert.Single(workspace.Tabs));
        Assert.Empty(harness.Shell.RecycledDirectories);
        Assert.Empty(harness.Confirmations.BasketConfirmations);
    }

    [Fact]
    public void RecycleFailureKeepsBasketAndPersistedStateWithConfirmationDisabled()
    {
        using var harness = new WorkspaceHarness();
        var workspace = CreateWorkspace(harness, new AppSettings { ConfirmBasketDeletion = false });
        workspace.Initialize();
        var retained = workspace.CreateAndRequireTab();
        harness.Shell.DirectoryRecycleFailure = new IOException("Recycle Bin unavailable");

        Assert.False(workspace.CloseTab(retained.Uid));
        Assert.Contains(retained, workspace.Tabs);
        Assert.Contains("Recycle Bin unavailable", workspace.RequireError());
        Assert.Contains(harness.ReadSavedState().Tabs, tab => tab.Uid == retained.Uid);
        Assert.Empty(harness.Confirmations.BasketConfirmations);
    }

    [Fact]
    public void BasketPreferenceDoesNotBypassFileAndNestedFolderConfirmation()
    {
        using var harness = new WorkspaceHarness();
        var workspace = CreateWorkspace(harness, new AppSettings { ConfirmBasketDeletion = false });
        workspace.Initialize();
        var basket = workspace.RequireActiveTab();
        File.WriteAllText(Path.Combine(basket.DirectoryPath, "note.txt"), "Keep me");
        Directory.CreateDirectory(Path.Combine(basket.DirectoryPath, "nested"));
        harness.Confirmations.ConfirmMaterialResult = false;
        harness.Confirmations.ConfirmFolderResult = false;

        foreach (var entry in workspace.GetEntries(string.Empty))
        {
            Assert.False(workspace.DeleteEntry(entry));
        }

        Assert.Equal("note.txt", Assert.Single(harness.Confirmations.MaterialConfirmations));
        Assert.Equal("nested", Assert.Single(harness.Confirmations.FolderConfirmations));
        Assert.Equal(2, workspace.GetEntries(string.Empty).Count);
        Assert.Empty(harness.Shell.RecycledFiles);
        Assert.Empty(harness.Shell.RecycledDirectories);
    }

    [Fact]
    public void OldAndUnreadableSettingsKeepConfirmationEnabled()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(path, "{\"StartInTray\":false}");
        Assert.True(new SettingsStore(path).Load().ConfirmBasketDeletion);

        File.WriteAllText(path, "invalid json");
        Assert.True(new SettingsStore(path).Load().ConfirmBasketDeletion);
    }

    [Fact]
    public void ConfirmationPreferenceDefaultsOnAndSurvivesCloneAndRestart()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Root, "settings.json"));
        Assert.True(store.Load().ConfirmBasketDeletion);

        var settings = store.Load();
        settings.ConfirmBasketDeletion = false;
        store.Save(settings.Clone());

        Assert.False(new SettingsStore(store.SettingsFilePath).Load().ConfirmBasketDeletion);
    }
}
