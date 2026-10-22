using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>A saved game read back: the game, and the options and comment it was saved with.</summary>
public sealed record LoadedGame(GameState State, bool Music, bool SoundEffects, string Comment);

/// <summary>
/// The original's saved games (SAVES\DKSAVEn.SAV and its battle file .BSV) ↔ the game state. Every
/// field the model names is read into it and written from it; the bytes it does not name are
/// kept with the state (<see cref="GameState.OriginalSave"/>, <see cref="Character.OriginalRecord"/>,
/// <see cref="Location.OriginalRecord"/>) and written back as they were, so a game that has not
/// changed is saved byte for byte as it was read.
/// </summary>
// orig: save R037+0x2ACE (1462:2ACE) draft; load R037+0x8ED4 (1C7D:0D24)
// matched; tests: OriginalSaveTests
// C: the header, as both functions read and write it (offsets in the file):
//   +00 place name (0x15 bytes: the current location's name, or "Wilderness"), +15 comment (0x4F bytes), +64 party_seed,
//   +66 the word 7, +68 year, month, day, hour, +70 florins, groschen, pfennigs, +76 a zero long, +7A party_fame, +7C cur_loc,
//   +7E party_x, +80 party_y, +82 DS:A772 (the card), +84 DS:E898 (the card's text state), +86 saved_card, +88 saved_card2,
//   +8A prev_screen, +8C DS:9072/9074/9076 (bank notes), +92 DS:9078 (philosopher's stone), +94 DS:A67C (hirelings), +96 game_difficulty
//   (bit 7: saved in a battle), +97 DS:DB27, +99 DS:DA34, +9B marching_order[6], +A1 leader, +A2 opt_music, +A3 opt_sound, +A4 DS:A891,
//   +A6 a zero byte, +A7 36 zero words.
public static class OriginalSaves
{
    private const int PlaceAt = 0x00;
    private const int PlaceLength = 0x15;
    private const int CommentAt = 0x15;
    private const int CommentLength = 0x4F;
    private const int SeedAt = 0x64;
    private const int VersionAt = 0x66;
    private const int DateAt = 0x68;
    private const int PurseAt = 0x70;
    private const int ZeroLongAt = 0x76;
    private const int FameAt = 0x7A;
    private const int LocationAt = 0x7C;
    private const int MapXAt = 0x7E;
    private const int MapYAt = 0x80;
    private const int CardAt = 0x82;
    private const int SavedCardAt = 0x86;
    private const int SavedCard2At = 0x88;
    private const int PreviousCardAt = 0x8A;
    private const int BankNotesAt = 0x8C;
    private const int PhilosophersStoneAt = 0x92;
    private const int HirelingsAt = 0x94;
    private const int DifficultyAt = 0x96;
    private const int BattleLocationAt = 0x97;
    private const int ClearedLevelsAt = 0x99;
    private const int MarchingOrderAt = 0x9B;
    private const int LeaderAt = 0xA1;
    private const int MusicAt = 0xA2;
    private const int SoundAt = 0xA3;
    private const int MapLeaveModeAt = 0xA4;
    private const int ZerosAt = 0xA6;

    /// <summary>The word the original writes at +0x66 (v7o = 7); the loader skips it.</summary>
    private const int Version = 7;

    /// <summary>The comment the save dialog offers.</summary>
    // orig: `char comment[0x29] = "Darklands"` in save_game R037+0x2A68 (1462:2A68) matched
    public const string DefaultComment = "Darklands";

    /// <summary>The place name of a game saved outside any location.</summary>
    public const string Wilderness = "Wilderness";

    private const byte InBattleBit = 0x80;

    // ---- reading ------------------------------------------------------------------------

