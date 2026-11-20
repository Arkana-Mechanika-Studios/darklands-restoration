using Darklands.Content;
using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// The translators' two commands of the program (docs/LOCALISATION.md): <c>--translator-template &lt;folder&gt;</c> writes the
/// template of the player's game there, <c>--check-language-pack &lt;folder&gt;</c> lists what is wrong with a pack; both read the
/// boxes of <c>--text-boxes &lt;file or folder&gt;</c> when given (else the template's <c>boxes.json</c>, else none).
/// </summary>
// Not in the original: the 1.1 translation packs (L-7w).
public static class TranslatorCommands
{
    /// <summary>The boxes file's name in a template folder.</summary>
    public const string BoxesFile = "boxes.json";

    /// <summary>Writes the template of the game in <paramref name="gameFolder"/> to <paramref name="output"/>; 0 when written.</summary>
    public static int WriteTemplate(string gameFolder, string output, string? boxes, TextWriter say)
    {
        ArgumentNullException.ThrowIfNull(say);
        var found = ReadBoxes(boxes, null, say);
        if (found is null)
        {
            return 1;
        }
        var messages = TranslatorTemplate.Build(new GameFiles(gameFolder), found);
        TranslatorTemplate.Write(output, messages);
        if (found.Count > 0)
        {
            TextBoxes.Write(Path.Combine(output, BoxesFile), found);
        }
        say.WriteLine($"translator's template: {messages.Count} texts in {output} ({found.Count} boxes known)");
        return 0;
    }

    /// <summary>
    /// Writes the pseudo-locale pack of the game in <paramref name="gameFolder"/> to <paramref name="output"/> (<see cref="PseudoPack"/>),
    /// with the boxes when given, and lists where its longer texts do not fit; 0 when written.
    /// </summary>
    public static int WritePseudoPack(string gameFolder, string output, string? boxes, TextWriter say)
    {
        ArgumentNullException.ThrowIfNull(say);
        var found = ReadBoxes(boxes, null, say);
        if (found is null)
        {
            return 1;
        }
        var files = new GameFiles(gameFolder);
        var template = TranslatorTemplate.Build(files, found);
        TranslatorTemplate.Write(output, PseudoPack.From(template), PseudoPack.Language, "Pseudo-locale (longer texts, marked vowels)");
        if (found.Count > 0)
        {
            TextBoxes.Write(Path.Combine(output, BoxesFile), found);
        }
        say.WriteLine($"pseudo-locale pack: {template.Count} texts in {output}; play with --language-pack to see it, the check below lists where it does not fit");
        // an error here is the generator's: the pseudo texts keep every structure the check requires
        return CheckPack(gameFolder, output, boxes, say);
    }

    /// <summary>
    /// Writes to <paramref name="output"/> a pack of <paramref name="language"/> made from the card texts of the edition in
    /// <paramref name="otherFolder"/> (<see cref="EditionPack"/>), says what was left out and why, and checks it; 0 when written.
    /// </summary>
    public static int WriteEditionPack(string gameFolder, string otherFolder, string language, string output, string? boxes, TextWriter say,
        string? variables = null)
    {
        ArgumentNullException.ThrowIfNull(say);
        if (!GameFiles.IsGameFolder(otherFolder))
        {
            say.WriteLine($"{otherFolder}: no game there (no DARKLAND.EXE)");
            return 1;
        }
        var result = EditionPack.Write(new GameFiles(gameFolder), new GameFiles(otherFolder), language, $"Card texts of the edition in {otherFolder}", output, variables);
        foreach (var group in result.LeftOut.GroupBy(l => Reason(l.Why)).OrderByDescending(g => g.Count()))
        {
            say.WriteLine($"left out: {group.Count()} block(s): {group.Key} (for example {group.First().Key}: {group.First().Why})");
        }
        say.WriteLine($"edition pack: {result.Written} card texts in {output}, {result.LeftOut.Count} left out");
        return CheckPack(gameFolder, output, boxes, say);
    }

    // the kind of a structure problem, without the block's own tags (so like ones count together)
    private static string Reason(string why)
    {
        var at = why.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? why[..at] : why;
    }

    /// <summary>
    /// Writes the boxes of <paramref name="boxes"/> without the game's words (TextBoxes.Strip) to <paramref name="output"/>: the boxes the
    /// Workshop ships (a developer's step after the run of the recorded sessions); 0 when written.
    /// </summary>
    public static int WriteStrippedBoxes(string boxes, string output, TextWriter say)
    {
        ArgumentNullException.ThrowIfNull(say);
        if (ReadBoxes(boxes, null, say) is not { Count: > 0 } found)
        {
            say.WriteLine($"{boxes}: no boxes");
            return 1;
        }
        TextBoxes.Write(output, TextBoxes.Strip(found));
        say.WriteLine($"{found.Count} boxes, without the game's words, in {output}");
        return 0;
    }

    /// <summary>Checks the pack in <paramref name="pack"/> against the game in <paramref name="gameFolder"/>; 0 when nothing stops its texts being used.</summary>
    public static int CheckPack(string gameFolder, string pack, string? boxes, TextWriter say)
    {
        ArgumentNullException.ThrowIfNull(say);
        if (LanguagePack.Load(pack, out var why) is not { } loaded)
        {
            say.WriteLine(why);
            return 1;
        }
        var found = ReadBoxes(boxes, pack, say);
        if (found is null)
        {
            return 1;
        }
        var files = new GameFiles(gameFolder);
        var findings = LanguagePackCheck.Run(loaded, new GameAssets(files), TranslatorTemplate.Build(files, found), found);
        foreach (var f in findings)
        {
            say.WriteLine(f);
        }
        var errors = findings.Count(f => f.Severity == LanguagePackCheck.Severity.Error);
        var overflows = findings.Count(f => f.Severity == LanguagePackCheck.Severity.Overflow);
        say.WriteLine($"{loaded.Name} ({loaded.Language}): {loaded.Messages.Count} texts, {errors} error(s), {overflows} too wide or too long, " +
            $"{findings.Count - errors - overflows} note(s); {found.Count} boxes known");
        return errors > 0 ? 1 : 0;
    }

    // the boxes given, else a boxes.json in the pack's folder, else none; null when the given ones cannot be read
    private static IReadOnlyList<TextBox>? ReadBoxes(string? given, string? pack, TextWriter say)
    {
        var path = given ?? (pack is not null && File.Exists(Path.Combine(pack, BoxesFile)) ? Path.Combine(pack, BoxesFile) : null);
        if (path is null)
        {
            return [];
        }
        try
        {
            return TextBoxes.Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            say.WriteLine($"{path}: {e.Message}");
            return null;
        }
    }
}
