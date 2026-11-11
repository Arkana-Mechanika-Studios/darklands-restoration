namespace Darklands.Core;

/// <summary>
/// The original game's bugs and quirks that the rules reproduce, in one place. Each flag is
/// off (faithful) by default; setting it applies the obvious fix. Quirks without a flag are
/// reproduced only (listed at the end).
/// </summary>
/// <remarks>
/// Reproduced without a fix flag:
/// <list type="bullet">
/// <item><c>place-query</c> 2..11, 15..35, 40, 41 read the current city record whatever location is asked about; codes 39–41 read the current location.</item>
/// <item><c>place-query</c> codes without a case return the code itself (42 and up, and negative codes: what the original leaves in AX) or 0 for 19, 21, 22, 25, 30 and 36 (there the original returns the caller's BX).</item>
/// <item>Card-visible side effects kept: the overnight healing test leaves the best healer in <c>top-scoring-member</c>/<c>top-value-so-far</c>; the attribute queries' own member (DS:9A44); <c>thunk 09C0:2175</c>'s return is the last event's slot.</item>
/// <item><c>event-datum</c> and the quest/fact queries ignore the DS:00F0 suppression flag; the presence queries honour it.</item>
/// <item>Event scans leave the last visited slot in <c>latest-event</c>; <c>event-here-lengthen</c> leaves −1 or 300 there when it creates an event.</item>
/// <item><c>event-refresh</c> gives a new event its hours literally (9999 is not "forever" there).</item>
/// <item><c>event-delete</c>, <c>place-flags-reset</c> and <c>attribute-overshoot-limit</c> return the AX left over when slot 299 holds the kind; a brew records its place and risk in <c>events[latest-event]</c>.</item>
/// <item>Potion weapon bonuses (kind 0x4A) are never undone at expiry.</item>
/// <item>The daily city politics change sides and measure strengths at the <em>current</em> location, not the event's city.</item>
/// <item><c>carries-item</c>'s formula test reads the formula table at member × 22 + code − 0x5F (other members' formulas for low codes).</item>
/// <item><c>exercise-skill</c> for one member rolls even when the member is absent.</item>
/// <item>The jewelers' guild at night passes the party's lowest Stealth value as the member: record 20's skill byte is the last battle's place action of combatant 11 (<see cref="Rules.RecordOverrun"/>).</item>
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
/// <item>Packs are 64 entries with a signed count: deleting leaves stale entries, <c>item-keep-percent</c> on an empty pack makes the count −1 and the next item added goes to the entry before the pack (the previous member's last entry).</item>
/// <item>The member data of segment 2E38 is addressed as the original lays it out: <c>carries-item</c>'s formula byte and <c>member-flag-set</c>(−1) reach the packs, formula and saint tables around (<c>Party.MemoryByte</c>).</item>
/// <item>Rows past the end of the barter, item store and loot screens' pack lists, and of the item store list, act on the stale entries there (the store's working table keeps what earlier stores left in this run).</item>
/// <item><c>place-standing-adjust</c>(−1) (the open country) reads a null record: one chance drawn, nothing changed.</item>
/// </list>
/// Not reproduced (the original reads undefined memory): a loot pile row past the last entry (the
/// pile's buffer is a fresh DOS block whose tail is freed battle memory), empty event slots in scans,
/// the quest getters without a quest, handler 3's write through an undefined slot, and member −2's skill in
/// <c>skill-temporary-drop</c> (taken as 0 here).
/// </remarks>
public sealed class OriginalBehaviour
{
    /// <summary>The faithful game: every bug as in the original.</summary>
    public static OriginalBehaviour Faithful => new();