    /// <summary>
    /// Reads a saved game. <paramref name="running"/> is the game being played when the player loads
    /// from inside one: what the original does not save (the random generator, the bankers' standings,
    /// the saintly-changes option, a city record it does not reread) carries over from it.
    /// </summary>
    // orig: R037+0x8ED4 (1C7D:0D24) matched
    // C: the loader reads the header in the order of its fields, then `if (game_difficulty & 0x80) { DS:9081 |= 1; game_difficulty &= 0x7f; }`,
    // show_difficulty(1) and the "Loading Game" box (the presentation's), the party block, the party's records into their slots (the
    // others are skipped), the slot colours, the events, the location block, load_saved_game (which rebuilds charactr.tmp, locs.tmp and an
    // empty cache.tmp and recounts party_size from the slots' status), the rest of the file as cache.tmp, then city_desc and city_rec
    // of cur_loc from darkland.dsc and darkland.cty, and battle_restore (the .BSV).
    // C: `if (party[je][1] < 'A') party[je][1] = 'A';` for each party member: a shield letter below A becomes A.
    // C: DS:9081 bit 0 (a game saved in a battle) is GameState.ResumeBattle: the game takes the battle up from the .BSV (GameState.BattleFile).
    // DIVERGENCE: the missing-file exits ("load", "cache", "city": printf and exit(1)) are the caller's (a missing .SAV is not listed;
    // the city records come from the game data).
    public static LoadedGame Read(ReadOnlySpan<byte> sav, ReadOnlySpan<byte> bsv, string fileName, IGameData data,
        GameState? running = null, IRandomSource? random = null)
    {
        // C: a file whose character count runs past its records (written with a party slot of status 0: the Wild Hunt's victim, Wild
        // C: Hunt card 0x41, or another tool's DKSAVE40) is read out of step: the records, events and places come from the wrong bytes.
        // DIVERGENCE: depending on those bytes the original either goes on with the garbled game (DKSAVE40's re-save) or stops with
        // run-time error R6001, "null pointer assignment", back to DOS (a re-save after losing to the Wild Hunt; card oracle, then the
        // lockstep oracle); the rewrite loads the garbled game and goes on, never crashing.
        var file = SaveFile.Parse(sav, fixedLocationTable: true);
        var h = file.Header;
        var state = new GameState(random ?? running?.Random ?? new MscRandom()) { Data = data };
        if (running is not null)
        {
            state.Behaviour = running.Behaviour;
            // C: the item store screen's working table (2E38:37F4) is not in the save: a load keeps what the run left in it.
            state.StoreTable = running.StoreTable;
            state.OptionFlags = running.OptionFlags;
            // C: DS:160C is not in the .SAV: the running game's value stays (unless a battle file restores it, below).
            state.LastCastleFlags = running.LastCastleFlags;
            state.ClockSeed = running.ClockSeed;
            state.Messages = running.Messages;
            state.FuggerStanding = running.FuggerStanding;
            state.MediciStanding = running.MediciStanding;
            state.HanseStanding = running.HanseStanding;
            state.ShowSaintlyChanges = running.ShowSaintlyChanges;
            state.ReportSkillChanges = running.ReportSkillChanges;
            state.PartyTerrain = running.PartyTerrain;
            // C: the battle mode, battlefield, seed and enemy types (DS:A766..A770), the combat flags (DS:9080) and the result (DS:A893) are
            // globals the save does not hold.
            var orders = running.BattleOrders;
            state.BattleOrders.Mode = orders.Mode;
            state.BattleOrders.Enemy = orders.Enemy;
            state.BattleOrders.Seed = orders.Seed;
            orders.EnemyTypes.CopyTo(state.BattleOrders.EnemyTypes, 0);
            state.BattleOrders.Flags = orders.Flags;
            state.BattleOrders.Result = orders.Result;
        }
        state.Date = new GameDate((short)Word(h, DateAt + 6), (short)Word(h, DateAt + 4), (short)Word(h, DateAt + 2), (short)Word(h, DateAt));
        // C: DS:A67C is read from the save (load_saved_game: fread(&DS:A67C, 2, 1, fi)).
        state.HirelingCount = Word(h, HirelingsAt);
        state.CurrentCard = Word(h, CardAt);
        state.SavedCard = Word(h, SavedCardAt);
        state.SavedCard2 = Word(h, SavedCard2At);
        state.PreviousCard = Word(h, PreviousCardAt);
        state.Difficulty = (Difficulty)(h[DifficultyAt] & ~InBattleBit);
        state.Battlefield.LastLocation = Word(h, BattleLocationAt);
        state.Battlefield.ClearedLevels = Word(h, ClearedLevelsAt);
        state.MapLeaveMode = Word(h, MapLeaveModeAt);
        h.AsSpan(MarchingOrderAt, state.MarchingOrder.Length).CopyTo(state.MarchingOrder);
        state.MapPosition = new MapPosition(Word(h, MapXAt), Word(h, MapYAt));

        // The roster, then the party slots (the same objects).
        foreach (var record in file.Characters)
        {
            state.Roster.Characters.Add(CharacterRecords.Read(record));
        }
        var party = new Party
        {
            // C: party_seed is read as an int; the game uses it as an unsigned 16-bit seed (NewGame.PartySeed).
            Seed = UWord(h, SeedAt),
            Purse = ReadMoney(h, PurseAt),
            Fame = Word(h, FameAt),
            BankNotes = ReadMoney(h, BankNotesAt),
            PhilosophersStone = Word(h, PhilosophersStoneAt),
            LeaderIndex = (sbyte)h[LeaderAt],
        };
        var used = new HashSet<int>();
        for (var slot = 0; slot < SaveFile.PartySlots; slot++)
        {
            var id = file.PartyIds[slot];
            Character member;
            if (id >= 0 && id < state.Roster.Characters.Count)
            {
                // C: a second slot naming the same record gets its own copy (combatants), as the original reads it twice.
                member = used.Add(id) ? state.Roster.Characters[id] : state.Roster.Characters[id].Copy();
                state.Roster.PartySlots[slot] = id;
                // C: charactr.tmp keeps the record as read until the member leaves through R037+0x3B58 (CharacterRoster.StoredRecord).
                state.Roster.KeepStored(member, state.Roster.Characters[id].Copy());
                if (member.ShieldLetter < 'A')
                {
                    member.ShieldLetter = 'A';
                }
            }
            else
            {
                // C: a slot without a record keeps the status the load left (party[i][0] = 0): an empty slot.
                member = new Character { Status = CharacterStatus.Empty };
            }
            member.Image = Text(file.Images, slot * SaveFile.ImageSize, SaveFile.ImageSize);
            member.ImagePalette = file.Palettes[(slot * SaveFile.PaletteSize)..((slot + 1) * SaveFile.PaletteSize)];
            party.Members.Add(member);
        }
        // C: load_saved_game recounts party_size as the last slot whose status is not 0, plus one; the empty slots after it are not part of the
        // C: party but keep what was read into them (Party.TrimToSize).
        party.TrimToSize();
        state.Party = party;

        for (var i = 0; i < file.Locations.Count; i++)
        {
            state.Locations.Add(LocationRecords.Read(i, file.Locations[i]));
        }
        state.CurrentLocation = Word(h, LocationAt);
        // DIVERGENCE: more than 300 events would overrun the original's table; the rest are dropped.
        for (var i = 0; i < file.Events.Count && i < EventLog.Capacity; i++)
        {
            state.Events.Put(i, EventRecords.Read(file.Events[i]));
        }
        state.ItemStores.Load(file.Cache);
        LoadCityRecord(state, running);

        BattleSaveFile? battle = null;
        if (!bsv.IsEmpty)
        {
            battle = BattleSaveFile.Parse(bsv);
            // C: battle_restore runs last: its DS:DA34 (and marching order) replace the header's.
            state.Battlefield.ClearedLevels = battle.ClearedLevels;
            battle.MarchingOrder.CopyTo(state.MarchingOrder);
            state.LastCastleFlags = battle.LastCastleFlags;
            state.BattleFile = battle.ToBattleFile();
            // C: the restore masks the combat flags with 0x10 (a byte operation: DS:9081, their high byte, is the loader's).
            state.BattleOrders.Flags = state.BattleOrders.Flags & ~0xFF | state.BattleOrders.Flags & 0x10;
        }
        else
        {
            // C: without a battle file the restore returns at once: the program's battle variables and work files stay as they were.
            state.BattleFile = running?.BattleFile?.Clone();
        }
        if (state.BattleFile is { } held)
        {
            // C: the compressor's work area is the program's (load_buf + 0x4E20), not the saved game's.
            held.Compressor = running?.BattleFile?.Compressor ?? held.Compressor;
        }
        // C: DS:9081 bit 0: the main loop starts the battle again instead of the card.
        state.ResumeBattle = (h[DifficultyAt] & InBattleBit) != 0;
        state.OriginalSave = new SaveImage
        {
            FileName = fileName,
            Header = (byte[])h.Clone(),
            Images = (byte[])file.Images.Clone(),
            Palettes = (byte[])file.Palettes.Clone(),
        };
        return new LoadedGame(state, h[MusicAt] == 1, h[SoundAt] == 1, Text(h, CommentAt, CommentLength));
    }

