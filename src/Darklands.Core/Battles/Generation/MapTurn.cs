using System.Buffers.Binary;

namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Turning and mirroring a prepared map (<c>map_turn</c>): r & 3 quarter turns clockwise on
/// screen, then a mirror in x when r ≥ 4. Cells, floors, walls (east/west/north/south nibbles
/// permuted), the party start, doors, objects, part lists, exits, chest groups, object extras,
/// rectangles and the door tables all move with the map.
/// </summary>
// orig: map_turn R004+0x21BA (1091:000A) draft (9 of 13 functions byte-matched per the draft
// notes; 0604, 0800, 0CCA and 1126 re-read against the asm by the spec, scratchpad A_battlefield_wild.md §13)
public static class MapTurn
{
    private const int Span = 0x140;

    // orig: R004+0x21BA (1091:000A) draft (see the type)
    public static void Apply(BattlefieldLevel level, int r, bool turnExitTargets)
    {
        // C: r %= 8 with a positive result.
        r = ((r % 8) + 8) % 8;
        var q = r & 3;
        var mirror = r >= 4;
        var image = level.Image;
        level.PartyFacing = TurnFacing(level.PartyFacing, r);
        TurnCells(level, q, mirror);
        // The party start turns as a point (even when it is still 0, 0).
        TurnPointAt(image, 0x1F4C, 0x1F4E, q, mirror);
        for (var i = 0; i < image[0x1F52]; i++)
        {
            TurnDoor(image, 0x1F54 + 14 * i, q, mirror);
        }
        for (var i = 0; i < image[0x2184]; i++)
        {
            TurnBox(image, 0x2186 + 14 * i, q, mirror, wordSizes: false);
        }
        TurnPartLists(image, q, mirror);
        for (var i = 0; i < image[0x2FB6]; i++)
        {
            var at = 0x2FB8 + 14 * i;
            TurnBox(image, at, q, mirror, wordSizes: false);
            if (turnExitTargets)
            {
                TurnPointAt(image, at + 8, at + 10, q, mirror);
                image[at + 12] = (byte)TurnFacing(image[at + 12], r);
            }
        }
        for (var i = 0; i < image[0x300C]; i++)
        {
            var at = 0x300E + 0x2C * i;
            TurnBox(image, at, q, mirror, wordSizes: false);
            TurnCellPair(image, at + 6, at + 7, q, mirror);
        }
        TurnObjectExtras(image, r, q, mirror);
        for (var i = 0; i < image[0x3326]; i++)
        {
            TurnBox(image, 0x3328 + 10 * i, q, mirror, wordSizes: true);
        }
        TurnDoorTables(image, r, q, mirror);
    }

    /// <summary>A facing 0..7 turned with the map.</summary>
    // orig: facing_turn R004+0x3990 (1091:17E0) draft (byte-matched per the draft notes)
    public static int TurnFacing(int facing, int r)
    {
        var f = (facing + 2 * (r & 3)) & 7;
        return r >= 4 ? (8 - f) % 8 : f;
    }

    /// <summary>Moves cells and floor bytes and permutes the wall nibbles (E W N S at bits 0, 8, 16, 24).</summary>
    // orig: R004+0x227A (1091:00CA) draft (byte-matched); the nibble permutation R004+0x27B4 (1091:0604) draft, re-read against the asm
    private static void TurnCells(BattlefieldLevel level, int q, bool mirror)
    {
        if (q != 0)
        {
            var old = level.Clone();
            for (var x = 0; x < BattlefieldLevel.Size; x++)
            {
                for (var y = 0; y < BattlefieldLevel.Size; y++)
                {
                    var (nx, ny) = q switch { 1 => (39 - y, x), 2 => (39 - x, 39 - y), _ => (y, 39 - x) };
                    level[nx, ny] = new BattlefieldCell(TurnWalls(old[x, y].Bits, q));
                    level.SetFloorByte(nx, ny, old.FloorByte(x, y));
                }
            }
        }
        if (mirror)
        {
            var old = level.Clone();
            for (var x = 0; x < BattlefieldLevel.Size; x++)
            {
                for (var y = 0; y < BattlefieldLevel.Size; y++)
                {
                    level[39 - x, y] = new BattlefieldCell(MirrorWalls(old[x, y].Bits));
                    level.SetFloorByte(39 - x, y, old.FloorByte(x, y));
                }
            }
        }
    }

