using Darklands.Cards;
using Darklands.Content;

namespace Darklands.Simulation.Mods;

/// <summary>
/// The mods a game runs with: each folder read (<see cref="ModFolder"/>), its cards registered in a <see cref="CardRegistry"/>
/// in the order given, and their texts ready for the card screen. No mod: the registry holds the game's cards only (Classic).
/// </summary>
public sealed class ModSet
{
    private readonly List<ModFolder> _mods = [];
    private readonly List<MessageFile> _texts = [];

    /// <summary>The cards: the game's and the mods'.</summary>
    public CardRegistry Cards { get; }

    /// <summary>The mods read, in load order (also those whose cards were all refused).</summary>
    public IReadOnlyList<ModFolder> Mods => _mods;

    /// <summary>The message files of the registered data cards (the card screen finds a card's texts by its name).</summary>
    public IReadOnlyList<MessageFile> Texts => _texts;

    /// <summary>Every problem and registration line, in order (a host writes them to its console).</summary>
    public IReadOnlyList<string> Log => _log;

    private readonly List<string> _log = [];

    private ModSet(CardRegistry cards) => Cards = cards;

    /// <summary>The game's cards only.</summary>
    public static ModSet None() => new(new CardRegistry());

    /// <summary>
    /// Reads the mod folders in order and registers their cards (a later mod wins a number an earlier one took).
    /// <paramref name="log"/> receives each line as it is written.
    /// </summary>
    public static ModSet Load(IEnumerable<string> folders, Action<string>? log = null)
    {
        var set = new ModSet(new CardRegistry());
        void Write(string line)
        {
            set._log.Add(line);
            log?.Invoke(line);
        }
        set.Cards.Logger = Write;
        foreach (var folder in folders)
        {
            var mod = ModFolder.Load(folder);
            set._mods.Add(mod);
            foreach (var problem in mod.Problems)
            {
                Write(problem);
            }
            foreach (var definition in mod.Cards)
            {
                var original = definition.Overrides ? set.Cards.OriginalOf(definition.Number) : null;
                var card = new DataCard(definition.Number, definition.Name, definition.Picture, definition.Pages, definition.Texts, original);
                if (set.Cards.Register(card, mod.Source, definition.Overrides, definition.SavesAs))
                {
                    set._texts.RemoveAll(t => string.Equals(t.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
                    set._texts.Add(definition.Texts);
                }
            }
        }
        foreach (var (number, registered) in set.Cards.ModCards.OrderBy(m => m.Key))
        {
            if (registered.Card is not DataCard data)
            {
                continue;
            }
            foreach (var target in data.Pages.SelectMany(p => p.Options.Select(o => o.Next).Concat(new[] { p.Next, p.Otherwise }.OfType<CardTarget>())))
            {
                if (target is CardTarget.Card { Number: var to } && !set.Cards.Contains(to) && to != CardDispatcher.OverlandMap && to != CardDispatcher.EndOfGame)
                {
                    Write($"card {number} of {registered.Source} leads to card {to}, which no script or mod defines (the game would show \"not in the card table\")");
                }
            }
        }
        return set;
    }

    /// <summary>
    /// The folder of a mod named on the command line or in the settings: an existing folder as given (relative to
    /// <paramref name="baseFolder"/> when not rooted), else a folder of that name in a <c>mods</c> folder next to the
    /// program, in the game folder or next to the game folder. Null when none holds a mod.json.
    /// </summary>
    public static string? Find(string nameOrFolder, string? gameFolder, string? baseFolder = null)
    {
        var given = Path.IsPathRooted(nameOrFolder) || baseFolder is null ? nameOrFolder : Path.Combine(baseFolder, nameOrFolder);
        if (ModFolder.IsMod(given))
        {
            return Path.GetFullPath(given);
        }
        foreach (var root in SearchFolders(gameFolder))
        {
            var candidate = Path.Combine(root, nameOrFolder);
            if (ModFolder.IsMod(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    /// <summary>The <c>mods</c> folders looked in, in order: next to the program, in the game folder, next to the game folder.</summary>
    public static IReadOnlyList<string> SearchFolders(string? gameFolder)
    {
        var roots = new List<string> { Path.Combine(AppContext.BaseDirectory, "mods") };
        if (!string.IsNullOrEmpty(gameFolder))
        {
            roots.Add(Path.Combine(gameFolder, "mods"));
            if (Path.GetDirectoryName(Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { } above)
            {
                roots.Add(Path.Combine(above, "mods"));
            }
        }
        return roots;
    }

    /// <summary>The mods' marks for a saved game (id, name, version), one per mod with a registered card.</summary>
    public IReadOnlyList<Core.ModMark> Marks => Cards.Mods.Select(m => new Core.ModMark(m.Id, m.Name, m.Version)).ToList();
}