    /// <summary>
    /// city_rec after a load: the record of cur_loc read from DARKLAND.CTY. Outside the cities the
    /// read lands elsewhere in the file: in the wilderness (−1) the seek fails and the record is the
    /// file's first 0x26E bytes (the city count byte and most of city 0); past the last city nothing is
    /// read and the record stays as it was.
    /// </summary>
    // orig: `fseek(f2b, (long)cur_loc * 0x26e + 1, 0); _dos_read(hj, city_rec, 0x26e, &id);` in R037+0x8ED4
    // C: the read is CityFile.Read over the running game's record: from the start screen that is the record world_load read at
    // C: start-up for the random first city (NewGame.StartUp, which the game passes as the running state).
    private static void LoadCityRecord(GameState state, GameState? running)
    {
        if (CityFile.Read(state.Data, state.CurrentLocation, running?.City) is { } record)
        {
            state.City = record;
        }
    }

    // ---- writing ------------------------------------------------------------------------

    /// <summary>
    /// The saved game of a state: the .SAV bytes and the .BSV bytes (the program's battle file: the last battle's or the last
    /// load's, zeros in a program that has had neither).
    /// </summary>
    /// <param name="comment">The comment typed in the save dialog; null keeps the one the game was read with.</param>
    // orig: R037+0x2ACE (1462:2ACE) draft (unmatched: the `nel > ixn` loop test's operand order)
    // C: `if (DS:907A == 1)` (saving in a battle): the members' readied items are put away (thunk 09C0:1CCF/1cc5/1cbb), game_difficulty |= 0x80,
    // locs.tmp and events.tmp are read back into memory first (the events into slots 0..n-1 over the battle's: EventLog.Reload), and the
    // box is drawn in colour 9; after the file is written every event is freed again (EventLog.ReleaseAfterSave): the battle goes on with an
    // empty table, and what it had added is lost (in slots n and up it was saved with the game).
    // C: in a battle the party's records are its combatant records (PartyRecordInBattle) and the battle file is written from the battle.
    // C: party_size is recomputed from the slots of charactr.tmp every time (DKSAVE40, written by another tool with 6, is saved with 4);
    // then charactr.tmp's count nd and, for each of the nd roster entries in order: a party member's record from memory (combatants,
    // saints, formulas, pack) with one charactr.tmp record read past it, nothing at all when the slot's status `party[je][0]` is 0, any
    // other entry the next record read from charactr.tmp. A status-0 slot (a loaded file can have them: DKSAVE40 has two) so leaves the
    // entries after it that many records late and the file that many records short of nd, as the original writes it (lockstep oracle).
    // C: the events are written in slot order without the empty slots, then the word 0x19E and the 414 location records, then cache.tmp.
    // C: DS:A67C, which the original recomputes when a hireling joins, is written as read while nobody has joined.
    // DIVERGENCE: the bytes after the place name and the comment are stack left-overs in the original; a new text is followed by zeros.
    // C: the .BSV: the battle-state save R003+0x00DC writes the program's battle file (GameState.BattleFile: the battle's variables and the last
    // battlefield's levels and files); before any battle or battle file (a new game) the variables are the program's zeros.
    public static (byte[] Sav, byte[] Bsv) Write(GameState state, string? comment = null, bool music = true, bool soundEffects = true,
        Core.Battles.Battle? inBattle = null)
    {
        if (inBattle is not null)
        {
            state.Events.Reload();
        }
        var image = state.OriginalSave as SaveImage;
        var file = new SaveFile { Header = image is null ? new byte[SaveFile.HeaderSize] : (byte[])image.Header.Clone() };
        var h = file.Header;
        // orig: the saved game's writer R037+0x2ACE writes the current location's name (or "Wilderness") every time
        // C: so a file whose header named another place (DKSAVE0: "Prag" at cur_loc 85, Pressburg) names cur_loc's after a save, as in the original.
        PutText(h, PlaceAt, PlaceLength, PlaceName(state));
        PutText(h, CommentAt, CommentLength, comment ?? (image is null ? DefaultComment : Text(h, CommentAt, CommentLength)));
        PutWord(h, SeedAt, state.Party.Seed);
        PutWord(h, VersionAt, Version);
        PutWord(h, DateAt, state.Date.Year);
        PutWord(h, DateAt + 2, state.Date.Month);
        PutWord(h, DateAt + 4, state.Date.Day);
        PutWord(h, DateAt + 6, state.Date.Hour);
        PutMoney(h, PurseAt, state.Party.Purse);
        h.AsSpan(ZeroLongAt, 4).Clear();
        PutWord(h, FameAt, state.Party.Fame);
        PutWord(h, LocationAt, state.CurrentLocation);
        PutWord(h, MapXAt, state.MapPosition.X);
        PutWord(h, MapYAt, state.MapPosition.Y);
        PutWord(h, CardAt, state.CurrentCard);
        PutWord(h, SavedCardAt, state.SavedCard);
        PutWord(h, SavedCard2At, state.SavedCard2);
        PutWord(h, PreviousCardAt, state.PreviousCard);
        PutMoney(h, BankNotesAt, state.Party.BankNotes);
        PutWord(h, PhilosophersStoneAt, state.Party.PhilosophersStone);
        // C: DS:A67C, the cached hireling count (GameState.HirelingCount).
        PutWord(h, HirelingsAt, state.HirelingCount);
        // C: bit 7 of the difficulty is set when saving in a battle (a game taken up in a battle not yet begun again is still one).
        h[DifficultyAt] = (byte)((int)state.Difficulty & ~InBattleBit | (inBattle is not null || state.ResumeBattle ? InBattleBit : 0));
        PutWord(h, BattleLocationAt, state.Battlefield.LastLocation);
        PutWord(h, ClearedLevelsAt, state.Battlefield.ClearedLevels);
        // C: the marching order (DS:C624 / DS:C629) is one global: the header and the battle file's variables (+0x1573) are both
        // C: written from it (GameState.MarchingOrder, which a battle keeps up to date and a battle file's copy replaces on loading).
        if (inBattle is not null && state.BattleFile is { } running)
        {
            // C: a battle saved while it runs works on the global itself: the order its variables were just written with is the game's.
            running.Variables.AsSpan(Core.Battles.BattleVariables.MarchingOrder, state.MarchingOrder.Length).CopyTo(state.MarchingOrder);
        }
        state.MarchingOrder.CopyTo(h, MarchingOrderAt);
        h[LeaderAt] = (byte)state.Party.LeaderIndex;
        if (state.BattleFile is { } held)
        {
            state.MarchingOrder.CopyTo(held.Variables, Core.Battles.BattleVariables.MarchingOrder);
            if (inBattle is not null)
            {
                // C: the cleared levels (DS:DA34, with this battle's encounters) and the leader are the battle's while it runs.
                h[ClearedLevelsAt] = held.Variables[Core.Battles.BattleVariables.ClearedLevels];
                h[ClearedLevelsAt + 1] = held.Variables[Core.Battles.BattleVariables.ClearedLevels + 1];
                h[LeaderAt] = (byte)inBattle.Leader;
            }
        }
        PutOption(h, MusicAt, music);
        PutOption(h, SoundAt, soundEffects);
        PutWord(h, MapLeaveModeAt, state.MapLeaveMode);
        h.AsSpan(ZerosAt).Clear();

        state.Roster.Reconcile(state.Party);
        state.Roster.PartySlots.CopyTo(file.PartyIds, 0);
        file.PartySize = PartySizeOf(file.PartyIds);
        file.Images = image is null ? new byte[file.Images.Length] : (byte[])image.Images.Clone();
        file.Palettes = image is null ? new byte[file.Palettes.Length] : (byte[])image.Palettes.Clone();
        for (var slot = 0; slot < SaveFile.PartySlots; slot++)
        {
            // C: member_shield / member_palette keep a departed member's figure (party_delete clears only the status byte)
            var member = slot < state.Party.Members.Count ? state.Party.Members[slot] : state.Party.SpareRecord(slot);
            if (member is null)
            {
                continue;
            }
            PutText(file.Images, slot * SaveFile.ImageSize, SaveFile.ImageSize, member.Image);
            member.ImagePalette.AsSpan(0, Math.Min(member.ImagePalette.Length, SaveFile.PaletteSize))
                .CopyTo(file.Palettes.AsSpan(slot * SaveFile.PaletteSize, SaveFile.PaletteSize));
        }
        // C: `next`: the charactr.tmp record the writer's next read gets.
        var next = 0;
        for (var i = 0; i < state.Roster.Characters.Count; i++)
        {
            var slot = Array.IndexOf(state.Roster.PartySlots, i);
            if (slot >= 0 && slot < state.Party.Members.Count)
            {
                var member = state.Party.Members[slot];
                var fighting = inBattle is not null && slot < Core.Battles.Combatant.PartySlots && inBattle[slot].Member == member;
                // C: `if (party[je][0])`: the status byte of the slot's record in memory (its combatant record in a battle).
                // BUG: OriginalBehaviour.FixSaveWriterSkipsEmptySlots - R037+0x2ACE leaves the record out and writes the full count, so the file is short and the original's loader reads out of step.
                // DIVERGENCE: with the fix (data safety, switchable; on in the player's game) the record of the status-0 slot is written like the others and the count is true.
                if ((fighting ? inBattle![slot].Status : member.Status) == CharacterStatus.Empty && !state.Behaviour.FixSaveWriterSkipsEmptySlots)
                {
                    continue;
                }
                file.Characters.Add(fighting ? PartyRecordInBattle(member, inBattle![slot]) : CharacterRecords.Write(member));
                next++;
            }
            else
            {
                file.Characters.Add(CharacterRecords.Write(state.Roster.StoredRecord(next++)));
            }
        }
        file.CharacterCount = state.Roster.Characters.Count;
        foreach (var (_, e) in state.Events.All())
        {
            file.Events.Add(EventRecords.Write(e));
        }
        if (inBattle is not null)
        {
            state.Events.ReleaseAfterSave();
        }
        // DIVERGENCE: the original always writes 414 records; a state with another number of places (tests) writes what it has.
        foreach (var l in state.Locations)
        {
            file.Locations.Add(LocationRecords.Write(l));
        }
        file.Cache = CacheFile.Write(state.ItemStores);
        // C: a program that has fought no battle and loaded no battle file writes its zero globals (no levels, no work files).
        return (file.ToBytes(), BattleSaveFile.Write(state.BattleFile ?? new Core.Battles.BattleFile()));
    }

