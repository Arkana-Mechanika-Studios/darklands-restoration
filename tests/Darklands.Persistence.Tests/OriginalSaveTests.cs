using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Persistence.Tests;

/// <summary>
/// The original's saved games: every save of the player's SAVES folder (DARKLANDS_DIR, or
/// the "game" key of local/paths.json; copied to a temporary folder, never written) is read into a game and written
/// back byte for byte, and the fields land where the original keeps them.
/// </summary>
public sealed class OriginalSaveTests
{
    public static TheoryData<string> Saves()
    {
        var data = new TheoryData<string>();
        foreach (var name in SaveFolderCopy.Names)
        {
            data.Add(name);
        }
        if (data.Count == 0)
        {
            data.Add("");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void EverySaveComesBackByteForByte(string name)
    {
        if (name.Length == 0)
        {
            return;
        }
        var sav = SaveFolderCopy.Read(name);
        var bsvName = Path.ChangeExtension(name, ".bsv");
        var bsv = SaveFolderCopy.Exists(bsvName) ? SaveFolderCopy.Read(bsvName) : [];

        Assert.Equal(sav, SaveFile.Parse(sav).ToBytes());

        var loaded = OriginalSaves.Read(sav, bsv, name, NoGameData.Instance);
        var (written, battle) = OriginalSaves.Write(loaded.State, null, loaded.Music, loaded.SoundEffects);
        // The original's writer names the current place in the header every time; a file that named another place (DKSAVE0, DKSAVE1:
        // "Prag" while the party is in Pressburg) comes back naming the party's place, and every other byte as it was read, except
        // what the writer recomputes from a file another tool wrote (DKSAVE40: AsTheOriginalWritesIt).
        var state = loaded.State;
        var place = state.CurrentLocation >= 0 && state.CurrentLocation < state.Locations.Count
            ? state.Locations[state.CurrentLocation].Name
            : OriginalSaves.Wilderness;
        var nameEnd = Array.IndexOf(written, (byte)0, 0, 0x15);
        Assert.Equal(place, System.Text.Encoding.Latin1.GetString(written, 0, nameEnd < 0 ? 0x15 : nameEnd));
        var expectedSav = AsTheOriginalWritesIt(sav);
        if (!sav.AsSpan(0, 0x15).SequenceEqual(written.AsSpan(0, 0x15)))
        {
            written.AsSpan(0, 0x15).CopyTo(expectedSav);
        }
        Assert.True(expectedSav.AsSpan().SequenceEqual(written), $"{name}: {sav.Length} bytes read, {written.Length} written, first difference at 0x{FirstDifference(expectedSav, written):X}");
        if (bsv.Length > 0)
        {
            // The battle file comes back as the original writes it after a load: a work file saved empty was removed by the
            // restore, so its raw size becomes -1 (DKSAVE17, DKSAVE18, Dksave10, Dksave22: the loot file); the rest byte for byte.
            var expected = (byte[])bsv.Clone();
            foreach (var word in new[] { 17, 19 })
            {
                if (expected[2 * word] == 0 && expected[2 * word + 1] == 0)
                {
                    expected[2 * word] = expected[2 * word + 1] = 0xFF;
                }
            }
            Assert.Equal(expected, battle);
        }
        else
        {
            // No battle file (DKSAVE41): the restore leaves the program's battle globals as they were, here the program start's.
            Assert.Equal(BattleSaveFile.Write(new Core.Battles.BattleFile()), battle);
        }
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void EveryBattleFileIsWrittenBackByteForByte(string name)
    {
        var bsvName = name.Length == 0 ? "" : Path.ChangeExtension(name, ".bsv");
        if (name.Length == 0 || !SaveFolderCopy.Exists(bsvName))
        {
            return;
        }
        // The program's reader and writer (the restore's and the save's layout, the PKWARE implode of overlay 3) give the
        // original's bytes: each block imploded again by a compressor whose work area starts empty is the block the original wrote.
        var bsv = SaveFolderCopy.Read(bsvName);
        Assert.Equal(bsv, BattleSaveFile.Write(BattleSaveFile.Parse(bsv).ToBattleFile(keepEmptyFiles: true)));
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void EveryBattleFileExplodes(string name)
    {
        var bsvName = name.Length == 0 ? "" : Path.ChangeExtension(name, ".bsv");
        if (name.Length == 0 || !SaveFolderCopy.Exists(bsvName))
        {
            return;
        }
        var battle = BattleSaveFile.Parse(SaveFolderCopy.Read(bsvName));
        Assert.Equal(BattleSaveFile.VariablesSize, battle.Variables.Length);
        Assert.Equal(battle.Variables[BattleSaveFile.LevelCountAt], battle.Levels.Count);
        Assert.False(battle.SavedInBattle);
        var header = SaveFile.Parse(SaveFolderCopy.Read(name)).Header;
        // The battle file holds the marching order and cleared levels the header holds.
        Assert.Equal(header.AsSpan(0x9B, 6).ToArray(), battle.MarchingOrder.ToArray());
        Assert.Equal(BitConverter.ToInt16(header, 0x99), battle.ClearedLevels);
        foreach (var level in battle.Levels)
        {
            Assert.Equal(BattlefieldImageSize, level.Floor.Image.Length);
            Assert.Equal(level.Activations[0] * 30 + 1, level.Activations.Length);
            Assert.Equal(level.Enemies[0] * 0x122 + 1, level.Enemies.Length);
        }
    }

    private const int BattlefieldImageSize = 0x33FC;

    /// <summary>
    /// A saved game as the original's writer (R037+0x2ACE) writes it straight after loading it: party_size recounted from the slots,
    /// and a party slot whose status is 0 written as nothing, without the charactr.tmp read past it, so the entries after it come that
    /// many records late and the file that many records short of the count (DKSAVE40, from another tool: party_size 6 for four slots,
    /// slots 0 and 1 of status 0; written by 483.07 in the lockstep oracle with party_size 4 and records 0, 1, 2, 3, 4, 7 of 8).
    /// A file the original wrote itself comes back as it was.
    /// </summary>
    private static byte[] AsTheOriginalWritesIt(byte[] sav)
    {
        var file = SaveFile.Parse(sav);
        var records = file.Characters.ToList();
        file.PartySize = Array.FindLastIndex(file.PartyIds, id => id != -1) + 1;
        file.CharacterCount = records.Count;
        file.Characters.Clear();
        var next = 0;
        for (var i = 0; i < records.Count; i++)
        {
            var slot = Array.IndexOf(file.PartyIds, i);
            if (slot < 0)
            {
                file.Characters.Add(records[next++]);
            }
            else if (records[i][0x14] != 0)
            {
                file.Characters.Add(records[i]);
                next++;
            }
        }
        return file.ToBytes();
    }

    [Fact]
    public void APartySlotOfStatusZeroIsSkippedAndTheRosterRecordsAfterItComeLate()
    {
        // The original's writer (R037+0x2ACE): `if (party[je][0])` writes the member from memory and reads one charactr.tmp record past
        // it; a slot of status 0 writes nothing and reads nothing, so the bench entry after it gets the record charactr.tmp holds for
        // member 3 (as it was written there) and the file says five records where four follow.
        var state = SampleGame();
        var bench = DefaultParty.Create(seed: 78).Members[0];
        state.Roster.Characters.Add(bench);
        var stored = CharacterRecords.Write(state.Party.Members[3]);
        state.Party.Members[3].Attributes[0]++;
        state.Party.Members[1].Status = CharacterStatus.Empty;
        var sav = OriginalSaves.Write(state).Sav;
        byte[] Record(int k) => sav[(0x189 + k * SaveFile.CharacterSize)..(0x189 + (k + 1) * SaveFile.CharacterSize)];
        Assert.Equal(4, BitConverter.ToInt16(sav, 0xEF));
        Assert.Equal(5, BitConverter.ToInt16(sav, 0xF1));
        Assert.Equal(CharacterRecords.Write(state.Party.Members[0]), Record(0));
        Assert.Equal(CharacterRecords.Write(state.Party.Members[2]), Record(1));
        Assert.Equal(CharacterRecords.Write(state.Party.Members[3]), Record(2));
        Assert.Equal(stored, Record(3));
        // then the events: their count word follows the fourth record
        Assert.Equal(state.Events.All().Count(), BitConverter.ToInt16(sav, 0x189 + 4 * SaveFile.CharacterSize));
    }

    [Fact]
    public void AFileShortOfItsRecordsIsReadOutOfStepLikeTheOriginalWithoutFailing()
    {
        // The original's loader (R037+0x8ED4) reads as many character records as the count says and then the location table as one
        // block of 414 records whatever its count word holds. A file written with a status-0 party slot is records short of its count
        // (DKSAVE40's re-save, as 483.07 writes it: two short), so the load reads into the events and takes the block from there on.
        // 483.07 loads that file (lockstep oracle, its own re-save of DKSAVE40) and goes on; the rewrite must not fail on it either.
        var name = SaveFolderCopy.Names.FirstOrDefault(n => n.Equals("DKSAVE40.SAV", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return;
        }
        var bsv = SaveFolderCopy.Read(Path.ChangeExtension(name, ".BSV"));
        var first = OriginalSaves.Read(SaveFolderCopy.Read(name), bsv, name, NoGameData.Instance).State;
        var shortSave = OriginalSaves.Write(first).Sav;
        Assert.Equal(8, BitConverter.ToInt16(shortSave, 0xF1));

        var loaded = OriginalSaves.Read(shortSave, bsv, name, NoGameData.Instance).State;

        Assert.Equal(SaveFile.LocationCount, loaded.Locations.Count);
        Assert.Equal(8, loaded.Roster.Characters.Count);
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }
        return n;
    }

    [Fact]
    public void TheQuickstartColoursAreThoseOfSavesDefault()
    {
        // The program's own colours (R001+0x0797) and the block SAVES\DEFAULT keeps at 0x111 are the same 120 bytes.
        if (!SaveFolderCopy.Exists("DEFAULT"))
        {
            return;
        }
        var file = SaveFile.Parse(SaveFolderCopy.Read("DEFAULT"));
        for (var slot = 0; slot < SaveFile.PartySlots; slot++)
        {
            Assert.Equal(DefaultParty.Colours[slot], file.Palettes[(slot * SaveFile.PaletteSize)..((slot + 1) * SaveFile.PaletteSize)]);
        }
    }

    [Fact]
    public void TheHeaderFieldsLandInTheGame()
    {
        if (!SaveFolderCopy.Exists("DKSAVE12.SAV"))
        {
            return;
        }
        var loaded = OriginalSaves.Read(SaveFolderCopy.Read("DKSAVE12.SAV"), SaveFolderCopy.Read("DKSAVE12.BSV"), "DKSAVE12.SAV", NoGameData.Instance);
        var s = loaded.State;
        Assert.Equal("AskJob", loaded.Comment);
        Assert.Equal(new GameDate(11, 16, 5, 1401), s.Date);
        Assert.Equal(6070, s.Party.Seed);
        Assert.Equal(48, s.CurrentLocation);
        Assert.Equal("Koblenz", s.Locations[48].Name);
        Assert.Equal(414, s.Locations.Count);
        Assert.Equal(53, s.Events.All().Count());
        Assert.Equal(8, s.Roster.Characters.Count);
        Assert.Equal([4, 5, 6, 7, -1], s.Roster.PartySlots);
        Assert.Equal(4, s.Party.Members.Count);
        // The party's members are the roster's records of their slots.
        for (var slot = 0; slot < 4; slot++)
        {
            Assert.Same(s.Roster.Characters[s.Roster.PartySlots[slot]], s.Party.Members[slot]);
        }
        Assert.Equal(116, s.Battlefield.LastLocation);
        Assert.Equal(13, s.Battlefield.ClearedLevels);
        Assert.Equal(Difficulty.Standard, s.Difficulty);
    }

    [Fact]
    public void ARecordsFieldsAreTheCharactersAndTheRestIsKept()
    {
        var record = new byte[CharacterRecords.Size];
        new Random(7).NextBytes(record);
        record[0x14] = 1;
        record[0x15] = (byte)'C';
        record[0x17] = 1;
        "Ebhard of Achdorf\0"u8.CopyTo(record.AsSpan(0x25));
        "Ebhard\0"u8.CopyTo(record.AsSpan(0x3E));
        record[0x7E] = 3;
        record[0x7F] = 0;
        record[0x80] = 0x90;          // saints 0 and 3
        record[0x94] = 0xFD;          // potion 0: grades 0 and 2, five unknown bits
        record[0x51] = 0xFF;          // no weapon

        var c = CharacterRecords.Read(record);
        Assert.Equal("Ebhard of Achdorf", c.Name);
        Assert.Equal("Ebhard", c.ShortName);
        Assert.Equal(CharacterStatus.Able, c.Status);
        Assert.Equal('C', c.ShieldLetter);
        Assert.True(c.IsFemale);
        Assert.Equal(BitConverter.ToInt16(record, 0x12), c.Age);
        Assert.Equal(BitConverter.ToInt16(record, 0x00), c.RosterLocation);
        Assert.Equal(3, c.Pack.Count);
        Assert.Equal(new ItemStack(BitConverter.ToUInt16(record, 0xAA), record[0xAC], record[0xAD], record[0xAE], record[0xAF]), c.Pack[0]);
        Assert.Contains(0, c.Saints);
        Assert.Contains(3, c.Saints);
        Assert.Contains(0, c.Formulas);
        Assert.Contains(2, c.Formulas);
        Assert.DoesNotContain(1, c.Formulas);
        Assert.True(c.Weapon.IsEmpty);
        Assert.Equal(record[0x5D + 2], c[Core.Characters.Attribute.Agility]);
        Assert.Equal(record[0x6B + (int)Skill.Healing], c[Skill.Healing]);
        Assert.Equal(record, CharacterRecords.Write(c));

        c.Saints.Add(9);
        c.Formulas.Remove(2);
        c.Pack.RemoveAt(2);
        c.ShortName = "Ebby";
        c.Weapon = new EquippedItem(17, 30);
        var again = CharacterRecords.Write(c);
        Assert.Equal(0x40, again[0x81] & 0x40);
        Assert.Equal(0xF9, again[0x94]);
        Assert.Equal(2, again[0x7E]);
        Assert.Equal("Ebby\0\0\0\0\0\0"u8.ToArray(), again.AsSpan(0x3E, 10).ToArray());
        Assert.Equal((17, 30), (again[0x51], again[0x58]));
        // The bytes the model does not name are as they were.
        Assert.Equal(record.AsSpan(0x02, 0x10).ToArray(), again.AsSpan(0x02, 0x10).ToArray());
        // item_delete moved the stale entry 3 down into entry 2 and left entry 3 as it was.
        Assert.Equal(record.AsSpan(0xAA + 3 * 6, 6).ToArray(), again.AsSpan(0xAA + 2 * 6, 6).ToArray());
        Assert.Equal(record.AsSpan(0xAA + 3 * 6, 0x22A - 0xAA - 3 * 6).ToArray(), again.AsSpan(0xAA + 3 * 6).ToArray());
    }

    [Fact]
    public void AnEventsWordsAreItsFields()
    {
        var e = new TimedEvent
        {
            Kind = EventKinds.Quest, Type = 5, Owner = -2, Location = 48, Value = 17, Extra = 3,
            Flags = EventFlags.CallExpiryHandler, ExpiryHandler = (ExpiryHandler)3, Tag = 0x1D, Data1 = -4, Data2 = 6, Data3 = 0x7F,
            Created = new GameDate(9, 3, 4, 1401), Start = new GameDate(10, 3, 4, 1401), End = EventLog.ForeverEnd,
        };
        var r = EventRecords.Write(e);
        Assert.Equal(5, BitConverter.ToInt16(r, 0x00));
        Assert.Equal(9, BitConverter.ToInt16(r, 0x02));
        Assert.Equal(1401, BitConverter.ToInt16(r, 0x08));
        Assert.Equal(1499, BitConverter.ToInt16(r, 0x18));
        Assert.Equal(-2, BitConverter.ToInt16(r, 0x1A));
        Assert.Equal(48, BitConverter.ToInt16(r, 0x1C));
        Assert.Equal(EventKinds.Quest, BitConverter.ToInt16(r, 0x22));
        Assert.Equal(0x7F, BitConverter.ToInt16(r, 0x2E));
        var back = EventRecords.Read(r);
        Assert.Equal(e.ToString(), back.ToString());
        Assert.Equal((e.Data1, e.Data2, e.Data3, e.Extra, e.Value, e.Created), (back.Data1, back.Data2, back.Data3, back.Extra, back.Value, back.Created));
    }

    [Fact]
    public void TheItemStoresAreAppendedAfterTheEmptyHeader()
    {
        var stores = new ItemStores();
        Assert.Equal(198, CacheFile.Write(stores).Length);
        stores.Add([new StoredItem(0x10A, 25, 3), new StoredItem(5, 40, 1)]);
        stores.Add([new StoredItem(7, 30, 255)]);
        var file = CacheFile.Write(stores);
        Assert.Equal(0x63, file[0]);
        Assert.Equal(2, file[1]);
        Assert.Equal(198, BitConverter.ToUInt16(file, 2));
        Assert.Equal(198 + 1 + 8, BitConverter.ToUInt16(file, 4));
        var back = CacheFile.Read(file);
        Assert.Equal(stores.All.Select(s => s.ToList()), back.All.Select(s => s.ToList()));
    }

    [Fact]
    public void ANewGameIsWrittenInTheOriginalsLayout()
    {
        var state = SampleGame();
        var (sav, bsv) = OriginalSaves.Write(state, "a test", music: false, soundEffects: true);
        // A new game has fought no battle: its battle file holds the program start's zero variables.
        Assert.Equal(BattleSaveFile.Write(new Core.Battles.BattleFile()), bsv);
        var file = SaveFile.Parse(sav);
        Assert.Equal("Koln", System.Text.Encoding.Latin1.GetString(file.Header, 0, 4));
        Assert.Equal(0, file.Header[4]);
        Assert.Equal("a test", System.Text.Encoding.Latin1.GetString(file.Header, 0x15, 6));
        Assert.Equal(7, BitConverter.ToInt16(file.Header, 0x66));
        Assert.Equal(1401, BitConverter.ToInt16(file.Header, 0x68));
        Assert.Equal(123, BitConverter.ToInt16(file.Header, 0x8C));
        Assert.Equal((0, 1), (file.Header[0xA2], file.Header[0xA3]));
        Assert.Equal(4, file.PartySize);
        Assert.Equal([0, 1, 2, 3, -1], file.PartyIds);
        Assert.Equal("F60\0"u8.ToArray(), file.Images[..4]);
        Assert.Equal(4, file.Characters.Count);
        Assert.Single(file.Events);
        Assert.Equal(198, file.Cache.Length);

        var loaded = OriginalSaves.Read(sav, [], "DKSAVE0.SAV", NoGameData.Instance);
        var s = loaded.State;
        Assert.False(loaded.Music);
        Assert.True(loaded.SoundEffects);
        Assert.Equal("a test", loaded.Comment);
        Assert.Equal(state.Date, s.Date);
        Assert.Equal(state.Party.Purse, s.Party.Purse);
        Assert.Equal(state.Party.BankNotes, s.Party.BankNotes);
        Assert.Equal(state.Party.Members.Select(m => m.Name), s.Party.Members.Select(m => m.Name));
        Assert.Equal(state.Party.Members[2].Pack, s.Party.Members[2].Pack);
        Assert.Equal(state.Party.Members[3].Saints.Order(), s.Party.Members[3].Saints.Order());
        Assert.Equal(state.Party.Members[2].Formulas.Order(), s.Party.Members[2].Formulas.Order());
        Assert.Equal(17, s.Events[0]!.Value);
        Assert.Equal(5, s.Locations[0].Reputation);
        Assert.Equal(40, s.Locations[0].LocalValues[1]);
        Assert.Equal(3, s.MapLeaveMode);
        Assert.Equal("DKSAVE0.SAV", OriginalSaves.FileNameOf(s));
        Assert.Equal(sav, OriginalSaves.Write(s, null, false, true).Sav);
    }

    [Fact]
    public void ARosterCharacterLeftAtAnInnStaysInTheFile()
    {
        var state = SampleGame();
        var gunther = state.Party.Members[1];
        gunther.Status = CharacterStatus.Empty;
        gunther.RosterLocation = 12;
        state.Roster.LeaveParty(1);
        var file = SaveFile.Parse(OriginalSaves.Write(state).Sav);
        Assert.Equal([0, -1, 2, 3, -1], file.PartyIds);
        Assert.Equal(4, file.Characters.Count);
        Assert.Equal(12, BitConverter.ToInt16(file.Characters[1], 0));
        Assert.Equal(0, file.Characters[1][0x14]);

        var back = OriginalSaves.Read(file.ToBytes(), [], "x.sav", NoGameData.Instance).State;
        Assert.Equal(CharacterStatus.Empty, back.Party.Members[1].Status);
        Assert.Equal("Gunther Langer", back.Roster.Characters[1].Name);
    }

    [Fact]
    public void TheMarchingOrderIsTheHeadersSixBytesAndTheMembersRecordsStayAsRead()
    {
        var state = SampleGame();
        byte[] order = [2, 0, 1, 3, 0, 4];
        order.CopyTo(state.MarchingOrder, 0);
        var sav = OriginalSaves.Write(state).Sav;
        Assert.Equal(order, SaveFile.Parse(sav).Header[0x9B..0xA1]);

        var back = OriginalSaves.Read(sav, [], "x.sav", NoGameData.Instance).State;
        Assert.Equal(order, back.MarchingOrder);
        // charactr.tmp's record of a member is the one read, not the member as he changes
        var member = back.Party.Members[0];
        Assert.Same(member, back.Roster.Characters[0]);
        member.Skills[0] = 99;
        Assert.NotEqual(99, back.Roster.StoredRecord(0).Skills[0]);
        Assert.Equal(99, SaveFile.Parse(OriginalSaves.Write(back).Sav).Characters[0][0x6B]);  // a save takes the member from memory
    }

    [Fact]
    public void ASavedGameWithoutBattleFileKeepsTheProgramsOwn()
    {
        if (!SaveFolderCopy.Exists("DKSAVE12.SAV") || !SaveFolderCopy.Exists("DKSAVE41.SAV"))
        {
            return;
        }
        // DKSAVE41 has no battle file: the restore returns at once and the program's battle variables stay (here DKSAVE12's).
        var first = OriginalSaves.Read(SaveFolderCopy.Read("DKSAVE12.SAV"), SaveFolderCopy.Read("DKSAVE12.BSV"), "DKSAVE12.SAV", NoGameData.Instance);
        var second = OriginalSaves.Read(SaveFolderCopy.Read("DKSAVE41.SAV"), [], "DKSAVE41.SAV", NoGameData.Instance, first.State);
        Assert.NotNull(second.State.BattleFile);
        Assert.Equal(first.State.BattleFile!.Variables, second.State.BattleFile!.Variables);
        Assert.Same(first.State.BattleFile.Compressor, second.State.BattleFile.Compressor);
        Assert.NotNull(OriginalSaves.Write(second.State).Bsv);
        // Read in a program that holds no battle file, it is saved with the program start's (zero variables, no levels, no work files).
        var fresh = BattleSaveFile.Parse(OriginalSaves.Write(OriginalSaves.Read(SaveFolderCopy.Read("DKSAVE41.SAV"), [], "DKSAVE41.SAV", NoGameData.Instance).State).Bsv);
        // C: but for the marching order, one global with the header's (+0x9B): the battle file's copy (+0x1573) is the same six bytes.
        var loaded41 = OriginalSaves.Read(SaveFolderCopy.Read("DKSAVE41.SAV"), [], "DKSAVE41.SAV", NoGameData.Instance).State;
        Assert.Equal(loaded41.MarchingOrder, fresh.Variables[0x1573..0x1579]);
        Assert.All(fresh.Variables.Take(0x1573).Concat(fresh.Variables.Skip(0x1579)), v => Assert.Equal(0, v));
        Assert.Empty(fresh.Levels);
        Assert.Null(fresh.File1162);
        Assert.Null(fresh.File1164);
        Assert.Equal(0xFFFF, fresh.HeaderWords[17]);
        Assert.Equal(0xFFFF, fresh.HeaderWords[19]);
    }

    [Fact]
    public void TheWildernessCityRecordIsTheFilesFirstBytes()
    {
        var data = new FakeCities();
        var state = SampleGame();
        state.CurrentLocation = GameState.Wilderness;
        var (sav, _) = OriginalSaves.Write(state);
        Assert.Equal("Wilderness", System.Text.Encoding.Latin1.GetString(sav, 0, 10));
        var s = OriginalSaves.Read(sav, [], "x.sav", data).State;
        Assert.Equal(FakeCities.Count, s.City.Byte(0));
        Assert.Equal(data.LoadCity(0).ToArray()[..(CityRecord.Size - 1)], s.City.ToArray()[1..]);

        state.CurrentLocation = 200;
        var running = new GameState(new MscRandom(3)) { Data = data, City = data.LoadCity(7), CurrentLocation = 7 };
        running.CurrentLocation = 200;
        var village = OriginalSaves.Read(OriginalSaves.Write(state).Sav, [], "x.sav", data, running).State;
        Assert.Equal(7, village.City.City);
        Assert.Same(running.Random, village.Random);
    }

    private sealed class FakeCities : IGameData
    {
        public const int Count = 92;

        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; } = [];

        public CityRecord LoadCity(int city) => new(city, Enumerable.Range(0, CityRecord.Size).Select(i => (byte)(i + city)).ToArray());

        public byte[]? CityFileBytes(long offset, int length)
        {
            var file = new[] { (byte)Count }.Concat(Enumerable.Range(0, Count).SelectMany(c => LoadCity(c).ToArray())).ToArray();
            return offset >= file.Length ? [] : file.Skip((int)offset).Take(length).ToArray();
        }
    }

    private static GameState SampleGame()
    {
        var state = new GameState(new MscRandom(1234)) { Party = DefaultParty.Create(seed: 77), Date = new GameDate(9, 3, 4, 1401) };
        state.Roster.Reset(state.Party);
        state.Party.BankNotes = Money.From(123, 4, 5);
        state.Locations.Add(new Location { Index = 0, Name = "Koln", X = 10, Y = 20, Reputation = 5 });
        state.Locations[0].LocalValues[1] = 40;
        state.CurrentLocation = 0;
        state.CurrentCard = 0x29;
        state.MapLeaveMode = 3;
        state.Events.Put(12, new TimedEvent { Kind = EventKinds.Quest, Value = 17, End = EventLog.ForeverEnd });
        return state;
    }
}

/// <summary>A copy of the player's SAVES folder in a temporary folder (the original folder is only read).</summary>
internal static class SaveFolderCopy
{
    private static readonly Lazy<string?> Folder = new(Copy);

    public static IReadOnlyList<string> Names =>
        Folder.Value is { } folder
            ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Where(n => string.Equals(Path.GetExtension(n), ".sav", StringComparison.OrdinalIgnoreCase) || n == "DEFAULT").Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public static bool Exists(string name) => Folder.Value is { } folder && Find(folder, name) is not null;

    public static byte[] Read(string name) => File.ReadAllBytes(Find(Folder.Value!, name) ?? throw new FileNotFoundException(name));

    private static string? Find(string folder, string name) =>
        Directory.GetFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

    private static string? Copy()
    {
        var game = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        var saves = Path.Combine(game, "SAVES");
        if (!Directory.Exists(saves))
        {
            return null;
        }
        var copy = TempCopies.Create("darklands-saves-");
        foreach (var f in Directory.GetFiles(saves))
        {
            File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
        }
        return copy;
    }
}
