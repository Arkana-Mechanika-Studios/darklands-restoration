using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Simulation;
using Darklands.Simulation.Mods;
using Darklands.Simulation.Overland;

namespace Darklands.Presentation;

/// <summary>The whole game: the program start, the opening, the start screen, then card after card until the game ends.</summary>
// orig: the program start R000+0x0022 .. R000+0x052A before the main loop function (StartUp); tests: StartUpTests
// orig: the main function root 03A1:000A matched: opening animations, start-up loads, the start screen (thunk 09C0:18DD = R037+0x9C48), then the card loop `do { DS:A764 = (*R136+0xD090[DS:A772])(); ... } while (DS:A764 != 0x62 && DS:A764 != -0x63)`; tests: none (needs a host and the game files)
public sealed class DarklandsGame
{
    private readonly GameUi _ui;
    private readonly GameFiles _files;
    private WorldData? _worldData;
    private CardDispatcher _cards = new();
    private ModSet _mods = ModSet.None();
    private readonly IHost _host;

    public DarklandsGame(IHost host, GameFiles files)
    {
        _host = host;
        var assets = new GameAssets(files);
        _ui = new GameUi(host, assets);
        _files = files;
        SaveDirectory = DefaultSaveDirectory(files.Root);
    }

    /// <summary>The world's data, read on first use (after the program start has checked the files).</summary>
    private WorldData World => _worldData ??= WorldData.Load(_files);

    public GameUi Ui => _ui;

    /// <summary>The game being played (null before one starts and after it ends): what a host's developer console acts on.</summary>
    public GameState? Playing { get; private set; }

    /// <summary>A battle is running (a host's state report: tools/oracle/lockstep.py does not compare a state taken inside a battle).</summary>
    public bool Fighting => _battles?.Fighting == true;

    private BattleScreens? _battles;

    /// <summary>True while the start screen shows (a host may draw the program's version over the window then).</summary>
    public bool OnStartScreen { get; private set; }

    /// <summary>Seed for new games and for reseeding after time passes; the game uses the clock.</summary>
    public Func<uint> Seed { get; set; } = () => (uint)Environment.TickCount;

    /// <summary>The BIOS tick word (0040:006C) the start screen's Quickstart and Create characters take as the party seed; the game reads the BIOS counter, not <c>time(0)</c>.</summary>
    // orig: R037+0x9DB2 (keys C and Q): party_seed (DS:9C4A) = the word at 0040:006C
    // C: a tester (the headless host's ticks: step) gives the word the original read; the default is the BIOS count's value now (Platform.DosClock)
    public Func<ushort> PartyTicks { get; set; } = () => DosClock.TickWordOfDay(DateTime.Now.TimeOfDay);

    /// <summary>
    /// The folder of the saved games (DKSAVEn.SAV and .BSV files). Default: the game folder's SAVES, as the
    /// original uses (shared with it); the settings file's savesFolder, DARKLANDS_SAVES or --saves override it.
    /// </summary>
    // orig: the save/load code opens saves\ relative to the game's folder (R037); the folder name is matched case-insensitively.
    public string SaveDirectory { get; set; } = "";

    /// <summary>The bugs the games played here have fixed: the player's host sets <see cref="OriginalBehaviour.DataSafe"/>, testers keep the faithful game.</summary>
    public OriginalBehaviour Behaviour { get; set; } = OriginalBehaviour.Faithful;

    /// <summary>The game folder's SAVES folder (an existing one in any letter case, else SAVES).</summary>
    public static string DefaultSaveDirectory(string gameFolder) =>
        Directory.Exists(gameFolder)
            ? Directory.EnumerateDirectories(gameFolder).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "SAVES", StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(gameFolder, "SAVES")
            : Path.Combine(gameFolder, "SAVES");

    private SaveFolder Saves => new(SaveDirectory) { Mods = SaveMods };