    /// <summary>
    /// A party member's record saved in a battle: the record is its combatant record in the original, so what the battle
    /// changed (status, attributes, equipment, potions, the pack) and where it stands and what it does are in it.
    /// </summary>
    // orig: R037+0x2ACE (1462:2ACE) writes the slot's combatant record (0x80 bytes at DS:9C55), saints, formulas and pack for a party member
    // UNCERTAIN: the record words the model does not keep for a combatant (+0x02, +0x0E the pointer R002+0x1830 sets, +0x1C) are
    // written as the record was read.
    private static byte[] PartyRecordInBattle(Character member, Core.Battles.Combatant c)
    {
        var copy = member.Copy();
        copy.Status = c.Status;
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            copy.Attributes[k] = c.Attributes[k];
            copy.AttributeMaximums[k] = c.Maximums[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            copy.Skills[k] = c.Skills[k];
        }
        // C: the pack is the member's 64 entries the battle works on (entries past the count included).
        copy.Pack.CopyFrom(c.Items);
        var record = CharacterRecords.Write(copy);
        CombatantRecords.Write(c, record);
        CombatantRecords.WriteAnimation(c, record);
        return record;
    }

    // C: `if (cur_loc >= 0 && cur_loc < 0x19e) _fstrcpy(placeb, locations[cur_loc] + 0x26); else strcpy(placeb, "Wilderness");`
    private static string PlaceName(GameState state) =>
        state.CurrentLocation >= 0 && state.CurrentLocation < SaveFile.LocationCount && state.CurrentLocation < state.Locations.Count
            ? state.Locations[state.CurrentLocation].Name
            : Wilderness;

