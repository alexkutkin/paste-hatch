using System;
using System.Globalization;
using PasteHatch.Core;

namespace PasteHatch;

/// <summary>
/// The selected UI language and the culture used for dates, numbers and counted phrases.
/// English is the default; the choice is persisted in <see cref="AppSettings.Language"/>.
/// </summary>
public static class Localization
{
    public const string English = "en";
    public const string Russian = "ru";

    private static string _language = English;

    public static string Language => _language;

    public static bool IsRussian => string.Equals(_language, Russian, StringComparison.Ordinal);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>BCP-47 tag of the resource folder this language reads from.</summary>
    public static string ResourceLanguage => IsRussian ? "ru-RU" : "en-US";

    /// <summary>Text for the current language, or resource keys before the app resources exist.</summary>
    public static ITextProvider Text => (ITextProvider?)LocalizedStrings.Current ?? KeyTextProvider.Instance;

    /// <summary>Raised after the language changed, so code-built text can be rebuilt.</summary>
    public static event EventHandler? LanguageChanged;

    public static string Normalize(string? language)
        => string.Equals(language, Russian, StringComparison.OrdinalIgnoreCase) ? Russian : English;

    /// <summary>
    /// Selects the startup language. Must run before <c>InitializeComponent</c> in the App
    /// constructor so the resource object is created with the right context and Windows uses the
    /// chosen language for its own framework strings.
    /// </summary>
    public static void Initialize(string? language)
    {
        _language = Normalize(language);
        Apply();
    }

    public static void SetLanguage(string? language)
    {
        var normalized = Normalize(language);
        if (normalized == _language)
        {
            return;
        }

        _language = normalized;
        Apply();
        LocalizedStrings.Current?.Select(ResourceLanguage, Culture);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void Apply()
    {
        Culture = CultureInfo.GetCultureInfo(IsRussian ? "ru-RU" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = ResourceLanguage;
    }
}
