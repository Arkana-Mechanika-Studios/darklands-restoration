using System.Runtime.CompilerServices;
using Darklands.Core;
using Darklands.Core.Characters;

namespace Darklands.Cards;

/// <summary>
/// Engine services the cards call: music, shops, the camp, the item store, reputation and the roster.
/// </summary>
public sealed partial class CardContext
{
    // ---- music ----------------------------------------------------------------------

    /// <summary>Plays a music piece (R043 1462 09ae; the presentation decides about the music option).</summary>
    public Action<int>? Music { get; set; }

    /// <summary>Starts the card's music piece (the original plays piece 1 instead when music is off).</summary>
    public void PlayMusic(int piece) => Music?.Invoke(piece);

    // ---- shops ----------------------------------------------------------------------

    /// <summary>The names the shop screen shows when a card gives none, by shop slot (0..11).</summary>
    public static readonly string[] ShopNames =
    [
        "Blacksmith", "Goods Merchant", "Swordsmith", "Armorer", "Gunsmith", "Bowyer",
        "Artificier", "Jeweler", "Clothmaker", "Physician", "Hospital", "Nobility",
    ];

    /// <summary>
    /// Names of kinds of traders and patrons, used for $NamedOneName (goods merchant,
    /// blacksmith, artificier, cathedral, foreign trader, pharmacist, Medici, Hanse, Fugger,
    /// village, City Lord, castle).
    /// </summary>
    public static readonly string[] PatronNames =
    [
        "goods merchant", "blacksmith", "artificier", "cathedral", "foreign trader", "pharmacist",
        "Medici", "Hanse", "Fugger", "village", "City Lord", "castle",
    ];

    /// <summary>The message shown when a missile weapon is needed.</summary>
    public const string NeedMissileWeapon = "You need a missile weapon";

    /// <summary>
    /// Opens a shop (R029 21aa): <paramref name="shop"/> is the city's shop slot (−1 a shop of
    /// its own, −2 the pawnshop), <paramref name="categories"/> the item category mask; without
    /// a name the shop slot's own name is shown. The card is shown again afterwards.
    /// </summary>
    public void Barter(int shop, long categories, string? name = null) =>
        Screens.Barter(shop, name ?? (shop >= 0 && shop < ShopNames.Length ? ShopNames[shop] : ""), categories);

    /// <summary>Camping or lodging (R029 222a): mode 0 at an inn, 1 in the wilderness, 2 other lodging, 3 sleeping rough.</summary>
    public void Camp(int mode) => Screens.Camp(mode);

    /// <summary>The items stored at a location (R029 225c).</summary>
    // orig: R029+0x225C (0E76:225C) draft(fn matched): item_cache_screen_vec(location), then card_load(card_name, pic_buf)
    // C: cache_screen does not use its argument (it works on cur_loc), so the location is not passed on.
    // DIVERGENCE: the card's redraw afterwards (card_load) is the card screen's job here, as for the shop and the camp.
    public void OpenItemStore(int location) => Screens.ItemStore();

    // ---- locations ------------------------------------------------------------------

    /// <summary>Sets the party's standing at a location (R029 19b4), without clamping.</summary>
    public void SetReputation(int location, int value) => Core.Rules.LocationRules.SetReputation(State, location, value);

    // ---- battles started by returning −2..−6 --------------------------------------------

    /// <summary>The battle a card sets up before it returns −2..−6.</summary>
    public Core.Battles.BattleOrders BattleSetup => State.BattleOrders;

    /// <summary>
    /// The result of the last battle (<c>battle_result</c>, DS:A893): what the main loop's
    /// battle (a card returning −2..−6) ended with; the battle code sets it.
    /// </summary>
    // UNCERTAIN: set by the battle screen when a fight ends; 0 (a victory) before any battle.
    public int LastBattleResult
    {
        get => State.BattleOrders.Result;
        set => State.BattleOrders.Result = value;
    }

    // ---- random personal names -------------------------------------------------------

    // RandomName (random_name) is shared: see CardContext.Memory.cs (PersonalNames supplies the name lists).

    // ---- the party roster (charactr.tmp) ----------------------------------------------

    /// <summary>
    /// Brings a stored character into the first free party slot (R037 3cd6); the game's
    /// character roster supplies it. Returns the slot, or −1 (also while no roster is connected).
    /// </summary>
    public Func<int, int>? RosterJoin { get; set; }

    public int JoinFromRoster(int character) => RosterJoin?.Invoke(character) ?? -1;

    // DeleteMember (R037 3f30) is shared: see CardContext.Memory.cs.

    /// <summary>Told when a member leaves for good, so the roster can forget the character.</summary>
    public Action<int>? RosterLeave { get; set; }

    // ---- game-wide values not in the game state yet --------------------------------------

    /// <summary>
    /// How the party last left the world map (the original's word at DS:A891, saved with the
    /// game): 3 after travelling, 1 after resting; the main loop sets 0 when a card other than
    /// the map runs.
    /// </summary>
    // orig: DS:A891 (GameState.MapLeaveMode)
    public int MapLeaveMode
    {
        get => State.MapLeaveMode;
        set => State.MapLeaveMode = value;
    }

    /// <summary>
    /// The party's standing as a card last read it (<c>cur_rep</c>): city cards set it when the
    /// party arrives and later cards use that value, not the live one (<see cref="ReputationHere"/>).
    /// </summary>
    public int StandingSeen
    {
        get => Extras.StandingSeen;
        set => Extras.StandingSeen = value;
    }

    private sealed class CardExtras
    {

        public int StandingSeen { get; set; }

        public int LastBattleResult { get; set; }
    }

    private static readonly ConditionalWeakTable<GameState, CardExtras> ExtrasOf = [];

    private CardExtras Extras => ExtrasOf.GetValue(State, _ => new CardExtras());
}
