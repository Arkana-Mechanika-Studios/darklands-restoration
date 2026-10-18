using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Items;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

/// <summary>Debug: opens one of the trading screens on a Quickstart game, to look at it without playing to it.</summary>
internal static class ScreenPreview
{
    public static void Run(IHost host, GameFiles files, string screen, uint seed)
    {
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, seed, () => seed);
        state.Data = world;
        state.Party.Purse = Money.From(12, 5, 3);
        var panel = new PartyPanel(ui.Assets);
        var screens = new GameScreens(ui, panel, state, new NoOtherScreens());
        try
        {
            switch (screen)
            {
                case "barter":
                    screens.Barter(1, null!, -1);
                    break;
                case "store":
                    var store = new List<StoredItem> { new(0x26, 20, 3), new(0x5F, 30, 1), new(3, 25, 1), new(40, 30, 2), new(0x10, 15, 1) };
                    state.Here!.CacheHandle = state.ItemStores.Add(store);
                    screens.ItemStore();
                    break;
                case "loot":
                    // the pile of an original capture (a Quickstart party after a street fight): 4 falchions, 4 leather and 4 clothing (the screen takes 10 off the quality), 0 fl 1 gr 6 pf
                    var items = new List<ItemStack>
                    {
                        new(4, state.Item(4).Type, 15, 4, (byte)state.Item(4).Weight),
                        new(0x26, state.Item(0x26).Type, 20, 4, (byte)state.Item(0x26).Weight),
                        new(0x2D, state.Item(0x2D).Type, 20, 4, (byte)state.Item(0x2D).Weight),
                    };
                    screens.Loot(items, Money.From(0, 1, 6));
                    break;
            }
        }
        catch (QuitGameException)
        {
        }
    }

    private sealed class NoOtherScreens : IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<ItemStack> items, Money money) { }
        public int Battle(BattleRequest request) => 0;
    }
}
