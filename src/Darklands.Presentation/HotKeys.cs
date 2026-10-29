using Darklands.Audio;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>The game goes on from the main loop with another state: a saved game was loaded (Alt-L).</summary>
// orig: load_game R037+0x8B76 ends in longjmp(DS:A752, -1) back to the setjmp of the main function root 03A1:000A, which
// starts the card engine again (thunk 09C0:18D3) and runs the card DS:A772 from the start
// C: the jump is taken whether the load screen loaded a game or was left with Esc.
public sealed class LoadGameRequestedException(Darklands.Core.GameState state) : Exception("A saved game was loaded.")
{
    public Darklands.Core.GameState State { get; } = state;
}

/// <summary>Player options toggled by the hot keys.</summary>
// orig: opt_sound (DGROUP 01BF) and opt_music (01BE), both 1 at the start; saved in the saved game (R037 seg 1C7D: DS:01BF sound, DS:01BE music)
public sealed class GameOptions
{
    public bool SoundEffects { get; set; } = true;

    public bool Music { get; set; } = true;

    /// <summary>
    /// Draws the screens clean where the original's drawing shows a leftover (the Fixed and Classic + comfort presets): a new card's
    /// picture at once rather than with its first text, no pressed-button picture after the load screen's "no". Off (Classic): as the
    /// original draws them. A drawing choice only: no game value depends on it.
    /// </summary>
    public bool CleanDrawing { get; set; }
}

/// <summary>
/// The keys that work everywhere while playing, as the original's hot keys: Alt-Q quit,
/// Alt-P pause, Alt-S save, Alt-L load, Alt-D difficulty, Alt-F sound effects, Alt-M music,
/// Alt-C saintly changes, F1..F5 character sheets, F6 party information, Ctrl-F1..F5 the leader.
/// </summary>
// orig: hot_keys root 0000:0CAA draft (matches the bytes; the file is "hand port" with 24 of 39 functions matching) for the Alt keys and F6, called from menu_bar_run (root 0000:0000) whenever a key is waiting; card_global_keys R043+0x33C6 (150B:2936) for F1..F6 and Ctrl-F1..F5 in the card engine; the F1..F5 / Ctrl-F cases of R030+0x0C44 on the map; tests: none (needs a host)
// C: hot_keys looks at the key code with key_pressed() and consumes it (get_key) only for the codes it knows; other keys stay for the screen. Here a key it does not handle is not consumed either (Handle returns false).
// orig: F1..F5 / Ctrl-F1..F5 only where the original takes them: the card engine's menus and wait_continue (card_global_keys R043+0x33C6)
//   and the map's loop (R030+0x0C44), which poll with GameUi.SheetKeys; the card's message wait (CardScreen.Tell), the full screens and the
//   other loops leave them to the screen (hot_keys root 0000:0CAA does not know them); tests: HotKeyFlagsTests
// orig: the screen flags of Alt-O, Alt-S, Alt-L and F6: DS:9893, DS:9920, DS:9934 and DS:987F. DS:9893 and DS:987F are 1 outside a battle
//   (R000+0x0DF5 / 0x0DF8 at the start, R002+0x0760 at a battle's end, R037+0x8D41 after a load; no other writer); inside one the battle's
//   own (BattleMenus: DS:987F = 2 from R002+0x0258, DS:9893 = 2 around the combat sheet R022+0x009D..0x02B7 and the loot screen
//   R014+0xE42D..0xE72B) are read by BattleScreen. DS:9920 / DS:9934: the inn's roster, the stay (camp), the character sheet and barter set
//   them (SaveGameItem, LoadGameItem) as R038, R036, R031 and R032 do.
// C: F10 (menu mode DS:9C26) and the right button open the menu bar: GameUi.Poll hands them to MenuBar, whose items call the actions below.
public sealed class HotKeys(GameUi ui, GameState state, GameOptions options)
{
    private readonly MessageBox _box = new(ui);
    private bool _busy;

    /// <summary>Saves the game (Alt-S): the save dialog.</summary>
    // orig: save_game_vec = save_game R037+0x2A68 (key 0x1F00, only if DS:9920 == 1)
    public Action? Save { get; init; }

    /// <summary>Loads a saved game (Alt-L): the question, then the load screen.</summary>
    // orig: load_game_vec = load_game R037+0x8B76 (key 0x2600, only if DS:9934 == 1)
    public Action? Load { get; init; }

    /// <summary>Opens a member's character sheet (F1..F5).</summary>
    // orig: member_sheet_vec(key) = R031+0x0124 (1462:0124) then party_panel_frames/rows and R043+0x1E82
    public Action<int>? CharacterSheet { get; init; }

    /// <summary>Opens the party information screen (F6).</summary>
    // orig: party_info_screen_vec (key 0x4000, only if DS:987F == 1)
    public Action? PartyInfo { get; init; }

