using System.Text.RegularExpressions;

namespace PasteHatch.Core;

/// <summary>
/// The application logic for baskets, folders, materials and tab state. It owns the invariants
/// that the UI and the requirements depend on: at most 10 open baskets, always an active basket,
/// files on disk as the source of truth, and everything that leaves the app going through the
/// Windows Recycle Bin only after a confirmation.
/// </summary>
public sealed class BasketWorkspace
{
    public const int MaxTabs = 10;
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

    private static readonly EnumerationOptions CopyEntriesOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        // A copied folder keeps its hidden files; only reparse points are skipped.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly string _basketsRoot;
    private readonly ITabStateStore _stateStore;
    private readonly IClipboardReader _clipboard;
    private readonly IShellService _shell;
    private readonly IConfirmationService _confirmations;
    private readonly IClock _clock;
    private readonly TabNameFormatter _formatter;

    private readonly List<BasketTab> _tabs = [];
    private readonly Dictionary<string, string> _selectedFolders = new(StringComparer.Ordinal);
    private List<BasketEntry> _entries = [];
    private List<Material> _activeMaterials = [];
    private string? _activeUid;
    private WindowPlacementState? _windowPlacement;

    public BasketWorkspace(
        string basketsRoot,
        ITabStateStore stateStore,
        IClipboardReader clipboard,
        IShellService shell,
        IConfirmationService confirmations,
        IClock? clock = null,
        TabNameFormatter? formatter = null)
    {
        _basketsRoot = basketsRoot;
        _stateStore = stateStore;
        _clipboard = clipboard;
        _shell = shell;
        _confirmations = confirmations;
        _clock = clock ?? SystemClock.Instance;
        _formatter = formatter ?? TabNameFormatter.Default;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<BasketTab> Tabs => _tabs;

    public BasketTab? ActiveTab => _tabs.Find(tab => tab.Uid == _activeUid) ?? _tabs.FirstOrDefault();

    /// <summary>Items of the folder currently open, folders first and then files.</summary>
    public IReadOnlyList<BasketEntry> Entries => _entries;

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

    /// <summary>
    /// Remembers the folder the user is looking at without re-reading or raising Changed. The
    /// tree calls this on every selection; the state file is written on the next real change.
    /// </summary>
    public void RememberFolder(string? relativeFolder)
    {
        var tab = ActiveTab;
        var normalized = NormalizeRelativePath(relativeFolder);
        if (tab is null || normalized is null)
        {
            return;
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(path))
        {
            return;
        }

        SelectedFolder = normalized;
        _selectedFolders[tab.Uid] = normalized;
    }

    /// <summary>Writes the current tab state immediately (used when hiding to the tray or exiting).</summary>
    public void SaveState() => SaveStateQuietly();


    /// <summary>Files of the folder currently open, kept for callers that only deal with files.</summary>
    public IReadOnlyList<Material> Materials => _activeMaterials;

    /// <summary>Folder currently open, relative to the basket root; an empty string is the root.</summary>
    public string SelectedFolder { get; private set; } = string.Empty;

    public string SelectedFolderPath
    {
        get
        {
            var tab = ActiveTab;
            if (tab is null)
            {
                return string.Empty;
            }

            return SelectedFolder.Length == 0
                ? tab.DirectoryPath
                : Path.Combine(tab.DirectoryPath, SelectedFolder);
        }
    }

    public int MaxTabCount => MaxTabs;

    public bool CanCreateTab => _tabs.Count < MaxTabs;

    public bool CanCloseTab => _tabs.Count > 1;

    public bool CanGoUp => SelectedFolder.Length > 0;

    /// <summary>Last user-facing message (copy result, rejected paste, save failure).</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>True when <see cref="StatusMessage"/> describes an error that should stay visible.</summary>
    public bool StatusIsError { get; private set; }

    /// <summary>Restores open baskets from the state file and from the folders still on disk.</summary>
    public void Initialize()
    {
        Directory.CreateDirectory(_basketsRoot);

        var state = _stateStore.Load();
        _windowPlacement = IsUsablePlacement(state.Window) ? state.Window : null;

        _tabs.Clear();
        LoadTabs(state);

        if (_tabs.Count == 0)
        {
            CreateBasketInternal();
        }

        _activeUid = _tabs.Any(tab => tab.Uid == state.ActiveUid) ? state.ActiveUid : _tabs[0].Uid;
        SelectedFolder = _selectedFolders.TryGetValue(_activeUid!, out var folder) ? folder : string.Empty;
        RefreshEntries();
        SaveStateQuietly();
        RaiseChanged();
    }

    public BasketTab? CreateTab()
    {
        if (_tabs.Count >= MaxTabs)
        {
            SetStatus("Достигнут предел в 10 корзин.");
            RaiseChanged();
            return null;
        }

        BasketTab tab;
        try
        {
            tab = CreateBasketInternal();
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось создать корзину: {ex.Message}");
            return null;
        }

        _activeUid = tab.Uid;
        SelectedFolder = string.Empty;
        _selectedFolders[tab.Uid] = string.Empty;
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
        SelectedFolder = _selectedFolders.TryGetValue(uid, out var folder) ? folder : string.Empty;
        Commit();
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
        Commit();
        return true;
    }

    /// <summary>Confirms, recycles the basket folder, then removes the tab.</summary>
    public bool CloseTab(string uid)
    {
        if (_tabs.Count <= 1)
        {
            SetStatus("Нельзя закрыть последнюю корзину.");
            RaiseChanged();
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
            ReportError($"Не удалось отправить корзину в Корзину Windows: {ex.Message}");
            return false;
        }

        _tabs.RemoveAt(index);
        _selectedFolders.Remove(uid);
        if (string.Equals(_activeUid, uid, StringComparison.Ordinal))
        {
            var next = _tabs[Math.Min(index, _tabs.Count - 1)];
            _activeUid = next.Uid;
            SelectedFolder = _selectedFolders.TryGetValue(next.Uid, out var folder) ? folder : string.Empty;
        }

        SetStatus($"Корзина «{tab.DisplayName}» отправлена в Корзину Windows.");
        Commit();
        return true;
    }

    /// <summary>
    /// Applies the tab order produced by dragging a tab in the TabView. Unknown UIDs are ignored and
    /// tabs missing from the list keep their relative order at the end, so a stale drag payload can
    /// never hide an open basket. The UI already shows the new order, so no Changed event is raised.
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
        RaiseChanged();
        return true;
    }

