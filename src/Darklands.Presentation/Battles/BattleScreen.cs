using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

/// <summary>
/// The tactical battle screen: builds the battlefield of a request, runs the battle in real time
/// (the 60 Hz clock, a combat tick every 8 clock ticks), draws the isometric view, the combatants,
/// missiles, cursors and the party panel, and turns the player's keys and clicks into orders.
/// </summary>
// orig: fight-main-loop R014+0x0000 (0E76:0000) draft (the loop); fight-draw R014+0x6818 (14F7:0008) draft;
// fight-input R014+0x3166 (0E76:3166) draft; fight-mouse R014+0xB576 (19CD:0006) draft; fight-keys
// R016+0x0000 (1EF8:0000) draft (spec E)
// DIVERGENCE: the screen redraws the whole view every frame (the original keeps dirty rectangles); a message blocks as the original's
// notice-display does (ShowMessage).
public sealed partial class BattleScreen : ITacticalView
{
    private const int FrameMilliseconds = 16;

    // when the edge scroll last stepped (the host's clock)
    private long _edgeScrolledAt = long.MinValue / 2;

    /// <summary>The callbacks the original had after the last sheet in its pass (SheetCallbacks), for the pass's end.</summary>
    private int _afterSheetCallbacks;

    private (int Loads, int FirstFrame, int Late, int BeforeActivations) _setUpTicks;

    private bool _resuming;

    private int _resumeLate;

    private static int _setUpsSeen;

    // the battles of the process told apart for the tester's clock keys (DARKLANDS_BATTLE_CLOCK_JUMPS / _LOOT_TICKS)
    private static int _passBattle;
    private static int? _lastPassClock;

    private static int JumpKey(int clock) => _passBattle == 0 ? clock : _passBattle << 16 | clock;
    private long _setUpMark;

    private readonly GameUi _ui;
    private readonly BattleAssets _assets;
    private readonly GameState _state;
    private readonly BattleScreens _owner;
    private readonly AnimationLibrary _library;
    private readonly BattlefieldView _view = new();
    private readonly List<(int Who, int Left, int Top, int Right, int Bottom, int MapY)> _hotSpots = [];
    private readonly HashSet<int> _paletteApplied = [];

    /// <summary>The combatants (slot, appearance) whose schemes' cycles were taken in this battle.</summary>
    private readonly HashSet<int> _cyclesTaken = [];
    private TacticalBattle? _battle;
    private BattlefieldTiles? _tiles;
    private TextPainter? _panelText;
    private TextPainter _panel => _panelText ??= _ui.Painter(1);
    /// <summary>The game's hot keys outside the battle (Alt-Q, Alt-P …), which the battle passes some keys to.</summary>
    private Func<int, bool>? _hotKey;
    private string? _message;
    /// <summary>notice-display is waiting out its ticks: the pointer is hidden (not drawn) until the box is gone.</summary>
    private bool _messageWaiting;
    private long _messageUntil;

    /// <summary>DS:C62A / DS:C4FE: the releases of the left button taken from the mouse driver and not acted on yet, and the place of the last.</summary>
    private int _releases;
    private int _releaseX, _releaseY;

    private double _clockFraction;

    public BattleScreen(GameUi ui, BattleAssets assets, GameState state, BattleScreens owner)
    {
        _ui = ui;
        _assets = assets;
        _state = state;
        _owner = owner;
        _library = new AnimationLibrary(assets);
    }

    /// <summary>The tactical battle being shown (after <see cref="Start"/>).</summary>
    public TacticalBattle? Tactics => _battle;

    /// <summary>The place note party row <paramref name="row"/> shows instead of its bars ("near a door" ...), null for the bars.</summary>
    public string? PlaceNote(int row) => _placeNotes[row];

    /// <summary>The card the battle leaves for (battle-start-arguments).</summary>
    public int ResultCard { get; private set; }