    /// <summary>
    /// The faithful game with the data-safety fixes on: the original's bugs that damage the player's data (saved games, the item
    /// stores in CACHE.TMP) or end the program in play (the game since the last save lost, as a load crash loses it) are fixed, every
    /// other quirk stays. The fixed files are still read by the original. The player's game
    /// uses this; future data-safety fixes go here, and besides them only the evident display fixes the user chose for every player
    /// game (<see cref="FixRosterFigurePreview"/>, <see cref="FixPartyInfoBattleCycles"/>).
    /// </summary>
    public static OriginalBehaviour DataSafe => new()
    {
        FixInnCacheRewrite = true, FixCacheStoreLimit = true, FixSaveWriterSkipsEmptySlots = true, FixLeavingMemberRecordTail = true,
        FixStaleDissolveSlot = true, FixDissolveKeyMember = true, FixStairsRecordOfShownLevel = true, FixLeaderStairsRecordOfShownLevel = true, FixExitedMemberStrandsTheRest = true, FixPlaceActionsOnOwnLevel = true, FixTriggersOfOtherLevels = true, FixPileOfOtherLevel = true, FixStaleStairsBackRecord = true, FixFollowersOnShownLevel = true,
        FixGhostPartySlot = true, FixDamagedSavedGame = true,
        FixRosterMissingFigure = true, FixLoadTwoSlotsOneRecord = true, FixMapQuitFreedBuffers = true, FixRosterColourRow = true,
        FixRosterFigurePreview = true, FixRosterStaleRow = true, FixImpossibleSavedGame = true, FixPartyInfoBattleCycles = true,
    };

    /// <summary>
    /// Leaving an item store that already has a stored list rewrites CACHE.TMP with a 101-byte header over the 198-byte header's
    /// offsets, copying the other stores from the wrong place (heap bytes, store data lost or garbled); fixed: the file is laid out
    /// as a game's file is, every store kept (the compact layout of <see cref="FixCacheStoreLimit"/>).
    /// </summary>
    // orig: stash-store R035+0x1468 (1462:1468), data safety, data-damaging; the clean layout is saved-game-read R037+0x4184 (0x63, 0, 0x62 zero words) with stash-store(-1) appending after it
    public bool FixInnCacheRewrite { get; set; }

    /// <summary>
    /// The 99th item store's offset is written over the first store's count byte and first item, so the file is damaged
    /// (the original also reads, appends and rewrites only a small offset table correctly); fixed: the file is laid out compactly
    /// (a 126-byte table, at most 62 stores, at most 127 entries a store, stores no place refers to dropped and the rest renumbered)
    /// and Put at a place that would need more is refused, the item staying in the pack.
    /// </summary>
    // orig: stash-store R035+0x1468 (1462:1468), the append at R035+0x1532 and the rewrite at R035+0x15A2 (cbw R035+0x1615), data safety, data-damaging
    public bool FixCacheStoreLimit { get; set; }

    /// <summary>The item stores are laid out compactly and Put is limited (either of <see cref="FixInnCacheRewrite"/>, <see cref="FixCacheStoreLimit"/>).</summary>
    public bool CompactCache => FixInnCacheRewrite || FixCacheStoreLimit;

    /// <summary>
    /// The save writer leaves out the record of a party slot whose status is 0 but writes the full record count, so the file is
    /// short and the original can crash loading it (the Wild Hunt's victim causes it); fixed: every roster record is written.
    /// </summary>
    // orig: save writer R037+0x2ACE (1462:2ACE), data safety, data-damaging
    public bool FixSaveWriterSkipsEmptySlots { get; set; }

    /// <summary>
    /// A member who leaves the party through <c>party-leave</c> (gone away, sent off by a card) has only the 0x80-byte member record
    /// written back to CHARACTR.TMP: the saints, formulas and pack entries there stay as the file last had them (the member joined, the
    /// game was loaded) while the item count is the member's own, so on rejoining the member has the old saints, formulas and items
    /// (what was learned or gained since is lost; a count above the old list's brings stale or empty entries); fixed: the whole record
    /// is written.
    /// </summary>
    // orig: party-leave R037+0x3B58 (1462:3B58), data safety, data-damaging
    public bool FixLeavingMemberRecordTail { get; set; }

    /// <summary>
    /// A saved game cut short (a file that ends inside the header and party block, a record or the location table its own counts say it
    /// holds: copied badly, a full disk) is loaded all the same: the original reads what the file still holds and goes on from what the
    /// missing bytes leave in its buffers, so the game that results is garbage (a party without members, the places zeroed) or the
    /// program ends back in DOS ("pics\shield.pic: No such file or directory" from a header cut at 0 or 96 bytes). Fixed: the load is refused before anything changes and a notice says so (the load screen or
    /// the start screen stays); a file that holds all its records, whatever its cache or its battle file, loads as before.
    /// </summary>
    // orig: saved-game load R037+0x8ED4 (1C7D:0D24): each _dos_read of the header, party block, records and location block ignores a short count (checked in the emulated original: header cut at 0 or 96 bytes ends in DOS, cut at 512 or 9374 plays on)
    public bool FixDamagedSavedGame { get; set; }

