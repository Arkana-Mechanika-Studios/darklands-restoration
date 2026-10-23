using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Presentation;

/// <summary>
/// The full screens cards open, as far as written here: the shop (barter), the item store and
/// the loot screen. Every other screen is passed on to <paramref name="others"/>.
/// </summary>
public sealed class GameScreens : IGameScreens
{
    private readonly BuySellPage _page;
    private readonly GameState _state;
    private readonly IGameScreens _others;
    private readonly ItemStoreScreen _itemStore;

    public GameScreens(GameUi ui, PartyPanel panel, GameState state, IGameScreens others)
    {
        _page = new BuySellPage(ui, panel);
        _state = state;
        _others = others;
        _itemStore = new ItemStoreScreen(_page, state, this);
    }

    // orig: R029+0x21AA (0E76:21AA) (barter wrapper: name ?? R136+0xA70F[shop], then barter_screen_vec = thunk 09C0:1EFF → barter_screen R032+0x0000) draft
    // DIVERGENCE: the wrapper's card-picture redraw afterwards (thunk 09C0:1EA5) is the card screen's job here.
    public void Barter(int shop, string name, long categories) =>
        new BarterScreen(_page, _state, this, shop, name, categories).Run();

    // orig: item_cache_screen_vec = thunk 09C0:1ED7 → cache_screen R035+0x0000 (1462:0000) matched
    public void ItemStore() => _itemStore.Run();

    // orig: loot_distribute_vec = thunk 09C0:1DAB → loot_distribute R040+0x0000 (1462:0000) draft (byte-matched function)
    public void Loot(IList<ItemStack> items, Money money) => new LootScreen(_page, _state, items).Run(money);

    public void Camp(int mode) => _others.Camp(mode);

    public void Alchemy(int member, int place) => _others.Alchemy(member, place);

    /// <summary>
    /// The character sheet the barter, item store and loot screens open from the party column (the game passes the sheet of
    /// the screens that wrap these); null: the sheet of <c>others</c>.
    /// </summary>
    // orig: the party-column test of the barter loop R032+0x0000 and of the item store loop R035+0x0000 opens the sheet (R031+0x0124 in the call stack of the original)
    public Action<int>? Sheet { get; set; }

    public void CharacterSheet(int member)
    {
        if (Sheet is { } sheet)
        {
            sheet(member);
        }
        else
        {
            _others.CharacterSheet(member);
        }
    }

    public void PartyInfo() => _others.PartyInfo();

    public int Battle(BattleRequest request) => _others.Battle(request);
}
