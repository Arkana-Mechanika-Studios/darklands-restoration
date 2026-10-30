using Darklands.Cards;
using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Simulation;

namespace Darklands.Cards.Tests;

/// <summary>Tests behind the justified not-modelled divergences of the card engine (they need the game files and pass vacuously without).</summary>
public sealed class NotModelledEvidenceTests
{
    // root 03A1:000A after a card: DS:A88F = DS:A88D; DS:A88D = DS:A772; DS:A772 = DS:A764. DS:A88F has no reader in the EXE, so the
    // card before the previous one is not kept; what a card wrote into the previous-card word (SneakAshore: the street card) is overwritten.
    [Fact]
    public void FinishPassMovesTheCardsDown()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        state.CurrentCard = 66;
        state.PreviousCard = 9;   // as ArrivalByBoat.SneakAshore leaves DS:A88D
        CardDispatcher.FinishPass(state, 1);
        Assert.Equal(66, state.PreviousCard);
        Assert.Equal(1, state.CurrentCard);
    }

    // call-on-saint R029+0x2180: the prayer screen is wired by the presentation; the headless context fails the prayer (0)
    [Fact]
    public void InvokeSaintNeedsThePrayerScreen()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var context = new CardContext(CardHarness.NewGame(), CardHarness.World!, new ScriptedScreen(-1));
        Assert.Equal(0, context.InvokeSaint(0, 3));
        context.SaintPrayer = (who, saint) => who * 100 + saint;
        Assert.Equal(3, context.InvokeSaint(0, 3));
        Assert.Equal(103, context.InvokeSaint(1, 3));
    }

    // text-bank-read R043+0x0A9A refuses a catalogue entry of 6000 bytes or more (message box, then quit): no shipped message file is that large
    [Fact]
    public void NoMessageFileReachesMsgLoadsSizeLimit()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var archive = new MsgFilesArchiveDecoder().Decode(new GameFiles(folder).Read("MSGFILES"));
        Assert.NotEmpty(archive.Entries);
        Assert.All(archive.Entries, e => Assert.True(e.Size < 6000, $"{e.Name} is {e.Size} bytes"));
    }

    // CardTable is complete: every card number of the table has a script (so CardDispatcher's UnknownCard is only reached by a number outside it)
    [Fact]
    public void EveryCardOfTheTableHasAScript()
    {
        var dispatcher = new CardDispatcher();
        foreach (var card in CardTable.All.Where(c => c.Name.Length > 0))
        {
            Assert.True(dispatcher.HasScript(card.Number), $"card {card.Number} ({card.Name})");
        }
    }
}
