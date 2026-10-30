using Darklands.Content.Formats.Maps;

namespace Darklands.Simulation.Overland;

/// <summary>
/// The travel map's row buffer: 0x83 slots of 0xD2 bytes holding the run-length rows of DARKLAND.MAP from the view's first row on, as the
/// original keeps them while the map screen is open. The map drawing decodes the cell buffer's rows from it.
/// </summary>
// orig: [DS:2870]:0000 (far pointer DS:866C/866E), allocated by R030+0x1C76 (root 05BA:0004(0x7530, 0, DS:2912)) when the map screen opens
//   (R030+0x022B) and freed when it is left (root 05BA:00C2 at R030+0x026C, R030+0x0FDB, R037+0x8E38); filled by R030+0x1DD4
// C: a row is read byte by byte (fgetc) into the start of its slot until its runs cover the map's width (DS:E3F6); the rest of the slot keeps
//   what was there: the bytes of an earlier, longer row that sat in that slot (rows are 0x8E..0xD2 bytes long), else the block's first content.
//   The map drawing's decoder runs on into those bytes and then into the next slot when it reads past a row's end (the view's column 18 at the
//   map's east border, Decode).
public sealed class RowBuffer
{
    public const int SlotSize = 0xD2;
    public const int Slots = 0x83;

    /// <summary>The size of the block (0x7530 bytes: the 0x83 slots and 0x9BA bytes after them that no read reaches).</summary>
    public const int BlockSize = 0x7530;

    private readonly WorldMapData _file;
    private readonly byte[] _block = new byte[BlockSize];

    public RowBuffer(WorldMapData file) => _file = file;

    /// <summary>The map row of slot 0 as the last read left it (-1 before the first), i.e. DS:286E as of the last call of 1dd4.</summary>
    public int Top { get; private set; } = -1;

    /// <summary>The byte at <paramref name="offset"/> of the block.</summary>
    public byte this[int offset] => _block[offset];

    /// <summary>The map screen opens with the view's first row <paramref name="top"/>: a new block, the 0x83 rows read into it.</summary>
    // orig: R030+0x1C76: DS:2870 = root 05BA:0004(0x7530, 0, DS:2912); DS:866C/866E = DS:2870:0000; R030+0x1DD4(file, DS:286E, -1)
    // DIVERGENCE: root 05BA:0004 takes the block from DOS (INT 21h/48h through 044A:014E) and writes only its 8-byte name in the paragraph
    //   before it: the block's bytes are what that memory held before (an earlier map buffer, other blocks freed since: the DOS memory
    //   layout). Here it starts as zeros, as in every machine state captured in the emulator (39 states of the travel sessions, the second
    //   map visits included: no byte of a slot past its row's end and none after the slots is non-zero). Only the decoder's reads past a
    //   row's end see them (Decode).
    public void Open(int top)
    {
        Array.Clear(_block);
        Top = -1;
        Load(top, -1);
    }

    /// <summary>The rows from <paramref name="top"/> on, after the rows from <paramref name="old"/> on (-1: none, read them all).</summary>
    // orig: R030+0x1DD4(file, top, old) (1462:1DD4): old == top: nothing. old == -1: read rows top .. top + 0x82 into slots 0 .. 0x82.
    //   old < top (d = top - old): memmove(slot 0, slot d, (0x83 - d) * 0xD2), then read rows top + d .. top + 0x82 into slots d .. 0x82.
    //   old > top (d = old - top): memmove(slot d, slot 0, d * 0xD2), then read rows top .. top + d - 1 into slots 0 .. d - 1.
    //   A read seeks the file to the offset in its row table (fseek(row * 4 + 4), fread 4 bytes, fseek) and then reads the rows one after
    //   the other (fgetc, root 06A1:12DC) until each row's runs reach the map's width.
    // BUG: scrolling up moves d slots, not 0x83 - d: slots 2d .. 0x82 keep the rows they held, d rows further down the map than their new
    //   place. With the scroll's d = 0x41 only slot 0x82 is stale; when the buffer's top is clamped to row 0 (old top 0x18 or 0x30 from
    //   world-map-start, d = old) the rows from slot 2d on are 0x18 / 0x30 rows off until the next scroll down reads them again. Reproduced:
    //   the map drawing (and the cell reads of the step and the camp) show those rows (checked by direct calls of 1dd4 in the original).
    public void Load(int top, int old)
    {
        if (old == top)
        {
            return;
        }
        int start, count, seek;
        if (old == -1)
        {
            (start, count, seek) = (0, Slots, top);
        }
        else if (old < top)
        {
            var d = top - old;
            Array.Copy(_block, d * SlotSize, _block, 0, (Slots - d) * SlotSize);
            (start, count, seek) = (d, Slots - d, top + d);
        }
        else
        {
            var d = old - top;
            Array.Copy(_block, 0, _block, d * SlotSize, d * SlotSize);
            (start, count, seek) = (0, d, top);
        }
        Top = top;
        var table = seek * 4 + 4;
        var at = _file.FileByte(table) | _file.FileByte(table + 1) << 8 | _file.FileByte(table + 2) << 16 | _file.FileByte(table + 3) << 24;
        for (var slot = start; slot < start + count; slot++)
        {
            var decoded = 0;
            for (var i = 0; decoded < _file.Width; i++)
            {
                var value = _file.FileByte(at++);
                _block[slot * SlotSize + i] = value;
                decoded += value >> 5;
            }
        }
    }

    /// <summary>
    /// The 19 cells of the map drawing's buffer row from slot <paramref name="slot"/>, map columns <paramref name="column"/> - 1 ..
    /// <paramref name="column"/> + 17, decoded as the drawing does.
    /// </summary>
    // orig: R030+0x20D2 (R030+0x2104..0x2182): per buffer row y, bx = (DS:286C + y) * 0xD2 + DS:866C - 0xD2 (slot DS:286C + y - 1); bytes si
    //   = 0, 1, .. give runs of b >> 5 cells of code b & 0x1F; cells from map column col - 1 on are stored until 19 are, i.e. while the
    //   count of cells read is below col + 0x12. Nothing stops it at the row's end: at the map's east border (DS:286A = width - 0x11, map
    //   columns up to width) the last cell is decoded from the bytes after the row in its slot and, if those are zeros (runs of none),
    //   from the next slot: the next map row's first cells.
    // DIVERGENCE: the decoder can run past the block's end (the slot 0x83 that no read fills, below the map's bottom view, or the east end of
    //   slot 0x82): it then reads the DOS memory control block after the block and what follows it (the memory layout). Here the cells
    //   not yet decoded there are 0.
    public void Decode(int slot, int column, Span<byte> cells)
    {
        cells.Clear();
        var stored = 0;
        var read = 0;
        for (var at = slot * SlotSize; column + 0x12 > read; at++)
        {
            if ((uint)at >= BlockSize)
            {
                return;
            }
            var value = _block[at];
            for (var run = value >> 5; run > 0 && stored < cells.Length; run--)
            {
                if (column - 1 <= read)
                {
                    cells[stored++] = (byte)(value & 0x1F);
                }
                read++;
            }
            if (stored == cells.Length)
            {
                return;
            }
        }
    }
}
