using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Persistence;


namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The pairing of the original's memory (DS and the far segment 20A5) with the rewrite's battle: <see cref="Apply"/> sets the rewrite from
/// the bytes, <see cref="Extract"/> writes what the rewrite models back over a copy of them. The bytes Extract writes are the modelled
/// set (<see cref="Describe"/>): a job agrees when its expected memory (input + the original's changes) and the rewrite's agree over it.
/// </summary>
internal static class ImageOverlay
{
    public const int Combatants = 0x9C55, Record = 0x80;
    public const int AttackRates = 0xD493, Queued = 0xD70C;

    private sealed record Scalar(string Name, int Ds, int Width, Func<Battle, int> Get, Action<Battle, int> Set);

    private sealed record Table(string Name, int Ds, int Width, int Count, Func<Combatant, int> Get, Action<Combatant, int> Set, int Stride = 0)
    {
        public int Step => Stride == 0 ? Width : Stride;
    }

    private static readonly Scalar[] Scalars =
    [
        new("result", 0xA893, 2, b => (int)b.Result, (b, v) => b.Result = (BattleResult)(short)v),
        new("result card", 0xCA3D, 2, b => b.ResultCard, (b, v) => b.ResultCard = (short)v),
        new("clock", 0x07D4, 2, b => b.Clock, (b, v) => b.Clock = (ushort)v),
        new("end time", 0xDA45, 2, b => b.EndTime, (b, v) => b.EndTime = (ushort)v),
        new("timed exit", 0xDA4D, 2, b => b.TimedExitTime, (b, v) => b.TimedExitTime = (ushort)v),
        new("floor shown", 0x11CA, 1, b => b.FloorShown, (b, v) => b.FloorShown = (sbyte)v),
        new("party floor", 0xC623, 1, b => b.PartyFloor, (b, v) => b.PartyFloor = (sbyte)v),
        new("enemy floor", 0xC651, 1, b => b.EnemyFloor, (b, v) => b.EnemyFloor = (sbyte)v),
        new("leader", 0x907B, 1, b => b.Leader, (b, v) => b.Leader = (sbyte)v),
        new("active member", 0x17B0, 1, b => b.ActiveMember, (b, v) => b.ActiveMember = (sbyte)v),
        new("real combat", 0xA6C1, 1, b => b.RealCombat ? 1 : 0, (b, v) => b.RealCombat = v != 0),
        new("alarm", 0xD6F4, 1, b => b.Alarm, (b, v) => b.Alarm = (sbyte)v),
        new("leave after victory", 0x9084, 1, b => b.LeaveAfterVictory ? 1 : 0, (b, v) => b.LeaveAfterVictory = v != 0),
        new("difficulty", 0x906A, 1, b => b.Difficulty, (b, v) => b.Difficulty = (byte)v),
        new("dragon fight", 0xC21A, 1, b => b.DragonFight, (b, v) => b.DragonFight = (byte)v),
    ];

