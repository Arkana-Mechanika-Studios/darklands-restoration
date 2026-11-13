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
    // orig: R037+0x9DB2 (keys C and Q): party-random-seed (DS:9C4A) = the word at 0040:006C
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

    private SaveFolder Saves => new(SaveDirectory)
    {
        Mods = SaveMods,
        Used = SaveUsed is { } used ? (name, loading) => used(SaveDirectory, name, loading) : null,
    };

    /// <summary>
    /// A host's notice (null: none) of the saved game being loaded (just before its file is read) or saved: the save folder, the file's
    /// name, true for a load. The host's problem reports use it; the game does not.
    /// </summary>
    // Host feature, not part of the original: the game's behaviour is unchanged.
    public Action<string, string, bool>? SaveUsed { get; set; }


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

    /// <summary>What the program start read (CONFIG.DRK's driver, the command line's Q), read again by a new run (null without <see cref="ShowStartUp"/>).</summary>
    public StartUpResult? Started { get; private set; }

    private Cinematics Cinematics => new(_ui) { Animations = !_noAnimations };

    /// <summary>Shows the opening animations before the start screen.</summary>
    // orig: `if (!DS:0BF2)` in root 03A1:000A: DS:0BF2 is set by the command-line letter Q (R000+0x0114, "case 'Q': DS:0BF2 = 1") which also skips the ending and the death animation
    public bool PlayOpening { get; set; } = true;

    /// <summary>A debugging aid: a Quickstart game begins at this card instead of the first (null: as the game does).</summary>
    public int? QuickstartCard { get; set; }

    /// <summary>A debugging aid: the game starts with this saved game of the folder (as the start screen's load would), null: the start screen.</summary>
    public string? StartWithSave { get; set; }

    /// <summary>Runs the program: the start-up, the opening, the start screen and the games it leads to, until the player quits.</summary>
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
                Started = started;
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
                if (LoadSavedGame(first, StartUpSession(), beforeNotice: () => LoadGameScreen.PaintBackdrop(_ui)) is { } given)
                {
                    PlayFrom(given.State);
                    return;
                }
                // a refused load (FixDamagedSavedGame) goes on at the start screen
            }
            while (true)
            {
                if (_played && !RestartProgram())
                {
                    return;
                }
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
                        quickstart.BattleFile = ProgramBattleFile();
                        PlayFrom(quickstart);
                        break;
                    // orig: R037+0x9DB2 case 0x1454/0x1474: DS:E89A = 1; if (R037+0x81B2()) return 0x11b (play); else the start screen again
                    // C: the game in memory (at first the program start's, NewGame.StartUp) is what the load reads over: city-record and the session globals.
                    case StartChoice.LoadGame when new LoadGameScreen(_ui, Saves, LoadScreenTexts).Run(fromStartScreen: true, name => LoadSavedGame(name, StartUpSession())) is { } loaded:
                        PlayFrom(loaded.State);
                        break;
                    case StartChoice.CreateCharacters when new CharacterCreationScreen(_ui, World, Seed, PartyTicks) { MadeUpName = name => _cardVariables.Named[0] = name }.Run() is { } created:
                        created.BattleFile = ProgramBattleFile();
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
        catch (ProgramExitException exit)
        {
            // orig: root 0617:067E: the text screen, perror's line, exit(99): DOS comes back with the line on the screen
            ProgramExit = exit;
            try
            {
                new StartUp(_ui, _files, CommandLine).Exit(exit.Printed);
            }
            catch (QuitGameException)
            {
            }
        }
    }

    /// <summary>The original's end with a message that ended the last <see cref="Run"/> (null: it ended otherwise); a host reports its exit code.</summary>
    public ProgramExitException? ProgramExit { get; private set; }

    /// <summary>The game last in memory (the program start's until one is played): what a load from the start screen reads over.</summary>
    private GameState? _session;

    /// <summary>The program start's game (NewGame.StartUp) with the program's battle file, made on first use.</summary>
    private GameState StartUpSession()
    {
        if (_session is null)
        {
            _session = NewGame.StartUp(World, Seed());
            _session.BattleFile = ProgramBattleFile();
        }
        return _session;
    }

    /// <summary>A game has been played in this run (the start screen shown again stands for a new run of the program).</summary>
    private bool _played;

    /// <summary>
    /// The work files terrain.fil and cfile.dat (the loot file) the program starts with: the game folder's (null: the file is not there),
    /// read on first use; after a game, the ones its battle file held (<see cref="RestartProgram"/>).
    /// </summary>
    private (byte[]? Terrain, byte[]? Loot)? _workFiles;

    /// <summary>
    /// The battle file of a program that has fought no battle and loaded no battle file: the program's start values
    /// (<see cref="Core.Battles.BattleFile.ProgramStartVariables"/>) and the work files the folder holds.
    /// </summary>
    // orig: the battle-state save R003+0x00DC (+0x0A6C..0x0C52) opens terrain.fil (DS:1164) and cfile.dat (DS:1162) in the program's folder
    //   and packs each one that is there, whatever wrote it (raw size -1 when it is not); the files outlive the program: only a battle
    //   (its level preparation and loot) and the restore R003+0x0CE8 write or remove them
    // C: lockstep session tester-newgame-reload: a new game saved before any battle; the original's DKSAVE0.BSV holds the game folder's
    //   terrain.fil (196 bytes) and cfile.dat (18 bytes) as an earlier run left them
    // DIVERGENCE: the C# keeps the work files in the program's battle file and writes none into the game folder: a program starts from
    //   what the folder holds (left by a run of the original), not from the last battle of an earlier run of this program.
    private Core.Battles.BattleFile ProgramBattleFile()
    {
        _workFiles ??= (ReadWorkFile("TERRAIN.FIL"), ReadWorkFile("CFILE.DAT"));
        return new Core.Battles.BattleFile { Terrain = (byte[]?)_workFiles.Value.Terrain?.Clone(), Loot = (byte[]?)_workFiles.Value.Loot?.Clone() };
    }

    private byte[]? ReadWorkFile(string name) => _files.Exists(name) ? _files.Read(name) : null;

    /// <summary>
    /// The start screen shown again after a game (the party perished, or the game ended): the program's globals go back to their start
    /// values, as in a new run of the program; the work files stay as the last game's battle file held them. The interface's globals too
    /// (GameUi.ProgramStart: the music and sound options on, the colours, the panel's selection ...), the party menus' (the creation menu's
    /// and the inn roster's), and the program start's configuration is read again (CONFIG.DRK, the command line). False: the configuration
    /// would stop the new run (a sound driver given with -A is gone), and the program ends.
    /// </summary>
    // orig: the start screen (thunk 09C0:18DD = R037+0x9C48) has one caller, root 03A1:0291, before setjmp(DS:A752) (03A1:0299) and the
    //   card loop; R037+0x9C48 has no other caller and is in no pointer table. The loop ends the program on 0x62 / -0x63 (03A1:046B, main
    //   returns); a party with nobody left ends it with exit(0) (03A1:0426; hours-elapse R029+0x255A and battle-driver R029+0x2278 with
    //   exit_); an Alt-L longjmps to DS:A752, after the start screen. So the original starts a new party only in a new run of the
    //   program, where every global (DS:D843 the party's terrain, DS:290E the map's vertical step, DS:E237 the enemies' turn rotation,
    //   the generation's and the battle file's globals, the cards' text variables) has its start value, and the work files are what
    //   the last run left.
    // C: the start screen coming again after a game is the divergence of Run (and GameOverException); this makes it a new run. The new run's
    //   banner, memory check and opening are not shown again (the start screen comes at once); the options file the original reads is
    //   CONFIG.DRK (R000+0x0114; no file name in the EXE or its overlays ends in .cfg: the folder's DARKLAND.CFG, the installation's choices, is never opened); tests: ProgramStartTests
    private bool RestartProgram()
    {
        _played = false;
        if (ShowStartUp)
        {
            if (new StartUp(_ui, _files, CommandLine).ReadConfiguration() is not { } again)
            {
                return false;
            }
            Started = again;
            _noAnimations = again.NoAnimations;
        }
        _ui.ProgramStart();
        CharacterCreationScreen.ProgramStart();
        if (_session?.BattleFile is { } last)
        {
            _workFiles = ((byte[]?)last.Terrain?.Clone(), (byte[]?)last.Loot?.Clone());
        }
        _session = null;
        _map = null;
        _mapMemory = new OverlandMemory();
        _battleViewCentre = default;
        _cardVariables = CardVariables.ProgramStart();
        _lastCards = null;
        _generationMemory = new Core.Battles.Generation.GenerationProcessMemory();
        _seenPositions = null;
        _cursorKeys = new Core.Battles.Tactics.CursorKeyState();
        return true;
    }

    /// <summary>Plays a game; a load while playing (Alt-L) goes on with the loaded game.</summary>
    // orig: setjmp(DS:A752) in the main function: game-load-overlay-entry longjmps back to it and the loop continues with the loaded DS:A772
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
                _played = true;
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
    // orig: R037+0x8ED4 (1C7D:0D24) matched: after the header difficulty-display(1), then
    // text-box-paint "Loading Game" until the file is read; music-setting and sound-setting come from the file
    // C: the options' menu ticks (DS:996E, DS:9982) are set too; there is no menu bar here.
    // BUG: OriginalBehaviour.FixDamagedSavedGame - R037+0x8ED4 reads a file cut short as far as it goes and plays on from what is left in its buffers (or ends in DOS); the battle file's restore (R003+0x0CE8) ends in DOS on an empty, short or corrupt .BSV.
    // DIVERGENCE: with the fix (data safety, switchable; on in the player's game) a file that ends inside its header, records or location table, or a battle file (.BSV) that is empty, short or corrupt (the restore R003+0x0CE8 reads it in every game and ends in DOS with "EOF-..." / "Corrupt file-..."), is refused before anything changes: null, and a notice (own text: the original has none).
    private LoadedGame? LoadSavedGame(string name, GameState? running, Action? beforeNotice = null)
    {
        if (Behaviour.FixDamagedSavedGame && Saves.IsDamaged(name))
        {
            Log?.Invoke($"{name}: the saved game is damaged (its file ends before its contents do); not loaded");
            // DIVERGENCE: a load given on the command line has drawn nothing yet: the notice is drawn over the load screen's picture and colours, not over black with the start-up colours
            beforeNotice?.Invoke();
            new MessageBox(_ui).Notice(DamagedSavedGame);
            return null;
        }
        // BUG: OriginalBehaviour.FixImpossibleSavedGame - R037+0x8ED4 reads the date, location-now and the location table with no check: a hand-edited month above 11 or location-now past the places reads past the original's tables, and the rewrite then throws where it indexes them.
        // DIVERGENCE: with the fix (data safety, switchable; on in the player's game) a saved game holding what no game has (SaveFile.ImpossibleValue) is refused before anything changes: null, and a notice (own text: the original has none).
        if (Behaviour.FixImpossibleSavedGame && Saves.ImpossibleValue(name, World.LocationRecords.Locations.Count) is { } impossible)
        {
            Log?.Invoke($"{name}: the saved game holds {impossible}, which no game has (edited by hand?); not loaded");
            beforeNotice?.Invoke();
            new MessageBox(_ui).Notice(ImpossibleSavedGame);
            return null;
        }
        var loaded = Saves.Load(name, World, running);
        var box = new MessageBox(_ui);
        box.Show(HotKeys.DifficultyText(loaded.State.Difficulty));
        var shown = box.Open("Loading Game");
        loaded.State.ClockSeed = Seed;
        Options.Music = loaded.Music;
        Options.SoundEffects = loaded.SoundEffects;
        box.Close(shown);
        LoadShieldPictures(loaded);
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

    /// <summary>The notice for a saved game that is cut short (own text, one box a line).</summary>
    public static readonly IReadOnlyList<string> DamagedSavedGame = ["This saved game is damaged and cannot be loaded.", "Press a key."];

    /// <summary>The notice of a saved game holding what no game has (OriginalBehaviour.FixImpossibleSavedGame).</summary>
    public static readonly IReadOnlyList<string> ImpossibleSavedGame = ["This saved game holds values no game can have", "and cannot be loaded.", "Press a key."];

    /// <summary>Alt-L while playing: "Loading is irreversable. Proceed?" (the original's own spelling), then the load screen; the game goes on with what it gives.</summary>
    // orig: game-load-entry R037+0x8B76 (1C7D:09C6) matched: card-text-window->f10 = 3; the question;
    // the debug log 1905("load", "a"), not reproduced (see Card.ShowPicture); in a battle (DS:907A == 1) the battle's pictures, sounds and level buffers are freed and its
    // flags reset; the card picture freed; `party[i][0] = 0` for the five slots; with DS:A891 == 3 the map's buffers freed; the events
    // freed; the load screen (DS:E89A = 0: no Return button); longjmp(DS:A752, -1)
    // BUG: OriginalBehaviour.FixLoadScreenEscape - Esc on the load screen goes on with the emptied slots and no events.
    // C: the battle's and the map's buffers are their screens' own here; a load from a battle leaves the battle by the jump.
    // orig: game-load-entry R037+0x8B76 (1C7D:09C6) matched: in a battle (DS:907A = 1) its buffers are freed and cards-setup-overlay-entry(0) runs
    // (R043+0x078E: srand((unsigned)time(0)), TimePassage.Reseed) before the load screen
    private void LoadInGame(GameState state, bool inBattle = false)
    {
        // orig: R037+0x8B76 card-text-window->f10 = 3 first: the question is in font 3 from Alt-L too (GameUi.CardWindowFont)
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
            foreach (var member in state.Party.Slots)
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
    // orig: world-map-entry R030+0x0000 (1462:0000) draft (bytes match): from a city (location-now < 92) the party starts at the city's exit point, from another place at its position, in the wilderness where it is; then `if (event-begun-flag(0x47)) return following-card = 0xf1;` and `if (!event-begun-flag(0x63)) { copy-protection-overlay-entry(); event-here-test(0x63, 0x3e80); }` before world-map-start
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
            // DIVERGENCE: copy-protection-overlay-entry (R041+0x0000) asks the copy-protection question (a symbol from the manual); it is not asked here (a host
            //   choice, as the later CD release). Its five random draws are made (CopyProtection.Draw: roll-below 4, 20, 7, 7 and 6, before anything else
            //   of it), so the generator goes on as the original's; the event it leaves behind, for 0x3E80 hours (about 22 months), is created as in the original.
            //   Justified (evid1o): a right answer (a click on the picture or keys 1..4 against the symbol asked, R041+0x050E; key 5, top row or keypad,
            //   passes whatever was asked: R041+0x0537 / 0x0553) changes nothing else of the game: R041+0x04DA puts the screen back and restores DS:2816 /
            //   DS:2804 from the copies R041+0x0006 made; only the background window's text colour DS:2812 stays at the 14 set at R041+0x00C8 (presentation); a wrong one
            //   ends the program (R041+0x0594: shutdown, exit(0) at R041+0x05C2). Measured: direct calls of R041+0x0000 from a map state
            //   (mapsave) with the tester's stub of it removed (the stub, orig_side's _copy_protection, makes the five draws and returns: why an earlier
            //   call returned after them), answered with each key: 5, keypad 5 and the symbol asked return 0 after five draws with the saved game
            //   byte-identical (the DS bytes changed: DS:2812, the random state, the pointer's and windows' words, the palette copies); 1, 3, 4,
            //   Enter and Esc end the program with exit(0) (local/batch/copyprot: copyprot.json).
            Core.World.CopyProtection.Draw(state.Random);
            state.Events.CreateHere(CopyProtectionEvent, CopyProtectionHours);
        }
        return new OverlandScreen(_ui, panel, _map) { CharacterSheet = sheet, Camp = camp, Memory = _mapMemory }.Travel(state);
    }

    /// <summary>Event 0x47 (a member has left; Type = member) shows card 0xF1 instead of the map.</summary>
    private const int MemberLeftEvent = 0x47;

    private const int MemberLeftCard = 0xF1;

    /// <summary>Event 0x63, the copy-protection check passed: the first visit to the map creates it for 0x3E80 hours.</summary>
    private const int CopyProtectionEvent = 0x63;

    private const int CopyProtectionHours = 0x3E80;

    private WorldMap? _map;

    /// <summary>The world map screen's program globals (DS:290E): one for the program, kept over every map visit and load.</summary>
    private OverlandMemory _mapMemory = new();

    /// <summary>Alt-S: the save dialog; inside a battle the battle's own Alt-S (BattleScreens.Save) saves it with the battle.</summary>
    // orig: game-store-overlay-entry = game-store-entry R037+0x2A68 (hotkey-table key 0x1F00)
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

    private TablePersonalNames? _personalNames;

    /// <summary>The battle view's centre (DS:C866 / DS:C886), a program global: a game loaded keeps it.</summary>
    private (int Column, int Row) _battleViewCentre;

    /// <summary>The cards' text variables ($NamedOneName, $ChosenOneName, $Money1, $Text1, the conditions ...): one set for the whole run.</summary>
    // orig: the text slots (named[0..4] 2E38:1A96, chosen DS:EE8C, money DS:EE9C / DS:ED46, numbers DS:ED4C, texts DS:ED6D, item DS:ED5C, places
    //   DS:E89B / DS:ED54, support DS:ED5E, city location DS:ED52, conditions DS:9C30) are globals, zeros in the EXE's image: the saved-game
    //   reader and writer (R037 1C7D) leave them, so after a load (Alt-L or from the start screen) a card reads what the cards before left
    //   (b35: card 0xE6 called in the original after a new game and after a load of another game prints the creation's last made-up name;
    //   every slot marked before an Alt-L's reader was still marked at the next card call); a card context is made anew for every game
    //   played, the variables are not
    private CardVariables _cardVariables = CardVariables.ProgramStart();

    /// <summary>The card context of the game played last (its engine words carry over, see CardContext.TakeEngineGlobals).</summary>
    private CardContext? _lastCards;

    // C: the battle globals of GenerationProcessMemory (DS:E051, floor-picture, DS:C80F / DS:C831, DS:1182 / DS:118E, the walk spot DS:C652 /
    //   DS:C67D, DS:9BE5, the effects' modifiers DS:A532) are the program's: no load writes them (game-load-entry R037+0x8B76 frees the
    //   battle's buffers and resets other bytes; the battle-reload R003+0x0CE8 under the reader restores the party's place tables, which each
    //   battle's start takes from the battle file (BattleScreen), and the alarm trigger DS:C925.. (set in Play)). One object for the whole run,
    //   given to the battles of every game played (b35: the sizes marked before an Alt-L's reader were still marked at the next battle;
    //   DS:9BE5 kept by a load, vis10 session uncert-cme-menus).
    private Core.Battles.Generation.GenerationProcessMemory _generationMemory = new();

    /// <summary>The battle screens of the game being played (null before the first game), each with the program's battle globals.</summary>
    public BattleScreens? Battles => _battles;

    /// <summary>The combatants' seen positions (20A5:79A9), a program global: a game loaded keeps them.</summary>
    private Core.Battles.MapPoint[][]? _seenPositions;

    /// <summary>The cursor keys' state (DS:D728, DS:D73B, DS:D7FE, DS:D947, DS:D945), program globals: a game loaded keeps them (a battle
    /// file restored replaces them).</summary>
    private Core.Battles.Tactics.CursorKeyState _cursorKeys = new();

    /// <summary>
    /// A card returned −2..−6: the battle it ordered is fought, the dead leave the party and the
    /// rest stand up again; if nobody is left (or all fell, result 5) the game ends with the
    /// death animation. Returns the card the battle leads to.
    /// </summary>
    // orig: main loop root 03A1:000A (matched per the byte match) - cards-teardown-overlay-entry,
    //   fight-flags |= 4 (−3) / 8 (−5) / 0x80 (−6) / 0xC (−4), |= 1, battle-entry(battle-kind, battle-enemy-group, encounter-seed,
    //   −1, A76C, 0, −1, A76E, 0, −1, A770, 0), status 3 → 0 with party-member-erase-overlay-entry, others but 0 and 5 → 1,
    //   none left or battle-outcome == 5 → music 0x12, "death", exit; else cards-setup-overlay-entry and the result is the next screen.
    // DIVERGENCE: the game returns to the start screen instead of exiting to DOS; the work files of cards-teardown and cards-setup are
    //   kept in memory, their effects made (the events, the reseed, the city record: see CardContext.RunBattle).
    // orig: cards-teardown R043+0x054E writes the events to events.tmp and empties the table (EventLog.Shutdown); cards-setup
    //   R043+0x078E (1462:078E) matched begins with srand((unsigned)time(0)): TimePassage.Reseed, and its world-read reads the events
    //   back (EventLog.Reload). A load in the battle leaves it by the jump (LoadGameRequestedException): no cards-setup.
    // C: with DS:9081 bit 0 (GameState.ResumeBattle: a game saved in a battle) the card was skipped and the code is the DS:A764 the loop
    // holds (not a battle code after a load); R002+0x0000 takes the battle up from the battle file and clears the bit at its end.
    private int FightTheBattle(GameState state, CardContext context, BattleScreens battles, int code)
    {
        // orig: root 03A1:000A calls cards-teardown-overlay-entry (R043+0x054E) before the battle; it ends with sound-start(1) (root 047C:078A)
        state.Events.Shutdown();
        _ui.Audio.PlayMusic(Darklands.Audio.GameSounds.Stop);
        var orders = state.BattleOrders;
        orders.Flags |= code switch { -3 => 4, -5 => 8, -6 => 0x80, -4 => 0xC, _ => 0 };
        orders.Flags |= 1;
        try
        {
            orders.Result = battles.Battle(new BattleRequest(orders.BattlefieldType, orders.Seed,
                [(-1, orders.EnemyTypes[0], 0), (-1, orders.EnemyTypes[1], 0), (-1, orders.EnemyTypes[2], 0)],
                orders.Mode, Persistent: true, CombatFlags: orders.Flags, Resume: state.ResumeBattle));
        }
        finally
        {
            // orig: R002+0x0000 masks the combat flags with 0xFE10 at its end (R037+0x8B76 does the same when a load leaves the battle)
            state.ResumeBattle = false;
            orders.Flags &= 0xFE10;
        }
        var members = state.Party.Slots;
        // orig: root 03A1:000A (matched per the byte match): party-member-erase-overlay-entry for each slot of status 3, before cards-setup
        // C: the dead are deleted first (slot by slot, highest first here because DeleteMember trims empty slots at the end).
        // BUG: party-member-erase-overlay-entry runs while the events are in events.tmp, so a member who died in the battle keeps the term and
        //   "has left" events (kinds 0x43/0x47) it would cancel: they come back with the table.
        for (var i = members.Count - 1; i >= 0; i--)
        {
            if (members[i].Status == Core.Characters.CharacterStatus.Dead)
            {
                context.DeleteMember(i);
            }
        }
        foreach (var m in state.Party.Slots)
        {
            if (m.Status is not Core.Characters.CharacterStatus.Empty and not Core.Characters.CharacterStatus.Away)
            {
                m.Status = Core.Characters.CharacterStatus.Able;
            }
        }
        if (state.Party.Slots.All(m => m.Status == Core.Characters.CharacterStatus.Empty) || orders.Result == 5)
        {
            Cinematics.Death();
            return CardDispatcher.EndOfCards;
        }
        state.Party.EnsureAbleLeader();
        // C: cards-setup (thunk of R043+0x078E): the clock reseed, then world-read's city record and events (CardContext.RunBattle has the details).
        Core.Rules.TimePassage.Reseed(state);
        Core.World.CityFile.ReloadAfterBattle(state);
        state.Events.Reload();
        // orig: root 03A1:0438..0441: after cards-setup (thunk 09C0:1919(0), 03A1:0430) thunk 09C0:190F(0) = pass-hours(0), before the
        // pass's own end calls it again (03A1:0463, CardDispatcher.FinishPass): two more clock reseeds (lockstep session dungeon-mine-doormsg:
        // the second second of the original's reload took the next clock seed)
        Core.Rules.TimePassage.Advance(state, 0);
        return battles.LastResultCard;
    }

    /// <summary>
    /// What a load's battle-reload sets among the battle globals the program keeps: the alarm trigger and the cursor keys' bytes, from the
    /// loaded game's battle file (the other globals of <see cref="Core.Battles.Generation.GenerationProcessMemory"/> stay as they were).
    /// </summary>
    // orig: battle-reload R003+0x0CE8 under the saved-game reader (every load: a save made outside a battle has its .BSV too): the alarm
    //   trigger DS:C925 / DS:C978 / DS:C989 / DS:CA1B (R003+0x1241..0x1270) and the cursor keys DS:D728 / DS:D73B / DS:D7FE / DS:D947 /
    //   DS:D945 (R003+0x0EDE..0x0F06), as a battle taken up from the file reads them (TacticalBattle.Resume); the party's place tables and
    //   the chests' offsets it also sets are taken from the battle file at each battle's start (BattleScreen)
    public static void TakeBattleReload(Core.Battles.BattleFile held, Core.Battles.Generation.GenerationProcessMemory memory, Core.Battles.Tactics.CursorKeyState keys)
    {
        var v = held.Variables;
        if (v.Length < Core.Battles.BattleVariables.AlarmTrigger + 6)
        {
            return;
        }
        memory.AlarmTrigger = (BitConverter.ToInt16(v, Core.Battles.BattleVariables.AlarmTrigger),
            BitConverter.ToInt16(v, Core.Battles.BattleVariables.AlarmTrigger + 2), v[Core.Battles.BattleVariables.AlarmTrigger + 4],
            v[Core.Battles.BattleVariables.AlarmTrigger + 5]);
        keys.Read(v.AsSpan(Core.Battles.BattleVariables.CursorKeys, 6));
    }

    /// <summary>Plays one game: wires its card context, screens, hot keys and menu bar together, then runs the card loop until the game ends.</summary>
    // orig: the do/while of root 03A1:000A: a screen function returns the next screen number; 0x0C is the overland map (world-map-entry), -2..-6 start a battle, 0x62 ends the game, -0x63 quits
    private void Play(GameState state)
    {
        CardContext? context = null;
        var panel = new PartyPanel(_ui);
        var screen = new CardScreen(_ui, () => context!, panel);
        context = new CardContext(state, World, screen) { Variables = _cardVariables };
        context.TakeEngineGlobals(_lastCards);
        _lastCards = context;
        context.PersonalNames = _personalNames ??= new TablePersonalNames(Content.Formats.Characters.PersonalNamesDecoder.Decode(_ui.Assets.Files.Read("DARKLAND.EXE")));
        SaintPrayerScreen.Attach(context, _ui, panel);
        var battles = new BattleScreens(_ui, () => state, (items, money) => context.Screens.Loot(items, money))
        {
            GenerationMemory = _generationMemory,
            CharacterSheet = member => context!.Screens.CharacterSheet(member),
            PartyInfo = () => context!.Screens.PartyInfo(),
        };
        battles.Save = battle => SaveGame(state, battles, battle);
        _battles = battles;
        // C: the view centre DS:C866 / DS:C886 is a global the load leaves as it was: a battle taken up from the file loaded in a battle is
        // drawn around the centre the battle left had (R014+0x02FF), and so its members' screen positions (+0x00 / +0x02) as its next save
        // writes them (lockstep session savemid-mine)
        battles.ViewCentre = _battleViewCentre;
        if (state.BattleFile is { } held)
        {
            TakeBattleReload(held, _generationMemory, _cursorKeys);
        }
        battles.SeenPositions = _seenPositions;
        battles.CursorKeys = _cursorKeys;
        battles.Load = () =>
        {
            _battleViewCentre = battles.CurrentViewCentre;
            _seenPositions = battles.SeenPositions;
            LoadInGame(state, inBattle: true);
        };
        // orig: battle-driver R029+0x2278: a card's own battle (battle-entry with mode 0).
        context.Battle = (battlefieldType, seed, p) => battles.Battle(new BattleRequest(battlefieldType, seed,
            [(p[0], p[1], p[2]), (p[3], p[4], p[5]), (p[6], p[7], p[8])]));
        context.PlayFilm = steps => Cinematics.Film(steps);
        // orig: battle-driver R029+0x2278: nobody left after the battle: music-start(0x12), the death film, exit_(0)
        context.AllDead = () =>
        {
            Cinematics.Death();
            throw new GameOverException();
        };
        // orig: music-start R043+0x09AE: the piece if music is on, else 1 (stop) — GameUi.PlayMusic applies the option.
        context.Music = tune => _ui.PlayMusic(tune);
        // orig: sound-start root 047C:078A: straight to the towns-and-map driver, whatever the music option says.
        context.SoundPlayer = number => _ui.Audio.PlayMusic(number);
        // orig: music-start(5) R043+0x09AE: the AX of driver command 5 (or of the stop with music off), which $Shell00 keeps in DS:9C3D
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
        // orig: saint-prayer (byte-matched): a saint row on the character sheet prays for that member.
        screens.ShowSaint = (member, saint) =>
            new SaintPrayerScreen(_ui, panel) { InBattle = () => battles.Fighting }.Pray(state, member, saint, Core.Rules.PrayerMode.ForMember, Core.Rules.SaintEffects.Instance);
        // orig: hours-elapse R029+0x255A: with nobody left, music-start(0x12), cards-teardown-overlay-entry, the death film and exit_(n)
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
                if (card is CardDispatcher.EndOfCards or CardDispatcher.QuitGame)
                {
                    return;
                }
                if (card == CardDispatcher.OverlandMap)
                {
                    // orig: C / F8 on the map: thunk 09C0:1FF9 = R036+0x0000 (1462:0000) with the party's watch value (R030+0x25BE): the
                    //   wilderness stay with the day's poaching risk (CampScreen.CampInTheWild)
                    // orig: the map is card 0x0C of the table: root 03A1:000A ends its pass as any card's (03A1:0442..0463): the previous-screen
                    //   word becomes 0x0C only after the map returns (a game saved on the map keeps the card before), then pass-hours(0)
                    _ui.CardShown(card);
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
                _ui.CardShown(card);
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

    /// <summary>The party's shield pictures after a load: one the original cannot open ends the program.</summary>
    // BUG: OriginalBehaviour.FixLoadTwoSlotsOneRecord - after the read the load screen loads the party's shield pictures (R037+0x86DA..0x8754,
    //   every slot whose id is not -1): a slot the reader left without its record has the letter 0, "pics\shield.pic" does not open and the
    //   program ends (exit 99; lockstep session carry-dup-resave, R037+0x84BA+0x29F). Fixed (DataSafe): the game goes on with the slot empty.
    private void LoadShieldPictures(LoadedGame loaded)
    {
        if (!Behaviour.FixLoadTwoSlotsOneRecord && loaded.SlotsWithoutRecord.Count > 0)
        {
            throw ProgramExitException.FileNotFound("pics\\shield.pic");
        }
    }
}
