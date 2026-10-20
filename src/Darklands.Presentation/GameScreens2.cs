using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;

namespace Darklands.Presentation;

// orig: the screens of overlays R031 (sheet), R034 (party information), R036 (camp) and R033 (alchemy), opened by cards through R029+0x2204 (alchemy) and R029+0x222A (camp)
// DIVERGENCE: the original redraws the card afterwards (thunk 09C0:1EA5); here the screen as it was is put back.
/// <summary>
/// The character sheet, party information, camp and alchemy screens; the other screens come
/// from <paramref name="others"/>. Each screen puts back what was on the screen when it was
/// opened (the original redraws the card or map it was opened from).
/// </summary>
public sealed class GameScreens2 : IGameScreens
{
    private readonly GameUi _ui;
    private readonly GameState _state;
    private readonly IGameScreens _others;
    private readonly PartyInfoScreen _partyInfo;
    private readonly AlchemyScreen _alchemy;
    private readonly CharacterSheetScreen _sheet;
    private readonly CampScreen _camp;

    public GameScreens2(GameUi ui, PartyPanel panel, GameState state, WorldData world, IGameScreens others)
    {
        _ui = ui;
        _state = state;
        _others = others;
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        _partyInfo = new PartyInfoScreen(ui, panel);
        _alchemy = new AlchemyScreen(ui, panel, formulas);
        _sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), _alchemy);
        _camp = new CampScreen(ui, panel, formulas, _alchemy, _sheet);
        _sheet.SaveAndLoad = SetSaveAndLoad;
        _camp.SaveAndLoad = SetSaveAndLoad;
        _panel = panel;
        _world = world;
    }

    private readonly PartyPanel _panel;
    private readonly WorldData _world;

    /// <summary>
    /// The game's menu-bar keys (Alt-Q, Alt-P, Alt-S …) while the sheet is up; true when one was handled.
    /// UNCERTAIN: which keys the original's menu_bar_run takes inside the sheet is not traced; only the Alt keys are passed.
    /// </summary>
    public Func<int, bool>? MenuBarKey
    {
        get => _sheet.MenuBarKey;
        set => _sheet.MenuBarKey = value is null ? null : key => key is Keys.AltQ or Keys.AltP or Keys.AltS or Keys.AltL or Keys.AltD
            or Keys.AltF or Keys.AltM or Keys.AltC && value(key);
    }

    /// <summary>The saint page the sheet's Saints list opens (member, saint).</summary>
    public Action<int, int>? ShowSaint
    {
        get => _sheet.ShowSaint;
        set => _sheet.ShowSaint = value;
    }

    public void CharacterSheet(int member) => Keeping(() => _sheet.Show(_state, _state.Party.Resolve(member)));

    public void PartyInfo() => Keeping(() => _partyInfo.Show(_state));

    /// <summary>The game's hot keys (the roster greys their Save and Load items).</summary>
    public HotKeys? HotKeys { get; set; }

    /// <summary>The inn's party roster, opened by the inn card's first line; <paramref name="callerWord"/> is the card line's SI.</summary>
    // orig: R029+0x2246 -> thunk 09C0:1EE1 -> R038+0x0000 (PartyRosterScreen)
    public void PartyRoster(int callerWord) =>
        Keeping(() => new PartyRosterScreen(_ui, _panel, _world) { HotKeys = HotKeys }.Run(_state, callerWord));

    public void Camp(int mode) => Keeping(() => _camp.Stay(_state, mode));

    /// <summary>Camping from the map ('C'), with the day's poaching risk.</summary>
    public void CampInTheWild(int dailyRisk) => Keeping(() => _camp.CampInTheWild(_state, dailyRisk));

    public void Alchemy(int member, int place) => Keeping(() => _alchemy.Use(_state, _state.Party.Resolve(member), place));

    // orig: barter_open R032+0x0000 sets DS:9920 = DS:9934 = 2 and barter_close R032+0x076C sets them to 1
    public void Barter(int shop, string name, long categories)
    {
        SetSaveAndLoad(2);
        _others.Barter(shop, name, categories);
        SetSaveAndLoad(1);
    }

    private void SetSaveAndLoad(int flag)
    {
        if (HotKeys is { } keys)
        {
            keys.SaveGameItem = flag;
            keys.LoadGameItem = flag;
        }
    }

    public void ItemStore() => _others.ItemStore();

    public void Loot(IList<ItemStack> items, Money money) => _others.Loot(items, money);

    public int Battle(BattleRequest request) => _others.Battle(request);

    private void Keeping(Action screen)
    {
        var saved = _ui.Canvas.SaveScreen();
        try
        {
            screen();
        }
        finally
        {
            _ui.Canvas.Restore(saved);
            _ui.Present();
        }
    }
}
