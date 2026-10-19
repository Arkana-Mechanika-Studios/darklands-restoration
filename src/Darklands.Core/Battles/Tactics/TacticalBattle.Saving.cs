using Darklands.Core.Battles.Generation;
using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The battle-state save and restore of overlay 3: the battle written into the program's battle file (its variables,
/// the battlefield's levels, terrain.fil and the loot file) and a battle taken up again from one, through the start
/// of the fight the original runs for a game saved in a battle.
/// </summary>
public sealed partial class TacticalBattle
{
    private const int TriggerCount = 0x2184, TriggerHandlers = 0x218C, TriggerSize = 14;

    /// <summary>DS:C1F0: the battlefield type the battle was ordered with (the battle file keeps it).</summary>
    public int BattlefieldType { get; set; }

    /// <summary>DS:D6CE: the battle seed (the battle file keeps it).</summary>
    public int Seed { get; set; }

    /// <summary>DS:C4FD: an enemy has seen a member (set by the enemies' sight test, read by nothing but the battle file).</summary>
    public bool EnemySawMember { get; private set; }

    /// <summary>
    /// Writes the battle into the program's battle file: the variables (bytes this model does not name keep what the file
    /// held: the last battle's or the last load's), each level's floor image (object functions as the numbers the file
    /// uses), activations and enemy kinds, terrain.fil and the loot file.
    /// </summary>
    /// <param name="inBattle">DS:907A: 1 while the battle runs (a save inside it), 0 once it is over.</param>
    /// <param name="clearedLevels">DS:DA34 before this battle (the battlefield memory): this battle's encounters are added.</param>
    /// <param name="lastCastleFlags">DS:160C (GameState.LastCastleFlags).</param>
    // orig: the battle-state save R003+0x00DC (0E76:00DC) draft (the serialisation and the level loop); the level switch
    // R024+0x059A and R014+0x0694 write the shown level's .flr/.atv when it changed (the levels are kept in memory here, so they are
    // always current); tests: BattleSaveTests
    // DIVERGENCE: the bytes the level loader leaves in each .enm record from its buffer's earlier content, and the per-combatant
    // tables' bytes left by earlier battles (path tails, slots not used since), are this program's (zeros) for a battlefield made here.
    // C: the function pointers of a level's objects are written as numbers: the secret door search 0 (a null pointer), thunk 09C0:17F7 1,
    // the spell trap 2, the missile trap 4, thunk 09C0:181F 5, thunk 09C0:1815 6, thunk 09C0:1833 7, thunk 09C0:1829 8 (as longs); another value is left as it is.
    public void WriteBattleFile(BattleFile file, bool inBattle, int clearedLevels, int lastCastleFlags)
    {
        var v = file.Variables;
        v[BattleVariables.InBattle] = (byte)(inBattle ? 1 : 0);
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            c.Path.CopyTo(v, BattleVariables.Paths + i * Combatant.PathLength);
            v[BattleVariables.PathIndex + i] = (byte)c.PathIndex;
            v[BattleVariables.Moving + i] = Flag(c.Moving);
            v[BattleVariables.Exchange + i] = (byte)c.Exchange;
            v[BattleVariables.State + i] = (byte)c.State;
            v[BattleVariables.AttackFacing + i] = (byte)c.AttackFacing;
            v[BattleVariables.DamageShown + i] = (byte)c.DamageShown;
            v[BattleVariables.DamageNumber + i] = (byte)c.DamageNumber;
            v[BattleVariables.WasHit + i] = Flag(c.WasHit);
            v[BattleVariables.Floor + i] = (byte)c.Floor;
            v[BattleVariables.GaveUp + i] = Flag(c.GaveUp);
            v[BattleVariables.EngagedByAi + i] = Flag(c.EngagedByAi);
            PutWord(v, BattleVariables.BlockedBy + 2 * i, c.BlockedBy);
            PutWord(v, BattleVariables.Unused1641 + 2 * i, c.SavedWord);
            PutWord(v, BattleVariables.Flags + 2 * i, (int)c.Flags);
            v[BattleVariables.Place + i] = (byte)c.Place;
            v[BattleVariables.PlacedBaseLoad + i] = (byte)c.PlacedBaseLoad;
            for (var k = 0; k < 3; k++)
            {
                PutWord(v, BattleVariables.Orders + 6 * i + 2 * k, (int)c.Orders[k]);
            }
            PutWord(v, BattleVariables.NextStepAt + 2 * i, c.NextStepAt);
            PutWord(v, BattleVariables.NextFrameAt + 2 * i, c.NextFrameAt);
            PutWord(v, BattleVariables.EngageAt + 2 * i, c.EngageAt);
            PutWord(v, BattleVariables.RepathAt + 2 * i, c.RepathAt);
            PutWord(v, BattleVariables.GiveUpUntil + 2 * i, c.GiveUpUntil);
            PutWord(v, BattleVariables.ReloadStartAt + 2 * i, c.ReloadStartAt);
            PutWord(v, BattleVariables.AimStartAt + 2 * i, c.AimStartAt);
            PutWord(v, BattleVariables.AimEndAt + 2 * i, c.AimEndAt);
            PutWord(v, BattleVariables.FlashUntil + 2 * i, c.FlashUntil);
            PutWord(v, BattleVariables.DamageNumberUntil + 2 * i, c.DamageNumberUntil);
            PutWord(v, BattleVariables.RecoverAt + 2 * i, c.RecoverAt);
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            v[BattleVariables.MemberDoor + i] = (byte)c.Door;
            v[BattleVariables.MemberDoorOpens + i] = Flag(c.DoorOpens);
            v[BattleVariables.MemberDoorSide + i] = (byte)c.DoorSide;
            v[BattleVariables.MemberStairs + i] = (byte)c.StairsExit;
            v[BattleVariables.MemberChest + i] = (byte)c.Chest;
            v[BattleVariables.MemberDissolveSlot + i] = (byte)c.DissolveSlot;
            v[BattleVariables.MemberExitZone + i] = (byte)c.ExitZone;
            v[BattleVariables.MemberStairsBack + i] = (byte)c.StairsBack;
            // C: DS:D68D is a word per member; the battle keeps bits 0..6 (the rest is kept as the file held it).
            v[BattleVariables.SkillsUsed + 2 * i] = (byte)((v[BattleVariables.SkillsUsed + 2 * i] & 0x80) | ((int)c.SkillsUsed & 0x7F));
        }
        InitialSpotRecord.CopyTo(v, BattleVariables.InitialSpot);
        v[BattleVariables.IgnorePeople] = Flag(Map.IgnorePeople);
        var loot = Store.Loot;
        for (var k = 0; k < loot.ChestOffsets.Length; k++)
        {
            PutWord(v, BattleVariables.ChestOffsets + 2 * k, loot.ChestOffsets[k]);
        }
        Array.Clear(v, BattleVariables.Piles, BattleVariables.MaxPiles * BattleVariables.PileSize);
        for (var k = 0; k < loot.Piles.Count && k < BattleVariables.MaxPiles; k++)
        {
            var p = loot.Piles[k];
            var at = BattleVariables.Piles + BattleVariables.PileSize * k;
            PutWord(v, at, p.Offset);
            v[at + 2] = (byte)p.Count;
            v[at + 3] = (byte)p.Level;
            PutWord(v, at + 4, p.Position.X);
            PutWord(v, at + 6, p.Position.Y);
            PutWord(v, at + 8, p.Visible ? 1 : 0);
            // C: every pile is made with the offsets 10 and 4 (R014+0x2324).
            v[at + 10] = 10;
            v[at + 11] = 4;
        }
        v[BattleVariables.PileCount] = (byte)loot.Piles.Count;
        v[BattleVariables.Gathered] = (byte)loot.Gathered;
        v[BattleVariables.ActivationsOn] = Flag(ActivationsEnabled);
        v[BattleVariables.DelayedStart] = Flag(DelayedStart);
        PutWord(v, BattleVariables.BattlefieldType, BattlefieldType);
        for (var k = 0; k < 5; k++)
        {
            if (k < MarchingOrder.Count)
            {
                v[BattleVariables.MarchingOrder + k] = (byte)MarchingOrder[k];
            }
        }
        v[BattleVariables.MarchingOrder + 5] = (byte)MarchingOrder.Count;
        v[BattleVariables.GroupMove] = Flag(GroupMoveAllowed);
        v[BattleVariables.Alarm] = (byte)Battle.Alarm;
        for (var level = 0; level < 16; level++)
        {
            var bits = Store.DoorsOpened[level];
            for (var k = 0; k < 4; k++)
            {
                v[BattleVariables.DoorsOpened + 4 * level + k] = (byte)(bits >> (8 * k));
            }
        }
        v[BattleVariables.RealCombat] = Flag(Battle.RealCombat);
        v[BattleVariables.EnemySaw] = Flag(EnemySawMember);
        v[BattleVariables.FloorShown] = (byte)Battle.FloorShown;
        v[BattleVariables.PartyFloor] = (byte)Battle.PartyFloor;
        v[BattleVariables.EnemyFloor] = (byte)Battle.EnemyFloor;
        var alarm = AlarmTrigger;
        PutWord(v, BattleVariables.AlarmTrigger, alarm.X);
        PutWord(v, BattleVariables.AlarmTrigger + 2, alarm.Y);
        v[BattleVariables.AlarmTrigger + 4] = (byte)alarm.Width;
        v[BattleVariables.AlarmTrigger + 5] = (byte)alarm.Height;
        v[BattleVariables.Stopped] = Flag(_stopped);
        v[BattleVariables.LockIsDoor] = Flag(LockIsDoor);
        PutWord(v, BattleVariables.Result, (int)Battle.Result);
        PutWord(v, BattleVariables.ResultCard, Battle.ResultCard);
        v[BattleVariables.LevelCount] = (byte)_levels.Count;
        PutWord(v, BattleVariables.StartLevel, Field.StartLevel);
        PutWord(v, BattleVariables.Seed, Seed);
        PutWord(v, BattleVariables.ClearedLevels, clearedLevels | EncounteredLevels);
        PutWord(v, BattleVariables.NextTickAt, NextTickAt);
        PutWord(v, BattleVariables.PartyRethinkAt, _partyRethinkAt);
        PutWord(v, BattleVariables.EnemyRethinkAt, _enemyRethinkAt);
        PutWord(v, BattleVariables.ActivationsAt, ActivationsAt);
        PutWord(v, BattleVariables.TimedExitAt, Battle.TimedExitTime);
        PutWord(v, BattleVariables.GroupCursorUntil, _groupCursorUntil);
        PutWord(v, BattleVariables.CombatBoxRetryAt, CombatBoxRetryAt);
        PutWord(v, BattleVariables.AlarmAt, AlarmAt);
        PutWord(v, BattleVariables.DelayedStartAt, DelayedStartAt);
        PutWord(v, BattleVariables.EndAt, Battle.EndTime);
        PutWord(v, BattleVariables.SavedPathLength, SavedPathLength);
        PutWord(v, BattleVariables.Clock, Battle.Clock);
        SavedPath.CopyTo(v, BattleVariables.SavedPath);
        // C: DS:07D8 is 1 from the start of the fight to its end (the loot screen and the character sheet clear it while they are up).
        v[BattleVariables.ScreenUp] = Flag(inBattle);
        PutWord(v, BattleVariables.LastCastleFlags, lastCastleFlags);