    /// <summary>
    /// The mods this game runs with (docs/MODS.md); default none: the game's own cards only (Classic). Setting it registers
    /// the mods' card texts and makes the dispatcher run the registry's cards.
    /// </summary>
    // DIVERGENCE: no original: mod cards (a registry beside the game's card table, CardRegistry) and their saves' companion files (ModdedSaves).
    public ModSet Mods
    {
        get => _mods;
        set
        {
            _mods = value;
            foreach (var texts in value.Texts)
            {
                _ui.Assets.AddMessages(texts);
            }
            _cards = new CardDispatcher(value.Cards);
        }
    }

    /// <summary>Where the game's own notes go (the mods' lines, a companion file it could not use); a host passes its console.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>What the save folder needs of the mods: none (null) without a mod card, so Classic saves as before.</summary>
    private SaveMods? SaveMods => _mods.Cards.HasModCards
        ? new SaveMods { Active = _mods.Marks, SavedCardOf = n => _mods.Cards.ModCards.TryGetValue(n, out var r) ? r.SavesAs : null, HasCard = _mods.Cards.Contains }
        : null;

    /// <summary>The card numbers a saved game's header takes for the mods' new cards (the headless host's state writer), null without mods.</summary>
    public Func<int, int?>? SavedCardOf => SaveMods?.SavedCardOf;

    private IReadOnlyList<string>? _loadScreenTexts;

    private IReadOnlyList<string> LoadScreenTexts =>
        _loadScreenTexts ??= Content.Formats.Saves.LoadScreenTexts.Decode(_ui.Assets.Files.Read("DARKLAND.EXE"));

    public GameOptions Options => _ui.Options;

    /// <summary>Runs the program start (the BANNER.DAT banner, the configuration and its checks) before the opening.</summary>
    // orig: R000+0x0022 (main) .. R000+0x052A, see StartUp
    public bool ShowStartUp { get; set; } = true;

    /// <summary>The DOS command line after the program's name (the original's -Q, -O, -A&lt;driver letter&gt;), read by the program start.</summary>
    // orig: argv of R000+0x0114
    public IReadOnlyList<string> CommandLine { get; set; } = [];

    /// <summary>The command line's Q: no opening, ending or death animations.</summary>
    // orig: DS:0BF2, set by Q in R000+0x0114
    private bool _noAnimations;

    private Cinematics Cinematics => new(_ui) { Animations = !_noAnimations };

    /// <summary>Shows the opening animations before the start screen.</summary>
    // orig: `if (!DS:0BF2)` in root 03A1:000A: DS:0BF2 is set by the command-line letter Q (R000+0x0114, "case 'Q': DS:0BF2 = 1") which also skips the ending and the death animation
    public bool PlayOpening { get; set; } = true;

    /// <summary>A debugging aid: a Quickstart game begins at this card instead of the first (null: as the game does).</summary>
    public int? QuickstartCard { get; set; }

    /// <summary>A debugging aid: the game starts with this saved game of the folder (as the start screen's load would), null: the start screen.</summary>
    public string? StartWithSave { get; set; }

