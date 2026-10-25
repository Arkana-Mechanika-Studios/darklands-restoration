using System.Reflection;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The tactical battle's own state paired with the original's memory (what <see cref="ImageOverlay"/> does for the combatants and the battle's
/// scalars): the shown level's image, the paths, the effects' modifiers, the map's switches and furniture shapes, the encounters still
/// hidden, a few of the tactical battle's scalars. Each entry is a place the original keeps a value and the property the rewrite keeps it in.
/// </summary>
internal static class TacticsOverlay
{
    public const int FloorSize = BattlefieldLevel.ImageSize, PathsAt = 0x6238, PathLength = 250, ActivationsAt = 0x47EE, ActivationSize = 30;
    public const int Modifiers = 0xA532, ModifierSize = 0x12, WatchedCounts = 0xD5C6, QueuedSteps = 0xCA30, WatchedFacingTable = 0x20EE;
    public const int FurnitureHeights = 0xC11D, FurnitureWidths = 0xC12F, FurnitureX = 0xD7BE, FurnitureY = 0xD802, LastParts = 0xE11C;
    public const int TwinDoorsAt = 0xC00, DoorsOpenedAt = 0xBB6, MissileCount = 0xC180, MissilesAt = 0x944D, MissileSize = 0x1A, MaxMissiles = 17;
    public const int AttackEnables = 0x97DD, AttackMenuTitle = 0x9433, FloorsAt = 0xA6B4, FlagsAt = 0xA696;
    public const int MenuOrders = 0x9609, MenuStride = 0x14, DisarmText = 0x970B, Picker = 0xE1CA, RecoverableCount = 0xC783, RecoverablesAt = 0x3954, MaxRecoverables = 10;
    public const int PartyPacks = 0x3E, PartyPackStride = 0x180, EnemyPacks = 0xBDE, EnemyPackStride = 0x60, CountAt = 0x7E;
    public const int Clouds = 0xA2EB, CloudCount = 0xA33D, Areas = 0xA342, AreaCount = 0xA395, Markers = 0xA396, MarkerCount = 0xA531, Immunity = 0xC927, FlashUntil = 0xDA89;
    public const int FileNames = 0x11B2, SlotBuffers = 0xCA45, PictureCodes = 0x1166;
    public const int FlashAt = 0x35EE, FlashSprites = 0xD4C1, AnimFile = 0x1174, Slots = 7;
    public const int Appearances = 0xC200, EnemyClasses = 0xC2BF, PreferredSlots = 0xD680, SlotIdentities = 0xD65F, SlotIdentity2 = 0xD67E, FireBlocked = 0xA3D8;
    public const int ActivationCount = 0xD6CC, SeenAt = 0x79A9, ReachAt = 0x7900, TimerCallbacksAt = 0x7D1, DoorsBeforeSearch = 0xE0C0;

    /// <summary>The pack counts Apply left, for a state without the packs (see Extract).</summary>
    [ThreadStatic]
    private static int[]? s_stacksBefore;

    private static int[] StacksBefore => s_stacksBefore ??= new int[Combatant.Count];

    private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Set(object target, string name, object? value)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, Any);
        if (property is not null)
        {
            property.SetValue(target, value);
            return;
        }
        (type.GetField(name, Any) ?? throw new MissingMemberException(type.Name, name)).SetValue(target, value);
    }

    public static T Get<T>(object target, string name)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, Any);
        return (T)(property is not null ? property.GetValue(target)! : (type.GetField(name, Any) ?? throw new MissingMemberException(type.Name, name)).GetValue(target)!);
    }

    private sealed record Flag(string Name, int Ds, int Width, string Property, bool OnMap = false, bool Byte = false);

    private static readonly Flag[] Flags =
    [
        new("map strict walls", 0xE0A2, 1, "StrictWalls", true),
        new("map searching", 0xD697, 1, "Searching", true),
        new("map ignore scenery", 0xE13C, 1, "IgnoreScenery", true),
        new("map ignore people", 0xC600, 1, "IgnorePeople", true),
        new("enemy saw a member", 0xC4FD, 1, "EnemySawMember"),
        new("activations enabled", 0xD6F0, 1, "ActivationsEnabled"),
        new("delayed start", 0xD842, 1, "DelayedStart"),
        new("group move allowed", 0xD610, 1, "GroupMoveAllowed"),
        // the pause byte itself (0x11 after Esc with nobody selected, 0x03 after order_mode_leave's missile to record -1)
        new("paused", 0xA695, 1, "PauseByte", Byte: true),
        // DS:D4E4: the level in the buffer changed since it was loaded or saved (level_switch saves the level it leaves only then)
        new("level changed", 0xD4E4, 1, "LevelChanged"),
        new("stopped", 0xCA29, 1, "_stopped"),
        new("lock is door", 0xCA99, 1, "LockIsDoor"),
        new("next to pile", 0xC865, 1, "NextToPile"),
    ];

    private static readonly (string Name, int Ds, string Property)[] Words =
    [
        ("activations at", 0xDA4F, "ActivationsAt"),
        ("alarm at", 0xDA47, "AlarmAt"),
        ("combat box retry at", 0xDA49, "CombatBoxRetryAt"),
        ("delayed start at", 0xC2BB, "DelayedStartAt"),
        ("next tick at", 0xD9E4, "NextTickAt"),
        ("party rethink at", 0xDA53, "_partyRethinkAt"),
        ("enemy rethink at", 0xDA51, "_enemyRethinkAt"),
        ("group cursor until", 0xDA4B, "_groupCursorUntil"),
    ];

    private static int Word(byte[] m, int at) => m[at] | m[at + 1] << 8;

    private static void PutWord(byte[] m, int at, int v)
    {
        m[at] = (byte)v;
        m[at + 1] = (byte)(v >> 8);
    }

    public static void Apply(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        var far = image.Far;
        if (t.Battle.FloorShown < 0 || t.Battle.FloorShown >= t.LevelCount)
        {
            throw new BattleCallsTests.SkipJob("the floor shown is not a level of this battlefield");
        }
        // the level shown is the one DS:11CA names: the rewrite's own start may show another (a job that moves the shown floor)
        t.Map.Level = Get<IReadOnlyList<BattlefieldLevel>>(t, "_levels")[t.Battle.FloorShown];
        t.Battle.ExitCards = t.Map.Level.ExitCards;
        far.AsSpan(0, FloorSize).CopyTo(t.Level.Image);
        var map = t.Map;
        foreach (var f in Flags)
        {
            if (f.Byte)
            {
                Set(t, f.Property, ds[f.Ds]);
                continue;
            }
            Set(f.OnMap ? map : t, f.Property, ds[f.Ds] != 0);
        }
        foreach (var (_, at, property) in Words)
        {
            var kind = typeof(TacticalBattle).GetProperty(property, Any)?.PropertyType ?? typeof(TacticalBattle).GetField(property, Any)!.FieldType;
            Set(t, property, Convert.ChangeType((ushort)Word(ds, at), kind));
        }
        t.TimerCallbacks = ds[TimerCallbacksAt];
        // the enemy slots' identities (DS:D65F slot 5, DS:D67E slot 6; −1 free)
        t.EnemySlotIdentity[0] = (short)Word(ds, SlotIdentities);
        t.EnemySlotIdentity[1] = (short)Word(ds, SlotIdentity2);
        // DS:E0C0: the doors' open flags the last enemy path search kept (R014+0x48DE), put back by every enemy search's exit (R014+0x4B5A)
        ds.AsSpan(DoorsBeforeSearch, 40).CopyTo(Get<byte[]>(t, "_doorsBeforeSearch"));
        // the eleven animation slots' tables (sprite_file_load's, AnimationSlot): what the loaded files left in DS
        for (var s = 0; s < t.Slots.Length; s++)
        {
            var slot = t.Slots[s];
            Set(slot, "HalfWidth", (int)(sbyte)ds[0x1182 + s]);
            Set(slot, "HalfHeight", (int)(sbyte)ds[0x118E + s]);
            Set(slot, "CombatHalfWidth", (int)(sbyte)ds[0x119A + s]);
            Set(slot, "CombatHalfHeight", (int)(sbyte)ds[0x11A6 + s]);
            Set(slot, "CanBePushed", ds[0xC799 + s] != 0);
            Set(slot, "CanMiss", ds[0xC7D2 + s] != 0);
            Set(slot, "DropsLoot", ds[0xC803 + s] != 0);
            Set(slot, "CorpseKind", (int)(sbyte)ds[0xC83F + s]);
            Set(slot, "StepX", Enumerable.Range(0, 10).Select(j => (sbyte)ds[0xC681 + 10 * s + j]).ToArray());
            Set(slot, "StepY", Enumerable.Range(0, 10).Select(j => (sbyte)ds[0xC6F4 + 10 * s + j]).ToArray());
            Set(slot, "FrameCount", (int)(sbyte)ds[0xC908 + s]);
            Set(slot, "AttackLength", (int)(sbyte)ds[0xC84A + s]);
            Set(slot, "NoParrySequence", ds[0xC86B + s] != 0);
            Set(slot, "AimLength", ds[0xC8A7 + s]);
            Set(slot, "Sequences", ds.AsSpan(0xA3E7 + 30 * s, 30).ToArray());
        }
        for (var s = 0; s < t.Slots.Length; s++)
        {
            // the file each slot holds (DS:11B2[slot] points to its name), whether its buffers are allocated (DS:CA45), and the picture codes (DS:1166[slot])
            Set(t.Slots[s], "FileName", CString(ds, Word(ds, FileNames + 2 * s), 12));
            t.Slots[s].HasBuffer = ds[SlotBuffers + s] != 0;
            if (s < Slots)
            {
                t.SlotCodes[s] = CString(ds, Word(ds, PictureCodes + 2 * s), 3);
            }
        }
        t.Missiles.Clear();
        for (var i = 0; i < Math.Min((int)ds[MissileCount], MaxMissiles); i++)
        {
            var at = MissilesAt + MissileSize * i;
            t.Missiles.Add(new Missile
            {
                Position = new MapPoint((short)Word(ds, at + 8), (short)Word(ds, at + 0x0A)), Goal = new MapPoint((short)Word(ds, at + 0x0C), (short)Word(ds, at + 0x0E)),
                Target = (sbyte)ds[at + 0x10], Done = ds[at + 0x11], Facing = (sbyte)ds[at + 0x12], Shooter = (sbyte)ds[at + 0x13], Sprite = (sbyte)ds[at + 0x14],
                Spell = ds[at + 0x15], Power = ds[at + 0x16], Speed = ds[at + 0x17], Floor = (sbyte)ds[at + 0x18],
            });
        }
        for (var n = 0; n < t.Menus.Orders.Length; n++)
        {
            t.Menus.Orders[n] = ds[MenuOrders + MenuStride * n];
        }
        t.Menus.DisarmText = Word(ds, DisarmText);
        for (var n = 0; n < t.Menus.Attack.Length; n++)
        {
            t.Menus.Attack[n] = ds[AttackEnables + MenuStride * n];
        }
        t.Menus.AttackMenu = ds[AttackMenuTitle];
        OrderOverlay.Apply(t, image);
        LootOverlay.Apply(t, image);
        Set(t, "_picker", (int)ds[Picker]);
        t.Recoverables.Clear();
        for (var n = 0; n < Math.Min((int)ds[RecoverableCount], MaxRecoverables); n++)
        {
            var at = RecoverablesAt + 6 * n;
            t.Recoverables.Add(new Recoverable(Word(far, at), far[at + 2], (sbyte)far[at + 3], far[at + 4], far[at + 5]));
        }
        if (image.Far3 is { } packs)
        {
            for (var k = 0; k < Combatant.Count; k++)
            {
                var pack = t.Battle[k].Items;
                pack.Clear();
                var (start, entries) = PackAt(k);
                for (var n = 0; n < entries; n++)
                {
                    var at = start + 6 * n;
                    pack[n] = new ItemStack(Word(packs, at), packs[at + 2], packs[at + 3], packs[at + 4], packs[at + 5]);
                }
                pack.Stacks = (sbyte)ds[ImageOverlay.Combatants + ImageOverlay.Record * k + CountAt];
            }
        }
        else
        {
            // a state without the packs (older rounds): the packs stay the rewrite's own start's; Extract writes back the counts the call changed
            for (var k = 0; k < Combatant.Count; k++)
            {
                StacksBefore[k] = t.Battle[k].Items.Stacks;
            }
        }
        var fx = t.Effects;
        for (var k = 0; k < 8; k++)
        {
            var c = Clouds + 10 * k;
            fx.CloudSlots[k] = new EffectCloud { X = (short)Word(ds, c), Y = (short)Word(ds, c + 2), Size = ds[c + 4], Kind = ds[c + 5], Byte6 = (sbyte)ds[c + 6], Level = (sbyte)ds[c + 7], Until = (ushort)Word(ds, c + 8) };
            var a = Areas + 10 * k;
            fx.AreaSlots[k] = new EffectArea { X = (short)Word(ds, a), Y = (short)Word(ds, a + 2), Size = ds[a + 4], Strength = ds[a + 5], Level = (sbyte)ds[a + 6], Byte7 = (sbyte)ds[a + 7], Until = (ushort)Word(ds, a + 8) };
            var m = Markers + 8 * k;
            fx.MarkerSlots[k] = new EffectMarker { X = (short)Word(ds, m), Y = (short)Word(ds, m + 2), Frame = ds[m + 4], Kind = ds[m + 5], Level = (sbyte)ds[m + 6], Byte7 = (sbyte)ds[m + 7] };
        }
        fx.SetCounts(ds[CloudCount], ds[AreaCount], ds[MarkerCount]);
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = t.Battle[k];
            c.EffectImmunity = (short)Word(ds, Immunity + 2 * k);
            c.FlashUntil = (ushort)Word(ds, FlashUntil + 2 * k);
            for (var w = 0; w < 3; w++)
            {
                var v = Word(far, FlashAt + 6 * k + 2 * w);
                c.Flash[w] = v == 0 ? 0 : v == Word(ds, FlashSprites) ? EffectField.FlashSprite0 : v == Word(ds, FlashSprites + 2) ? EffectField.FlashSprite1 : v == Word(ds, FlashSprites + 4) ? EffectField.FlashSprite2 : v;
            }
        }
        for (var s = 0; s < Slots; s++)
        {
            var at = Word(ds, AnimFile + 2 * s);
            var end = Array.IndexOf(ds, (byte)0, at);
            t.SlotWeapons[s] = System.Text.Encoding.Latin1.GetString(ds, at, Math.Min(end - at, 4));
        }
        Set(map, "SearchTarget", ds[0xD6CD] == 0xFF ? -1 : ds[0xD6CD]);
        map.FirstSightBlockingFurniture = ds[0xC1CF];
        var shapes = new FurnitureShape?[16];
        for (var k = 0; k < 16; k++)
        {
            shapes[k] = new FurnitureShape(ds[FurnitureWidths + k], ds[FurnitureHeights + k],
                [.. Enumerable.Range(0, 4).Select(j => (int)ds[FurnitureX + 4 * k + j])], [.. Enumerable.Range(0, 4).Select(j => (int)ds[FurnitureY + 4 * k + j])]);
        }
        map.Furniture = shapes;
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = t.Battle[k];
            var at = Modifiers + ModifierSize * k;
            for (var j = 0; j < 2; j++)
            {
                c.Modifiers.Attributes[j] = (sbyte)ds[at + j];
            }
            for (var j = 0; j < 7; j++)
            {
                c.Modifiers.Skills[j] = (sbyte)ds[at + 2 + j];
            }
            c.Modifiers.Held = ds[at + 9] != 0;
            c.Modifiers.SavedAction = (CombatAction)(ushort)Word(ds, at + 0x0A);
            c.Modifiers.AttributesUntil = (ushort)Word(ds, at + 0x0C);
            c.Modifiers.SkillsUntil = (ushort)Word(ds, at + 0x0E);
            c.Modifiers.HeldUntil = (ushort)Word(ds, at + 0x10);
            c.WatchedFacingCount = (sbyte)ds[WatchedCounts + k];
            // the facings an enemy watches are the table of its animation slot (record +0x16): six words of twelve bytes from DS:20EE
            var slot = (sbyte)ds[ImageOverlay.Combatants + ImageOverlay.Record * k + 0x16];
            for (var j = 0; j < 6; j++)
            {
                var index = WatchedFacingTable + 12 * slot + 2 * j;
                c.WatchedFacings[j] = index >= 0 && index + 1 < ds.Length ? (short)Word(ds, index) : 0;
            }
            c.QueuedStep = (sbyte)ds[QueuedSteps + k];
            // the enemy's animation identity (DS:C200 words), its class (DS:C2BF), the other slot it moves to (DS:D680, 0xFF none), the fire-blocked count (DS:A3D8)
            c.Appearance = (short)Word(ds, Appearances + 2 * k);
            c.EnemyClass = ds[EnemyClasses + k];
            c.PreferredSlot = (sbyte)ds[PreferredSlots + k];
            Get<byte[]>(t, "_fireBlocked")[k] = ds[FireBlocked + k];
            Array.Copy(far, PathsAt + PathLength * k, c.Path, 0, PathLength);
            for (var j = 0; j < Combatant.Count; j++)
            {
                var seen = SeenAt + 4 * (k * Combatant.Count + j);
                c.SeenPositions[j] = new MapPoint((short)Word(far, seen), (short)Word(far, seen + 2));
                t.Battle.Reaches[k, j] = far[ReachAt + k * Combatant.Count + j] != 0;
            }
        }
        if (image.Far2 is { } far2)
        {
            var twins = Get<bool[]>(t, "_twinDoor");
            for (var k = 0; k < twins.Length; k++)
            {
                twins[k] = far2[TwinDoorsAt + k] != 0;
            }
            for (var level = 0; level < 16; level++)
            {
                t.Field.Store.DoorsOpened[level] = BitConverter.ToUInt32(far2, DoorsOpenedAt + 4 * level);
            }
        }
        for (var j = 0; j < 16; j++)
        {
            map.LastParts[j] = Word(ds, LastParts + 2 * j);
        }
        Set(map, "LastMask", (int)(short)Word(ds, 0xD708));
        var list = t.Activations[t.Battle.FloorShown];
        list.Clear();
        for (var k = 0; k < ds[ActivationCount] && ActivationsAt + ActivationSize * (k + 1) <= far.Length; k++)
        {
            var group = EnemyGroup.FromRecord(far.AsSpan(ActivationsAt + ActivationSize * k, ActivationSize));
            if (group.Kinds.Any(kind => kind < -1))
            {
                throw new BattleCallsTests.SkipJob($"encounter {k} of {ds[ActivationCount]} has a kind byte below -1: a count past the records the level holds (DS:D6CC)");
            }
            list.Add(group);
        }
    }

    private static string CString(byte[] m, int at, int max)
    {
        var end = Array.IndexOf(m, (byte)0, at);
        return System.Text.Encoding.Latin1.GetString(m, at, Math.Min(end - at, max));
    }

    public static void Extract(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        var far = image.Far;
        t.Level.Image.AsSpan().CopyTo(far);
        var map = t.Map;
        foreach (var f in Flags)
        {
            ds[f.Ds] = f.Byte ? Get<byte>(t, f.Property) : (byte)(Get<bool>(f.OnMap ? map : t, f.Property) ? 1 : 0);
        }
        foreach (var (_, at, property) in Words)
        {
            PutWord(ds, at, Convert.ToInt32(Get<object>(t, property)));
        }
        for (var n = 0; n < t.Menus.Orders.Length; n++)
        {
            ds[MenuOrders + MenuStride * n] = t.Menus.Orders[n];
        }
        PutWord(ds, DisarmText, t.Menus.DisarmText);
        for (var n = 0; n < t.Menus.Attack.Length; n++)
        {
            ds[AttackEnables + MenuStride * n] = t.Menus.Attack[n];
        }
        ds[AttackMenuTitle] = t.Menus.AttackMenu;
        OrderOverlay.Extract(t, image);
        LootOverlay.Extract(t, image);
        ds[Picker] = (byte)Get<int>(t, "_picker");
        ds[RecoverableCount] = (byte)t.Recoverables.Count;
        for (var n = 0; n < Math.Min(t.Recoverables.Count, MaxRecoverables); n++)
        {
            var at = RecoverablesAt + 6 * n;
            var r = t.Recoverables[n];
            PutWord(far, at, r.Item);
            far[at + 2] = (byte)r.Kind;
            far[at + 3] = (byte)r.Quality;
            far[at + 4] = (byte)r.Count;
            far[at + 5] = (byte)r.Pad;
        }
        if (image.Far3 is { } packs)
        {
            for (var k = 0; k < Combatant.Count; k++)
            {
                var pack = t.Battle[k].Items;
                var (start, entries) = PackAt(k);
                for (var n = 0; n < entries; n++)
                {
                    var at = start + 6 * n;
                    var e = pack.Entries[n];
                    PutWord(packs, at, e.Code);
                    packs[at + 2] = (byte)e.Type;
                    packs[at + 3] = (byte)e.Quality;
                    packs[at + 4] = (byte)e.Count;
                    packs[at + 5] = (byte)e.Weight;
                }
                ds[ImageOverlay.Combatants + ImageOverlay.Record * k + CountAt] = (byte)pack.Stacks;
            }
        }
        else
        {
            for (var k = 0; k < Combatant.Count; k++)
            {
                if (t.Battle[k].Items.Stacks != StacksBefore[k])
                {
                    ds[ImageOverlay.Combatants + ImageOverlay.Record * k + CountAt] = (byte)t.Battle[k].Items.Stacks;
                }
            }
        }
        var fx = t.Effects;
        for (var k = 0; k < 8; k++)
        {
            var c = Clouds + 10 * k;
            var cl = fx.CloudSlots[k];
            PutWord(ds, c, cl.X);
            PutWord(ds, c + 2, cl.Y);
            ds[c + 4] = cl.Size;
            ds[c + 5] = cl.Kind;
            ds[c + 6] = (byte)cl.Byte6;
            ds[c + 7] = (byte)cl.Level;
            PutWord(ds, c + 8, cl.Until);
            var a = Areas + 10 * k;
            var ar = fx.AreaSlots[k];
            PutWord(ds, a, ar.X);
            PutWord(ds, a + 2, ar.Y);
            ds[a + 4] = ar.Size;
            ds[a + 5] = ar.Strength;
            ds[a + 6] = (byte)ar.Level;
            ds[a + 7] = (byte)ar.Byte7;
            PutWord(ds, a + 8, ar.Until);
            var m = Markers + 8 * k;
            var mk = fx.MarkerSlots[k];
            PutWord(ds, m, mk.X);
            PutWord(ds, m + 2, mk.Y);
            ds[m + 4] = mk.Frame;
            ds[m + 5] = mk.Kind;
            ds[m + 6] = (byte)mk.Level;
            ds[m + 7] = (byte)mk.Byte7;
        }
        ds[CloudCount] = (byte)fx.CloudCount;
        ds[AreaCount] = (byte)fx.AreaCount;
        ds[MarkerCount] = (byte)fx.MarkerCount;
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = t.Battle[k];
            PutWord(ds, Immunity + 2 * k, c.EffectImmunity);
            PutWord(ds, FlashUntil + 2 * k, c.FlashUntil);
            for (var w = 0; w < 3; w++)
            {
                var v = c.Flash[w];
                PutWord(far, FlashAt + 6 * k + 2 * w, v == EffectField.FlashSprite0 ? Word(ds, FlashSprites) : v == EffectField.FlashSprite1 ? Word(ds, FlashSprites + 2) : v == EffectField.FlashSprite2 ? Word(ds, FlashSprites + 4) : v);
            }
        }
        for (var s = 0; s < Slots; s++)
        {
            var at = Word(ds, AnimFile + 2 * s);
            var code = System.Text.Encoding.Latin1.GetBytes(t.SlotWeapons[s]);
            for (var j = 0; j < Math.Min(code.Length, 4); j++)
            {
                ds[at + j] = code[j];
            }
            ds[at + Math.Min(code.Length, 4)] = 0;
        }
        var target = Get<int>(map, "SearchTarget");
        ds[0xD6CD] = (byte)(target < 0 ? 0xFF : target);
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = t.Battle[k];
            var at = Modifiers + ModifierSize * k;
            for (var j = 0; j < 2; j++)
            {
                ds[at + j] = (byte)c.Modifiers.Attributes[j];
            }
            for (var j = 0; j < 7; j++)
            {
                ds[at + 2 + j] = (byte)c.Modifiers.Skills[j];
            }
            ds[at + 9] = (byte)(c.Modifiers.Held ? 1 : 0);
            PutWord(ds, at + 0x0A, (int)c.Modifiers.SavedAction);
            PutWord(ds, at + 0x0C, c.Modifiers.AttributesUntil);
            PutWord(ds, at + 0x0E, c.Modifiers.SkillsUntil);
            PutWord(ds, at + 0x10, c.Modifiers.HeldUntil);
            ds[WatchedCounts + k] = (byte)c.WatchedFacingCount;
            ds[QueuedSteps + k] = (byte)c.QueuedStep;
            PutWord(ds, Appearances + 2 * k, c.Appearance);
            ds[EnemyClasses + k] = (byte)c.EnemyClass;
            ds[PreferredSlots + k] = (byte)c.PreferredSlot;
            ds[FireBlocked + k] = Get<byte[]>(t, "_fireBlocked")[k];
            Array.Copy(c.Path, 0, far, PathsAt + PathLength * k, PathLength);
            for (var j = 0; j < Combatant.Count; j++)
            {
                var seen = SeenAt + 4 * (k * Combatant.Count + j);
                PutWord(far, seen, c.SeenPositions[j].X);
                PutWord(far, seen + 2, c.SeenPositions[j].Y);
                far[ReachAt + k * Combatant.Count + j] = (byte)(t.Battle.Reaches[k, j] ? 1 : 0);
            }
        }
        for (var j = 0; j < 16; j++)
        {
            PutWord(ds, LastParts + 2 * j, map.LastParts[j]);
        }
        PutWord(ds, 0xD708, map.LastMask);
        if (image.Far2 is { } far2)
        {
            var twins = Get<bool[]>(t, "_twinDoor");
            for (var k = 0; k < twins.Length; k++)
            {
                far2[TwinDoorsAt + k] = (byte)(twins[k] ? 1 : 0);
            }
            for (var level = 0; level < 16; level++)
            {
                BitConverter.GetBytes(t.Field.Store.DoorsOpened[level]).CopyTo(far2, DoorsOpenedAt + 4 * level);
            }
        }
        ds[TimerCallbacksAt] = (byte)t.TimerCallbacks;
        PutWord(ds, SlotIdentities, t.EnemySlotIdentity[0]);
        PutWord(ds, SlotIdentity2, t.EnemySlotIdentity[1]);
        Get<byte[]>(t, "_doorsBeforeSearch").CopyTo(ds, DoorsBeforeSearch);
        for (var s = 0; s < t.Slots.Length; s++)
        {
            var slot = t.Slots[s];
            ds[0x1182 + s] = (byte)slot.HalfWidth;
            ds[0x118E + s] = (byte)slot.HalfHeight;
            ds[0x119A + s] = (byte)slot.CombatHalfWidth;
            ds[0x11A6 + s] = (byte)slot.CombatHalfHeight;
            ds[0xC799 + s] = (byte)(slot.CanBePushed ? 1 : 0);
            ds[0xC7D2 + s] = (byte)(slot.CanMiss ? 1 : 0);
            ds[0xC803 + s] = (byte)(slot.DropsLoot ? 1 : 0);
            ds[0xC83F + s] = (byte)slot.CorpseKind;
            for (var j = 0; j < 10; j++)
            {
                ds[0xC681 + 10 * s + j] = (byte)slot.StepX[j];
                ds[0xC6F4 + 10 * s + j] = (byte)slot.StepY[j];
            }
            ds[0xC908 + s] = (byte)slot.FrameCount;
        }
        ds[MissileCount] = (byte)t.Missiles.Count;
        for (var i = 0; i < t.Missiles.Count && i < MaxMissiles; i++)
        {
            var at = MissilesAt + MissileSize * i;
            var m = t.Missiles[i];
            PutWord(ds, at + 8, m.Position.X);
            PutWord(ds, at + 0x0A, m.Position.Y);
            PutWord(ds, at + 0x0C, m.Goal.X);
            PutWord(ds, at + 0x0E, m.Goal.Y);
            ds[at + 0x10] = (byte)m.Target;
            ds[at + 0x11] = m.Done;
            ds[at + 0x12] = (byte)m.Facing;
            ds[at + 0x13] = (byte)m.Shooter;
            ds[at + 0x14] = (byte)m.Sprite;
            ds[at + 0x15] = (byte)m.Spell;
            ds[at + 0x16] = (byte)m.Power;
            ds[at + 0x17] = (byte)m.Speed;
            ds[at + 0x18] = (byte)m.Floor;
        }
        var list = t.Activations[t.Battle.FloorShown];
        ds[ActivationCount] = (byte)list.Count;
        for (var k = 0; k < list.Count && ActivationsAt + ActivationSize * (k + 1) <= far.Length; k++)
        {
            list[k].ToRecord().CopyTo(far, ActivationsAt + ActivationSize * k);
        }
        // a removed group leaves the records behind it moved down and the vacated record as it was: the rewrite has no such bytes, so the original's stay
    }

    /// <summary>Where combatant k's pack lies in the packs' segment and how many entries it has room for (member 64, enemy 16).</summary>
    public static (int Start, int Entries) PackAt(int k) => k < Combatant.PartySlots ? (PartyPacks + PartyPackStride * k, 64) : (EnemyPacks + EnemyPackStride * k, 16);

    /// <summary>An offset of the packs' segment → what models it, or null.</summary>
    public static string? DescribeFar3(int at)
    {
        for (var k = 0; k < Combatant.Count; k++)
        {
            var (start, entries) = PackAt(k);
            if (at >= start && at < start + 6 * entries)
            {
                return $"pack[{k}] entry {(at - start) / 6} +{(at - start) % 6}";
            }
        }
        return null;
    }

    /// <summary>A DS offset → what models it here, or null.</summary>
    public static string? Describe(int ds)
    {
        if (OrderOverlay.Describe(ds) is { } order)
        {
            return order;
        }
        if (LootOverlay.Describe(ds) is { } lootPlace)
        {
            return lootPlace;
        }
        foreach (var f in Flags)
        {
            if (ds == f.Ds)
            {
                return f.Name;
            }
        }
        foreach (var (name, at, _) in Words)
        {
            if (ds >= at && ds < at + 2)
            {
                return name;
            }
        }
        if (ds >= Modifiers && ds < Modifiers + ModifierSize * 13)
        {
            return $"modifiers[{(ds - Modifiers) / ModifierSize}] +0x{(ds - Modifiers) % ModifierSize:X2}";
        }
        if (ds >= WatchedCounts && ds < WatchedCounts + 13)
        {
            return $"watched facings[{ds - WatchedCounts}]";
        }
        if (ds >= QueuedSteps && ds < QueuedSteps + 13)
        {
            return $"queued step[{ds - QueuedSteps}]";
        }
        // DS:E11C and DS:D708 (the last collision test's parts and mask) are scratch the path search leaves in its own order: carried in, not compared
        if (ds >= MenuOrders && ds < MenuOrders + MenuStride * 18 && (ds - MenuOrders) % MenuStride == 0)
        {
            return $"menu enable[{(ds - MenuOrders) / MenuStride}]";
        }
        if (ds >= AttackEnables && ds < AttackEnables + MenuStride * 6 && (ds - AttackEnables) % MenuStride == 0)
        {
            return $"attack enable[{(ds - AttackEnables) / MenuStride}]";
        }
        if (ds == AttackMenuTitle)
        {
            return "attack menu title";
        }
        if (ds == DisarmText || ds == DisarmText + 1)
        {
            return "disarm text";
        }
        if (ds == Picker)
        {
            return "picker";
        }
        if (ds == RecoverableCount)
        {
            return "recoverables count";
        }
        if (ds >= Clouds && ds < Clouds + 80)
        {
            return $"cloud[{(ds - Clouds) / 10}] +{(ds - Clouds) % 10}";
        }
        if (ds >= Areas && ds < Areas + 80)
        {
            return $"area[{(ds - Areas) / 10}] +{(ds - Areas) % 10}";
        }
        if (ds >= Markers && ds < Markers + 64)
        {
            return $"marker[{(ds - Markers) / 8}] +{(ds - Markers) % 8}";
        }
        if (ds == CloudCount || ds == AreaCount || ds == MarkerCount)
        {
            return ds == CloudCount ? "cloud count" : ds == AreaCount ? "area count" : "marker count";
        }
        if (ds >= Immunity && ds < Immunity + 26)
        {
            return $"effect immunity[{(ds - Immunity) / 2}]";
        }
        if (ds >= FlashUntil && ds < FlashUntil + 26)
        {
            return $"flash until[{(ds - FlashUntil) / 2}]";
        }
        if (ds >= Appearances && ds < Appearances + 26)
        {
            return $"appearance[{(ds - Appearances) / 2}]";
        }
        if (ds >= EnemyClasses && ds < EnemyClasses + 13)
        {
            return $"enemy class[{ds - EnemyClasses}]";
        }
        if (ds >= PreferredSlots && ds < PreferredSlots + 13)
        {
            return $"preferred slot[{ds - PreferredSlots}]";
        }
        if (ds >= FireBlocked && ds < FireBlocked + 13)
        {
            return $"fire blocked[{ds - FireBlocked}]";
        }
        if (ds is SlotIdentities or SlotIdentities + 1 or SlotIdentity2 or SlotIdentity2 + 1)
        {
            return ds < SlotIdentity2 ? "slot 5 identity" : "slot 6 identity";
        }
        if (ds == TimerCallbacksAt)
        {
            return "timer callbacks";
        }
        if (ds >= DoorsBeforeSearch && ds < DoorsBeforeSearch + 40)
        {
            return $"doors before search[{ds - DoorsBeforeSearch}]";
        }
        if (ds == MissileCount)
        {
            return "missile count";
        }
        if (ds >= MissilesAt && ds < MissilesAt + MissileSize * MaxMissiles && (ds - MissilesAt) % MissileSize is >= 8 and <= 0x18)
        {
            return $"missile[{(ds - MissilesAt) / MissileSize}] +0x{(ds - MissilesAt) % MissileSize:X2}";
        }
        // DS:D6CD (the path search's target) is scratch of the search, like the last collision test's parts: carried in, not compared
        if (ds == ActivationCount)
        {
            return "activations count";
        }
        return null;
    }

    /// <summary>An offset of the second far segment (DS:7D4A) → what models it, or null.</summary>
    public static string? DescribeFar2(int at)
    {
        if (at >= TwinDoorsAt && at < TwinDoorsAt + 40)
        {
            return $"twin door[{at - TwinDoorsAt}]";
        }
        return at >= DoorsOpenedAt && at < DoorsOpenedAt + 64 ? $"doors opened[{(at - DoorsOpenedAt) / 4}] +{(at - DoorsOpenedAt) % 4}" : null;
    }

    /// <summary>An offset of the far segment → what models it, or null (the level's image, the paths, the hidden encounters).</summary>
    public static string? DescribeFar(int at)
    {
        if (LootOverlay.DescribeFar(at) is { } pile)
        {
            return pile;
        }
        if (at >= 0 && at < FloorSize)
        {
            return $"level image +0x{at:X4}";
        }
        if (at >= PathsAt && at < PathsAt + PathLength * Combatant.Count)
        {
            return $"path[{(at - PathsAt) / PathLength}] +{(at - PathsAt) % PathLength}";
        }
        if (at >= SeenAt && at < SeenAt + 4 * 169)
        {
            return $"seen position[{(at - SeenAt) / 4 / 13}][{(at - SeenAt) / 4 % 13}] +{(at - SeenAt) % 4}";
        }
        if (at >= ReachAt && at < ReachAt + 169)
        {
            return $"reach[{(at - ReachAt) / 13}][{(at - ReachAt) % 13}]";
        }
        if (at >= FlashAt && at < FlashAt + 6 * Combatant.Count)
        {
            return $"flash[{(at - FlashAt) / 6}][{(at - FlashAt) % 6 / 2}] +{(at - FlashAt) % 2}";
        }
        if (at >= RecoverablesAt && at < RecoverablesAt + 6 * MaxRecoverables)
        {
            return $"recoverable {(at - RecoverablesAt) / 6} +{(at - RecoverablesAt) % 6}";
        }
        if (at >= ActivationsAt && at < ActivationsAt + ActivationSize * 40)
        {
            return $"encounter {(at - ActivationsAt) / ActivationSize} +0x{(at - ActivationsAt) % ActivationSize:X2}";
        }
        return null;
    }
}
