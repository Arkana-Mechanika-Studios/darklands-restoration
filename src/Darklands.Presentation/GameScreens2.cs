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
    /// </summary>
    // orig: the sheet's loop runs menu-bar-loop(DS:9419, 0) (R031+0x06CA = root 0000:0000), which calls hotkey-table root 0000:0CAA when a key
    //   waits (root 0000:0029..0x0033): every key of its table is taken in the sheet too, Alt-O (0x1800, then R022+0x051C only if DS:9893 == 1)
    //   and F6 (0x4000, only if DS:987F == 1) among them; R031 writes neither flag
    // C: in the game those keys never come here: the sheet's GameUi.KeyAfterTheMouse -> GameUi.Poll hands every key to GameUi.GameKey first
    //   (HotKeys.Handle; in a battle BattleScreen.BattleHotKey), which takes F6 and Alt-O (tests: PartyIconsDirtyTests, SheetHotKeyTests). This
    //   is the fallback for a key GameKey left: in a battle's sheet Alt-O with the battle's Change Marching Order greyed (DS:9893 = 2 there,
    //   R022+0x009D), which the original refuses as well, so Alt-O and F6 stay out of this list (b32 C-fix, U-96d507).
    public Func<int, bool>? MenuBarKey
    {
        get => _sheet.MenuBarKey;
        set => _sheet.MenuBarKey = value is null ? null : key => key is Keys.AltQ or Keys.AltP or Keys.AltS or Keys.AltL or Keys.AltD
            or Keys.AltF or Keys.AltM or Keys.AltC && _ui.InMenuBarLoop(() => value(key));
    }

    /// <summary>The saint page the sheet's Saints list opens (member, saint).</summary>
    public Action<int, int>? ShowSaint
    {
        get => _sheet.ShowSaint;
        set => _sheet.ShowSaint = value;
    }

    /// <summary>The battle's "can't view" test for a member who left the field (BattleScreens.CannotViewMember), asked by the sheet.</summary>
    public Func<int, bool>? CannotView
    {
        get => _sheet.CannotView;
        set => _sheet.CannotView = value;
    }

    public void CharacterSheet(int member) => Keeping(() => _sheet.Show(_state, _state.Party.Resolve(member)));

    public void PartyInfo() => Keeping(() => WithoutBattleCycles(() => _partyInfo.Show(_state)));

    /// <summary>The party information over a battle: its palette cycles held while it is open (OriginalBehaviour.FixPartyInfoBattleCycles).</summary>
    // orig: R034+0x0EE6 (party-info-screen) through the overlay vector 09C0:185B (F6, the battle bar's Party Info), draft
    // BUG: OriginalBehaviour.FixPartyInfoBattleCycles - opened in a battle, the screen's background (R034+0x11BC) loads
    //   pics\ptystats.pic with its palette while the battle's cycles run (DS:07D8 = 1, slots 1, 2 and 4 over colours 16..30): nothing
    //   stops them (root 047C:040B) or clears DS:07D8, so the timer's frame work writes the battle's water and fire colours over the
    //   screen's at every step until it closes (the load screen stops them first, R037+0x8400..0x8426). Reproduced (the cycles run on);
    //   PartyInfoCyclesTests. Fixed (an evident display fix, every player game): the cycles held while the screen is open.
    private void WithoutBattleCycles(Action screen)
    {
        if (!_state.Behaviour.FixPartyInfoBattleCycles || !_ui.Cycles.Running)
        {
            screen();
            return;
        }
        _ui.RunCycles(false);
        _ui.PaletteSet();
        try
        {
            screen();
        }
        finally
        {
            // the battle's palette comes back with its screen (Keeping); its ranges turn again from their next steps
            _ui.RunCycles(true);
        }
    }

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

    // orig: barter_open R032+0x0000 sets DS:9920 = DS:9934 = 2 and trade-screen-close R032+0x076C sets them to 1
    public void Barter(int shop, string name, long categories)
    {
        SetSaveAndLoad(2);
        Up(() => _others.Barter(shop, name, categories));
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

    public void ItemStore() => Up(() => _others.ItemStore());

    public void Loot(IList<ItemStack> items, Money money) => Up(() => _others.Loot(items, money));

    public int Battle(BattleRequest request) => _others.Battle(request);

    private void Keeping(Action screen)
    {
        var saved = _ui.Canvas.SaveScreen();
        try
        {
            Up(screen);
        }
        finally
        {
            _ui.Canvas.Restore(saved);
            _ui.Present();
        }
    }

    /// <summary>A full screen: the hot keys leave F1..F5 and Ctrl-F to it (HotKeys.ScreensUp).</summary>
    private void Up(Action screen)
    {
        // host comfort (GameUi.SelectionList, the mouse wheel's ↑ / ↓): a full screen sets its own, over a card menu too
        using var wheel = _ui.UseSelectionList(null);
        if (HotKeys is { } keys)
        {
            keys.ScreensUp++;
        }
        try
        {
            screen();
        }
        finally
        {
            if (HotKeys is { } k)
            {
                k.ScreensUp--;
            }
        }
    }
}