    // DIVERGENCE: the original ends the program when the card loop ends (the 0x62 or -0x63 screen): it frees its buffers and exits to DOS; here the player comes back to the start screen, where Alt-Q leaves.
    public void Run()
    {
        try
        {
            StartUp? startUp = null;
            if (ShowStartUp)
            {
                startUp = new StartUp(_ui, _files, CommandLine);
                if (startUp.Run() is not { } started)
                {
                    return;
                }
                _noAnimations = started.NoAnimations;
                if (PlayOpening && !_noAnimations && !startUp.CheckOpeningSamples(started))
                {
                    return;
                }
            }
            if (PlayOpening && !_noAnimations)
            {
                Cinematics.Opening();
            }
            // orig: root 03A1:000A sets the start-up colours 0..15 after the opening, before anything else is drawn.
            _ui.UseStartupColors();
            if (startUp is not null && !startUp.LoadGameData())
            {
                return;
            }
            if (StartWithSave is { } first)
            {
                PlayFrom(LoadSavedGame(first, _session ??= NewGame.StartUp(World, Seed())).State);
                return;
            }
            while (true)
            {
                StartChoice choice;
                OnStartScreen = true;
                try
                {
                    choice = new StartScreen(_ui).Run();
                }
                finally
                {
                    OnStartScreen = false;
                }
                switch (choice)
                {
                    case StartChoice.Quit:
                        return;
                    case StartChoice.Quickstart:
                        var quickstart = NewGame.Quickstart(World, Seed(), Seed, PartyTicks());
                        quickstart.CurrentCard = QuickstartCard ?? quickstart.CurrentCard;
                        PlayFrom(quickstart);
                        break;
                    // orig: R037+0x9DB2 case 0x1454/0x1474: DS:E89A = 1; if (R037+0x81B2()) return 0x11b (play); else the start screen again
                    // C: the game in memory (at first the program start's, NewGame.StartUp) is what the load reads over: city_rec and the session globals.
                    case StartChoice.LoadGame when new LoadGameScreen(_ui, Saves, LoadScreenTexts).Run(fromStartScreen: true, name => LoadSavedGame(name, _session ??= NewGame.StartUp(World, Seed()))) is { } loaded:
                        PlayFrom(loaded.State);
                        break;
                    case StartChoice.CreateCharacters when new CharacterCreationScreen(_ui, World, Seed, PartyTicks).Run() is { } created:
                        PlayFrom(created);
                        break;
                    default:
                        // Backing out of character creation or of the load screen: the start screen again.
                        break;
                }
            }
        }
        catch (QuitGameException)
        {
        }
    }

    /// <summary>The game last in memory (the program start's until one is played): what a load from the start screen reads over.</summary>
    private GameState? _session;

    /// <summary>Plays a game; a load while playing (Alt-L) goes on with the loaded game.</summary>
    // orig: setjmp(DS:A752) in the main function: load_game_vec longjmps back to it and the loop continues with the loaded DS:A772
    private void PlayFrom(GameState state)
    {
        try
        {
            PlayEach(state);
        }
        finally
        {
            Playing = null;
        }
    }

    private void PlayEach(GameState state)
    {
        while (true)
        {
            try
            {
                _session = state;
                state.Behaviour = Behaviour;
                Playing = state;
                Play(state);
                return;
            }
            catch (GameOverException)
            {
                return;
            }
            catch (LoadGameRequestedException jump)
            {
                state = jump.State;
            }
            finally
            {
                _ui.GameKey = null;
                _ui.MenuBar = null;
            }
        }
    }

    /// <summary>Reads a saved game of the folder, with the messages the original shows while it loads.</summary>
    // orig: R037+0x8ED4 (1C7D:0D24) matched: after the header show_difficulty(1), then
    // text_box "Loading Game" until the file is read; opt_music and opt_sound come from the file
    // C: the options' menu ticks (DS:996E, DS:9982) are set too; there is no menu bar here.
    private LoadedGame LoadSavedGame(string name, GameState? running)
    {
        var loaded = Saves.Load(name, World, running);
        var box = new MessageBox(_ui);
        box.Show(HotKeys.DifficultyText(loaded.State.Difficulty));
        var shown = box.Open("Loading Game");
        loaded.State.ClockSeed = Seed;
        Options.Music = loaded.Music;
        Options.SoundEffects = loaded.SoundEffects;
        box.Close(shown);
        // DIVERGENCE: no original: a game saved with mods (its companion file, ModdedSaves) that are not loaded now goes on without
        //   them, from the game's cards its .SAV names; the player is told which mods are missing.
        if (loaded.Mods.Problem is { } problem)
        {
            Log?.Invoke($"{name}: {problem}");
        }
        if (loaded.Mods.MissingMods is { Count: > 0 } missing)
        {
            Log?.Invoke($"{name} was saved with mods not loaded now ({string.Join(", ", missing)}): the game goes on without them");
            var lines = new List<string> { missing.Count == 1 ? "This game was saved with the mod" : "This game was saved with the mods" };
            // at most three names (the notice has six boxes)
            lines.AddRange(missing.Count <= 3 ? missing.Select(m => m.ToString()) : [.. missing.Take(2).Select(m => m.ToString()), $"and {missing.Count - 2} more"]);
            lines.Add("which is not loaded now. It goes on");
            lines.Add("without it. Press a key.");
            if (missing.Count > 1)
            {
                lines[^2] = "which are not loaded now. It goes on";
                lines[^1] = "without them. Press a key.";
            }
            box.Notice(lines);
        }
        return loaded;
    }