    /// <summary>The screen for choosing the party order (Alt-O): MarchingOrderScreen.</summary>
    // orig: root 0000:0CAA key 0x1800: the key is taken, then thunk 09C0:183D (R022+0x051C) only if the menu item's flag DS:9893 is 1
    // C: DS:9893 is 1 outside a battle: only the battle (R014) and its character sheet (R022+0x0000) set it to 2, and put it back.
    public Action? ChoosePartyOrder { get; init; }

    /// <summary>The flag of the Save Game item and of Alt-S (1: allowed, 2: greyed and refused).</summary>
    // orig: DS:9920 (the item's flag byte), set to 2 by the inn's roster R038+0x0000 and back to 1 by R038+0x01FE
    public int SaveGameItem { get; set; } = 1;

    /// <summary>The flag of the Load Saved Game item and of Alt-L (1: allowed, 2: greyed and refused).</summary>
    // orig: DS:9934, set with DS:9920
    public int LoadGameItem { get; set; } = 1;

    /// <summary>
    /// The full screens a card opened and that are still up (the sheet, camp, barter, item store, loot ...): while one is up,
    /// F1..F5 and Ctrl-F1..F5 are not taken here but left to the screen's own loop.
    /// </summary>
    // C: the original takes F1..F5 / Ctrl-F only in the card engine (card_global_keys R043+0x33C6) and on the map (R030+0x0C44); a screen's
    // loop gets them through menu_bar_run untouched (hot_keys root 0000:0CAA does not know them): the sheet shows the member in place
    // (R031+0x0124), the camp opens the sheet or changes the leader itself (R036+0x08EC), barter changes the leader (R032+0x0F60), others ignore them.
    public int ScreensUp { get; set; }

    /// <summary>Handles a key; true when it was a hot key.</summary>
    public bool Handle(int key)
    {
        if (_busy)
        {
            return false;
        }
        var action = ActionFor(key);
        if (action is null)
        {
            return false;
        }
        _busy = true;
        // the screen a hot key opens has loops of its own: F1..F5 are theirs there
        var sheetKeys = ui.SheetKeys;
        ui.SheetKeys = false;
        try
        {
            action();
        }
        finally
        {
            _busy = false;
            ui.SheetKeys = sheetKeys;
        }
        return true;
    }

    private Action? ActionFor(int key) => key switch
    {
        Keys.AltQ => QuitToDos,
        Keys.AltO when ChoosePartyOrder is not null => ChoosePartyOrder,
        Keys.AltP => Pause,
        // C: root 0000:0CAA takes Alt-S / Alt-L out of the buffer before it tests the flag: a refused key is not left for the screen.
        Keys.AltS or Keys.AltL when (key == Keys.AltS ? SaveGameItem : LoadGameItem) != 1 => Refused,
        Keys.AltS => Save,
        Keys.AltL when Load is not null => LoadGame,
        Keys.AltD => NextDifficulty,
        Keys.AltF => ToggleSoundEffects,
        Keys.AltM => ToggleMusic,
        Keys.AltC => ToggleSaintlyChanges,
        Keys.F6 when PartyInfo is not null => ShowPartyInfo,
        >= Keys.F1 and <= Keys.F5 when ui.SheetKeys && ScreensUp == 0 && CharacterSheet is not null && MemberOf(key) is { } member => () =>
        {
            CharacterSheet(member);
            ui.Redraw?.Invoke();
        },
        >= Keys.CtrlF1 and <= Keys.CtrlF5 when ui.SheetKeys && ScreensUp == 0 && AbleMemberOf(key) is { } leader => () => MakeLeader(leader),
        _ => null,
    };

    private static void Refused()
    {
    }

    /// <summary>Alt-L (and the menu bar's Load Saved Game): takes up a saved game through the load screen.</summary>
    // orig: load_game_vec (key 0x2600, only if DS:9934 == 1)
    public void LoadGame() => Load?.Invoke();

    /// <summary>F6: the party information, then the screen below is drawn again.</summary>
    // orig: party_info_screen_vec (key 0x4000, only if DS:987F == 1), whose close (R034+0x119C) sets party_icons_dirty (DS:9C46): the screens
    //   with that test in their loop draw themselves again after the menu bar (GameUi.KeyAfterTheMouse); the card and the map redraw after it
    // C: the screens that keep a redraw in GameUi.Redraw (cards, the map, the inn's roster) are redrawn here and the flag taken back.
    public void ShowPartyInfo()
    {
        if (PartyInfo is not null)
        {
            PartyInfo();
            ui.PartyIconsDirty = true;
            if (ui.Redraw is { } redraw)
            {
                ui.PartyIconsDirty = false;
                redraw();
            }
        }
    }

    /// <summary>Alt-F: sound effects on or off.</summary>
    public void ToggleSoundEffects() => options.SoundEffects = Toggle("Sound effects ", options.SoundEffects);

    /// <summary>Alt-C: saintly changes shown or not.</summary>
    public void ToggleSaintlyChanges() => state.ShowSaintlyChanges = Toggle("Show saintly changes ", state.ShowSaintlyChanges);

