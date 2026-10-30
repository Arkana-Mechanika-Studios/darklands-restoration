using System.Buffers.Binary;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Battles;

/// <summary>
/// One animation file of a combatant (an IMC of the A00C/C00C/F01C/F60C/E00C/M00C catalogues:
/// walking "WK", fighting "CB" or dying "DY"): the header values the battle keeps per animation set
/// and the frames, 8 facings each (facing 0 back view, 2 right, 4 towards the viewer, 6 left).
/// </summary>
// orig: sprite-file-read R014+0x3D2C (0E76:3D2C) draft (header meanings per spec E §1.2,
// verified on F60/E02/E03/M01 files)
public sealed class CombatAnimation
{
    private readonly RowRunSprite[] _frames;

    private CombatAnimation(short[] header, sbyte[] stepX, sbyte[] stepY, int height, int[] widths, int[] footX, RowRunSprite[] frames, int frameCount)
    {
        Header = header;
        StepX = stepX;
        StepY = stepY;
        Height = height;
        Widths = widths;
        FootX = footX;
        _frames = frames;
        FrameCount = frameCount;
    }

    /// <summary>The 13 header words: footprint half width/height (0, 1) and their outer parts (2, 3), size class (4),
    /// can be pushed (5), can miss (6), leaves loot (7), corpse kind (8), y base (9), cursor y (10), x origin (11), motion flag (12).</summary>
    public short[] Header { get; }

    public int HalfWidth => Header[0];

    public int HalfHeight => Header[1];

    /// <summary>DS:C763: the size class of the mouse hot spot (0..4).</summary>
    public int SizeClass => Header[4];

    /// <summary>DS:C7D2: 0 means a plain miss is shown as a parry.</summary>
    public bool CanMiss => Header[6] != 0;

    /// <summary>DS:C803: leaves a loot pile when it dies.</summary>
    public bool LeavesLoot => Header[7] == 1;

    /// <summary>DS:C83F: 2 = no corpse.</summary>
    public int CorpseKind => Header[8];

    /// <summary>DS:C8CA: added to the foot y (with the height subtracted) to place the sprite.</summary>
    public int BaseY => Header[9];

    /// <summary>DS:D5DE: the target cursor's y offset.</summary>
    public int CursorY => Header[10];

    /// <summary>DS:C681/DS:C6F4: one walking step per path direction 0..9 (empty for dying files).</summary>
    public sbyte[] StepX { get; }

    public sbyte[] StepY { get; }

    /// <summary>DS:D5D3: height of every frame.</summary>
    public int Height { get; }

    /// <summary>DS:D55D: frame width per facing.</summary>
    public int[] Widths { get; }

    /// <summary>DS:D505: the foot point's x within the frame, per facing.</summary>
    public int[] FootX { get; }

    /// <summary>DS:C908: frames per facing.</summary>
    public int FrameCount { get; }

    /// <summary>The frame shown for <paramref name="frame"/> and <paramref name="facing"/>.</summary>
    public RowRunSprite Frame(int frame, int facing) =>
        frame >= 0 && frame < FrameCount && facing is >= 0 and < 8 ? _frames[frame * 8 + facing] : RowRunSprite.Empty;

    /// <summary>Decodes an IMC file; with <paramref name="assetName"/> ("F01C.CAT/F01WKSW.IMC") frame f of facing d is named "…#f.d".</summary>
    public static CombatAnimation Decode(byte[] decoded, string? assetName = null)
    {
        var sprite = ImcSprite.Decode(decoded);
        var header = new short[13];
        for (var i = 0; i < 13; i++)
        {
            header[i] = BinaryPrimitives.ReadInt16LittleEndian(decoded.AsSpan(2 * i));
        }
        var cursor = 26;
        sbyte[] stepX = [], stepY = [];
        if (header[12] != -1)
        {
            stepX = [.. decoded.AsSpan(cursor, 10).ToArray().Select(b => (sbyte)b)];
            stepY = [.. decoded.AsSpan(cursor + 10, 10).ToArray().Select(b => (sbyte)b)];
            cursor += 20;
        }
        var height = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(cursor));
        var widths = new int[8];
        var footX = new int[8];
        for (var f = 0; f < 8; f++)
        {
            widths[f] = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(cursor + 2 + 2 * f));
            // C: DS:D505 = (char)u − hot[f] where u is header word 11.
            footX[f] = (sbyte)header[11] - BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(cursor + 18 + 2 * f));
        }
        var frames = new RowRunSprite[sprite.FrameCount * 8];
        for (var k = 0; k < frames.Length; k++)
        {
            frames[k] = RowRunSprite.Decode(sprite.GetFrameBytes(k / 8, k % 8), 0, assetName is null ? null : $"{assetName}#{k / 8}.{k % 8}");
        }
        return new CombatAnimation(header, stepX, stepY, height, widths, footX, frames, sprite.FrameCount);
    }

    /// <summary>The same animation with every colour passed through a table (party colours, enemy palettes).</summary>
    public CombatAnimation Recolored(ReadOnlySpan<byte> table)
    {
        var frames = new RowRunSprite[_frames.Length];
        for (var k = 0; k < frames.Length; k++)
        {
            frames[k] = _frames[k].Recolored(table);
        }
        return new CombatAnimation(Header, StepX, StepY, Height, Widths, FootX, frames, FrameCount);
    }
}