    /// <summary>Alt-L while playing: "Loading is irreversable. Proceed?", then the load screen; the game goes on with what it gives.</summary>
    // orig: load_game R037+0x8B76 (1C7D:09C6) matched: card_window->f10 = 3; the question;
    // the debug log 1905("load", "a"), not reproduced (see Card.ShowPicture); in a battle (DS:907A == 1) the battle's pictures, sounds and level buffers are freed and its
    // flags reset; the card picture freed; `party[i][0] = 0` for the five slots; with DS:A891 == 3 the map's buffers freed; the events
    // freed; the load screen (DS:E89A = 0: no Return button); longjmp(DS:A752, -1)
    // BUG: OriginalBehaviour.FixLoadScreenEscape - Esc on the load screen goes on with the emptied slots and no events.
    // C: the battle's and the map's buffers are their screens' own here; a load from a battle leaves the battle by the jump.
    // orig: load_game R037+0x8B76 (1C7D:09C6) matched: in a battle (DS:907A = 1) its buffers are freed and cards_startup_vec(0) runs
    // (R043+0x078E: srand((unsigned)time(0)), TimePassage.Reseed) before the load screen
    private void LoadInGame(GameState state, bool inBattle = false)
    {
        // orig: R037+0x8B76 card_window->f10 = 3 first: the question is in font 3 from Alt-L too (GameUi.CardWindowFont)
        _ui.CardWindowFont = 3;
        if (!new MessageBox(_ui).Ask("Loading is irreversable. Proceed?"))
        {
            return;
        }
        if (inBattle)
        {
            Core.Rules.TimePassage.Reseed(state);
        }
        if (!state.Behaviour.FixLoadScreenEscape)
        {
            foreach (var member in state.Party.Members)
            {
                member.Status = Core.Characters.CharacterStatus.Empty;
            }
            state.Events.Clear();
        }
        var screen = new LoadGameScreen(_ui, Saves, LoadScreenTexts) { Playing = OriginalSaves.FileNameOf(state) };
        var loaded = screen.Run(fromStartScreen: false, name => LoadSavedGame(name, state));
        throw new LoadGameRequestedException(loaded?.State ?? state);
    }

