using System.Globalization;
using Darklands.Content.Formats.Exe;
using Darklands.Core;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>
/// A rule's <see cref="GameMessage"/> or a card's <see cref="GameText"/> in the original's words, read from the player's
/// DARKLAND.EXE: the original-language source of the game's texts, beside <see cref="OriginalTextExtractor"/> (an <c>exe:DS:offset</c>
/// or <c>exe:290E:offset</c> id is the string there; a built sentence is put together from the EXE's pieces as the original does).
/// The rewrite's own notices (<c>id:rw.*</c>) are its own English. Only the presentation asks it (the game carries ids: the L-0
/// architecture test); the localiser takes its place in front of the screens with the language setting.
/// </summary>
public sealed class OriginalMessageText(DarklandsExecutable exe)
{
    private readonly OriginalWords _words = new(exe);

    // orig: raise-skill-by R029+0x02EA and ability-change-notice R021+0x0080: the skill's name (the far table 290E:2137, OriginalWords.SkillNames), " changed from " (DS:26BE), the old
    //   value, " to " (DS:26CD), the new value, in text-bank-buffer (DS:9085)
    private const int ChangedFrom = 0x26BE;
    private const int To = 0x26CD;

    // orig: chance-line R029+0x5B62: "Chance of success is " (DS:27CD), the low value, " to " (DS:27E5), the high value; a value is "N%" at
    //   Basic, else grade-words[v / 20]
    private const int ChanceOfSuccessIs = 0x27CD;
    private const int ChanceTo = 0x27E5;

    // orig: the Shift hint R043+0x27E6: "The chance of success is " (DS:5284), then "N%" (Basic) or a grade word, "hopeless" (DS:52A0) for 0
    private const int TheChanceOfSuccessIs = 0x5284;
    private const int Hopeless = 0x52A0;

    private const int MaxLength = 200;

    // The rewrite's own notices (no original text: the screens a test host does not have, the dispatcher's and mod cards' notices).
    private static readonly Dictionary<string, Func<GameText, string>> Own = new(StringComparer.Ordinal)
    {
        [GameTextIds.TradingUnavailable] = t => $"{t.Word("name")}: trading is not available here.",
        [GameTextIds.CampUnavailable] = _ => "Camping is not available here.",
        [GameTextIds.AlchemyUnavailable] = _ => "Alchemy is not available here.",
        [GameTextIds.ItemStoreUnavailable] = _ => "The item store is not available here.",
        [GameTextIds.CharacterSheetUnavailable] = _ => "The character sheet is not available here.",
        [GameTextIds.PartyInfoUnavailable] = _ => "Party information is not available here.",
        [GameTextIds.LootUnavailable] = _ => "Loot is not available here.",
        [GameTextIds.BattleUnavailable] = _ => "Battles are not available here: you win.",
        [GameTextIds.CardNotInTable] = t => $"Card {t.Word("number")} is not in the card table.",
        [GameTextIds.CardHasNoScript] = t => $"{t.Word("name")} has no script.",
        [GameTextIds.ModCardGoesRound] = t => $"{t.Word("name")} goes round in circles: back to the card before.",
        ["id:rw.notice.and-more"] = t => $"... and {t.Word("count")} more",
        // the load's refusals and the missing mods' notice (DarklandsGame), one box a line
        ["id:rw.save.damaged.1"] = _ => "This saved game is damaged and cannot be loaded.",
        ["id:rw.save.impossible.1"] = _ => "This saved game holds values no game can have",
        ["id:rw.save.impossible.2"] = _ => "and cannot be loaded.",
        ["id:rw.notice.press-a-key"] = _ => "Press a key.",
        ["id:rw.card.more"] = _ => "(more ...)",
        ["id:rw.save.mods-missing.one"] = _ => "This game was saved with the mod",
        ["id:rw.save.mods-missing.many"] = _ => "This game was saved with the mods",
        ["id:rw.save.mods-missing.more"] = t => $"and {t.Word("count")} more",
        ["id:rw.save.mods-missing.one-end.1"] = _ => "which is not loaded now. It goes on",
        ["id:rw.save.mods-missing.one-end.2"] = _ => "without it. Press a key.",
        ["id:rw.save.mods-missing.many-end.1"] = _ => "which are not loaded now. It goes on",
        ["id:rw.save.mods-missing.many-end.2"] = _ => "without them. Press a key.",
    };

    /// <summary>The words of one of the rewrite's own notices (null for any other id): they need no EXE.</summary>
    public static string? OwnWords(GameText text) => text is not null && Own.TryGetValue(text.Id, out var own) ? own(text) : null;

    /// <summary>The ids of the rewrite's own notices (for tests: every one has words).</summary>
    public static IReadOnlyCollection<string> OwnIds => Own.Keys;

    /// <summary>The message's text; an id it does not know comes back as the id itself (never an empty string).</summary>
    public string Text(GameMessage message) => Text(new GameText(message.Id, message.Args));

    /// <summary>The text's words; <see cref="GameText.None"/> is "", an id it does not know comes back as the id itself.</summary>
    public string Text(GameText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (text.Id)
        {
            case "":
                return "";
            case GameMessageIds.SkillChanged:
                return _words.Text(OriginalWords.SkillNames, text.Number("skill")) + exe.String(ChangedFrom) + Number(text, "from") +
                    exe.String(To) + Number(text, "to");
            case GameTextIds.ChanceRange:
                return exe.String(ChanceOfSuccessIs) + Chance(text, "low") + exe.String(ChanceTo) + Chance(text, "high");
            case GameTextIds.HintChance:
                var chance = text.Number("chance");
                return exe.String(TheChanceOfSuccessIs) + (text.Number("basic") != 0
                    ? Percent(chance)
                    : chance != 0 ? _words.GradeWord(chance / 20) : exe.String(Hopeless));
        }
        if (Own.TryGetValue(text.Id, out var own))
        {
            return own(text);
        }
        if (Exe(text.Id) is not { } words)
        {
            return text.Id;
        }
        // a format string of the original with its arguments "0", "1", ... (the screens' sprintf sentences): printed as the original prints it
        return text.Args.Count == 0 ? words : OriginalFormat.Format(words, Positional(text));
    }

    private static object[] Positional(GameText text)
    {
        var args = new object[text.Args.Count];
        for (var k = 0; k < args.Length; k++)
        {
            args[k] = text.Args.TryGetValue(k.ToString(CultureInfo.InvariantCulture), out var v)
                ? v
                : throw new ArgumentException($"{text.Id}: argument {k} missing (a format's arguments are \"0\", \"1\", ...)", nameof(text));
        }
        return args;
    }

    // exe:DS:XXXX or exe:SSSS:XXXX (a far segment, 290E): the zero-terminated string there
    private string? Exe(string id) => _words.ExeOrNull(id);

    // a value of chance-line: "N%" at Basic, else the grade word (the argument is then the word's index)
    private string Chance(GameText text, string name) =>
        text.Number("basic") != 0 ? Percent(text.Number(name)) : _words.GradeWord(text.Number(name));

    private static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";

    private static string Number(GameText text, string name) => text.Number(name).ToString(CultureInfo.InvariantCulture);
}