    /// <summary>
    /// A saved game whose header holds what no game has (edited by hand or by another tool: a month above 11, a day above 31, an hour above
    /// 23, location-now outside the places, a location table shorter than the game's places) is loaded all the same: the original reads past
    /// its tables where they are indexed (the month names, the days per month, the place records), and the rewrite stops with an exception
    /// in the code that indexes (a card, the party information, a trade). Fixed: the load is refused before anything changes and a notice
    /// says so, as for a damaged file (<see cref="FixDamagedSavedGame"/>); every saved game play writes loads as before.
    /// </summary>
    // orig: saved-game load R037+0x8ED4 (1C7D:0D24): the date, location-now and the location table are read with no check (card fuzz: a month
    //   of 12 read past the month table in the original, an IndexOutOfRange in the rewrite); data safety, a crash
    public bool FixImpossibleSavedGame { get; set; }

    /// <summary>
    /// The inn's roster ("reform your party"): an arrow key onto a row past the figure list (row 4 or later with the game's four figure
    /// pictures) whose character is in the party loads "pics\small.pic", which does not exist, and the program ends (exit 99, the C
    /// library's "No such file or directory"): the game since the last save is lost. Fixed: nothing is drawn and the roster goes on.
    /// </summary>
    // orig: R038+0x02F8 (the arrows, R038+0x045F..0x04CA): "pics\" (DS:4A3D) + the 4-byte entry DS:E7E2 + 4 x DS:E7DA + "small.pic" (DS:4A43),
    //   root 0617:045C, whose opener root 0617:000E ends the program on a missing file (root 0617:067E: mode 3, perror, exit(99)); data safety, a crash
    public bool FixRosterMissingFigure { get; set; }

    /// <summary>
    /// The inn's roster ("reform your party"): the colour keys 1, 2, 3 act on the party slot whose roster entry equals the highlighted ROW
    /// number, not on the row's character (as Heraldry and Select Character Image do): once a record before a member is not listed (a
    /// character left at another inn), a colour key changes another member's figure colours, which the saved game keeps, or nobody's.
    /// Fixed: the highlighted row's own character's colours change (none on a row without a character).
    /// </summary>
    // orig: R038+0x02F8 keys 0x231 / 0x332 / 0x433 (R038+0x127F, 0x1807, 0x1E87: the five slots' ids compared with DS:E7DA itself); the
    //   part's bytes written into the slot's colours (segment DS:7ED2 + 0x33FC + 0x18 x slot); measured: vis10 roster-colour; data safety, data-damaging
    public bool FixRosterColourRow { get; set; }

    /// <summary>
    /// The inn's roster ("reform your party"): the highlight goes down to as many rows as the roster has characters, listed here or not,
    /// and a row the list did not fill keeps the character an earlier list left there (the first character before any list): Heraldry
    /// and Select Character Image on such an empty row change that character's shield or figure, which the saved game keeps, if the
    /// character is in the party. Fixed: nobody's changes on a row without a character.
    /// </summary>
    // orig: R038+0x02F8 keys 0x2348 (R038+0x0EB7) and 0x1F53 (R038+0x1083): the five slots' ids compared with DS:8D06[DS:E7DA]; the list
    //   R038+0x23EC writes DS:8D06 only for the rows it lists and sets DS:E7DC to the number of records (R038+0x259D); measured: vis10
    //   roster-stale; data safety, data-damaging
    public bool FixRosterStaleRow { get; set; }

