using System.Text.RegularExpressions;

namespace PasteHatch.Core;

/// <summary>
/// The application logic for baskets, folders, materials and tab state. It owns the invariants
/// that the UI and the requirements depend on: at most 100 open baskets, always an active basket,
/// files on disk as the source of truth, and everything that leaves the app going through the
/// Windows Recycle Bin only after a confirmation.
/// </summary>
public sealed class BasketWorkspace
{
    public const int MaxTabs = 100;
    public const int MaxTabNameLength = 120;

    private const string UidAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex UidPattern = new("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$", RegexOptions.CultureInvariant);

    private static readonly EnumerationOptions ChildEntriesOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        // Explorer hides hidden/system entries by default; junctions and symlinks inside a basket
        // could point outside it or form a cycle, so they are never shown or followed.
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    private readonly string _basketsRoot;
    private readonly ITabStateStore _stateStore;
    private readonly IClipboardReader _clipboard;
    private readonly IShellService _shell;
    private readonly IConfirmationService _confirmations;
    private readonly IClock _clock;
    private readonly TabNameFormatter _formatter;
    private readonly ITextProvider _text;
    private readonly FileTransfer _transfers;

    private readonly List<BasketTab> _tabs = [];
    private readonly Dictionary<string, string> _savedFolders = new(StringComparer.Ordinal);
    private readonly Func<BasketTab, string> _currentFolderOf;
    private string? _activeUid;

    public BasketWorkspace(
        string basketsRoot,
        ITabStateStore stateStore,
        IClipboardReader clipboard,
        IShellService shell,
        IConfirmationService confirmations,
        IClock? clock = null,
        TabNameFormatter? formatter = null,
        ITextProvider? text = null,
        Func<BasketTab, string>? currentFolderOf = null)
    {
        _basketsRoot = basketsRoot;
        _stateStore = stateStore;
        _clipboard = clipboard;
        _shell = shell;
        _text = text ?? KeyTextProvider.Instance;
        _transfers = new FileTransfer(shell, _text);
        _confirmations = confirmations;
        _clock = clock ?? SystemClock.Instance;
        _formatter = formatter ?? new TabNameFormatter();
        _currentFolderOf = currentFolderOf ?? (_ => string.Empty);
    }

    public event EventHandler<WorkspaceChange>? Changed;

    public IReadOnlyList<BasketTab> Tabs => _tabs;

    public string BasketsRoot => _basketsRoot;

    /// <summary>Window geometry recorded in the state file; the window owns its values.</summary>
    public WindowGeometry Geometry { get; } = new();

    public BasketTab? ActiveTab => _tabs.Find(tab => tab.Uid == _activeUid) ?? _tabs.FirstOrDefault();

