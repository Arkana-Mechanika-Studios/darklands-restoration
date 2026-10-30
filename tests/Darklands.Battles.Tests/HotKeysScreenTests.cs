using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>F1..F5 and Ctrl-F belong to the card engine and the map: a full screen gets them itself (HotKeys.ScreensUp).</summary>
public class HotKeysScreenTests
{
    [Fact]
    public void TheFunctionKeysAreLeftToAFullScreen()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "wait:2");
        if (MusicTests.Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        var shown = new List<int>();
        var keys = new HotKeys(ui, state, ui.Options) { CharacterSheet = shown.Add };
        Assert.False(keys.Handle(Keys.F1 + 0x100));         // a loop that is not the card engine's or the map's: F2 is the screen's
        Assert.False(keys.Handle(Keys.CtrlF1 + 0x100));
        Assert.Empty(shown);
        ui.SheetKeys = true;                                 // the card engine's wait (press-to-continue, card-choice-menu: card-hotkeys)
        Assert.True(keys.Handle(Keys.F1 + 0x100));          // on a card: F2 opens member 1's sheet
        Assert.Equal([1], shown);
        keys.ScreensUp++;                                    // the camp, the sheet, barter ...: their own loops take F2 and Ctrl-F2
        Assert.False(keys.Handle(Keys.F1 + 0x100));
        Assert.False(keys.Handle(Keys.CtrlF1 + 0x100));
        Assert.Equal([1], shown);
        keys.ScreensUp--;
        Assert.True(keys.Handle(Keys.F1));
        Assert.Equal([1, 0], shown);
    }

    /// <summary>The barter, item store and loot screens open the game's sheet from their party column (R032+0x0000, R035+0x0000).</summary>
    [Fact]
    public void TheShopScreensOpenTheGamesSheet()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "wait:2");
        if (MusicTests.Start(host) is not var (ui, state, panel, _))
        {
            return;
        }
        var shown = new List<int>();
        var shops = new GameScreens(ui, panel, state, new NoScreens()) { Sheet = shown.Add };
        shops.CharacterSheet(2);
        Assert.Equal([2], shown);
    }

    private sealed class NoScreens : Darklands.Cards.IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
        public int Battle(Darklands.Cards.BattleRequest request) => 0;
    }
    /// <summary>The screen a hot key opens has its own loops: F1..F5 are not the hot keys' there (GameUi.SheetKeys off inside).</summary>
    [Fact]
    public void TheSheetKeysAreOffInsideTheScreenAHotKeyOpens()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "wait:2");
        if (MusicTests.Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        var inside = new List<bool>();
        var keys = new HotKeys(ui, state, ui.Options) { CharacterSheet = _ => inside.Add(ui.SheetKeys) };
        ui.SheetKeys = true;
        Assert.True(keys.Handle(Keys.F1));
        Assert.Equal([false], inside);
        Assert.True(ui.SheetKeys);
    }
}
