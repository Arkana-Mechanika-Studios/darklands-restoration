using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Economy;

namespace Darklands.Simulation.Mods;

/// <summary>Where a page or an option leads: another page of the card, a card number, the overridden game card, or the card before.</summary>
public abstract record CardTarget
{
    /// <summary>Another page of the same card.</summary>
    public sealed record Page(string Id) : CardTarget;

    /// <summary>A card (the game's or a mod's) by number; 12 is the map.</summary>
    public sealed record Card(int Number) : CardTarget;

    /// <summary>The game's own card this card overrides, run as if the mod were not there.</summary>
    public sealed record Original : CardTarget;

    /// <summary>The card shown before this one.</summary>
    public sealed record Previous : CardTarget;
}

/// <summary>What a page or an option changes: money (pfennigs; negative pays) and hours passing.</summary>
public sealed record CardEffects(int Pfennigs = 0, int Hours = 0)
{
    public static readonly CardEffects None = new();
}

/// <summary>An option line of a page: its text, the money the party must have to choose it, what it does and where it leads.</summary>
public sealed record DataOption(string Text, int RequiresPfennigs, CardEffects Effects, CardTarget Next);

/// <summary>
/// A page of a data card: a branch (no text: by chance, or by the card shown before), a text the player reads, or a text with
/// option lines. <see cref="Block"/> is the page's text block in the card's message file (-1 for a page without text).
/// </summary>
/// <param name="Chance">A chance branch: <see cref="Next"/> with this chance in 100 (the game's generator), else <see cref="Otherwise"/>.</param>
/// <param name="PreviousCards">A branch on the card before: <see cref="Next"/> when it is one of these, else <see cref="Otherwise"/>.</param>
public sealed record DataPage(string Id, int Block, int? Chance, IReadOnlyList<DataOption> Options, CardEffects Effects, CardTarget? Next, CardTarget? Otherwise)
{
    public IReadOnlyList<int>? PreviousCards { get; init; }
}

/// <summary>
/// A card a mod describes in its mod.json instead of code: a picture, pages of text with option lines, a few effects
/// (money, hours) and where each choice leads. It runs on the same card screen as the game's cards.
/// </summary>
/// <remarks>
/// New code (a mod card, no original). It uses the game's random generator for its chance pages, so a game with the mod
/// draws differently from Classic: it is a modded game (its saves get a companion file).
/// </remarks>
// orig: stands where a card function of the table R136+0xD090[] would, called by the dispatch loop of root 03A1:000A (CardDispatcher); no card of the game is one
public sealed class DataCard(int number, string name, string picture, IReadOnlyList<DataPage> pages, MessageFile texts, Card? original) : Card
{
    /// <summary>How many pages a card may go through in one run before it is taken as a loop (a page leading to itself by chance).</summary>
    public const int MaxSteps = 1000;

    public override int Number => number;

    /// <summary>The card's message-file name ("$Shrin00"): its texts are found by it.</summary>
    public string MessageName => name;

    /// <summary>The card's texts, one block per page with text.</summary>
    public MessageFile Texts => texts;

    public IReadOnlyList<DataPage> Pages => pages;

    /// <summary>The game's card this one overrides (null for a new card).</summary>
    public Card? OriginalCard => original;

    public override int Run(CardContext c)
    {
        var shown = false;
        var page = pages[0];
        // DIVERGENCE: no original: a mod's card (docs/MODS.md, CardRegistry); the game's table holds none, Classic never runs one.
        for (var step = 0; step < MaxSteps; step++)
        {
            CardTarget next;
            if (page.PreviousCards is { } before)
            {
                next = before.Contains(c.State.PreviousCard) ? page.Next! : page.Otherwise!;
            }
            else if (page.Chance is { } chance)
            {
                next = c.Random(100) < chance ? page.Next! : page.Otherwise!;
            }
            else
            {
                if (!shown && page.Block >= 0)
                {
                    c.Screen.Show(name, picture);
                    shown = true;
                }
                if (page.Options.Count > 0)
                {
                    var states = page.Options
                        .Select(o => o.RequiresPfennigs <= 0 || c.State.Party.Purse.TotalPfennigs >= o.RequiresPfennigs ? OptionState.Enabled : OptionState.Shown)
                        .ToList();
                    var chosen = page.Options[Math.Clamp(c.Screen.Choose(page.Block, states), 0, page.Options.Count - 1)];
                    Apply(c, page.Effects);
                    Apply(c, chosen.Effects);
                    next = chosen.Next;
                }
                else
                {
                    if (page.Block >= 0)
                    {
                        c.Screen.Text(page.Block);
                    }
                    Apply(c, page.Effects);
                    next = page.Next!;
                }
            }
            switch (next)
            {
                case CardTarget.Page p:
                    page = pages.First(x => x.Id == p.Id);
                    continue;
                case CardTarget.Card card:
                    return card.Number;
                case CardTarget.Original:
                    return original is null ? c.State.PreviousCard : original.Run(c);
                default:
                    return c.State.PreviousCard;
            }
        }
        // DIVERGENCE: no original: a mod card whose pages lead round in a circle (chance pages only) is ended here.
        c.Screen.Tell($"{name} goes round in circles: back to the card before.");
        return c.State.PreviousCard;
    }

    private static void Apply(CardContext c, CardEffects effects)
    {
        if (effects.Pfennigs > 0)
        {
            c.State.Party.Purse = c.State.Party.Purse.Add(Amount(effects.Pfennigs));
        }
        else if (effects.Pfennigs < 0)
        {
            var cost = Amount(-effects.Pfennigs);
            c.State.Party.Purse = c.State.Party.Purse >= cost ? c.State.Party.Purse.Subtract(cost) : Money.Zero;
        }
        if (effects.Hours > 0)
        {
            c.PassHours(effects.Hours);
        }
    }

    /// <summary>A sum of pfennigs as florins, groschen and pfennigs (ModFolder keeps it within 1,000 florins).</summary>
    private static Money Amount(int pfennigs)
    {
        const int perFlorin = Money.GroschenPerFlorin * Money.PfennigsPerGroschen;
        return Money.From(pfennigs / perFlorin, pfennigs % perFlorin / Money.PfennigsPerGroschen, pfennigs % Money.PfennigsPerGroschen);
    }
}
