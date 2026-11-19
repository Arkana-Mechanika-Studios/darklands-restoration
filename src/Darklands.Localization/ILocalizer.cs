namespace Darklands.Localization;

/// <summary>
/// Looks up player-visible text by key. The game asks for message ids plus arguments; the answer is the text
/// of the active locale, or the original text when that locale has no translation for the key.
/// An answer is never empty for a key the original table knows; for a key nobody knows it is the key itself.
/// </summary>
public interface ILocalizer
{
    /// <summary>The locale tag this localizer answers for, for example <c>en-original</c>, <c>de</c> or <c>qps</c>.</summary>
    string Locale { get; }

    /// <summary>Text for <paramref name="key"/>. Arguments become Fluent variables (strings and numbers).</summary>
    string Get(string key, IReadOnlyDictionary<string, object?>? args = null);

    /// <summary>
    /// An attribute of the message, for example <c>key</c> (the hotkey letter) or <c>option-2</c>;
    /// null when the active locale has no such attribute.
    /// </summary>
    string? GetAttribute(string key, string attribute, IReadOnlyDictionary<string, object?>? args = null);

    /// <summary>True when the active locale itself (not the original fallback) defines the key.</summary>
    bool IsTranslated(string key);

    /// <summary>Keys that were asked for but not found in the active locale.</summary>
    MissingKeyLog Missing { get; }
}

/// <summary>Convenience overloads for <see cref="ILocalizer"/>.</summary>
public static class LocalizerExtensions
{
    /// <summary>Text for <paramref name="key"/> with arguments given as name/value pairs.</summary>
    public static string Get(this ILocalizer localizer, string key, params (string Name, object? Value)[] args)
    {
        if (args.Length == 0)
        {
            return localizer.Get(key, (IReadOnlyDictionary<string, object?>?)null);
        }

        var map = new Dictionary<string, object?>(args.Length);
        foreach (var (name, value) in args)
        {
            map[name] = value;
        }

        return localizer.Get(key, map);
    }
}
