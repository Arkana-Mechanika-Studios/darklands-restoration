namespace Darklands.Core;

/// <summary>
/// The original game's bugs and quirks that the rules reproduce, in one place. Each flag is
/// off (faithful) by default; setting it applies the obvious fix. Quirks without a flag are
/// reproduced only (listed at the end).
/// </summary>
/// <remarks>
/// Reproduced without a fix flag:
/// <list type="bullet">
/// <item><c>loc_query</c> 2..11, 15..35, 40, 41 read the current city record whatever location is asked about; codes 39–41 read the current location.</item>
/// <item><c>loc_query</c> codes without a case return the code itself (42 and up, and negative codes: what the original leaves in AX) or 0 for 19, 21, 22, 25, 30 and 36 (there the original returns the caller's BX).</item>
/// <item>Card-visible side effects kept: the overnight healing test leaves the best healer in <c>best_member</c>/<c>best_value</c>; the attribute queries' own member (DS:9A44); <c>thunk 09C0:2175</c>'s return is the last event's slot.</item>
/// <item><c>event_value</c> and the quest/fact queries ignore the DS:00F0 suppression flag; the presence queries honour it.</item>
/// <item>Event scans leave the last visited slot in <c>last_event</c>; <c>event_here_extend</c> leaves −1 or 300 there when it creates an event.</item>
/// <item><c>event_renew</c> gives a new event its hours literally (9999 is not "forever" there).</item>
/// <item><c>event_remove</c>, <c>loc_facts_clear</c> and <c>event_48_max</c> return the AX left over when slot 299 holds the kind; a brew records its place and risk in <c>events[last_event]</c>.</item>
/// <item>Potion weapon bonuses (kind 0x4A) are never undone at expiry.</item>
/// <item>The daily city politics change sides and measure strengths at the <em>current</em> location, not the event's city.</item>
/// <item><c>has_item</c>'s formula test reads the formula table at member × 22 + code − 0x5F (other members' formulas for low codes).</item>
/// <item><c>practice_skill</c> for one member rolls even when the member is absent.</item>
/// <item>A member who is the leader but not able is replaced by the first able member only when time passes.</item>
/// <item>Potions above quality 45 take the last arm of their switch and are the weakest; weapon potions add to the bonus byte without limit; Strongedge and Great Power record the pack index.</item>
/// <item>A camp guard's weapon test leaves out Impact Weapons; day jobs needing a second set of buildings are never open.</item>
/// <item>The camp's teacher rows are numbered by teacher events met in the whole log.</item>
/// <item>On the character sheet Ctrl-F2..F5 with the pack open also give the selected item to member (n − 1) (the case falls through).</item>
/// <item>Barter: standing only surcharges and fame never changes a price (integer truncation); in places with flag 8 the price shown and the price paid differ (rnd(10) quality each time).</item>
/// <item>Barter: D drops an item from the <em>leader's</em> pack at the row shown for the bartering member; Ctrl-F1..F5 change the leader only while the shop is open.</item>
/// <item>Item store: an entry's count is a byte (the 256th item of a kind wraps it to 0); an emptied store is forgotten by the place but stays in the store table; the screen's list positions persist between visits.</item>
/// <item>Loot: found florins are dropped and groschen and pfennigs are capped at 10; a full pile (45 entries) or a full pack loses the item moved; a dead leader is replaced by the first slot that is not dead (even an empty one).</item>
/// <item>Character generator: Esc from the childhood attributes returns to the background list at age 15, where a row then serves the adult occupation of that number.</item>
/// <item>Character generator: saints are set with bit <c>1 &lt;&lt; (n % 8)</c> but read with <c>0x80 &gt;&gt; (n % 8)</c> (the saint learned is mirrored in its byte); a second formula of a potion increments its byte (grade 1 becomes grade 2).</item>
/// <item>"Create a new party" fills only four of the five party slots; a slot keeps its figure colours whoever joins it; heraldry changed in the party is lost when the member leaves it.</item>
/// <item>Packs are 64 entries with a signed count: deleting leaves stale entries, <c>keep_items_pct</c> on an empty pack makes the count −1 and the next item added goes to the entry before the pack (the previous member's last entry).</item>
/// <item>The member data of segment 2E38 is addressed as the original lays it out: <c>has_item</c>'s formula byte and <c>set_member_bit</c>(−1) reach the packs, formula and saint tables around (<c>Party.MemoryByte</c>).</item>
/// <item>Rows past the end of the barter, item store and loot screens' pack lists, and of the item store list, act on the stale entries there (the store's working table keeps what earlier stores left in this run).</item>
/// <item><c>loc_reputation_adj</c>(−1) (the open country) reads a null record: one chance drawn, nothing changed.</item>
/// </list>
/// Not reproduced (the original reads undefined memory): a loot pile row past the last entry (the
/// pile's buffer is a fresh DOS block whose tail is freed battle memory), empty event slots in scans,
/// the quest getters without a quest, handler 3's write through an undefined slot, and member −2's skill in
/// <c>lower_skill_temp</c> (taken as 0 here).
/// </remarks>
public sealed class OriginalBehaviour
{
    /// <summary>The faithful game: every bug as in the original.</summary>
    public static OriginalBehaviour Faithful => new();