    /// <summary>
    /// Entries of any folder in the active basket. Folders come first, then files, both in
    /// Explorer-like natural order; the tree uses this for lazy loading.
    /// </summary>
    public IReadOnlyList<BasketEntry> GetEntries(string? relativeFolder)
    {
        var tab = ActiveTab;
        var normalized = NormalizeRelativePath(relativeFolder);
        if (tab is null || normalized is null)
        {
            return [];
        }

        var directory = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            var folders = new DirectoryInfo(directory)
                .EnumerateDirectories("*", ChildEntriesOptions)
                .OrderBy(folder => folder.Name, NaturalStringComparer.Instance)
                .Select(folder => new BasketEntry(
                    folder.Name,
                    folder.FullName,
                    RelativeTo(tab, folder.FullName),
                    IsFolder: true,
                    MaterialKind.File)
                {
                    Modified = folder.LastWriteTime,
                });

            var files = new DirectoryInfo(directory)
                .EnumerateFiles("*", ChildEntriesOptions)
                .OrderBy(file => file.Name, NaturalStringComparer.Instance)
                .Select(file => new BasketEntry(
                    file.Name,
                    file.FullName,
                    RelativeTo(tab, file.FullName),
                    IsFolder: false,
                    MaterialClassifier.Classify(file.Name))
                {
                    Size = file.Length,
                    Modified = file.LastWriteTime,
                });

            return folders.Concat(files).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>True when a folder has at least one visible child, so the tree shows a chevron.</summary>
    public bool HasEntries(string? relativeFolder)
    {
        var tab = ActiveTab;
        var normalized = NormalizeRelativePath(relativeFolder);
        if (tab is null || normalized is null)
        {
            return false;
        }

        var directory = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(directory, "*", ChildEntriesOptions).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Writes the current tab state immediately (used when hiding to the tray or exiting).</summary>
    public void SaveState() => SaveStateQuietly();

    /// <summary>
    /// Folder this basket had open when the state file was last read. Tree state owns the current
    /// folder; this is only the seed it restores on the first reconcile after a launch.
    /// </summary>
    public string SavedFolderFor(string uid) => _savedFolders.GetValueOrDefault(uid, string.Empty);

    public int MaxTabCount => MaxTabs;

    public bool CanCreateTab => _tabs.Count < MaxTabs;

    public bool CanCloseTab => _tabs.Count > 1;

    /// <summary>Last user-facing message (copy result, rejected paste, save failure).</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>True when <see cref="StatusMessage"/> describes an error that should stay visible.</summary>
    public bool StatusIsError { get; private set; }

    /// <summary>Restores open baskets from the state file and from the folders still on disk.</summary>
    public void Initialize()
    {
        Directory.CreateDirectory(_basketsRoot);

        var state = _stateStore.Load();
        Geometry.Restore(state.Window, state.PreviewWidth);

        _tabs.Clear();
        _savedFolders.Clear();
        LoadTabs(state);

        if (_tabs.Count == 0)
        {
            CreateBasketInternal();
        }

        _activeUid = _tabs.Any(tab => tab.Uid == state.ActiveUid) ? state.ActiveUid : _tabs[0].Uid;
        SaveStateQuietly();
        Publish(WorkspaceChangeKind.View);
    }

    public BasketTab? CreateTab()
    {
        if (_tabs.Count >= MaxTabs)
        {
            SetStatus(_text.Get("Status_BasketLimit"));
            Publish();
            return null;
        }

        BasketTab tab;
        try
        {
            tab = CreateBasketInternal();
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CreateBasket", ex.Message));
            return null;
        }

        _activeUid = tab.Uid;
        Commit();
        return tab;
    }

    public bool ActivateTab(string uid)
    {
        if (!_tabs.Any(tab => tab.Uid == uid))
        {
            return false;
        }

        if (string.Equals(_activeUid, uid, StringComparison.Ordinal))
        {
            return true;
        }

        _activeUid = uid;
        Commit(WorkspaceChangeKind.View);
        return true;
    }

    /// <summary>Renames a tab; the basket folder keeps its UID, only the display name changes.</summary>
    public bool RenameTab(string uid, string? newName)
    {
        var index = _tabs.FindIndex(tab => tab.Uid == uid);
        if (index < 0)
        {
            return false;
        }

        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Length > MaxTabNameLength)
        {
            trimmed = trimmed[..MaxTabNameLength].TrimEnd();
        }

        if (string.Equals(_tabs[index].DisplayName, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        _tabs[index] = _tabs[index] with { DisplayName = trimmed };
        Commit(WorkspaceChangeKind.View);
        return true;
    }

    /// <summary>Confirms, recycles the basket folder, then removes the tab.</summary>
    public bool CloseTab(string uid)
    {
        if (_tabs.Count <= 1)
        {
            SetStatus(_text.Get("Status_LastBasket"));
            Publish();
            return false;
        }

        var index = _tabs.FindIndex(tab => tab.Uid == uid);
        if (index < 0)
        {
            return false;
        }

        var tab = _tabs[index];
        if (!_confirmations.ConfirmRecycleBasket(tab.DisplayName, tab.DirectoryPath))
        {
            return false;
        }

        try
        {
            _shell.RecycleDirectory(tab.DirectoryPath);
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_RecycleBasket", ex.Message));
            return false;
        }

        _tabs.RemoveAt(index);
        _savedFolders.Remove(uid);
        if (string.Equals(_activeUid, uid, StringComparison.Ordinal))
        {
            _activeUid = _tabs[Math.Min(index, _tabs.Count - 1)].Uid;
        }

        SetStatus(_text.Format("Status_BasketRecycled", tab.DisplayName));
        Commit();
        return true;
    }

    /// <summary>
    /// Applies the tab order produced by dragging a tab in the TabView. Unknown UIDs are ignored and
    /// tabs missing from the list keep their relative order at the end, so a stale drag payload can
    /// never hide an open basket. The change is published like every other mutation.
    /// </summary>
    public bool ReorderTabs(IReadOnlyList<string> orderedUids)
    {
        if (orderedUids is null || orderedUids.Count == 0)
        {
            return false;
        }

        var remaining = new List<BasketTab>(_tabs);
        var reordered = new List<BasketTab>(_tabs.Count);
        foreach (var uid in orderedUids)
        {
            var index = remaining.FindIndex(tab => string.Equals(tab.Uid, uid, StringComparison.Ordinal));
            if (index >= 0)
            {
                reordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
        }

        reordered.AddRange(remaining);

        if (reordered.Select(tab => tab.Uid).SequenceEqual(_tabs.Select(tab => tab.Uid), StringComparer.Ordinal))
        {
            return false;
        }

        _tabs.Clear();
        _tabs.AddRange(reordered);
        SaveStateQuietly();
        Publish(WorkspaceChangeKind.View);
        return true;
    }

    /// <summary>Moves one tab by <paramref name="offset"/> positions and reports the change.</summary>
    public bool MoveTab(string uid, int offset)
    {
        if (offset == 0)
        {
            return false;
        }

        var index = _tabs.FindIndex(tab => string.Equals(tab.Uid, uid, StringComparison.Ordinal));
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _tabs.Count)
        {
            return false;
        }

        var moved = _tabs[index];
        _tabs.RemoveAt(index);
        _tabs.Insert(target, moved);
        SaveStateQuietly();
        Publish(WorkspaceChangeKind.View);
        return true;
    }

    // ---- Clipboard and adding materials ----------------------------------------------------

    /// <summary>Handles Ctrl+V into a concrete folder of the active basket.</summary>
    public int AddFromClipboard(string? targetRelativeFolder)
    {
        PastePayload payload;
        try
        {
            payload = _clipboard.Read();
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_ReadClipboard", ex.Message));
            return 0;
        }

        return AddPayload(payload, targetRelativeFolder);
    }

    /// <summary>Adds an already read clipboard payload to a folder of the active basket.</summary>
    private int AddPayload(PastePayload payload, string? targetRelativeFolder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Kind != PasteContentKind.Files) _transfers.ForgetClipboard();

        var targetDirectory = ResolveTargetDirectory(targetRelativeFolder);
        if (ActiveTab is null || targetDirectory is null)
        {
            return 0;
        }

        switch (payload.Kind)
        {
            case PasteContentKind.Files:
                return TransferFiles(payload.Files, targetDirectory, _transfers.ClipboardIntent(payload.Files));

            case PasteContentKind.Image:
                return AddImage(payload.ImagePng, targetDirectory.Path);

            case PasteContentKind.Text:
                return AddText(payload.Text, targetDirectory.Path);

            default:
                SetStatus(_text.Get("Status_UnsupportedClipboard"));
                Publish();
                return 0;
        }
    }

    /// <summary>Handles files or folders dropped onto a concrete folder of the active basket.</summary>
    public int AddFiles(IReadOnlyList<string> paths, string? targetRelativeFolder)
    {
        var targetDirectory = ResolveTargetDirectory(targetRelativeFolder);
        if (targetDirectory is null)
        {
            return 0;
        }

        return TransferFiles(paths, targetDirectory, FileTransferIntent.Import);
    }

    /// <summary>Saves the quick-note text as a .txt file named after the current day and time.</summary>
    public BasketEntry? AddQuickNote(string? text, string? targetRelativeFolder)
    {
        var tab = ActiveTab;
        if (tab is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var target = ResolveWritableDirectory(targetRelativeFolder);
        if (target is null)
        {
            return null;
        }

        try
        {
            var fileName = MaterialNaming.DefaultFileName(
                target.Path,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt",
                _text);
            var fullPath = Path.Combine(target.Path, fileName);
            File.WriteAllText(fullPath, text, MaterialNaming.Utf8NoBom);
            Commit();
            var info = new FileInfo(fullPath);
            return new BasketEntry(fileName, fullPath, RelativeTo(tab, fullPath), IsFolder: false, MaterialKind.Text)
            {
                Size = info.Length,
                Modified = info.LastWriteTime,
            };
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_SaveNote", ex.Message));
            return null;
        }
    }

    // ---- Folder operations -----------------------------------------------------------------

    /// <summary>Creates a subfolder in a folder of the active basket; returns it when creation succeeded.</summary>
    public BasketEntry? CreateFolder(string? name, string? targetRelativeFolder, bool ensureUnique = false)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return null;
        }

        var cleaned = MaterialNaming.ValidateFolderName(name, _text, out var error);
        if (cleaned is null)
        {
            Fail(error ?? _text.Get("Error_InvalidFolderName"));
            return null;
        }

        var parent = ResolveWritableDirectory(targetRelativeFolder);
        if (parent is null)
        {
            return null;
        }

        try
        {
            var freeName = ensureUnique ? FileTransfer.UniqueDirectoryName(parent.Path, cleaned, _text) : cleaned;
            var fullPath = Path.Combine(parent.Path, freeName);
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
            {
                Fail(_text.Format("Error_FolderExists", freeName));
                return null;
            }

            Directory.CreateDirectory(fullPath);
            SetStatus(_text.Format("Status_FolderCreated", freeName));
            Commit();
            return GetEntries(parent.Folder).FirstOrDefault(entry =>
                entry.IsFolder &&
                string.Equals(entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CreateFolder", ex.Message));
            return null;
        }
    }

    /// <summary>
    /// Renames a file or a folder without touching its contents. Kind rules stay inside: a material
    /// keeps its extension, a folder gets a folder name, a collision is refused, and a case-only
    /// rename goes through a temporary name. Renaming a nested folder changes the paths of its
    /// contents; renaming a basket is <see cref="RenameTab"/> and keeps its folder.
    /// </summary>
    public bool RenameEntry(BasketEntry entry, string? newBaseName)
    {
        if (ActiveTab is null || entry.RelativePath.Length == 0)
        {
            return false;
        }

        string? targetName;
        if (entry.IsFolder)
        {
            var cleaned = MaterialNaming.ValidateFolderName(newBaseName, _text, out var error);
            if (cleaned is null)
            {
                Fail(error ?? _text.Get("Error_InvalidFolderName"));
                return false;
            }

            targetName = cleaned;
        }
        else
        {
            var extension = Path.GetExtension(entry.Name);
            var cleaned = MaterialNaming.ValidateRenameBase(newBaseName, extension, _text, out var error);
            if (cleaned is null)
            {
                Fail(error ?? _text.Get("Error_InvalidFileName"));
                return false;
            }

            targetName = cleaned + extension;
        }

        if (string.Equals(targetName, entry.Name, StringComparison.Ordinal))
        {
            return true;
        }

        var sourcePath = entry.FullPath;
        var parent = Path.GetDirectoryName(sourcePath);
        var exists = entry.IsFolder ? Directory.Exists(sourcePath) : File.Exists(sourcePath);
        if (parent is null || !exists)
        {
            Fail(entry.IsFolder
                ? _text.Format("Error_FolderNotFoundMovedNamed", entry.Name)
                : _text.Format("Error_FileNotFoundMoved", entry.Name), WorkspaceChangeKind.View);
            return false;
        }

        var targetPath = Path.Combine(parent, targetName);
        // Path checks are case-insensitive on Windows, so the "already exists" check must not fire
        // for the same entry renamed to a different case.
        var caseOnlyRename = string.Equals(targetName, entry.Name, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && (File.Exists(targetPath) || Directory.Exists(targetPath)))
        {
            Fail(entry.IsFolder
                ? _text.Format("Error_FolderExists", targetName)
                : _text.Format("Error_FileExists", targetName));
            return false;
        }

        try
        {
            MoveOnDisk(sourcePath, targetPath, entry.IsFolder, caseOnlyRename);
        }
        catch (Exception ex)
        {
            Fail(entry.IsFolder
                ? _text.Format("Error_RenameFolder", ex.Message)
                : _text.Format("Error_RenameFile", ex.Message));
            return false;
        }

        SetStatus(entry.IsFolder
            ? _text.Format("Status_FolderRenamed", entry.Name, targetName)
            : _text.Format("Status_FileRenamed", entry.Name, targetName));
        Commit();
        return true;
    }

    /// <summary>Confirms, then recycles one entry with everything inside it.</summary>
    public bool DeleteEntry(BasketEntry entry)
    {
        if (ActiveTab is null || entry.RelativePath.Length == 0)
        {
            return false;
        }

        if (entry.IsFolder && !Directory.Exists(entry.FullPath))
        {
            Fail(_text.Format("Error_FolderNotFound", entry.Name), WorkspaceChangeKind.View);
            return false;
        }

        var confirmed = entry.IsFolder
            ? _confirmations.ConfirmRecycleFolder(entry.Name)
            : _confirmations.ConfirmRecycleMaterial(entry.Name);
        if (!confirmed)
        {
            return false;
        }

        try
        {
            if (entry.IsFolder)
            {
                _shell.RecycleDirectory(entry.FullPath);
            }
            else if (File.Exists(entry.FullPath))
            {
                _shell.RecycleFile(entry.FullPath);
            }
        }
        catch (Exception ex)
        {
            Fail(entry.IsFolder
                ? _text.Format("Error_RecycleFolder", ex.Message)
                : _text.Format("Error_RecycleFile", ex.Message));
            return false;
        }

        SetStatus(entry.IsFolder
            ? _text.Format("Status_FolderRecycled", entry.Name)
            : _text.Format("Status_FileRecycled", entry.Name));
        Commit();
        return true;
    }

    /// <summary>Opens a file with its default application, or a folder in Windows Explorer.</summary>
    public void OpenEntry(BasketEntry entry)
    {
        if (entry.IsFolder)
        {
            if (!Directory.Exists(entry.FullPath))
            {
                Fail(_text.Format("Error_FolderNotFound", entry.Name), WorkspaceChangeKind.View);
                return;
            }

            try
            {
                _shell.OpenFolder(entry.FullPath);
            }
            catch (Exception ex)
            {
                Fail(_text.Format("Error_OpenFolder", ex.Message));
            }

            return;
        }

        if (!File.Exists(entry.FullPath))
        {
            Fail(_text.Format("Error_FileNotFound", entry.Name), WorkspaceChangeKind.View);
            return;
        }

        try
        {
            _shell.OpenItem(entry.FullPath);
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_OpenFile", ex.Message));
        }
    }

    /// <summary>Opens a folder of the active basket in Windows Explorer, creating it when missing.</summary>
    public void OpenActiveBasketFolder(string? targetRelativeFolder)
    {
        if (ActiveTab is null)
        {
            return;
        }

        var target = ResolveWritableDirectory(targetRelativeFolder);
        if (target is null)
        {
            return;
        }

        try
        {
            _shell.OpenFolder(target.Path);
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_OpenBasketFolder", ex.Message));
        }
    }

    /// <summary>Copies the full path of a folder of the active basket, without quotes.</summary>
    public void CopyActiveBasketPath(string? targetRelativeFolder)
    {
        if (ActiveTab is null)
        {
            return;
        }

        var directory = ResolveFolderPath(targetRelativeFolder);
        if (directory is null)
        {
            Fail(_text.Get("Error_InvalidFolderPath"));
            return;
        }

        try
        {
            _shell.CopyText(directory);
            SetStatus(_text.Get("Status_FolderPathCopied"));
            Publish();
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CopyPath", ex.Message));
        }
    }

    /// <summary>Copies the full path of a file or folder.</summary>
    public void CopyEntryPath(BasketEntry entry)
    {
        try
        {
            _shell.CopyText(entry.FullPath);
            SetStatus(_text.Get(entry.IsFolder ? "Status_FolderPathCopied" : "Status_FilePathCopied"));
            Publish();
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CopyPath", ex.Message));
        }
    }

    /// <summary>
    /// Copies what a material holds: the text of a text file, the address of a link, the picture
    /// of an image. Any other file goes to the clipboard as a file, the way Explorer copies it.
    /// </summary>
    public void CopyEntryContent(BasketEntry entry)
    {
        if (entry.IsFolder) return;
        try
        {
            switch (entry.Kind)
            {
                case MaterialKind.Text:
                    _shell.CopyText(MaterialPreview.ReadAllText(entry.FullPath));
                    break;
                case MaterialKind.Link:
                    _shell.CopyText(MaterialPreview.ReadLinkTarget(entry.FullPath));
                    break;
                case MaterialKind.Image:
                    _shell.CopyImageToClipboard(entry.FullPath);
                    break;
                default:
                    _shell.CopyFilesToClipboard([entry.FullPath], move: false);
                    break;
            }

            SetStatus(_text.Format("Status_ContentCopied", entry.Name));
            Publish();
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CopyToClipboard", ex.Message));
        }
    }

    // ---- Moving entries --------------------------------------------------------------------

    /// <summary>Moves several already resolved paths (a cut) into a folder.</summary>
    public int MoveFiles(IReadOnlyList<string> paths, string? targetRelativeFolder)
    {
        if (paths.Count == 0)
        {
            SetStatus(_text.Get("Status_NoFilesToMove"));
            Publish();
            return 0;
        }

        var target = ResolveTargetDirectory(targetRelativeFolder);
        if (target is null)
        {
            return 0;
        }

        return TransferFiles(paths, target, FileTransferIntent.Move);
    }

    // ---- Refresh and state -----------------------------------------------------------------

    /// <summary>Re-reads the baskets root, e.g. after an external agent changed it.</summary>
    public void Refresh()
    {
        if (AdoptFolders()) SaveStateQuietly();
        Publish(WorkspaceChangeKind.View);
    }

    /// <summary>Publishes an informational status message (clipboard operations from the UI).</summary>
    public void ReportStatus(string message)
    {
        SetStatus(message);
        Publish();
    }

    private void LoadTabs(WorkspaceState state)
    {
        var savedTabs = state.Tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Uid))
            .GroupBy(tab => tab.Uid, StringComparer.Ordinal)
            .Select(group => group.First());

        foreach (var saved in savedTabs)
        {
            if (_tabs.Count >= MaxTabs)
            {
                break;
            }

            var directoryPath = Path.Combine(_basketsRoot, saved.Uid);
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            var name = NormalizeDisplayName(saved.Name) ?? DefaultNameFor(directoryPath);
            _tabs.Add(new BasketTab(saved.Uid, name, directoryPath));
            _savedFolders[saved.Uid] = NormalizeRelativePath(saved.SelectedFolder) ?? string.Empty;
        }

        AdoptFolders();
    }

    private bool AdoptFolders()
    {
        var adopted = false;
        // Folders that exist on disk but not in the state file (lost state, crash after create)
        // are still user baskets, so they are adopted instead of being left unreachable.
        var orphanDirectories = new DirectoryInfo(_basketsRoot)
            .EnumerateDirectories("*", ChildEntriesOptions)
            .Where(info => _tabs.All(tab => !string.Equals(tab.Uid, info.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(info => info.CreationTimeUtc)
            .ThenBy(info => info.Name, StringComparer.Ordinal);

        foreach (var directory in orphanDirectories)
        {
            if (_tabs.Count >= MaxTabs)
            {
                break;
            }

            _tabs.Add(new BasketTab(
                directory.Name,
                UidPattern.IsMatch(directory.Name) ? DefaultNameFor(directory.FullName) : directory.Name,
                directory.FullName));
            adopted = true;
        }
        return adopted;
    }

    private BasketTab CreateBasketInternal()
    {
        var uid = GenerateUid();
        var directoryPath = Path.Combine(_basketsRoot, uid);
        Directory.CreateDirectory(directoryPath);
        var tab = new BasketTab(uid, _formatter.Format(_clock.Now), directoryPath);
        _tabs.Add(tab);
        return tab;
    }

    private string GenerateUid()
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = UidAlphabet[Random.Shared.Next(UidAlphabet.Length)];
            }

            var uid = new string(chars);
            if (_tabs.All(tab => !string.Equals(tab.Uid, uid, StringComparison.Ordinal)) &&
                !Directory.Exists(Path.Combine(_basketsRoot, uid)))
            {
                return uid;
            }
        }

        throw new InvalidOperationException(_text.Get("Error_GenerateUid"));
    }

    private int TransferFiles(IReadOnlyList<string> paths, TargetFolder target, FileTransferIntent intent)
    {
        var tab = ActiveTab;
        if (tab is null) return 0;

        var result = _transfers.Execute(paths, tab.DirectoryPath, target.Path, target.Folder, intent);
        SetStatus(result.Status);
        if (result.Problems.Count > 0)
        {
            SetError(string.Join(Environment.NewLine, result.Problems.Take(6)));
        }
        // Publish once, after status and partial failures are final.
        Commit();
        return result.Completed;
    }

    /// <summary>Publishes a file selection and remembers its paste intent outside the window.</summary>
    public void CopyEntryToClipboard(BasketEntry entry, bool move)
    {
        if (entry.RelativePath.Length == 0) return;
        try
        {
            string[] paths = [entry.FullPath];
            _shell.CopyFilesToClipboard(paths, move);
            _transfers.RememberClipboard(paths, move);
            ReportStatus(_text.Format(move ? "Status_EntryCut" : "Status_EntryCopied", entry.Name));
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CopyToClipboard", ex.Message));
        }
    }

    private int AddImage(byte[]? pngBytes, string targetDirectory)
    {
        if (pngBytes is not { Length: > 0 })
        {
            SetStatus(_text.Get("Status_NoImage"));
            Publish();
            return 0;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            var fileName = MaterialNaming.DefaultFileName(
                targetDirectory,
                _formatter.FormatAsFileName(_clock.Now),
                ".png",
                _text);
            File.WriteAllBytes(Path.Combine(targetDirectory, fileName), pngBytes);
            SetStatus(_text.Format("Status_ScreenshotAdded", fileName));
            Commit();
            return 1;
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_SaveImage", ex.Message));
            return 0;
        }
    }

    private int AddText(string? text, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus(_text.Get("Status_NoText"));
            Publish();
            return 0;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);

            if (MaterialNaming.TryGetLink(text, out var link) && link is not null)
            {
                var urlFileName = MaterialNaming.DefaultFileName(
                    targetDirectory,
                    _formatter.FormatAsFileName(_clock.Now),
                    ".url",
                    _text);
                File.WriteAllText(
                    Path.Combine(targetDirectory, urlFileName),
                    MaterialNaming.CreateInternetShortcutContent(link),
                    MaterialNaming.Utf8NoBom);
                SetStatus(_text.Format("Status_LinkAdded", link.Host));
                Commit();
                return 1;
            }

            var textFileName = MaterialNaming.DefaultFileName(
                targetDirectory,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt",
                _text);
            File.WriteAllText(
                Path.Combine(targetDirectory, textFileName),
                text,
                MaterialNaming.Utf8NoBom);
            SetStatus(_text.Get("Status_TextAdded"));
            Commit();
            return 1;
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_SaveText", ex.Message));
            return 0;
        }
    }

    private void Commit(WorkspaceChangeKind kind = WorkspaceChangeKind.Contents)
    {
        SaveStateQuietly();
        Publish(kind);
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        StatusIsError = false;
    }

    /// <summary>Records an error as model state without publishing, for paths that publish at their end.</summary>
    private void SetError(string message)
    {
        StatusMessage = message;
        StatusIsError = true;
    }

    /// <summary>Records an error and publishes it once. Presenting it is the view's decision.</summary>
    private void Fail(string message, WorkspaceChangeKind kind = WorkspaceChangeKind.Status)
    {
        SetError(message);
        Publish(kind);
    }

    /// <summary>
    /// The only place a change leaves this module. An operation publishes exactly once, after its
    /// status and results are final; the kind tells the refresh coordinator what is affected.
    /// </summary>
    private void Publish(WorkspaceChangeKind kind = WorkspaceChangeKind.Status)
        => Changed?.Invoke(this, new WorkspaceChange(kind));

    private string DefaultNameFor(string directoryPath)
    {
        try
        {
            var createdAt = Directory.GetCreationTime(directoryPath);
            var now = _clock.Now.LocalDateTime;
            if (createdAt.Year is > 2000 and < 2200 && createdAt <= now.AddMinutes(1))
            {
                return _formatter.Format(new DateTimeOffset(createdAt));
            }
        }
        catch (Exception)
        {
            // Fall back to "now" below.
        }

        return _formatter.Format(_clock.Now);
    }

    private void SaveStateQuietly()
    {
        try
        {
            _stateStore.Save(new WorkspaceState
            {
                Version = WorkspaceState.CurrentVersion,
                ActiveUid = _activeUid,
                Tabs = _tabs.Select(tab => new TabState
                {
                    Uid = tab.Uid,
                    Name = tab.DisplayName,
                    SelectedFolder = _currentFolderOf(tab),
                }).ToList(),
                Window = Geometry.Placement,
                PreviewWidth = Geometry.PreviewWidth,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = _text.Format("Error_SaveTabState", ex.Message);
            StatusIsError = true;
        }
    }

    /// <summary>Moves a file or folder to its new name; a case-only rename goes through a temporary name.</summary>
    private static void MoveOnDisk(string sourcePath, string targetPath, bool isFolder, bool caseOnlyRename)
    {
        if (!caseOnlyRename)
        {
            if (isFolder)
            {
                Directory.Move(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
            }

            return;
        }

        var temporaryPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"~{Guid.NewGuid():N}.tmp");
        if (isFolder)
        {
            Directory.Move(sourcePath, temporaryPath);
            Directory.Move(temporaryPath, targetPath);
        }
        else
        {
            File.Move(sourcePath, temporaryPath);
            File.Move(temporaryPath, targetPath);
        }
    }

    private TargetFolder? ResolveTargetDirectory(string? relativeFolder)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return null;
        }

        var normalized = NormalizeRelativePath(relativeFolder);
        if (normalized is null)
        {
            Fail(_text.Get("Error_InvalidFolderPath"));
            return null;
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(path))
        {
            Fail(_text.Get("Error_FolderNotFoundMoved"));
            return null;
        }

        return new TargetFolder(normalized, path);
    }

    /// <summary>Resolves a target folder and creates it when missing, for writes that name their folder.</summary>
    private TargetFolder? ResolveWritableDirectory(string? relativeFolder)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return null;
        }

        var normalized = NormalizeRelativePath(relativeFolder);
        if (normalized is null)
        {
            Fail(_text.Get("Error_InvalidFolderPath"));
            return null;
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Fail(_text.Format("Error_CreateFolder", ex.Message));
            return null;
        }

        return new TargetFolder(normalized, path);
    }

    /// <summary>Full path of a folder inside the active basket without touching the file system.</summary>
    private string? ResolveFolderPath(string? relativeFolder)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return null;
        }

        var normalized = NormalizeRelativePath(relativeFolder);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
    }

    private sealed record TargetFolder(string Folder, string Path);

    /// <summary>
    /// Turns a user/state supplied folder into a relative path inside the basket. Returns null
    /// for rooted paths, "..", and other values that could escape the basket root.
    /// </summary>
    private static string? NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(normalized))
        {
            return null;
        }

        var parts = normalized.Split(Path.DirectorySeparatorChar);
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
        {
            return null;
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static string RelativeTo(BasketTab tab, string fullPath)
        => Path.GetRelativePath(tab.DirectoryPath, fullPath);

    private static string? NormalizeDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length > MaxTabNameLength
            ? trimmed[..MaxTabNameLength].TrimEnd()
            : trimmed;
    }
}