    /// <summary>Leaves the current place for the open country and travels until something happens.</summary>
    // orig: overland_map R030+0x0000 (1462:0000) draft (bytes match): from a city (cur_loc < 92) the party starts at the city's exit point, from another place at its position, in the wilderness where it is; then `if (event_started(0x47)) return next_card = 0xf1;` and `if (!event_started(0x63)) { copyprot_run_vec(); event_here(0x63, 0x3e80); }` before overland_open
    private int Travel(GameState state, PartyPanel panel, Action<int> sheet, Action<int> camp)
    {
        _map ??= new WorldMap(Content.Formats.Maps.WorldMapData.Decode(_ui.Assets.Files.Read("DARKLAND.MAP")), state.Locations);
        if (state.InCity && World.Cities.Find(state.CurrentLocation) is { } city)
        {
            state.MapPosition = new Core.World.MapPosition(city.Data.ExitX, city.Data.ExitY);
        }
        else if (state.Here is { } place)
        {
            state.MapPosition = place.Position;
        }
        // C: the position is set BEFORE this test, so the party is moved to the exit point even when the card 0xF1 ("a member has left") is shown instead of the map.
        if (state.Events.Started(MemberLeftEvent))
        {
            return MemberLeftCard;
        }
        if (!state.Events.Started(CopyProtectionEvent))
        {
            // DIVERGENCE: copyprot_run_vec (R041+0x0000) asks the copy-protection question (a symbol from the manual); it is not asked here (a host
            //   choice, as the later CD release). Its five random draws are made (CopyProtection.Draw: rand_mod 4, 20, 7, 7 and 6, before anything else
            //   of it), so the generator goes on as the original's; the event it leaves behind, for 0x3E80 hours (about 22 months), is created as in the original.
            //   Justified (evid1o): a right answer (a click on the picture or keys 1..4 against the symbol asked, R041+0x050E; key 5, top row or keypad,
            //   passes whatever was asked: R041+0x0537 / 0x0553) changes nothing else of the game: R041+0x04DA puts the screen back and restores DS:2816 /
            //   DS:2804 from the copies R041+0x0006 made; only the background window's text colour DS:2812 stays at the 14 set at R041+0x00C8 (presentation); a wrong one
            //   ends the program (R041+0x0594: shutdown, exit(0) at R041+0x05C2). This rests on the reading of the code: a direct call of R041+0x0000 from
            //   the Tyrnau map state (j_copy.py) ended in the tester right after the five draws, so it shows only those.
            Core.World.CopyProtection.Draw(state.Random);
            state.Events.CreateHere(CopyProtectionEvent, CopyProtectionHours);
        }
        return new OverlandScreen(_ui, panel, _map) { CharacterSheet = sheet, Camp = camp }.Travel(state);
    }

    /// <summary>Event 0x47 (a member has left; Type = member) shows card 0xF1 instead of the map.</summary>
    private const int MemberLeftEvent = 0x47;

    private const int MemberLeftCard = 0xF1;

    /// <summary>Event 0x63, the copy-protection check passed: the first visit to the map creates it for 0x3E80 hours.</summary>
    private const int CopyProtectionEvent = 0x63;

    private const int CopyProtectionHours = 0x3E80;

    private WorldMap? _map;

    /// <summary>Alt-S: the save dialog; inside a battle the battle's own Alt-S (BattleScreens.Save) saves it with the battle.</summary>
    // orig: save_game_vec = save_game R037+0x2A68 (hot_keys key 0x1F00)
    private void SaveGame(GameState state, BattleScreens battles, Core.Battles.Tactics.TacticalBattle? battle = null)
    {
        if (battle is not null)
        {
            new SaveGameDialog(_ui, Saves).Run(state, battle);
        }
        else if (!battles.Fighting)
        {
            new SaveGameDialog(_ui, Saves).Run(state);
        }
    }

    // orig: the do/while of root 03A1:000A: a screen function returns the next screen number; 0x0C is the overland map (overland_map), -2..-6 start a battle, 0x62 ends the game, -0x63 quits
    private TablePersonalNames? _personalNames;

    /// <summary>The battle view's centre (DS:C866 / DS:C886), a program global: a game loaded keeps it.</summary>
    private (int Column, int Row) _battleViewCentre;

    /// <summary>The combatants' seen positions (20A5:79A9), a program global: a game loaded keeps them.</summary>
    private Core.Battles.MapPoint[][]? _seenPositions;

    /// <summary>The cursor keys' state (DS:D728, DS:D73B, DS:D7FE, DS:D947, DS:D945), program globals: a game loaded keeps them (a battle
    /// file restored replaces them).</summary>
    private readonly Core.Battles.Tactics.CursorKeyState _cursorKeys = new();