    /// <summary>
    /// The inn's roster's figure preview shows the wrong figure in the wrong place: Up, Left and Right draw it at the bottom of the
    /// party column, which is never cleared (each figure lands over the earlier ones); Down draws it below the commands; the picture is
    /// the figure list's entry of the highlighted ROW number, in the colours of the member whose slot holds that number; a click on a
    /// row moves only the highlight. Fixed (an evident display fix the user chose for every player game): the highlighted character's
    /// own figure in its own colours, in one place at the bottom of the party column, the previous one cleared, shown when the roster
    /// opens and after an arrow, a click, Add, Select Image and the colour keys; nothing for a character not in the party.
    /// </summary>
    // orig: R038+0x02F8 keys 0x4800/0x4B00/0x4D00 (draw at (0x0C, 0xA4), R038+0x05AD..0x05B9) and 0x5000 (draw at (0x46, 0xA5)); only
    //   (0x46, 0xA5, 0x23 x 0x23) is copied back (R038+0x0396 / 0x061E); the name DS:E7E2 + 4 x DS:E7DA (the row); the colours of the slot
    //   whose id equals DS:E7DA; R038+0x0000's list click moves only the highlight; measured: vis10 roster-figure (11 shots at 0 px)
    public bool FixRosterFigurePreview { get; set; }

    /// <summary>
    /// The party information (F6, or the battle bar's Party Info) opened in a battle shows its picture's colours with the battle's
    /// palette cycles still turning over colours 16..30: the battle's water and fire colours flash over the screen until it closes.
    /// Fixed (an evident display fix, as the load screen's own stop of the cycles): the cycles are held while the screen is open and
    /// run again when the battle is back.
    /// </summary>
    // orig: the F6 hot key (root 0000, key 0x4000 with DS:987F == 1) and the battle bar's item call the overlay vector 09C0:185B, which
    //   is the party information screen itself (R034+0x0EE6): its background, R034+0x11BC, loads pics\ptystats.pic with its palette
    //   (palette-load, root 0410:0364) and nothing stops the cycles (root 047C:040B) or clears DS:07D8, so the timer's frame work
    //   (root 047C:000A -> 047C:042A) writes the battle's ranges at their next steps. The load screen stops them (R037+0x8400..0x8426)
    public bool FixPartyInfoBattleCycles { get; set; }

    /// <summary>
    /// A saved game whose two party slots name one character record: the load reads the record into the first slot only, the second
    /// keeps a shield letter of 0, and the load screen's shield pictures then ask for "pics\shield.pic", which does not exist: the program
    /// ends (exit 99). Fixed: the game goes on with that slot empty (its id kept, so the save is written back as it was).
    /// </summary>
    // orig: saved-game-read R037+0x8ED4 (R037+0x96BE..0x96D3), the load screen R037+0x84BA (R037+0x86DA..0x8754 "pics\shield<letter>.pic"
    //   for every slot whose id is not -1), measured live: lockstep session carry-dup-resave (synthetic save dup01), program exit 99 at
    //   R037+0x84BA+0x29F; data safety, a load crash
    public bool FixLoadTwoSlotsOneRecord { get; set; }

    /// <summary>
    /// Alt-Q read by the travel map's own key look frees the map's row and cell buffers before it asks "Quit to DOS?"; after "no" the
    /// map goes on without them, and the next free of one (a second Alt-Q, the camp, a portrait click, leaving the map) frees a buffer
    /// already freed: the memory manager ends the program ("M - Buffer dealloc error", "S - ...", "Map - ...", exit 0). Fixed: the map
    /// keeps its buffers.
    /// </summary>
    // orig: R030+0x0FD8..0x101B, then R030+0x0F06 (camp), R030+0x031F (portrait), R030+0x026C (map exit) (root 05BA:00C2 with handle 0: root 044A:0164 fails, root 0000:1354, DOS function 9, exit(0));
    //   measured: session tk-map-altq-twice (local/batch/mapquit); data safety, a crash
    public bool FixMapQuitFreedBuffers { get; set; }

    /// <summary><c>event-advance</c> with "add" on a new event leaves 2 × value; fixed: the value.</summary>
    public bool FixNewEventAddDoubles { get; set; }

    /// <summary><c>stat-cap-assign</c> ignores its 0..99 limit; fixed: the limit applies.</summary>
    public bool FixSetAttributeMaximumLimit { get; set; }

    /// <summary><c>member-top-skill</c> always reports skill 6; fixed: the best skill's number.</summary>
    public bool FixBestWeaponSkillNumber { get; set; }

    /// <summary>The attribute-sum queries record 2 × a; fixed: a + b.</summary>
    public bool FixAttributeSums { get; set; }

    /// <summary><c>place-hostile</c>'s unrest test has its arguments swapped; fixed: unrest here.</summary>
    public bool FixHostileUnrestTest { get; set; }

