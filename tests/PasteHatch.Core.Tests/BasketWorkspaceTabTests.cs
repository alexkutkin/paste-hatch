namespace PasteHatch.Core.Tests;

public sealed class BasketWorkspaceTabTests : IDisposable
{
    private const string UidAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Refresh_AdoptsExternalNamedFolderWithoutChangingActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var active = workspace.RequireActiveTab();
        var directory = _harness.Temp.CreateBasketDirectory("repo-grooming-skill");
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), "skill");

        workspace.Refresh();

        var adopted = Assert.Single(workspace.Tabs, tab => tab.Uid == "repo-grooming-skill");
        Assert.Equal("repo-grooming-skill", adopted.DisplayName);
        Assert.Equal(directory, adopted.DirectoryPath);
        Assert.Equal(active, workspace.ActiveTab);
        Assert.Contains(_harness.ReadSavedState().Tabs, tab => tab.Uid == adopted.Uid);
        workspace.Refresh();
        Assert.Equal(2, workspace.Tabs.Count);
        var restored = _harness.CreateInitializedWorkspace();
        Assert.Contains(restored.Tabs, tab => tab.Uid == adopted.Uid && tab.DisplayName == adopted.DisplayName);
    }

    [Fact]
    public void Refresh_AdoptionPreservesSelectionNamesAndOrderAndHonorsLimit()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var active = workspace.RequireActiveTab();
        workspace.RenameTab(active.Uid, "Custom basket");
        Directory.CreateDirectory(Path.Combine(active.DirectoryPath, "selected"));
        workspace.RememberFolder("selected");
        for (var i = 0; i < BasketWorkspace.MaxTabs + 1; i++)
            _harness.Temp.CreateBasketDirectory($"external-{i}");

        workspace.Refresh();

        Assert.Equal(BasketWorkspace.MaxTabs, workspace.Tabs.Count);
        Assert.Equal(active.Uid, workspace.Tabs[0].Uid);
        Assert.Equal("Custom basket", workspace.Tabs[0].DisplayName);
        Assert.Equal(active.Uid, workspace.RequireActiveTab().Uid);
        Assert.Equal("selected", workspace.SelectedFolder);
        Assert.Equal(BasketWorkspace.MaxTabs, _harness.ReadSavedState().Tabs.Count);
    }

    [Fact]
    public void Refresh_DoesNotAdoptHiddenOrSystemFolders()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var hidden = _harness.Temp.CreateBasketDirectory("hidden-folder");
        var system = _harness.Temp.CreateBasketDirectory("system-folder");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        File.SetAttributes(system, File.GetAttributes(system) | FileAttributes.System);
        workspace.Refresh();
        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public void Initialize_OnEmptyRoot_CreatesSingleBasket()
    {
        var workspace = _harness.CreateWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        workspace.Initialize();

        var tab = workspace.RequireActiveTab();
        Assert.Single(workspace.Tabs);
        Assert.Equal(6, tab.Uid.Length);
        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$", tab.Uid);
        Assert.All(tab.Uid, character => Assert.Contains(character, UidAlphabet));
        Assert.Equal("Пн, 13:02", tab.DisplayName);
        Assert.Equal(Path.Combine(_harness.Temp.BasketsRoot, tab.Uid), tab.DirectoryPath);
        Assert.True(Directory.Exists(tab.DirectoryPath));
        Assert.Empty(workspace.Materials);
        Assert.Equal(10, workspace.MaxTabCount);
        Assert.True(workspace.CanCreateTab);
        Assert.False(workspace.CanCloseTab);
        Assert.Null(workspace.StatusMessage);
        Assert.Equal(1, changedCount);

        var saved = _harness.ReadSavedState();
        var savedTab = Assert.Single(saved.Tabs);
        Assert.Equal(tab.Uid, savedTab.Uid);
        Assert.Equal("Пн, 13:02", savedTab.Name);
        Assert.Equal(tab.Uid, saved.ActiveUid);
    }

    [Fact]
    public void Initialize_WhenFoldersAndStateExist_RestoresTheSameBasket()
    {
        var firstLaunch = _harness.CreateInitializedWorkspace();
        var uid = firstLaunch.RequireActiveTab().Uid;
        Assert.True(firstLaunch.RenameTab(uid, "Первая корзина"));

        var secondLaunch = _harness.CreateWorkspace();
        secondLaunch.Initialize();

        var restored = secondLaunch.RequireActiveTab();
        Assert.Single(secondLaunch.Tabs);
        Assert.Equal(uid, restored.Uid);
        Assert.Equal("Первая корзина", restored.DisplayName);
    }

    [Fact]
    public void CreateTab_AddsBasketAndActivatesIt()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;

        var created = workspace.CreateAndRequireTab();

        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Equal(created.Uid, workspace.RequireActiveTab().Uid);
        Assert.Equal("Вт, 14:30", created.DisplayName);
        Assert.NotEqual(firstTab.Uid, created.Uid);
        Assert.True(Directory.Exists(created.DirectoryPath));
        Assert.Empty(workspace.Materials);
        Assert.True(workspace.CanCreateTab);
    }

    [Fact]
    public void CreateTab_StopsAtTenBaskets()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;

        for (var i = 1; i < BasketWorkspace.MaxTabs; i++)
        {
            Assert.NotNull(workspace.CreateTab());
        }

        Assert.Equal(10, workspace.Tabs.Count);
        Assert.False(workspace.CanCreateTab);

        var rejected = workspace.CreateTab();

        Assert.Null(rejected);
        Assert.Equal(10, workspace.Tabs.Count);
        Assert.Equal(10, Directory.GetDirectories(_harness.Temp.BasketsRoot).Length);
        Assert.Contains("10", workspace.StatusMessage);
        Assert.Contains("предел", workspace.StatusMessage);
    }

    [Fact]
    public void CreateTab_WhenLimitIsReached_RaisesChangedButAddsNothing()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        for (var i = 1; i < BasketWorkspace.MaxTabs; i++)
        {
            workspace.CreateTab();
        }

        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        var rejected = workspace.CreateTab();

        Assert.Null(rejected);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void RenameTab_UpdatesDisplayNameAndPersistsIt()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();

        var renamed = workspace.RenameTab(tab.Uid, "  Отчёты  ");

        Assert.True(renamed);
        Assert.Equal("Отчёты", workspace.RequireActiveTab().DisplayName);
        Assert.Equal(tab.Uid, workspace.RequireActiveTab().Uid);
        Assert.True(Directory.Exists(tab.DirectoryPath));

        var saved = _harness.ReadSavedState();
        var savedTab = Assert.Single(saved.Tabs);
        Assert.Equal(tab.Uid, savedTab.Uid);
        Assert.Equal("Отчёты", savedTab.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void RenameTab_RejectsEmptyOrWhitespaceName(string? newName)
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();

        var renamed = workspace.RenameTab(tab.Uid, newName);

        Assert.False(renamed);
        Assert.Equal(tab.DisplayName, workspace.RequireActiveTab().DisplayName);
    }

    [Fact]
    public void RenameTab_ForUnknownUid_ReturnsFalse()
    {
        var workspace = _harness.CreateInitializedWorkspace();

        Assert.False(workspace.RenameTab("ZZZZZZ", "Новое имя"));
    }

    [Fact]
    public void RenameTab_NameLongerThanMaximum_IsTruncatedToMaximum()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();
        var longName = new string('Я', BasketWorkspace.MaxTabNameLength + 50);

        var renamed = workspace.RenameTab(tab.Uid, longName);

        Assert.True(renamed);
        Assert.Equal(new string('Я', BasketWorkspace.MaxTabNameLength), workspace.RequireActiveTab().DisplayName);
    }

    [Fact]
    public void ActivateTab_SwitchesActiveTabAndItsMaterials()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;
        var secondTab = workspace.CreateAndRequireTab();
        _harness.Clipboard.Payload = PastePayload.FromText("заметка второй корзины");
        Assert.Equal(1, workspace.AddFromClipboard());

        Assert.True(workspace.ActivateTab(firstTab.Uid));
        Assert.Equal(firstTab.Uid, workspace.RequireActiveTab().Uid);
        Assert.Empty(workspace.Materials);

        Assert.True(workspace.ActivateTab(secondTab.Uid));
        Assert.Equal(secondTab.Uid, workspace.RequireActiveTab().Uid);
        Assert.Single(workspace.Materials);
    }

    [Fact]
    public void ActivateTab_ForUnknownUid_ReturnsFalseAndKeepsActiveTab()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var activeUid = workspace.RequireActiveTab().Uid;

        Assert.False(workspace.ActivateTab("ZZZZZZ"));
        Assert.Equal(activeUid, workspace.RequireActiveTab().Uid);
    }

    [Fact]
    public void ActivateTab_WhenAlreadyActive_ReturnsTrueWithoutRaisingChanged()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        Assert.True(workspace.ActivateTab(workspace.RequireActiveTab().Uid));
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void CloseTab_WhenConfirmationDeclined_KeepsBasketAndDoesNotRecycle()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        _harness.Confirmations.ConfirmBasketResult = false;

        var closed = workspace.CloseTab(firstTab.Uid);

        Assert.False(closed);
        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Empty(_harness.Shell.RecycledDirectories);
        var confirmation = Assert.Single(_harness.Confirmations.BasketConfirmations);
        Assert.Equal(firstTab.DisplayName, confirmation.DisplayName);
        Assert.Equal(firstTab.DirectoryPath, confirmation.DirectoryPath);
        Assert.True(Directory.Exists(firstTab.DirectoryPath));
    }

    [Fact]
    public void CloseTab_WhenConfirmed_RecyclesFolderAndRemovesTab()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;
        var secondTab = workspace.CreateAndRequireTab();
        _harness.Confirmations.ConfirmBasketResult = true;

        var closed = workspace.CloseTab(firstTab.Uid);

        Assert.True(closed);
        Assert.Single(workspace.Tabs);
        Assert.Equal(secondTab.Uid, workspace.RequireActiveTab().Uid);
        Assert.Equal(new[] { firstTab.DirectoryPath }, _harness.Shell.RecycledDirectories);
        Assert.False(Directory.Exists(firstTab.DirectoryPath));
        Assert.Contains(firstTab.DisplayName, workspace.StatusMessage);
        Assert.Contains("Корзину Windows", workspace.StatusMessage);
        Assert.Single(_harness.ReadSavedState().Tabs);
    }

    [Fact]
    public void CloseTab_ForLastRemainingBasket_IsRejected()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var tab = workspace.RequireActiveTab();

        var closed = workspace.CloseTab(tab.Uid);

        Assert.False(closed);
        Assert.Equal("Нельзя закрыть последнюю корзину.", workspace.StatusMessage);
        Assert.Single(workspace.Tabs);
        Assert.False(workspace.CanCloseTab);
        Assert.Empty(_harness.Confirmations.BasketConfirmations);
        Assert.Empty(_harness.Shell.RecycledDirectories);
    }

    [Fact]
    public void CloseTab_ForActiveTab_ActivatesRemainingNeighbour()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        _harness.Clock.Now = TestMoments.TuesdayAfternoon;
        workspace.CreateTab();
        _harness.Clock.Now = TestMoments.WednesdayMorning;
        var thirdTab = workspace.CreateAndRequireTab();
        var secondTab = workspace.Tabs[1];
        Assert.True(workspace.ActivateTab(secondTab.Uid));

        Assert.True(workspace.CloseTab(secondTab.Uid));

        Assert.Equal(thirdTab.Uid, workspace.RequireActiveTab().Uid);
    }

    [Fact]
    public void CloseTab_ForUnknownUid_ReturnsFalse()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        workspace.CreateTab();

        Assert.False(workspace.CloseTab("ZZZZZZ"));
        Assert.Equal(2, workspace.Tabs.Count);
    }

    [Fact]
    public void CloseTab_WhenRecycleFails_KeepsTabAndReportsError()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var firstTab = workspace.RequireActiveTab();
        workspace.CreateTab();
        _harness.Shell.DirectoryRecycleFailure = new IOException("folder is locked");

        var closed = workspace.CloseTab(firstTab.Uid);

        Assert.False(closed);
        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Contains("Не удалось отправить корзину", workspace.StatusMessage);
        Assert.Single(_harness.Confirmations.Errors);
        Assert.True(Directory.Exists(firstTab.DirectoryPath));
    }

    [Fact]
    public void Initialize_RestoresSavedTabWithDisplayNameAndMaterials()
    {
        var directory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        File.WriteAllText(Path.Combine(directory, "заметка.txt"), "содержимое");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "AB2CD3",
            Tabs = [new TabState { Uid = "AB2CD3", Name = "Моя корзина" }],
        });

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        var tab = workspace.RequireActiveTab();
        Assert.Single(workspace.Tabs);
        Assert.Equal("AB2CD3", tab.Uid);
        Assert.Equal("Моя корзина", tab.DisplayName);
        Assert.Equal(directory, tab.DirectoryPath);
        var material = workspace.RequireSingleMaterial();
        Assert.Equal("заметка.txt", material.FileName);
        Assert.Equal(MaterialKind.Text, material.Kind);
    }

    [Fact]
    public void Initialize_PrunesSavedTabsWhoseFolderIsMissing()
    {
        _harness.Temp.CreateBasketDirectory("GHJKLM");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "AB2CD3",
            Tabs =
            [
                new TabState { Uid = "AB2CD3", Name = "Пропавшая" },
                new TabState { Uid = "GHJKLM", Name = "Вторая" },
            ],
        });

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        var tab = workspace.RequireActiveTab();
        Assert.Single(workspace.Tabs);
        Assert.Equal("GHJKLM", tab.Uid);
        Assert.Equal("Вторая", tab.DisplayName);
        Assert.False(Directory.Exists(Path.Combine(_harness.Temp.BasketsRoot, "AB2CD3")));

        var saved = _harness.ReadSavedState();
        Assert.Equal("GHJKLM", Assert.Single(saved.Tabs).Uid);
    }

    [Fact]
    public void Initialize_AdoptsFoldersThatAreNotInTheSavedState()
    {
        var firstDirectory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        var secondDirectory = _harness.Temp.CreateBasketDirectory("GHJKLM");
        Directory.SetCreationTime(firstDirectory, TestMoments.MondayAfternoon.DateTime);
        Directory.SetCreationTime(secondDirectory, TestMoments.TuesdayAfternoon.DateTime);
        // "Now" is later than both folder creation times, as it would be on the next launch.
        _harness.Clock.Now = TestMoments.WednesdayMorning;

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        Assert.Equal(2, workspace.Tabs.Count);
        var namesByUid = workspace.Tabs.ToDictionary(tab => tab.Uid, tab => tab.DisplayName);
        Assert.Equal("Пн, 13:02", namesByUid["AB2CD3"]);
        Assert.Equal("Вт, 14:30", namesByUid["GHJKLM"]);

        var saved = _harness.ReadSavedState();
        Assert.Equal(2, saved.Tabs.Count);
    }

    [Fact]
    public void Initialize_AdoptsNamedFoldersWithoutRenamingThem()
    {
        // External folders retain their names even when they are not generated basket UIDs.
        var invalidNames = new[] { "invalid", "abcdef", "ABCDE", "ABCDE0", "TOOLONG", "IIIIII", "OOOOOO", "000000" };
        foreach (var invalidName in invalidNames)
        {
            _harness.Temp.CreateBasketDirectory(invalidName);
        }

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        Assert.Equal(invalidNames.Length, workspace.Tabs.Count);
        Assert.All(workspace.Tabs, tab => Assert.Equal(tab.Uid, tab.DisplayName));
        Assert.Equal(invalidNames.Length, Directory.GetDirectories(_harness.Temp.BasketsRoot).Length);
        foreach (var invalidName in invalidNames)
        {
            Assert.True(Directory.Exists(Path.Combine(_harness.Temp.BasketsRoot, invalidName)));
        }
    }

    [Fact]
    public void Initialize_DeduplicatesSavedTabsWithTheSameUid()
    {
        _harness.Temp.CreateBasketDirectory("AB2CD3");
        _harness.Store.Save(new WorkspaceState
        {
            Tabs =
            [
                new TabState { Uid = "AB2CD3", Name = "Первая запись" },
                new TabState { Uid = "AB2CD3", Name = "Дубликат" },
            ],
        });

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        var tab = Assert.Single(workspace.Tabs);
        Assert.Equal("AB2CD3", tab.Uid);
        Assert.Equal("Первая запись", tab.DisplayName);
    }

    [Fact]
    public void Initialize_WhenSavedNameIsBlank_FallsBackToFolderCreationTime()
    {
        var directory = _harness.Temp.CreateBasketDirectory("AB2CD3");
        Directory.SetCreationTime(directory, TestMoments.MondayAfternoon.DateTime);
        _harness.Store.Save(new WorkspaceState
        {
            Tabs = [new TabState { Uid = "AB2CD3", Name = "   " }],
        });

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        Assert.Equal("Пн, 13:02", workspace.RequireActiveTab().DisplayName);
    }

    [Fact]
    public void Initialize_UsesSavedActiveTabWhenItStillExists()
    {
        _harness.Temp.CreateBasketDirectory("AB2CD3");
        _harness.Temp.CreateBasketDirectory("GHJKLM");
        _harness.Store.Save(new WorkspaceState
        {
            ActiveUid = "GHJKLM",
            Tabs =
            [
                new TabState { Uid = "AB2CD3", Name = "Первая" },
                new TabState { Uid = "GHJKLM", Name = "Вторая" },
            ],
        });

        var workspace = _harness.CreateWorkspace();
        workspace.Initialize();

        Assert.Equal("GHJKLM", workspace.RequireActiveTab().Uid);
    }
}
