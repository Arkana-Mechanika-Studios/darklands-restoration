namespace Darklands.Localization;

/// <summary>A problem found while loading a translation file or while formatting a message.</summary>
/// <param name="Source">File name (or other source name) the problem belongs to; empty for run-time formatting.</param>
/// <param name="Line">1-based line in <paramref name="Source"/>, or 0 when unknown.</param>
/// <param name="Message">What is wrong.</param>
public readonly record struct LocalizationDiagnostic(string Source, int Line, string Message)
{
    /// <inheritdoc />
    public override string ToString() => Line > 0 ? $"{Source}:{Line}: {Message}" : $"{Source}: {Message}";
}
