using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using PasteHatch.Core;

namespace PasteHatch;

/// <summary>
/// Resolves user-facing text from the compiled <c>.resw</c> resources for the selected language.
/// XAML binds to the indexer, so raising <c>Item[]</c> re-reads every bound string at once.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged, ITextProvider
{
    private static readonly ResourceManager Manager = new();
    private ResourceContext _context;
    private CultureInfo _culture;

    public LocalizedStrings()
    {
        _context = CreateContext(Localization.ResourceLanguage);
        _culture = Localization.Culture;
        Current = this;
    }

    /// <summary>The instance declared in <c>App.xaml</c>; created once per application run.</summary>
    public static LocalizedStrings Current { get; private set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public string Get(string key) => Manager.MainResourceMap
        .TryGetValue("Resources/" + key.Replace('.', '/'), _context)?.ValueAsString ?? key;

    public string Format(string key, params object?[] arguments)
        => string.Format(_culture, Get(key), arguments);

    public string Plural(string keyPrefix, int count, params object?[] arguments)
    {
        var form = Localization.IsRussian
            ? RussianPlural.Form(count)
            : count == 1 ? PluralForm.One : PluralForm.Many;
        var values = new object?[arguments.Length + 1];
        values[0] = count;
        arguments.CopyTo(values, 1);
        return string.Format(_culture, Get($"{keyPrefix}.{form}"), values);
    }

    internal void Select(string resourceLanguage, CultureInfo culture)
    {
        _context = CreateContext(resourceLanguage);
        _culture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static ResourceContext CreateContext(string language)
    {
        var context = Manager.CreateResourceContext();
        context.QualifierValues["language"] = language;
        return context;
    }
}
