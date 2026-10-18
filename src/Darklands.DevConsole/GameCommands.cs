using System.Globalization;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using CharacterAttribute = Darklands.Core.Characters.Attribute;

namespace Darklands.DevConsole;

/// <summary>
/// The console's commands on the game being played: they read and change the game's state through the
/// game's own types and rules (time passes through <see cref="TimePassage"/>), between two frames.
/// Screens show a change the next time they draw.
/// </summary>
public static class GameCommands
{
    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Registers the commands; <paramref name="game"/> gives the game being played (null when none is).</summary>
    public static void Register(GameConsole console, Func<GameState?> game)
    {
        Func<string[], ConsoleResult> On(Func<GameState, string[], ConsoleResult> run) => words =>
            game() is { } state ? run(state, words) : ConsoleResult.Error("no game is being played yet");

        console.Add(new ConsoleCommand("show", "show time | place | party | money | state", On(Show)));
        console.Add(new ConsoleCommand("time", "time set <hour 0..23> | time advance <hours>", On(Time)));
        console.Add(new ConsoleCommand("date", "date set <month 1..12> <day> [year]", On(Date)));
        console.Add(new ConsoleCommand("money", "money add|set <florins> [groschen] [pfennigs]", On(MoneyCommand)));
        console.Add(new ConsoleCommand("reputation", "reputation set <value>  (the current place)", On(Reputation)));
        console.Add(new ConsoleCommand("character",
            "character <slot 1..5|all> <attribute|skill> <value>  (attributes: end str agl per int chr df; skills: edge impact "
            + "flail polearm thrown bow missile alchemy religion virtue speakcommon latin readwrite healing artifice stealth "
            + "streetwise riding woodwise)", On(CharacterCommand)));
        console.Add(new ConsoleCommand("heal", "heal [slot 1..5|all]  (endurance and strength back to their maximums)", On(Heal)));
        console.Add(new ConsoleCommand("saint", "saint grant|remove <slot 1..5|all> <saint number>", On(Saint)));
        console.Add(new ConsoleCommand("item", "item grant <slot 1..5|all> <code> [quality] [count] | item find <text>", On(Item)));
    }

    // ---- show

    private static ConsoleResult Show(GameState s, string[] w) => Word(w, 0) switch
    {
        "time" => ConsoleResult.Ok(DateText(s.Date)),
        "place" => ConsoleResult.Ok(PlaceText(s)),
        "money" => ConsoleResult.Ok($"purse {s.Party.Purse}"),
        "party" => new ConsoleResult(PartyLines(s)),
        "state" => new ConsoleResult([DateText(s.Date), PlaceText(s),
            $"purse {s.Party.Purse}, fame {s.Party.Fame}, card 0x{s.CurrentCard:X2}", .. PartyLines(s)]),
        _ => throw new ArgumentException("show what?"),
    };

    private static string DateText(GameDate d) =>
        $"{d.Day} {MonthNames[GameDate.Mod12(d.Month)]} {d.Year}, {d.Hour:00}:00";

    private static string PlaceText(GameState s) => s.Here is { } here
        ? $"{(here.Name.Length > 0 ? here.Name : "location")} (#{here.Index}), reputation {here.Reputation}, map {s.MapPosition.X},{s.MapPosition.Y}"
        : $"the wilderness, map {s.MapPosition.X},{s.MapPosition.Y}";

    private static List<string> PartyLines(GameState s)
    {
        var lines = new List<string>();
        for (var i = 0; i < s.Party.Members.Count; i++)
        {
            var m = s.Party.Members[i];
            if (!m.IsPresent)
            {
                continue;
            }
            lines.Add($"{i + 1}{(i == s.Party.LeaderIndex ? "*" : " ")} {m.Name} ({m.Status}): "
                + string.Join(" ", Enum.GetValues<CharacterAttribute>().Select(a => $"{AttributeCode(a)} {m[a]}/{m.Maximum(a)}")));
        }
        return lines;
    }

    // ---- time

