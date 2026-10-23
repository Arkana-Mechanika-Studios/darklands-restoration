using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Rules;

namespace Darklands.Cards;

/// <summary>Card helpers: clamping, the music now playing, party changes and encounter values.</summary>
public sealed partial class CardContext
{
    // ---- numbers --------------------------------------------------------------------

    // orig: clamp_range R029+0x4F76 (1367:0066) matched; tests: CardSmokeTests
    /// <summary>
    /// <c>clamp_range(lo, hi, v)</c> exactly: the upper bound is tested first, so when
    /// <paramref name="low"/> exceeds <paramref name="high"/> a large value gives
    /// <paramref name="high"/> (Math.Clamp would throw). Use it where the bounds can cross.
    /// </summary>
    public static int ClampRange(int low, int high, int value) => value > high ? high : value < low ? low : value;

    // ---- music -------------------------------------------------------------------------
    // PlayMusic (R043 09ae), PlaySound (sound_play), RandomName, ShopNames and the shops are shared:
    // see CardContext.Services.cs and CardContext.Memory.cs.

    // orig: R043+0x09AE (1462:09AE) matched, music_play(5) (driver command 5: the number last played, root 047C:078A); tests: CardSmokeTests
    // C: music_play is typed void, but its callers ($Shell00) keep the AX the driver left (GameSounds.CurrentPiece); with music off
    //   it plays the stop instead and gives that call's AX.
    /// <summary>music_play(5): which piece of music is playing, supplied by the presentation (GameUi.MusicPlaying).</summary>
    public Func<int>? MusicPlaying { get; set; }

    /// <summary>The piece now playing, or −1 when the presentation cannot tell.</summary>
    public int CurrentMusic => MusicPlaying?.Invoke() ?? -1;

    /// <summary>The piece kept to be played back, shared with the presentation's screens (GameUi.SavedMusic).</summary>
    // orig: DS:9C3D
    public System.Runtime.CompilerServices.StrongBox<int> SavedMusic { get; set; } = new(0);

    // ---- the party's composition -----------------------------------------------------

    // orig: party_remove R037+0x3B58 (1462:3B58) draft(fn matched): PartyRules.RemoveFromParty; tests: CardSmokeTests
    /// <summary>
    /// A member leaves the party but stays on the roster (party_remove, <see cref="PartyRules.RemoveFromParty"/>):
    /// the leader passes to the first other able member; the slot becomes empty and the character is
    /// left at the current place.
    /// </summary>
    public void RemoveMember(int slot) => PartyRules.RemoveFromParty(State, slot);

    // DeleteMember (party_delete) is shared: see CardContext.Memory.cs.

    // orig: DS:A67C; tests: CardSmokeTests
    // C: the cached count (GameState.HirelingCount): set when a hireling joins, by the default party and by a load.
    /// <summary>The number of hirelings (members with a term), as the game counted them when one last joined.</summary>
    public int HirelingCount => State.HirelingCount;

    // SetReputation (R029 19b4) is shared: see CardContext.Services.cs.

    /// <summary>
    /// The inn's party roster (R038+0x0000, PartyRosterScreen), supplied by the presentation; its argument is the register word the
    /// card line had when it opened the screen (the roster's first list reads it, see InnRoster.List).
    /// </summary>
    public Action<int>? PartyRosterScreen { get; set; }

    // orig: R029+0x2246 (0E76:2246) draft (bytes match): thunk 09C0:1EE1 = R038+0x0000 (the inn's roster), then the card's picture and the
    // party column again (thunk 09C0:1EA5: ICardScreen.Reload); tests: CardSmokeTests, InnRosterTests
    // C: the card passes the location as an argument the function does not read; the roster's list reads the caller's SI instead.
    /// <summary>Opens the inn's party roster; <paramref name="callerWord"/> is the card line's SI (its hours).</summary>
    public void ShowPartyRoster(int callerWord)
    {
        PartyRosterScreen?.Invoke(callerWord);
        Screen.Reload();
    }

    // ---- what other parts of the game report to cards ------------------------------

    // orig: DS:9084; tests: CardSmokeTests
    // C: set by the battle (R014) when a marked combatant falls; meaning inferred.
    /// <summary>
    /// Set by a battle when a combatant marked as its target fell (the robber knight, the
    /// dragon …); cards ask it after <see cref="RunBattle"/>.
    /// </summary>
    public bool BattleTargetSlain { get; set; }

    // orig: DS:A891; tests: CardSmokeTests
    // C: cleared by the main loop after any screen but the map; who sets 1 and 3 is not traced. The same word as MapLeaveMode (GameState.MapLeaveMode); the two names come from the cards' readings of it.
    /// <summary>
    /// How the party came upon the place of an encounter: set by the travel map, 0 after any
    /// other screen (1: the party was on its guard / spotted it first; 3: night camp).
    /// </summary>
    public int EncounterApproach
    {
        get => State.MapLeaveMode;
        set => State.MapLeaveMode = value;
    }

    // orig: party_x DS:E3D0; tests: CardSmokeTests
    // C: the cards' reuse of the map X while in a city.
    /// <summary>
    /// A mark city cards keep while the party is in a city, in the map position's X (the reference's
    /// <c>party_x</c>), which the travel map sets again when the party leaves: the side taken
    /// in a political struggle (0 the rebels, 1 the government), a failed jail escape (2) ...
    /// </summary>
    public int CityMark
    {
        get => State.MapPosition.X;
        set => State.MapPosition = State.MapPosition with { X = value };
    }

    // $Support .. $Support6 are CardVariables.Support (CardVariables.Additional.cs).
}
