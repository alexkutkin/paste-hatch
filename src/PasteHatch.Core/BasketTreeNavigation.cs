namespace PasteHatch.Core;

/// <summary>
/// Owns tree state: the selected item and the expanded folders of every basket. The current folder
/// is derived from the selection (a selected folder is current, a selected file's parent is current),
/// so no caller has to keep a second copy in sync. Selection survives tree rebuilds and basket
/// switches; when the selected path vanished from disk, it falls back to the nearest existing parent.
/// </summary>
public sealed class BasketTreeNavigation(BasketWorkspace workspace)
{
    private readonly Dictionary<string, TreeSelection> _views = new(StringComparer.Ordinal);
    private string? _displayedUid;

    /// <summary>Folder the user is working in, relative to the basket root; an empty string is the root.</summary>
    public string CurrentFolder => CurrentView.Folder;

    /// <summary>Absolute path of <see cref="CurrentFolder"/> inside the active basket.</summary>
    public string CurrentFolderPath
    {
        get
        {
            var tab = workspace.ActiveTab;
            if (tab is null)
            {
                return string.Empty;
            }

            return CurrentFolder.Length == 0
                ? tab.DirectoryPath
                : Path.Combine(tab.DirectoryPath, CurrentFolder);
        }
    }

    /// <summary>Current folder of any basket, for recording it in the state file.</summary>
    public string FolderFor(string uid)
        => _views.TryGetValue(uid, out var view) ? view.Folder : workspace.SavedFolderFor(uid);

    /// <summary>
    /// Reconciles tree state with the basket that is active now. The arguments describe the tree as
    /// it is still displayed (or <c>null</c> and empty when nothing was displayed yet); the returned
    /// selection tells the caller what to render. One call replaces the capture/restore protocol.
    /// </summary>
    public TreeSelection Reconcile(BasketEntry? displayedSelection, IReadOnlyCollection<string> displayedExpanded)
    {
        if (_displayedUid is not null)
        {
            Capture(displayedSelection, displayedExpanded, _displayedUid);
        }

        var tab = workspace.ActiveTab;
        _displayedUid = tab?.Uid;
        if (tab is null)
        {
            return new TreeSelection(string.Empty, string.Empty, []);
        }

        foreach (var uid in _views.Keys.Where(uid => workspace.Tabs.All(t => t.Uid != uid)).ToArray())
        {
            _views.Remove(uid);
        }

        var view = _views.GetValueOrDefault(tab.Uid)
            ?? new TreeSelection(workspace.SavedFolderFor(tab.Uid), workspace.SavedFolderFor(tab.Uid), []);

        var path = view.Path;
        BasketEntry? entry;
        while ((entry = Find(path)) is null && path.Length > 0)
        {
            path = Parent(path);
        }

        var expanded = view.ExpandedPaths.Where(p => Find(p)?.IsFolder == true)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ToArray();

        var reconciled = new TreeSelection(path, FolderOf(entry), expanded);
        _views[tab.Uid] = reconciled;
        return reconciled;
    }

    /// <summary>Records the item the user selected in the displayed tree.</summary>
    public void Select(BasketEntry? entry)
    {
        var uid = _displayedUid ?? workspace.ActiveTab?.Uid;
        if (uid is null)
        {
            return;
        }

        var previous = _views.GetValueOrDefault(uid) ?? new TreeSelection(string.Empty, string.Empty, []);
        _views[uid] = previous with
        {
            Path = entry?.RelativePath ?? FolderOf(entry),
            Folder = FolderOf(entry),
        };
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

    private TreeSelection CurrentView
        => _views.GetValueOrDefault(_displayedUid ?? workspace.ActiveTab?.Uid ?? string.Empty)
            ?? new TreeSelection(string.Empty, string.Empty, []);

    private void Capture(BasketEntry? displayedSelection, IReadOnlyCollection<string> displayedExpanded, string uid)
    {
        var previous = _views.GetValueOrDefault(uid) ?? new TreeSelection(string.Empty, string.Empty, []);
        _views[uid] = previous with
        {
            Path = displayedSelection?.RelativePath ?? CurrentFolder,
            Folder = displayedSelection is null ? CurrentFolder : FolderOf(displayedSelection),
            ExpandedPaths = displayedExpanded.ToArray(),
        };
    }

    private static string FolderOf(BasketEntry? entry)
        => entry is null ? string.Empty : entry.IsFolder ? entry.RelativePath : Parent(entry.RelativePath);

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

/// <summary>What to select and expand when a basket's tree is rendered.</summary>
public sealed record TreeSelection(string Path, string Folder, IReadOnlyList<string> ExpandedPaths);

public sealed record FolderCrumb(string Label, string RelativePath)
{
    public override string ToString() => Label;
}
