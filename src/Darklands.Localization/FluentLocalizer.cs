using System.Globalization;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Bundle.Errors;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;

namespace Darklands.Localization;

/// <summary>A function a translation can call, such as <c>{ OPTION(1) }</c>. Arguments are strings or doubles.</summary>
public delegate object? FluentFunctionImpl(IReadOnlyList<object?> arguments);

/// <summary>
/// An <see cref="ILocalizer"/> that overlays Project Fluent (<c>.ftl</c>) resources over the original table and falls
/// back to the original text per key. Backed by the Linguini library, which no other project references.
/// Safe to call from several threads (calls are serialised).
/// </summary>
public sealed class FluentLocalizer : ILocalizer
{
    private const int MaxRuntimeDiagnostics = 200;

    private readonly OriginalTable _original;
    private readonly FluentBundle _bundle;
    private readonly bool _pseudo;
    private readonly object _gate = new();
    private readonly List<LocalizationDiagnostic> _diagnostics = new();
    private int _runtimeDiagnostics;

    /// <summary>Creates a localizer for <paramref name="locale"/> with no overlay loaded yet.</summary>
    /// <param name="locale">Locale tag (<c>de</c>, <c>ru</c>, <c>qps</c>); it selects the plural rules.</param>
    /// <param name="original">The fallback texts.</param>
    /// <param name="functions">Functions translations may call, by upper-case name.</param>
    public FluentLocalizer(string locale, OriginalTable original, IReadOnlyDictionary<string, FluentFunctionImpl>? functions = null)
        : this(locale, original, functions, pseudoTransform: true)
    {
    }

    /// <summary>
    /// A localizer whose <c>qps</c> locale applies the pseudo transform only when <paramref name="pseudoTransform"/> (a translation pack's
    /// texts are used as written: a pseudo-locale pack is made already transformed, in letters the game's fonts have).
    /// </summary>
    public FluentLocalizer(string locale, OriginalTable original, IReadOnlyDictionary<string, FluentFunctionImpl>? functions, bool pseudoTransform)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);
        Locale = locale;
        _original = original ?? throw new ArgumentNullException(nameof(original));
        _pseudo = pseudoTransform && string.Equals(locale, PseudoLocale.Tag, StringComparison.OrdinalIgnoreCase);

        var step = LinguiniBuilder.Builder().CultureInfo(CultureFor(locale)).SkipResources().SetUseIsolating(false);
        if (functions is not null)
        {
            foreach (var (name, impl) in functions)
            {
                step = step.AddFunction(name, Wrap(impl));
            }
        }

        if (_pseudo)
        {
            step = step.SetTransformFunc(static text => PseudoLocale.Transform(text, brackets: false, placeables: false));
        }

        _bundle = step.UncheckedBuild();
    }

    /// <inheritdoc />
    public string Locale { get; }

    /// <inheritdoc />
    public MissingKeyLog Missing { get; } = new();

    /// <summary>Problems found while loading overlays, and (capped) while formatting.</summary>
    public IReadOnlyList<LocalizationDiagnostic> Diagnostics
    {
        get
        {
            lock (_gate)
            {
                return _diagnostics.ToArray();
            }
        }
    }

    /// <summary>
    /// Adds Fluent source. Valid entries are kept even when others have errors; the errors are returned
    /// (and kept in <see cref="Diagnostics"/>) with the source name and line. A message id that an earlier overlay
    /// already defines is reported and the earlier one is kept.
    /// </summary>
    public IReadOnlyList<LocalizationDiagnostic> AddOverlay(string sourceName, string ftlText)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(ftlText);
        var found = new List<LocalizationDiagnostic>();
        lock (_gate)
        {
            _bundle.AddResource(ftlText, out List<FluentError>? errors);
            foreach (var error in errors ?? new List<FluentError>())
            {
                found.Add(new LocalizationDiagnostic(sourceName, LineOf(error), error.ToString() ?? error.ErrorKind().ToString()));
            }

            _diagnostics.AddRange(found);
        }

        return found;
    }

    /// <summary>Adds the Fluent file at <paramref name="path"/> (UTF-8).</summary>
    public IReadOnlyList<LocalizationDiagnostic> AddOverlayFile(string path) =>
        AddOverlay(path, File.ReadAllText(path));

    /// <inheritdoc />
    public bool IsTranslated(string key)
    {
        var id = IdOrNull(key);
        if (id is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _bundle.HasMessage(id);
        }
    }

    /// <inheritdoc />
    public string Get(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        var id = IdOrNull(key);
        if (id is not null)
        {
            lock (_gate)
            {
                // On a formatting problem (for example a missing variable) Linguini returns false but still
                // hands back the text with the failing placeable shown as written; that is used, and the problem is logged.
                if (_bundle.HasMessage(id))
                {
                    _bundle.TryGetMessage(id, ConvertArgs(args), out var errors, out var text);
                    NoteRuntime(key, errors);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return _pseudo ? "[" + text + "]" : text;
                    }
                }
            }
        }

        if (_original.TryGet(key, out var original))
        {
            Missing.Record(key, MissingKind.Untranslated);
            return _pseudo ? PseudoLocale.Transform(original, brackets: true, placeables: false) : original;
        }

        Missing.Record(key, MissingKind.Unknown);
        return key;
    }

    /// <inheritdoc />
    public string? GetAttribute(string key, string attribute, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(attribute);
        var id = IdOrNull(key);
        if (id is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_bundle.HasMessage(id))
            {
                var ok = _bundle.TryGetMessage(id, attribute, ConvertArgs(args), out var errors, out var text);
                NoteRuntime(key + "." + attribute, errors);
                if ((ok || errors is { Count: > 0 }) && !string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? IdOrNull(string key)
    {
        try
        {
            return KeyMangling.ToFluentId(key);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Caller holds the gate.
    private void NoteRuntime(string where, IList<FluentError>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return;
        }

        foreach (var error in errors ?? new List<FluentError>())
        {
            if (_runtimeDiagnostics++ >= MaxRuntimeDiagnostics)
            {
                return;
            }

            _diagnostics.Add(new LocalizationDiagnostic(where, 0, error.ToString() ?? error.ErrorKind().ToString()));
        }
    }

    private static int LineOf(FluentError error)
    {
        try
        {
            // The parser's row is the 1-based line number.
            return error.GetSpan()?.Row ?? 0;
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or NotSupportedException)
        {
            return 0;
        }
    }

    private static CultureInfo CultureFor(string locale)
    {
        foreach (var candidate in new[] { locale, locale.Split('-')[0] })
        {
            try
            {
                return CultureInfo.GetCultureInfo(candidate);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.GetCultureInfo("en");
    }

    private static Dictionary<string, IFluentType>? ConvertArgs(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, IFluentType>(args.Count);
        foreach (var (name, value) in args)
        {
            map[name] = ToFluent(value);
        }

        return map;
    }

    private static IFluentType ToFluent(object? value) => value switch
    {
        null => (FluentString)string.Empty,
        string s => (FluentString)s,
        double d => (FluentNumber)d,
        float f => (FluentNumber)f,
        int or long or short or byte or sbyte or ushort or uint or ulong or decimal =>
            (FluentNumber)Convert.ToDouble(value, CultureInfo.InvariantCulture),
        IFluentType already => already,
        _ => (FluentString)(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static ExternalFunction Wrap(FluentFunctionImpl impl) => (positional, _) =>
    {
        var args = new object?[positional.Count];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = positional[i] is FluentNumber n ? n.Value : positional[i].AsString();
        }

        return ToFluent(impl(args));
    };
}