    /// <summary>
    /// Runs the battle of the request until it ends; returns battle-outcome (0 won, 2 fled, 3
    /// lost, 4 surrendered, 5 all down, 6 fell into a pit, 7 timed exit; 8 "left before the
    /// fight" is reported as 2, as battle-entry's callers read it).
    /// </summary>
    // orig: battle-entry R002+0x0000 (0E76:0000) draft; its loop is fight-input R014+0x3166 (0E76:3166) draft
    public int Run(BattleRequest request)
    {
        // its texts as a translation pack lays them out (interface/layout.json), its hit tests and highlights with them
        using var laidOut = _ui.OnScreen("BattleScreen");
        // C: DS:07D6 counted on outside the battle (root 047C:000A runs at every timer callback of the program)
        var keys = _owner.CursorKeys;
        var timerTicks = _ui.Timer.Ticks;
        keys.HeldCallbacks = (ushort)(keys.HeldCallbacks + (timerTicks - keys.TimerTicksAtLeave));
        keys.TimerTicksAtLeave = timerTicks;
        Start(request);
        var previous = _ui.Clock.Milliseconds;
        _ui.PointerVisible = false;
        // orig: battle-entry R002+0x02A0: pointer-shape-set(0, 0, DS:A693) (the battle draws its own shapes, DrawPointer); R002+0x0514 at the
        //   end and a load in the battle (R037+0x8BBC) set (0, 0) with the gauntlet again: after a battle the gauntlet's hot spot is (0, 0)
        //   (GameUi.PointerHotSpot), whatever the map had set; tests: PointerHotSpotTests
        _ui.PointerHotSpot = (0, 0);
        _hotKey = _ui.GameKey;
        _ui.GameKey = BattleHotKey;
        _ui.BattleStatusLetter = StatusLetter;
        _ui.BattleOrderMode = () => _battle is { } live ? (live.InOrderMode, live.Selected) : _ui.PanelOrderMode;
        // the battle's menu bar (DS:9419 with the Orders and Attack menus) is the one the game's waits run while the battle lasts
        var menuBar = _ui.MenuBar;
        _ui.MenuBar = _menuBar;
        try
        {
            // orig: R002+0x001B root 047C:0568 at battle-entry's head, R014+0x02F8 root 047C:056F before the loop: the battle's own screen is shown
            // C: a card's picture still waiting for its first text (DS:EE55, GameUi.HoldScreen) is never shown: the battle replaces the screen
            //   (vis10, the city ambush of card 0x24: local/batch/b24fix)
            _ui.ReleaseScreen();
            // the first pass presents before its first Draw: the canvas still holds the screen of before the battle (a card's picture), already in
            // the battle's palette (SetPalette in Start), seen as a flash of wrong colours. That present shows the frame on show again; the battle
            // is seen from the second, after the first pass's Draw (the original's screen-hold until R014+0x02F8, GameUi.KeepShownFrameOnce);
            // tests: BattleEntryFrameTests
            _ui.KeepShownFrameOnce();
            // DIVERGENCE: the set-up's picture (fight-main-loop's panel and first fight-draw, R014+0x02FF..0x0307) is not presented here: the
            //   host's first frame comes after the first pass, whose tick has opened order mode for the leader. The original shows it until that
            //   tick (8 timer callbacks), with the member cursor not yet drawn and, in a battle started in the order mode the last one left,
            //   every row's name dim (vis10 session ordercarry-load-battle: the original's screen at the first command-mode-begin, rows 0..3 in
            //   colour 7; the host's first battle frame has member 3 chosen, its name 0x0E). Host timing of the frames, not followed.
            // orig: R014+0x031E / 0x0326 (R014+0x0000, before its loop): the mouse driver's release count read and DS:C62A = 0
            _ui.TakeLeftReleases();
            _releases = 0;
            var lateTicks = 0;
            Dictionary<int, int>? pendingRises = null;
            while (true)
            {
                var now = _ui.Clock.Milliseconds;
                Darklands.Core.Randomness.MscRandom.BeforeDraw = null;
                TacticalBattle.ClockPhase = null;
                TacticalBattle.RegainRead = null;
                if (OneTickPerPass && RegainRises is not null)
                {
                    // the regain runs before this pass's head, after the end test (whose loot screen counts the clock on): its key is the clock at
                    // its first read and the battle this pass will count in (a new one where the clock went back)
                    Dictionary<int, int>? regain = null;
                    TacticalBattle.RegainRead = slot =>
                    {
                        if (regain is null)
                        {
                            var head = _battle!.Battle.Clock;
                            var battle = _lastPassClock is { } last && head < last ? _passBattle + 1 : _passBattle;
                            regain = RegainRises.Remove(battle == 0 ? head : battle << 16 | head, out var found) ? found : [];
                        }
                        if (regain.Remove(slot, out var rise))
                        {
                            _clockFraction = rise;
                            Advance(0);
                        }
                    };
                }
                // orig: R014+0x0000 (0E76:0000) runs the end test (R014+0x08FE) and the endurance recovery (R014+0x45AE), then R014+0x3166
                // (the head the tester compares, the player's input); the timer callbacks of a pass come after them
                var over = _battle!.IterateHead();
                if (TacticalBattle.RegainRead is not null)
                {
                    // the callbacks after the regain's last read, before fight-input (its slot RegainAfter)
                    TacticalBattle.RegainRead(RegainAfter);
                }
                TacticalBattle.RegainRead = null;
                if (over)
                {
                    break;
                }
                Passes++;
                // the tester's keys of a session's later battles are battle << 16 | clock (a new battle where the clock goes back)
                if (_lastPassClock is { } lastClock && _battle!.Battle.Clock < lastClock)
                {
                    _passBattle++;
                }
                _lastPassClock = _battle!.Battle.Clock;
                PassHook?.Invoke(_battle.Battle.Clock);
                if (PassTrace is { } trace)
                {
                    trace($"pass clock {_battle!.Battle.Clock} rand {(_state.Random is Darklands.Core.Randomness.MscRandom msc ? msc.State.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : "?")}"
                        + (RecordTrace ? CombatantDump(_battle.Battle) : ""));
                }
                if (OneTickPerPass)
                {
                    _clockFraction = 1;
                    if (ClockJumps is not null && ClockJumps.Remove(JumpKey(_battle!.Battle.Clock), out var jump))
                    {
                        lateTicks = jump;
                    }
                    if (ClockRises is not null && ClockRises.Remove(JumpKey(_battle!.Battle.Clock), out var rises))
                    {
                        var draws = 0;
                        pendingRises = new Dictionary<int, int>(rises);
                        Darklands.Core.Randomness.MscRandom.BeforeDraw = () =>
                        {
                            if (rises.TryGetValue(draws, out var rise))
                            {
                                pendingRises.Remove(draws);
                                _clockFraction = rise;
                                Advance(0);
                            }
                            draws++;
                        };
                        TacticalBattle.ClockPhase = phase =>
                        {
                            // a tick the C# found not due where the original's scheduler ran (its rise -1): the original's long computation
                            // between the activations' enemy turn and fight-tick's due test (R014+0x0F58) made it due; the callbacks that
                            // made it due come before the test, one a call, the rest at the scheduler (round C3: gen-bluer-01, a 136-tick pass)
                            if (phase == -6)
                            {
                                if (pendingRises is not null && pendingRises.TryGetValue(-1, out var left) && left > 0)
                                {
                                    pendingRises[-1] = left - 1;
                                    _clockFraction = 1;
                                    Advance(0);
                                }
                                return;
                            }
                            if (pendingRises is not null && pendingRises.Remove(phase, out var rise))
                            {
                                _clockFraction = rise;
                                Advance(0);
                            }
                        };
                    }
                    Advance(0);
                }
                else
                {
                    Advance(now - previous);
                }
                previous = now;
                _ui.Present();
                // orig: R014+0x3166: the cursor keys (DS:D7FE), the mouse handler (the hover while the button is held, then a
                // release taken from the driver's count as a click in the view), root 0000:0000, a release over the party panel, the Shift
                // scrolling at the view's edges, then the keys (a waiting key, or a direction key held)
                if (_battle!.CursorKeysOn)
                {
                    CursorKeys();
                }
                CombatMouse();
                if (MenuBarRun())
                {
                    // C: root 0000:0000's loop holds DS:07D3: the combat clock stood still while the bar was up
                    previous = _ui.Clock.Milliseconds;
                }
                PanelRelease();
                EdgeScroll();
                var keyHandled = false;
                if (_ui.KeyWaiting || _ui.Directions.X != 0 || _ui.Directions.Y != 0)
                {
                    CombatKeys();
                    keyHandled = true;
                }
                if (keyHandled && pendingRises is not null && pendingRises.Remove(-2, out var keyRise))
                {
                    // the original's long computation of a key's order (the path searches of Q, single file) came before the tick tests the clock
                    // (the tester lists it as the rise at the tick's first clock read, the melee exchanges) but after the activations' test
                    // (R014+0x05B4: the original's next check came a pass later, lockstep session savemid-mine): it is given there
                    var held = TacticalBattle.ClockPhase;
                    TacticalBattle.ClockPhase = phase =>
                    {
                        if (phase == -3)
                        {
                            _clockFraction = keyRise;
                            Advance(0);
                            keyRise = 0;
                        }
                        held?.Invoke(phase);
                    };
                }
                if (_resyncClock)
                {
                    previous = _ui.Clock.Milliseconds;
                    _resyncClock = false;
                }
                _battle!.IterateRest();
                if (_resyncClock)
                {
                    // a wait inside the tick (a message of a door, a trap, a pit) counted its own ticks
                    previous = _ui.Clock.Milliseconds;
                    _resyncClock = false;
                }
                if (pendingRises is { Count: > 0 })
                {
                    // the rises of the original's pass that this pass did not reach (a draw or a phase it does not make, as when the loot screen
                    // or a path search took the time there): they come after it
                    lateTicks += pendingRises.Values.Sum();
                }
                pendingRises = null;
                if (_afterSheetCallbacks > 0)
                {
                    // the callbacks the original had after a sheet in this pass (a paused pass: no clock rise lists them)
                    lateTicks += _afterSheetCallbacks;
                    _afterSheetCallbacks = 0;
                }
                if (lateTicks > 0)
                {
                    // the callbacks of the time a long tick took came during it: after what the tick read of the clock
                    _clockFraction = lateTicks;
                    Advance(0);
                    lateTicks = 0;
                }
                Draw();
                _ui.Clock.Sleep(FrameMilliseconds);
            }
            Darklands.Core.Randomness.MscRandom.BeforeDraw = null;
            PassHook?.Invoke(-1);
            // orig: fight-main-loop's end R014+0x0759..0x0783: root 047C:040B(1), (2), (3), (4) and DS:07D8 = 0: the cycles stop where they are
            for (var slot = 1; slot <= 4; slot++)
            {
                _ui.Cycles.Stop(slot);
            }
            _ui.RunCycles(false);
            // orig: battle-entry R002+0x0000 after thunk 09C0:1B03 (fight-main-loop): pointer-hide (root 047C:12C3), pointer-shape-set(0, 0, mouse-cursor-default)
            // (root 047C:124F), catalog-release (root 0000:262E), then "BattleResults - victory" … through notice-display(DS:9085, 0x96) over the screen
            // as fight-main-loop left it: the battle view in the battle palette (after a victory the loot screen has put both back, see OpenLoot).
            // Nothing restores the screen of before the battle: the caller draws its next screen (the card's picture and palette).
            ErasePointer();
            var ended = _battle.Battle.Result;
            if (ended is not (BattleResult.Running or BattleResult.FellIntoPit or BattleResult.TimedExit))
            {
                new MessageBox(_ui).Show(BattleScreens.ResultText(_ui, (int)ended), 0x96 * 1000 / 60);
            }
            // C: R002+0x05C1: fight-recovery, then the buffers freed and (R002+0x0710) the able and unconscious lose their action and target
            _battle.RecoverAfterBattle();
            Finish();
            // C: the battle's marching order is the game's (DS:C624 / DS:C629): it stays as the battle left it (its five bytes and the length).
            _battle.StoreMarchingOrder(_state.MarchingOrder);
        }
        finally
        {
            KeepPanelSelection();
            _owner.CursorKeys.TimerTicksAtLeave = _ui.Timer.Ticks;
            _ui.PointerVisible = true;
            _ui.GameKey = _hotKey;
            _ui.MenuBar = menuBar;
            _ui.BattleStatusLetter = null;
            _ui.BattleOrderMode = null;
            // C: the next screen sets its own palette (the DAC then shows it); a game loaded from the battle's menu stops the cycles first
            //   (R037+0x8400..0x8426: root 047C:040B(1..4), DS:07D8 = 0)
            for (var slot = 1; slot <= 4; slot++)
            {
                _ui.Cycles.Stop(slot);
            }
            _ui.RunCycles(false);
            _ui.PaletteSet();
        }
        var result = (int)_battle.Battle.Result;
        return result == 8 ? 2 : result;
    }

