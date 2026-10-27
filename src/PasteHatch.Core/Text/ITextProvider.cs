namespace PasteHatch.Core;

/// <summary>
/// Supplies user-facing text already translated into the current UI language. The app implements
/// this over its <c>.resw</c> resources; <see cref="KeyTextProvider"/> is the neutral fallback used
/// by tests and by callers that do not need human-readable output.
/// </summary>
public interface ITextProvider
{
    /// <summary>Text for a resource key.</summary>
    string Get(string key);

    /// <summary>Text for a resource key, with <c>{0}</c>-style placeholders filled in.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>
    /// Text for a counted phrase. <paramref name="keyPrefix"/> names three resources
    /// (<c>.One</c>, <c>.Few</c>, <c>.Many</c>); the correct one is chosen for
    /// <paramref name="count"/> in the current language.
    /// </summary>
    string Plural(string keyPrefix, int count, params object?[] arguments);
}

/// <summary>Returns resource keys instead of text; keeps Core deterministic when no UI is present.</summary>
public sealed class KeyTextProvider : ITextProvider
{
    public static readonly KeyTextProvider Instance = new();

    private KeyTextProvider()
    {
    }

    public string Get(string key) => key;

    public string Format(string key, params object?[] arguments) => key;

    public string Plural(string keyPrefix, int count, params object?[] arguments) => keyPrefix;
}
