using Darklands.Audio;
using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

/// <summary>Opens the sheet, party information, camp or alchemy screen directly (a debugging aid).</summary>
internal static class DebugScreens
{
    public static void Run(IHost host, GameFiles files, string which, uint seed, string? languagePack = null)
    {
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        if (languagePack is not null)
        {
            // a translation pack: the screen's words (and names) in its language
            Darklands.Presentation.FontPack.ReadImage = Darklands.Rendering.PngCodec.ReadAlpha;
            Darklands.Presentation.FontPack.LoadScalable = json => (Darklands.Rendering.TtfFont.Load(json, out var why), why);
            ui.UseLanguagePack(languagePack, Console.Error.WriteLine);
        }
        var state = Darklands.Simulation.NewGame.Quickstart(world, seed);
        state.Data = world;
        var panel = new PartyPanel(ui.Assets);
        var screens = new GameScreens2(ui, panel, state, world, new NoOtherScreens());
        try
        {
            switch (which)
            {
                case "sheet": screens.CharacterSheet(0); break;
                case "party": screens.PartyInfo(); break;
                case ['a', 'l', ..]: screens.Alchemy(which.Length > 7 ? which[7] - '0' : 0, 0); break;
                default: screens.Camp(which.Length > 4 ? which[4] - '0' : 0); break;
            }
        }
        catch (QuitGameException)
        {
        }
    }

    private sealed class NoOtherScreens : Darklands.Cards.IGameScreens
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
}