    /// <summary><c>place-assign</c> 6 and 11 write each other's shop; fixed: the shop <c>place-query</c> reads.</summary>
    public bool FixShopSetSwap { get; set; }

    /// <summary><c>place-assign</c> 37 overwrites every occupied slot; fixed: the list shifts.</summary>
    public bool FixRecordedListShift { get; set; }

    /// <summary><c>give-item-record-form</c>(−2) gives each member the items twice; fixed: once.</summary>
    public bool FixPartyItemsAddedTwice { get; set; }

    /// <summary>
    /// A temporary boost owned by −2 (the whole party) is never taken back at expiry (the
    /// presence test reads the byte before the member records); fixed: taken back from everybody.
    /// </summary>
    public bool FixEverybodyBoostsUndone { get; set; }

    /// <summary>
    /// <c>member-flag-set</c>(−1) does not resolve the leader: the saint is not learned and member
    /// 4's formula bytes get the bit; fixed: the leader learns it.
    /// </summary>
    public bool FixSaintLearnedByLeader { get; set; }

    /// <summary>
    /// St.Cecilia's standing goes to <c>location-now</c> even in the open country (−1: a null record,
    /// nothing changes), where the other saints take the nearest city; fixed: the nearest city.
    /// </summary>
    public bool FixSaintStandingInOpenCountry { get; set; }

    /// <summary>
    /// A saint's weapon-skill boost (<c>gear-bonus-sums</c>) for a prayer for the party (−1) looks up
    /// the equipped weapons of the record before the members' (menu-bar words), finds none and
    /// changes nothing; fixed: the leader's weapons.
    /// </summary>
    public bool FixSaintWeaponSkillsForParty { get; set; }

    /// <summary><c>item-keep-percent</c> starts one past the last stack; fixed: at the last stack.</summary>
    public bool FixKeepItemsOffByOne { get; set; }

    /// <summary><c>item-grade-flags</c> tests item 0's flags; fixed: each stack's.</summary>
    public bool FixItemQualityByFlags { get; set; }

    /// <summary>
    /// <c>skill-temporary-drop</c>(−2) boosts everybody once per present member; and
    /// <c>stat-temporary-boost</c>(−2) stops at a member above the boost and carries the reduced
    /// boost on. Fixed: each present member gets the boost once.
    /// </summary>
    public bool FixWholePartyBoosts { get; set; }

    /// <summary><c>battle-driver</c> adds the party seed to a location seed twice; fixed: once.</summary>
    public bool FixBattleSeed { get; set; }

    /// <summary>The fame cost sets fame to cost − fame; fixed: fame − cost.</summary>
    public bool FixFameCost { get; set; }

    /// <summary>
    /// <c>groups-repair</c> drops every enemy group after the first empty one (it shifts the groups
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
    /// Changing a camp activity looks for the member's brews from event slot (number of formulas) up, not from slot 0, so it
    /// finds none in a game with fewer events and the brew goes on; fixed: every slot is scanned.
    /// </summary>
    public bool FixCallOffScansEveryEvent { get; set; }

    /// <summary>
    /// The shop stock test reads the city size from an uninitialised stack slot, so the first
    /// item and the item after each of the first four goods use the stock table's row 1; fixed:
    /// always the city size − 1.
    /// </summary>
    public bool FixShopStockSize { get; set; }

    /// <summary>
    /// <c>cloud-on-line-test</c> loops to the cloud count inclusive, so the slot after the last
    /// effect cloud (an old cloud's copy) still blocks shots; fixed: only the clouds there are.
    /// </summary>
    public bool FixCloudLineOneTooMany { get; set; }

    /// <summary>
    /// With the party split over two levels and the game paused, the U key's stairs order copies entry [selected] of the member
    /// table DS:C8B2 into DS:C77E whoever is selected; with nobody selected (−1) it copies the byte before the first entry,
    /// DS:C8B1 into DS:C77D. Fixed: nothing is copied without a selection. (Nothing visible reads those two bytes: harmless.)
    /// </summary>
    // orig: R016+0x014E (1EF8:014E), the copy at R016+0x0165..0x0169, no test of DS:A6B2
    public bool FixStairsKeyWithoutSelection { get; set; }

