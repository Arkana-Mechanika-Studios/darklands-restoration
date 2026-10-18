using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Simulation;
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
    private readonly CardDispatcher _cards = new();
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

    /// <summary>True while the start screen shows (a host may draw the program's version over the window then).</summary>
    public bool OnStartScreen { get; private set; }

    /// <summary>Seed for new games and for reseeding after time passes; the game uses the clock.</summary>
    public Func<uint> Seed { get; set; } = () => (uint)Environment.TickCount;

    /// <summary>
    /// The folder of the saved games (DKSAVEn.SAV and .BSV files). Default: the game folder's SAVES, as the
    /// original uses (shared with it); the settings file's savesFolder, DARKLANDS_SAVES or --saves override it.
    /// </summary>
    // orig: the save/load code opens saves\ relative to the game's folder (R037); the folder name is matched case-insensitively.
    public string SaveDirectory { get; set; } = "";

    /// <summary>The game folder's SAVES folder (an existing one in any letter case, else SAVES).</summary>
    public static string DefaultSaveDirectory(string gameFolder) =>
        Directory.Exists(gameFolder)
            ? Directory.EnumerateDirectories(gameFolder).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "SAVES", StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(gameFolder, "SAVES")
            : Path.Combine(gameFolder, "SAVES");

    private SaveFolder Saves => new(SaveDirectory);

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
                        var quickstart = NewGame.Quickstart(World, Seed(), Seed);
                        quickstart.CurrentCard = QuickstartCard ?? quickstart.CurrentCard;
                        PlayFrom(quickstart);
                        break;
                    // orig: R037+0x9DB2 case 0x1454/0x1474: DS:E89A = 1; if (R037+0x81B2()) return 0x11b (play); else the start screen again
                    // C: the game in memory (at first the program start's, NewGame.StartUp) is what the load reads over: city_rec and the session globals.
                    case StartChoice.LoadGame when new LoadGameScreen(_ui, Saves, LoadScreenTexts).Run(fromStartScreen: true, name => LoadSavedGame(name, _session ??= NewGame.StartUp(World, Seed()))) is { } loaded:
                        PlayFrom(loaded.State);
                        break;
                    case StartChoice.CreateCharacters when new CharacterCreationScreen(_ui, World, Seed).Run() is { } created:
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
        return loaded;
    }

    /// <summary>Alt-L while playing: "Loading is irreversable. Proceed?", then the load screen; the game goes on with what it gives.</summary>
    // orig: load_game R037+0x8B76 (1C7D:09C6) matched: card_window->f10 = 3; the question;
    // thunk 09C0:1905("load", "a") (the debug log); in a battle (DS:907A == 1) the battle's pictures, sounds and level buffers are freed and its
    // flags reset; the card picture freed; `party[i][0] = 0` for the five slots; with DS:A891 == 3 the map's buffers freed; the events
    // freed; the load screen (DS:E89A = 0: no Return button); longjmp(DS:A752, -1)
    // BUG: OriginalBehaviour.FixLoadScreenEscape - Esc on the load screen goes on with the emptied slots and no events.
    // C: the battle's and the map's buffers are their screens' own here; a load from a battle leaves the battle by the jump.
    private void LoadInGame(GameState state)
    {
        if (!new MessageBox(_ui).Ask("Loading is irreversable. Proceed?"))
        {
            return;
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
            // DIVERGENCE: copyprot_run_vec asks the copy-protection question (a code from the manual); it is not asked here. The event it leaves behind, for 0x3E80 hours (about 22 months), is created as in the original.
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

    /// <summary>
    /// A card returned −2..−6: the battle it ordered is fought, the dead leave the party and the
    /// rest stand up again; if nobody is left (or all fell, result 5) the game ends with the
    /// death animation. Returns the card the battle leads to.
    /// </summary>
    // orig: main loop root 03A1:000A (matched per the byte match) - cards_shutdown_vec,
    //   combat_flags |= 4 (−3) / 8 (−5) / 0x80 (−6) / 0xC (−4), |= 1, battle_run(battle_mode, battle_enemy, battle_seed,
    //   −1, A76C, 0, −1, A76E, 0, −1, A770, 0), status 3 → 0 with party_delete_vec, others but 0 and 5 → 1,
    //   none left or battle_result == 5 → music 0x12, "death", exit; else cards_startup_vec and the result is the next screen.
    // DIVERGENCE: the game returns to the start screen instead of exiting to DOS; cards_shutdown and cards_startup's reloads are not needed.
    // orig: cards_startup R043+0x078E (1462:078E) matched begins with srand((unsigned)time(0)): TimePassage.Reseed.
    // C: with DS:9081 bit 0 (GameState.ResumeBattle: a game saved in a battle) the card was skipped and the code is the DS:A764 the loop
    // holds (not a battle code after a load); R002+0x0000 takes the battle up from the battle file and clears the bit at its end.
    private int FightTheBattle(GameState state, CardContext context, BattleScreens battles, int code)
    {
        // orig: root 03A1:000A calls cards_shutdown_vec (R043+0x054E) before the battle; it ends with sound_play(1) (root 047C:078A)
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
        // C: the dead are deleted first (slot by slot, highest first here because DeleteMember trims empty slots at the end).
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
        Core.Rules.TimePassage.Reseed(state);
        return battles.LastResultCard;
    }

    private void Play(GameState state)
    {
        CardContext? context = null;
        var panel = new PartyPanel(_ui.Assets);
        var screen = new CardScreen(_ui, () => context!, panel);
        context = new CardContext(state, World, screen);
        context.PersonalNames = _personalNames ??= new TablePersonalNames(Content.Formats.Characters.PersonalNamesDecoder.Decode(_ui.Assets.Files.Read("DARKLAND.EXE")));
        SaintPrayerScreen.Attach(context, _ui, panel);
        var battles = new BattleScreens(_ui, () => state, (items, money) => context.Screens.Loot(items, money))
        {
            CharacterSheet = member => context!.Screens.CharacterSheet(member),
            PartyInfo = () => context!.Screens.PartyInfo(),
            Load = () => LoadInGame(state),
        };
        battles.Save = battle => SaveGame(state, battles, battle);
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
        var screens = new GameScreens2(_ui, panel, state, World, new GameScreens(_ui, panel, state, new ScreensUnavailable(screen)));
        context.Screens = screens;
        var hotKeys = new HotKeys(_ui, state, Options)
        {
            Save = () => SaveGame(state, battles),
            Load = () => LoadInGame(state),
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
                    state.PreviousCard = card;
                    // orig: C / F8 on the map: thunk 09C0:1FF9 = R036+0x0000 (1462:0000) with the party's watch value (R030+0x25BE): the
                    //   wilderness stay with the day's poaching risk (CampScreen.CampInTheWild)
                    state.CurrentCard = Travel(state, panel, context.Screens.CharacterSheet, screens.CampInTheWild);
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
