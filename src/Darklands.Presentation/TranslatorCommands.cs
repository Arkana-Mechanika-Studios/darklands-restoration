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