    private static readonly Table[] Tables =
    [
        new("attack rate", AttackRates, 1, 13, c => c.AttackRate, (c, v) => c.AttackRate = (byte)v),
        new("queued endurance", Queued, 1, 13, c => (byte)c.QueuedEndurance, (c, v) => c.QueuedEndurance = (sbyte)v, 2),
        new("queued strength", Queued + 1, 1, 13, c => (byte)c.QueuedStrength, (c, v) => c.QueuedStrength = (sbyte)v, 2),
        new("half width", 0xC80F, 1, 13, c => c.HalfWidth, (c, v) => c.HalfWidth = (sbyte)v),
        new("half height", 0xC831, 1, 13, c => c.HalfHeight, (c, v) => c.HalfHeight = (sbyte)v),
        new("sequence length", 0xC96B, 1, 13, c => c.SequenceLength, (c, v) => c.SequenceLength = (sbyte)v),
        new("wait steps", 0xCA1C, 1, 13, c => c.WaitSteps, (c, v) => c.WaitSteps = (byte)v),
        new("push direction", 0xCA9A, 1, 13, c => c.PushDirection, (c, v) => c.PushDirection = (sbyte)v),
        new("pusher", 0xCAA7, 1, 13, c => c.Pusher, (c, v) => c.Pusher = (sbyte)v),
        new("loop set up", 0xE05F, 1, 13, c => c.LoopSetUp ? 1 : 0, (c, v) => c.LoopSetUp = v != 0),
        new("restart loop", 0xE052, 1, 13, c => c.RestartLoop ? 1 : 0, (c, v) => c.RestartLoop = v != 0),
        new("aim set up", 0xE06C, 1, 13, c => c.AimSetUp ? 1 : 0, (c, v) => c.AimSetUp = v != 0),
        new("moving", 0xA6D3, 1, 13, c => c.Moving ? 1 : 0, (c, v) => c.Moving = v != 0),
        new("exchange", 0xCA57, 1, 13, c => (int)c.Exchange, (c, v) => c.Exchange = (Exchange)(byte)v),
        new("action state", 0xCA64, 1, 13, c => (int)c.State, (c, v) => c.State = (ActionState)(byte)v),
        new("attack facing", 0xD3FE, 1, 13, c => c.AttackFacing, (c, v) => c.AttackFacing = (sbyte)v),
        new("damage shown", 0xD4C7, 1, 13, c => (byte)c.DamageShown, (c, v) => c.DamageShown = (sbyte)v),
        new("damage number", 0xD4E5, 1, 13, c => c.DamageNumber, (c, v) => c.DamageNumber = (sbyte)v),
        new("was hit", 0xD4F2, 1, 13, c => c.WasHit ? 1 : 0, (c, v) => c.WasHit = v != 0),
        new("floor", 0xA6B4, 1, 13, c => c.Floor, (c, v) => c.Floor = (sbyte)v),
        new("gave up", 0xD5E9, 1, 13, c => c.GaveUp ? 1 : 0, (c, v) => c.GaveUp = v != 0),
        new("engaged", 0xDA36, 1, 13, c => c.EngagedByAi ? 1 : 0, (c, v) => c.EngagedByAi = v != 0),
        new("blocked by", 0xD5F6, 2, 13, c => c.BlockedBy, (c, v) => c.BlockedBy = (short)v),
        new("saved word", 0xD62B, 2, 13, c => c.SavedWord, (c, v) => c.SavedWord = (short)v),
        new("flags", 0xA696, 2, 13, c => (int)c.Flags, (c, v) => c.Flags = (TacticalFlags)(ushort)v),
        new("place", 0xA6C4, 1, 13, c => (int)c.Place, (c, v) => c.Place = (PlaceAction)(byte)v),
        new("placed base load", 0xC1C1, 1, 13, c => c.PlacedBaseLoad, (c, v) => c.PlacedBaseLoad = (sbyte)v),
        new("path index", 0xD6FB, 1, 13, c => c.PathIndex, (c, v) => c.PathIndex = (byte)v),
        new("next step at", 0xD9CA, 2, 13, c => c.NextStepAt, (c, v) => c.NextStepAt = (ushort)v),
        new("next frame at", 0xD9B0, 2, 13, c => c.NextFrameAt, (c, v) => c.NextFrameAt = (ushort)v),
        new("engage at", 0xD996, 2, 13, c => c.EngageAt, (c, v) => c.EngageAt = (ushort)v),
        new("repath at", 0xD97C, 2, 13, c => c.RepathAt, (c, v) => c.RepathAt = (ushort)v),
        new("give up until", 0xD962, 2, 13, c => c.GiveUpUntil, (c, v) => c.GiveUpUntil = (ushort)v),
        new("reload start at", 0xD948, 2, 13, c => c.ReloadStartAt, (c, v) => c.ReloadStartAt = (ushort)v),
        new("aim start at", 0xDABD, 2, 13, c => c.AimStartAt, (c, v) => c.AimStartAt = (ushort)v),
        new("aim end at", 0xDAA3, 2, 13, c => c.AimEndAt, (c, v) => c.AimEndAt = (ushort)v),
        new("damage number until", 0xDA6F, 2, 13, c => c.DamageNumberUntil, (c, v) => c.DamageNumberUntil = (ushort)v),
        new("recover at", 0xDA55, 2, 13, c => c.RecoverAt, (c, v) => c.RecoverAt = (ushort)v),
    ];