    /// <summary>
    /// The hot keys during a battle: the Alt keys of the game (quit, pause, difficulty, music, sound, saintly changes) go to the
    /// game's handler; Alt-O (Change Marching Order) is the battle's; saving, loading and F6 follow their menu entries.
    /// </summary>
    // orig: hotkey-table root 0000:0CAA draft with the battle's flags DS:9893 (Alt-O), DS:9920 (Alt-S), DS:9934 (Alt-L), DS:987F (F6)
    private bool BattleHotKey(int key)
    {
        var m = _battle!.Menus;
        switch (key)
        {
            case Keys.AltO:
                if (m.MarchingOrder == 1)
                {
                    ChooseMarchingOrder();
                    return true;
                }
                return false;
            case Keys.AltS:
                SaveGame();
                return true;
            case Keys.AltL:
                LoadGame();
                return true;
            case Keys.F6:
                if (m.PartyInfo == 1)
                {
                    _owner.PartyInfo?.Invoke();
                }
                return true;
            case Keys.AltQ or Keys.AltP or Keys.AltD or Keys.AltF or Keys.AltM or Keys.AltC:
                // C: the battle's pointer is the driver's, shown in the battle's last shape: the pause box (root 0000:1080) shows it while it
                //   waits (pointer-show 0x10E7), the message box and the question hide it (vis10 session leftovers-pause: the order hand over
                //   "Game Paused"); the battle's own drawn one is already off (HotKeysForWaitingKey)
                var restore = ShowDriverPointer();
                bool acted;
                try
                {
                    acted = _hotKey?.Invoke(key) ?? false;
                }
                finally
                {
                    restore();
                }
                if (acted && OneTickPerPass && key is Keys.AltD or Keys.AltF or Keys.AltM or Keys.AltC && BoxCallbacks is { Count: > 0 } measured)
                {
                    // C: the host has no time of its own in OneTickPerPass: the timer callbacks of the box's wait (root 0000:24BC: DS:07D1 counts on)
                    // and, paused, those after it up to the next pass
                    var (during, after) = measured.Dequeue();
                    CountCallbacks(during);
                    if (_battle!.PauseByte != 0)
                    {
                        _afterSheetCallbacks += after;
                    }
                }
                return acted;
            default:
                return false;
        }
    }