    /// <summary>
    /// Esc with nobody selected (paused, outside order mode) writes the flee bit to flags[−1]'s high byte, which is the pause
    /// byte DS:A695: with DS:D6EA = 1 it becomes 0x11, which the tests for "paused" (== 1) and for "running" (== 0) both refuse
    /// (the battle's tests of it are mixed: Space then resumes, the AI and melee stay frozen). Fixed: no flee bit is written.
    /// </summary>
    // orig: R016+0x13D4 (1EF8:13D4), the xor at R016+0x13F9 with bx = −2
    public bool FixEscapeWithoutSelectionPauseByte { get; set; }

    /// <summary>
    /// The loot screen's D after a battle skips a party slot "gone through an exit" by reading the combat floor table (bytes, one a
    /// combatant) as words at twice the slot: a member who left the battlefield is not skipped (his items can be shared to him), and
    /// slot s is skipped, with the "can't view" message, only when combatant 2s's floor is -99 and combatant 2s + 1's is -1. Fixed:
    /// the slot's own floor byte is tested.
    /// </summary>
    // orig: R040+0x0DB3..0x0DB7 `shl si, 1; cmp word [si - 0x594C], -0x63`
    public bool FixLootExitTestReadsWords { get; set; }

    /// <summary>
    /// Dissolve Lock uses up the pack entry the Eater-Water was found at (DS:C7CD, a byte) when the order was given or, for the leader,
    /// at the last tick, whatever the pack holds when the member acts: after an item given or dropped on the character sheet in order
    /// mode it is another stack, or the stale entry past the last one (the last stack is then lost, an empty pack's count becomes −1),
    /// and with the Eater-Water gone (slot 0xFF) entry 255 lies in another member's pack or past the packs. Fixed: only an Eater-Water is
    /// used up, the weakest one the pack still holds (none: nothing).
    /// </summary>
    // orig: fighter-goto-action R014+0x2256..0x2263 (use-up-slot 18BF:0FA4 with DS:C7CD[m]), data safety, data-damaging
    public bool FixStaleDissolveSlot { get; set; }

    /// <summary>
    /// The Stairs order of a member alone on another level is carried out by use-exit with his stairs-back record number (DS:C8B2,
    /// a record of HIS level), read from the level SHOWN when the order is given: with the view moved back to the party's level
    /// before Space (another member selected after Use Stairs, GitHub issue 3) it is that level's record n, past its count as a
    /// rule. Those slots hold what the generators left there (castles and mines build every level in one buffer and clear only the
    /// counts): zeros (level 0 at (0, 0)), another level's record or stale bytes, so he goes to the wrong place or level, or comes
    /// back by chance (the Rotenburg keep). Fixed: his own level's record is taken (the view goes to his level first), so he always
    /// takes his own stairs back, as Use Stairs is offered for.
    /// </summary>
    // orig: fighter-goto-action R014+0x22B2..0x22CB (use-exit with DS:C77E[i]), use-exit R023+0x1618 (record n of the level shown,
    //   R023+0x162B..0x164A, no count test), data safety
    public bool FixStairsRecordOfShownLevel { get; set; }

    /// <summary>
    /// With the party split over two levels and the battle running (not paused), U sends the leader through his stairs-back record
    /// number (DS:C8B2[leader], a record of HIS level) read from the level SHOWN: with the view on another floor (the lone member
    /// selected, then deselected: GitHub issue 3) it is that floor's record n, past its count as a rule, so the leader leaves through
    /// stairs of a floor he is not on, to the wrong place or level, and the lone member stays. Fixed: the leader still takes the
    /// stairs (U outside order mode is his action), through his own level's record (the view goes to his level first).
    /// </summary>
    // orig: fight-keys R016+0x014E (1EF8:014E, the U case with DS:11CA != DS:C623, not paused), use-exit R023+0x1618 (record n of
    //   the level shown, R023+0x162B..0x164A, no count test), data safety
    public bool FixLeaderStairsRecordOfShownLevel { get; set; }

    /// <summary>
    /// A member who left through an exit keeps status 1 with floor -99. The end test then takes "nobody able on the enemies' floor and
    /// somebody gone through an exit" for the party's flight even when the others are alive on another level (gone by the stairs): the
    /// battle ends as fled (or left) and every member on another level becomes Away, which the next card may turn into leaving the party
    /// for good. use-exit's reunion test counts him too, so the last member to take the stairs never brings the party together.
    /// Fixed: a member gone through an exit counts in neither test while an able member is still on the battlefield.
    /// </summary>
    // orig: fight-finished R014+0x08FE (the fled test), use-exit R023+0x1618 (the alone loop), data safety
    public bool FixExitedMemberStrandsTheRest { get; set; }

