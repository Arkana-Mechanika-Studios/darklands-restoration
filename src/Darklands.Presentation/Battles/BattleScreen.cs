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
public sealed class BattleScreen : ITacticalView
{
    private const int FrameMilliseconds = 16;

    /// <summary>A debugging aid: exactly one timer callback of the 60 Hz clock a pass of the loop, whatever the time (DARKLANDS_BATTLE_TICK_PER_PASS),
    /// as tools/oracle/lockstep.py runs the original's battle.</summary>
    /// <summary>A debugging aid: a line at the head of each pass, the combat clock and the random state (DARKLANDS_BATTLE_TRACE).</summary>
    public static Action<string>? PassTrace { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_TRACE") is null ? null : Console.WriteLine;

    /// <summary>DARKLANDS_BATTLE_TRACE=records (the battle tester): each pass's line also holds the combatants' records and tables.</summary>
    public static bool RecordTrace { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_TRACE") == "records";

    /// <summary>" cbt &lt;13 records, hex&gt; q &lt;queued endurance, strength per combatant&gt; ar &lt;attack rates&gt;", as the tester reads them in
    /// the original's memory.</summary>
    private static string CombatantDump(Battle battle)
    {
        var records = new byte[Combatant.Count * Darklands.Persistence.CombatantRecords.Size];
        var queued = new byte[Combatant.Count * 2];
        var rates = new byte[Combatant.Count];
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = battle[i];
            Darklands.Persistence.CombatantRecords.Write(c, records.AsSpan(i * Darklands.Persistence.CombatantRecords.Size));
            queued[2 * i] = (byte)c.QueuedEndurance;
            queued[2 * i + 1] = (byte)c.QueuedStrength;
            rates[i] = c.AttackRate;
        }
        return $" cbt {Convert.ToHexString(records)} q {Convert.ToHexString(queued)} ar {Convert.ToHexString(rates)}";
    }

    public static bool OneTickPerPass { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_TICK_PER_PASS") is not null;

    /// <summary>A debugging aid: the clock ticks each battle's set-up takes (stages 1 to 4, see ITacticalView.SetUpTime), in place of
    /// the time that passed, battle after battle ("704,41,1,0;650,40,0"; the last entry goes on; the optional fourth number is the part of the
    /// third that comes before the activations check's clock is read, R014+0x0332), as tools/oracle/lockstep.py measures them
    /// in the original (DARKLANDS_BATTLE_SETUP_TICKS).</summary>
    public static Queue<(int Loads, int FirstFrame, int Late, int BeforeActivations)>? SetUpTicks { get; set; } = ParseSetUpTicks(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_SETUP_TICKS"));

    private static Queue<(int, int, int, int)>? ParseSetUpTicks(string? text) => text is null ? null
        : new(text.Split(';').Select(p => p.Split(',')).Select(p => (int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
            p.Length > 2 ? int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 0,
            p.Length > 3 ? int.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 0)));

    /// <summary>A debugging aid: timer callbacks that came within a pass beyond its one, by the combat clock at the pass's head ("1382,69;1452,67":
    /// the clock was 1382 at the head, 69 more callbacks came during the pass), as tools/oracle/battle_oracle.py measures them in the original
    /// (DARKLANDS_BATTLE_CLOCK_JUMPS): the original's emulated time passes during a pass that computes for long (R014+0x4BFA..0x66FE
    /// in the wild fight), and the timer callbacks of that time count the clock on.</summary>
    /// <summary>A debugging aid: the timer callbacks of the original's loot screens, by the combat clock it was opened at ("2731,7"), as
    /// tools/oracle/lockstep.py takes them from the original's clock jumps (DARKLANDS_BATTLE_LOOT_TICKS): the end test R014+0x08FE runs
    /// fight-recovery R014+0x0DEE before the loot screen R040, during which the timer root 047C:000A counts the clock on.</summary>
    public static Dictionary<int, int>? LootTicks { get; set; } = ParseClockJumps(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_LOOT_TICKS"));

    public static Dictionary<int, int>? ClockJumps { get; set; } = ParseClockJumps(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CLOCK_JUMPS"));

    private static Dictionary<int, int>? ParseClockJumps(string? text) => string.IsNullOrEmpty(text) ? null
        : text.Split(';').Select(p => p.Split(',')).ToDictionary(p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A debugging aid: the same jumps' part that came between the pass's random draws ("1452,67,4:4,5:2": before the pass's 4th draw
    /// (counted from 0 at the pass's head) 4 callbacks came, before the 5th 2 more, the rest after the pass), as battle_oracle.py measures them:
    /// the draws that read the clock (a re-path time) see the clock the original had then (DARKLANDS_BATTLE_CLOCK_JUMPS). The keys -1 and -2 are the
    /// callbacks that came before the tick's scheduler (R014+0xF418's 1DB7:0D36) and melee exchanges (R014+0x2A4A), which test the clock against the
    /// re-path and engage times before any draw.</summary>
    public static Dictionary<int, Dictionary<int, int>>? ClockRises { get; set; } = ParseClockRises(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CLOCK_JUMPS"));

    private static Dictionary<int, Dictionary<int, int>>? ParseClockRises(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var all = new Dictionary<int, Dictionary<int, int>>();
        foreach (var entry in text.Split(';').Select(p => p.Split(',')).Where(p => p.Length > 2))
        {
            all[int.Parse(entry[0], System.Globalization.CultureInfo.InvariantCulture)] = entry.Skip(2).Select(r => r.Split(':'))
                .ToDictionary(r => int.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture), r => int.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        return all;
    }

    /// <summary>A debugging aid: for each combat sheet of the run in turn, the timer callbacks that came while it was open (R022+0x0066 ..
    /// R022+0x03C6, DS:07D3 held) and those that came after it up to the next pass's head ("2310,9;40,0"), as tools/oracle/lockstep.py
    /// measures them in the original (DARKLANDS_BATTLE_SHEET_CALLBACKS); the C# host has no time of its own there in OneTickPerPass.</summary>
    public static Queue<(int During, int After)>? SheetCallbacks { get; set; } = ParseSheetCallbacks(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_SHEET_CALLBACKS"));

    private static Queue<(int, int)>? ParseSheetCallbacks(string? text) => string.IsNullOrEmpty(text) ? null
        : new(text.Split(';').Select(p => p.Split(',')).Select(p => (int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p.Length > 1 ? int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 0)));

    /// <summary>A debugging aid: for each run of the menu bar in a battle in turn, the timer callbacks that came while it ran (root 0000:0000 with
    /// the bar up, the clock held) and after it up to the next pass, as tools/oracle/lockstep.py measures them in the original
    /// (DARKLANDS_BATTLE_MENU_CALLBACKS, "1420,54;..."); the C# host has no time of its own there in OneTickPerPass.</summary>
    public static Queue<(int During, int After)>? MenuCallbacks { get; set; } = ParseSheetCallbacks(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_MENU_CALLBACKS"));

    /// <summary>A debugging aid: the same for each message box of a hot key in a battle (Alt-D / F / M / C: notice-display's wait of 0x3C ticks of the
    /// timer's tick byte), as tools/oracle/lockstep.py measures them in the original (DARKLANDS_BATTLE_BOX_CALLBACKS).</summary>
    public static Queue<(int During, int After)>? BoxCallbacks { get; set; } = ParseSheetCallbacks(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_BOX_CALLBACKS"));

    /// <summary>The callbacks the original had after the last sheet in its pass (SheetCallbacks), for the pass's end.</summary>
    private int _afterSheetCallbacks;

    private (int Loads, int FirstFrame, int Late, int BeforeActivations) _setUpTicks;

    /// <summary>A debugging aid: for each battle taken up from a saved game in turn, the combat clock of the original's first pass after it
    /// ("5720;...", an entry for every battle after the first): the restore R003+0x0CE8 reads the clock from the battle file and the timer callback root 047C:000A counts it on while
    /// the levels are exploded and the set-up loads its files (emulated time), as tools/oracle/lockstep.py reads it from the original's trace
    /// (DARKLANDS_BATTLE_RESUME_CLOCKS). The clock moves there after the set-up.</summary>
    public static Queue<int>? ResumeClocks { get; set; } = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_RESUME_CLOCKS")) ? null
        : new(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_RESUME_CLOCKS")!.Split(';').Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)));

    private bool _resuming;

    private int _resumeLate;

    private static int _setUpsSeen;
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
    private TextPainter? _small;
    private TextPainter? _panelText;
    private TextPainter _panel => _panelText ??= new TextPainter(_ui.Assets.Font(1));
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
        // C: DS:07D6 counted on outside the battle (root 047C:000A runs at every timer callback of the program)
        var keys = _owner.CursorKeys;
        var timerTicks = _ui.Timer.Ticks;
        keys.HeldCallbacks = (ushort)(keys.HeldCallbacks + (timerTicks - keys.TimerTicksAtLeave));
        keys.TimerTicksAtLeave = timerTicks;
        Start(request);
        var previous = _ui.Clock.Milliseconds;
        _ui.PointerVisible = false;
        _hotKey = _ui.GameKey;
        _ui.GameKey = BattleHotKey;
        _ui.BattleStatusLetter = StatusLetter;
        // the battle's menu bar (DS:9419 with the Orders and Attack menus) is the one the game's waits run while the battle lasts
        var menuBar = _ui.MenuBar;
        _ui.MenuBar = _menuBar;
        try
        {
            // orig: R002+0x001B root 047C:0568 at battle-entry's head, R014+0x02F8 root 047C:056F before the loop: the battle's own screen is shown
            // C: a card's picture still waiting for its first text (DS:EE55, GameUi.HoldScreen) is never shown: the battle replaces the screen
            //   (vis10, the city ambush of card 0x24: local/batch/b24fix)
            _ui.ReleaseScreen();
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
                // orig: R014+0x0000 (0E76:0000) runs the end test (R014+0x08FE) and the endurance recovery (R014+0x45AE), then R014+0x3166
                // (the head the tester compares, the player's input); the timer callbacks of a pass come after them
                if (_battle!.IterateHead())
                {
                    break;
                }
                Passes++;
                if (PassTrace is { } trace)
                {
                    trace($"pass clock {_battle!.Battle.Clock} rand {(_state.Random is Darklands.Core.Randomness.MscRandom msc ? msc.State.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : "?")}"
                        + (RecordTrace ? CombatantDump(_battle.Battle) : ""));
                }
                if (OneTickPerPass)
                {
                    _clockFraction = 1;
                    if (ClockJumps is not null && ClockJumps.Remove(_battle!.Battle.Clock, out var jump))
                    {
                        lateTicks = jump;
                    }
                    if (ClockRises is not null && ClockRises.Remove(_battle!.Battle.Clock, out var rises))
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
                new MessageBox(_ui).Show(BattleScreens.ResultText((int)ended), 0x96 * 1000 / 60);
            }
            // C: R002+0x05C1: fight-recovery, then the buffers freed and (R002+0x0710) the able and unconscious lose their action and target
            _battle.RecoverAfterBattle();
            Finish();
            // C: the battle's marching order is the game's (DS:C624 / DS:C629): it stays as the battle left it (its five bytes and the length).
            _battle.StoreMarchingOrder(_state.MarchingOrder);
        }
        finally
        {
            _owner.CursorKeys.TimerTicksAtLeave = _ui.Timer.Ticks;
            _ui.PointerVisible = true;
            _ui.GameKey = _hotKey;
            _ui.MenuBar = menuBar;
            _ui.BattleStatusLetter = null;
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
                var acted = _hotKey?.Invoke(key) ?? false;
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

    /// <summary>Combatant i's hot spot as last drawn (screen pixels, inclusive), null when it has none (tests, the headless host).</summary>
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
        // battle ended (1 with the party where it stood, 2 at that level's start), 3 shows level `Battlefield` (at its start).
        // orig: R002+0x0098..0x00EE checked against the asm; tests: RevisitTests
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
            store.FloorShown = held.Variables[BattleVariables.FloorShown];
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (i < _state.Party.Members.Count && _state.Party.Members[i] is { IsPresent: true, OriginalRecord: { Length: >= Persistence.CombatantRecords.Size } r })
                {
                    var probe = new Combatant(i);
                    Persistence.CombatantRecords.Read(r, probe);
                    store.PartyPlaces[i] = (probe.Position, probe.Facing, probe.SpriteFacing);
                }
            }
        }
        var reuse = request.Mode != 0 && _owner.LastField is { } last && _owner.LastFieldLocation == _state.CurrentLocation;
        var field = reuse ? _owner.LastField! : BattlefieldBuilder.Build(context, request.Battlefield, request.Seed, request.Groups);
        _owner.LastField = field;
        _owner.LastFieldLocation = _state.CurrentLocation;
        var floor = !reuse ? field.StartLevel
            : request.Mode == 3 ? Math.Clamp(request.Battlefield, 0, field.Levels.Count - 1)
            : field.Store.FloorShown >= 0 ? field.Store.FloorShown : field.StartLevel;
        var battle = new Battle(_assets.Tables, _state.Random)
        {
            Persistent = field.Persistent || request.Persistent, DifficultyOf = _state, FloorShown = floor,
            LeaderOf = _state.Party, ActiveMember = _state.Party.LeaderIndex, Behaviour = _state.Behaviour,
        };
        _tiles = BattlefieldTiles.Load(_assets.TileSets, field.Terrain);
        var furniture = _tiles.Furniture.Select(f => f is null ? null : new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY)).ToArray();
        // C: R002+0x0000 sets DS:C1F0 to the battlefield and DS:D6CE to the seed (the battle file keeps them).
        Array.Clear(_placeNotes);
        _battle = new TacticalBattle(battle, field, furniture, _library, this)
        {
            ProgramMemory = _owner.GenerationMemory, BattlefieldType = request.Battlefield, Seed = request.Seed, CursorKeys = _owner.CursorKeys,
            StaleFlags = _state.BattleFile is { } program ? [.. Enumerable.Range(0, Combatant.Count).Select(i => (ushort)(program.Variables[BattleVariables.Flags + 2 * i] | program.Variables[BattleVariables.Flags + 2 * i + 1] << 8))] : null,
            StalePaths = _state.BattleFile is { } heldFile ? (heldFile.Variables[BattleVariables.Paths..(BattleVariables.Paths + Combatant.Count * Combatant.PathLength)],
                heldFile.Variables[BattleVariables.PathIndex..(BattleVariables.PathIndex + Combatant.Count)]) : null,
        };
        _view.PieceFrame = (layer, x, y) => _battle?.Effects.PieceFrame(layer, x, y) ?? -1;
        KeepSeenPositions(battle);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = battle[i];
            c.AnimationSlot = i;
            if (i < _state.Party.Members.Count && _state.Party.Members[i].IsPresent)
            {
                var member = _state.Party.Members[i];
                CombatantSetup.FromMember(c, member);
                _battle.SlotCodes[i] = member.Image.Length > 0 ? member.Image : "F01";
            }
            else
            {
                c.Status = CharacterStatus.Empty;
            }
        }
        _small = new TextPainter(_ui.Assets.Font(3));
        SetPalette(field.Terrain.BackgroundPalette);
        StartCycles();
        // orig: the marching order (DS:C624 / DS:C629) is the game's: the battle starts from it, R016+0x0DA6 keeping it while it is valid
        _battle.LoadMarchingOrder(_state.MarchingOrder);
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
            Persistent = true, DifficultyOf = _state, FloorShown = file.Variables[BattleVariables.FloorShown],
            LeaderOf = _state.Party, ActiveMember = _state.Party.LeaderIndex, Behaviour = _state.Behaviour,
        };
        _tiles = BattlefieldTiles.Load(_assets.TileSets, field.Terrain);
        var furniture = _tiles.Furniture.Select(f => f is null ? null : new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY)).ToArray();
        Array.Clear(_placeNotes);
        _battle = new TacticalBattle(battle, field, furniture, _library, this)
        {
            ProgramMemory = _owner.GenerationMemory, BattlefieldType = request.Battlefield, Seed = request.Seed, CursorKeys = _owner.CursorKeys,
        };
        KeepSeenPositions(battle);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = battle[i];
            c.AnimationSlot = i;
            if (i < _state.Party.Members.Count && _state.Party.Members[i].IsPresent)
            {
                var member = _state.Party.Members[i];
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
        _small = new TextPainter(_ui.Assets.Font(3));
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
            var colours = i < _state.Party.Members.Count ? PartyColours(_state.Party.Members[i].ImagePalette, i)
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
        _owner.GenerationMemory.FloorImage = (byte[])_battle.Level.Image.Clone();
        // the party's collision sizes (DS:C80F / DS:C831) stay in the program's tables for the next battle's placement
        _owner.GenerationMemory.PartySizes = [.. Enumerable.Range(0, Combatant.PartySlots).Select(i => (b[i].HalfWidth, b[i].HalfHeight))];
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
        store.FloorShown = b.FloorShown;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            store.PartyPlaces[i] = b[i].Status == CharacterStatus.Empty ? null : (b[i].Position, b[i].Facing, b[i].SpriteFacing);
        }
    }

    // ITacticalView

    public int CameraX => _view.CameraX;

    public int CameraY => _view.CameraY;

    public bool HasHotSpot(int i) => HotSpot(i) is not null;

    /// <summary>The renderer's clip test of the combatant's sprite box, 4 pixels round it, against the view (0..0x103 × 0..0xC7).</summary>
    // orig: R014+0x9C8C (14F7:347C) draft, called by R014+0x719E with (scrx − 4, scry − 4, w + 8, h + 8)
    public bool CombatantInView(int i)
    {
        if (SpriteBox(i) is not { } box)
        {
            return false;
        }
        int x = box.Left - BattlefieldView.Left - 4, y = box.Top - 4, w = box.Width + 8, h = box.Height + 8;
        const int Right = 0x103, Bottom = 0xC7;
        if (x < 0)
        {
            w += x;
            x = 0;
            if (w <= 0)
            {
                return false;
            }
        }
        if (w + x > Right && Right - x + 1 <= 0)
        {
            return false;
        }
        if (y < 0)
        {
            h += y;
            y = 0;
            if (h <= 0)
            {
                return false;
            }
        }
        return !(h + y > Bottom && Bottom - y + 1 <= 0);
    }

    /// <summary>Where combatant i's sprite is drawn (screen pixels) and its size, null without an animation.</summary>
    private (int Left, int Top, int Width, int Height, int FootX, int FootY, Content.Formats.Battles.CombatAnimation Animation)? SpriteBox(int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        var (code, posture, weapon) = tb.ShownFile(i);
        var animation = _library.Drawn(c.AnimationSlot, code, posture, weapon)
            ?? _library.Drawn(c.AnimationSlot, code, "WK", weapon);
        if (animation is null)
        {
            return null;
        }
        var facing = c.SpriteFacing & 7;
        var (fx, fy) = _view.ToScreen(c.Position.X, c.Position.Y);
        var left = fx - animation.FootX[facing];
        var top = animation.BaseY - animation.Height + fy;
        return (left, top, animation.Widths[facing], animation.Height, fx, fy, animation);
    }

    /// <summary>A sound effect, when the sound options allow it (sound on and n ≥ 10, or 0x12, or music on and 7..9).</summary>
    // orig: R014+0x465E (0E76:465E) draft (asm-verified per spec D §1.11)
    public void Sound(int n) => Darklands.Audio.GameSounds.PlayBattleSound(_ui.Audio, n, _ui.Options.SoundEffects, _ui.Options.Music);

    // orig: R024+0x07A2 (1EF8:07A2) draft (the load and the tile's sprite and offsets; see BattlefieldTiles.Vary)
    public FurnitureShape? VaryFurniture(int tile, string set, int record) =>
        _tiles?.Vary(_assets.TileSets, set, tile, record) is { } f ? new FurnitureShape(f.Footprint, f.Height, f.BoxX, f.BoxY) : null;

    public void Message(int text, int ticks) => ShowMessage(_assets.Executable.String(text), ticks);

    public void FarMessage(int text, int ticks) => ShowMessage(_owner.FarText(text), ticks);

    public void Message(int text, int ticks, int member)
    {
        var c = _battle!.Battle[member];
        ShowMessage((c.Member?.Name ?? "") + _assets.Executable.String(text), ticks);
    }

    /// <summary>notice-display: the box over the battle for <paramref name="ticks"/> timer ticks, the battle's loop waiting meanwhile; then the box is gone.</summary>
    // orig: notice-display root 0000:24BC
    // C: pointer-hide (047C:12C3), the framed box (0000:23E6, which saves the rectangle it covers), wait-ticks(ticks) (0410:0302: a loop
    // C: until the byte DS:07D2 has risen by ticks), the rectangle put back (03EC:000E), pointer-show (047C:1293);
    // C: nothing of the battle runs meanwhile (the callers go on after it: the "USE STAIRS" message at 19CD:0A1C, 19CD:0E70 and R016's
    // C: arrow keys), no key or click is taken (they wait in the BIOS buffer and the driver's counts); the timer callback 047C:000A counts
    // C: every tick: DS:07D1 / 07D2 / 07D6, and the combat clock DS:07D4 unless DS:07D3 holds it. Direct calls of command-mode-end with
    // C: the party split: DS:07D1 +150 at the delay's return, the clock +150 when running (castle-p600) and unmoved when paused.
    // tests: BattleUiTests.AMessageWaitsItsTicksWithTheTimerRunning, BattleCallsTests (R014+0xBCA8 jobs)
    private void ShowMessage(string text, int ticks)
    {
        _message = text;
        _messageUntil = long.MaxValue;
        _messageWaiting = true;
        try
        {
            if (!OneTickPerPass)
            {
                Draw();
                _ui.Delay(ticks * 1000 / 60);
            }
        }
        finally
        {
            _messageWaiting = false;
        }
        _message = null;
        WaitedTicks(ticks);
    }

    /// <summary>Timer ticks spent inside a call that waits on the timer (notice-display's wait-ticks): the callbacks count them, the clock too
    /// unless it is held; the loop's own time starts again after them.</summary>
    // orig: root 047C:000A (see ShowMessage); the clock stands while the pause byte is not 0, as Advance
    private void WaitedTicks(int ticks)
    {
        _resyncClock = true;
        if (_battle!.PauseByte == 0 && OneTickPerPass && (ClockJumps is not null || SetUpTicks is not null))
        {
            // C: a debugging aid: the tester's measured time of a running battle (the clock jumps of its passes, the set-up's late ticks,
            // DARKLANDS_BATTLE_CLOCK_JUMPS / DARKLANDS_BATTLE_SETUP_TICKS) holds the wait's callbacks already
            return;
        }
        CountCallbacks(ticks);
        if (_battle.PauseByte == 0)
        {
            _battle.Battle.Clock = (ushort)(_battle.Battle.Clock + ticks);
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

    /// <summary>
    /// What member-gauge-bars put in each party row when the row was last drawn: a place note ("near a door", DS:0049 ...) in place of the
    /// bars, or null (the bars). The row keeps it until it is drawn again.
    /// </summary>
    // orig: member-gauge-bars root 0000:1A9C, root 0000:1BB7..1CD4 (checked against the asm): when fighter-flags[i] (DS:A696) has bit 0x08 and
    // genuine-fight (DS:A6C1) is 0, the text at the near pointer DS:01A8 (bit 0x02, in a doorway), DS:01AA (0x01, on stairs), DS:01AC (0x04,
    // at a chest), DS:01AE (0x20, in an exit zone) or DS:01B0 (0x40, left the field), the first bit set in that order, replaces the three
    // bars (the values are still drawn); with none of them, the bars. Bit 0x08 is set only while party-place-table (R014+0xDC4E..0xDD17)
    // draws the row of a member who entered a place and while floor-view-paint (R017+0x001E..0x004C) draws all the rows, so the note
    // comes when a member steps into a place (or the view is drawn anew) and stays until the row is drawn again.
    private readonly string?[] _placeNotes = new string?[Combatant.PartySlots];

    /// <summary>A tester's trace (DARKLANDS_PANEL_TRACE): each row drawn, with the combat clock and its note.</summary>
    public static Action<string>? PanelTrace { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_PANEL_TRACE") is null ? null : Console.WriteLine;

    // orig: party-panel-row-set root 0000:1716 (which): the rows of the bits DS:01E4[which] (1 << which), all five for 0x100; each through
    // member-gauge-bars, which chooses the note from the flags as they are at that moment.
    public void RowChanged(int member)
    {
        if (member == -1)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                DrawRowNote(i, 0);
            }
        }
        else if (member is >= 0 and < Combatant.PartySlots)
        {
            DrawRowNote(member, 0);
        }
    }

    // orig: floor-view-paint R017+0x0000..0x0058: bit 0x08 set in fighter-flags[0..4], party-panel-frame-set(0x100) and party-panel-row-set(0x100),
    // then bit 0x08 cleared again: every member who stands in a place shows its note.
    private void FloorViewPanel()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            DrawRowNote(i, TacticalFlags.PlaceChanged);
        }
    }

    private void DrawRowNote(int i, TacticalFlags extra)
    {
        if (_battle is not { } tb)
        {
            return;
        }
        var flags = tb.Battle[i].Flags | extra;
        int? at = null;
        if ((flags & TacticalFlags.PlaceChanged) != 0 && !tb.Battle.RealCombat)
        {
            at = (flags & TacticalFlags.InDoorway) != 0 ? 0x01A8
                : (flags & TacticalFlags.OnStairs) != 0 ? 0x01AA
                : (flags & TacticalFlags.AtChest) != 0 ? 0x01AC
                : (flags & TacticalFlags.InExitZone) != 0 ? 0x01AE
                : (flags & TacticalFlags.LeftField) != 0 ? 0x01B0
                : null;
        }
        _placeNotes[i] = at is { } pointer
            ? _assets.Executable.String(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_assets.Executable.DataSegment(pointer, 2)))
            : null;
        PanelTrace?.Invoke($"row {i} clock {tb.Battle.Clock} {_placeNotes[i] ?? "bars"}");
    }

    // orig: the timer callback root 047C:000A counts the combat clock (DS:07D4) while the set-up of fight-main-loop R014+0x0000 loads its
    // files: here the clock moves on by the real time the set-up took (next to nothing)
    public void SetUpTime(int stage)
    {
        var now = _ui.Clock.Milliseconds;
        if (stage == 0)
        {
            _setUpMark = now;
            // the first battle has no entry; every later one takes its own, a battle taken up from a saved game uses it
            if (_setUpsSeen++ > 0 && ResumeClocks is { Count: > 0 } clocks && clocks.Dequeue() is var first && _resuming)
            {
                // the restore's and the set-up's time comes after the set-up's clock reads (the original's first pass checks the
                // activations at once: R014+0x05B4), up to the first pass
                _setUpTicks = (0, 0, 0, 0);
                _resumeLate = (ushort)(first - _battle!.Battle.Clock);
                return;
            }
            if (SetUpTicks is { Count: > 0 } given)
            {
                _setUpTicks = given.Count > 1 ? given.Dequeue() : given.Peek();
            }
            return;
        }
        if (SetUpTicks is not null || (_resuming && ResumeClocks is not null))
        {
            _clockFraction = stage switch
            {
                1 => _setUpTicks.Loads,
                2 => _setUpTicks.FirstFrame,
                3 => _setUpTicks.BeforeActivations,
                _ => _setUpTicks.Late - _setUpTicks.BeforeActivations,
            };
            Advance(0);
        }
        else if (stage >= 3)
        {
            return;
        }
        else
        {
            Advance(now - _setUpMark);
        }
        _setUpMark = now;
    }

    // orig: floor-view-paint R017+0x0000 (1EF8:0000) draft with who −1: the view around the centre kept (DS:C866 / DS:C886, which a
    // placement may have set); DS:E160 = 1, so the next fight-draw forgets the last frame's logged sprites (DS:E176 = 0)
    public void MapChanged()
    {
        ApplyViewOrigin();
        _viewRedrawn = true;
        FloorViewPanel();
    }

    // orig: floor-view-paint R017+0x0000 draft with who ≥ 0: the centre becomes the combatant's cell (DS:E233 = x >> 5, DS:E235 = y >> 4)
    public void Recentre(int who)
    {
        var c = _battle!.Battle[who];
        _battle.ViewOrigin = null;
        _view.CentreColumn = c.Position.Column;
        _view.CentreRow = c.Position.Row;
        _viewRedrawn = true;
        FloorViewPanel();
    }

    // orig: fight-tick R014+0x1065..0x10A7 (checked against the asm): scry < 0x14 or > 0xA0, or scrx < 0x14 or > 0xDC, of DS:17B0, as
    // fighter-screen-position R014+0x99F6 left them in the last frame (the sprite's top left in the view)
    public bool KeepInView(int who)
    {
        var (sx, sy) = _battle!.Battle[who].ScreenPlace;
        if (sy < 0x14 || sy > 0xA0 || sx < 0x14 || sx > 0xDC)
        {
            Recentre(who);
            return true;
        }
        return false;
    }

    /// <summary>The loot screen over the battle (the party records are the combatants): what the party leaves stays in the list.</summary>
    // orig: loot-view R014+0xE3C0 (19CD:2E50) unmatched → R040 loot-split-entry (the
    // combat buffers freed and reloaded around it are the original's memory management)
    public void OpenLoot(int member, List<ItemStack> list, Money money)
    {
        // C: the pointer is the driver's: the loot screen keeps the battle's party column (R040), never the battle's pointer drawn over it
        ErasePointer();
        if (LootTicks is not null && LootTicks.Remove(_battle!.Battle.Clock, out var ticks))
        {
            _clockFraction = ticks;
            Advance(0);
        }
        // orig: loot-view: palette-keep(0, DS:CA55) before loot-split-entry (R040, which shows BUYSELL.PIC and sets its palette),
        // palette-reload(DS:CA53, DS:CA55) after it; then loot-pile-opened R014+0xE278 redraws the battle:
        // floor-view-paint-overlay-entry(DS:17B0) (the view centred on the active member) and fight-draw.
        var palette = (uint[])_ui.Canvas.Screen.Palette.Clone();
        // orig: loot-view R014+0xE4F6: DS:07D8 = 0 (the loot screen's palette), R014+0xE6BB: DS:07D8 = 1 after the palette restore
        _ui.RunCycles(false);
        _ui.PaletteSet();
        // orig: the party records are the combatants, so the loot screen (R040) and its panel see the wounds and skills of the fight at once (as the
        // character sheet does, see CharacterSheet)
        _battle!.MembersFromCombat();
        PacksToMembers();
        // orig: R014+0xE5AF..0xE5B2: the panel rows (root 0000:1716) with 0x100 before the loot screen, the frames not: each row's 0x26 lines are
        //   copied to the screen, its last line and the frames stay as the battle drew them (vis10 session D-56a675: the shields, the order letters
        //   and the battle's hatching on the frame line)
        new PartyPanel(_ui).DrawRowsAsCopied(_ui.Canvas, _state.Party);
        // orig: loot-view R014+0xE410..0xE42D: the menu titles and DS:9893 are 2 while the loot screen is up, put back at
        //   R014+0xE716..0xE72B (as around the combat sheet)
        var menus = GreyMenusForAScreen();
        try
        {
            _owner.TakeLoot(list, money);
        }
        finally
        {
            menus();
        }
        // orig: the party record is the combatant record: what the screen changed in a member comes back whole, not only the pack: an item
        // put on the pile that was the equipped one is unequipped (R031+0x5B38 through item-take-single) in the record itself (+0x22, +0x51,
        // +0x4B/+0x4C, +0x5C and the encumbrance +0x49), which the battle reads afterwards
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (_battle!.Battle[i].Member is { } m)
            {
                CombatantSetup.Refresh(_battle.Battle[i], m);
            }
        }
        _battle!.TakeUpEnemyAnimations();
        // orig: R014+0xE5E3: the releases made on the loot screen are taken from the driver and forgotten
        _ui.TakeLeftReleases();
        _releases = 0;
        palette.CopyTo(_ui.Canvas.Screen.Palette, 0);
        _ui.PaletteSet();
        _ui.RunCycles(true);
        _underPointer = null;
        var c = _battle!.Battle[_battle.Battle.ActiveMember];
        _view.CentreColumn = c.Position.Column;
        _view.CentreRow = c.Position.Row;
        Draw();
    }

    /// <summary>A message in a member's panel row (or the whole column for −2), waiting for a key or a left click.</summary>
    // orig: R021+0x01E2 (1EF8:01E2) matched: the row background DS:A626 at (3, row × 40 + 1)
    // (the column: the strips DS:A628 and y 0x51), the text wrapped in 0x37 pixels from y + 4, then `while (!key-waiting-test() && !(mouse-button-state & 1))
    // menu-bar-loop(...)` and the button waited out
    public void MemberMessage(int member, string text, int color)
    {
        var canvas = _ui.Canvas;
        var sprites = _ui.Assets.CommonSprites;
        int y;
        if (member == -2)
        {
            var column = new Clip(3, 1, 3 + 0x36, 1 + 0xC6);
            for (var v = 0; v < 0xC6; v += 0xB)
            {
                SpriteDrawing.Draw(canvas, sprites.ColumnBackground, 3, v, column);
            }
            y = 0x51;
        }
        else
        {
            y = member * 40 + 1;
            SpriteDrawing.Draw(canvas, sprites.RowBackground, 3, y);
        }
        var layout = new CardTextLayout(_ui.Text);
        layout.Draw(canvas, TextPainter.Encode(text), 3, y + 4, 0x37,
            new CardTextLayout.Settings { Color = (byte)color, Leading = 0, Alignment = TextAlignment.Center });
        _ui.PointerVisible = true;
        // orig: R021+0x02A6 matched C: `while (!root 047C:073D() && !(mouse-button-state & 1)) root 0000:0000(&DS:9419, 0);` (the key stays
        // waiting for the battle's keys), then R021+0x02C3 `while (mouse-button-state & 1);` (the release is counted: the next pass of the
        // mouse handler takes it as a click); tests: BattleUiTests
        while (!_ui.KeyWaiting && !_ui.Mouse.Left)
        {
            _ui.Present();
            if (_ui.Mouse.Right)
            {
                RunMenu(() => _menuBar!.Run(keyboard: false));
            }
            _ui.Idle();
        }
        _ui.WaitForButtonRelease();
        _ui.PointerVisible = false;
    }

    public int ScreenTileOf(int cell)
    {
        for (var k = 0; k < BattlefieldView.Rows * BattlefieldView.Columns; k++)
        {
            var (dx, dy) = BattlefieldView.SlotOffset(k / BattlefieldView.Columns, k % BattlefieldView.Columns);
            if ((_view.CentreColumn + dx) * 256 + _view.CentreRow + dy == cell)
            {
                return k;
            }
        }
        return 0x8C;
    }

    // Input

    /// <summary>The menu bar of the battle (Game, Orders, Attack, Party): the game's bar over the battle's menus.</summary>
    private MenuBar? _menuBar;

    /// <summary>The bar's titles and entries over the battle's menu state.</summary>
    // orig: the battle menus of R000+0x083E (0E76:083E) draft, bytes match: their texts (DS strings), keys and handlers; each entry's enable
    // byte is the battle's (DS:9607 for Orders, DS:97DB for Attack), each title's carries-items-test too; tests: BattleUiTests
    private MenuBar BuildMenuBar()
    {
        var tb = _battle!;
        var m = tb.Menus;
        var exe = _assets.Executable;
        string S(int at) => exe.String(at);
        static void Nothing()
        {
        }
        MenuBar.Item O(OrdersItem item, Func<string> text, string key, Action run) => new(text, true, key, run, State: () => m.Orders[(int)item]);
        MenuBar.Item A(AttackItem item, Func<string> text, string key, Action run) => new(text, true, key, run, State: () => m.Attack[(int)item]);
        string With(int at, int pos, string suffix)
        {
            var t = S(at).TrimStart('\x01', '\x02').TrimEnd();
            var text = S(at).TrimStart('\x01', '\x02');
            return (text.Length > pos ? text[..pos] : t) + suffix;
        }
        // C: the Game texts of R000+0x083E start with their tick byte too (6 pixels wide, so the dropdown dropdown-open root 0000:07D4
        // C: opens is 6 pixels wider than the words, and its sub-menu with it): 2 for Save Game, Load Saved Game, Difficulty, Pause and Quit
        // C: to DOS; Show Changes, Music and Sound FX as saint-notices-switch / music-switch / sound-switch set them (*DS:995A, *DS:996E,
        // C: *DS:9982); Basic / Standard / Expert as difficulty-display root 0000:1438 does (*DS:99EC / *DS:9A02 / *DS:9A18). They are read
        // C: from the options when drawn, as MenuBar.ForGame's (its DIVERGENCE about the Show Changes tick across a load holds here too).
        var options = _ui.Options;
        var difficulty = new List<MenuBar.SubItem>
        {
            new(() => Tick(_state.Difficulty == Difficulty.Basic) + "Basic", true, "", 0, () => SetDifficulty(Difficulty.Basic)),
            new(() => Tick(_state.Difficulty == Difficulty.Standard) + "Standard", true, "", 0, () => SetDifficulty(Difficulty.Standard)),
            new(() => Tick(_state.Difficulty == Difficulty.Expert) + "Expert", true, "", 0, () => SetDifficulty(Difficulty.Expert)),
        };
        var game = new List<MenuBar.Item>
        {
            new(() => Tick(false) + "Save Game", true, "\u000fS", SaveGame, State: () => m.SaveGame),
            new(() => Tick(false) + "Load Saved Game", true, "\u000fL", LoadGame, State: () => m.LoadGame),
            new(() => Tick(false) + "Difficulty", true, "\u000fD", Nothing, difficulty),
            new(() => Tick(_state.ShowSaintlyChanges) + "Show Changes", true, "\u000fC", () => _hotKey?.Invoke(Keys.AltC)),
            new(() => Tick(options.Music) + "Music", true, "\u000fM", () => _hotKey?.Invoke(Keys.AltM)),
            new(() => Tick(options.SoundEffects) + "Sound FX", true, "\u000fF", () => _hotKey?.Invoke(Keys.AltF)),
            new(() => Tick(false) + "Pause", true, "\u000fP", () => _hotKey?.Invoke(Keys.AltP)),
            new(() => Tick(false) + "Quit to DOS", true, "\u000fQ", () => _hotKey?.Invoke(Keys.AltQ)),
        };
        // C: every Orders text starts with its tick byte (font 1: 1 a tick, 2 none, both 6 pixels wide): 2, but Travel As Group's (DS:07AA) and
        // C: Travel Single File's (DS:07BC), which R016+0x0BD6 / R016+0x0CDC set to 1 for the mode turned on and every other writer (R002+0x0000,
        // C: R014+0x0000 at real combat, R037+0x8B76) to 2 with travel-mode 0: the tick is travel-mode's
        static string Tick(bool on) => on ? "" : "";
        var orders = new List<MenuBar.Item>
        {
            O(OrdersItem.Resume, () => "Resume", "", Resume),
            O(OrdersItem.Finished, () => "" + m.Giver + S(BattleScreens.FinishedText), "", () => tb.LeaveOrderMode()),
            O(OrdersItem.EnemyInfo, () => "Enemy Info", " E", EnemyInfo),
            O(OrdersItem.Walk, () => S(0x67C).TrimEnd(), " W", () => tb.ChooseOrder(1)),
            O(OrdersItem.Flee, () => S(0x690).TrimEnd(), " F", () => tb.ChooseOrder(1, flee: true)),
            O(OrdersItem.Halt, () => "Halt", " H", () => tb.Halt(tb.Battle.Leader)),
            O(OrdersItem.Group, () => Tick(tb.MoveMode == 1) + "Travel As Group", " G", tb.ToggleGroup),
            O(OrdersItem.SingleFile, () => Tick(tb.MoveMode == 2) + "Travel Single File", " Q", tb.ToggleSingleFile),
            O(OrdersItem.UseDoor, () => "" + With(0x6AA, 8, m.DoorSuffix), " U", () => tb.UseKey(tb.Selected)),
            O(OrdersItem.UseStairs, () => "" + With(0x6BA, 10, m.StairsSuffix), " U", () => tb.UseKey(tb.Selected)),
            O(OrdersItem.OpenChest, () => "" + With(0x6CC, 10, m.ChestSuffix), " O", () => tb.OpenKey(tb.Selected)),
            O(OrdersItem.PickLock, () => "" + With(0x6DE, 9, m.PickSuffix), " P", () => tb.PickKey(tb.Selected)),
            O(OrdersItem.DissolveLock, () => "" + With(0x6F0, 13, m.DissolveSuffix), " D", () => tb.DissolveKey(tb.Selected)),
            O(OrdersItem.DisarmTrap, () => S(m.DisarmText), " D", () => tb.DissolveKey(tb.Selected)),
            O(OrdersItem.Surrender, () => "Surrender (All)", " S", Surrender),
            // orig: R016+0x13CA (1EF8:13CA) matched (R016+0x13CA): R016+0x1590 with 9
            O(OrdersItem.LootBodies, () => "Loot Bodies", " L", () => tb.ChoosePlace(PlaceAction.TakeLoot)),
            O(OrdersItem.Exit, () => "" + With(0x726, 16, m.ExitSuffix), " X", tb.ExitKey),
            O(OrdersItem.Cancel, () => "Cancel Giving Order", "", tb.CancelOrder),
        };
        // orig: the Attack items' functions DS:97DB (R000+0x083E), each R016+0x156E(action), matched: R016+0x130A (0x12), R016+0x1314 (0x0A)
        // orig: R016+0x131E (6), R016+0x1328 (0x22), R016+0x136E (0x80), matched; the Throw item's own function does nothing
        var attack = new List<MenuBar.Item>
        {
            new(() => "Throw", true, "T>", Nothing, State: () => m.Attack[(int)AttackItem.Throw], Subs: ThrowEntries),
            A(AttackItem.Standard, () => "Std Attack ", "A", () => tb.ChooseOrder(0x12)),
            A(AttackItem.Vulnerable, () => "Vulnerable", "V", () => tb.ChooseOrder(0x0A)),
            A(AttackItem.Berserk, () => "Berserk", "B", () => tb.ChooseOrder(0x06)),
            A(AttackItem.Parry, () => "Parry", "P", () => tb.ChooseOrder(0x22)),
            A(AttackItem.Missile, () => "Use Missile", "M", () => tb.ChooseOrder(0x80)),
        };
        var party = new List<MenuBar.Item>
        {
            new(() => "Party Info", true, "\x0a", () => _owner.PartyInfo?.Invoke(), State: () => m.PartyInfo),
            new(() => "Change Marching Order", true, "\u000fO", ChooseMarchingOrder, State: () => m.MarchingOrder),
        };
        // C: the words before the item tables (the keyboard's Right with no item chosen) as outside a battle: MenuBar.BeforeGameItems
        MenuBar? bar = null;
        bar = new MenuBar(_ui,
        [
            new("Game", true, game, BeforeItems: () => MenuBar.BeforeGameItems(_state)),
            new("Orders", true, orders, () => m.OrdersMenu),
            new("Attack", true, attack, () => m.AttackMenu),
            new("Party", true, party, () => m.PartyMenu, () => bar!.DropdownRight),
        ])
        {
            QuitToDos = () => _hotKey?.Invoke(Keys.AltQ),
        };
        return bar;

        // orig: the Throw list's functions DS:9A47[k] (R000+0x083E), each R016+0x152E(k) (k = 0..19: line k's potion and quality, DS:9A47[k].f9 / f8):
        // orig: R016+0x1466, R016+0x1470, R016+0x147A, R016+0x1484, R016+0x148E matched
        // orig: R016+0x1498, R016+0x14A2, R016+0x14AC, R016+0x14B6, R016+0x14C0 matched
        // orig: R016+0x14CA, R016+0x14D4, R016+0x14DE, R016+0x14E8, R016+0x14F2 matched
        // orig: R016+0x14FC, R016+0x1506, R016+0x1510, R016+0x151A, R016+0x1524 matched
        IReadOnlyList<MenuBar.SubItem> ThrowEntries() => [.. m.ThrowLines.Select(l => new MenuBar.SubItem(
            () => PotionMenuName(l.Potion), true, $"q{l.Quality}", l.Count, () => tb.ChooseThrow(l.Potion, l.Quality)))];
    }

    /// <summary>A Throw line's name (DS:114A[2 + potion]): the short name of items 0x5F.. read from DARKLAND.LST at the battle's start.</summary>
    // orig: R002+0x1762 (0E76:1762) unmatched: ten records at 0x1115 (items 0x5F..0x68),
    // the byte at +0x1D zeroed, so the name (+0x14) is at most 9 characters; R019+0x0C80 puts DS:114E[id] in the menu line
    private string PotionMenuName(int potion)
    {
        if (potion is < 0 or > 9 || potion + 0x5F >= _assets.Items.Count)
        {
            return "";
        }
        var name = _assets.Items[potion + 0x5F].ShortName;
        return name.Length > 9 ? name[..9] : name;
    }

    /// <summary>Save Game (Alt-S): while DS:9920 offers it, the game's save in its battle form.</summary>
    // orig: R037+0x2A68 (1462:2A68) draft through its thunk, from the hot keys (root 0000:0CAA) and the Game menu when DS:9920 is 1; the
    // battle-state save R003+0x00DC ends with the level switch to the shown level
    private void SaveGame()
    {
        if (_battle!.Menus.SaveGame == 1 && _owner.Save is { } save)
        {
            save(_battle);
            _battle.SwitchLevel(_battle.Battle.FloorShown);
        }
    }

    /// <summary>Load Saved Game (Alt-L): while DS:9934 offers it (always in a battle), the game's load; a game loaded leaves the battle.</summary>
    // orig: R037+0x8B76 (1C7D:09C6) matched through its thunk, its branch for a battle (DS:907A = 1: the battle's
    // buffers freed, its flags reset, then the load screen and the jump back into the main loop)
    private void LoadGame()
    {
        if (_battle!.Menus.LoadGame == 1)
        {
            _owner.Load?.Invoke();
        }
    }

    /// <summary>difficulty-easy-set / _standard / _expert (root 0000:1408..0x1428): the game's difficulty, which the battle reads too, and its box.</summary>
    // C: the battle bar's Difficulty sub-items (DS:99EC[0..2], filled by the battle menus' set-up) hold difficulty-easy-set / _standard /
    //   _expert, the same functions as the bar outside a battle (HotKeys.SetDifficulty): difficulty-setting = 0 / 1 / 2, then difficulty-display(1):
    //   "Difficulty is now set to " + the name in notice-display(text, 0x3C) (text colour 1, box colour 0xFF, centred in the card window, the
    //   pointer hidden, 60 ticks of DS:07D2 with nothing else running). menu-bar-loop puts the card window's colour and font back before the
    //   sub-item's call (root 0000:019D..0x01A9), so the box is in the font it found (GameUi.CardWindowFont), not the hot keys' font 1;
    //   vis10 session tleads-battle-difficulty: the original's box and this one identical; tests: BattleUiTests
    private void SetDifficulty(Difficulty d)
    {
        // C: the battle's difficulty is the game's (Battle.DifficultyOf)
        _state.Difficulty = d;
        // C: the box's wait counts in the bar's run (RunMenu: the menu's measured callbacks hold it)
        new MessageBox(_ui).Show(HotKeys.DifficultyText(d));
    }

    private void Resume()
    {
        var tb = _battle!;
        // C: the Orders item Resume calls R016+0x1436 through root 09C0:1941 with no test of DS:A695; its menu state (DS:9609) is 1 from a
        // pause to its resume, so it is chosen with A695 1 or 0x11 (after Esc with nobody selected), both of which TogglePause resumes
        if (tb.PauseByte != 0)
        {
            tb.TogglePause();
        }
    }

    /// <summary>
    /// root 0000:0000 in the battle: the hot keys for a waiting key (F10 starts the keyboard menu), then the bar while the right
    /// button is held or the keyboard menu lasts, the battle standing still meanwhile. True when the bar was up.
    /// </summary>
    // orig: root 0000:0000 (DS:07D3 = 1 while it loops) with root 0000:0CAA for a waiting key (case 0x4400: DS:9C26 = 1,
    // DS:9C2F = 0, DS:9C3C = DS:9C41 = -1); tests: BattleUiTests
    private bool MenuBarRun()
    {
        // orig: root 0000:001E..0x0033: these hot keys run with the card window's font 1 (GameUi.CardWindowFont); R016's own call keeps it
        _ui.InMenuBarLoop(() =>
        {
            HotKeysForWaitingKey();
            return 0;
        });
        if (_unfoldedMenu is { } unfolded)
        {
            // C: the T key's flags (DS:9C26 = 1, DS:9C2F = 2, DS:A394 = 3) open the bar here, at the next root 0000:0000 of the battle's loop
            _unfoldedMenu = null;
            RunMenu(() => _menuBar!.RunUnfolded(unfolded));
            _keyboardMenu = false;
            return true;
        }
        if (!_ui.Mouse.Right && !_keyboardMenu)
        {
            return false;
        }
        RunMenu(() => _menuBar!.Run(keyboard: _keyboardMenu));
        _keyboardMenu = false;
        return true;
    }

    /// <summary>T seen in the battle's keys: the next root 0000:0000 runs the bar unfolded to this menu (Attack, at Throw's list).</summary>
    private int? _unfoldedMenu;

    /// <summary>The battle loop's passes so far (R014+0x0000's iterations past the end test).</summary>
    public int Passes { get; private set; }

    /// <summary>The pass the menu bar last came up in, and the pass the last T (the Throw list) was read in.</summary>
    public int MenuPass { get; private set; } = -1;

    public int ThrowKeyPass { get; private set; } = -1;

    /// <summary>A menu bar run from the keys (T): the clock stood still meanwhile.</summary>
    private bool _resyncClock;

    /// <summary>F10 seen by the hot keys: the next root 0000:0000 is in keyboard menu mode.</summary>
    private bool _keyboardMenu;

    /// <summary>The bar's loop with the pointer of the screens shown (the battle's own cursor is not drawn meanwhile).</summary>
    // orig: root 0000:0000: `if (mouse-cursor-default != mouse-cursor-now && DS:907A == 1 && DS:9C4C == 1) root 047C:124F(0, 0, ...)` while the bar is up
    // C: root 0000:0000 holds the combat clock (DS:07D3 = 1 at 0x003D, 0 at 0x01CB) while the bar is up; the timer callbacks go on
    // C: the battle's pointer is the driver's (DS:907A == 1: root 047C:124F swaps it for the default pointer shape at the mouse, restoring what it covered):
    //   it never stays in the picture under the bar or a box the bar's items show (vis10 session tleads-battle-difficulty; tests: BattleUiTests)
    private void RunMenu(Action run)
    {
        MenuPass = Passes;
        ErasePointer();
        _ui.PointerVisible = true;
        var opened = _ui.Clock.Milliseconds;
        try
        {
            run();
        }
        finally
        {
            _ui.PointerVisible = false;
        }
        HeldClockTime(_ui.Clock.Milliseconds - opened);
        if (OneTickPerPass && MenuCallbacks is { Count: > 0 } measured)
        {
            var (during, after) = measured.Dequeue();
            CountCallbacks(during);
            if (_battle!.PauseByte != 0)
            {
                // as after a sheet: a running battle's callbacks after the bar raise the clock (the clock jumps list them)
                _afterSheetCallbacks += after;
            }
        }
    }

    /// <summary>root 0000:0CAA for a waiting key: a key it knows is taken and acted on, any other stays waiting for the battle's keys.</summary>
    private void HotKeysForWaitingKey()
    {
        if (!_ui.KeyWaiting || !_ui.TryReadKey(out var key))
        {
            return;
        }
        if (key == Keys.F12 && _ui.ScreenshotFolder is not null)
        {
            _ui.SaveScreenshot();
            return;
        }
        if (key == Keys.F10)
        {
            _keyboardMenu = true;
            return;
        }
        // C: the driver's pointer is never in the picture a hot key's box or screen is drawn over (vis10 session D-b797f7: the Alt-S comment
        //   dialog shows none); the pass's own draw puts it back when the key turns out to be the battle's
        ErasePointer();
        if (!BattleHotKey(key))
        {
            _ui.PushBackKey(key);
        }
    }

    /// <summary>
    /// The battle's keys, once a pass when a key waits or a direction key is held: the hot keys first, then the key; with no key
    /// waiting, the direction of the held key as an arrow (Home/PgUp/End/PgDn for the diagonals).
    /// </summary>
    // orig: R016+0x0000 draft, checked against the asm for the arrows and Enter: `if (root 047C:073D()) { root 0000:0CAA(); if
    // (root 047C:073D()) k = root 047C:0744(); } else k = the arrow of the signs of DS:0880 / DS:0881`; tests: BattleUiTests
    private void CombatKeys()
    {
        var k = 0;
        if (_ui.KeyWaiting)
        {
            HotKeysForWaitingKey();
            if (_ui.KeyWaiting && _ui.TryReadKey(out var key))
            {
                k = key;
            }
        }
        else
        {
            k = ArrowOf(_ui.Directions.X, _ui.Directions.Y);
        }
        if (k != 0)
        {
            Key(k);
        }
    }

    /// <summary>The arrow key of a direction (the signs of x and y), 0 for none.</summary>
    private static int ArrowOf(int x, int y) => (x, y) switch
    {
        ( < 0, < 0) => Keys.Home,
        ( < 0, > 0) => Keys.End,
        ( > 0, < 0) => Keys.PageUp,
        ( > 0, > 0) => Keys.PageDown,
        ( < 0, _) => Keys.Left,
        ( > 0, _) => Keys.Right,
        (_, < 0) => Keys.Up,
        (_, > 0) => Keys.Down,
        _ => 0,
    };

    /// <summary>DS:E094: the combat tick the pointer last moved in. The hover (DS:D728), the keyboard's click (DS:D73B), the click when
    /// the key is let go (DS:D947) and the timer callbacks since the keys were last let go (DS:07D6) are the program's CursorKeys.</summary>
    private int _pointerTick = -1;

    /// <summary>
    /// The cursor keys move the pointer (order mode, travelling as a group): once a combat tick while a direction key is held, the
    /// pointer moves 1, 3, 5 or 10 pixels the longer it is held (within x 0x3D..0x13F, y 0..0xC7) and the hover acts as with the
    /// button held; letting the key go is a click at the pointer (not when travelling as a group). A waiting key is eaten when the
    /// pointer moved.
    /// </summary>
    // orig: R014+0x35FC draft, checked against the asm (its jump table and clamps): t = DS:07D6 - DS:D945 (is 0): speed 1 up to 0x2D,
    // 3 up to 0x4B, 5 up to 0x5A, else 10; root 047C:1266; `if (root 047C:073D()) root 047C:0744();`; tests: BattleUiTests
    // C: DS:07D6 counts every timer callback of the program (paused too, outside the battles too) and only this function clears it, so a
    // key already held when order mode begins starts at the speed of the time since the last let-go, in this battle or an earlier one
    // (or since the program's start): t is a signed int, so a count past 0x7FFF is slow again.
    private void CursorKeys()
    {
        var tb = _battle!;
        var keys = tb.CursorKeys;
        var (dx, dy) = (_ui.Directions.X, _ui.Directions.Y);
        if (dx == 0 && dy == 0)
        {
            keys.Hover = false;
            if (keys.ClickOnRelease)
            {
                keys.Click = true;
                keys.ClickOnRelease = false;
            }
            // C: R014+0x38AC..0x38B1: DS:07D6 = DS:D945 = 0
            keys.HeldCallbacks = 0;
            keys.ReleasedAt = 0;
            return;
        }
        var key = 0;
        if (_pointerTick != tb.Ticks)
        {
            _pointerTick = tb.Ticks;
            keys.Hover = true;
            if (tb.MoveMode != 1)
            {
                keys.ClickOnRelease = true;
            }
            key = ArrowOf(dx, dy);
        }
        var t = (short)(keys.HeldCallbacks - keys.ReleasedAt);
        var speed = t <= 0x2D ? 1 : t <= 0x4B ? 3 : t <= 0x5A ? 5 : 10;
        var (mx, my) = (_ui.Mouse.X, _ui.Mouse.Y);
        int Up() => my - speed >= 0 ? my - speed : 0;
        int Down() => speed + my > 0xC7 ? 0xC7 : speed + my;
        int LeftX() => mx - speed >= 0x3D ? mx - speed : 0x3D;
        int RightX() => speed + mx > 0x13F ? 0x13F : speed + mx;
        switch (key)
        {
            case Keys.Up: _ui.MovePointer(mx, Up()); break;
            case Keys.Down: _ui.MovePointer(mx, Down()); break;
            case Keys.Left: _ui.MovePointer(LeftX(), my); break;
            case Keys.Right: _ui.MovePointer(RightX(), my); break;
            case Keys.Home: _ui.MovePointer(LeftX(), Up()); break;
            case Keys.End: _ui.MovePointer(LeftX(), Down()); break;
            case Keys.PageDown: _ui.MovePointer(RightX(), Down()); break;
            case Keys.PageUp: _ui.MovePointer(RightX(), Up()); break;
            default: return;
        }
        if (_ui.KeyWaiting)
        {
            _ui.TryReadKey(out _);
        }
    }

    /// <summary>A key of the battle.</summary>
    // orig: fight-keys R016+0x0000 (1EF8:0000) draft (each case with its enable test)
    private void Key(int key)
    {
        var tb = _battle!;
        var m = tb.Menus;
        // orig: R016+0x00FC.. (and R016+0x00B5.. with the party split) switch on the whole key word (checked against the asm, every case): a
        // letter is its key's two words (scan code with the small and the capital letter, as Keys.ForCharacter makes them), F1..F5 and
        // Ctrl-F1..F5 their own words, 1..5 the top row's (0x0231 .. 0x0635); the keypad's digits (Num Lock on, or Shift with it off:
        // 0x4737 .. 0x5133) go to the scrolling part of the arrows' cases without the Shift test (R016+0x02A0, 0x0304, 0x0360, 0x03BC, 0x0418,
        // 0x04C2, 0x04C7, 0x02A4), its 5 (0x4C35) is no case.
        var scroll = key switch
        {
            0x4838 => 0,
            0x4939 => 1,
            0x4D36 => 2,
            0x5133 => 3,
            0x5032 => 4,
            0x4F31 => 5,
            0x4B34 => 6,
            0x4737 => 7,
            _ => -1,
        };
        var function = (key & 0xFF) == 0 && key is >= Keys.F1 and <= Keys.F5;
        var ctrlFunction = (key & 0xFF) == 0 && key is >= Keys.CtrlF1 and <= Keys.CtrlF5;
        var digit = key is >= 0x0231 and <= 0x0635 && Keys.ForCharacter(Keys.CharacterOf(key)) == key;
        var letter = char.IsAsciiLetter(Keys.CharacterOf(key)) && Keys.ForCharacter(Keys.CharacterOf(key)) == key ? char.ToUpperInvariant(Keys.CharacterOf(key)) : ' ';
        if (tb.Battle.PartyFloor != tb.Battle.FloorShown)
        {
            // The party is split over two levels (R016+0x00AC: DS:C623 != DS:11CA): every arrow word, the keypad's digits too and Shift or not,
            // gives the message (R016+0x00EA), U takes the stairs back (R016+0x014E), a few keys go on to the switch.
            if (scroll >= 0 || key is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            {
                Message(TacticalBattle.UseStairsText, 0x96);
                return;
            }
            if (letter == 'U')
            {
                tb.UseKey(tb.Selected);
                return;
            }
            // DORMANT: 0x1000 (Alt-Q) asks "Quit to DOS?" here (R016+0x0194, root 0000:13A0): the hot keys take Alt-Q first.
            if (!(key is Keys.Escape or Keys.Enter or Keys.Space || function || ctrlFunction || digit))
            {
                return;
            }
        }
        if (scroll >= 0)
        {
            ScrollView(scroll);
            return;
        }
        switch (key)
        {
            case Keys.Up:
                Arrow(0);
                return;
            case Keys.PageUp:
                Arrow(1);
                return;
            case Keys.Right:
                Arrow(2);
                return;
            case Keys.PageDown:
                Arrow(3);
                return;
            case Keys.Down:
                Arrow(4);
                return;
            case Keys.End:
                Arrow(5);
                return;
            case Keys.Left:
                Arrow(6);
                return;
            case Keys.Home:
                Arrow(7);
                return;
            case Keys.Space:
                tb.TogglePause();
                return;
            case Keys.Escape:
                if (tb.Paused)
                {
                    tb.CancelOrder();
                }
                return;
            case Keys.Enter:
                // orig: R016+0x0734 (checked against the asm): `if (DS:D7FE == 1 && travel-mode == 1) DS:D73B = 1; else if (travel-mode == 0)
                // R014+0xBCA8();`: travelling as a group, Enter clicks at the pointer
                if (tb.CursorKeysOn && tb.MoveMode == 1)
                {
                    tb.CursorKeys.Click = true;
                }
                else if (tb.MoveMode == 0)
                {
                    tb.LeaveOrderMode();
                }
                return;
            case Keys.AltO:
                // C: 0x1800 (Alt-O) in the battle's own switch: everybody stops and real combat ends (the hot key takes it first when allowed).
                tb.StopEverything();
                return;
        }
        if (function)
        {
            CharacterSheet((key - Keys.F1) >> 8);
            return;
        }
        if (ctrlFunction)
        {
            tb.ActivateMember((key - Keys.CtrlF1) >> 8);
            return;
        }
        if (digit)
        {
            tb.SelectMember(Keys.CharacterOf(key) - '1');
            return;
        }
        var selected = tb.Selected != -1;
        switch (letter)
        {
            case 'A':
                if (m.On(AttackItem.Standard) && selected)
                {
                    tb.ChooseOrder(0x12);
                }
                return;
            case 'B':
                if (m.On(AttackItem.Berserk) && selected)
                {
                    tb.ChooseOrder(0x06);
                }
                return;
            case 'D':
                tb.DissolveKey(tb.Selected);
                return;
            case 'E':
                if (m.On(OrdersItem.EnemyInfo))
                {
                    EnemyInfo();
                }
                return;
            case 'F':
                if (tb.Paused && m.On(OrdersItem.Flee) && selected)
                {
                    tb.ChooseOrder(1, flee: true);
                }
                return;
            case 'G':
                if (m.On(OrdersItem.Group))
                {
                    tb.ToggleGroup();
                }
                return;
            case 'H':
                tb.Halt(tb.Battle.Leader);
                return;
            case 'M':
                if (m.On(AttackItem.Missile))
                {
                    tb.ChooseOrder(0x80);
                }
                return;
            case 'O':
                tb.OpenKey(tb.Selected);
                return;
            case 'P':
                if (tb.PickKey(tb.Selected))
                {
                    tb.ChooseOrder(0x22);
                }
                return;
            case 'Q':
                if (m.On(OrdersItem.SingleFile))
                {
                    tb.ToggleSingleFile();
                }
                return;
            case 'S':
                Surrender();
                return;
            case 'T':
                if (m.On(AttackItem.Throw))
                {
                    // orig: case 0x1454 (R016+0x068A..0x06A1): DS:9C26 = 1, DS:9C2F = 2, DS:9C3C = DS:9C41 = -1, DS:A394 = 3 (the Attack menu
                    // unfolded to Throw's list) and nothing else: the bar comes up at the next pass's root 0000:0000 (MenuBarRun), after that pass's
                    // head, so the timer callbacks it holds the clock for are seen by that pass's combat tick, not this one's
                    _unfoldedMenu = 2;
                    ThrowKeyPass = Passes;
                }
                return;
            case 'U':
                tb.UseKey(tb.Selected);
                return;
            case 'V':
                if (m.On(AttackItem.Vulnerable) && selected)
                {
                    tb.ChooseOrder(0x0A);
                }
                return;
            case 'W':
                if (tb.Paused && m.On(OrdersItem.Walk) && selected)
                {
                    tb.ChooseOrder(1);
                }
                return;
            case 'X':
                tb.ExitKey();
                return;
        }
    }

    /// <summary>An arrow: the active member steps (Shift, BIOS 0040:0017 bit 0 or 1: the view scrolls by a cell, two sideways).</summary>
    private void Arrow(int direction)
    {
        if (_ui.ShiftHeld)
        {
            ScrollView(direction);
            return;
        }
        _battle!.StepKey(direction);
    }

    // orig: R016+0x02A0.. (the arrows' scrolling parts): DS:C866 / DS:C886 moved, then R016+0x0B92 (1EF8:0B92) matched: the view centre kept
    // within 1..0x27, then floor-view-paint
    private void ScrollView(int direction)
    {
        int[] dx = [-1, 0, 1, 2, 1, 0, -1, -2], dy = [-1, -1, 0, 1, 1, 1, 0, -1];
        _view.CentreColumn = Math.Clamp(_view.CentreColumn + dx[direction], 1, 0x27);
        _view.CentreRow = Math.Clamp(_view.CentreRow + dy[direction], 1, 0x27);
        ViewScrolled();
    }

    /// <summary>The front-most combatant (any, and enemy) whose hot spot holds the point, −1 none.</summary>
    // orig: the TOPMOST macro and the enemy loops of R014+0xC044 / R014+0xC33C draft, checked
    // against asm (R014+0xC342..0xC3A5, the TOPMOST loop of the click): the hot spots (DS:D8F1 x0, DS:D8D7 x1, DS:D8BD y0, DS:D8A3 y1, inclusive) are scanned from the last
    // drawn to the first; "any" is the one of greatest map y (`by < y`, by starting at 0, so the front-most of equal y), "enemy" the first
    // hit of a combatant ≥ 5 in that order (the front-most drawn), whatever its y.
    private (int Any, int Enemy) HotSpotsAt(int x, int y)
    {
        int best = -1, bestY = 0, enemy = -1;
        for (var k = _hotSpots.Count - 1; k >= 0; k--)
        {
            var h = _hotSpots[k];
            if (x < h.Left || x > h.Right || y < h.Top || y > h.Bottom)
            {
                continue;
            }
            if (bestY < h.MapY)
            {
                best = h.Who;
                bestY = h.MapY;
            }
            if (enemy < 0 && h.Who >= Combatant.PartySlots)
            {
                enemy = h.Who;
            }
        }
        return (best, enemy);
    }

    /// <summary>The map point under the pointer (DS:D742/DS:D744, DS:E1B4/DS:E1B6): the inverse of the projection, so the ground cursor is drawn under the pointer.</summary>
    // orig: fight-mouse R014+0xB576 (19CD:0006) draft, see BattlefieldView.ToMap (its − 16 is already in the formula: a second
    // − 16 put the spot, and the ghost drawn at it, 16 pixels left of the click)
    private MapPoint GroundAt(int x, int y)
    {
        var (mx, my) = _view.ToMap(x, y);
        return new MapPoint(mx, my);
    }

    /// <summary>The front-most party member whose hot spot holds the point (the hot spots scanned from the last drawn), −1 none.</summary>
    // orig: the member loops of the mouse handler (R014+0xB632.., R014+0xB87E..): `for (k = mouse-zone-total - 1; k >= 0; k--) if (mouse-zone-fighter[k] < 5 && HIT(k, x, y))`
    private int MemberHotSpotAt(int x, int y)
    {
        for (var k = _hotSpots.Count - 1; k >= 0; k--)
        {
            var h = _hotSpots[k];
            if (h.Who < Combatant.PartySlots && x >= h.Left && x <= h.Right && y >= h.Top && y <= h.Bottom)
            {
                return h.Who;
            }
        }
        return -1;
    }

    /// <summary>
    /// The mouse handler, once a pass: while the left button is held over the view (Ctrl up) the cursor follows (outside order
    /// mode the member under the pointer is selected, in order mode the order cursor, in a formation the group cursor); then a
    /// release taken from the mouse driver's count is a click at the place of the release (Ctrl: the member there is activated).
    /// A release over the party panel is left for <see cref="PanelRelease"/>.
    /// </summary>
    // orig: R014+0xB576 (19CD:0006) draft, checked against the asm: the hover R014+0xB632.., the release count R014+0xB814 (INT 33h
    // function 6 into DS:C4FE, its count into DS:C62A when none is pending), the keyboard click DS:D73B (the pointer where it is now),
    // `ex -= 60; if (ex < 0) return;` before DS:C62A = 0, the Ctrl-click R014+0xB87E..; tests: BattleUiTests
    // C: the menu bar shown (DS:9409, F10's keyboard mode) returns at once: the releases stay counted.
    private void CombatMouse()
    {
        var tb = _battle!;
        if (_menuBar!.Shown)
        {
            return;
        }
        tb.GroupCursorTimeout();
        var mouse = _ui.Mouse;
        var mx = mouse.X - BattlefieldView.Left;
        if ((tb.CursorKeys.Hover || mouse.Left) && mx >= 0 && mx < 0x104 && !_ui.CtrlHeld)
        {
            if (tb.MoveMode == 0)
            {
                if (!tb.InOrderMode)
                {
                    tb.PreviewMember(MemberHotSpotAt(mouse.X, mouse.Y));
                }
                else
                {
                    var (any, enemy) = HotSpotsAt(mouse.X, mouse.Y);
                    tb.Hover(any, enemy, GroundAt(mouse.X, mouse.Y));
                }
            }
            else
            {
                tb.GroupCursor(GroundAt(mouse.X, mouse.Y), tb.CursorKeysOn);
            }
        }
        TakeReleases();
        if (_releases <= 0 && !tb.CursorKeys.Click)
        {
            return;
        }
        int ex, ey;
        if (tb.CursorKeys.Click)
        {
            if (tb.MoveMode != 0)
            {
                tb.KeepGroupCursor();
            }
            tb.CursorKeys.Click = false;
            (ex, ey) = (_ui.Mouse.X, _ui.Mouse.Y);
        }
        else
        {
            (ex, ey) = (_releaseX, _releaseY);
        }
        if (ex < BattlefieldView.Left)
        {
            return;
        }
        _releases = 0;
        if (_ui.CtrlHeld)
        {
            if (MemberHotSpotAt(ex, ey) is var member and >= 0)
            {
                tb.ActivateMember(member);
            }
            return;
        }
        Click(ex, ey);
    }

    /// <summary>DS:C62A: when no release is pending, the mouse driver's count and the place of the last release are taken.</summary>
    // orig: `if (DS:C62A == 0) { DS:C4FE = root 047C:1320(6, 0); DS:C62A = DS:C4FE[1]; }`
    private void TakeReleases()
    {
        if (_releases == 0)
        {
            (_releases, _releaseX, _releaseY) = _ui.TakeLeftReleases();
        }
    }

    /// <summary>A click in the view: outside order mode a member enters order mode, in order mode the order's target or spot, in a formation the group moves.</summary>
    // orig: R014+0xB87E.. (the click of the mouse handler) draft, checked against the asm
    private void Click(int x, int y)
    {
        var tb = _battle!;
        if (tb.MoveMode == 0)
        {
            if (!tb.InOrderMode)
            {
                // C: chosen-member = -1, then the last-drawn member hot spot under the release enters order mode
                var member = MemberHotSpotAt(x, y);
                tb.PreviewMember(member);
                if (member >= 0)
                {
                    tb.EnterOrderMode(member);
                }
                return;
            }
            var (any, enemy) = HotSpotsAt(x, y);
            tb.OrderClick(any, enemy, GroundAt(x, y));
            return;
        }
        tb.GroupMove(GroundAt(x, y));
    }

    /// <summary>
    /// A release over the party panel (x &lt; 60): the sheet of the member of the release's row when that member stands able on
    /// the field (every able member then readies his weapon again), else "You can't view …"; with Ctrl the member is activated.
    /// </summary>
    // orig: R014+0x3198..0x3238 (R014+0x3166) draft, checked against the asm: `if (DS:C62A > 0 && DS:C4FE[2] < 0x3c) { if (!(KBFLAGS & 4))
    // { i = y / 40; if (status == 1 && fighter-floor-level[i] != -99) { R022+0x0000(y); for (...) if (status == 1) R019+0x0B9E(i); } else
    // R022+0x04E4(i); } else R016+0x09BC(y / 40); DS:C62A = 0; }`; tests: BattleUiTests
    // C: an empty, dead or absent member's row also gives the message (with its record's name).
    private void PanelRelease()
    {
        var tb = _battle!;
        TakeReleases();
        if (_releases <= 0 || _releaseX >= BattlefieldView.Left)
        {
            return;
        }
        var row = _releaseY / PartyPanel.RowHeight;
        if (!_ui.CtrlHeld)
        {
            var c = tb.Battle[row];
            if (c.Status == CharacterStatus.Able && c.Floor != BattleEnd.LeftThroughExit)
            {
                CharacterSheet(row);
                tb.ReReadyAbleMembers();
            }
            else
            {
                CannotView(row);
            }
        }
        else
        {
            tb.ActivateMember(row);
        }
        _releases = 0;
    }

    /// <summary>Shift with the pointer at an edge of the view: the view scrolls toward it (a cell, two at the corners' sides).</summary>
    // orig: R014+0x323E.. (R014+0x3166) draft: the zones of the edge cursors (KBFLAGS & 3), DS:C866 / DS:C886 changed, then R016+0x0B92
    // (R016+0x0B92: the centre kept within 1..0x27, the view redrawn); tests: BattleUiTests
    // DIVERGENCE: the original scrolls once per pass of its loop (R014+0x3166, no time gate before R016+0x0B92), which runs as fast as the
    //   machine draws; here once a frame (a pass of the C# battle loop), so the speed of the edge scroll is a host's, not the EXE's.
    private void EdgeScroll()
    {
        if (!_ui.ShiftHeld)
        {
            return;
        }
        var (x, y) = (_ui.Mouse.X, _ui.Mouse.Y);
        int dc, dr;
        if (x > 0x7D && x < 0xFF && y < 8)
        {
            (dc, dr) = (-1, -1);
        }
        else if (x > 0x7D && x < 0xFF && y > 0xBF)
        {
            (dc, dr) = (1, 1);
        }
        else if (x >= 0x3C && x < 0x44 && y > 0x32 && y < 0x96)
        {
            (dc, dr) = (-1, 0);
        }
        else if (x >= 0x137 && y > 0x32 && y < 0x96)
        {
            (dc, dr) = (1, 0);
        }
        else if ((x >= 0x3C && x <= 0x7D && y < 8) || (x >= 0x3C && x < 0x44 && y <= 0x32))
        {
            (dc, dr) = (-2, -1);
        }
        else if ((x >= 0xFF && y < 8) || (x >= 0x137 && y <= 0x32))
        {
            (dc, dr) = (0, -1);
        }
        else if ((x >= 0x3C && x <= 0x7D && y > 0xBF) || (x >= 0x3C && x < 0x44 && y >= 0x96))
        {
            (dc, dr) = (0, 1);
        }
        else if ((x >= 0xFF && y > 0xBF) || (x >= 0x137 && y >= 0x96))
        {
            (dc, dr) = (2, 1);
        }
        else
        {
            return;
        }
        ScrollView(dc, dr);
    }

    /// <summary>The view's centre moves (kept within 1..0x27) and the view no longer follows the active member.</summary>
    // orig: R016+0x0B92 (1EF8:0B92) matched: the view centre DS:C866/DS:C886 kept within 1..0x27, then R017+0x0000
    private void ScrollView(int dc, int dr)
    {
        _view.CentreColumn = Math.Clamp(_view.CentreColumn + dc, 1, 0x27);
        _view.CentreRow = Math.Clamp(_view.CentreRow + dr, 1, 0x27);
        ViewScrolled();
    }

    /// <summary>R016+0x0B92's end: floor-view-paint (DS:E160) and DS:11B1 = 0, the view no longer follows the active member.</summary>
    private void ViewScrolled()
    {
        _battle!.ViewOrigin = null;
        _battle.FollowsActive = false;
        _viewRedrawn = true;
        FloorViewPanel();
    }

    /// <summary>The Orders, Attack and Party titles and Change Marching Order (DS:9893) set to 2; returns what puts them back.</summary>
    private Action GreyMenusForAScreen()
    {
        var m = _battle!.Menus;
        var (orders, attack, party, marching) = (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder);
        m.OrdersMenu = m.AttackMenu = m.PartyMenu = m.MarchingOrder = 2;
        return () => (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder) = (orders, attack, party, marching);
    }

    /// <summary>"You can't view … at this time.": a message box for 150 ticks, the battle waiting.</summary>
    // orig: R022+0x04E4 matched: sprintf(buf, "%Fs%s%Fs", 286B:0212, name, 286B:0222); root 0000:24BC(buf, 0x96) (root 0000:24BC, a wait
    // on the timer that nothing shortens)
    // C: fight-character-sheet comes here (its test of the floor byte 0x9D) before it holds the clock (inc DS:07D3): the clock runs in the wait
    private void CannotView(int member) => ShowMessage(_owner.CannotView(_battle!.Battle[member].Member?.Name ?? ""), 0x96);

    /// <summary>F1..F5 or a click on a portrait: the member's character sheet over the battle (only for an able member still on the field).</summary>
    // orig: fight-character-sheet R022+0x0000 (1EF8:0000) draft
    // C: CannotView is R022+0x04E4's message (the "You can't view … at this time." message)
    private void CharacterSheet(int member)
    {
        var tb = _battle!;
        var c = tb.Battle[member];
        if (c.Status == CharacterStatus.Able && c.Floor == BattleEnd.LeftThroughExit || c.Status == CharacterStatus.Unconscious)
        {
            CannotView(member);
            return;
        }
        if (c.Status != CharacterStatus.Able || _owner.CharacterSheet is not { } sheet)
        {
            return;
        }
        var saved = _ui.Canvas.SaveScreen();
        // orig: R022+0x0066: inc DS:07D3 (the combat clock held) before the sheet, R022+0x03C6: dec DS:07D3 at its end
        var opened = _ui.Clock.Milliseconds;
        // orig: R022+0x0167: DS:07D8 = 0 before the sheet (its own palette then); R022+0x0240: DS:07D8 = 1 after the battle's palette is back
        _ui.RunCycles(false);
        _ui.PaletteSet();
        tb.MembersFromCombat();
        _ui.PointerVisible = true;
        // orig: R022+0x007A..0x009D: the Orders, Attack and Party titles (DS:9429, DS:9433, DS:943D) and Change Marching Order (DS:9893,
        //   Alt-O) are 2 (greyed, refused) while the sheet is up, put back at R022+0x02A2..0x02B7; tests: HotKeyFlagsTests
        var menus = GreyMenusForAScreen();
        try
        {
            sheet(member);
        }
        finally
        {
            menus();
        }
        // C: a click on the member's own row closes the sheet on its press (R031+0x0635 to R031+0x08E1, no wait for the button); R022 then
        // reloads the battle's pictures and level (R022+0x01EA..0x03BE) before it reads the release count, so the closing click's release
        // falls inside the reload and is taken and forgotten at R022+0x03BE (lockstep session orders-sheetclick: the original is back in
        // the battle, R014+0x3198 does not open the sheet again). The reload takes no time here: the button is waited out in its place.
        // DIVERGENCE: a button held longer than the original's reload would give the battle a release (the sheet again); the reload's time
        //   is the machine's (disk reads of the battle's pictures and level, R022+0x01EA..0x03BE), not a value of the EXE: here the reload
        //   always outlasts the press (an ordinary click on a PC of the time), so a long press does not reopen the sheet.
        _ui.WaitForButtonRelease();
        // orig: R022+0x01EA after the sheet: the releases made on it taken from the driver and forgotten (DS:C62A = 0)
        _ui.TakeLeftReleases();
        _releases = 0;
        _ui.PointerVisible = false;
        tb.MembersBackToCombat();
        _ui.Canvas.Restore(saved);
        SetPalette(tb.Field.Terrain.BackgroundPalette);
        _paletteApplied.Clear();
        _ui.RunCycles(true);
        // orig: R022+0x03BE at the end: the release count read once more (DS:C62A untouched)
        _ui.TakeLeftReleases();
        HeldClockTime(_ui.Clock.Milliseconds - opened);
        if (OneTickPerPass && SheetCallbacks is { Count: > 0 } measured)
        {
            var (during, after) = measured.Dequeue();
            CountCallbacks(during);
            if (tb.PauseByte != 0)
            {
                // a running battle's callbacks after the sheet raise the clock: the clock jumps list them
                _afterSheetCallbacks += after;
            }
        }
    }

    /// <summary>S: "Do you really want to surrender?" in real combat; yes ends the battle (or, with members gone, only for those left).</summary>
    // orig: yield-action R022+0x03E4 (1EF8:03E4) matched
    private void Surrender()
    {
        var tb = _battle!;
        if (!tb.Battle.RealCombat)
        {
            return;
        }
        if (new MessageBox(_ui).Ask(_owner.SurrenderQuestion))
        {
            tb.Surrender();
        }
    }

    /// <summary>E: the enemy information: the portraits, names, armour and weapons (with skill words below expert) of up to two kinds.</summary>
    // orig: enemy-details R021+0x02CE (1EF8:02CE) and R021+0x03FE (1EF8:03FE) matched
    private void EnemyInfo()
    {
        var tb = _battle!;
        var kinds = tb.EnemyInfoKinds();
        if (kinds.Count == 0)
        {
            return;
        }
        var canvas = _ui.Canvas;
        var panel = canvas.Save(0, 0, PartyPanel.Width, Canvas.Height);
        var common = _assets.Common;
        for (var y = 0; y < 0xC6; y += 0x0B)
        {
            // DS:A628: the panel's background piece of COMMONSP.IMG.
            SpriteDrawing.Draw(canvas, common.ColumnBackground, 3, y, new Clip(0, 0, PartyPanel.Width, Canvas.Height));
        }
        for (var k = 0; k < kinds.Count; k++)
        {
            DrawEnemyInfo(canvas, kinds[k], kinds.Count == 1 ? 50 : k * 100);
        }
        _ui.Present();
        // orig: R021+0x03AF matched C: `while (!root 047C:073D() && !(mouse-button-state & 1));` (no menu bar), root 0000:1384, then R021+0x03BB
        // `while (mouse-button-state & 1);`; tests: BattleUiTests
        while (!_ui.KeyWaiting && !_ui.Mouse.Left)
        {
            _ui.Present();
            _ui.Idle();
        }
        _ui.FlushKeys();
        _ui.WaitForButtonRelease();
        canvas.Restore(panel);
        // orig: R021+0x03E3..0x03F6: party-panel-frame-set(0x100), party-panel-row-set(0x100) as the screen closes
        RowChanged(-1);
    }

    private void DrawEnemyInfo(Canvas canvas, int m, int x)
    {
        var tb = _battle!;
        var c = tb.Battle[m];
        var small = _small!;
        var code = tb.SlotCodes[c.AnimationSlot] + tb.SlotWeapons[c.AnimationSlot] + "st.pic";
        if (code[0] > 0x60)
        {
            code = char.ToUpperInvariant(code[0]) + code[1..];
        }
        if (_assets.EnemyPortrait(code) is { } pic)
        {
            canvas.Draw(pic.Image, 0x0E, x, transparent: true);
        }
        var y = x + 0x20;
        var name = tb.Field.Forces.Kinds[c.Floor < 0 ? 0 : c.Floor].ElementAtOrDefault(tb.EnemyKindIndex(c.AnimationSlot))?.Name ?? "";
        while (name.Length > 0 && small.Measure(name) > 0x36)
        {
            name = name[..^1];
        }
        small.Draw(canvas, (0x36 - small.Measure(name)) / 2 + 3, y, name, 0x0E);
        y += 8;
        var expert = _state.Difficulty == Difficulty.Expert;
        string Word(int v) => _assets.Executable.String(QualityWordAt(v));
        if (code[0] == 'E')
        {
            var plus = (c.Protection & 0x7F) > 0 ? "\x8e+\x8f" : "";
            small.Draw(canvas, 4, y, TypeName(0x01F7, c.ArmourType[0]) + plus, 0x0F);
            y += 8;
            small.Draw(canvas, 4, y, TypeName(0x01F7, c.ArmourType[1]) + plus, 0x0F);
            y += 8;
            if (c.ShieldType != -1)
            {
                small.Draw(canvas, 4, y, TypeName(0x01CF, c.ShieldType), 0x0F);
                y += 8;
            }
            var t = c.DefaultWeapon == -1 ? 9 : c.DefaultWeapon;
            var bonus = c.WeaponBonus != 0 ? "\x8e+\x8f" : "";
            if (t <= 0x21)
            {
                small.Draw(canvas, 4, y, TypeName(0x0277, t) + bonus, 0x0F);
                y += 8;
                if (!expert)
                {
                    small.Draw(canvas, 4, --y, " - " + Word(Math.Clamp((int)c.Skills[0], 0, 100) / 20), 0x0F);
                    y += 8;
                }
            }
            t = c.ReadiedWeapon != -1 ? c.ReadiedWeapon : c.EnemyClass == 2 ? 0x22 : -1;
            if (t != -1)
            {
                small.Draw(canvas, 4, y, TypeName(0x0277, t) + bonus, 0x0F);
                y += 8;
                if (!expert)
                {
                    small.Draw(canvas, 4, y - 1, " - " + Word(Math.Clamp((int)c.Skills[5], 0, 100) / 20), 0x0F);
                }
            }
            return;
        }
        small.Draw(canvas, 4, y, "Natrl Armr", 0x0F);
        y += 8;
        if (!expert)
        {
            small.Draw(canvas, 4, --y, " - " + Word(Math.Clamp(c.WeaponClass & 0x0F, 0, 6)), 0x0F);
            y += 8;
        }
        small.Draw(canvas, 4, y, "Natrl Weapn", 0x0F);
        y += 8;
        if (!expert)
        {
            small.Draw(canvas, 4, y - 1, " - " + Word(Math.Clamp((int)c.Skills[0], 0, 100) / 20), 0x0F);
        }
    }

    /// <summary>grade-words (DS:4FCE): "very poor" … "superb".</summary>
    private int QualityWordAt(int k)
    {
        var table = _assets.Executable.DataSegment(0x4FCE, 14);
        k = Math.Clamp(k, 0, 6);
        return table[2 * k] | table[2 * k + 1] << 8;
    }

    /// <summary>The short name of a weapon, armour or shield type: far pointer table at 286B:base + 4 × type (one table, three bases).</summary>
    // orig: R136+0x7ED7 (weapons), R136+0x7E57 (armour), R136+0x7E2F (shields): the same far pointer table seen from three bases
    private string TypeName(int tableBase, int type)
    {
        var exe = _assets.Executable;
        var at = tableBase + 4 * type;
        if (at < 0 || at > 0x400)
        {
            return "";
        }
        var p = exe.Far(0x286B, at, 4);
        int off = p[0] | p[1] << 8, seg = p[2] | p[3] << 8;
        if (seg != 0x286B)
        {
            return "";
        }
        var bytes = exe.Far(seg, off, 40);
        var end = bytes.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Change Marching Order (Party menu, Alt-O): the members are picked in order with F1..F5 or clicks.</summary>
    // orig: R022+0x051C (1EF8:051C) draft through MarchingOrderChoice (the one core of the battle and the shell, R022+0x07AA draft (bytes
    // match)); the battle's loop R022+0x0655..0x0682, checked against the asm: the keys are read raw (`if (root 047C:073D()) switch
    // (root 047C:0744())`: exactly 0x3B00..0x3F00 pick members 0..4, whatever their state); else a release from the driver's count
    // (DS:C62A / DS:C4FE): over the panel the row's member if able and still marked, in the view (DS:907A == 1, the battle) every marked
    // member hot spot under it, each taken in turn; tests: BattleUiTests, MarchingOrderTests
    // C: a key picks the member even when absent or already picked (R022+0x07AA does not check); several hot spots under one release are
    // all taken, so more than one member can be picked for one ordinal; a sixth entry is the length's own byte (MarchingOrderChoice).
    // DIVERGENCE: the prompt ("Select first" ...) is the battle screen's message line, not a text box over the card window (MarchingOrderScreen
    // draws the box outside a battle).
    private void ChooseMarchingOrder()
    {
        var tb = _battle!;
        var lengthAt = Darklands.Core.Rules.MarchingOrderChoice.LengthAt;
        var order = new byte[lengthAt + 1];
        tb.StoreMarchingOrder(order);
        var choice = new Darklands.Core.Rules.MarchingOrderChoice(order, i => tb.Battle[i].IsAble);
        for (var k = 0; k < choice.Prompts; k++)
        {
            _message = _owner.SelectOrdinal(k);
            _messageUntil = long.MaxValue;
            Draw();
            var more = true;
            do
            {
                _ui.Present();
                if (_ui.TryReadKey(out var key))
                {
                    if (key is Keys.F1 or Keys.F2 or Keys.F3 or Keys.F4 or Keys.F5)
                    {
                        choice.Take((key - Keys.F1) >> 8);
                        more = false;
                    }
                    continue;
                }
                _ui.Idle();
                TakeReleases();
                if (_releases > 0)
                {
                    var (x, y) = (_releaseX, _releaseY);
                    _releases = 0;
                    if (x < BattlefieldView.Left)
                    {
                        var i = y / PartyPanel.RowHeight;
                        if (choice.CanClick(i, tb.Battle[i].IsAble))
                        {
                            choice.Take(i);
                            more = false;
                        }
                    }
                    else
                    {
                        for (var j = _hotSpots.Count - 1; j >= 0; j--)
                        {
                            var h = _hotSpots[j];
                            if (h.Who < Combatant.PartySlots && h.Left <= x && h.Right >= x && h.Top <= y && h.Bottom >= y && choice.Selected[h.Who])
                            {
                                choice.Take(h.Who);
                                more = false;
                            }
                        }
                    }
                }
            }
            while (more);
        }
        choice.Finish();
        _message = null;
        tb.SetMarchingOrder(Darklands.Core.Rules.MarchingOrderChoice.Entries(order));
    }

    // Drawing

    /// <summary>DS:E160: the view was drawn anew since the last fight-draw (which then forgets the logged sprites).</summary>
    private bool _viewRedrawn;

    /// <summary>A placement's view centre (effect-begin's DS:C866 / DS:C886) becomes the view's.</summary>
    private void ApplyViewOrigin()
    {
        if (_battle!.ViewOrigin is { } origin)
        {
            _view.CentreColumn = origin.Column;
            _view.CentreRow = origin.Row;
            _battle.ViewOrigin = null;
        }
    }

    /// <summary>fighter-screen-position's places: each combatant on the level shown (any status but empty) gets its sprite's top left.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft: scrx = foot x − DS:D505[set][facing], scry = DS:C8CA[set] − DS:D5D3[set] + y + 0x69
    private void RecordScreenPlaces()
    {
        var tb = _battle!;
        for (var i = 0; i < Combatant.Count; i++)
        {
            if (tb.Battle[i].Status != CharacterStatus.Empty && tb.Battle[i].Floor == tb.Battle.FloorShown && SpriteBox(i) is { } box)
            {
                tb.Battle[i].ScreenPlace = (box.Left - BattlefieldView.Left, box.Top);
            }
        }
    }

    /// <summary>Draws the whole screen.</summary>
    // orig: fight-draw's sprite lists: list C (depth-sorted sprites), which R014+0x7BBE fills
    // unmatched (a sprite queued with its depth and screen tile) and R014+0x8724 matched (at most 157), list A
    // R014+0x87E4 matched, list B R014+0x8862 matched; the dirty screen tiles re-queued by
    // R014+0x88E6 unmatched and R014+0x827A unmatched (walls, furniture, floor objects); list C
    // sorted by R014+0x97A6 unmatched (insertion sort by depth); the lists blitted by R014+0x74A2
    // unmatched through R014+0x9DAA, R014+0x9DDE, R014+0x9CE6 and the view
    // clip R014+0x8680 (all matched). Here the view, the piles, the areas, the combatants (sorted by depth: the
    // fallen first, then y and x), the missiles and the clouds are drawn in that order every frame.
    // DIVERGENCE: the whole view is redrawn each frame (no dirty tiles), the wall pieces are drawn by the view before the sprites rather
    // than sorted in among them, and the 157-sprite limit is not kept (see EffectField.Drawn for its effect on the random numbers).
    // C: R014+0x827A's roll-n(2) for each animated piece of the last full redraw (R017+0x0000's lists DS:E13D / DS:E178 / DS:E196) are
    // drawn by EffectField.Drawn at each fight-draw, the first one at the battle's start (R014+0x0307).
    public void Draw()
    {
        var tb = _battle!;
        var canvas = _ui.Canvas;
        ApplyEnemyPalettes();
        ApplyViewOrigin();
        // orig: R014+0x105B..0x1062: once DS:CA29 is set (a pit or an alarm stopped the battle, also in the tick that set it) fight-tick
        // returns before fight-draw (R014+0x10AA), so fighter-screen-position (R014+0x99F6) no longer moves +0x00 / +0x02, which the saved game
        // keeps (lockstep session dungeon-mine-doormsg: the alarm of a riddle door)
        if (!tb.Stopped)
        {
            RecordScreenPlaces();
        }
        _view.Draw(canvas, tb.Level, _tiles!);
        var clip = BattlefieldView.Area;
        foreach (var pile in tb.LootPiles)
        {
            if (pile.Level == tb.Battle.FloorShown && pile.Visible)
            {
                // orig: R014+0x799E (14F7:118E) draft: x = −dx − ((y − sy0) − x) − sx0 + 0x88, y = (y − sy0) − dy + 0x69 (dx 10, dy 4).
                var (px, py) = _view.ToScreen(pile.Position.X, pile.Position.Y);
                SpriteDrawing.Draw(canvas, _assets.Images[BattleImages.LootPile], px - 10, py - 4, clip);
            }
        }
        DrawAreas(canvas);
        _hotSpots.Clear();
        var order = Enumerable.Range(0, Combatant.Count)
            .Where(i => tb.Battle[i].Status is CharacterStatus.Able or CharacterStatus.Unconscious or CharacterStatus.Dead && tb.Battle[i].Floor == tb.Battle.FloorShown)
            .OrderBy(i => tb.Battle[i].IsAble ? 1 : 0)
            .ThenBy(i => tb.Battle[i].Position.Y)
            .ThenBy(i => tb.Battle[i].Position.X)
            .ToList();
        foreach (var i in order)
        {
            DrawCombatant(canvas, i);
        }
        // the draw list goes on after the combatants' entries (EffectField.Drawn) with the ground cursor and the missiles, which are
        // logged for the next frame together with the damage numbers queued
        _listCount = tb.Effects.DrawListCount;
        _logged = Enumerable.Range(0, Combatant.Count).Count(tb.Effects.NumberQueued);
        DrawGroundCursorEntry();
        DrawMissiles(canvas);
        _loggedSprites = _logged;
        DrawClouds(canvas);
        DrawGroundCursor(canvas);
        OpenPendingOrderMode();
        DrawPanel(canvas);
        DrawMessage(canvas);
        // orig: notice-display root 0000:24BC: pointer-hide (root 047C:12C3) before the box and pointer-show (root 047C:1293) after the wait: no pointer while it waits
        if (_messageWaiting)
        {
            _underPointer = null;
        }
        else
        {
            DrawPointer(canvas);
        }
    }

    /// <summary>In the frame drawn after a combat tick: the member waiting for order mode (DS:D6F1 / DS:D6ED) enters it once it has a hot spot.</summary>
    // orig: fight-tick R014+0x0F58 (0E76:0F58) draft (its end, 0x10AF..0x1125, checked against the asm)
    // C: after the tick's fight-draw, if DS:D6F1 == 1 the hot spots (DS:D848[0..DS:E0A1 - 1]) are searched for the member DS:D6ED; with
    // C: one: chosen-member = it, the member cursor's place (DS:E09F / DS:E09D, drawn here from the member), command-mode-begin and
    // C: DS:D6F1 = 0; without one D6F1 stays for the next tick (the member's status is not tested)
    private void OpenPendingOrderMode()
    {
        var tb = _battle!;
        if (tb.TakeTickToDraw() && tb.PendingOrderMember >= 0 && HotSpot(tb.PendingOrderMember) is not null)
        {
            var member = tb.PendingOrderMember;
            tb.PendingOrderMember = -1;
            tb.EnterOrderMode(member);
        }
    }

    /// <summary>The draw list's count and the sprites logged in the frame being drawn; the last frame's logged sprites (DS:E176).</summary>
    private int _listCount, _logged, _loggedSprites;

    // C: fight-draw reads DS:E160 once at its start: after a floor-view-paint DS:E176 = 0 (and DS:E160 = 0)
    public int LoggedSprites
    {
        get
        {
            var logged = _viewRedrawn ? 0 : _loggedSprites;
            _viewRedrawn = false;
            return logged;
        }
    }

    /// <summary>A sprite after the combatants takes a place in the draw list while there is room; true when it has one.</summary>
    // orig: R014+0x7BD0 (13AE's `cmp word ptr [E162h], 9Dh / jl`), the ground cursor (R014+0x6B1C) and the missiles (R014+0x6D33) logged
    private bool TakeListPlace()
    {
        if (_listCount >= EffectField.DrawListLimit)
        {
            return false;
        }
        _listCount++;
        _logged++;
        return true;
    }

    /// <summary>The ground cursor's place in the draw list (it comes before the missiles).</summary>
    private void DrawGroundCursorEntry()
    {
        _groundCursorQueued = _battle!.CursorTarget is -2 or -3 && TakeListPlace();
    }

    private bool _groundCursorQueued;

    /// <summary>One combatant: its frame at the foot point; hot spot, member cursor and damage number.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft; R014+0x719E (14F7:098E) draft (spec E §1.9, §3.3)
    private void DrawCombatant(Canvas canvas, int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        if (SpriteBox(i) is not { } box)
        {
            return;
        }
        var facing = c.SpriteFacing & 7;
        var animation = box.Animation;
        var (fx, fy, left, top) = (box.FootX, box.FootY, box.Left, box.Top);
        var frame = Math.Clamp(tb.ShownFrame(i), 0, Math.Max(0, animation.FrameCount - 1));
        if (c.IsAble && c.Pose == Pose.Walking && !c.Moving && c.Frame == 0)
        {
            frame = 0;
        }
        var clip = BattlefieldView.Area;
        SpriteDrawing.Draw(canvas, animation.Frame(frame, facing), left, top, clip);
        if (tb.Effects.FlashSpriteOf(i) is var flash and >= 0 && flash < BattleImages.Count)
        {
            // orig: R014+0x719E: the flash at (foot x - 5, top + height / 3 - 5).
            SpriteDrawing.Draw(canvas, _assets.Images[flash], fx - 5, top + animation.Height / 3 - 5, clip);
        }
        if (!c.IsAble)
        {
            return;
        }
        var h = animation.Height;
        var bottom = top + h;
        var mid = top + h / 2;
        var (l, t, r, b) = animation.SizeClass switch
        {
            0 => (fx - 4, bottom - 25, fx + 3, bottom),
            1 => (fx - 10, mid - 4, fx + 10, mid + 4),
            2 => (fx - 5, mid - 15, fx + 5, mid + 15),
            3 => (fx - 10, mid - 15, fx + 10, mid + 15),
            _ => (fx - 25, mid - 25, fx + 25, mid + 25),
        };
        // orig: R014+0x719E skips a combatant whose box (4 pixels round) is out of the view (R014+0x9C8C), and R014+0x7BBE
        // queues no sprite record, so no hot spot, when the box clipped to the view (R014+0x8680) is empty.
        // DIVERGENCE: the box is not grown by the last frame's movement (DS:85E8.., the dirty-rectangle deltas).
        // C: past the draw list's limit the body has no entry: no hot spot, no member or target cursor (DS:853E's bit) either
        var queued = tb.Effects.BodyQueued(i);
        if (queued && CombatantInView(i) && left - BattlefieldView.Left < 0x104 && left - BattlefieldView.Left + box.Width > 0 && top < Canvas.Height && top + animation.Height > 0)
        {
            _hotSpots.Add((i, l, t, r, b, c.Position.Y));
        }
        if (queued && (i == tb.Selected || (c.Selected && i < Combatant.PartySlots)))
        {
            SpriteDrawing.Draw(canvas, _assets.Images[BattleImages.MemberCursor], fx - 4, mid - 13, clip);
        }
        if (queued && tb.Selected >= 0 && tb.CursorTarget == i && tb.Cursor is OrderCursor.Target or OrderCursor.TargetBlocked)
        {
            // orig: R014+0xCCD4 (mode 0): the target cursor DS:C242 (or DS:C2BD blocked) at the sprite's foot.
            var image = tb.Cursor == OrderCursor.Target ? BattleImages.TargetCursor : BattleImages.TargetBlocked;
            SpriteDrawing.Draw(canvas, _assets.Images[image], fx - 5, bottom - animation.CursorY - 5, clip);
        }
        if (c.DamageNumber is > 0 and <= 42 && tb.Effects.NumberQueued(i))
        {
            var image = (i < Combatant.PartySlots ? BattleImages.PartyDamage : BattleImages.EnemyDamage) + c.DamageNumber;
            SpriteDrawing.Draw(canvas, _assets.Images[image], fx - 7, bottom - 0x27, clip);
        }
    }

    /// <summary>The ground cursor of order mode (a free or blocked spot, a throw's spot) and the group cursor.</summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft (the ground cursor: x = px - sx0 - (py - sy0) + 0x88 - w / 2, y = (py - sy0) - h + 0x69)
    private void DrawGroundCursor(Canvas canvas)
    {
        var tb = _battle!;
        if (tb.CursorTarget is not (-2 or -3) || !_groundCursorQueued)
        {
            return;
        }
        var (image, width, height) = tb.Cursor switch
        {
            OrderCursor.Ground => (BattleImages.GroundFree, 0x0B, 0x1A),
            OrderCursor.GroundBlocked => (BattleImages.GroundBlocked, 0x0B, 0x1A),
            OrderCursor.Throw => (199, 0x0B, 0x0F),
            OrderCursor.ThrowBlocked => (200, 0x0B, 0x0F),
            _ => (-1, 0, 0),
        };
        if (tb.MoveMode != 0 && tb.Selected == -1)
        {
            (image, width, height) = (tb.MoveMode == 1, tb.Cursor == OrderCursor.Ground) switch
            {
                (true, true) => (BattleImages.GroupFree, 0x16, 0x19),
                (true, false) => (BattleImages.GroupBlocked, 0x16, 0x19),
                (false, true) => (BattleImages.SingleFileFree, 0x0E, 0x1C),
                _ => (BattleImages.SingleFileBlocked, 0x0E, 0x1C),
            };
        }
        if (image < 0)
        {
            return;
        }
        var (x, y) = _view.CursorTopLeft(tb.CursorPoint.X, tb.CursorPoint.Y, width, height);
        SpriteDrawing.Draw(canvas, _assets.Images[image], x, y, BattlefieldView.Area);
    }

    /// <summary>Missiles in flight (and arrows stuck where they fell).</summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft (spec E §2.1: sy = y − sy0; sx = x − sx0 − sy − dx + 0x88; y = sy + 0x55);
    // the screen positions by R014+0x9C14 (14F7:3404) unmatched
    private void DrawMissiles(Canvas canvas)
    {
        var tb = _battle!;
        foreach (var m in tb.MissilesDrawn)
        {
            if (m.Floor != tb.Battle.FloorShown || (m.Arrived && m.Sprite != 0))
            {
                continue;
            }
            var id = m.Sprite & 0x7F;
            var dir = m.Facing & 7;
            int image, dx;
            switch (id)
            {
                case 1:
                    (image, dx) = (BattleImages.Bullet, 1);
                    break;
                case 7 or 8:
                    (image, dx) = (BattleImages.BigProjectile, 10);
                    break;
                case 9:
                    (image, dx) = (BattleImages.BigProjectile2, 10);
                    break;
                default:
                    var group = id switch { 0 => m.Arrived ? 1 : 0, 6 => 2, 5 => 3, 3 => 4, 4 => 5, _ => 2 };
                    (image, dx) = (BattleImages.Missiles + 6 * dir + group, id == 4 ? 7 : 3);
                    break;
            }
            if (!TakeListPlace())
            {
                continue;
            }
            var p = m.Arrived ? new MapPoint(m.Position.X + 2, m.Position.Y + 2) : m.Position;
            var sy = p.Y - _view.CameraY;
            var sx = p.X - _view.CameraX - sy - dx + 0x88 + BattlefieldView.Left;
            SpriteDrawing.Draw(canvas, _assets.Images[image], sx, sy + 0x55, BattlefieldView.Area);
        }
    }

    /// <summary>DS:1C4A: the 3-colour schemes of the cloud sprites by effect type; DS:1C5C: the 4-colour schemes of the markers.</summary>
    // orig: DS:1C4A, DS:1C5C (dumped from the 483.07 EXE)
    private static readonly byte[][] CloudSchemes =
        [[0xFB, 0xF5, 0xF6], [0xF3, 0xFB, 0xFE], [0xF7, 0xF8, 0xFA], [0xFC, 0xF6, 0xFE], [0xF4, 0xF5, 0xF3], [0xF6, 0xFE, 0xF4]];

    private static readonly byte[][] MarkerSchemes = [[0xFC, 0xFE, 0xFB, 0xF3], [0xF7, 0xFE, 0xF4, 0xFB], [0xFD, 0xFC, 0xF9, 0xF6]];

    /// <summary>The colours sprite buffers 1 (clouds) and 2 (markers) hold now: the schemes applied so far, composed (DS:D438, DS:D4B0).</summary>
    private readonly byte[] _cloudColours = Identity(), _markerColours = Identity();
    private int _cloudScheme, _markerScheme = 2;

    private static byte[] Identity() => [.. Enumerable.Range(0, 256).Select(v => (byte)v)];

    /// <summary>Recolours a sprite buffer from its current scheme to scheme s (the pixels themselves are translated, so the history counts).</summary>
    // orig: R014+0x10530 (1EC9:0000) / R014+0x105A0 (1EC9:0070) draft (inline asm xlat)
    // DIVERGENCE: a scheme index past the tables reads the bytes after them in the original; such schemes are ignored here.
    private static void Remap(byte[] colours, byte[][] schemes, ref int current, int s)
    {
        if (current == s || s < 0 || s >= schemes.Length || current < 0 || current >= schemes.Length)
        {
            return;
        }
        var table = Identity();
        for (var i = 0; i < schemes[s].Length; i++)
        {
            table[schemes[current][i]] = schemes[s][i];
        }
        for (var v = 0; v < 256; v++)
        {
            colours[v] = table[colours[v]];
        }
        current = s;
    }

    /// <summary>The slowing areas' puffs (list B, under the combatants).</summary>
    // orig: R014+0x917E (14F7:296E) draft
    private void DrawAreas(Canvas canvas)
    {
        var tb = _battle!;
        for (var k = 0; k < tb.Effects.AreaCount; k++)
        {
            if (tb.Effects.Areas[k].Level != tb.Battle.FloorShown)
            {
                continue;
            }
            var n = tb.Effects.Areas[k].Size & 7;
            for (var j = 0; j < n * n && j < 16; j++)
            {
                var (x, y) = tb.Effects.AreaPuff(k, j);
                var sprite = tb.Effects.AreaSprite(k, j);
                if (x != 0 && y != 0 && sprite is >= 196 and <= 197)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[sprite], BattlefieldView.Left + x, y, BattlefieldView.Area);
                }
            }
        }
    }

    /// <summary>The clouds (edges, then the puffs on top, in the colours of their type) and the flash and blast markers.</summary>
    // orig: R014+0x6FFC (14F7:07EC), R014+0x8CB4 (14F7:24A4), R014+0x9542 (14F7:2D32; the markers) and R014+0x76AE (14F7:0E9E) draft
    // DIVERGENCE: the edges are drawn over the combatants (the original sorts them in with the sprites by depth) and the puffs
    // are clipped to the view, not to the cloud's box.
    private void DrawClouds(Canvas canvas)
    {
        var tb = _battle!;
        var clip = BattlefieldView.Area;
        for (var k = 0; k < tb.Effects.CloudCount; k++)
        {
            var cloud = tb.Effects.Clouds[k];
            if (cloud.Level != tb.Battle.FloorShown)
            {
                continue;
            }
            var n = cloud.Size & 7;
            var (south, east) = tb.Effects.CloudEdgeSprites(k);
            Remap(_cloudColours, CloudSchemes, ref _cloudScheme, cloud.Kind);
            for (var c = 0; c < n && c < 4; c++)
            {
                var (sx, sy) = tb.Effects.CloudSouthEdge(k, c);
                if (sx != -999 && south != 0)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[south], BattlefieldView.Left + sx, sy, clip, _cloudColours);
                }
                var (ex, ey) = tb.Effects.CloudEastEdge(k, c);
                if (ex != -999 && east != 0)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[east], BattlefieldView.Left + ex, ey, clip, _cloudColours);
                }
            }
            var body = tb.Effects.CloudSprite(k);
            if (body < 0)
            {
                continue;
            }
            for (var j = 0; j < n * n && j < 16; j++)
            {
                var (x, y) = tb.Effects.CloudPuff(k, j);
                if (x != 0 && y != 0)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[body], BattlefieldView.Left + x, y, clip, _cloudColours);
                }
            }
        }
        for (var k = 0; k < tb.Effects.MarkerCount; k++)
        {
            var m = tb.Effects.Markers[k];
            // C: frame 4 reads DS:CEA4[4] (DS:CEAC, set to 0 by the loader): nothing is drawn.
            var frame = m.Frame & 7;
            if (m.Level != tb.Battle.FloorShown || frame > 3)
            {
                continue;
            }
            Remap(_markerColours, MarkerSchemes, ref _markerScheme, m.Kind);
            var (x, y) = tb.Effects.MarkerPoint(k);
            SpriteDrawing.Draw(canvas, _assets.Images[109 + frame], BattlefieldView.Left + x, y, clip, _markerColours);
        }
    }

    /// <summary>The ENEMYPAL.DAT colour schemes of the enemies on the field, and their colour cycles.</summary>
    // orig: enemy-placement-entry R025+0x0000 (1EF8:0000) draft (spec E §1.8)
    // orig: R025+0x0958..0x098D after the palette upload (R025+0x094B): for each of the three schemes with +0x31 != 0xFF,
    //   root 047C:0362(+0x34, +0x33, +0x31, +0x32) then 047C:03DA(+0x34): the scheme's range taken from the DAC and started forwards
    // C: ENEMYPAL.DAT's 14 cycling schemes all name slot 3: an enemy's (or a later placement's) last cycling scheme is the one that turns,
    //   the earlier ones keep the colours the take found. A cycle is taken once per combatant placed (not again when the sheet or the loot
    //   screen gave the palette back: the slot goes on from where it was).
    private void ApplyEnemyPalettes()
    {
        var tb = _battle!;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var c = tb.Battle[i];
            if (c.Status == CharacterStatus.Empty || !_paletteApplied.Add(i * 1000 + c.Appearance))
            {
                continue;
            }
            foreach (var p in c.Palettes)
            {
                if (p < 0 || p >= _assets.EnemyPalettes.Count)
                {
                    continue;
                }
                var first = _assets.EnemyPalettes.FirstColor(p);
                var colors = _assets.EnemyPalettes.Colors(p);
                for (var k = 0; k < 16; k++)
                {
                    _ui.Canvas.SetColor(first + k, new Content.Formats.Graphics.Rgb24(
                        BattlePalette.Expand(colors[3 * k]), BattlePalette.Expand(colors[3 * k + 1]), BattlePalette.Expand(colors[3 * k + 2])));
                }
            }
            if (!_cyclesTaken.Add(i * 1000 + c.Appearance))
            {
                continue;
            }
            foreach (var p in c.Palettes)
            {
                if (p < 0 || p >= _assets.EnemyPalettes.Count || _assets.EnemyPalettes.Cycle(p) is not { First: not 0xFF } cycle)
                {
                    continue;
                }
                _ui.TakeCycle(cycle.Slot, cycle.Reload, cycle.First, cycle.Last);
                _ui.Cycles.Start(cycle.Slot);
            }
        }
    }

    /// <summary>The party panel: per member its row with portrait, name, bars and the order letter.</summary>
    // orig: party-panel-frame-set root 0000:1588, party-panel-row-set root 0000:1716, member-gauge-bars root 0000:1A9C, member-status-glyph root 0000:18FA
    // draft (spec E §3.2)
    private void DrawPanel(Canvas canvas)
    {
        var tb = _battle!;
        var common = _assets.Common;
        canvas.Fill(0, 0, PartyPanel.Width, Canvas.Height, 0);
        var screen = new Clip(0, 0, PartyPanel.Width, Canvas.Height);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var y = i * PartyPanel.RowHeight;
            var c = tb.Battle[i];
            var lit = c.Selected;
            PartyPanel.DrawFrame(canvas, common, i, lit);
            SpriteDrawing.Draw(canvas, common.RowBackground, 3, y + 1, screen);
            if (c.Status == CharacterStatus.Empty || c.Member is null)
            {
                continue;
            }
            var y1 = y + 1;
            if (c.Status == CharacterStatus.Dead)
            {
                SpriteDrawing.Draw(canvas, common.DeadShield, 6, y1 + 8, screen);
            }
            else
            {
                DrawShortPortrait(canvas, i, tb.SlotCodes[i], 6, y1 + 8);
            }
            var gone = c.Floor == BattleEnd.LeftThroughExit || c.Status is CharacterStatus.Dead or CharacterStatus.Away || (tb.Selected >= 0 && tb.Selected != i);
            var color = gone ? Palette.Grey : tb.Battle.Leader == i ? (byte)0x0E : Palette.White;
            // orig: the panel's window DS:0156 (pointer DS:0168) has font slot 1 in a battle too (hooked at party-panel-row-set root 0000:1716 / member-gauge-bars root 0000:1A9C over the gate battle: font 1 at every call), so the name, the values, the place note and the order letter are in the map panel's font (PartyPanel); the window colour set for the name stays for the values
            // orig: the panel rows (root 0000:1716) draws the name at (4, y + 1) of the row's y = 1 + 0x28 * i (vis10 session tleads-battle-difficulty: the
            //   original's names stand a row lower than they were drawn here, in every battle frame)
            _panel.Draw(canvas, 4, y1 + 1, c.Member.ShortName, color);
            if (c.Status == CharacterStatus.Away)
            {
                // orig: the bars root 0000:1A9C: a member of status 5 shows "???? ???? ????" and nothing else (vis10, the synthetic save
                //   DKSAVE0-away24: the original's row of the away member)
                PartyPanel.DrawUnknownBars(canvas, _panel, i);
            }
            else
            {
                var note = _placeNotes[i];
                if (note is not null)
                {
                    DrawPlaceNote(canvas, i, note, color);
                }
                Bar(canvas, i, c.Endurance, c.MaximumEndurance, 0x15, 0x12, 9, color, note is null);
                Bar(canvas, i, c.Strength, c.MaximumStrength, 0x23, 0x20, 2, color, note is null);
                var favour = (int)Core.Characters.Attribute.DivineFavor;
                Bar(canvas, i, c.Attributes[favour], c.Maximums[favour], 0x30, 0x2D, 0x0E, color, note is null);
            }
            if (StatusLetter(i) is { } letter)
            {
                // orig: the order letter root 0000:18FA draws the letter as text in the window's colour, the name's (the leader's
                //   letter yellow: vis10 session tleads-battle-difficulty)
                _panel.Draw(canvas, 6, y + 0x1F, letter.ToString(), color);
            }
            if (c.Status == CharacterStatus.Dead)
            {
                // orig: the panel rows root 0000:1716: a dead member's row gets the grey hatching in a battle too (vis10 session D-09fbc1:
                //   the original's row of the fallen member)
                PartyPanel.DrawHatching(canvas, y1, 0x27);
            }
        }
    }

    /// <summary>The order letter of a member (member-status-glyph); none for a member who left the field.</summary>
    // orig: member-status-glyph root 0000:18FA (0000:18FA) draft (checked against the asm): nothing outside a battle or for record +0x14 == 5;
    // DS:A3D8[i] 1 'b' (no line of fire), 2 'e' (out of ammunition); else the order: command-kind (DS:A6B0) when DS:A695 == 1, command-mode == 1,
    // chosen-member == i and it is not -1, else +0x10; 0x400 the place action: DS:A6D1 on the same conditions, else DS:A6C4[i]
    // (cs:1A22: 1, 2 'U', 3, 6 'P', 4, 7 'D', 5 'O', 8 'X'); 0 '-', 1 'F' following else 'W', 6 'B', 0x0A 'V', 0x12 'A', 0x22 'P', 0x40 'R',
    // 0x80 'M', 0x100 'T'
    // DIVERGENCE: any other value (an A3D8, action or place the tables do not list) leaves the original's letter byte uninitialised (a stack
    // byte drawn as it is); '-' here.
    private char? StatusLetter(int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        if (c.Status == CharacterStatus.Away)
        {
            return null;
        }
        if (tb.FireBlocked(i) != 0)
        {
            return tb.FireBlocked(i) switch
            {
                1 => 'b',
                2 => 'e',
                _ => '-',
            };
        }
        var pending = tb.Paused && tb.InOrderMode && tb.Selected == i;
        var order = pending && tb.OrderAction != -1 ? tb.OrderAction : (int)c.Action;
        if (order == 0x400)
        {
            var place = pending && tb.PlaceOrder != -1 ? tb.PlaceOrder : (int)c.Place;
            return place switch
            {
                1 or 2 => 'U',
                3 or 6 => 'P',
                4 or 7 => 'D',
                5 => 'O',
                8 => 'X',
                _ => '-',
            };
        }
        return order switch
        {
            0 => '-',
            1 => (c.Flags & TacticalFlags.Following) != 0 ? 'F' : 'W',
            6 => 'B',
            0x0A => 'V',
            0x12 => 'A',
            0x22 => 'P',
            0x40 => 'R',
            0x80 => 'M',
            0x100 => 'T',
            _ => '-',
        };
    }

    // orig: member-gauge-bars root 0000:1C36..1CCF: text-paint-wrapped(DS:0168, text, 0x24, 0x13, i * 0x28 + dy + 9, font height, ...) with DS:01BC = 1
    // (centred) in the window's colour (the name's), dy = (i * 0x28 - ext.high + 0x1F) / 2 but at least -1, ext = wrapped-text-size of the same
    // text at y = i * 0x28 + 9 (its high word the last line's bottom, y - 1 + height); the window's font is slot 1 (as the name and the values)
    private void DrawPlaceNote(Canvas canvas, int row, string note, byte color)
    {
        var layout = new CardTextLayout(_panel);
        var text = TextPainter.Encode(note);
        var height = layout.MeasureHeight(text, 0x24, 0);
        var dy = Math.Max(-1, (0x1F - 9 - height + 1) / 2);
        layout.Draw(canvas, text, 0x13, row * PartyPanel.RowHeight + dy + 9, 0x24,
            new CardTextLayout.Settings { Color = color, Leading = 0, Alignment = TextAlignment.Center });
    }

    /// <summary>A 16-pixel bar: the lost part white at the top, the rest in the stat colour; the value below (the bar not when a place note stands there).</summary>
    // orig: member-gauge-bars root 0000:1A9C (as PartyPanel.DrawBar): a "+" at (x - 1, top - 4) when the value exceeds the maximum (only in the bars branch: with a place note none, root 0000:1C36..1CCF); the value and a space at (textX, row * 0x28 + 0x1F) with the window's last column 0x38, in the name's colour
    private void Bar(Canvas canvas, int row, int current, int maximum, int x, int textX, byte color, byte textColor, bool bar = true)
    {
        var realMaximum = maximum;
        var top = row * PartyPanel.RowHeight + 0x0C;
        var bottom = row * PartyPanel.RowHeight + 0x1B;
        maximum = Math.Max(1, maximum);
        var lost = 16 - (current << 4) / maximum;
        if (lost == 16 && current != 0)
        {
            lost = 15;
        }
        lost = Math.Max(0, lost);
        for (var y = top; bar && y <= bottom; y++)
        {
            var c = y < top + lost ? Palette.White : color;
            canvas[x, y] = c;
            canvas[x + 1, y] = c;
        }
        if (bar && current > realMaximum)
        {
            _panel.Draw(canvas, x - 1, top - 4, "+", textColor, 0x38);
        }
        _panel.Draw(canvas, textX, row * PartyPanel.RowHeight + 0x1F, current.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", textColor, 0x38);
    }

    private readonly Dictionary<string, Content.Formats.Graphics.Pic?> _portraits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The 11 × 19 short portrait of the member's picture family, in the member's colours.</summary>
    // orig: battle-portrait-set R002+0x0806 (0E76:0806) draft
    private void DrawShortPortrait(Canvas canvas, int slot, string code, int x, int y)
    {
        var name = code + "SHORT";
        if (!_portraits.TryGetValue(name, out var pic))
        {
            pic = _ui.Assets.PictureExists(name) ? _ui.Assets.Picture(name) : null;
            _portraits[name] = pic;
        }
        if (pic is null)
        {
            return;
        }
        var image = pic.Image;
        for (var py = 0; py < Math.Min(19, image.Height); py++)
        {
            for (var px = 0; px < Math.Min(11, image.Width); px++)
            {
                var p = image.Pixels[py * image.Width + px];
                if (p == 0)
                {
                    continue;
                }
                canvas[x + px, y + py] = p is >= 0xEB and <= 0xF2 ? (byte)(0x50 + 8 * slot + (p - 0xEB)) : p;
            }
        }
    }

    /// <summary>A message box centred in the view (notice-display).</summary>
    // orig: notice-display root 0000:24BC draft (spec E §3.5)
    private void DrawMessage(Canvas canvas)
    {
        if (_message is null)
        {
            return;
        }
        if (_ui.Clock.Milliseconds >= _messageUntil)
        {
            _message = null;
            return;
        }
        var text = _ui.Text;
        var tw = text.Measure(_message);
        var fh = text.LineHeight;
        var tx = (320 - tw - 60) / 2 + BattlefieldView.Left;
        var ty = 100 - fh / 2;
        canvas.Fill(tx - 5, ty - 4, tw + 10, fh + 7, 0xFF);
        Frame(canvas, tx - 5, ty - 4, tw + 10, fh + 7, 0x86);
        Frame(canvas, tx - 4, ty - 3, tw + 8, fh + 5, 0x82);
        text.Draw(canvas, tx, ty, _message, 1);
    }

    private static void Frame(Canvas canvas, int x, int y, int w, int h, byte color)
    {
        for (var k = 0; k < w; k++)
        {
            canvas[x + k, y] = color;
            canvas[x + k, y + h - 1] = color;
        }
        for (var k = 0; k < h; k++)
        {
            canvas[x, y + k] = color;
            canvas[x + w - 1, y + k] = color;
        }
    }

    /// <summary>The battle's pointer: the glove while running, the order hand while paused; arrows at the view edges.</summary>
    // orig: R014+0x33A2 (0E76:33A2) draft; mouse-cursor-plain R014+0x4780 (0E76:4780) draft
    // C: pointer-shape-set(hx, hy, sprite) gives each shape its hot spot: the driver draws the sprite at (mouse − hot spot). The glove and the
    // order hand have (0, 0); the arrows (3, 0) up, (7, 0) up-right, (7, 4) right, (7, 7) down-right, (3, 7) down, (0, 7) down-left, (0, 4) left,
    // (0, 0) up-left.
    private void DrawPointer(Canvas canvas)
    {
        var mouse = _ui.Mouse;
        int x = mouse.X, y = mouse.Y;
        int image, hx = 0, hy = 0;
        var arrow = x >= 60 ? EdgeArrow(x, y) : -1;
        if (arrow >= 0)
        {
            image = BattleImages.ScrollArrows + arrow;
            (hx, hy) = ArrowHotSpots[arrow];
        }
        else
        {
            // C: root 0000:00A0 (and R022+0x0362 after the sheet): DS:A695 == 0 the normal pointer, any other value (0x11 too) the order pointer
            image = _battle!.PauseByte == 0 ? BattleImages.NormalPointer : BattleImages.OrderPointer;
        }
        var sprite = _assets.Images[image];
        _underPointer = sprite is null ? null : canvas.Save(x - hx, y - hy, sprite.Width, sprite.Height);
        SpriteDrawing.Draw(canvas, sprite, x - hx, y - hy, new Clip(0, 0, Canvas.Width, Canvas.Height));
    }

    /// <summary>The hot spots of the scroll arrows (pointer-shape-set's first two arguments in R014+0x33A2), by <see cref="EdgeArrow"/>.</summary>
    private static readonly (int X, int Y)[] ArrowHotSpots = [(3, 0), (7, 0), (7, 4), (7, 7), (3, 7), (0, 7), (0, 4), (0, 0)];

    /// <summary>What the last pointer drawn covered: the original's pointer is the mouse driver's, never part of the picture.</summary>
    private SavedRegion? _underPointer;

    /// <summary>Takes the pointer drawn by the last frame off the canvas (pointer-hide).</summary>
    private void ErasePointer()
    {
        if (_underPointer is { } under)
        {
            _ui.Canvas.Restore(under);
            _underPointer = null;
        }
    }

    /// <summary>The scroll arrow of an edge zone (0 up … 7 up-left), −1 none.</summary>
    // orig: R014+0x33A2 (the zones: up 0x7D &lt; x &lt; 0xFF, y &lt; 8; the corners and sides as below; the zones do not overlap, so the order of the tests does not matter)
    private static int EdgeArrow(int x, int y)
    {
        if ((x is >= 60 and <= 125 && y < 8) || (x is >= 60 and < 68 && y <= 50))
        {
            return 7;
        }
        if ((x >= 255 && y < 8) || (x >= 311 && y <= 50))
        {
            return 1;
        }
        if ((x is >= 60 and < 68 && y >= 150) || (x <= 125 && y > 191))
        {
            return 5;
        }
        if ((x >= 311 && y >= 150) || (x >= 255 && y > 191))
        {
            return 3;
        }
        if (x is >= 126 and < 255 && y < 8)
        {
            return 0;
        }
        if (y > 191)
        {
            return 4;
        }
        if (x is >= 60 and < 68 && y is > 50 and < 150)
        {
            return 6;
        }
        if (x >= 311 && y is > 50 and < 150)
        {
            return 2;
        }
        return -1;
    }
}