    /// <summary>The saved game the state was read from or last saved to ("DKSAVE3.SAV"), or null.</summary>
    public static string? FileNameOf(GameState state) => (state.OriginalSave as SaveImage)?.FileName;

    /// <summary>
    /// After a save to <paramref name="fileName"/>: the written bytes become what the state is compared
    /// with next time (DS:A680, the loaded game's name, is the new file's).
    /// </summary>
    // orig: `strcpy(DS:A680, "dksave"); itoa(--kh, numk, 10); strcat(DS:A680, numk); strcat(DS:A680, ".sav");` in R037+0x2ACE
    public static void Saved(GameState state, string fileName, byte[] sav)
    {
        var file = SaveFile.Parse(sav);
        state.OriginalSave = new SaveImage
        {
            FileName = fileName,
            Header = file.Header,
            Images = file.Images,
            Palettes = file.Palettes,
        };
    }

    // C: `party_size.v = ixn = 0; for (; ixn < 5; ixn++) if (idso[ixn] != -1) party_size.v = ixn + 1;`
    private static int PartySizeOf(int[] ids)
    {
        var size = 0;
        for (var slot = 0; slot < ids.Length; slot++)
        {
            if (ids[slot] != -1)
            {
                size = slot + 1;
            }
        }
        return size;
    }

    private static Money ReadMoney(ReadOnlySpan<byte> h, int at) => new((short)Word(h, at), (short)Word(h, at + 2), (short)Word(h, at + 4));

    private static void PutMoney(Span<byte> h, int at, Money m)
    {
        PutWord(h, at, m.Florins);
        PutWord(h, at + 2, m.Groschen);
        PutWord(h, at + 4, m.Pfennigs);
    }

    // C: the options are 1 (on) or 0 (off, toggled with ^= 1); the loader tests == 1. A value that already reads right is kept.
    private static void PutOption(Span<byte> h, int at, bool on)
    {
        if ((h[at] == 1) != on)
        {
            h[at] = (byte)(on ? 1 : 0);
        }
    }

    /// <summary>What the state keeps of the file it was read from.</summary>
    private sealed class SaveImage
    {
        public required string FileName { get; init; }

        public required byte[] Header { get; init; }

        public required byte[] Images { get; init; }

        public required byte[] Palettes { get; init; }
    }
}