    /// <summary>
    /// A card returned −2..−6: the battle it ordered is fought, the dead leave the party and the
    /// rest stand up again; if nobody is left (or all fell, result 5) the game ends with the
    /// death animation. Returns the card the battle leads to.
    /// </summary>
    // orig: main loop root 03A1:000A (matched per the byte match) - cards_shutdown_vec,
    //   combat_flags |= 4 (−3) / 8 (−5) / 0x80 (−6) / 0xC (−4), |= 1, battle_run(battle_mode, battle_enemy, battle_seed,
    //   −1, A76C, 0, −1, A76E, 0, −1, A770, 0), status 3 → 0 with party_delete_vec, others but 0 and 5 → 1,
    //   none left or battle_result == 5 → music 0x12, "death", exit; else cards_startup_vec and the result is the next screen.
    // DIVERGENCE: the game returns to the start screen instead of exiting to DOS; the work files of cards_shutdown and cards_startup are
    //   kept in memory, their effects made (the events, the reseed, the city record: see CardContext.RunBattle).
    // orig: cards_shutdown R043+0x054E writes the events to events.tmp and empties the table (EventLog.Shutdown); cards_startup
    //   R043+0x078E (1462:078E) matched begins with srand((unsigned)time(0)): TimePassage.Reseed, and its world_load reads the events
    //   back (EventLog.Reload). A load in the battle leaves it by the jump (LoadGameRequestedException): no cards_startup.
    // C: with DS:9081 bit 0 (GameState.ResumeBattle: a game saved in a battle) the card was skipped and the code is the DS:A764 the loop
    // holds (not a battle code after a load); R002+0x0000 takes the battle up from the battle file and clears the bit at its end.
    private int FightTheBattle(GameState state, CardContext context, BattleScreens battles, int code)
    {
        // orig: root 03A1:000A calls cards_shutdown_vec (R043+0x054E) before the battle; it ends with sound_play(1) (root 047C:078A)
        state.Events.Shutdown();
        _ui.Audio.PlayMusic(Darklands.Audio.GameSounds.Stop);
        var orders = state.BattleOrders;
        orders.Flags |= code switch { -3 => 4, -5 => 8, -6 => 0x80, -4 => 0xC, _ => 0 };
        orders.Flags |= 1;
        try
        {
            orders.Result = battles.Battle(new BattleRequest(orders.Enemy, orders.Seed,
                [(-1, orders.EnemyTypes[0], 0), (-1, orders.EnemyTypes[1], 0), (-1, orders.EnemyTypes[2], 0)],
                orders.Mode, Persistent: true, CombatFlags: orders.Flags, Resume: state.ResumeBattle));
        }
        finally
        {
            // orig: R002+0x0000 masks the combat flags with 0xFE10 at its end (R037+0x8B76 does the same when a load leaves the battle)
            state.ResumeBattle = false;
            orders.Flags &= 0xFE10;
        }
        var members = state.Party.Members;
        // orig: root 03A1:000A (matched per the byte match): party_delete_vec for each slot of status 3, before cards_startup
        // C: the dead are deleted first (slot by slot, highest first here because DeleteMember trims empty slots at the end).
        // BUG: party_delete_vec runs while the events are in events.tmp, so a member who died in the battle keeps the term and
        //   "has left" events (kinds 0x43/0x47) it would cancel: they come back with the table.
        for (var i = members.Count - 1; i >= 0; i--)
        {
            if (members[i].Status == Core.Characters.CharacterStatus.Dead)
            {
                context.DeleteMember(i);
            }
        }
        foreach (var m in state.Party.Members)
        {
            if (m.Status is not Core.Characters.CharacterStatus.Empty and not Core.Characters.CharacterStatus.Away)
            {
                m.Status = Core.Characters.CharacterStatus.Able;
            }
        }
        if (state.Party.Members.All(m => m.Status == Core.Characters.CharacterStatus.Empty) || orders.Result == 5)
        {
            Cinematics.Death();
            return CardDispatcher.EndOfGame;
        }
        state.Party.EnsureAbleLeader();
        // C: cards_startup (thunk of R043+0x078E): the clock reseed, then world_load's city record and events (CardContext.RunBattle has the details).
        Core.Rules.TimePassage.Reseed(state);
        Core.World.CityFile.ReloadAfterBattle(state);
        state.Events.Reload();
        // orig: root 03A1:0438..0441: after cards_startup (thunk 09C0:1919(0), 03A1:0430) thunk 09C0:190F(0) = advance_hours(0), before the
        // pass's own end calls it again (03A1:0463, CardDispatcher.FinishPass): two more clock reseeds (lockstep session dungeon-mine-doormsg:
        // the second second of the original's reload took the next clock seed)
        Core.Rules.TimePassage.Advance(state, 0);
        return battles.LastResultCard;
    }

