using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Rules;

namespace Darklands.Simulation;

/// <summary>
/// Runs the game from card to card, as the game's main loop does: each card returns the
/// number of the next one. A number with no script (none in the game reaches one) goes to <see cref="UnknownCard"/>.
/// </summary>
// orig: the dispatch loop of root 03A1:000A matched: DS:A764 = (*R136+0xD090[DS:A772])(); tests: CardSmokeTests, TextAndOpeningTests
public sealed class CardDispatcher
{
    /// <summary>Card numbers with special meaning.</summary>
    // orig: 0xC is the overland map (the main loop clears DS:A891 after every other card), the loop ends when a screen returns 0x62 or -0x63 (while (DS:A764 != 0x62 && DS:A764 != -0x63))
    public const int OverlandMap = 0x0C;
    public const int EndOfGame = 0x62;
    public const int QuitGame = -0x63;

    private readonly CardRegistry _cards;

    /// <summary>The game's own cards only (the scripts of the assemblies, default Darklands.Cards): Classic.</summary>
    public CardDispatcher(params Assembly[] scriptAssemblies)
        : this(new CardRegistry(scriptAssemblies))
    {
    }

    /// <summary>The cards of a registry: the game's, and those mods registered on top of them (CardRegistry has the precedence).</summary>
    public CardDispatcher(CardRegistry cards)
    {
        _cards = cards;
    }

    /// <summary>The cards this dispatcher runs.</summary>
    public CardRegistry Cards => _cards;

    /// <summary>How many cards have scripts.</summary>
    public int ScriptedCount => _cards.Count;

    public bool HasScript(int number) => _cards.Contains(number);

    /// <summary>Runs one card and moves the game on to the next one.</summary>
    // orig: one pass of the do/while in root 03A1:000A, the part that is not a battle
    // C: the original runs the card only if bit 0 of DS:9081 is clear; when it is set (a game saved in a battle, GameState.ResumeBattle) the
    // card is skipped and the battle branch runs at once (DarklandsGame.Play).
    // C: DS:A88F = DS:A88D; DS:A88D = DS:A772; DS:A772 = DS:A764 — the previous card (DS:A88D, the previous-screen word) becomes PreviousCard here. The one before it (DS:A88F) is kept by the original, but no instruction of the EXE reads it (the root and all 136 overlays hold only its two stores, root 03A1 and R066+0x271F), so it is not kept here; test CardDispatcherTests.FinishPassMovesTheCardsDown.
    // C: "if (DS:A772 != 0xc) DS:A891 = 0": after any card but the map the original clears the map-leave mode word (DS:A891, GameState.MapLeaveMode, saved with the game); the map itself runs in DarklandsGame.Travel and keeps it.
    // C: thunk 09C0:190F(0), called after every pass, is advance_hours(0) (the vector resolves to R029+0x54D8 (1367:05C8) through the overlay thunk table): the clock housekeeping TimePassage.Advance(state, 0) stands for. In the battle branch it is called a second time after thunk 09C0:1919(0) (= cards_startup, which reloads the card engine): DarklandsGame.FightTheBattle makes that call.
    // C: a battle code (-2..-6) leaves the pass unfinished: the battle runs first (DS:A772 is still the card during it, a save in the
    // battle writes it) and FinishPass then moves on to the card the battle returned.
    public int RunCard(CardContext c)
    {
        var state = c.State;
        var number = state.CurrentCard;
        var card = _cards.Find(number) ?? new UnknownCard(number);
        var next = card.Run(c);
        if (!IsBattle(next))
        {
            FinishPass(state, next);
        }
        return next;
    }

    /// <summary>The end of a pass of the main loop: the card before is remembered and the next one (a card's or a battle's) becomes current.</summary>
    // orig: root 03A1:000A: after a card other than the map DS:A891 = 0; the cards before move down (DS:A88F, DS:A88D), DS:A772 takes
    // the next card (DS:A764), then the clock housekeeping through thunk 09C0:190F (with 0 hours)
    public static void FinishPass(Core.GameState state, int next)
    {
        if (state.CurrentCard != OverlandMap)
        {
            state.MapLeaveMode = 0;
        }
        state.PreviousCard = state.CurrentCard;
        state.CurrentCard = next;
        TimePassage.Advance(state, 0);
    }

    /// <summary>A battle starts when a card returns -2..-6.</summary>
    // orig: DS:A764 == -2 .. -6 in root 03A1:000A. -3 sets bit 4 of DS:9080, -5 bit 8, -6 bit 0x80, -4 bits 0xC (-2 none), then bit 1 for all; the battle is thunk 09C0:18BF(DS:A766..DS:A770), the battle screen (R014) is DarklandsGame.FightTheBattle
    // C: afterwards: members of status 3 (dead) become status 0 and are removed (thunk 09C0:18B5); every other present member that is not 5 (unconscious) is set back to 1 (able); the game is over if no member is left or DS:A893 == 5 (the "death" animation, then exit); otherwise thunk 09C0:1919(0) and the pass ends.
    // C: the battle itself, the post-battle clean-up and the game-over test above are DarklandsGame.FightTheBattle's (its divergence is noted there).
    public static bool IsBattle(int card) => card is <= -2 and >= -6;
}

/// <summary>
/// A card number without a script: shows its picture and first text, if the table knows it, and
/// returns to the card before, so a bad number cannot stop the game.
/// </summary>
// DIVERGENCE: no original: the original calls whatever R136+0xD090[number] holds; every card of the table has a script, so only a number outside it gets here.
public sealed class UnknownCard(int number) : Card
{
    public override int Number => number;

    public override int Run(CardContext c)
    {
        var info = CardTable.Find(number);
        if (info is not { } card)
        {
            c.Screen.Tell($"Card {number} is not in the card table.");
            return c.State.PreviousCard;
        }
        c.Screen.Show(card.Name, PictureNames.Of(card.Picture));
        c.Screen.Text(0);
        c.Screen.Tell($"{card.Name} has no script.");
        return c.State.PreviousCard;
    }
}
