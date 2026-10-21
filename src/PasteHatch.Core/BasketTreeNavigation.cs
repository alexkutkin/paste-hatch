namespace PasteHatch.Core;

/// <summary>Owns selection, folder derivation and restoration independently of WinUI nodes.</summary>
public sealed class BasketTreeNavigation(BasketWorkspace workspace)
{
    private readonly Dictionary<string, TreeSelection> _views = new(StringComparer.Ordinal);
    private string? _displayedUid;

    public string CurrentFolder { get; private set; } = string.Empty;

    /// <summary>Captures the old displayed basket before reconciling the new active basket.</summary>
    public void Capture(BasketEntry? selected, IReadOnlyCollection<string> expanded)
    {
        if (_displayedUid is not null)
            _views[_displayedUid] = new TreeSelection(selected?.RelativePath ?? CurrentFolder, expanded.ToArray());
    }

    public TreeSelection Restore()
    {
        var tab = workspace.ActiveTab;
        _displayedUid = tab?.Uid;
        if (tab is null)
        {
            CurrentFolder = string.Empty;
            return new TreeSelection(string.Empty, []);
        }
        foreach (var uid in _views.Keys.Where(uid => workspace.Tabs.All(t => t.Uid != uid)).ToArray())
            _views.Remove(uid);

        var view = _views.GetValueOrDefault(tab.Uid) ?? new TreeSelection(workspace.SelectedFolder, []);
        var path = view.Path;
        BasketEntry? entry;
        while ((entry = Find(path)) is null && path.Length > 0) path = Parent(path);
        Select(entry);
        var expanded = view.ExpandedPaths.Where(p => Find(p)?.IsFolder == true)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar)).ToArray();
        return new TreeSelection(path, expanded);
    }

    public void Select(BasketEntry? entry)
    {
        CurrentFolder = entry is null ? string.Empty : entry.IsFolder ? entry.RelativePath : Parent(entry.RelativePath);
        workspace.RememberFolder(CurrentFolder);
    }

    public IReadOnlyList<FolderCrumb> Breadcrumbs()
    {
        var tab = workspace.ActiveTab;
        if (tab is null) return [];
        var crumbs = new List<FolderCrumb> { new(tab.DisplayName, string.Empty) };
        var path = string.Empty;
        foreach (var part in CurrentFolder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            path = path.Length == 0 ? part : Path.Combine(path, part);
            crumbs.Add(new FolderCrumb(part, path));
        }
        return crumbs;
    }

    public string ParentFolder => Parent(CurrentFolder);

    private BasketEntry? Find(string path)
    {
        var tab = workspace.ActiveTab;
        if (tab is null) return null;
        if (path.Length == 0) return new BasketEntry(tab.DisplayName, tab.DirectoryPath, string.Empty, true, MaterialKind.File);
        return workspace.GetEntries(Parent(path)).FirstOrDefault(e =>
            string.Equals(e.RelativePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static string Parent(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}

public sealed record TreeSelection(string Path, IReadOnlyList<string> ExpandedPaths);

public sealed record FolderCrumb(string Label, string RelativePath)
{
    public override string ToString() => Label;
}