    /// <summary>
    /// The level image in memory is the one SHOWN: the place tests of each tick (which door, stairs, chest region a member stands in) and
    /// the place actions (Use Door, Pick Lock, Open Chest, Dissolve Lock, Disarm Trap) read it whatever level the member is on. A member on
    /// another level whose order runs while it is shown toggles, picks, opens or disarms the door or chest with his index on the level
    /// shown (waking that level's encounter behind it; a zero record past its count), and his own is untouched; standing at a place of
    /// the shown level's map he gets its index. Fixed: the place tests leave a member on another level alone, and his place action is
    /// carried out with his own level shown (the level shown before comes back).
    /// </summary>
    // orig: party-place-table R014+0xDC14, fighter-goto-action R014+0x2170 (door-flip R023+0x00B8, lock-picking R023+0x12C0, chest-opening
    //   R023+0x13D0, R023+0x153E), data safety
    public bool FixPlaceActionsOnOwnLevel { get; set; }

    /// <summary>
    /// The trigger test of each tick (secret doors, hidden traps, dart and spell traps, pits, wall openings, alarms) reads the triggers of
    /// the level SHOWN at the place of every member, and of every enemy for the traps, whatever level they are on: a member on another
    /// level sets off the shown level's alarm (the battle ends with its card though nobody is there), its dart or spell trap (hitting
    /// whoever stands there) or finds its secret doors and traps by remote. Fixed: only those on the level shown spring its triggers.
    /// </summary>
    // orig: R014+0xD6DA (no floor test), handlers R014+0xD882 / 0xD966 / 0xDA18 / 0xDACA, R023+0x0D88 / 0x0E30 / 0x0812, data safety
    public bool FixTriggersOfOtherLevels { get; set; }

    /// <summary>
    /// The pack of a combatant who falls becomes a loot pile on the level SHOWN, merged with a pile of that level near his place and
    /// nudged by the shown screen's tiles, whatever level he fell on: a member who dies alone on a level not shown leaves his belongings
    /// on another level (in a wall there, perhaps), out of reach until a victory gathers every pile; after a flight they are lost with the
    /// battlefield. Fixed: the pile lies on his own level, merged only with a pile there.
    /// </summary>
    // orig: fighter-dying R014+0x2324 (the pile's level = DS:11CA, the merge test on DS:11CA), R014+0x2802, data safety
    public bool FixPileOfOtherLevel { get; set; }

    /// <summary>
    /// use-exit writes a member's stairs-back record number (DS:C8B2) after its level switch: right when he goes alone (the old level is
    /// shown again), but the NEW level's record when the party comes together, and never for a member separated without moving alone (0:
    /// unconscious left behind, the last of a group move). With the party split Use Stairs is offered anywhere and sends him through
    /// that number: record 0 of his level, or a slot past its count (a zero record: level 0 at (0, 0), outside the map). Fixed: a number
    /// past his own level's stairs count is not taken (the order does nothing).
    /// </summary>
    // orig: use-exit R023+0x1618 (DS:C8B2[who] = record n's +0x0D read after the level switch), fight-keys R016+0x014E, data safety
    public bool FixStaleStairsBackRecord { get; set; }

    /// <summary>
    /// When the party has gone down (or up) the stairs, the enemies of the kinds that follow are placed after a delay of 1 to 4 seconds
    /// around the arrival spot on the level SHOWN at that moment: with the view moved to a member left behind on the old level (an
    /// unconscious one can be selected), they appear on his level, and the next end test, finding nobody able on the enemies' level, turns
    /// him Away (lost from the party) and removes the enemies. Fixed: they are placed on the party's level.
    /// </summary>
    // orig: R025+0x0CFC (placed on DS:11CA around DS:C3CD), the delay DS:C2BB from R023+0x17D2, fight-finished R014+0x08FE, data safety
    public bool FixFollowersOnShownLevel { get; set; }