    // ---- Folder navigation -----------------------------------------------------------------

    /// <summary>
    /// Opens a folder of the active basket. The path is relative to the basket root; an unknown
    /// folder is rejected and the current folder stays open.
    /// </summary>
    public bool NavigateTo(string? relativeFolder)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return false;
        }

        var normalized = NormalizeRelativePath(relativeFolder);
        if (normalized is null)
        {
            SetStatus("Не удалось перейти в папку.");
            RaiseChanged();
            return false;
        }

        if (string.Equals(normalized, SelectedFolder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(path))
        {
            SetStatus("Папка не найдена — возможно, она была перемещена извне.");
            RaiseChanged();
            return false;
        }

        SelectedFolder = normalized;
        _selectedFolders[tab.Uid] = normalized;
        RefreshEntries();
        SaveStateQuietly();
        RaiseChanged();
        return true;
    }

    public bool GoUp()
    {
        if (!CanGoUp)
        {
            return false;
        }

        var parent = Path.GetDirectoryName(SelectedFolder);
        return NavigateTo(parent is null ? string.Empty : parent.Replace('\\', Path.DirectorySeparatorChar));
    }

    /// <summary>Subfolders of a folder, sorted like Explorer; used by the navigation tree.</summary>
    public IReadOnlyList<BasketEntry> GetSubfolders(string? relativeFolder)
    {
        var tab = ActiveTab;
        var normalized = NormalizeRelativePath(relativeFolder);
        if (tab is null || normalized is null)
        {
            return [];
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories("*", ChildEntriesOptions)
                .OrderBy(directory => directory.Name, NaturalStringComparer.Instance)
                .Select(directory => new BasketEntry(
                    directory.Name,
                    directory.FullName,
                    RelativeTo(tab, directory.FullName),
                    IsFolder: true,
                    MaterialKind.File)
                {
                    Modified = directory.LastWriteTime,
                })
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public bool HasSubfolders(string? relativeFolder) => GetSubfolders(relativeFolder).Count > 0;

    // ---- Clipboard and adding materials ----------------------------------------------------

    /// <summary>Reads the clipboard once, reporting an error instead of throwing.</summary>
    public PastePayload ReadClipboard()
    {
        return TryReadClipboard(out var payload) ? payload : PastePayload.None;
    }

    /// <summary>Reads the clipboard and tells the caller whether reading succeeded.</summary>
    public bool TryReadClipboard(out PastePayload payload)
    {
        try
        {
            payload = _clipboard.Read();
            return true;
        }
        catch (Exception ex)
        {
            payload = PastePayload.None;
            ReportError($"Не удалось прочитать буфер обмена: {ex.Message}");
            return false;
        }
    }

    /// <summary>Handles a single Ctrl+V; returns how many materials were added.</summary>
    public int AddFromClipboard() => AddFromClipboard(SelectedFolder);

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
            ReportError($"Не удалось прочитать буфер обмена: {ex.Message}");
            return 0;
        }

        return AddPayload(payload, targetRelativeFolder);
    }

    /// <summary>Adds an already read clipboard payload to a folder of the active basket.</summary>
    public int AddPayload(PastePayload payload, string? targetRelativeFolder)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var targetDirectory = ResolveTargetDirectory(targetRelativeFolder);
        if (ActiveTab is null || targetDirectory is null)
        {
            return 0;
        }

        switch (payload.Kind)
        {
            case PasteContentKind.Files:
                return AddFilesCore(payload.Files, targetDirectory.Folder, targetDirectory.Path, skipFilesAlreadyInBasket: true);

            case PasteContentKind.Image:
                return AddImage(payload.ImagePng, targetDirectory.Path);

            case PasteContentKind.Text:
                return AddText(payload.Text, targetDirectory.Path);

            default:
                SetStatus("В буфере обмена нет поддерживаемых данных.");
                RaiseChanged();
                return 0;
        }
    }

    /// <summary>Handles files dropped onto the window; returns how many were added.</summary>
    public int AddFiles(IReadOnlyList<string> paths) => AddFiles(paths, SelectedFolder);

    /// <summary>Handles files or folders dropped onto a concrete folder of the active basket.</summary>
    public int AddFiles(IReadOnlyList<string> paths, string? targetRelativeFolder)
    {
        var targetDirectory = ResolveTargetDirectory(targetRelativeFolder);
        if (targetDirectory is null)
        {
            return 0;
        }

        return AddFilesCore(paths, targetDirectory.Folder, targetDirectory.Path, skipFilesAlreadyInBasket: true);
    }

    /// <summary>
    /// Copies or moves already resolved paths into a folder. Used by internal Ctrl+C / Ctrl+X,
    /// where copying a file that is already inside the basket is legitimate.
    /// </summary>
    public int PasteFiles(IReadOnlyList<string> paths, string? targetRelativeFolder, bool move)
    {
        if (paths.Count == 0)
        {
            SetStatus("Нет файлов для добавления.");
            RaiseChanged();
            return 0;
        }

        var targetDirectory = ResolveTargetDirectory(targetRelativeFolder);
        if (targetDirectory is null)
        {
            return 0;
        }

        return move
            ? MoveFilesCore(paths, targetDirectory.Folder, targetDirectory.Path)
            : CopyFilesCore(paths, targetDirectory.Folder, targetDirectory.Path);
    }

    /// <summary>Saves the quick-note text as a .txt file named after the current day and time.</summary>
    public Material? AddQuickNote(string? text)
    {
        var tab = ActiveTab;
        if (tab is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var directory = SelectedFolderPath;

        try
        {
            Directory.CreateDirectory(directory);
            var fileName = MaterialNaming.DefaultFileName(
                directory,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt");
            var fullPath = Path.Combine(directory, fileName);
            File.WriteAllText(fullPath, text, MaterialNaming.Utf8NoBom);
            Commit();
            return _activeMaterials.Find(material => string.Equals(material.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось сохранить заметку: {ex.Message}");
            return null;
        }
    }

    // ---- Folder operations -----------------------------------------------------------------

    /// <summary>Creates a subfolder in the current folder; returns it when creation succeeded.</summary>
    public BasketEntry? CreateFolder(string? name, bool ensureUnique = false)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return null;
        }

        var cleaned = MaterialNaming.ValidateFolderName(name, out var error);
        if (cleaned is null)
        {
            ReportError(error ?? "Недопустимое имя папки.");
            return null;
        }

        var parent = SelectedFolderPath;
        try
        {
            Directory.CreateDirectory(parent);
            var freeName = ensureUnique ? UniqueDirectoryName(parent, cleaned) : cleaned;
            var fullPath = Path.Combine(parent, freeName);
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
            {
                ReportError($"Папка «{freeName}» уже существует.");
                return null;
            }

            Directory.CreateDirectory(fullPath);
            SetStatus($"Папка «{freeName}» создана.");
            Commit();
            return _entries.FirstOrDefault(entry =>
                entry.IsFolder &&
                string.Equals(entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось создать папку: {ex.Message}");
            return null;
        }
    }

    /// <summary>Renames a subfolder without touching its contents.</summary>
    public bool RenameFolder(BasketEntry folder, string? newBaseName)
    {
        if (!folder.IsFolder || folder.RelativePath.Length == 0)
        {
            return false;
        }

        var cleaned = MaterialNaming.ValidateFolderName(newBaseName, out var error);
        if (cleaned is null)
        {
            ReportError(error ?? "Недопустимое имя папки.");
            return false;
        }

        var sourcePath = folder.FullPath;
        var parent = Path.GetDirectoryName(sourcePath);
        if (parent is null || !Directory.Exists(sourcePath))
        {
            ReportError($"Папка «{folder.Name}» не найдена — возможно, она была перемещена извне.");
            RefreshAndNotify();
            return false;
        }

        var targetPath = Path.Combine(parent, cleaned);
        if (string.Equals(cleaned, folder.Name, StringComparison.Ordinal))
        {
            return true;
        }

        var caseOnlyRename = string.Equals(cleaned, folder.Name, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && (Directory.Exists(targetPath) || File.Exists(targetPath)))
        {
            ReportError($"Папка «{cleaned}» уже существует.");
            return false;
        }

        try
        {
            if (caseOnlyRename)
            {
                var temporaryPath = Path.Combine(parent, $"~{Guid.NewGuid():N}.tmp");
                Directory.Move(sourcePath, temporaryPath);
                Directory.Move(temporaryPath, targetPath);
            }
            else
            {
                Directory.Move(sourcePath, targetPath);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось переименовать папку: {ex.Message}");
            return false;
        }

        SetStatus($"Папка «{folder.Name}» переименована в «{cleaned}».");
        Commit();
        return true;
    }

    /// <summary>Confirms, recycles a subfolder with everything inside it, then refreshes the list.</summary>
    public bool DeleteFolder(BasketEntry folder)
    {
        if (!folder.IsFolder || folder.RelativePath.Length == 0 || ActiveTab is null)
        {
            return false;
        }

        if (!Directory.Exists(folder.FullPath))
        {
            ReportError($"Папка «{folder.Name}» не найдена.");
            RefreshAndNotify();
            return false;
        }

        if (!_confirmations.ConfirmRecycleFolder(folder.Name))
        {
            return false;
        }

        try
        {
            _shell.RecycleDirectory(folder.FullPath);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось отправить папку в Корзину Windows: {ex.Message}");
            return false;
        }

        SetStatus($"Папка «{folder.Name}» отправлена в Корзину Windows.");
        Commit();
        return true;
    }

    // ---- File operations -------------------------------------------------------------------

    /// <summary>Renames a material while keeping its extension; invalid names are rejected.</summary>
    public bool RenameMaterial(Material material, string? newBaseName)
    {
        if (ActiveTab is null)
        {
            return false;
        }

        var extension = Path.GetExtension(material.FileName);
        var cleaned = MaterialNaming.ValidateRenameBase(newBaseName, extension, out var error);
        if (cleaned is null)
        {
            ReportError(error ?? "Недопустимое имя файла.");
            return false;
        }

        var targetName = cleaned + extension;
        if (string.Equals(targetName, material.FileName, StringComparison.Ordinal))
        {
            return true;
        }

        var sourcePath = material.FullPath;
        var parent = Path.GetDirectoryName(sourcePath);
        if (parent is null || !File.Exists(sourcePath))
        {
            ReportError($"Файл «{material.FileName}» не найден — возможно, он был перемещён извне.");
            RefreshAndNotify();
            return false;
        }

        var targetPath = Path.Combine(parent, targetName);
        var caseOnlyRename = string.Equals(targetName, material.FileName, StringComparison.OrdinalIgnoreCase);

        // File.Exists is case-insensitive on Windows, so the "already exists" check must not
        // fire for the same file renamed to a different case.
        if (!caseOnlyRename && (File.Exists(targetPath) || Directory.Exists(targetPath)))
        {
            ReportError($"Файл или папка «{targetName}» уже существует в этой корзине.");
            return false;
        }

        try
        {
            if (caseOnlyRename)
            {
                // Case-only rename: go through a temporary name, Windows would reject the direct move.
                var temporaryPath = Path.Combine(parent, $"~{Guid.NewGuid():N}.tmp");
                File.Move(sourcePath, temporaryPath);
                File.Move(temporaryPath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось переименовать файл: {ex.Message}");
            return false;
        }

        SetStatus($"Файл «{material.FileName}» переименован в «{targetName}».");
        Commit();
        return true;
    }

    /// <summary>Confirms, recycles one material file, then removes it from the list.</summary>
    public bool DeleteMaterial(Material material)
    {
        if (ActiveTab is null)
        {
            return false;
        }

        if (!_confirmations.ConfirmRecycleMaterial(material.FileName))
        {
            return false;
        }

        try
        {
            if (File.Exists(material.FullPath))
            {
                _shell.RecycleFile(material.FullPath);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось отправить файл в Корзину Windows: {ex.Message}");
            return false;
        }

        SetStatus($"Файл «{material.FileName}» отправлен в Корзину Windows.");
        Commit();
        return true;
    }

    public void OpenMaterial(Material material)
    {
        if (!File.Exists(material.FullPath))
        {
            ReportError($"Файл «{material.FileName}» не найден.");
            RefreshAndNotify();
            return;
        }

        try
        {
            _shell.OpenItem(material.FullPath);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось открыть файл: {ex.Message}");
        }
    }

    /// <summary>Opens a file or folder with its default Windows application.</summary>
    public void OpenEntry(BasketEntry entry)
    {
        if (entry.IsFolder)
        {
            OpenFolderInExplorer(entry);
            return;
        }

        OpenMaterial(ToMaterial(entry));
    }

    public void OpenFolderInExplorer(BasketEntry folder)
    {
        if (!Directory.Exists(folder.FullPath))
        {
            ReportError($"Папка «{folder.Name}» не найдена.");
            RefreshAndNotify();
            return;
        }

        try
        {
            _shell.OpenFolder(folder.FullPath);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось открыть папку: {ex.Message}");
        }
    }

    public void OpenActiveBasketFolder()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return;
        }

        try
        {
            var directory = SelectedFolderPath;
            Directory.CreateDirectory(directory);
            _shell.OpenFolder(directory);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось открыть папку корзины: {ex.Message}");
        }
    }

    public void CopyActiveBasketPath()
    {
        if (ActiveTab is null)
        {
            return;
        }

        try
        {
            _shell.CopyText(SelectedFolderPath);
            SetStatus("Путь к папке скопирован в буфер обмена.");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось скопировать путь: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies the full path of one material so the user can hand a concrete file to a local agent.
    /// The path is copied as plain text, without quotes, exactly like the basket path.
    /// </summary>
    public void CopyMaterialPath(Material material)
    {
        try
        {
            _shell.CopyText(material.FullPath);
            SetStatus("Путь к файлу скопирован в буфер обмена.");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось скопировать путь: {ex.Message}");
        }
    }

    /// <summary>Copies the full path of a file or folder.</summary>
    public void CopyEntryPath(BasketEntry entry)
    {
        try
        {
            _shell.CopyText(entry.FullPath);
            SetStatus(entry.IsFolder
                ? "Путь к папке скопирован в буфер обмена."
                : "Путь к файлу скопирован в буфер обмена.");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось скопировать путь: {ex.Message}");
        }
    }

    // ---- Moving entries --------------------------------------------------------------------

    /// <summary>
    /// Moves a file or folder into another folder of the same basket. Collisions get the Explorer
    /// suffix instead of overwriting anything.
    /// </summary>
    public bool MoveEntry(BasketEntry entry, string? targetRelativeFolder)
    {
        if (entry.RelativePath.Length == 0)
        {
            return false;
        }

        var target = ResolveTargetDirectory(targetRelativeFolder);
        if (target is null)
        {
            return false;
        }

        var sourceParent = Path.GetDirectoryName(entry.FullPath);
        if (sourceParent is null)
        {
            return false;
        }

        if (string.Equals(Path.GetFullPath(sourceParent), Path.GetFullPath(target.Path), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.IsFolder && IsSameOrInside(target.Path, entry.FullPath))
        {
            ReportError("Нельзя переместить папку внутрь себя.");
            return false;
        }

        try
        {
            if (entry.IsFolder)
            {
                if (!Directory.Exists(entry.FullPath))
                {
                    ReportError($"Папка «{entry.Name}» не найдена.");
                    RefreshAndNotify();
                    return false;
                }

                var destination = Path.Combine(target.Path, UniqueDirectoryName(target.Path, entry.Name));
                Directory.Move(entry.FullPath, destination);
            }
            else
            {
                if (!File.Exists(entry.FullPath))
                {
                    ReportError($"Файл «{entry.Name}» не найден.");
                    RefreshAndNotify();
                    return false;
                }

                var destination = Path.Combine(target.Path, MaterialNaming.UniqueFileName(target.Path, entry.Name));
                File.Move(entry.FullPath, destination);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось переместить «{entry.Name}»: {ex.Message}");
            return false;
        }

        SetStatus($"«{entry.Name}» {(entry.IsFolder ? "перемещена" : "перемещён")} в «{DisplayFolderName(target.Folder)}».");
        Commit();
        return true;
    }

    /// <summary>Moves several already resolved paths (a cut) into a folder.</summary>
    public int MoveFiles(IReadOnlyList<string> paths, string? targetRelativeFolder)
    {
        if (paths.Count == 0)
        {
            SetStatus("Нет файлов для перемещения.");
            RaiseChanged();
            return 0;
        }

        var target = ResolveTargetDirectory(targetRelativeFolder);
        if (target is null)
        {
            return 0;
        }

        return MoveFilesCore(paths, target.Folder, target.Path);
    }

    // ---- Refresh and state -----------------------------------------------------------------

    /// <summary>Re-reads the active folder, e.g. after an external agent changed it.</summary>
    public void Refresh()
    {
        RefreshEntries();
        RaiseChanged();
    }

    /// <summary>Publishes an informational status message (clipboard operations from the UI).</summary>
    public void ReportStatus(string message)
    {
        SetStatus(message);
        RaiseChanged();
    }

    public void SaveWindowPlacement(WindowPlacementState? placement)
    {
        if (!IsUsablePlacement(placement))
        {
            return;
        }

        _windowPlacement = placement;
        SaveStateQuietly();
    }

    public WindowPlacementState? WindowPlacement => _windowPlacement;

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
            _selectedFolders[saved.Uid] = NormalizeRelativePath(saved.SelectedFolder) ?? string.Empty;
        }

        // Folders that exist on disk but not in the state file (lost state, crash after create)
        // are still user baskets, so they are adopted instead of being left unreachable.
        var orphanDirectories = Directory.EnumerateDirectories(_basketsRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(info => UidPattern.IsMatch(info.Name))
            .Where(info => _tabs.All(tab => !string.Equals(tab.Uid, info.Name, StringComparison.Ordinal)))
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
                DefaultNameFor(directory.FullName),
                directory.FullName));
            _selectedFolders[directory.Name] = string.Empty;
        }
    }

    private BasketTab CreateBasketInternal()
    {
        var uid = GenerateUid();
        var directoryPath = Path.Combine(_basketsRoot, uid);
        Directory.CreateDirectory(directoryPath);
        var tab = new BasketTab(uid, _formatter.Format(_clock.Now), directoryPath);
        _tabs.Add(tab);
        _selectedFolders[uid] = string.Empty;
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

        throw new InvalidOperationException("Не удалось подобрать свободный UID для корзины.");
    }

    private int AddFilesCore(
        IReadOnlyList<string> paths,
        string targetFolder,
        string targetDirectory,
        bool skipFilesAlreadyInBasket)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return 0;
        }

        if (paths.Count == 0)
        {
            SetStatus("Нет файлов для добавления.");
            RaiseChanged();
            return 0;
        }

        var addedFiles = 0;
        var addedFolders = 0;
        var skipped = 0;
        var problems = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        problems.Add($"«{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» — ссылка на папку; такие папки не копируются.");
                        continue;
                    }

                    if (IsSameOrInside(path, tab.DirectoryPath))
                    {
                        skipped++;
                        continue;
                    }

                    if (IsSameOrInside(targetDirectory, path))
                    {
                        problems.Add($"Нельзя скопировать папку «{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» внутрь себя.");
                        continue;
                    }

                    var sourceName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    if (sourceName.Length == 0)
                    {
                        problems.Add("Нельзя копировать корень диска.");
                        continue;
                    }

                    var folderName = UniqueDirectoryName(targetDirectory, sourceName);
                    CopyDirectoryRecursive(path, Path.Combine(targetDirectory, folderName));
                    addedFolders++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    problems.Add($"Файл не найден: {path}");
                    continue;
                }

                if (skipFilesAlreadyInBasket && IsSameOrInside(path, tab.DirectoryPath))
                {
                    skipped++;
                    continue;
                }

                var destinationName = MaterialNaming.UniqueFileName(targetDirectory, Path.GetFileName(path));
                var destinationPath = Path.Combine(targetDirectory, destinationName);
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                File.Copy(path, destinationPath, overwrite: false);
                addedFiles++;
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var added = addedFiles + addedFolders;
        var status = addedFolders > 0
            ? added == 0
                ? "Элементы не добавлены."
                : $"Добавлено {RussianPlural.Items(added)}."
            : (added, skipped) switch
            {
                (0, > 0) => "Файлы уже находятся в этой корзине.",
                (0, _) => "Материалы не добавлены.",
                (1, 0) => "Добавлен 1 файл.",
                (1, > 0) => "Добавлен 1 файл (часть файлов уже была в корзине).",
                (_, 0) => $"Добавлено {added} {RussianPlural.Noun(added, "файл", "файла", "файлов")}.",
                _ => $"Добавлено {added} {RussianPlural.Noun(added, "файл", "файла", "файлов")} (часть файлов уже была в корзине).",
            };

        SetStatus(status);
        Commit();

        if (problems.Count > 0)
        {
            ReportError(string.Join(Environment.NewLine, problems.Take(6)));
        }

        return added;
    }

    private int CopyFilesCore(IReadOnlyList<string> paths, string targetFolder, string targetDirectory)
    {
        var added = 0;
        var problems = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        problems.Add($"«{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» — ссылка на папку; такие папки не копируются.");
                        continue;
                    }

                    if (IsSameOrInside(targetDirectory, path))
                    {
                        problems.Add($"Нельзя скопировать папку «{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» внутрь себя.");
                        continue;
                    }

                    var sourceName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    if (sourceName.Length == 0)
                    {
                        problems.Add("Нельзя копировать корень диска.");
                        continue;
                    }

                    var folderName = UniqueDirectoryName(targetDirectory, sourceName);
                    CopyDirectoryRecursive(path, Path.Combine(targetDirectory, folderName));
                    added++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    problems.Add($"Файл не найден: {path}");
                    continue;
                }

                var destinationName = MaterialNaming.UniqueFileName(targetDirectory, Path.GetFileName(path));
                var destinationPath = Path.Combine(targetDirectory, destinationName);
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(path, destinationPath, overwrite: false);
                added++;
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        SetStatus(added == 0
            ? "Элементы не скопированы."
            : $"Скопировано {RussianPlural.Items(added)} в «{DisplayFolderName(targetFolder)}».");
        Commit();

        if (problems.Count > 0)
        {
            ReportError(string.Join(Environment.NewLine, problems.Take(6)));
        }

        return added;
    }

    private int MoveFilesCore(IReadOnlyList<string> paths, string targetFolder, string targetDirectory)
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return 0;
        }

        var moved = 0;
        var problems = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var sourceParent = Path.GetDirectoryName(path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                if (sourceParent is not null &&
                    string.Equals(
                        Path.GetFullPath(sourceParent),
                        Path.GetFullPath(targetDirectory),
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Dropping an item onto the folder it already lives in is a no-op, not a copy.
                    continue;
                }

                if (Directory.Exists(path))
                {
                    if (IsSameOrInside(targetDirectory, path))
                    {
                        problems.Add($"Нельзя переместить папку «{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» внутрь себя.");
                        continue;
                    }

                    var sourceName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    if (sourceName.Length == 0)
                    {
                        problems.Add("Нельзя копировать корень диска.");
                        continue;
                    }

                    var folderName = UniqueDirectoryName(targetDirectory, sourceName);
                    var destination = Path.Combine(targetDirectory, folderName);
                    if (IsSameOrInside(path, tab.DirectoryPath))
                    {
                        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        Directory.Move(path, destination);
                    }
                    else
                    {
                        CopyDirectoryRecursive(path, destination);
                        _shell.RecycleDirectory(path);
                    }

                    moved++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    problems.Add($"Файл не найден: {path}");
                    continue;
                }

                var destinationName = MaterialNaming.UniqueFileName(targetDirectory, Path.GetFileName(path));
                var destinationFile = Path.Combine(targetDirectory, destinationName);
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destinationFile), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // File.Move supports cross-volume moves on modern .NET, so this works both for
                // paths inside the basket and for a cut pasted from Explorer.
                File.Move(path, destinationFile);

                moved++;
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        SetStatus(moved == 0
            ? "Элементы не перемещены."
            : $"Перемещено {RussianPlural.Items(moved)} в «{DisplayFolderName(targetFolder)}».");
        Commit();

        if (problems.Count > 0)
        {
            ReportError(string.Join(Environment.NewLine, problems.Take(6)));
        }

        return moved;
    }

    private int AddImage(byte[]? pngBytes, string targetDirectory)
    {
        if (pngBytes is not { Length: > 0 })
        {
            SetStatus("В буфере обмена нет изображения.");
            RaiseChanged();
            return 0;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            var fileName = MaterialNaming.DefaultFileName(
                targetDirectory,
                _formatter.FormatAsFileName(_clock.Now),
                ".png");
            File.WriteAllBytes(Path.Combine(targetDirectory, fileName), pngBytes);
            SetStatus($"Добавлен скриншот «{fileName}».");
            Commit();
            return 1;
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось сохранить изображение: {ex.Message}");
            return 0;
        }
    }

    private int AddText(string? text, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("В буфере обмена нет текста.");
            RaiseChanged();
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
                    ".url");
                File.WriteAllText(
                    Path.Combine(targetDirectory, urlFileName),
                    MaterialNaming.CreateInternetShortcutContent(link),
                    MaterialNaming.Utf8NoBom);
                SetStatus($"Добавлена ссылка «{link.Host}».");
                Commit();
                return 1;
            }

            var textFileName = MaterialNaming.DefaultFileName(
                targetDirectory,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt");
            File.WriteAllText(
                Path.Combine(targetDirectory, textFileName),
                text,
                MaterialNaming.Utf8NoBom);
            SetStatus("Текст добавлен.");
            Commit();
            return 1;
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось сохранить текст: {ex.Message}");
            return 0;
        }
    }

    private void RefreshEntries()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            _entries = [];
            _activeMaterials = [];
            return;
        }

        var directory = SelectedFolderPath;
        if (SelectedFolder.Length > 0 && !Directory.Exists(directory))
        {
            // The folder was removed outside the app; do not leave the basket pointing at nothing.
            SelectedFolder = string.Empty;
            _selectedFolders[tab.Uid] = string.Empty;
        }

        try
        {
            var entries = GetEntries(SelectedFolder);
            _entries = entries.ToList();
            _activeMaterials = entries
                .Where(entry => !entry.IsFolder)
                .Select(ToMaterial)
                .ToList();
        }
        catch (Exception)
        {
            // A basket or folder deleted outside the app must not break the UI; the next
            // create/refresh repairs it by reading the parent again.
            _entries = [];
            _activeMaterials = [];
        }
    }

    private void RefreshAndNotify()
    {
        RefreshEntries();
        RaiseChanged();
    }

    private void Commit()
    {
        RefreshEntries();
        SaveStateQuietly();
        RaiseChanged();
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        StatusIsError = false;
    }

    private void ReportError(string message)
    {
        StatusMessage = message;
        StatusIsError = true;
        try
        {
            _confirmations.ShowError(message);
        }
        catch (Exception)
        {
            // Reporting an error must never take the application down.
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

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
                    SelectedFolder = _selectedFolders.TryGetValue(tab.Uid, out var folder) ? folder : string.Empty,
                }).ToList(),
                Window = _windowPlacement,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось сохранить состояние вкладок: {ex.Message}";
            StatusIsError = true;
        }
    }

    private static Material ToMaterial(BasketEntry entry) => new(entry.Name, entry.FullPath, entry.Kind)
    {
        RelativePath = entry.RelativePath,
        Size = entry.Size,
        Modified = entry.Modified,
    };

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
            ReportError("Недопустимый путь папки.");
            return null;
        }

        var path = normalized.Length == 0 ? tab.DirectoryPath : Path.Combine(tab.DirectoryPath, normalized);
        if (!Directory.Exists(path))
        {
            ReportError("Папка не найдена — возможно, она была перемещена извне.");
            return null;
        }

        return new TargetFolder(normalized, path);
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

    private static string DisplayFolderName(string relativeFolder)
        => relativeFolder.Length == 0 ? "корень корзины" : relativeFolder;

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

    private static bool IsSameOrInside(string path, string directoryPath)
    {
        try
        {
            var root = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string UniqueDirectoryName(string directory, string desiredName)
    {
        var candidate = desiredName;
        var counter = 2;

        while (Directory.Exists(Path.Combine(directory, candidate)) ||
               File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{desiredName} ({counter})";
            counter++;

            if (counter > 9999)
            {
                throw new IOException($"Не удалось подобрать свободное имя папки для «{desiredName}».");
            }
        }

        return candidate;
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", CopyEntriesOptions))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", CopyEntriesOptions))
        {
            CopyDirectoryRecursive(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static bool IsUsablePlacement(WindowPlacementState? placement)
        => placement is { Width: >= 400, Height: >= 300 };
}