    // The new (E, W, N, S) walls from the old ones: q1 ← (N, S, W, E); q2 ← (W, E, S, N); q3 ← (S, N, E, W).
    private static uint TurnWalls(uint cell, int q)
    {
        uint e = cell & 0xF, w = (cell >> 8) & 0xF, n = (cell >> 16) & 0xF, s = (cell >> 24) & 0xF;
        var (ne, nw, nn, ns) = q switch { 1 => (n, s, w, e), 2 => (w, e, s, n), _ => (s, n, e, w) };
        return (cell & 0xF0F0_F0F0) | ne | nw << 8 | nn << 16 | ns << 24;
    }

    // Mirror: (E, W, N, S) ← (W, E, N, S). The decoration nibbles stay where they are (faithful).
    private static uint MirrorWalls(uint cell)
    {
        uint e = cell & 0xF, w = (cell >> 8) & 0xF;
        return (cell & ~0x0F0Fu) | w | e << 8;
    }

    /// <summary>A point in map pixels turned (in units of 4 × 2 pixels).</summary>
    // orig: R004+0x28FE (1091:074E) draft (byte-matched per the draft notes); the same pattern for the exit targets of
    //   R004+0x2F54, the type-4 records of 1126 and the door tables of 145a
    // C: x = px / 4, y = py / 2 (signed, toward zero); a quarter turn writes both words, the mirror then rewrites x only:
    //   px = (0x140 - px / 4) << 2 of the value just stored. With no turn and no mirror nothing is written; py is left as it was
    //   by the mirror alone (an odd py stays odd).
    private static void TurnPointAt(byte[] image, int xAt, int yAt, int q, bool mirror)
    {
        int x = Word(image, xAt) / 4, y = Word(image, yAt) / 2;
        switch (q)
        {
            case 1:
                PutWord(image, xAt, (Span - y) << 2);
                PutWord(image, yAt, x << 1);
                break;
            case 2:
                PutWord(image, xAt, (Span - x) << 2);
                PutWord(image, yAt, (Span - y) << 1);
                break;
            case 3:
                PutWord(image, xAt, y << 2);
                PutWord(image, yAt, (Span - x) << 1);
                break;
        }
        if (mirror)
        {
            PutWord(image, xAt, (Span - Word(image, xAt) / 4) << 2);
        }
    }

    /// <summary>A box (x, y words; width, height bytes, or words for the rectangles) turned.</summary>
    // orig: R004+0x2D66 (1091:0BB6) objects draft (byte-matched); R004+0x34E4 (1091:1334) rectangles draft (byte-matched);
    //   the same code in R004+0x2F54 (1091:0DA4) exits and R004+0x3166 (1091:0FB6) chest groups (draft, fn matched)
    //   and the door halves of 0800
    // C: x = x0 / 4, y = x2 / 2, w = x4 >> 2, h = x5 >> 1 (bytes; the rectangles' word sizes: x4 / 4, x6 / 2, signed). Quarter turns 1 and 3
    //   write all four fields, the half turn only x0 and x2; the mirror then rewrites x0 only, from the stored values:
    //   x0 = (0x140 - (unsigned char)(x4 >> 2) - x0 / 4) << 2. A size stored as a byte keeps its low eight bits (h << 2 of a tall box
    //   wraps) and the mirror reads that byte back.
    private static void TurnBox(byte[] image, int at, int q, bool mirror, bool wordSizes)
    {
        int x = Word(image, at) / 4, y = Word(image, at + 2) / 2;
        int w = wordSizes ? Word(image, at + 4) / 4 : image[at + 4] >> 2;
        int h = wordSizes ? Word(image, at + 6) / 2 : image[at + 5] >> 1;
        switch (q)
        {
            case 1:
                PutWord(image, at, (Span - h - y) << 2);
                PutWord(image, at + 2, x << 1);
                PutSizes(h << 2, w << 1);
                break;
            case 2:
                PutWord(image, at, (Span - w - x) << 2);
                PutWord(image, at + 2, (Span - h - y) << 1);
                break;
            case 3:
                PutWord(image, at, y << 2);
                PutWord(image, at + 2, (Span - w - x) << 1);
                PutSizes(h << 2, w << 1);
                break;
        }
        if (mirror)
        {
            var width = wordSizes ? Word(image, at + 4) / 4 : image[at + 4] >> 2;
            PutWord(image, at, (Span - width - Word(image, at) / 4) << 2);
        }

        void PutSizes(int width, int height)
        {
            if (wordSizes)
            {
                PutWord(image, at + 4, width);
                PutWord(image, at + 6, height);
            }
            else
            {
                image[at + 4] = (byte)width;
                image[at + 5] = (byte)height;
            }
        }
    }

