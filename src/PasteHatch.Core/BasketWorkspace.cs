using System.Text.RegularExpressions;

namespace PasteHatch.Core;

/// <summary>
/// The application logic for baskets, materials and tab state. It owns the invariants that the
/// UI and the requirements depend on: at most 10 open baskets, always an active basket, files
/// on disk as the source of truth, and everything that leaves the app going through the
/// Windows Recycle Bin only after a confirmation.
/// </summary>
public sealed class BasketWorkspace
{
    public const int MaxTabs = 10;
    public const int MaxTabNameLength = 120;

    private const string UidAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex UidPattern = new("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$", RegexOptions.CultureInvariant);

    private readonly string _basketsRoot;
    private readonly ITabStateStore _stateStore;
    private readonly IClipboardReader _clipboard;
    private readonly IShellService _shell;
    private readonly IConfirmationService _confirmations;
    private readonly IClock _clock;
    private readonly TabNameFormatter _formatter;

    private readonly List<BasketTab> _tabs = [];
    private readonly Dictionary<string, List<string>> _materialOrders = new(StringComparer.Ordinal);
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

    public IReadOnlyList<Material> Materials => _activeMaterials;

    public int MaxTabCount => MaxTabs;

    public bool CanCreateTab => _tabs.Count < MaxTabs;

    public bool CanCloseTab => _tabs.Count > 1;

