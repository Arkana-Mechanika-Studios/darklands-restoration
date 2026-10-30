using Darklands.Cards;
using Darklands.Content;

namespace Darklands.Simulation.Mods;

/// <summary>
/// The mods a game runs with: each mod.json read (<see cref="ModFolder"/>), checked whole, its cards registered in a
/// <see cref="CardRegistry"/> in the order given, and their texts ready for the card screen. No mod: the registry holds the
/// game's cards only (Classic).
/// </summary>
/// <remarks>
/// A mod is played whole or not at all: any problem in it (a card that cannot be read, a card the registry refuses, a text name
/// another card or the game's files already use, a link to a card no script and no played mod defines) leaves the whole mod
/// out, with a line that says why. Leaving one out can break another mod that leads to its cards, so the check repeats until
/// every played mod is sound. A game with mods therefore never reaches a card number that nothing defines.
/// </remarks>
public sealed class ModSet
{
    private readonly List<ModFolder> _mods = [];
    private readonly List<ModFolder> _played = [];
    private readonly List<MessageFile> _texts = [];
    private readonly List<string> _log = [];

    /// <summary>The cards: the game's and the played mods'.</summary>
    public CardRegistry Cards { get; }

    /// <summary>The mods read, in load order (also those left out).</summary>
    public IReadOnlyList<ModFolder> Mods => _mods;

    /// <summary>The mods played (their cards registered), in load order.</summary>
    public IReadOnlyList<ModFolder> Played => _played;

    /// <summary>The message files of the registered data cards (the card screen finds a card's texts by its name).</summary>
    public IReadOnlyList<MessageFile> Texts => _texts;

    /// <summary>Every problem and registration line, in order (a host writes them to its console).</summary>
    public IReadOnlyList<string> Log => _log;

    private ModSet(CardRegistry cards) => Cards = cards;

    /// <summary>The game's cards only.</summary>
    public static ModSet None() => new(new CardRegistry());

    /// <summary>
    /// Reads the mods in order (their manifests as Content's ModFolders.Read gives them), leaves out every mod with a problem
    /// and registers the cards of the others (a later mod wins a number an earlier one took). <paramref name="isGameMessageFile"/>
    /// tells whether the game's files hold a message file of that name (a mod card's texts may not take one); <paramref name="log"/>
    /// receives each line as it is written.
    /// </summary>
    public static ModSet Load(IEnumerable<ModManifest> manifests, Action<string>? log = null, Func<string, bool>? isGameMessageFile = null)
    {
        var set = new ModSet(new CardRegistry());
        void Write(string line)
        {
            set._log.Add(line);
            log?.Invoke(line);
        }
        var candidates = new List<ModFolder>();
        foreach (var manifest in manifests)
        {
            var mod = ModFolder.From(manifest);
            set._mods.Add(mod);
            if (mod.Problems.Count > 0)
            {
                foreach (var problem in mod.Problems)
                {
                    Write(problem);
                }
                Write($"{mod.Source} is not played: {mod.Problems.Count} problem(s) above");
                continue;
            }
            if (mod.Cards.Count == 0)
            {
                Write($"{mod.Source} has no cards: nothing to play");
                continue;
            }
            candidates.Add(mod);
        }
        // the check, repeated until the mods kept are sound together (leaving one out can break another's links)
        while (true)
        {
            var probe = new CardRegistry(set.Cards.Original);
            var refused = new List<string>();
            probe.Logger = line =>
            {
                if (line.Contains(" refused: ", StringComparison.Ordinal))
                {
                    refused.Add(line);
                }
            };
            var bad = new Dictionary<ModFolder, List<string>>();
            void Bad(ModFolder mod, string why)
            {
                if (!bad.TryGetValue(mod, out var list))
                {
                    bad[mod] = list = [];
                }
                list.Add(why);
            }
            var names = new Dictionary<string, ModFolder>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in candidates)
            {
                foreach (var definition in mod.Cards)
                {
                    refused.Clear();
                    if (!probe.Register(Card(definition, probe), mod.Source, definition.Overrides, definition.SavesAs))
                    {
                        refused.ForEach(r => Bad(mod, r));
                    }
                    if (isGameMessageFile?.Invoke(definition.Name) == true)
                    {
                        Bad(mod, $"card {definition.Number}: the name \"{definition.Name}\" is a message file of the game; give the card a name of its own");
                    }
                    if (names.TryGetValue(definition.Name, out var other) && other != mod)
                    {
                        Bad(mod, $"card {definition.Number}: the name \"{definition.Name}\" is already a card name of {other.Source}");
                    }
                    names.TryAdd(definition.Name, mod);
                }
            }
            foreach (var mod in candidates)
            {
                foreach (var definition in mod.Cards)
                {
                    foreach (var to in Links(definition.Pages))
                    {
                        if (!probe.Contains(to) && to != CardDispatcher.OverlandMap && to != CardDispatcher.EndOfGame)
                        {
                            Bad(mod, $"card {definition.Number} leads to card {to}, which no script of the game and no played mod defines");
                        }
                    }
                }
            }
            if (bad.Count == 0)
            {
                break;
            }
            foreach (var (mod, why) in bad)
            {
                foreach (var line in why.Distinct())
                {
                    Write($"{mod.Source}: {line}");
                }
                Write($"{mod.Source} is not played: {why.Distinct().Count()} problem(s) above");
                candidates.Remove(mod);
            }
        }
        set.Cards.Logger = Write;
        foreach (var mod in candidates)
        {
            foreach (var definition in mod.Cards)
            {
                set.Cards.Register(Card(definition, set.Cards), mod.Source, definition.Overrides, definition.SavesAs);
                set._texts.Add(definition.Texts);
            }
            set._played.Add(mod);
        }
        return set;
    }

    private static DataCard Card(DataCardDefinition definition, CardRegistry registry) =>
        new(definition.Number, definition.Name, definition.Picture, definition.Pages, definition.Texts,
            definition.Overrides ? registry.OriginalOf(definition.Number) : null);

    /// <summary>The card numbers a card's pages lead to.</summary>
    private static IEnumerable<int> Links(IEnumerable<DataPage> pages) =>
        pages.SelectMany(p => p.Options.Select(o => o.Next).Concat(new[] { p.Next, p.Otherwise }.OfType<CardTarget>()))
            .OfType<CardTarget.Card>()
            .Select(c => c.Number)
            .Distinct();

    /// <summary>The played mods' marks for a saved game (id, name, version), one per mod with a registered card.</summary>
    public IReadOnlyList<Core.ModMark> Marks => Cards.Mods.Select(m => new Core.ModMark(m.Id, m.Name, m.Version)).ToList();
}
