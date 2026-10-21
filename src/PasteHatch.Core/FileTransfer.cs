namespace PasteHatch.Core;

internal enum FileTransferIntent { Import, Copy, Move }

/// <summary>Owns transfer intent, collision rules, disk operations and partial results.</summary>
internal sealed class FileTransfer(IShellService shell)
{
    private static readonly EnumerationOptions CopyEntriesOptions = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };
    private string[]? _clipboardPaths;
    private FileTransferIntent _clipboardIntent;

    public void ForgetClipboard() => _clipboardPaths = null;

    public void RememberClipboard(IReadOnlyList<string> paths, bool move)
    {
        _clipboardPaths = paths.ToArray();
        _clipboardIntent = move ? FileTransferIntent.Move : FileTransferIntent.Copy;
    }

    public FileTransferIntent ClipboardIntent(IReadOnlyList<string> paths)
    {
        if (_clipboardPaths is not null && paths.Count == _clipboardPaths.Length &&
            new HashSet<string>(_clipboardPaths, StringComparer.OrdinalIgnoreCase).SetEquals(paths))
        {
            return _clipboardIntent;
        }
        _clipboardPaths = null;
        return FileTransferIntent.Import;
    }

    public TransferResult Execute(IReadOnlyList<string> paths, string basket, string target,
        string relativeTarget, FileTransferIntent intent)
    {
        var files = 0;
        var folders = 0;
        var skipped = 0;
        var problems = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var isFolder = Directory.Exists(path);
                if (!isFolder && !File.Exists(path))
                    throw new IOException($"Файл не найден: {path}");

                if (intent == FileTransferIntent.Move && SameDirectory(
                    Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), target))
                    continue;
                if (intent == FileTransferIntent.Import && IsSameOrInside(path, basket))
                {
                    skipped++;
                    continue;
                }
                if (isFolder && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Ссылки на папки не копируются и не перемещаются.");
                if (isFolder && IsSameOrInside(target, path))
                    throw new IOException("Нельзя скопировать или переместить папку внутрь себя.");

                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (name.Length == 0) throw new IOException("Нельзя переносить корень диска.");
                var destination = Path.Combine(target, isFolder
                    ? UniqueDirectoryName(target, name)
                    : MaterialNaming.UniqueFileName(target, name));
                if (isFolder)
                {
                    if (intent == FileTransferIntent.Move && IsSameOrInside(path, basket))
                        Directory.Move(path, destination);
                    else
                    {
                        CopyDirectory(path, destination);
                        if (intent == FileTransferIntent.Move) shell.RecycleDirectory(path);
                    }
                    folders++;
                }
                else
                {
                    if (intent == FileTransferIntent.Move) File.Move(path, destination);
                    else File.Copy(path, destination, overwrite: false);
                    files++;
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var completed = files + folders;
        var folder = relativeTarget.Length == 0 ? "корень корзины" : relativeTarget;
        var status = intent switch
        {
            FileTransferIntent.Move => completed == 0 ? "Элементы не перемещены." : completed == 1 ? $"1 элемент перемещён в «{folder}»." : $"Перемещено {RussianPlural.Items(completed)} в «{folder}».",
            FileTransferIntent.Copy => completed == 0 ? "Элементы не скопированы." : $"Скопировано {RussianPlural.Items(completed)} в «{folder}».",
            _ => folders > 0 ? $"Добавлено {RussianPlural.Items(completed)}." : (completed, skipped) switch
            {
                (0, > 0) => "Файлы уже находятся в этой корзине.",
                (0, _) => "Материалы не добавлены.",
                (1, 0) => "Добавлен 1 файл.",
                (1, _) => "Добавлен 1 файл (часть файлов уже была в корзине).",
                (_, 0) => $"Добавлено {files} {RussianPlural.Noun(files, "файл", "файла", "файлов")}.",
                _ => $"Добавлено {files} {RussianPlural.Noun(files, "файл", "файла", "файлов")} (часть файлов уже была в корзине).",
            },
        };
        if (paths.Count == 0) status = "Нет файлов для добавления.";
        return new TransferResult(completed, status, problems);
    }

    internal static bool SameDirectory(string? left, string right) => left is not null &&
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string path, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string UniqueDirectoryName(string directory, string desiredName)
    {
        var candidate = desiredName;
        for (var counter = 2; Directory.Exists(Path.Combine(directory, candidate)) || File.Exists(Path.Combine(directory, candidate)); counter++)
        {
            if (counter > 9999) throw new IOException($"Не удалось подобрать свободное имя папки для «{desiredName}».");
            candidate = $"{desiredName} ({counter})";
        }
        return candidate;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", CopyEntriesOptions))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", CopyEntriesOptions))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

internal sealed record TransferResult(int Completed, string Status, IReadOnlyList<string> Problems);