    private static readonly Table[] MemberTables =
    [
        new("member door", 0xC64C, 1, 5, c => c.Door, (c, v) => c.Door = (sbyte)v),
        new("member door opens", 0xC678, 1, 5, c => c.DoorOpens ? 1 : 0, (c, v) => c.DoorOpens = v != 0),
        new("member door side", 0xC6EF, 1, 5, c => c.DoorSide, (c, v) => c.DoorSide = (sbyte)v),
        new("member stairs", 0xC77E, 1, 5, c => c.StairsExit, (c, v) => c.StairsExit = (sbyte)v),
        new("member chest", 0xC784, 1, 5, c => c.Chest, (c, v) => c.Chest = (sbyte)v),
        new("member dissolve", 0xC7CD, 1, 5, c => c.DissolveSlot, (c, v) => c.DissolveSlot = (sbyte)v),
        new("member exit zone", 0xC7FE, 1, 5, c => c.ExitZone, (c, v) => c.ExitZone = (sbyte)v),
        new("member stairs back", 0xC8B2, 1, 5, c => c.StairsBack, (c, v) => c.StairsBack = (sbyte)v),
        new("member skills used", 0xD68D, 1, 5, c => (int)c.SkillsUsed, (c, v) => c.SkillsUsed = (SkillUse)(byte)v, 2),
    ];

    /// <summary>The bytes of a record the rewrite models (found by writing a combatant into two differently filled buffers).</summary>
    private static readonly bool[] RecordMask = BuildRecordMask();

    private static bool[] BuildRecordMask()
    {
        var c = new Combatant(0);
        var zero = new byte[Record];
        var ones = new byte[Record];
        Array.Fill(ones, (byte)0xFF);
        CombatantRecords.Write(c, zero);
        CombatantRecords.Write(c, ones);
        // + the animation bytes (+0x18 pose, +0x1B frame, +0x1E, +0x1F delays); the info pointer +0x0E is not compared (the fights' records leave it as it is)
        return [.. Enumerable.Range(0, Record).Select(i => zero[i] == ones[i] || i is 0x18 or 0x1B or 0x1E or 0x1F)];
    }

    /// <summary>A DS offset → what models it (for reports), or null.</summary>
    public static string? Describe(int ds)
    {
        if (ds >= Combatants && ds < Combatants + 13 * Record)
        {
            var k = (ds - Combatants) / Record;
            if ((ds - Combatants) % Record == TacticsOverlay.CountAt)
            {
                return $"pack count[{k}]";
            }
            // + 0x00..0x03: where the last frame drew the sprite (the screen's, not compared)
            return RecordMask[(ds - Combatants) % Record] && (ds - Combatants) % Record >= 4 ? $"record {k} +0x{(ds - Combatants) % Record:X2}" : null;
        }
        foreach (var s in Scalars)
        {
            if (ds >= s.Ds && ds < s.Ds + s.Width)
            {
                return s.Name;
            }
        }
        foreach (var t in Tables.Concat(MemberTables))
        {
            if (ds >= t.Ds && ds < t.Ds + t.Step * t.Count && (ds - t.Ds) % t.Step < t.Width)
            {
                return $"{t.Name}[{(ds - t.Ds) / t.Step}]";
            }
        }
        if (ds >= 0xD9E6 && ds < 0xD9E6 + 13 * 6)
        {
            return $"orders[{(ds - 0xD9E6) / 6}][{(ds - 0xD9E6) % 6 / 2}]";
        }
        return null;
    }

    private static int Read(byte[] m, int at, int width) => width == 1 ? m[at] : m[at] | m[at + 1] << 8;

    private static void Put(byte[] m, int at, int width, int v)
    {
        m[at] = (byte)v;
        if (width == 2)
        {
            m[at + 1] = (byte)(v >> 8);
        }
    }