    /// <summary>Last user-facing message (copy result, rejected paste, save failure).</summary>
    public string? StatusMessage { get; private set; }

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
        RefreshMaterials();
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
        _materialOrders.Remove(uid);
        if (string.Equals(_activeUid, uid, StringComparison.Ordinal))
        {
            _activeUid = _tabs[Math.Min(index, _tabs.Count - 1)].Uid;
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

    /// <summary>Handles a single Ctrl+V; returns how many materials were added.</summary>
    public int AddFromClipboard()
    {
        if (ActiveTab is null)
        {
            return 0;
        }

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

        switch (payload.Kind)
        {
            case PasteContentKind.Files:
                return AddFilesCore(payload.Files);

            case PasteContentKind.Image:
                return AddImage(payload.ImagePng);

            case PasteContentKind.Text:
                return AddText(payload.Text);

            default:
                SetStatus("В буфере обмена нет поддерживаемых данных.");
                RaiseChanged();
                return 0;
        }
    }

    /// <summary>Handles files dropped onto the window; returns how many were added.</summary>
    public int AddFiles(IReadOnlyList<string> paths) => AddFilesCore(paths);

    /// <summary>Saves the quick-note text as a .txt file named after the current day and time.</summary>
    public Material? AddQuickNote(string? text)
    {
        var tab = ActiveTab;
        if (tab is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var fileName = MaterialNaming.DefaultFileName(
                tab.DirectoryPath,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt");
            var fullPath = Path.Combine(tab.DirectoryPath, fileName);
            File.WriteAllText(fullPath, text, MaterialNaming.Utf8NoBom);
            Commit();
            return _activeMaterials.Find(material => material.FileName == fileName);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось сохранить заметку: {ex.Message}");
            return null;
        }
    }

    /// <summary>Renames a material while keeping its extension; invalid names are rejected.</summary>
    public bool RenameMaterial(Material material, string? newBaseName)
    {
        var tab = ActiveTab;
        if (tab is null)
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

        var sourcePath = Path.Combine(tab.DirectoryPath, material.FileName);
        var targetPath = Path.Combine(tab.DirectoryPath, targetName);
        var caseOnlyRename = string.Equals(targetName, material.FileName, StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(sourcePath))
        {
            ReportError($"Файл «{material.FileName}» не найден — возможно, он был перемещён извне.");
            RefreshAndNotify();
            return false;
        }

        // File.Exists is case-insensitive on Windows, so the "already exists" check must not
        // fire for the same file renamed to a different case.
        if (!caseOnlyRename && File.Exists(targetPath))
        {
            ReportError($"Файл «{targetName}» уже существует в этой корзине.");
            return false;
        }

        try
        {
            if (caseOnlyRename)
            {
                // Case-only rename: go through a temporary name, Windows would reject the direct move.
                var temporaryPath = Path.Combine(tab.DirectoryPath, $"~{Guid.NewGuid():N}.tmp");
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

        // Keep the user's order: the renamed file must stay in its slot, not move to the end.
        UpdateMaterialOrderAfterRename(material.FileName, targetName);
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
        RemoveMaterialFromOrder(material.FileName);
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

    public void OpenActiveBasketFolder()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(tab.DirectoryPath);
            _shell.OpenFolder(tab.DirectoryPath);
        }
        catch (Exception ex)
        {
            ReportError($"Не удалось открыть папку корзины: {ex.Message}");
        }
    }

    public void CopyActiveBasketPath()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            return;
        }

        try
        {
            _shell.CopyText(tab.DirectoryPath);
            SetStatus("Путь к папке корзины скопирован в буфер обмена.");
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

    /// <summary>
    /// Saves the user's material order for the active basket, e.g. after a drag reorder. Names that
    /// are not in the basket (or are duplicated) are ignored, missing files are appended in their
    /// current place, so the state file can never lose a material.
    /// </summary>
    public void SaveMaterialOrder(IReadOnlyList<string> orderedFileNames)
    {
        var tab = ActiveTab;
        if (tab is null || orderedFileNames is null)
        {
            return;
        }

        var known = _activeMaterials
            .Select(material => material.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>(_activeMaterials.Count);

        foreach (var name in orderedFileNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var fileName = Path.GetFileName(name);
            if (!known.Contains(fileName) ||
                order.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            order.Add(fileName);
        }

        foreach (var material in _activeMaterials)
        {
            if (!order.Contains(material.FileName, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(material.FileName);
            }
        }

        // Keep the in-memory list coherent with the saved order; the UI already shows this order
        // after a drag, so no Changed event is raised (that would rebuild the rows mid-drag).
        var byName = _activeMaterials.ToDictionary(
            material => material.FileName,
            StringComparer.OrdinalIgnoreCase);
        _activeMaterials = order.Select(name => byName[name]).ToList();
        _materialOrders[tab.Uid] = order;
        SaveStateQuietly();
    }

    /// <summary>
    /// Moves one material by <paramref name="offset"/> positions in the user's order and reports
    /// the change so the list refreshes; used by the keyboard alternative to drag and drop.
    /// </summary>
    public bool MoveMaterial(Material material, int offset)
    {
        var tab = ActiveTab;
        if (tab is null || offset == 0)
        {
            return false;
        }

        var index = _activeMaterials.FindIndex(candidate =>
            string.Equals(candidate.FileName, material.FileName, StringComparison.OrdinalIgnoreCase));
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _activeMaterials.Count)
        {
            return false;
        }

        var moved = _activeMaterials[index];
        _activeMaterials.RemoveAt(index);
        _activeMaterials.Insert(target, moved);
        _materialOrders[tab.Uid] = _activeMaterials.Select(item => item.FileName).ToList();
        SaveStateQuietly();
        RaiseChanged();
        return true;
    }

    /// <summary>Re-reads the active basket folder, e.g. after an external agent changed it.</summary>
    public void Refresh()
    {
        RefreshMaterials();
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
            _materialOrders[saved.Uid] = NormalizeMaterialOrder(saved.MaterialOrder);
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
        }
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

        throw new InvalidOperationException("Не удалось подобрать свободный UID для корзины.");
    }

    private int AddFilesCore(IReadOnlyList<string> paths)
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

        var added = 0;
        var skipped = 0;
        var problems = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    problems.Add($"«{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}» — папка; перетащите файлы.");
                    continue;
                }

                if (!File.Exists(path))
                {
                    problems.Add($"Файл не найден: {path}");
                    continue;
                }

                if (IsInsideDirectory(path, tab.DirectoryPath))
                {
                    skipped++;
                    continue;
                }

                var destinationName = MaterialNaming.UniqueFileName(tab.DirectoryPath, Path.GetFileName(path));
                File.Copy(path, Path.Combine(tab.DirectoryPath, destinationName), overwrite: false);
                added++;
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var status = (added, skipped) switch
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

    private int AddImage(byte[]? pngBytes)
    {
        var tab = ActiveTab;
        if (tab is null || pngBytes is not { Length: > 0 })
        {
            SetStatus("В буфере обмена нет изображения.");
            RaiseChanged();
            return 0;
        }

        try
        {
            var fileName = MaterialNaming.DefaultFileName(
                tab.DirectoryPath,
                _formatter.FormatAsFileName(_clock.Now),
                ".png");
            File.WriteAllBytes(Path.Combine(tab.DirectoryPath, fileName), pngBytes);
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

    private int AddText(string? text)
    {
        var tab = ActiveTab;
        if (tab is null || string.IsNullOrWhiteSpace(text))
        {
            SetStatus("В буфере обмена нет текста.");
            RaiseChanged();
            return 0;
        }

        try
        {
            if (MaterialNaming.TryGetLink(text, out var link) && link is not null)
            {
                var urlFileName = MaterialNaming.DefaultFileName(
                    tab.DirectoryPath,
                    _formatter.FormatAsFileName(_clock.Now),
                    ".url");
                File.WriteAllText(
                    Path.Combine(tab.DirectoryPath, urlFileName),
                    MaterialNaming.CreateInternetShortcutContent(link),
                    MaterialNaming.Utf8NoBom);
                SetStatus($"Добавлена ссылка «{link.Host}».");
                Commit();
                return 1;
            }

            var textFileName = MaterialNaming.DefaultFileName(
                tab.DirectoryPath,
                _formatter.FormatAsFileName(_clock.Now),
                ".txt");
            File.WriteAllText(
                Path.Combine(tab.DirectoryPath, textFileName),
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

    private void RefreshMaterials()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            _activeMaterials = [];
            return;
        }

        try
        {
            // Creation order is the fallback for files nobody has ordered yet (fresh basket,
            // files added externally); names present in the saved order keep the user's slots.
            var files = new DirectoryInfo(tab.DirectoryPath)
                .EnumerateFiles()
                .OrderBy(file => file.CreationTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new Material(file.Name, file.FullName, MaterialClassifier.Classify(file.Name)))
                .ToList();

            var byName = files.ToDictionary(material => material.FileName, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<Material>(files.Count);

            if (_materialOrders.TryGetValue(tab.Uid, out var savedOrder))
            {
                foreach (var fileName in savedOrder)
                {
                    if (byName.Remove(fileName, out var material))
                    {
                        ordered.Add(material);
                    }
                }
            }

            ordered.AddRange(files.Where(material => byName.ContainsKey(material.FileName)));
            _activeMaterials = ordered;
            _materialOrders[tab.Uid] = ordered.Select(material => material.FileName).ToList();
        }
        catch (Exception)
        {
            // A basket folder deleted outside the app must not break the UI; the tab is
            // repaired on the next create/refresh by the empty directory.
            _activeMaterials = [];
        }
    }

    private void RefreshAndNotify()
    {
        RefreshMaterials();
        RaiseChanged();
    }

    private void Commit()
    {
        RefreshMaterials();
        SaveStateQuietly();
        RaiseChanged();
    }

    private void SetStatus(string message) => StatusMessage = message;

    private static List<string> NormalizeMaterialOrder(IEnumerable<string>? names)
    {
        var order = new List<string>();
        if (names is null)
        {
            return order;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var fileName = Path.GetFileName(name);
            if (fileName.Length > 0 && seen.Add(fileName))
            {
                order.Add(fileName);
            }
        }

        return order;
    }

    private void UpdateMaterialOrderAfterRename(string oldFileName, string newFileName)
    {
        var tab = ActiveTab;
        if (tab is null || !_materialOrders.TryGetValue(tab.Uid, out var order))
        {
            return;
        }

        var index = order.FindIndex(name => string.Equals(name, oldFileName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            order[index] = newFileName;
        }
        else
        {
            order.Add(newFileName);
        }
    }

    private void RemoveMaterialFromOrder(string fileName)
    {
        var tab = ActiveTab;
        if (tab is not null && _materialOrders.TryGetValue(tab.Uid, out var order))
        {
            order.RemoveAll(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        }
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
                    MaterialOrder = _materialOrders.TryGetValue(tab.Uid, out var order)
                        ? order.ToList()
                        : [],
                }).ToList(),
                Window = _windowPlacement,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось сохранить состояние вкладок: {ex.Message}";
        }
    }

    private void ReportError(string message)
    {
        SetStatus(message);
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

    private static bool IsInsideDirectory(string filePath, string directoryPath)
    {
        try
        {
            var root = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(filePath);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsUsablePlacement(WindowPlacementState? placement)
        => placement is { Width: >= 400, Height: >= 300 };
}

