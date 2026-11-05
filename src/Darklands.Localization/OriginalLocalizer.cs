namespace Darklands.Localization;

/// <summary>The <c>en-original</c> locale: answers straight from the original table, with no overlay.</summary>
public sealed class OriginalLocalizer : ILocalizer
{
    private readonly OriginalTable _table;

    /// <summary>Creates the localizer over <paramref name="table"/>.</summary>
    public OriginalLocalizer(OriginalTable table) => _table = table ?? throw new ArgumentNullException(nameof(table));

    /// <inheritdoc />
    public string Locale => "en-original";

    /// <inheritdoc />
    public MissingKeyLog Missing { get; } = new();

    /// <inheritdoc />
    public string Get(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        if (_table.TryGet(key, out var text))
        {
            return text;
        }

        Missing.Record(key, MissingKind.Unknown);
        return key;
    }

    /// <inheritdoc />
    public string? GetAttribute(string key, string attribute, IReadOnlyDictionary<string, object?>? args = null) => null;

    /// <inheritdoc />
    public bool IsTranslated(string key) => _table.TryGet(key, out _);
}