    private void Play(GameState state)
    {
        CardContext? context = null;
        var panel = new PartyPanel(_ui);
        var screen = new CardScreen(_ui, () => context!, panel);
        context = new CardContext(state, World, screen);
        context.PersonalNames = _personalNames ??= new TablePersonalNames(Content.Formats.Characters.PersonalNamesDecoder.Decode(_ui.Assets.Files.Read("DARKLAND.EXE")));
        SaintPrayerScreen.Attach(context, _ui, panel);
        var battles = new BattleScreens(_ui, () => state, (items, money) => context.Screens.Loot(items, money))
        {
            CharacterSheet = member => context!.Screens.CharacterSheet(member),
            PartyInfo = () => context!.Screens.PartyInfo(),
        };
        battles.Save = battle => SaveGame(state, battles, battle);
        _battles = battles;
        // C: the view centre DS:C866 / DS:C886 is a global the load leaves as it was: a battle taken up from the file loaded in a battle is
        // drawn around the centre the battle left had (R014+0x02FF), and so its members' screen positions (+0x00 / +0x02) as its next save
        // writes them (lockstep session savemid-mine)
        battles.ViewCentre = _battleViewCentre;
        battles.SeenPositions = _seenPositions;
        battles.CursorKeys = _cursorKeys;
        battles.Load = () =>
        {
            _battleViewCentre = battles.CurrentViewCentre;
            _seenPositions = battles.SeenPositions;
            LoadInGame(state, inBattle: true);
        };
        // orig: run_battle R029+0x2278: a card's own battle (battle_run with mode 0).
        context.Battle = (side, seed, p) => battles.Battle(new BattleRequest(side, seed,
            [(p[0], p[1], p[2]), (p[3], p[4], p[5]), (p[6], p[7], p[8])]));
        context.PlayFilm = steps => Cinematics.Film(steps);
        // orig: run_battle R029+0x2278: nobody left after the battle: music_play(0x12), the death film, exit_(0)
        context.AllDead = () =>
        {
            Cinematics.Death();
            throw new GameOverException();
        };
        // orig: music_play R043+0x09AE: the piece if music is on, else 1 (stop) — GameUi.PlayMusic applies the option.
        context.Music = tune => _ui.PlayMusic(tune);
        // orig: sound_play root 047C:078A: straight to the towns-and-map driver, whatever the music option says.
        context.SoundPlayer = number => _ui.Audio.PlayMusic(number);
        // orig: music_play(5) R043+0x09AE: the AX of driver command 5 (or of the stop with music off), which $Shell00 keeps in DS:9C3D
        context.MusicPlaying = _ui.MusicPlaying;
        context.SavedMusic = _ui.SavedMusic;
        var shops = new GameScreens(_ui, panel, state, new ScreensUnavailable(screen));
        var screens = new GameScreens2(_ui, panel, state, World, shops);
        // the barter, item store and loot screens open the character sheet from their party column
        shops.Sheet = screens.CharacterSheet;
        // in a battle a member who left the field through an exit is refused by the sheet and skipped by the loot screen's D
        screens.CannotView = battles.CannotViewMember;
        shops.CannotView = battles.LootSkipsMember;
        context.Screens = screens;
        var hotKeys = new HotKeys(_ui, state, Options)
        {
            Save = () => SaveGame(state, battles),
            Load = () =>
            {
                _battleViewCentre = battles.CurrentViewCentre;
                _seenPositions = battles.SeenPositions;
                LoadInGame(state);
            },
            PartyInfo = () => context.Screens.PartyInfo(),
            CharacterSheet = member => context.Screens.CharacterSheet(member),
            // orig: thunk 09C0:183D = R022+0x051C outside a battle (the battle has its own: BattleScreen)
            ChoosePartyOrder = () => new MarchingOrderScreen(_ui, panel, battles.SelectOrdinal).Run(state),
        };
        screens.HotKeys = hotKeys;
        // orig: thunk 09C0:1EE1 = R038+0x0000, opened by R029+0x2246 from the inn card ($Party00)
        context.PartyRosterScreen = screens.PartyRoster;
        _ui.Redraw = null;
        _ui.GameKey = hotKeys.Handle;
        _ui.MenuBar = MenuBar.ForGame(_ui, state, Options, hotKeys);
        screens.MenuBarKey = hotKeys.Handle;
        // orig: saint_pray (byte-matched): a saint row on the character sheet prays for that member.
        screens.ShowSaint = (member, saint) =>
            new SaintPrayerScreen(_ui, panel) { InBattle = () => battles.Fighting }.Pray(state, member, saint, Core.Rules.PrayerMode.ForMember, Core.Rules.SaintEffects.Instance);
        // orig: hours_pass R029+0x255A: with nobody left, music_play(0x12), cards_shutdown_vec, the death film and exit_(n)
        void Perished(GameState perished)
        {
            if (ReferenceEquals(perished, state))
            {
                Cinematics.DeathAfterShutdown();
                throw new GameOverException();
            }
        }
        Core.Rules.TimePassage.PartyPerished += Perished;
        try
        {
            while (true)
            {
                var card = state.CurrentCard;
                if (card is CardDispatcher.EndOfGame or CardDispatcher.QuitGame)
                {
                    return;
                }
                if (card == CardDispatcher.OverlandMap)
                {
                    // orig: C / F8 on the map: thunk 09C0:1FF9 = R036+0x0000 (1462:0000) with the party's watch value (R030+0x25BE): the
                    //   wilderness stay with the day's poaching risk (CampScreen.CampInTheWild)
                    // orig: the map is card 0x0C of the table: root 03A1:000A ends its pass as any card's (03A1:0442..0463): the previous-screen
                    //   word becomes 0x0C only after the map returns (a game saved on the map keeps the card before), then advance_hours(0)
                    var afterMap = Travel(state, panel, context.Screens.CharacterSheet, screens.CampInTheWild);
                    if (CardDispatcher.IsBattle(afterMap))
                    {
                        CardDispatcher.FinishPass(state, FightTheBattle(state, context, battles, afterMap));
                    }
                    else
                    {
                        CardDispatcher.FinishPass(state, afterMap);
                    }
                    continue;
                }
                // orig: root 03A1:000A runs the card (the table at 2DAE:0000) only while DS:9081 bit 0 is clear: a game saved in a battle skips its
                // card and fights on.
                if (state.ResumeBattle)
                {
                    CardDispatcher.FinishPass(state, FightTheBattle(state, context, battles, 0));
                    continue;
                }
                if (CardDispatcher.IsBattle(card))
                {
                    state.CurrentCard = FightTheBattle(state, context, battles, card);
                    continue;
                }
                var next = _cards.RunCard(context);
                if (CardDispatcher.IsBattle(next))
                {
                    CardDispatcher.FinishPass(state, FightTheBattle(state, context, battles, next));
                }
            }
        }
        finally
        {
            Core.Rules.TimePassage.PartyPerished -= Perished;
        }
    }
}
