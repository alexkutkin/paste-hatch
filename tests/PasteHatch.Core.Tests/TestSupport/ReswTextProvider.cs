using System.Globalization;
using System.Xml.Linq;

namespace PasteHatch.Core.Tests;

/// <summary>
/// Serves the real <c>.resw</c> file shipped with the app, so Core assertions check the strings
/// users actually see instead of a duplicate copy kept in the test project.
/// </summary>
internal sealed class ReswTextProvider : ITextProvider
{
    private readonly Dictionary<string, string> _values;

    private ReswTextProvider(Dictionary<string, string> values, CultureInfo culture)
    {
        _values = values;
        Culture = culture;
    }

    public static ReswTextProvider Russian { get; } = Load("ru-RU");

    public static ReswTextProvider English { get; } = Load("en-US");

    public CultureInfo Culture { get; }

    public string Get(string key) => _values.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object?[] arguments)
        => string.Format(Culture, Get(key), arguments);

    public string Plural(string keyPrefix, int count, params object?[] arguments)
    {
        var form = Culture.Name.StartsWith("ru", StringComparison.Ordinal)
            ? RussianPlural.Form(count)
            : count == 1 ? PluralForm.One : PluralForm.Many;
        var values = new object?[arguments.Length + 1];
        values[0] = count;
        arguments.CopyTo(values, 1);
        return string.Format(Culture, Get($"{keyPrefix}.{form}"), values);
    }

    private static ReswTextProvider Load(string language)
    {
        var path = Path.Combine(FindAppDirectory(), "Strings", language, "Resources.resw");
        var values = XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
        return new ReswTextProvider(values, CultureInfo.GetCultureInfo(language));
    }

    private static string FindAppDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PasteHatch.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find src/PasteHatch.App above {AppContext.BaseDirectory}.");
    }
}
