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
}