    private static ConsoleResult Time(GameState s, string[] w)
    {
        switch (Word(w, 0))
        {
            case "set":
                s.Date = s.Date with { Hour = (short)Number(w, 1, 0, 23) };
                return ConsoleResult.Ok(DateText(s.Date));
            case "advance":
                TimePassage.Advance(s, Number(w, 1, 0, 24 * 365));
                return ConsoleResult.Ok(DateText(s.Date));
            default:
                throw new ArgumentException("time set or time advance?");
        }
    }

    private static ConsoleResult Date(GameState s, string[] w)
    {
        if (Word(w, 0) != "set")
        {
            throw new ArgumentException("date set?");
        }
        var month = Number(w, 1, 1, 12) - 1;
        var day = Number(w, 2, 1, GameDate.DaysInMonth(month));
        var year = w.Length > 3 ? Number(w, 3, 1, short.MaxValue) : s.Date.Year;
        s.Date = s.Date with { Month = (short)month, Day = (short)day, Year = (short)year };
        return ConsoleResult.Ok(DateText(s.Date));
    }

    // ---- money and standing

    private static ConsoleResult MoneyCommand(GameState s, string[] w)
    {
        var verb = Word(w, 0);
        var amount = Money.From(Number(w, 1, 0, short.MaxValue), w.Length > 2 ? Number(w, 2, 0, short.MaxValue) : 0,
            w.Length > 3 ? Number(w, 3, 0, short.MaxValue) : 0);
        s.Party.Purse = verb switch
        {
            "add" => s.Party.Purse.Add(amount),
            "set" => amount,
            _ => throw new ArgumentException("money add or money set?"),
        };
        return ConsoleResult.Ok($"purse {s.Party.Purse}");
    }

    private static ConsoleResult Reputation(GameState s, string[] w)
    {
        if (Word(w, 0) != "set")
        {
            throw new ArgumentException("reputation set?");
        }
        if (s.Here is not { } here)
        {
            return ConsoleResult.Error("the party is in the wilderness: no place to have a reputation in");
        }
        here.Reputation = Number(w, 1, short.MinValue, short.MaxValue);
        return ConsoleResult.Ok(PlaceText(s));
    }

    // ---- characters

    private static ConsoleResult CharacterCommand(GameState s, string[] w)
    {
        var members = Members(s, Word(w, 0));
        var name = Word(w, 1);
        var value = Number(w, 2, 0, 127);
        var attribute = AttributeNamed(name);
        var skill = attribute is null ? SkillNamed(name) : null;
        if (attribute is null && skill is null)
        {
            throw new ArgumentException($"no attribute or skill \"{name}\"");
        }
        var lines = new List<string>();
        foreach (var m in members)
        {
            if (attribute is { } a)
            {
                m.Attributes[(int)a] = value;
                m.AttributeMaximums[(int)a] = Math.Max(m.AttributeMaximums[(int)a], value);
                lines.Add($"{m.Name}: {AttributeCode(a)} {m[a]}/{m.Maximum(a)}");
            }
            else
            {
                m.Skills[(int)skill!.Value] = value;
                lines.Add($"{m.Name}: {skill} {m[skill.Value]}");
            }
        }
        return new ConsoleResult(lines);
    }

    private static ConsoleResult Heal(GameState s, string[] w)
    {
        var lines = new List<string>();
        foreach (var m in Members(s, w.Length > 0 ? w[0] : "all"))
        {
            m.Attributes[(int)CharacterAttribute.Endurance] = m.Maximum(CharacterAttribute.Endurance);
            m.Attributes[(int)CharacterAttribute.Strength] = m.Maximum(CharacterAttribute.Strength);
            lines.Add($"{m.Name}: end {m[CharacterAttribute.Endurance]}, str {m[CharacterAttribute.Strength]}");
        }
        return new ConsoleResult(lines);
    }

    private static ConsoleResult Saint(GameState s, string[] w)
    {
        var verb = Word(w, 0);
        if (verb is not ("grant" or "remove"))
        {
            throw new ArgumentException("saint grant or saint remove?");
        }
        var members = Members(s, Word(w, 1));
        var saint = Number(w, 2, 0, Character.SaintCount - 1);
        foreach (var m in members)
        {
            if (verb == "grant")
            {
                m.Saints.Add(saint);
            }
            else
            {
                m.Saints.Remove(saint);
            }
        }
        return ConsoleResult.Ok($"saint {saint} {(verb == "grant" ? "known by" : "forgotten by")} {string.Join(", ", members.Select(m => m.Name))}");
    }

