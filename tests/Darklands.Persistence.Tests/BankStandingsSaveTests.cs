using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;
using Darklands.Persistence;

namespace Darklands.Persistence.Tests;

/// <summary>
/// FixBankStandingsSaved (B-047d71, F-1 set 1b): the original saves no bank standing (a load keeps the running game's); the Fixed rules
/// keep the three in the header's spare words behind a marker, which the reader sets aside for the loaded game's rules.
/// </summary>
public sealed class BankStandingsSaveTests
{
    private static GameState Game(bool fixedGame)
    {
        var state = new GameState(new MscRandom(1234)) { Party = DefaultParty.Create(77, QuickstartTestNames.All), Date = new GameDate(9, 3, 4, 1401) };
        state.Roster.Reset(state.Party);
        state.Locations.Add(new Location { Index = 0, Name = "Koln", X = 10, Y = 20 });
        state.CurrentLocation = 0;
        state.Behaviour.FixBankStandingsSaved = fixedGame;
        (state.FuggerStanding, state.MediciStanding, state.HanseStanding) = (12, -3, 40);
        return state;
    }

    [Fact]
    public void TheOriginalWritesZerosThereAndReadsNothing()
    {
        var (sav, _) = OriginalSaves.Write(Game(false), "original");
        Assert.All(sav.AsSpan(0xA6, 0x48).ToArray(), b => Assert.Equal(0, b));
        Assert.Null(OriginalSaves.Read(sav, [], "DKSAVE0.SAV", NoGameData.Instance).State.SavedStandings);
    }

    [Fact]
    public void TheFixedRulesKeepTheStandingsBehindAMarker()
    {
        var (sav, _) = OriginalSaves.Write(Game(true), "fixed");
        Assert.Equal(0x42, sav[0xA7]);
        Assert.Equal(0x4B, sav[0xA8]);
        // the rest of the spare words stay zero, and nothing before them changes
        Assert.All(sav.AsSpan(0xAF, 0xEF - 0xAF).ToArray(), b => Assert.Equal(0, b));
        Assert.Equal(0, sav[0xA6]);
        var (plain, _) = OriginalSaves.Write(Game(false), "fixed");
        Assert.Equal(plain.AsSpan(0, 0xA7).ToArray(), sav.AsSpan(0, 0xA7).ToArray());
        Assert.Equal(plain.AsSpan(0xEF).ToArray(), sav.AsSpan(0xEF).ToArray());
        var loaded = OriginalSaves.Read(sav, [], "DKSAVE0.SAV", NoGameData.Instance, running: new GameState(new MscRandom(1))).State;
        Assert.Equal((12, -3, 40), loaded.SavedStandings);
        // the reader takes nothing itself: the running game's carry-over stays until the loaded game's rules are known
        Assert.Equal(0, loaded.FuggerStanding);
    }
}
