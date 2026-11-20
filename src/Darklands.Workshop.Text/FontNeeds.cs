using Darklands.Content;
using Darklands.Presentation;
using Darklands.Presentation.Fonts;
using Darklands.Presentation.Graphics;

namespace Darklands.Workshop.Text;

/// <summary>
/// What a pack's language needs of the fonts (W-0, font packs from the Workshop): the scripts of its letters, the letters of the
/// language the game's fonts lack, and the font pack in the game's pixel style built from the player's own fonts into the pack.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class FontNeeds
{
    // the languages written in Greek or Cyrillic letters (their primary subtag)
    private static readonly HashSet<string> GreekLanguages = new(StringComparer.OrdinalIgnoreCase) { "el", "grc" };
    private static readonly HashSet<string> CyrillicLanguages = new(StringComparer.OrdinalIgnoreCase) { "ru", "uk", "be", "bg", "sr", "mk", "kk", "ky", "tg", "mn" };

    // letters each language uses beyond English (a sample, enough to tell whether the game's fonts can write it)
    private static readonly Dictionary<string, string> Letters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = "àâæçéèêëîïôœùûüÿÀÂÇÉÈÊÎÔŒÙÛ",
        ["es"] = "áéíóúñüÁÉÍÓÚÑ¿¡",
        ["pt"] = "àáâãçéêíóôõúÀÁÂÃÇÉÊÍÓÔÕÚ",
        ["it"] = "àèéìòùÀÈÉÌÒÙ",
        ["de"] = "äöüßÄÖÜ",
        ["nl"] = "éëïöü",
        ["pl"] = "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ",
        ["cs"] = "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ",
        ["sk"] = "áäčďéíĺľňóôŕšťúýžÁČĎÉÍĽŇÓŠŤÚÝŽ",
        ["hu"] = "áéíóöőúüűÁÉÍÓÖŐÚÜŰ",
        ["ro"] = "ăâîșțĂÂÎȘȚ",
        ["hr"] = "čćđšžČĆĐŠŽ",
        ["sl"] = "čšžČŠŽ",
        ["sv"] = "åäöÅÄÖ",
        ["da"] = "æøåÆØÅ",
        ["no"] = "æøåÆØÅ",
        ["nb"] = "æøåÆØÅ",
        ["fi"] = "äöÄÖ",
        ["is"] = "áðéíóúýþæöÁÐÉÍÓÚÝÞÆÖ",
        ["tr"] = "çğıöşüÇĞİÖŞÜ",
        ["ca"] = "àçèéíïòóúüÀÇÈÉÍÏÒÓÚÜ",
        ["el"] = "αβγδεζηθικλμνξοπρστυφχψωάέήίόύώΓΔΘΛΞΠΣΦΨΩ",
        ["ru"] = "абвгдеёжзийклмнопрстуфхцчшщъыьэюяБГДЁЖЗИЙЛПФЦЧШЩЪЫЬЭЮЯ",
        ["uk"] = "абвгґдеєжзиіїйклмнопрстуфхцчшщьюяБГҐДЄЖЗИЇЙЛПФЦЧШЩЬЮЯ",
        ["bg"] = "абвгдежзийклмнопрстуфхцчшщъьюяБГДЖЗИЙЛПФЦЧШЩЪЮЯ",
    };

    /// <summary>The primary subtag of a language tag (pt-BR: pt).</summary>
    public static string Primary(string language) => language.Split('-', '_')[0];

    /// <summary>The scripts a font pack for <paramref name="language"/> holds: Latin always, Greek or Cyrillic for their languages.</summary>
    public static FontScripts Scripts(string language)
    {
        var primary = Primary(language);
        return FontScripts.Latin | (GreekLanguages.Contains(primary) ? FontScripts.Greek : 0) | (CyrillicLanguages.Contains(primary) ? FontScripts.Cyrillic : 0);
    }

    /// <summary>The letters of <paramref name="language"/> the game's own text font (slot 1) has no glyph for ("" none, or unknown).</summary>
    public static string LackedByTheGame(string game, string language)
    {
        if (!Letters.TryGetValue(Primary(language), out var letters))
        {
            return "";
        }
        var painter = new TextPainter(new GameAssets(new GameFiles(game)).Font(1));
        return string.Concat(letters.Where(c => !painter.CodeFont.Has(c)));
    }

    /// <summary>Whether the pack at <paramref name="folder"/> has a font pack of its own.</summary>
    public static bool HasFontPack(string folder) => Directory.Exists(Path.Combine(folder, FontPack.Folder));

    /// <summary>Builds the font pack for <paramref name="language"/> from the player's fonts into the pack; the glyphs of each slot.</summary>
    public static IReadOnlyList<int> Build(string game, string folder, string language) =>
        FontPackMaker.Make(new GameFiles(game), folder, Scripts(language));

    /// <summary>The glyph charts the build wrote (charts/slot2-*.png: the card texts' font), for showing them.</summary>
    public static IReadOnlyList<string> Charts(string folder) =>
        Directory.Exists(Path.Combine(folder, "charts"))
            ? [.. Directory.GetFiles(Path.Combine(folder, "charts"), "slot2-*.png").Order(StringComparer.Ordinal)]
            : [];
}
