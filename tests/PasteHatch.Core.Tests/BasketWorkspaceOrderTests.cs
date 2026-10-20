namespace PasteHatch.Core.Tests;

/// <summary>
/// The user's material order is metadata in state.json: files on disk stay the source of truth,
/// so the order must survive restarts but tolerate renamed, deleted and externally added files.
/// </summary>
public sealed class BasketWorkspaceOrderTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void SaveMaterialOrder_PersistsOrderAcrossRestart()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");

        workspace.SaveMaterialOrder(["c.txt", "a.txt", "b.txt"]);

        Assert.Equal(["c.txt", "a.txt", "b.txt"], FileNames(workspace));

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();

        Assert.Equal(["c.txt", "a.txt", "b.txt"], FileNames(restarted));
    }

    [Fact]
    public void Refresh_AppliesSavedOrderToTheActiveBasket()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");
        workspace.SaveMaterialOrder(["b.txt", "c.txt", "a.txt"]);

        workspace.Refresh();

        Assert.Equal(["b.txt", "c.txt", "a.txt"], FileNames(workspace));
    }

    [Fact]
    public void MoveMaterial_MovesOnePositionAndPersists()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");
        var middle = workspace.Materials[1];

        Assert.True(workspace.MoveMaterial(middle, -1));
        Assert.Equal(["b.txt", "a.txt", "c.txt"], FileNames(workspace));

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();
        Assert.Equal(["b.txt", "a.txt", "c.txt"], FileNames(restarted));
    }

    [Fact]
    public void MoveMaterial_AtTheEdge_LeavesOrderUntouched()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt");

        Assert.False(workspace.MoveMaterial(workspace.Materials[0], -1));
        Assert.False(workspace.MoveMaterial(workspace.Materials[1], 1));
        Assert.Equal(["a.txt", "b.txt"], FileNames(workspace));
    }

    [Fact]
    public void RenameMaterial_KeepsTheFileInItsOrderSlot()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");
        workspace.SaveMaterialOrder(["c.txt", "a.txt", "b.txt"]);
        var renamed = workspace.Materials[1];

        Assert.True(workspace.RenameMaterial(renamed, "alpha"));

        Assert.Equal(["c.txt", "alpha.txt", "b.txt"], FileNames(workspace));
        Assert.True(File.Exists(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "alpha.txt")));
    }

    [Fact]
    public void DeleteMaterial_RemovesItFromTheSavedOrder()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");
        workspace.SaveMaterialOrder(["c.txt", "a.txt", "b.txt"]);
        var deleted = workspace.Materials[1];

        Assert.True(workspace.DeleteMaterial(deleted));
        Assert.Equal(["c.txt", "b.txt"], FileNames(workspace));

        var restarted = _harness.CreateWorkspace();
        restarted.Initialize();
        Assert.Equal(["c.txt", "b.txt"], FileNames(restarted));
    }

    [Fact]
    public void SaveMaterialOrder_IgnoresUnknownAndDuplicateNames()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt");

        workspace.SaveMaterialOrder(["missing.txt", "b.txt", "b.txt", "..", "a.txt"]);

        Assert.Equal(["b.txt", "a.txt"], FileNames(workspace));
    }

    [Fact]
    public void ExternallyAddedFile_IsAppendedAfterOrderedFiles()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt");
        workspace.SaveMaterialOrder(["b.txt", "a.txt"]);

        var directory = workspace.RequireActiveTab().DirectoryPath;
        var external = Path.Combine(directory, "external.txt");
        File.WriteAllText(external, "external");
        File.SetCreationTimeUtc(external, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        workspace.Refresh();

        Assert.Equal(["b.txt", "a.txt", "external.txt"], FileNames(workspace));
    }

    [Fact]
    public void SavedOrderWithDeletedFile_SkipsTheMissingEntry()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        AddMaterials(workspace, "a.txt", "b.txt", "c.txt");
        workspace.SaveMaterialOrder(["c.txt", "b.txt", "a.txt"]);

        // Delete behind the workspace's back; the saved order still names the file.
        File.Delete(Path.Combine(workspace.RequireActiveTab().DirectoryPath, "b.txt"));
        workspace.Refresh();

        Assert.Equal(["c.txt", "a.txt"], FileNames(workspace));
    }

    private static void AddMaterials(BasketWorkspace workspace, params string[] names)
    {
        var directory = workspace.RequireActiveTab().DirectoryPath;
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < names.Length; index++)
        {
            var path = Path.Combine(directory, names[index]);
            File.WriteAllText(path, names[index]);
            File.SetCreationTimeUtc(path, start.AddMinutes(index));
        }

        workspace.Refresh();
    }

    private static string[] FileNames(BasketWorkspace workspace)
        => workspace.Materials.Select(material => material.FileName).ToArray();
}