    private static ConsoleResult Item(GameState s, string[] w)
    {
        var items = s.Data.ItemDefinitions;
        switch (Word(w, 0))
        {
            case "find":
                var text = string.Join(' ', w.Skip(1));
                if (text.Length == 0)
                {
                    throw new ArgumentException("find what?");
                }
                var found = items.Where(d => d.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(30)
                    .Select(d => $"{d.Code,4}  {d.Name}  (quality {d.Quality}, weight {d.Weight})").ToList();
                return found.Count == 0 ? ConsoleResult.Error($"no item named like \"{text}\"") : new ConsoleResult(found);
            case "grant":
                var members = Members(s, Word(w, 1));
                var code = Number(w, 2, 0, items.Count - 1);
                var item = items[code];
                var quality = w.Length > 3 ? Number(w, 3, 1, 99) : Math.Max(1, item.Quality);
                var count = w.Length > 4 ? Number(w, 4, 1, 255) : 1;
                var full = members.FirstOrDefault(m => m.Pack.Count >= Pack.Size);
                if (full is not null)
                {
                    return ConsoleResult.Error($"the pack of {full.Name} is full");
                }
                foreach (var m in members)
                {
                    m.Pack.Add(new ItemStack(code, (byte)item.Type, quality, count, (byte)item.Weight));
                }
                return ConsoleResult.Ok($"{count} x {item.Name} (quality {quality}) to {string.Join(", ", members.Select(m => m.Name))}");
            default:
                throw new ArgumentException("item grant or item find?");
        }
    }

    // ---- words

    private static string Word(string[] w, int i) =>
        i < w.Length ? w[i].ToLowerInvariant() : throw new ArgumentException("a word is missing");

    private static int Number(string[] w, int i, int min, int max)
    {
        if (i >= w.Length)
        {
            throw new ArgumentException("a number is missing");
        }
        if (!int.TryParse(w[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
        {
            throw new ArgumentException($"\"{w[i]}\" must be a number from {min} to {max}");
        }
        return n;
    }

    /// <summary>A party slot 1..5, or all (the members present).</summary>
    private static List<Character> Members(GameState s, string slot)
    {
        if (slot.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return s.Party.Present.ToList();
        }
        if (!int.TryParse(slot, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > s.Party.Members.Count
            || !s.Party.Members[n - 1].IsPresent)
        {
            throw new ArgumentException($"no party member in slot \"{slot}\"");
        }
        return [s.Party.Members[n - 1]];
    }

    private static string AttributeCode(CharacterAttribute a) => a switch
    {
        CharacterAttribute.Endurance => "end",
        CharacterAttribute.Strength => "str",
        CharacterAttribute.Agility => "agl",
        CharacterAttribute.Perception => "per",
        CharacterAttribute.Intelligence => "int",
        CharacterAttribute.Charisma => "chr",
        _ => "df",
    };

    private static CharacterAttribute? AttributeNamed(string name)
    {
        foreach (var a in Enum.GetValues<CharacterAttribute>())
        {
            if (AttributeCode(a) == name || a.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }
        return null;
    }

    private static readonly Dictionary<string, Skill> SkillAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["edge"] = Skill.EdgedWeapons, ["impact"] = Skill.ImpactWeapons, ["flail"] = Skill.FlailWeapons,
        ["polearm"] = Skill.Polearms, ["thrown"] = Skill.ThrownWeapons, ["bow"] = Skill.Bows,
        ["missile"] = Skill.MissileDevices, ["latin"] = Skill.SpeakLatin,
    };

    private static Skill? SkillNamed(string name) =>
        SkillAliases.TryGetValue(name, out var alias) ? alias
        : !int.TryParse(name, out _) && Enum.TryParse<Skill>(name, ignoreCase: true, out var skill) ? skill
        : null;
}
