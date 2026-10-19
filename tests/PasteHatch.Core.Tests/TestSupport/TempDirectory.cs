using System.Text;

namespace PasteHatch.Core.Tests;

/// <summary>
/// A unique directory under the system temp folder, recreated per test and removed on dispose.
/// All workspace state (baskets root, state file, source files for drag &amp; drop) lives below
/// <see cref="Root"/> so a test never touches the real <c>%LOCALAPPDATA%\PasteHatch</c> folder.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "PasteHatch.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SourceRoot);
    }

    public string Root { get; }

    public string BasketsRoot => Path.Combine(Root, "Baskets");

    public string StateFile => Path.Combine(Root, "state.json");

    /// <summary>Folder for files that are pasted / dropped into a basket.</summary>
    public string SourceRoot => Path.Combine(Root, "Sources");

    public string CreateSourceFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(SourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    public string CreateSourceDirectory(string relativePath)
    {
        var fullPath = Path.Combine(SourceRoot, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateBasketDirectory(string uid)
    {
        var fullPath = Path.Combine(BasketsRoot, uid);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(Root))
                {
                    return;
                }

                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException)
            {
                // A just-closed file may still be held briefly by the file system; retry.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: retry a moment later.
            }

            Thread.Sleep(50);
        }
    }
}