    /// <summary>F1..F5: the member in that slot, if there is one.</summary>
    // orig: card_global_keys: `key = (key >> 8) - 0x3b; if (key < party_size && party[key].status) { member_sheet_vec(key); ... }`
    // C: on the map (0c44) the sheet opens without the check; an empty slot's sheet is the screen's business there.
    private int? MemberOf(int key)
    {
        var member = (key - Keys.F1) >> 8;
        return member < state.Party.Members.Count && state.Party.Members[member].IsPresent ? member : null;
    }

    // orig: root 0000:1080 draft - sound_play(2), the box, sound_play(3)
    //   (the driver's pause/resume, regardless of the music option).
    public void Pause()
    {
        ui.Audio.PlayMusic(GameSounds.Pause);
        _box.Pause();
        ui.Audio.PlayMusic(GameSounds.Resume);
    }

    /// <summary>Ctrl-F1..F5: the member in that slot if he or she is able to act.</summary>
    // orig: `key = (key >> 8) - 0x5e; if (key < party_size && party[key].status == 1) { leader = key; party_panel_rows(0x100); }` (card_global_keys and 0c44; the comment of the C calls these Shift-F keys, but the codes 0x5E00..0x6200 are Ctrl-F1..F5)
    private int? AbleMemberOf(int key)
    {
        var member = (key - Keys.CtrlF1) >> 8;
        return member < state.Party.Members.Count && state.Party.Members[member].Status == CharacterStatus.Able ? member : null;
    }

    private void MakeLeader(int member)
    {
        state.Party.LeaderIndex = member;
        ui.Redraw?.Invoke();
    }

    // orig: quit_to_dos root 0000:13A0: `if (ask_yes_no("Quit to DOS?") == 1) { shutdown; exit(0) }`
    // C: wait_continue (card texts) treats the answer "no" like Enter: it calls quit_to_dos for key 0x1000 and then returns as if the text had been dismissed; here the text stays up after "no".
    public void QuitToDos()
    {
        if (_box.Ask("Quit to DOS?"))
        {
            throw new QuitGameException();
        }
    }

    /// <summary>Basic → Standard → Expert → Basic.</summary>
    // orig: hot_keys case 0x2000: game_difficulty 0 -> set_difficulty_standard (root 0000:1418), 1 -> expert (1428), 2 -> basic (1408), each ending in show_difficulty(1) (root 0000:1438): "Difficulty is now set to " + Basic / Standard / Expert in a message box for 0x3c ticks
    // C: show_difficulty also ticks the menu bar's difficulty entries (DS:99EC, DS:9A02, DS:9A18): MenuBar reads the difficulty. game_difficulty is switched on `& 0x7f`, a value above 2 gives no text.
    public void NextDifficulty() => SetDifficulty(state.Difficulty switch
    {
        Difficulty.Basic => Difficulty.Standard,
        Difficulty.Standard => Difficulty.Expert,
        _ => Difficulty.Basic,
    });

    /// <summary>Sets the difficulty and says so ("Difficulty is now set to ...").</summary>
    // orig: set_difficulty_basic root 0000:1408, set_difficulty_standard root 0000:1418, set_difficulty_expert root 0000:1428 (root 0000:1408..1428) draft (bytes match): game_difficulty = 0 / 1 / 2, show_difficulty(1) (the menu bar's ticks follow the difficulty, see MenuBar)
    public void SetDifficulty(Difficulty difficulty)
    {
        state.Difficulty = difficulty;
        _box.Show(DifficultyText(state.Difficulty));
    }

    /// <summary>The text show_difficulty(1) shows.</summary>
    // orig: show_difficulty root 0000:1438: "Difficulty is now set to " + Basic / Standard / Expert by `game_difficulty & 0x7f`
    // C: a value above 2 adds no name.
    public static string DifficultyText(Difficulty difficulty) =>
        "Difficulty is now set to " + (difficulty is >= Difficulty.Basic and <= Difficulty.Expert ? difficulty.ToString() : "");

    // orig: toggle_music root 0000:11F2: opt_music ^= 1, text DS:01B8 "Music " + ON/OFF via show_message(buf, 0x3c); when turned off also sound_play(1) (stop the music)
    // C: the stop is played before the message is shown.
    public void ToggleMusic()
    {
        options.Music = !options.Music;
        // orig: toggle_music root 0000:11F2 draft - turning music off plays 1 (stop).
        if (!options.Music)
        {
            ui.Audio.PlayMusic(GameSounds.Stop);
        }
        Toggle("Music ", !options.Music);
    }

    // orig: toggle_sound root 0000:129A ("Sound effects "), toggle_saint_msgs root 0000:1154 ("Show saintly changes ") and toggle_music: the text is the option name plus the NEW state ("ON" when it is now on), the menu entry's tick DS:9982 / DS:995A / DS:996E is set to 1 or 2
    private bool Toggle(string what, bool on)
    {
        _box.Show(what + (on ? "OFF" : "ON"));
        return !on;
    }
}
