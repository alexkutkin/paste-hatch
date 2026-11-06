namespace PasteHatch.Core;

internal static class DiskRename
{
    public static void Move(string source, string target, bool isFolder, string? temporaryPath = null)
    {
        if (string.Equals(source, target, StringComparison.Ordinal)) return;
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            MoveEntry(source, target, isFolder);
            return;
        }

        // Windows needs an intermediate name for a case-only rename. Reuse a recorded path
        // when the caller must recover an interrupted move after a process restart.
        temporaryPath ??= Path.Combine(Path.GetDirectoryName(source)!, $"~{Guid.NewGuid():N}.tmp");
        MoveEntry(source, temporaryPath, isFolder);
        try
        {
            MoveEntry(temporaryPath, target, isFolder);
        }
        catch
        {
            MoveEntry(temporaryPath, source, isFolder);
            throw;
        }
    }

    private static void MoveEntry(string source, string target, bool isFolder)
    {
        if (isFolder) Directory.Move(source, target);
        else File.Move(source, target);
    }
}