    /// <summary><c>event_update</c> with "add" on a new event leaves 2 × value; fixed: the value.</summary>
    public bool FixNewEventAddDoubles { get; set; }

    /// <summary><c>set_attr_max</c> ignores its 0..99 limit; fixed: the limit applies.</summary>
    public bool FixSetAttributeMaximumLimit { get; set; }

    /// <summary><c>member_best_skill</c> always reports skill 6; fixed: the best skill's number.</summary>
    public bool FixBestWeaponSkillNumber { get; set; }

    /// <summary>The attribute-sum queries record 2 × a; fixed: a + b.</summary>
    public bool FixAttributeSums { get; set; }

    /// <summary><c>loc_hostile</c>'s unrest test has its arguments swapped; fixed: unrest here.</summary>
    public bool FixHostileUnrestTest { get; set; }

    /// <summary><c>loc_set</c> 6 and 11 write each other's shop; fixed: the shop <c>loc_query</c> reads.</summary>
    public bool FixShopSetSwap { get; set; }

    /// <summary><c>loc_set</c> 37 overwrites every occupied slot; fixed: the list shifts.</summary>
    public bool FixRecordedListShift { get; set; }

    /// <summary><c>add_item_rec</c>(−2) gives each member the items twice; fixed: once.</summary>
    public bool FixPartyItemsAddedTwice { get; set; }

    /// <summary>
    /// A temporary boost owned by −2 (the whole party) is never taken back at expiry (the
    /// presence test reads the byte before the member records); fixed: taken back from everybody.
    /// </summary>
    public bool FixEverybodyBoostsUndone { get; set; }

    /// <summary>
    /// <c>set_member_bit</c>(−1) does not resolve the leader: the saint is not learned and member
    /// 4's formula bytes get the bit; fixed: the leader learns it.
    /// </summary>
    public bool FixSaintLearnedByLeader { get; set; }

    /// <summary>
    /// St.Cecilia's standing goes to <c>cur_loc</c> even in the open country (−1: a null record,
    /// nothing changes), where the other saints take the nearest city; fixed: the nearest city.
    /// </summary>
    public bool FixSaintStandingInOpenCountry { get; set; }

    /// <summary><c>keep_items_pct</c> starts one past the last stack; fixed: at the last stack.</summary>
    public bool FixKeepItemsOffByOne { get; set; }

    /// <summary><c>item_quality_flags</c> tests item 0's flags; fixed: each stack's.</summary>
    public bool FixItemQualityByFlags { get; set; }

    /// <summary>
    /// <c>lower_skill_temp</c>(−2) boosts everybody once per present member; and
    /// <c>raise_attr_temp</c>(−2) stops at a member above the boost and carries the reduced
    /// boost on. Fixed: each present member gets the boost once.
    /// </summary>
    public bool FixWholePartyBoosts { get; set; }

    /// <summary><c>run_battle</c> adds the party seed to a location seed twice; fixed: once.</summary>
    public bool FixBattleSeed { get; set; }

    /// <summary>The fame cost sets fame to cost − fame; fixed: fame − cost.</summary>
    public bool FixFameCost { get; set; }

    /// <summary>
    /// <c>groups_fixup</c> drops every enemy group after the first empty one (it shifts the groups
    /// but keeps testing the stale counter); fixed: only empty groups are dropped.
    /// </summary>
    public bool FixEnemyGroupCompaction { get; set; }

    /// <summary>
    /// Changing a camp activity gives back an unfinished brew's ingredients from all five
    /// ingredient slots, so an empty slot adds an empty stack of item 0; fixed: only the
    /// formula's ingredients.
    /// </summary>
    public bool FixUnbrewEmptyIngredientSlots { get; set; }

    /// <summary>
    /// The shop stock test reads the city size from an uninitialised stack slot, so the first
    /// item and the item after each of the first four goods use the stock table's row 1; fixed:
    /// always the city size − 1.
    /// </summary>
    public bool FixShopStockSize { get; set; }

    /// <summary>
    /// <c>cloud_blocks_line</c> loops to the cloud count inclusive, so the slot after the last
    /// effect cloud (an old cloud's copy) still blocks shots; fixed: only the clouds there are.
    /// </summary>
    public bool FixCloudLineOneTooMany { get; set; }

    /// <summary>
    /// Loading from inside a game (Alt-L) clears the party's slots and the events before the load
    /// screen opens; leaving that screen with Esc goes on with that emptied game; fixed: Esc goes
    /// back to the game as it was.
    /// </summary>
    // orig: load_game R037+0x8B76 (1C7D:09C6) matched: `party[i][0] = 0` and the events freed, then R037+0x81B2 and longjmp(DS:A752, -1) whatever it returned
    public bool FixLoadScreenEscape { get; set; }
}