    /// <summary>A cell pair (bytes) turned: q1 (39 − cy, cx), q2 (39 − cx, 39 − cy), q3 (cy, 39 − cx), mirror 39 − cx.</summary>
    // C: byte arithmetic: 0x27 − v wraps for v above 0x27.
    private static void TurnCellPair(byte[] image, int xAt, int yAt, int q, bool mirror)
    {
        byte cx = image[xAt], cy = image[yAt];
        (cx, cy) = q switch
        {
            1 => ((byte)(0x27 - cy), cx),
            2 => ((byte)(0x27 - cx), (byte)(0x27 - cy)),
            3 => (cy, (byte)(0x27 - cx)),
            _ => (cx, cy),
        };
        if (mirror)
        {
            cx = (byte)(0x27 - cx);
        }
        image[xAt] = cx;
        image[yAt] = cy;
    }

    /// <summary>Door-half records: the box (a negative x is a flag kept through the turn) and two flagged cell pairs.</summary>
    // orig: R004+0x29B0 (1091:0800) draft, re-read against the asm (all three arms, the mirror and the sign)
    private static void TurnDoor(byte[] image, int at, int q, bool mirror)
    {
        if (q == 0 && !mirror)
        {
            return;
        }
        var negative = Word(image, at) < 0;
        if (negative)
        {
            PutWord(image, at, -Word(image, at));
        }
        TurnBox(image, at, q, mirror, wordSizes: false);
        if (negative)
        {
            PutWord(image, at, -Word(image, at));
        }
        TurnFlaggedPair(image, at + 8, q, mirror);
        TurnFlaggedPair(image, at + 10, q, mirror);
    }

    // Two bytes: low 6 bits a cell coordinate (x then y), bits 7 and 6 flags that turn with the cell's sides.
    private static void TurnFlaggedPair(byte[] image, int at, int q, bool mirror)
    {
        if (image[at] == 0)
        {
            return;
        }
        int a = image[at], b = image[at + 1];
        int a7 = a >> 7 & 1, a6 = a >> 6 & 1, b7 = b >> 7 & 1, b6 = b >> 6 & 1;
        image[at] = (byte)(a & 0x3F);
        image[at + 1] = (byte)(b & 0x3F);
        TurnCellPair(image, at, at + 1, q, false);
        var cx = image[at];
        var cy = image[at + 1];
        (a7, a6, b7, b6) = q switch
        {
            1 => (b6, a7, a6, b7),
            2 => (b7, b6, a7, a6),
            3 => (a6, b7, b6, a7),
            _ => (a7, a6, b7, b6),
        };
        if (mirror)
        {
            cx = (byte)(0x27 - cx);
            (a6, b6) = (b6, a6);
        }
        image[at] = (byte)((cx & 0x3F) | a7 << 7 | a6 << 6);
        image[at + 1] = (byte)((cy & 0x3F) | b7 << 7 | b6 << 6);
    }

