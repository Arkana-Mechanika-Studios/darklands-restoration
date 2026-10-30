using System.Text;
using System.Text.Json;
using Darklands.Cards;
using Darklands.Content;

namespace Darklands.Simulation.Mods;

/// <summary>One card of a mod.json, read and checked, not yet registered.</summary>
public sealed record DataCardDefinition(int Number, string Name, bool Overrides, int? SavesAs, string Picture, IReadOnlyList<DataPage> Pages, MessageFile Texts);

/// <summary>
/// A mod: a folder with a mod.json that names it and describes its cards (docs/MODS.md). Reading it never runs anything
/// from the folder: a mod is data (texts, choices and a few effects), not code.
/// </summary>
public sealed class ModFolder
{
    /// <summary>The file that makes a folder a mod.</summary>
    public const string ManifestName = "mod.json";

    /// <summary>The block layout a page gets unless it sets its own (the usual place of a card's first text).</summary>
    public const int DefaultX = 10, DefaultY = 10, DefaultWidth = 232;

    /// <summary>The largest amount of money an effect or a condition may name (1,000 florins).</summary>
    public const int MaxPfennigs = 1000 * 20 * 12;

    /// <summary>The longest time a single effect may pass.</summary>
    public const int MaxHours = 24 * 7;

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private ModFolder(string folder, CardSource source, string description, IReadOnlyList<DataCardDefinition> cards, IReadOnlyList<string> problems)
    {
        Folder = folder;
        Source = source;
        Description = description;
        Cards = cards;
        Problems = problems;
    }

    public string Folder { get; }

    /// <summary>The mod's id (its folder's name), name and version.</summary>
    public CardSource Source { get; }

    public string Description { get; }

    /// <summary>The cards that were read without a problem.</summary>
    public IReadOnlyList<DataCardDefinition> Cards { get; }

    /// <summary>What could not be used, one line each (a card with a problem is left out whole).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>True when a mod.json is in the folder.</summary>
    public static bool IsMod(string folder) => File.Exists(Path.Combine(folder, ManifestName));

    /// <summary>Reads a mod folder. A missing folder or mod.json, or JSON that cannot be read, gives a mod with no cards and the problem.</summary>
    public static ModFolder Load(string folder)
    {
        var full = Path.GetFullPath(folder);
        var id = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var manifest = Path.Combine(full, ManifestName);
        if (!File.Exists(manifest))
        {
            return new ModFolder(full, new CardSource(id, id, ""), "", [], [$"{full}: no {ManifestName} (not a mod folder)"]);
        }
        try
        {
            return Parse(full, id, File.ReadAllText(manifest));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ModFolder(full, new CardSource(id, id, ""), "", [], [$"{manifest}: cannot be read: {e.Message}"]);
        }
    }