    /// <summary>Sets the battle (its combatants' records and tables, its scalars, its exit cards) from the memory.</summary>
    public static void Apply(Battle b, MemoryPair image)
    {
        var ds = image.Ds;
        for (var k = 0; k < Combatant.Count; k++)
        {
            var record = ds.AsSpan(Combatants + Record * k, Record);
            CombatantRecords.Read(record, b[k]);
            CombatantRecords.ReadAnimation(record, b[k]);
        }
        foreach (var s in Scalars)
        {
            s.Set(b, Read(ds, s.Ds, s.Width));
        }
        foreach (var t in Tables.Concat(MemberTables))
        {
            for (var k = 0; k < t.Count; k++)
            {
                t.Set(b[k], Read(ds, t.Ds + t.Step * k, t.Width));
            }
        }
        for (var k = 0; k < 13; k++)
        {
            for (var n = 0; n < 3; n++)
            {
                b[k].Orders[n] = (CombatAction)(ushort)Read(ds, 0xD9E6 + 6 * k + 2 * n, 2);
            }
        }
        var far = image.Far;
        b.ExitCards = new LevelExitCards(Read(far, 0x1F42, 2), Read(far, 0x1F44, 2), Read(far, 0x1F46, 2), Read(far, 0x1F48, 2), Read(far, 0x1F4A, 2));
    }

    /// <summary>Writes what the rewrite models over <paramref name="image"/>.</summary>
    public static void Extract(Battle b, MemoryPair image)
    {
        var ds = image.Ds;
        for (var k = 0; k < Combatant.Count; k++)
        {
            var record = ds.AsSpan(Combatants + Record * k, Record);
            CombatantRecords.Write(b[k], record);
            record[0x18] = (byte)b[k].Pose;
            record[0x1B] = (byte)b[k].Frame;
            record[0x1E] = (byte)b[k].StepDelay;
            record[0x1F] = (byte)b[k].FrameDelay;
        }
        foreach (var s in Scalars)
        {
            Put(ds, s.Ds, s.Width, s.Get(b));
        }
        foreach (var t in Tables.Concat(MemberTables))
        {
            for (var k = 0; k < t.Count; k++)
            {
                Put(ds, t.Ds + t.Step * k, t.Width, t.Get(b[k]));
            }
        }
        for (var k = 0; k < 13; k++)
        {
            for (var n = 0; n < 3; n++)
            {
                Put(ds, 0xD9E6 + 6 * k + 2 * n, 2, (int)b[k].Orders[n]);
            }
        }
    }

    /// <summary>
    /// The record bytes an enemy keeps from the enemy file's buffer (stack bytes) when it is placed and its weapon is not readied yet
    /// (battle_oracle.py's STALE_READY and the destination): the original leaves them as the stack held them, the rewrite as zero (a
    /// known difference of the tester, not a lead). Not compared for a record that was empty and is not after the call.
    /// </summary>
    private static readonly HashSet<int> StaleFields = [0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x1D, 0x20, 0x21, 0x23, 0x4D, 0x4E, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x59];

    /// <summary>The original clears the position and goal of a missile that has arrived (its done byte has bit 7): nothing reads them afterwards, the rewrite keeps them.</summary>
    private static bool ArrivedMissilePlace(int ds, MemoryPair expected)
    {
        var at = ds - TacticsOverlay.MissilesAt;
        if (at < 0 || at >= TacticsOverlay.MissileSize * TacticsOverlay.MaxMissiles)
        {
            return false;
        }
        var start = ds - at % TacticsOverlay.MissileSize;
        return at % TacticsOverlay.MissileSize is >= 8 and <= 0x0F && (expected.Ds[start + 0x11] & 0x80) != 0;
    }

    private static bool StaleOfPlacedEnemy(int ds, MemoryPair expected, MemoryPair? input)
    {
        if (input is null || ds < Combatants || ds >= Combatants + 13 * Record)
        {
            return false;
        }
        var k = (ds - Combatants) / Record;
        var status = Combatants + Record * k + 0x14;
        if (k >= Combatant.PartySlots && input.Ds[status] == 0 && expected.Ds[status] == 0 && ds != status)
        {
            // an enemy slot empty before and after: enemy-placement-entry tries its spots in the free record (R025) and leaves what it wrote there when
            // no spot is found; an empty record's bytes are read by nothing until a placement fills it
            return true;
        }
        return k >= Combatant.PartySlots && StaleFields.Contains((ds - Combatants) % Record) && input.Ds[status] == 0 && expected.Ds[status] != 0;
    }