    /// <summary>Part lists: −1-terminated runs of (cx &lt;&lt; 8) + cy words.</summary>
    // orig: R004+0x2E7A (1091:0CCA) draft, re-read against the asm (all arms)
    private static void TurnPartLists(byte[] image, int q, bool mirror)
    {
        if (q == 0 && !mirror)
        {
            return;
        }
        for (var k = 0; k < image[0x2488]; k++)
        {
            // C: the list start is a byte offset used as a word index (/2) from 0x24C6.
            var at = 0x24C6 + Word(image, 0x248A + 2 * k) / 2 * 2;
            for (; at + 1 < image.Length && Word(image, at) != -1; at += 2)
            {
                var value = (ushort)Word(image, at);
                int cx = value >> 8, cy = value & 0xFF;
                // C: 16-bit arithmetic as written.
                var turned = q switch
                {
                    1 => ((39 - cy) << 8) + cx,
                    2 => 0x2727 - (cx << 8) - cy,
                    3 => (cy << 8) - cx + 0x27,
                    _ => value,
                };
                if (mirror)
                {
                    turned = ((39 - ((turned >> 8) & 0xFF)) << 8) | (turned & 0xFF);
                }
                PutWord(image, at, turned);
            }
        }
    }

    /// <summary>Object extras by object type: type 1 cell lists (0x3236), type 4 records (0x316E).</summary>
    // orig: R004+0x32D6 (1091:1126) draft, re-read against the asm (both record kinds)
    private static void TurnObjectExtras(byte[] image, int r, int q, bool mirror)
    {
        for (var k = 0; k < image[0x2184]; k++)
        {
            var o = 0x2186 + 14 * k;
            var id = Word(image, o + 10);
            switch (Word(image, o + 6))
            {
                case 1:
                    {
                        var at = 0x3236 + 12 * id;
                        if (at < 0 || at + 12 > image.Length)
                        {
                            // DIVERGENCE: an id outside the table would write elsewhere in the original; skipped.
                            break;
                        }
                        for (var j = 0; j < 4; j++)
                        {
                            if (image[at + j] != 0 && (q != 0 || mirror))
                            {
                                TurnCellPair(image, at + j, at + 4 + j, q, mirror);
                            }
                        }
                        break;
                    }
                case 4:
                    {
                        var at = 0x316E + 10 * id;
                        if (at < 0 || at + 10 > image.Length)
                        {
                            break;
                        }
                        image[at + 7] = (byte)TurnFacing(image[at + 7], r);
                        TurnPointAt(image, at + 2, at + 4, q, mirror);
                        break;
                    }
            }
        }
    }

    /// <summary>The two door tables, always all five entries (they are zero in every prepared map).</summary>
    // orig: R004+0x360A (1091:145A) draft (byte-matched per the draft notes)
    private static void TurnDoorTables(byte[] image, int r, int q, bool mirror)
    {
        for (var i = 0; i < 5; i++)
        {
            var a = 0x3350 + 10 * i;
            image[a + 5] = (byte)TurnFacing(image[a + 5], r);
            TurnPointAt(image, a + 6, a + 8, q, mirror);
            TurnTableCell(image, a + 1, a + 2, q, mirror);
            TurnTableCell(image, a + 3, a + 4, q, mirror);
            var b = 0x3382 + 10 * i;
            image[b + 8] = (byte)TurnFacing(image[b + 8], r);
            TurnPointAt(image, b + 4, b + 6, q, mirror);
            TurnTableCell(image, b + 1, b + 2, q, mirror);
        }
    }

    // C: the mirror of these pairs computes 0x27 − (x >> 2) instead of 0x27 − x (a quirk of the original).
    private static void TurnTableCell(byte[] image, int xAt, int yAt, int q, bool mirror)
    {
        if (image[xAt] == 0)
        {
            return;
        }
        TurnCellPair(image, xAt, yAt, q, false);
        if (mirror)
        {
            image[xAt] = (byte)(0x27 - (image[xAt] >> 2));
        }
    }

    private static short Word(byte[] image, int at) => BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(at));

    private static void PutWord(byte[] image, int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(at), (short)value);
}
