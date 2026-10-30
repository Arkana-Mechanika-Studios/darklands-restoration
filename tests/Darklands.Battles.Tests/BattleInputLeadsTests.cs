using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Platform;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>The leads of round B1 (battle input) judged against the original (each test fails without its fix).</summary>
public sealed class BattleInputLeadsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// fighter-path-table (far 20A5:6238) and DS:D6FB are not cleared when a battle starts (R002+0x1830): zeros in a fresh program, else what the
    /// program holds (the last battle's or the last load's battle file). A member made to walk without a path given (Flee, then Space)
    /// takes those bytes as its path (approach R014+0x2CCA queues byte 0 = a step) and does not arrive at once (lockstep session gen-bheld-22).
    /// </summary>
    [Fact]
    public void AWalkWithoutAGivenPathFollowsThePathBytesTheProgramHolds()
    {
        Assert.All(new Combatant(0).Path, b => Assert.Equal(0, b));
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), state =>
        {
            var file = new BattleFile();
            file.Variables[BattleVariables.Paths + 0] = 3;
            file.Variables[BattleVariables.Paths + 1] = 0xFF;
            file.Variables[BattleVariables.Paths + Combatant.PathLength + 7] = 0x42;
            file.Variables[BattleVariables.PathIndex + 1] = 7;
            state.BattleFile = file;
        });
        Assert.Equal(3, t.Battle[0].Path[0]);
        Assert.Equal(0xFF, t.Battle[0].Path[1]);
        Assert.Equal(0x42, t.Battle[1].Path[7]);
        Assert.Equal(7, t.Battle[1].PathIndex);
        var c = t.Battle[0];
        c.PathIndex = 0;
        c.QueuedStep = -1;
        c.Moving = true;
        c.RepathAt = (ushort)(t.Battle.Clock + 100);
        typeof(TacticalBattle).GetMethod("Approach", Any)!.Invoke(t, [0]);
        Assert.True(c.Moving);
        Assert.Equal(3, c.QueuedStep);
    }

    /// <summary>
    /// DS:907B is the game's one leader byte: member-make-active R016+0x09BC (Ctrl-click, Shift-F) and fight-finished R014+0x08FE change the
    /// party's leader itself, which a save after the battle writes at head+0x3D (lockstep sessions gen-bheld-45, gen-bsafe-46 ...).
    /// </summary>
    [Fact]
    public void TheBattlesLeaderIsTheParty()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        Core.GameState? game = null;
        var (_, t) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]), s => game = s);
        Assert.Equal(game!.Party.LeaderIndex, t.Battle.Leader);
        var other = Enumerable.Range(0, Combatant.PartySlots).First(i => i != t.Battle.Leader && t.Battle[i].Status == Core.Characters.CharacterStatus.Able);
        t.ActivateMember(other);
        Assert.Equal(other, game.Party.LeaderIndex);
        game.Party.LeaderIndex = t.Battle.ActiveMember == 0 ? 1 : 0;
        Assert.Equal(game.Party.LeaderIndex, t.Battle.Leader);
    }

    /// <summary>
    /// difficulty-setting (DS:906A) is one byte: Alt-D in a battle (hotkey-table root 0000:0CAA) changes the difficulty the skill gains of the
    /// battle's end read (R014+0x0694, R021+0x0000: d4 at Standard, d5 at Expert; lockstep session gen-bkeys-05).
    /// </summary>
    [Fact]
    public void AltDInABattleChangesTheDifficultyTheBattleReads()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        Core.GameState? game = null;
        var (_, t) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]), s =>
        {
            game = s;
            s.Difficulty = Core.Difficulty.Standard;
        });
        Assert.Equal(1, t.Battle.Difficulty);
        new Presentation.HotKeys(new Presentation.GameUi(new BattleScreenTests.SilentHost(), new Content.GameAssets(GameFolder.Files)), game!, new Presentation.GameOptions()).NextDifficulty();
        Assert.Equal(Core.Difficulty.Expert, game!.Difficulty);
        Assert.Equal(2, t.Battle.Difficulty);
    }

    /// <summary>
    /// The battle's Game items start with their tick byte (R000+0x083E: "\x02Save Game", "\x02Load Saved Game", "\x01Show Changes" ...;
    /// the sub-menu "\x02Basic" ...), 6 pixels in font 1: the dropdown dropdown-open root 0000:07D4 opens is that much wider, so a
    /// release at its right edge (x 176 on the screen) is still on the item and not on its sub-menu (lockstep sessions gen-bkeys-32, -33).
    /// </summary>
    [Fact]
    public void TheBattlesGameItemsKeepTheirTicks()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, _) = BattleScreenTests.Start(GameFolder.Files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
        var bar = (Presentation.MenuBar)typeof(BattleScreen).GetField("_menuBar", Any)!.GetValue(screen)!;
        var game = bar.Menus[0].Items;
        Assert.All(game, i => Assert.Contains(i.Text()[0], "\x01\x02"));
        Assert.All(game[2].SubMenu!, s => Assert.Contains(s.Text()[0], "\x01\x02"));
        Assert.Equal(["\x02Save Game", "\x02Load Saved Game", "\x02" + "Difficulty"], game.Take(3).Select(i => i.Text()));
        var text = new Presentation.GameUi(new BattleScreenTests.SilentHost(), new Content.GameAssets(GameFolder.Files)).Text;
        // the widest text and key (85 and 19 pixels): the right edge x + 19 + 85 + 9
        Assert.Equal(85, game.Max(i => text.Measure(i.Text())));
        Assert.Equal(19, game.Max(i => text.Measure(i.Key)));
    }
}