    /// <summary>
    /// The Devil's Bridge Crossing (a won argument, then the battle lost or fled) sets the status of party slot DS:EE8E to able, a
    /// variable the won argument never assigned (the slot of the member the demon took in an earlier crossing, or a start value): the
    /// slot just emptied by dismissing the away members comes back with status 1 but no character record (id −1, the roster one short):
    /// a ghost member in a saved game. Fixed: a slot that was emptied stays empty.
    /// </summary>
    // orig: R088+0x05C6..0x05CF and R088+0x060E..0x0617 (`mov bx,[EE8E]; shl bx,7; mov byte [bx-6397h],1`), data safety, data-damaging
    public bool FixGhostPartySlot { get; set; }

    /// <summary>
    /// The Dissolve key (D) outside order mode, where the leader dissolves the lock, uses up the pack entry at member 0's slot byte
    /// (DS:C7CD) of the selected member (DS:A6B2): the party member under the pointer, else nobody (−1). With the pointer on another
    /// member that member's stack at member 0's slot is used up, whatever it is (or the stale entry past his last one: the last stack is
    /// then lost); with nobody selected the entry 64 before member 0's (before the packs, or with slot 0xFF member 2's entry 63) and the
    /// count byte 0x80 before member 0's (DS:9C53). The leader's own Eater-Water is used up only when the pointer is on member 0 and he leads.
    /// Fixed: the leader's Eater-Water is used up (his slot byte, as Dissolve Lock's, with <see cref="FixStaleDissolveSlot"/>).
    /// </summary>
    // orig: R016+0x0F9A..0x0FBA (use-up-slot 18BF:0FA4 of DS:A6B2 with the byte DS:C7CD), data safety, data-damaging
    public bool FixDissolveKeyMember { get; set; }

    /// <summary>
    /// Enter travelling individually with nobody selected (after the button held over a member and let go elsewhere) leaves order
    /// mode giving that member's last order to "member -1": the bytes before the tables, among them the pause byte (bits 0x10 and
    /// 0x02), the last combatant's exchange state, repath and give-up times, and the "USE STAIRS" message with the party split. Fixed:
    /// nothing is given (the parts on undefined memory, such as the active member DS:17B0 = 0xFF, are not reproduced either way).
    /// </summary>
    // orig: R014+0xBCA8 (19CD:0738) with DS:A6B2 = 0xFF, called by R016+0x0734
    public bool FixLeaveOrderModeWithoutSelection { get; set; }

    /// <summary>
    /// <c>level-change</c> saves the level it leaves only when DS:D4E4 is set, and <c>use-exit</c> writes the stairs record's
    /// +0x07 byte (0xFF: its wall group is uncovered) before that switch without setting it: after a switch with DS:D4E4 clear
    /// (and at the battle's end) the level comes back without the write, so the wall group opens again on the next use. Fixed: the
    /// write always persists.
    /// </summary>
    // orig: use-exit R023+0x1618 (the write at R023+0x1657, the switch at R023+0x16C7, DS:D4E4 set at R023+0x1764), level-change R024+0x05B4, battle save R003+0x0132
    public bool FixStairsWallMarkLost { get; set; }

    /// <summary>
    /// Loading from inside a game (Alt-L) clears the party's slots and the events before the load
    /// screen opens; leaving that screen with Esc goes on with that emptied game; fixed: Esc goes
    /// back to the game as it was.
    /// </summary>
    // orig: game-load-entry R037+0x8B76 (1C7D:09C6) matched: `party[i][0] = 0` and the events freed, then R037+0x81B2 and longjmp(DS:A752, -1) whatever it returned
    public bool FixLoadScreenEscape { get; set; }

    /// <summary>
    /// The university card (0x31) records a failed formulas search as a tried saints search, so
    /// the saints line is shut for the week instead; fixed: the formulas line is.
    /// </summary>
    // orig: R056+0x0D88 pushes type 0 where the other searches push their own line (R056+0x0F35 2, R056+0x1070 3, R056+0x126B 4)
    public bool FixUniversityFormulasTried { get; set; }

    /// <summary>
    /// The church-at-night card (0x35) asked about Mass between 09:00 and 14:59 names a stale
    /// stack word (−1, Matins) as the hour; fixed: the hour named when no Mass is held (Prime).
    /// </summary>
    // orig: R057+0x0D6A reads the uninitialised [bp-2]
    public bool FixChurchAltarBoyStaleHour { get; set; }
}