    /// <summary>The centre of combatant i's hot spot as last drawn (screen pixels), null when it has none (tests, the headless host).</summary>
    public (int X, int Y)? HotSpotCentre(int i) => HotSpot(i) is { } h ? ((h.Left + h.Right) / 2, (h.Top + h.Bottom) / 2) : null;

    /// <summary>The members' selection starts as the panel's selection bytes stand (GameUi.PanelSelection, never cleared at a battle's start).</summary>
    // orig: DS:9064 is the battle's fighter-selected table itself: no write between battles (see GameUi.PanelSelection)
    private void TakePanelSelection(Battle battle)
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            battle[i].Selected = _ui.PanelSelection[i];
        }
    }

    /// <summary>The panel's selection bytes as the battle leaves them, however it ends (a game loaded from its menu bar included).</summary>
    private void KeepPanelSelection()
    {
        if (_battle is { } tb)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                _ui.PanelSelection[i] = tb.Battle[i].Selected;
            }
            // C: DS:A6B3 / DS:A6B2 as the battle leaves them (GameUi.PanelOrderMode)
            _ui.PanelOrderMode = (tb.InOrderMode, tb.Selected);
        }
    }

    /// <summary>The battle's seen positions start as the program's (the last battle's, the one a load left) and are the program's from now.</summary>
    // C: seenpos (20A5:79A9) is written only by R014+0xF568 (a path found) and read by the walkers' re-path test (R014+0xF418's 1DB7:0D36):
    // no battle start, restore or load clears it
    private void KeepSeenPositions(Battle battle)
    {
        if (_owner.SeenPositions is { } kept)
        {
            for (var i = 0; i < Combatant.Count; i++)
            {
                kept[i].CopyTo(battle[i].SeenPositions, 0);
            }
        }
        _owner.SeenPositions = [.. Enumerable.Range(0, Combatant.Count).Select(i => battle[i].SeenPositions)];
    }

    /// <summary>The view's centre cell (DS:C866 / DS:C886).</summary>
    public (int Column, int Row) ViewCentre => (_view.CentreColumn, _view.CentreRow);

    /// <summary>Combatant i's hot spot as last drawn (screen pixels, inclusive), null when it has none (tests, the headless host).</summary>
    public (int Left, int Top, int Right, int Bottom)? HotSpot(int i)
    {
        foreach (var h in _hotSpots)
        {
            if (h.Who == i)
            {
                return (h.Left, h.Top, h.Right, h.Bottom);
            }
        }
        return null;
    }

    /// <summary>Saves what the screen shows as a PNG (tests, screenshots).</summary>
    public void SaveScreen(string path) => PngWriter.Write(_ui.Canvas.Screen, path);

    /// <summary>Builds the battlefield, the combatants and the palette; places the party.</summary>
    public void Start(BattleRequest request)
    {
        if (request.Resume && _state.BattleFile is { } file)
        {
            Resume(request, file);
            return;
        }
        var context = new BattlefieldContext
        {
            Enemies = _assets.Enemies, Items = _assets.Items, Tables = _assets.Tables, Maps = _assets.Maps, Citadel = _assets.Citadel, Random = _state.Random,
            Location = _state.CurrentLocation, WorldTerrain = _owner.WorldTerrain?.Invoke() ?? _state.PartyTerrain, Month = _state.Date.Month,
            AbleMembers = _state.Party.Able.Count(), Memory = _owner.Memory, Process = _owner.GenerationMemory, Behaviour = _state.Behaviour,
            LoreLevel = () => Darklands.Core.Rules.PartyQueries.LoreLevel(_state),
        };
        // battle-entry's modes: 0 builds a new battlefield (fight-flags 0x10); 1..3 go back onto the last one (kept with its
        // levels and the encounters not met yet, as the original keeps the level files): 1 and 2 show the level shown when the last
        // battle ended (1 with the party where it stood, 2 at that level's start), 3 shows level `BattlefieldType` (the level for mode 3, at its start).
        // orig: R002+0x0098..0x00EE checked against the asm; tests: RevisitTests
        // C: seen only from a synthetic save (vis10 visleads-prison-battle: card 0x16B's mode 1 from a city): without a .BSV the original
        //   fights on the level files the machine holds (background row 3), with a .BSV of no levels (the game's 282-byte DKSAVE0.BSV) on
        //   an empty field (its view stays black: 32,785 px against the C#'s new keep; re-run 2026-11-05). In play the cell follows the
        //   fortress battle, whose field the C# keeps (LastField) as the original keeps its files.
        // C: settled (round visuals 2026-11-06): card 0x16B is entered only from the zone of the Templar fortress's room 8 (R011+0x1043, the
        //   only 0x16B in the EXE besides the card's own following-card write R134+0x1F1A), so in play the files then hold the fortress of
        //   that place, also after a save and a load (the save packs them, the restore writes them back). The real-play state, the
        //   community save DKSAVE2 of bug-sabbath-jan0 (483.07, at the fortress's place 188 with the fortress in its .BSV) set to card
        //   0x16B, goes back onto the fortress in both (vis10 visuals-fortress-cell: 1 px of 64,000 differ, a ground pixel by the party, not
        //   the field: the dirty-tile redraw order of the registered whole-view redraw, see Draw). Only a save with the card set by hand away from the fortress, or a missing .BSV, reaches the case below.
        // DIVERGENCE: the original goes back onto whatever battlefield its files hold; here a revisit needs the last battlefield to have
        // been fought at the same place (else a new one is built), and mode 3's level is kept within the battlefield's levels.
        if (request.Mode != 0 && _owner.LastField is null && _state.BattleFile is { Levels.Count: > 0 } held)
        {
            // C: after a load the level files are the battle file's (the restore R003+0x0CE8 wrote them back): the last battlefield is that one.
            _owner.LastField = TacticalBattle.FromBattleFile(held,
                new BattlefieldForces(_assets.Enemies, _assets.Items, _assets.Tables, _state.Random) { Behaviour = _state.Behaviour });
            _owner.LastFieldLocation = _state.Battlefield.LastLocation;
            // C: the level shown is the variables' (DS:11CA) and each member's place its record's (+0x04, +0x06, facings +0x19, +0x1A).
            var store = _owner.LastField.Store;
            store.LevelShown = held.Variables[BattleVariables.LevelShown];
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (i < _state.Party.Slots.Count && _state.Party.Slots[i] is { OccupiesSlot: true, OriginalRecord: { Length: >= Persistence.CombatantRecords.Size } r })
                {
                    var probe = new Combatant(i);
                    Persistence.CombatantRecords.Read(r, probe);
                    store.PartyPlaces[i] = (probe.Position, probe.Facing, probe.SpriteFacing);
                }
            }
        }
        var reuse = request.Mode != 0 && _owner.LastField is { } last && _owner.LastFieldLocation == _state.CurrentLocation;
        var field = reuse ? _owner.LastField! : BattlefieldBuilder.Build(context, request.BattlefieldType, request.Seed, request.Groups);
        if (!reuse && _state.BattleFile is { } before)
        {
            // C: DS:C7A4 (the chests' loot file offsets) is not cleared for a new battlefield: the preparation sets the offsets of its
            //   chests, the others keep the last battle's or the .BSV's values, which the loot take (R014+0xE7C8) still moves down or sets
            //   to -1 and the battle file writes (b35: a read watch from the fight's entry; a fresh program's are zeros)
            for (var k = 0; k < field.Store.Loot.ChestOffsets.Length; k++)
            {
                field.Store.Loot.ChestOffsets[k] = (short)(before.Variables[BattleVariables.ChestOffsets + 2 * k] | before.Variables[BattleVariables.ChestOffsets + 2 * k + 1] << 8);
            }
        }
        _owner.LastField = field;
        _owner.LastFieldLocation = _state.CurrentLocation;
        var floor = !reuse ? field.StartLevel
            : request.Mode == 3 ? Math.Clamp(request.BattlefieldType, 0, field.Levels.Count - 1)
            : field.Store.LevelShown >= 0 ? field.Store.LevelShown : field.StartLevel;
        var battle = new Battle(_assets.Tables, _state.Random)
        {
            Persistent = field.Persistent || request.Persistent, DifficultyOf = _state, LevelShown = floor,
            LeaderOf = _state.Party, ActiveMember = _state.Party.LeaderIndex, Behaviour = _state.Behaviour,
            Parameters = _state.Parameters,
        };
        _tiles = BattlefieldTiles.Load(_assets.TileSets, field.Terrain);
        var furniture = _tiles.Furniture.Select(f => f is null ? null : new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY)).ToArray();
        // C: R002+0x0000 sets DS:C1F0 to the battlefield and DS:D6CE to the seed (the battle file keeps them).
        Array.Clear(_placeNotes);
        Array.Clear(_tickPlaces);
        Array.Clear(_orderLetters);
        Array.Clear(_rowLetters);
        Array.Clear(_rowLetterDrawn);
        _orderCellRows = 0;
        _pageCellHoldsRows = false;
        _framesOverHatching = false;
        _battle = new TacticalBattle(battle, field, furniture, _library, this)
        {
            ProgramMemory = _owner.GenerationMemory, BattlefieldType = request.BattlefieldType, Seed = request.Seed, CursorKeys = _owner.CursorKeys,
            StaleFlags = _state.BattleFile is { } program ? [.. Enumerable.Range(0, Combatant.Count).Select(i => (ushort)(program.Variables[BattleVariables.Flags + 2 * i] | program.Variables[BattleVariables.Flags + 2 * i + 1] << 8))] : null,
            StalePaths = _state.BattleFile is { } heldFile ? (heldFile.Variables[BattleVariables.Paths..(BattleVariables.Paths + Combatant.Count * Combatant.PathLength)],
                heldFile.Variables[BattleVariables.PathIndex..(BattleVariables.PathIndex + Combatant.Count)]) : null,
            StaleVariables = _state.BattleFile is { } programFile ? (byte[])programFile.Variables.Clone() : null,
        };
        _view.PieceFrame = (layer, x, y) => _battle?.Effects.PieceFrame(layer, x, y) ?? -1;
        TakePanelSelection(battle);
        _battle.TakeOrderMode(_ui.PanelOrderMode.On);
        KeepSeenPositions(battle);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = battle[i];
            c.AnimationSlot = i;
            if (i < _state.Party.Slots.Count && _state.Party.Slots[i].OccupiesSlot)
            {
                var member = _state.Party.Slots[i];
                CombatantSetup.FromMember(c, member);
                _battle.SlotCodes[i] = member.Image.Length > 0 ? member.Image : "F01";
            }
            else
            {
                c.Status = CharacterStatus.Empty;
            }
        }
        SetPalette(field.Terrain.BackgroundPalette);
        StartCycles();
        // orig: the marching order (DS:C624 / DS:C629) is the game's: the battle starts from it, R016+0x0DA6 keeping it while it is valid
        _battle.LoadMarchingOrder(_state.MarchingOrder);
        // C: the party's place tables (doors, stairs, chest, dissolve slot, exit zone, stairs back) are the program's battle variables as
        //   the last battle or a load's battle-reload (R003+0x0CE8, from the .BSV) left them (b35: a write watch in the original after an
        //   Alt-L; a second battle in one run starts with the first's; markers written at the fight's entry survive its start)
        _battle.ProgramMemory.PartyTables = _state.BattleFile is { } tables
            ? tables.Variables[BattleVariables.MemberDoor..(BattleVariables.MemberDoor + GenerationProcessMemory.PartyTablesSize)]
            : null;
        (_view.CentreColumn, _view.CentreRow) = _owner.ViewCentre;
        _battle.Start(arrivedThroughDoor: request.Mode >= 2, delayedStart: field.Timed && !reuse,
            combatFlags: request.CombatFlags | _state.BattleOrders.Flags, keptPlaces: reuse && request.Mode == 1 ? field.Store.PartyPlaces : null);
        _menuBar = BuildMenuBar();
    }

    /// <summary>
    /// A battle taken up from a saved game (combat flag 0x100): the battlefield of the program's battle file, the party
    /// from its records (where each member stood, its animation), the battle's variables, then the start of a fight
    /// taken up again.
    /// </summary>
    // orig: R002+0x0000 (0E76:0000) draft with DS:9081 bit 0: mode 1 (no new battlefield, combat flag 2), the reset R002+0x1830
    // without its restored part; tests: BattleSaveTests; the levels as filled by the restore R003+0x0CE8;
    // the party's records are its combatant records as filled by R037+0x8ED4 (1C7D:0D24)
    // C: the reset R002+0x1830 still clears each member's action (+0x10) and target (+0x4A) and empties records 5..12: the enemies of the
    // fight that was saved are not in the battle file (only those not met yet, in the activations).
    private void Resume(BattleRequest request, BattleFile file)
    {
        var forces = new BattlefieldForces(_assets.Enemies, _assets.Items, _assets.Tables, _state.Random) { Behaviour = _state.Behaviour };
        var field = TacticalBattle.FromBattleFile(file, forces);
        _owner.LastField = field;
        _owner.LastFieldLocation = _state.CurrentLocation;
        var battle = new Battle(_assets.Tables, _state.Random)
        {
            Persistent = true, DifficultyOf = _state, LevelShown = file.Variables[BattleVariables.LevelShown],
            LeaderOf = _state.Party, ActiveMember = _state.Party.LeaderIndex, Behaviour = _state.Behaviour,
            Parameters = _state.Parameters,
        };
        _tiles = BattlefieldTiles.Load(_assets.TileSets, field.Terrain);
        var furniture = _tiles.Furniture.Select(f => f is null ? null : new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY)).ToArray();
        Array.Clear(_placeNotes);
        Array.Clear(_tickPlaces);
        Array.Clear(_orderLetters);
        Array.Clear(_rowLetters);
        Array.Clear(_rowLetterDrawn);
        _orderCellRows = 0;
        _pageCellHoldsRows = false;
        _framesOverHatching = false;
        _battle = new TacticalBattle(battle, field, furniture, _library, this)
        {
            ProgramMemory = _owner.GenerationMemory, BattlefieldType = request.BattlefieldType, Seed = request.Seed, CursorKeys = _owner.CursorKeys,
        };
        TakePanelSelection(battle);
        _battle.TakeOrderMode(_ui.PanelOrderMode.On);
        KeepSeenPositions(battle);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = battle[i];
            c.AnimationSlot = i;
            if (i < _state.Party.Slots.Count && _state.Party.Slots[i].OccupiesSlot)
            {
                var member = _state.Party.Slots[i];
                CombatantSetup.FromMember(c, member);
                if (member.OriginalRecord is { Length: >= Persistence.CombatantRecords.Size } record)
                {
                    Persistence.CombatantRecords.Read(record, c);
                    Persistence.CombatantRecords.ReadAnimation(record, c);
                }
                c.Member = member;
                c.Target = -1;
                c.Action = CombatAction.None;
                _battle.SlotCodes[i] = member.Image.Length > 0 ? member.Image : "F01";
            }
            else
            {
                c.Status = CharacterStatus.Empty;
            }
        }
        _battle.ReadBattleFile(file);
        // orig: R002+0x0000 sets DS:C1F0 to the call's battlefield after the restore R003+0x0CE8 read the file's (a game loaded from the start
        // screen: DS:A768 still 0); lockstep dungeon-king-ladder (local/batch/b32/a7-king)
        _battle.BattlefieldType = request.BattlefieldType;
        SetPalette(field.Terrain.BackgroundPalette);
        StartCycles();
        (_view.CentreColumn, _view.CentreRow) = _owner.ViewCentre;
        _resuming = true;
        _battle.Resume();
        _resuming = false;
        if (_resumeLate > 0)
        {
            _clockFraction = _resumeLate;
            Advance(0);
            _resumeLate = 0;
        }
        _menuBar = BuildMenuBar();
    }

    /// <summary>The battle palette: common colours, the party's colours, the terrain's background colours.</summary>
    // orig: battle-entry R002+0x0000 R002+0x037B..0x0502 (0E76:037B) draft (the palette part, spec E §0)
    // orig: R002+0x045C..0x0470 copies the 0x78 bytes at 20A5:33FC (the five slots' figure colours, 0x18 each) whatever the party size:
    //   a slot past the party keeps the colours a load or a departed member left there (vis10 session D-b797f7: colour 112)
    private void SetPalette(int background)
    {
        var party = new byte[120];
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            var colours = i < _state.Party.Slots.Count ? PartyColours(_state.Party.Slots[i].ImagePalette, i)
                : _state.Party.SpareRecord(i)?.ImagePalette ?? [];
            colours.AsSpan(0, Math.Min(24, colours.Length)).CopyTo(party.AsSpan(24 * i));
        }
        // Colours 0..15 come from MGRAPHIC's 16 EGA colours through the index list {0, 1, 2, 3, 4, 0, 6, …, 15}: colour 5 is black,
        // the shadow colour inside the sprites (spec E §0).
        // orig: R002+0x0378..0x037B root 047C:0584(0x1C0) before the DS:9069 test, in every battle: the driver writes all sixteen (colour 9 is
        //   (21, 21, 63) again even when a card's picture changed it); root 0410:031E then reads them back into the palette set at R002+0x04BA
        _ui.UseStartupColors();
        foreach (var (index, rgb) in BattlePalette.Colors(_assets.CommonColors, _assets.BackgroundPalettes, background, party))
        {
            _ui.Canvas.SetColor(index, rgb);
        }
        // C: palette-load (root 0410:0364, R002+0x04BA): the DAC shows the palette until each running cycle's next step
        _ui.PaletteSet();
    }

    /// <summary>The battlefield's palette cycles (colours 16..21, 24..30 and 22..23 of the background palette) start with the battle.</summary>
    // orig: battle-entry R002+0x04D2..0x04FA after palette-load: root 047C:0362(1, 4, 0x10, 0x15), 047C:0362(2, 6, 0x18, 0x1E),
    //   047C:0362(4, 5, 0x16, 0x17); fight-main-loop R014+0x02D5..0x02F3: 047C:03DA(1), 047C:03DA(-2) (backwards), 047C:03DA(4), DS:07D8 = 1
    private void StartCycles()
    {
        _ui.TakeCycle(1, 4, 0x10, 0x15);
        _ui.TakeCycle(2, 6, 0x18, 0x1E);
        _ui.TakeCycle(4, 5, 0x16, 0x17);
        _ui.Cycles.Start(1);
        _ui.Cycles.Start(-2);
        _ui.Cycles.Start(4);
        _cyclesTaken.Clear();
        _ui.RunCycles(true);
    }

    /// <summary>fight-mode-exit: the enemies' cycle (slot 3) stops; its colours stay as its last step left them.</summary>
    // orig: R014+0x0D99: root 047C:040B(3)
    public void CombatModeLeft() => _ui.Cycles.Stop(3);

    /// <summary>A party slot's figure colours (member-colours[slot]) as the battle palette takes them.</summary>
    // orig: battle-entry R002+0x0000 R002+0x037B (0E76:037B) draft (member-colours copied into the party colours)
    // DIVERGENCE: a member whose 24 colour bytes are all zero is drawn with the Quickstart party's colours of its slot
    // (DefaultParty.Colours), where the original draws it black. Only saved games written by release 0.9.0 from a
    // Quickstart party have such members (that release left the four without colours); the original never writes them.
    // tests: QuickstartColoursTests
    public static byte[] PartyColours(byte[] colours, int slot) =>
        colours.All(b => b == 0) && slot < DefaultParty.Colours.Count ? DefaultParty.Colours[slot] : colours;

    /// <summary>The 60 Hz clock advances while the battle is not paused.</summary>
    // orig: the timer callback root 047C:000A (spec D §1.1): fight-clock stops while DS:07D3 != 0
    // (the callback also counts DS:07D1, paused or not: the paused ticks come every 8)
    private void Advance(long milliseconds)
    {
        _clockFraction += milliseconds * 60 / 1000.0;
        var ticks = (int)_clockFraction;
        _clockFraction -= ticks;
        CountCallbacks(ticks);
        // C: the pause holds DS:07D3 from R016+0x075A / R014+0xBC9E (with DS:A695 = 1) to R016+0x144D (A695 = 0); Esc with nobody selected
        // can turn A695 into 0x11 meanwhile (R016+0x13F9) without touching 07D3, so the clock stands while the byte is not 0
        if (_battle!.PauseByte != 0)
        {
            return;
        }
        _battle.Battle.Clock = (ushort)(_battle.Battle.Clock + ticks);
    }

    /// <summary>Timer callbacks that came: the counters DS:07D1 and DS:07D6 go on (the combat clock is the caller's).</summary>
    // orig: root 047C:000A: `inc byte [07D1]` and `inc word [07D6]` at every callback, whether DS:07D3 holds the clock or not; only
    // fight-tick R014+0x0F85 clears DS:07D1 (and R014+0x0F7B compares it unsigned with 8), so it wraps at 256
    private void CountCallbacks(int ticks)
    {
        _battle!.TimerCallbacks = (_battle.TimerCallbacks + ticks) & 0xFF;
        // C: DS:07D6 counts every callback, paused or not (a word; a program global: CursorKeyState.HeldCallbacks)
        _owner.CursorKeys.HeldCallbacks = (ushort)(_owner.CursorKeys.HeldCallbacks + ticks);
    }

    /// <summary>A screen over the battle that holds the combat clock (DS:07D3) took <paramref name="milliseconds"/>: the timer callbacks of
    /// that time count DS:07D1 and DS:07D6 on, the clock does not move; the loop's time starts again after it.</summary>
    // C: the timer stays installed meanwhile (root 047C:0066 / 0x00A9 are not called), so a paused battle whose 07D1 reached 8 ticks at
    // once after the screen (R014+0x0F7B), drawing its random numbers there rather than on the cadence of 8 passes
    private void HeldClockTime(long milliseconds)
    {
        if (!OneTickPerPass)
        {
            _clockFraction += milliseconds * 60 / 1000.0;
            var ticks = (int)_clockFraction;
            _clockFraction -= ticks;
            CountCallbacks(ticks);
        }
        _resyncClock = true;
    }

    /// <summary>After the battle: members' records back, loot handed over.</summary>
    private void Finish()
    {
        var b = _battle!.Battle;
        _owner.ViewCentre = (_view.CentreColumn, _view.CentreRow);
        // C: DS:A532 (the effects' modifiers) stays in the program for a battle taken up from a save later (not reset by a load)
        _battle.KeepModifiers();
        // C: what the battle leaves in the program's battle file (variables, levels, terrain.fil, the loot file), as R002+0x0000 leaves
        // its globals: DS:907A = 0, the floor (DS:A6B4) of the members who were able or unconscious 0, the result 8 read as 2.
        var file = _state.BattleFile ??= new BattleFile();
        _battle.WriteBattleFile(file, inBattle: false, _owner.Memory.ClearedLevels, _state.LastCastleFlags);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (b[i].Status is CharacterStatus.Able or CharacterStatus.Unconscious)
            {
                file.Variables[BattleVariables.Floor + i] = 0;
            }
        }
        if (b.Result == BattleResult.LeftBeforeFight)
        {
            file.Variables[BattleVariables.Result] = (byte)BattleResult.Fled;
            file.Variables[BattleVariables.Result + 1] = 0;
        }
        // floor-picture keeps the level shown at the end for the next battle's empty battlefield.
        _owner.GenerationMemory.FloorImage = (byte[])_battle.Levels.Shown.Image.Clone();
        // the party's collision sizes (DS:C80F / DS:C831) stay in the program's tables for the next battle's placement
        _owner.GenerationMemory.PartySizes = [.. Enumerable.Range(0, Combatant.PartySlots).Select(i => (b[i].HalfWidth, b[i].HalfHeight))];
        // and so do the party's animation slots' sizes (DS:1182 / DS:118E), which the next battle's start copies into them
        _owner.GenerationMemory.PartySlotSizes = [.. Enumerable.Range(0, Combatant.PartySlots).Select(i => (_battle.Slots[i].HalfWidth, _battle.Slots[i].HalfHeight))];
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (b[i].Member is { } member)
            {
                CombatantSetup.BackToMember(b[i]);
                // C: the member's record is its combatant record: where it stood and its animation stay in it (a save writes them, a
                // revisit in mode 1 after a load starts from them); R002+0x0000 clears the action and target of the able and unconscious.
                if (member.OriginalRecord is { Length: >= Persistence.CombatantRecords.Size } kept)
                {
                    var record = (byte[])kept.Clone();
                    Persistence.CombatantRecords.Write(b[i], record);
                    Persistence.CombatantRecords.WriteAnimation(b[i], record);
                    // C: R002+0x1830 set +0x1C to the slot at the battle's start
                    record[0x1C] = (byte)i;
                    if (b[i].Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                    {
                        record[0x10] = record[0x11] = 0;
                        record[0x4A] = 0xFF;
                    }
                    member.OriginalRecord = record;
                }
            }
        }
        ResultCard = b.ResultCard;
        _owner.Memory.ClearedLevels |= _battle.EncounteredLevels;
        // the battlefield remembers the level shown and where each member stands, for a revisit in mode 1 or 2
        var store = _battle.Field.Store;
        store.LevelShown = b.LevelShown;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            store.PartyPlaces[i] = b[i].Status == CharacterStatus.Empty ? null : (b[i].Position, b[i].Facing, b[i].SpriteFacing);
            store.PartyLevels[i] = b[i].Status == CharacterStatus.Empty ? -1 : b[i].Level;
        }
    }

    /// <summary>The combatants' packs into the party records before a screen that works on the members (loot, character sheet).</summary>
    private void PacksToMembers()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = _battle!.Battle[i];
            if (c.Member is { } m)
            {
                // orig: the pack is shared in the original: all 64 entries and the count, stale entries included
                m.Pack.CopyFrom(c.Items);
            }
        }
    }

    /// <summary>The battle loop's passes so far (R014+0x0000's iterations past the end test).</summary>
    public int Passes { get; private set; }
}