        file.Levels.Clear();
        for (var level = 0; level < _levels.Count; level++)
        {
            var activations = Activations[level];
            var atv = new byte[1 + activations.Count * 30];
            atv[0] = (byte)activations.Count;
            for (var k = 0; k < activations.Count; k++)
            {
                activations[k].ToRecord().CopyTo(atv, 1 + 30 * k);
            }
            var enm = Field.EnemyFiles is { } files && level < files.Count && files[level] is { } kept
                ? (byte[])kept.Clone()
                : LevelEnemyFile.Write(Field.Forces.Kinds[level] ?? []);
            file.Levels.Add(new LevelFiles(SavedFloor(_levels[level].Image), atv, enm));
        }
        file.Terrain = (byte[])Field.Terrain.Bytes.Clone();
        file.Loot = loot.FileExists ? loot.ToFile() : null;
    }

    /// <summary>A floor image in the battle file's form (object functions as numbers).</summary>
    // orig: the battle-state save R003+0x00DC (0E76:00DC) draft (the handler loop over lv->count objects at +0x218C)
    public static byte[] SavedFloor(byte[] image)
    {
        var floor = (byte[])image.Clone();
        for (var k = 0; k < floor[TriggerCount]; k++)
        {
            var at = TriggerHandlers + TriggerSize * k;
            if (at + 4 > floor.Length)
            {
                break;
            }
            var handler = floor[at] | floor[at + 1] << 8;
            // C: the prepared level holds far pointers (R024 0000 set them from these numbers); the save turns them back.
            if (handler is 0 or 1 or 2 or 4 or 5 or 6 or 7 or 8)
            {
                floor[at] = (byte)handler;
                floor[at + 1] = floor[at + 2] = floor[at + 3] = 0;
            }
        }
        return floor;
    }

    /// <summary>A floor image read back from a battle file: each object function number as the restore sets the pointer.</summary>
    // orig: the battle-state restore R003+0x0CE8 (0E76:0CE8) draft (the switch on the low word: 0, 1, 2, 4, 6, 7, 8 their functions, any other thunk 09C0:181F)
    public static byte[] RestoredFloor(byte[] saved)
    {
        var floor = (byte[])saved.Clone();
        for (var k = 0; k < floor[TriggerCount]; k++)
        {
            var at = TriggerHandlers + TriggerSize * k;
            if (at + 4 > floor.Length)
            {
                break;
            }
            var handler = floor[at] | floor[at + 1] << 8;
            if (handler is not (0 or 1 or 2 or 4 or 6 or 7 or 8))
            {
                // C: the switch's default is the pit's function (thunk 09C0:181F): 3, 5 and every other value.
                handler = (int)TriggerHandler.Pit;
            }
            floor[at] = (byte)handler;
            floor[at + 1] = 0;
        }
        return floor;
    }

    /// <summary>
    /// The battlefield of a battle file: its levels (from the floor images, with the object functions the restore sets),
    /// the activations and enemy kinds of each level, terrain.fil, the loot file and the doors already opened; the levels
    /// were prepared for battle when they were saved.
    /// </summary>
    // orig: the battle-state restore R003+0x0CE8 (0E76:0CE8) draft (the levels written back as level%d.flr/.atv/.enm,
    // terrain.fil and the loot file written back or removed); tests: BattleSaveTests
    public static GeneratedBattlefield FromBattleFile(BattleFile file, BattlefieldForces forces)
    {
        var levels = new List<BattlefieldLevel>();
        var enemies = new List<byte[]?>();
        for (var i = 0; i < file.Levels.Count; i++)
        {
            var l = file.Levels[i];
            levels.Add(new BattlefieldLevel(RestoredFloor(l.Floor)));
            var groups = new List<EnemyGroup>();
            for (var k = 0; k < l.Activations[0] && 1 + 30 * (k + 1) <= l.Activations.Length; k++)
            {
                groups.Add(EnemyGroup.FromRecord(l.Activations.AsSpan(1 + 30 * k, 30)));
            }
            if (i < BattlefieldForces.MaxLevels)
            {
                forces.Groups[i] = groups;
                forces.Kinds[i] = LevelEnemyFile.Read(l.Enemies);
            }
            enemies.Add((byte[])l.Enemies.Clone());
        }
        var v = file.Variables;
        var field = new GeneratedBattlefield
        {
            Levels = levels,
            Terrain = file.Terrain is { } t ? new TerrainDescription(t) : new TerrainDescription(),
            Forces = forces,
            StartLevel = Word(v, BattleVariables.StartLevel),
            EnemyFiles = enemies,
        };
        field.Store.Prepared = true;
        for (var level = 0; level < 16; level++)
        {
            field.Store.DoorsOpened[level] = BitConverter.ToUInt32(v, BattleVariables.DoorsOpened + 4 * level);
        }
        var loot = field.Store.Loot;
        if (file.Loot is { } bytes)
        {
            loot.FromFile(bytes);
        }
        for (var k = 0; k < loot.ChestOffsets.Length; k++)
        {
            loot.ChestOffsets[k] = Word(v, BattleVariables.ChestOffsets + 2 * k);
        }
        for (var k = 0; k < v[BattleVariables.PileCount] && k < BattleVariables.MaxPiles; k++)
        {
            var at = BattleVariables.Piles + BattleVariables.PileSize * k;
            loot.Piles.Add(new LootPile
            {
                Level = (sbyte)v[at + 3],
                Position = new MapPoint(Word(v, at + 4), Word(v, at + 6)),
                Offset = Word(v, at),
                Count = v[at + 2],
                Visible = Word(v, at + 8) == 1,
            });
        }
        loot.Gathered = v[BattleVariables.Gathered];
        return field;
    }

    /// <summary>
    /// The battle variables of a battle file into this battle (the party's records came with the saved game): what the
    /// restore puts back before the battle starts again. The battle's own clean-up at its start comes after (<see cref="Resume"/>).
    /// </summary>
    // orig: the battle-state restore R003+0x0CE8 (0E76:0CE8) draft
    // C: the seed word the save took from DS:D6CE is read into DS:DAD7 (an asymmetry of the original): DS:D6CE is the new call's seed.
    public void ReadBattleFile(BattleFile file)
    {
        var v = file.Variables;
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            Array.Copy(v, BattleVariables.Paths + i * Combatant.PathLength, c.Path, 0, Combatant.PathLength);
            c.PathIndex = v[BattleVariables.PathIndex + i];
            c.Moving = v[BattleVariables.Moving + i] != 0;
            c.Exchange = (Exchange)v[BattleVariables.Exchange + i];
            c.State = (ActionState)v[BattleVariables.State + i];
            c.AttackFacing = (sbyte)v[BattleVariables.AttackFacing + i];
            c.DamageShown = (sbyte)v[BattleVariables.DamageShown + i];
            c.DamageNumber = (sbyte)v[BattleVariables.DamageNumber + i];
            c.WasHit = v[BattleVariables.WasHit + i] != 0;
            c.Floor = (sbyte)v[BattleVariables.Floor + i];
            c.GaveUp = v[BattleVariables.GaveUp + i] != 0;
            c.EngagedByAi = v[BattleVariables.EngagedByAi + i] != 0;
            c.BlockedBy = Word(v, BattleVariables.BlockedBy + 2 * i);
            c.SavedWord = (short)Word(v, BattleVariables.Unused1641 + 2 * i);
            c.Flags = (TacticalFlags)(ushort)Word(v, BattleVariables.Flags + 2 * i);
            c.Place = (PlaceAction)v[BattleVariables.Place + i];
            c.PlacedBaseLoad = (sbyte)v[BattleVariables.PlacedBaseLoad + i];
            for (var k = 0; k < 3; k++)
            {
                c.Orders[k] = (CombatAction)(ushort)Word(v, BattleVariables.Orders + 6 * i + 2 * k);
            }
            c.NextStepAt = UWord(v, BattleVariables.NextStepAt + 2 * i);
            c.NextFrameAt = UWord(v, BattleVariables.NextFrameAt + 2 * i);
            c.EngageAt = UWord(v, BattleVariables.EngageAt + 2 * i);
            c.RepathAt = UWord(v, BattleVariables.RepathAt + 2 * i);
            c.GiveUpUntil = UWord(v, BattleVariables.GiveUpUntil + 2 * i);
            c.ReloadStartAt = UWord(v, BattleVariables.ReloadStartAt + 2 * i);
            c.AimStartAt = UWord(v, BattleVariables.AimStartAt + 2 * i);
            c.AimEndAt = UWord(v, BattleVariables.AimEndAt + 2 * i);
            c.FlashUntil = UWord(v, BattleVariables.FlashUntil + 2 * i);
            c.DamageNumberUntil = UWord(v, BattleVariables.DamageNumberUntil + 2 * i);
            c.RecoverAt = UWord(v, BattleVariables.RecoverAt + 2 * i);
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            c.Door = (sbyte)v[BattleVariables.MemberDoor + i];
            c.DoorOpens = v[BattleVariables.MemberDoorOpens + i] != 0;
            c.DoorSide = (sbyte)v[BattleVariables.MemberDoorSide + i];
            c.StairsExit = (sbyte)v[BattleVariables.MemberStairs + i];
            c.Chest = (sbyte)v[BattleVariables.MemberChest + i];
            c.DissolveSlot = (sbyte)v[BattleVariables.MemberDissolveSlot + i];
            c.ExitZone = (sbyte)v[BattleVariables.MemberExitZone + i];
            c.StairsBack = (sbyte)v[BattleVariables.MemberStairsBack + i];
            c.SkillsUsed = (SkillUse)(v[BattleVariables.SkillsUsed + 2 * i] & 0x7F);
        }
        v.AsSpan(BattleVariables.InitialSpot, InitialSpotRecord.Length).CopyTo(InitialSpotRecord);
        InitialEnemySpot = (new MapPoint((short)Word(InitialSpotRecord, 8), (short)Word(InitialSpotRecord, 10)), InitialSpotRecord[12]);
        Map.IgnorePeople = v[BattleVariables.IgnorePeople] != 0;
        ActivationsEnabled = v[BattleVariables.ActivationsOn] != 0;
        DelayedStart = v[BattleVariables.DelayedStart] != 0;
        BattlefieldType = Word(v, BattleVariables.BattlefieldType);
        MarchingOrder.Clear();
        for (var k = 0; k < v[BattleVariables.MarchingOrder + 5] && k < 5; k++)
        {
            MarchingOrder.Add(v[BattleVariables.MarchingOrder + k]);
        }
        GroupMoveAllowed = v[BattleVariables.GroupMove] != 0;
        Battle.Alarm = v[BattleVariables.Alarm];
        Battle.RealCombat = v[BattleVariables.RealCombat] != 0;
        EnemySawMember = v[BattleVariables.EnemySaw] != 0;
        Battle.FloorShown = v[BattleVariables.FloorShown];
        Battle.PartyFloor = v[BattleVariables.PartyFloor];
        Battle.EnemyFloor = (sbyte)v[BattleVariables.EnemyFloor];
        AlarmTrigger = (Word(v, BattleVariables.AlarmTrigger), Word(v, BattleVariables.AlarmTrigger + 2),
            v[BattleVariables.AlarmTrigger + 4], v[BattleVariables.AlarmTrigger + 5]);
        _stopped = v[BattleVariables.Stopped] != 0;
        LockIsDoor = v[BattleVariables.LockIsDoor] != 0;
        Battle.Result = (BattleResult)Word(v, BattleVariables.Result);
        Battle.ResultCard = Word(v, BattleVariables.ResultCard);
        NextTickAt = UWord(v, BattleVariables.NextTickAt);
        _partyRethinkAt = UWord(v, BattleVariables.PartyRethinkAt);
        _enemyRethinkAt = UWord(v, BattleVariables.EnemyRethinkAt);
        ActivationsAt = UWord(v, BattleVariables.ActivationsAt);
        Battle.TimedExitTime = UWord(v, BattleVariables.TimedExitAt);
        _groupCursorUntil = UWord(v, BattleVariables.GroupCursorUntil);
        CombatBoxRetryAt = UWord(v, BattleVariables.CombatBoxRetryAt);
        AlarmAt = UWord(v, BattleVariables.AlarmAt);
        DelayedStartAt = UWord(v, BattleVariables.DelayedStartAt);
        Battle.EndTime = UWord(v, BattleVariables.EndAt);
        SavedPathLength = Word(v, BattleVariables.SavedPathLength);
        Battle.Clock = UWord(v, BattleVariables.Clock);
        Array.Copy(v, BattleVariables.SavedPath, SavedPath, 0, SavedPath.Length);
    }

    /// <summary>
    /// The start of a fight taken up from a saved game (DS:9080 bit 0x100): the levels as saved, the members where they
    /// stood, the timers running on; the orders are dropped, weapons readied again, the enemies (not saved) gone.
    /// </summary>
    // orig: R014+0x0000 (0E76:0000) draft, its branches on DS:9081 bit 0: the members' floors and the party's floor are not reset,
    // R020+0x06B6 (the timers' clearing) is skipped; R002+0x0000 (0E76:0000) with DS:9081 bit 0 takes mode 1 (no new battlefield)
    // and R002+0x1830 skips the per-battle resets of the restored tables; tests: BattleSaveTests
    // C: the restore leaves bit 0x10 of the combat flags (DS:9080) and the main loop adds bit 0, so the battle is persistent and no
    // level is prepared again; the battle's other start-up runs as for any battle.
    public void Resume()
    {
        ResetDragonFight();
        Ticks = 0;
        InitMenus();
        RebuildMarchingOrder();
        SwitchLevel(Battle.FloorShown);
        AlarmTrigger = (-1, 0, 0, 0);
        CheckActivations();
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            c.ClearOrders();
            if (c.Status != CharacterStatus.Empty && (i < Combatant.PartySlots || c.EnemyClass == 0))
            {
                ReadyWeapon(i, c.DefaultWeapon, 0);
            }
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            c.HalfWidth = Slots[i].HalfWidth;
            c.HalfHeight = Slots[i].HalfHeight;
            if (c.Status != CharacterStatus.Empty)
            {
                SetPose(i, Pose.Walking);
            }
        }
        EnemyTurn();
        Battle.Result = BattleResult.Running;
        foreach (var c in Battle.Combatants)
        {
            c.QueuedStep = -1;
        }
        _partyRethinkSlot = 0;
        _enemyRethinkSlot = 4;
        _enemyRethinkInterval = 0x10;
        SelectMember(Battle.Leader);
        PendingOrderMember = Battle.ActiveMember;
        MoveMode = 0;
        NextTickAt = (ushort)(Clock + TickLength);
        ActivationsAt = (ushort)(Clock + 120);
        SaveRulesMessage(true);
    }

    /// <summary>
    /// What saving in a battle does to it: the party's running effect modifiers end first (the saved records are without
    /// them), the battle is written into the program's battle file, and the shown level is switched to again afterwards.
    /// </summary>
    // orig: R037+0x2ACE (1462:2ACE) draft, its branch for a save in a battle (DS:907A = 1): for each member present, each of the three
    // modifier timers of DS:A53E still running is ended (thunks 09C0:1CCF, 09C0:1CC5, 09C0:1CBB) before the file is written; the
    // battle-state save R003+0x00DC ends with the level switch to the shown level
    public void EndPartyModifiersForSave() => Effects.EndPartyModifiers(presentOnly: true);

    private static byte Flag(bool on) => (byte)(on ? 1 : 0);

    private static int Word(byte[] v, int at) => (short)(v[at] | v[at + 1] << 8);

    private static ushort UWord(byte[] v, int at) => (ushort)(v[at] | v[at + 1] << 8);

    private static void PutWord(byte[] v, int at, int value)
    {
        v[at] = (byte)value;
        v[at + 1] = (byte)(value >> 8);
    }
}
