namespace Darklands.Core.Battles.Tactics;

/// <summary>What the battle needs to know of one animation file (an IMC header and its HITSEQ row).</summary>
/// <param name="Name">The file name without extension ("F60WKSW", "E02DY").</param>
/// <param name="Header">The 13 header words (see the Content CombatAnimation).</param>
/// <param name="StepX">Walking step per path direction 0..9 (null for files without a motion block).</param>
/// <param name="FrameCount">Frames per facing.</param>
/// <param name="Hits">The HITSEQ row of the weapon code (null for dying files, which keep the slot's previous rows).</param>
public sealed record AnimationInfo(string Name, short[] Header, sbyte[]? StepX, sbyte[]? StepY, int FrameCount, HitRow? Hits);

/// <summary>A HITSEQ row: 0..4 attack frames, 5..9 aim/parry frames, the three miss frames, the attack length (+ bit 7 no parry), the aim length (+ bit 7 hold).</summary>
public sealed record HitRow(byte[] Frames, byte A, byte B, byte C, byte Flags, byte Aim)
{
    /// <summary>The 30 sequence bytes of a slot (DS:A3E7[slot]): the row's 10 frames, then the four 5-frame miss sequences.</summary>
    // orig: R002+0x17EA (0E76:17EA) draft (the selector pointers b b c a b, c a b c b, a c c b b, c b a b c)
    public byte[] Sequences()
    {
        var s = new byte[30];
        Frames.AsSpan(0, 10).CopyTo(s);
        byte[] selector = [B, B, C, A, B, C, A, B, C, B, A, C, C, B, B, C, B, A, B, C];
        selector.CopyTo(s, 10);
        return s;
    }
}

/// <summary>Finds animation files by picture code, posture ("WK", "CB", "DY") and weapon code.</summary>
public interface IAnimationLibrary
{
    AnimationInfo? Find(string code, string posture, string weapon);

    /// <summary>TACANIM.DB: the walking and fighting half sizes of a picture family ("E02").</summary>
    (int HalfWidth, int HalfHeight, int CombatHalfWidth, int CombatHalfHeight)? Sizes(string family);
}

/// <summary>
/// One animation slot's tables as the battle keeps them (indexed by <c>cbt_anim_set</c>: 0..4 the
/// party, 5/6 enemy walking, 7/8 enemy fighting, 9/10 enemy dying). Loading a file overwrites the
/// header values; the step tables only from files with a motion block; the hit sequences only from
/// files with a weapon code.
/// </summary>
// orig: sprite_file_load R014+0x3D2C (0E76:3D2C) draft (the per-slot tables DS:1182, DS:118E,
// DS:C763, DS:C799, DS:C7D2, DS:C803, DS:C83F, DS:C8CA, DS:D5DE, DS:C681, DS:C6F4, DS:C908, DS:A3E7, DS:C84A, DS:C86B, DS:C8A7)
public sealed class AnimationSlot
{
    /// <summary>DS:11B2[slot]: the file currently loaded ("" none).</summary>
    public string FileName { get; private set; } = "";

    public int HalfWidth { get; private set; } = 5;

    public int HalfHeight { get; private set; } = 2;

    /// <summary>DS:119A / DS:11A6: the bigger box used in a real fight (header words 0 + 2, 1 + 3; enemies: TACANIM.DB).</summary>
    public int CombatHalfWidth { get; private set; } = 6;

    public int CombatHalfHeight { get; private set; } = 3;

    public int SizeClass { get; private set; }

    /// <summary>DS:C799: can push and be pushed back.</summary>
    public bool CanBePushed { get; private set; }

    /// <summary>DS:C7D2: shows misses (otherwise a miss is shown as a parry).</summary>
    public bool CanMiss { get; private set; } = true;

    /// <summary>DS:C803: leaves a loot pile when it dies.</summary>
    public bool DropsLoot { get; private set; }

    /// <summary>DS:C83F: corpse handling (2 none).</summary>
    public int CorpseKind { get; private set; }

    public sbyte[] StepX { get; private set; } = new sbyte[10];

    public sbyte[] StepY { get; private set; } = new sbyte[10];

    /// <summary>DS:C908: frames per facing of the loaded file.</summary>
    public int FrameCount { get; private set; } = 1;

    /// <summary>DS:A3E7[slot]: 10 HITSEQ frames then the four miss sequences.</summary>
    public byte[] Sequences { get; private set; } = new byte[30];

    /// <summary>DS:C84A: attack sequence length; DS:C86B: no parry sequence; DS:C8A7: aim/parry length | 0x80 hold.</summary>
    public int AttackLength { get; private set; }

    public bool NoParrySequence { get; private set; }

    public byte AimLength { get; private set; }

    /// <summary>The four sizes of an enemy family from TACANIM.DB (R025+0x0D3C).</summary>
    // orig: R025+0x0D3C (1EF8:0D3C) draft
    public void SetSizes(int halfWidth, int halfHeight, int combatHalfWidth, int combatHalfHeight)
    {
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
        CombatHalfWidth = combatHalfWidth;
        CombatHalfHeight = combatHalfHeight;
    }

    public void Load(AnimationInfo info)
    {
        FileName = info.Name;
        HalfWidth = info.Header[0];
        HalfHeight = info.Header[1];
        // DS:119A = w0 + w2, DS:11A6 = w1 + w3 (spec E §1.2).
        CombatHalfWidth = info.Header[0] + info.Header[2];
        CombatHalfHeight = info.Header[1] + info.Header[3];
        SizeClass = info.Header[4];
        CanBePushed = info.Header[5] == 1;
        CanMiss = info.Header[6] != 0;
        DropsLoot = info.Header[7] == 1;
        CorpseKind = info.Header[8];
        if (info.StepX is { } sx && info.StepY is { } sy)
        {
            StepX = sx;
            StepY = sy;
        }
        FrameCount = info.FrameCount;
        if (info.Hits is { } hits)
        {
            Sequences = hits.Sequences();
            AttackLength = hits.Flags & 0x7F;
            NoParrySequence = hits.Flags >> 7 == 1;
            AimLength = hits.Aim;
        }
    }
}
