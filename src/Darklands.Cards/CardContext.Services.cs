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

    /// <summary>
    /// The name of a kind of trader or patron, for $NamedOneName (0 goods merchant, blacksmith, artificier, cathedral, foreign trader,
    /// pharmacist, Medici, Hanse, Fugger, village, City Lord, 11 castle).
    /// </summary>
    // orig: 290E:2107 entries 0..11 (OriginalWords.PatronNames)
    public string PatronName(int k) => Word(Darklands.Content.Formats.Text.Strings.OriginalWords.PatronNames, k);

    /// <summary>The words of an EXE key (<c>exe:DS:XXXX</c>): a card's own copy of a word it puts into a text variable or names a shop by.</summary>
    public string ExeText(string key) => Translation?.Invoke(key) ?? World.Words.Exe(key);

    /// <summary>A name of the data files (<see cref="Darklands.Content.DataNames"/>) in the pack's words when it has them, else <paramref name="original"/>.</summary>
    // Not in the original: the 1.2 translations (the untranslated-text audit): the names cards put into their texts.
    public string ShownName(string key, string original) => Translation?.Invoke(key) ?? original;

    /// <summary>An item's name as the cards put it into their texts (the pack's words when it has them).</summary>
    public string ItemNameShown(int code) => ShownName(Darklands.Content.DataNames.Item(code), World.ItemName(code));

    /// <summary>A formula's full name as the cards put it into their texts, "" for none.</summary>
    public string FormulaNameShown(int formula) =>
        formula >= 0 && formula < World.Items.Formulae.Count ? ShownName(Darklands.Content.DataNames.Formula(formula), World.Items.Formulae[formula].FullName) : "";

    /// <summary>Entry <paramref name="k"/> of one of the original's word tables, as the card texts put it in (a translation's words when there is one).</summary>
    public string Word(Darklands.Content.Formats.Text.Strings.WordTable table, int k) =>
        Translation is { } translation && translation(World.Words.Key(table, k)) is { } words ? words : World.Words.Text(table, k);

    /// <summary>
    /// A translation pack's words for a key (<c>exe:290E:0000</c>), or null to keep the original's: supplied by the presentation, which
    /// alone knows the packs (null: no translation). The cards' own words only (the values of their text variables and shop names);
    /// none of them is kept in a saved game or decides anything.
    /// </summary>
    // Not in the original: the 1.1 translation packs (docs/LOCALISATION.md).
    public Func<string, string?>? Translation { get; set; }

    /// <summary>
    /// A translation pack's own card-text variable (its name, whether the chosen one is female): its words, or null (none, or no pack).
    /// Asked only for a name the game does not know (TextVariables).
    /// </summary>
    // Not in the original: the 1.1 translations (docs/LOCALISATION.md: a language's inflected pronouns).
    public Func<string, bool, string?>? PackVariable { get; set; }

    /// <summary>
    /// An amount in a translation pack's money words (<c>$Money1</c>..<c>$Money5</c>: GameTextIds.MoneyWords), or null (no pack, or
    /// one that does not translate them: Money.ToString, the original's words).
    /// </summary>
    // Not in the original: the 1.1 translations.
    public Func<Darklands.Core.Economy.Money, string?>? PackMoney { get; set; }

    /// <summary>The message shown when a missile weapon is needed: "You need a missile weapon" (the far string the slot 290E:1E5F points at).</summary>
    public const string NeedMissileWeapon = "exe:290E:049A";

    /// <summary>
    /// Opens a shop (R029 21aa): <paramref name="shop"/> is the city's shop slot (−1 a shop of
    /// its own, −2 the pawnshop), <paramref name="categories"/> the item category mask; without
    /// a name the shop slot's own name is shown. The card is shown again afterwards.
    /// </summary>
    // orig: R029+0x21AA (0E76:21AA) draft: the barter screen, then card-run (thunk 09C0:1EA5), whose MEM.LOG query (PartyQueries.CardRunQuery)
    //   is kept for its results; the redraw itself is the card screen's (see OpenItemStore)
    public void Barter(int shop, long categories, string? name = null)
    {
        Screens.Barter(shop, ShopName(shop, name), categories);
        Core.Rules.PartyQueries.CardRunQuery(State);
    }

    /// <summary>The name a shop is opened under: the card's, else the shop slot's own name (0..11, the name table R136+0xA70F), else empty.</summary>
    // orig: R029+0x21AA (0E76:21C4..21DD): a null name is replaced by R136+0xA70F[shop] (OriginalWords.ShopNames)
    // C: the original indexes the table with any slot; here only 0..11 are looked up and other slots get "" (as 1.0; the cards that leave
    //   the name null ask for 0..3, 5, 8 and 9).
    private string ShopName(int shop, string? name) =>
        name ?? (shop >= 0 && shop < 12 ? Word(Darklands.Content.Formats.Text.Strings.OriginalWords.ShopNames, shop) : "");

    /// <summary>Camping or lodging (R029 222a): mode 0 at an inn, 1 in the wilderness, 2 other lodging, 3 sleeping rough.</summary>
    // orig: R029+0x222A (0E76:222A): the camp screen (thunk 09C0:1EEB), then card-run (thunk 09C0:1EA5): its MEM.LOG query kept (CardRunQuery)
    public void Camp(int mode)
    {
        Screens.Camp(mode);
        Core.Rules.PartyQueries.CardRunQuery(State);
    }

    /// <summary>The items stored at a location (R029 225c).</summary>
    // orig: R029+0x225C (0E76:225C) draft(fn matched): stash-screen-overlay-entry(location), then card-run(card-title, image-buffer)
    // C: stash-screen does not use its argument (it works on location-now), so the location is not passed on.
    // DIVERGENCE: the card's redraw afterwards (card-run) is the card screen's job here, as for the shop and the camp.
    // C: card-run's MEM.LOG query is kept for its results (PartyQueries.CardRunQuery).
    public void OpenItemStore(int location)
    {
        Screens.ItemStore();
        Core.Rules.PartyQueries.CardRunQuery(State);
    }

    // ---- locations ------------------------------------------------------------------

    /// <summary>Sets the party's standing at a location (R029 19b4), without clamping.</summary>
    public void SetReputation(int location, int value) => Core.Rules.LocationRules.SetReputation(State, location, value);

    // ---- battles started by returning −2..−6 --------------------------------------------

    /// <summary>The battle a card sets up before it returns −2..−6.</summary>
    public Core.Battles.BattleOrders BattleSetup => State.BattleOrders;

    /// <summary>
    /// The result of the last battle (<c>battle-outcome</c>, DS:A893): what the main loop's
    /// battle (a card returning −2..−6) ended with; the battle code sets it.
    /// </summary>
    // C: set by the battle screen when a fight ends; 0 (a victory) before any battle.
    public int LastBattleResult
    {
        get => State.BattleOrders.Result;
        set => State.BattleOrders.Result = value;
    }

    // ---- the party roster (charactr.tmp) ----------------------------------------------

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
    /// The party's standing as a card last read it (<c>reputation-now</c>): city cards set it when the
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
