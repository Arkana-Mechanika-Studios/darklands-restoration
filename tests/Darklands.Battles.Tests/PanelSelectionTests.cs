using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The party panel's selection bytes DS:9064 (GameUi.PanelSelection) last the whole run: a battle starts from them, and a member left
/// selected when a battle is left (a game loaded from its menu bar in order mode) keeps its frame lit outside it (vis10 sessions
/// uncert-march-load / uncert-march-load-selected).
/// </summary>
public sealed class PanelSelectionTests
{
    private static byte[] Rows(GameUi ui) => ui.Canvas.Screen.Pixels.ToArray();

    [Fact]
    public void ASelectedRowIsDrawnLitOutsideABattle()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var panel = new PartyPanel(ui);
        panel.Draw(ui.Canvas, state.Party);
        var unlit = Rows(ui);
        ui.PanelSelection[0] = true;
        panel.Draw(ui.Canvas, state.Party);
        var lit = Rows(ui);
        bool RowDiffers(int row) => Enumerable.Range(row * PartyPanel.RowHeight, PartyPanel.RowHeight)
            .Any(y => Enumerable.Range(0, PartyPanel.Width).Any(x => unlit[y * 320 + x] != lit[y * 320 + x]));
        Assert.True(RowDiffers(0));
        Assert.False(RowDiffers(1));
        Assert.True(panel.Lit(0));
        Assert.False(new PartyPanel(ui.Assets).Lit(0));
    }

    [Fact]
    public void ABattleStartsFromTheSelectionBytes()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        ui.PanelSelection[2] = true;
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var screens = new BattleScreens(ui, () => state);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        Assert.True(screen.Tactics!.Battle[2].Selected);
    }

    /// <summary>
    /// command-mode DS:A6B3 is not cleared by a battle's set-up: a battle left in order mode starts the next in order mode with nobody chosen
    /// (DS:A6B2 = 0xFF) until the first tick's command-mode-begin (vis10 session ordercarry-load-battle: DKSAVE99's ambush, Alt-L, the
    /// ambush again: A6B3 = 1 at its fight-main-loop, the rows read dim).
    /// </summary>
    [Fact]
    public void ABattleStartsInTheOrderModeTheLastLeft()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        foreach (var on in new[] { true, false })
        {
            var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
            ui.PanelOrderMode = (on, 3);
            var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
            var screens = new BattleScreens(ui, () => state);
            var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
            screen.Start(new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
            Assert.Equal(on, screen.Tactics!.InOrderMode);
            Assert.Equal(-1, screen.Tactics.Selected);
        }
    }

    /// <summary>
    /// Order mode as a battle left it (DS:A6B3 = 1, chosen member DS:A6B2) draws the names of the other rows in colour 7 outside the battle too
    /// (vis10 session uncert-march-load: after Alt-L from the mine ambush, member 3 chosen, the map's rows 0..2 dim).
    /// </summary>
    [Fact]
    public void OrderModeLeftByABattleDimsTheOtherRows()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        Assert.True(state.Party.Members.Count >= 3);
        var leader = state.Party.LeaderIndex;
        var chosen = leader == 1 ? 2 : 1;
        var other = Enumerable.Range(0, 3).First(i => i != leader && i != chosen);
        var panel = new PartyPanel(ui);
        byte[] NameLine(int row)
        {
            var pixels = Rows(ui);
            return Enumerable.Range(row * PartyPanel.RowHeight + 2, 7)
                .SelectMany(y => Enumerable.Range(4, 50).Select(x => pixels[y * 320 + x])).ToArray();
        }
        panel.Draw(ui.Canvas, state.Party);
        Assert.Contains((byte)0x0F, NameLine(other));
        var before = NameLine(chosen);
        ui.PanelOrderMode = (true, chosen);
        panel.Draw(ui.Canvas, state.Party);
        Assert.DoesNotContain((byte)0x0F, NameLine(other));
        Assert.Contains((byte)0x07, NameLine(other));
        Assert.DoesNotContain((byte)0x0E, NameLine(leader));
        Assert.Equal(before, NameLine(chosen));
        // order mode with nobody chosen (0xFF) dims every row; off, nothing
        ui.PanelOrderMode = (true, -1);
        panel.Draw(ui.Canvas, state.Party);
        Assert.DoesNotContain((byte)0x0F, NameLine(chosen));
        ui.PanelOrderMode = (false, chosen);
        panel.Draw(ui.Canvas, state.Party);
        Assert.Contains((byte)0x0F, NameLine(other));
    }
}
