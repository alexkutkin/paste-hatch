using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PasteHatch.Core;

namespace PasteHatch.Platform;

internal static class TrayPathLabel
{
    public static string Format(string path, string basketsRoot, IReadOnlyList<BasketTab> baskets)
    {
        var relative = Path.GetRelativePath(basketsRoot, path);
        var parts = relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(relative) || parts.Length == 0 || parts[0] is "." or "..")
        {
            return "…\\" + Shorten(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), 40);
        }

        var directory = Path.Combine(basketsRoot, parts[0]);
        var basketName = baskets.FirstOrDefault(basket => string.Equals(
            Path.TrimEndingDirectorySeparator(basket.DirectoryPath), directory, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? parts[0];
        var label = "…\\" + Shorten(basketName, 28);
        if (parts.Length == 1) return label;
        return label + (parts.Length > 2 ? "\\…\\" : "\\") + Shorten(parts[^1], 40);
    }

    private static string Shorten(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        var start = value.Length - maximumLength + 1;
        if (char.IsLowSurrogate(value[start])) start++;
        return "…" + value[start..];
    }
}
