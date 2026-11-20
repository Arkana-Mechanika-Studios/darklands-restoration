using System.Text.Json;

namespace Darklands.Workshop.Text;

/// <summary>
/// The human titles of the texts in the Workshop's list (W-0 polish): a card's from its card script's name (the table
/// Resources/card-titles.json, made from the scripts' class names: "City church by day"), a block's from its own first words, a
/// screen text's group from its screen, a name's group from its record kind. Nothing is written by hand.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class TextTitles
{
    private static readonly Lazy<Dictionary<string, string>> Cards = new(() =>
    {
        using var stream = typeof(TextTitles).Assembly.GetManifestResourceStream("Darklands.Workshop.Text.card-titles.json")
            ?? throw new InvalidOperationException("the card titles are not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });

    /// <summary>The section of the list a text belongs to: Cards, Screens, Names or Not seen.</summary>
    public static string Section(TextEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = entry.Key;
        return key.StartsWith("msg:", StringComparison.Ordinal) ? "Cards"
            : key.StartsWith("lst:", StringComparison.Ordinal) || key.StartsWith("cty:", StringComparison.Ordinal)
              || key.StartsWith("loc:", StringComparison.Ordinal) || key.StartsWith("enm:", StringComparison.Ordinal) ? "Names"
            : entry.Screens.Count > 0 ? "Screens"
            : "Not seen";
    }

    /// <summary>The group of a text in its section (a card's message file, a screen, a record kind) and the group's title.</summary>
    public static (string Key, string Title) Group(TextEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = entry.Key;
        if (key.StartsWith("msg:", StringComparison.Ordinal))
        {
            var file = key[4..key.IndexOf('#', StringComparison.Ordinal)];
            return ("msg:" + file, Card(file));
        }
        return Section(entry) switch
        {
            "Names" => key switch
            {
                _ when key.StartsWith("lst:item", StringComparison.Ordinal) => ("names:items", "Items"),
                _ when key.StartsWith("lst:saint", StringComparison.Ordinal) => ("names:saints", "Saints"),
                _ when key.StartsWith("lst:formula", StringComparison.Ordinal) => ("names:formulae", "Formulae"),
                _ when key.StartsWith("cty:", StringComparison.Ordinal) => ("names:cities", "Cities"),
                _ when key.StartsWith("loc:", StringComparison.Ordinal) => ("names:places", "Places"),
                _ => ("names:enemies", "Enemies"),
            },
            "Screens" => ("screen:" + entry.Screens[0], Screen(entry.Screens[0])),
            _ => ("other", "Not seen in the recorded sessions"),
        };
    }

    /// <summary>A card's title: its script's name in words, else the message file's name.</summary>
    public static string Card(string file) => Cards.Value.TryGetValue(file.ToUpperInvariant(), out var title) ? title : file;

    /// <summary>A screen's name in words ("BarterScreen" → "Barter", "CharacterSheetScreen" → "Character sheet").</summary>
    public static string Screen(string screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var name = screen.EndsWith("Screen", StringComparison.Ordinal) && screen.Length > 6 ? screen[..^6] : screen;
        var words = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return words.Length == 0 ? screen : words[..1].ToUpperInvariant() + words[1..].ToLowerInvariant();
    }

    /// <summary>A text's own title: its first words, cut at a word near <paramref name="length"/> letters.</summary>
    public static string FirstWords(string english, int length = 40)
    {
        ArgumentNullException.ThrowIfNull(english);
        var rows = CardRows.Parse(english);
        var text = rows.Count > 0 && rows[0].Kind != CardRowKind.Option ? CardRow.ToField(rows[0].Content) : CardRow.ToField(english);
        // a menu's hot key (<c12> M</c>ore: the letter, its padding left out), the game's control codes ({"\u0002"}) and placeables ({ $arg0 })
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<c\d+>\s*([^<\s]+)</c>", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\s*""\\u[0-9A-Fa-f]{4}""\s*\}", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\{\s*(\$\w+)\s*\}", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text.Replace('\n', ' '), @"\s+", " ").Trim();
        if (text.Length <= length)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', length);
        return text[..(cut > length / 2 ? cut : length)].TrimEnd(',', ';', ':', '.') + "…";
    }
}