    /// <summary>Reads a mod.json's text (the folder gives the mod's id).</summary>
    public static ModFolder Parse(string folder, string id, string json)
    {
        var problems = new List<string>();
        using var document = JsonDocument.Parse(json, Options);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ModFolder(folder, new CardSource(id, id, ""), "", [], [$"{id}: {ManifestName} must be an object"]);
        }
        var name = Text(root, "name") ?? id;
        var version = Text(root, "version") ?? "";
        var description = Text(root, "description") ?? "";
        var cards = new List<DataCardDefinition>();
        if (root.TryGetProperty("cards", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in list.EnumerateArray())
            {
                var reader = new CardReader(id, index++);
                if (reader.Read(element) is { } card)
                {
                    if (cards.Any(c => c.Number == card.Number))
                    {
                        problems.Add($"{id}: card {card.Number} is described twice; the second is left out");
                    }
                    else
                    {
                        cards.Add(card);
                    }
                }
                problems.AddRange(reader.Problems);
            }
        }
        else
        {
            problems.Add($"{id}: {ManifestName} has no \"cards\" list");
        }
        return new ModFolder(folder, new CardSource(id, name, version), description, cards, problems);
    }

    private static string? Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reads one card, collecting its problems; a card with any problem is not returned.</summary>
    private sealed class CardReader(string mod, int index)
    {
        public List<string> Problems { get; } = [];

        private string _where = $"{mod}: card #{index + 1}";

        public DataCardDefinition? Read(JsonElement card)
        {
            if (card.ValueKind != JsonValueKind.Object)
            {
                return Fail("must be an object");
            }
            if (!card.TryGetProperty("number", out var n) || !n.TryGetInt32(out var number))
            {
                return Fail("needs a whole \"number\"");
            }
            _where = $"{mod}: card {number}";
            var overrides = card.TryGetProperty("overrides", out var o) && o.ValueKind == JsonValueKind.True;
            int? savesAs = card.TryGetProperty("savesAs", out var s) && s.TryGetInt32(out var saved) ? saved : null;
            var name = Text(card, "name") ?? $"$Mod{number}";
            if (name.Length == 0 || name.Length > 12 || name.Any(ch => ch is < '!' or > '~'))
            {
                return Fail($"\"name\" \"{name}\" must be 1 to 12 printable characters without spaces");
            }
            if (CardTable.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return Fail($"\"name\" \"{name}\" is the name of one of the game's cards; give the mod card its own");
            }
            var picture = card.TryGetProperty("picture", out var p) ? p.ValueKind switch
            {
                JsonValueKind.Number when p.TryGetInt32(out var pn) => PictureNames.Of(pn),
                JsonValueKind.String => p.GetString() ?? "",
                _ => null,
            } : overrides && number is >= 0 and <= CardRegistry.LastOriginalCard ? PictureNames.Of(CardTable.Find(number)?.Picture ?? -1) : "";
            if (picture is null)
            {
                return Fail("\"picture\" must be a picture number or a picture name");
            }
            if (!card.TryGetProperty("pages", out var pageList) || pageList.ValueKind != JsonValueKind.Array || pageList.GetArrayLength() == 0)
            {
                return Fail("needs a \"pages\" list with at least one page");
            }
            var blocks = new List<MessageBlock>();
            var pages = new List<DataPage>();
            var pageIndex = 0;
            foreach (var page in pageList.EnumerateArray())
            {
                if (ReadPage(page, pageIndex++, overrides, blocks) is not { } read)
                {
                    return null;
                }
                if (pages.Any(x => x.Id == read.Id))
                {
                    return Fail($"two pages are called \"{read.Id}\"");
                }
                pages.Add(read);
            }
            foreach (var target in pages.SelectMany(Targets))
            {
                if (target is CardTarget.Page to && pages.All(x => x.Id != to.Id))
                {
                    return Fail($"a page leads to \"{to.Id}\", which is not a page of this card (nor original or previous)");
                }
            }
            return new DataCardDefinition(number, name, overrides, savesAs, picture, pages, new MessageFile(name, blocks));
        }

        private static IEnumerable<CardTarget> Targets(DataPage page) =>
            page.Options.Select(o => o.Next).Concat(new[] { page.Next, page.Otherwise }.OfType<CardTarget>());

        private DataPage? ReadPage(JsonElement page, int index, bool overrides, List<MessageBlock> blocks)
        {
            var where = $"page {index + 1}";
            if (page.ValueKind != JsonValueKind.Object)
            {
                return Fail<DataPage>($"{where} must be an object");
            }
            var id = Text(page, "id") ?? $"page{index + 1}";
            where = $"page \"{id}\"";
            var effects = ReadEffects(page, where);
            if (effects is null)
            {
                return null;
            }
            if (page.TryGetProperty("ifPreviousCard", out var beforeValue))
            {
                var before = new List<int>();
                foreach (var item in beforeValue.ValueKind == JsonValueKind.Array ? beforeValue.EnumerateArray().ToList() : [beforeValue])
                {
                    if (!item.TryGetInt32(out var number) || number < 0)
                    {
                        return Fail<DataPage>($"{where}: \"ifPreviousCard\" must be a card number or a list of them");
                    }
                    before.Add(number);
                }
                if (page.TryGetProperty("text", out _) || page.TryGetProperty("options", out _) || page.TryGetProperty("chance", out _))
                {
                    return Fail<DataPage>($"{where}: an ifPreviousCard page has no text, options or chance (it only chooses where to go)");
                }
                var yes = ReadTarget(page, "next", where, overrides);
                var no = ReadTarget(page, "otherwise", where, overrides);
                return yes is null || no is null ? null : new DataPage(id, -1, null, [], effects, yes, no) { PreviousCards = before };
            }
            if (page.TryGetProperty("chance", out var chanceValue))
            {
                if (!chanceValue.TryGetInt32(out var chance) || chance is < 0 or > 100)
                {
                    return Fail<DataPage>($"{where}: \"chance\" must be a whole number from 0 to 100");
                }
                if (page.TryGetProperty("text", out _) || page.TryGetProperty("options", out _))
                {
                    return Fail<DataPage>($"{where}: a chance page has no text or options (it only chooses where to go)");
                }
                var yes = ReadTarget(page, "next", where, overrides);
                var no = ReadTarget(page, "otherwise", where, overrides);
                return yes is null || no is null ? null : new DataPage(id, -1, chance, [], effects, yes, no);
            }
            var text = Text(page, "text");
            var options = new List<DataOption>();
            if (page.TryGetProperty("options", out var optionList))
            {
                if (optionList.ValueKind != JsonValueKind.Array || optionList.GetArrayLength() == 0 || optionList.GetArrayLength() > 20)
                {
                    return Fail<DataPage>($"{where}: \"options\" must be a list of 1 to 20 options");
                }
                if (text is null)
                {
                    return Fail<DataPage>($"{where}: a page with options needs a \"text\"");
                }
                var number = 0;
                foreach (var option in optionList.EnumerateArray())
                {
                    if (ReadOption(option, $"{where}, option {++number}", overrides) is not { } read)
                    {
                        return null;
                    }
                    options.Add(read);
                }
                if (options.All(x => x.RequiresPfennigs > 0))
                {
                    return Fail<DataPage>($"{where}: at least one option must have no \"if\" (else the party could be left with nothing to choose)");
                }
            }
            var block = -1;
            if (text is not null)
            {
                if (Bytes(text, where) is not { } bytes)
                {
                    return null;
                }
                var all = new List<byte>(bytes);
                foreach (var option in options)
                {
                    if (Bytes(option.Text, where) is not { } line)
                    {
                        return null;
                    }
                    all.Add((byte)'\n');
                    all.Add(0x15);
                    all.AddRange(line);
                }
                var x = Whole(page, "x", DefaultX, 0, 300, where);
                var y = Whole(page, "y", DefaultY, 0, 190, where);
                var width = Whole(page, "width", DefaultWidth, 40, 260, where);
                if (x is null || y is null || width is null)
                {
                    return null;
                }
                block = blocks.Count;
                blocks.Add(new MessageBlock(x.Value, y.Value, width.Value, all.ToArray()));
            }
            CardTarget? next = null;
            if (options.Count == 0)
            {
                next = ReadTarget(page, "next", where, overrides);
                if (next is null)
                {
                    return null;
                }
            }
            return new DataPage(id, block, null, options, effects, next, null);
        }

        private DataOption? ReadOption(JsonElement option, string where, bool overrides)
        {
            if (option.ValueKind != JsonValueKind.Object || Text(option, "text") is not { } text)
            {
                return Fail<DataOption>($"{where} needs a \"text\"");
            }
            var requires = 0;
            if (option.TryGetProperty("if", out var condition))
            {
                if (Pfennigs(condition, $"{where}, \"if\"") is not { } need || need <= 0)
                {
                    return Fail<DataOption>($"{where}: \"if\" names the money the party must have, e.g. {{ \"groschen\": 1 }}");
                }
                requires = need;
            }
            var effects = ReadEffects(option, where);
            var next = ReadTarget(option, "next", where, overrides);
            return effects is null || next is null ? null : new DataOption(text, requires, effects, next);
        }

        private CardEffects? ReadEffects(JsonElement holder, string where)
        {
            if (!holder.TryGetProperty("do", out var effects))
            {
                return CardEffects.None;
            }
            if (effects.ValueKind != JsonValueKind.Object)
            {
                return Fail<CardEffects>($"{where}: \"do\" must be an object");
            }
            foreach (var property in effects.EnumerateObject())
            {
                if (property.Name is not ("florins" or "groschen" or "pfennigs" or "hours"))
                {
                    return Fail<CardEffects>($"{where}: \"do\" knows florins, groschen, pfennigs and hours, not \"{property.Name}\"");
                }
            }
            var money = Pfennigs(effects, $"{where}, \"do\"");
            var hours = Whole(effects, "hours", 0, 0, MaxHours, where);
            return money is null || hours is null ? null : new CardEffects(money.Value, hours.Value);
        }

        /// <summary>florins, groschen and pfennigs of an object as one sum of pfennigs (each may be negative).</summary>
        private int? Pfennigs(JsonElement holder, string where)
        {
            if (holder.ValueKind != JsonValueKind.Object)
            {
                return FailNumber($"{where} must be an object");
            }
            long total = 0;
            foreach (var (key, worth) in new[] { ("florins", 240), ("groschen", 12), ("pfennigs", 1) })
            {
                if (holder.TryGetProperty(key, out var v))
                {
                    if (!v.TryGetInt32(out var count))
                    {
                        return FailNumber($"{where}: \"{key}\" must be a whole number");
                    }
                    total += (long)count * worth;
                }
            }
            if (Math.Abs(total) > MaxPfennigs)
            {
                return FailNumber($"{where}: at most 1000 florins");
            }
            return (int)total;
        }

        private CardTarget? ReadTarget(JsonElement holder, string key, string where, bool overrides)
        {
            if (!holder.TryGetProperty(key, out var v))
            {
                return Fail<CardTarget>($"{where} needs \"{key}\": a page id, a card number, \"original\" or \"previous\"");
            }
            if (v.ValueKind == JsonValueKind.Number)
            {
                return v.TryGetInt32(out var card) && card is >= 0 and <= CardRegistry.LastModCard
                    ? new CardTarget.Card(card)
                    : Fail<CardTarget>($"{where}: \"{key}\" card numbers are 0 to {CardRegistry.LastModCard}");
            }
            if (v.ValueKind != JsonValueKind.String || v.GetString() is not { Length: > 0 } word)
            {
                return Fail<CardTarget>($"{where}: \"{key}\" must be a page id, a card number, \"original\" or \"previous\"");
            }
            return word switch
            {
                "original" when !overrides => Fail<CardTarget>($"{where}: \"original\" is only for a card that overrides one of the game's"),
                "original" => new CardTarget.Original(),
                "previous" => new CardTarget.Previous(),
                _ => new CardTarget.Page(word),
            };
        }

        private int? Whole(JsonElement holder, string key, int fallback, int low, int high, string where)
        {
            if (!holder.TryGetProperty(key, out var v))
            {
                return fallback;
            }
            return v.TryGetInt32(out var value) && value >= low && value <= high ? value : FailNumber($"{where}: \"{key}\" must be a whole number from {low} to {high}");
        }

        /// <summary>A text as the card font's bytes: printable ASCII and line breaks; $Name inserts a value as in the game's texts.</summary>
        private byte[]? Bytes(string text, string where)
        {
            var at = text.AsSpan().IndexOfAnyExceptInRange(' ', '~');
            while (at >= 0 && text[at] == '\n')
            {
                var rest = text.AsSpan(at + 1).IndexOfAnyExceptInRange(' ', '~');
                at = rest < 0 ? -1 : at + 1 + rest;
            }
            if (at >= 0)
            {
                return Fail<byte[]>($"{where}: the text may hold printable ASCII and line breaks only, not U+{(int)text[at]:X4}");
            }
            return Encoding.ASCII.GetBytes(text);
        }

        private T? Fail<T>(string why) where T : class
        {
            Problems.Add($"{_where}: {why}; the card is left out");
            return null;
        }

        private DataCardDefinition? Fail(string why) => Fail<DataCardDefinition>(why);

        private int? FailNumber(string why)
        {
            Problems.Add($"{_where}: {why}; the card is left out");
            return null;
        }
    }
}