    /// <summary>The bytes of a path after its 0xFF terminator are the last search's leftovers, not the path: not compared.</summary>
    private static bool BeyondPathEnd(int far, MemoryPair expected)
    {
        var at = far - TacticsOverlay.PathsAt;
        if (at < 0 || at >= TacticsOverlay.PathLength * Combatant.Count)
        {
            return false;
        }
        var start = far - at % TacticsOverlay.PathLength;
        var end = Array.IndexOf(expected.Far, (byte)0xFF, start, TacticsOverlay.PathLength);
        return end >= 0 && far > end;
    }

    /// <summary>The differences of two memories over the modelled bytes (DS and the far segment): "what: original vs rewrite" lines.</summary>
    public static List<string> Differences(MemoryPair expected, MemoryPair actual, MemoryPair? input = null, int limit = 8, IReadOnlyDictionary<int, string>? extra = null, IReadOnlyDictionary<int, string>? extraFar = null)
    {
        var lines = new List<string>();
        for (var i = 0; i < expected.Ds.Length && lines.Count < limit; i++)
        {
            if (expected.Ds[i] != actual.Ds[i] && (Describe(i) ?? TacticsOverlay.Describe(i) ?? extra?.GetValueOrDefault(i)) is { } what && !StaleOfPlacedEnemy(i, expected, input) && !ArrivedMissilePlace(i, expected))
            {
                lines.Add($"{what} (DS:{i:X4}): original {expected.Ds[i]:X2}, rewrite {actual.Ds[i]:X2}" + (input is null ? "" : $" (input {input.Ds[i]:X2})"));
            }
        }
        for (var i = 0; i < expected.Far.Length && lines.Count < limit; i++)
        {
            if (expected.Far[i] != actual.Far[i] && (TacticsOverlay.DescribeFar(i) ?? extraFar?.GetValueOrDefault(i)) is { } what && !BeyondPathEnd(i, expected))
            {
                lines.Add($"{what} (20A5:{i:X4}): original {expected.Far[i]:X2}, rewrite {actual.Far[i]:X2}" + (input is null ? "" : $" (input {input.Far[i]:X2})"));
            }
        }
        if (extraFar is { Count: > 0 } && lines.Any(l => l.StartsWith("found path", StringComparison.Ordinal)))
        {
            var first = extraFar.Keys.Min();
            lines.Add($"found path original {Convert.ToHexString(expected.Far, first, 60)} rewrite {Convert.ToHexString(actual.Far, first, 60)}");
        }
        if (LootOverlay.Differences(expected, actual) is { } loot)
        {
            lines.Add(loot);
        }
        var shown = new HashSet<int>();
        foreach (var line in lines.ToArray())
        {
            if (line.StartsWith("path[", StringComparison.Ordinal) && int.Parse(line.Split('[')[1].Split(']')[0]) is var k && shown.Add(k))
            {
                var at = TacticsOverlay.PathsAt + TacticsOverlay.PathLength * k;
                lines.Add($"path {k} original {Convert.ToHexString(expected.Far, at, 48)} rewrite {Convert.ToHexString(actual.Far, at, 48)} input {(input is null ? "" : Convert.ToHexString(input.Far, at, 48))}");
            }
        }
        for (var i = 0; expected.Far2 is not null && actual.Far2 is not null && i < expected.Far2.Length && lines.Count < limit; i++)
        {
            if (expected.Far2[i] != actual.Far2[i] && TacticsOverlay.DescribeFar2(i) is { } what)
            {
                lines.Add($"{what} (far2:{i:X4}): original {expected.Far2[i]:X2}, rewrite {actual.Far2[i]:X2}" + (input?.Far2 is null ? "" : $" (input {input.Far2[i]:X2})"));
            }
        }
        for (var i = 0; expected.Far3 is not null && actual.Far3 is not null && i < expected.Far3.Length && lines.Count < limit; i++)
        {
            if (expected.Far3[i] != actual.Far3[i] && TacticsOverlay.DescribeFar3(i) is { } what)
            {
                lines.Add($"{what} (pack segment:{i:X4}): original {expected.Far3[i]:X2}, rewrite {actual.Far3[i]:X2}" + (input?.Far3 is null ? "" : $" (input {input.Far3[i]:X2})"));
            }
        }
        return lines;
    }
}
