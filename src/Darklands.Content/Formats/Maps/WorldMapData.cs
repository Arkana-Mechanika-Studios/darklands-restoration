using System.Buffers.Binary;

namespace Darklands.Content.Formats.Maps;

/// <summary>
/// The expanded terrain IDs stored in a <c>DARKLAND.MAP</c> resource.
/// Original consumers: R030+0x1C4E/0x3122 (header), 0x1DD4 (row-offset reads)
/// and 0x20D2 (three/five-bit runs). This immutable content cache does not model
/// the original 131-row compressed buffer's unused padding or DOS I/O errors.
/// </summary>
// orig: R030+0x1C4E (1462:1C4E) draft (fn matched): fread(&DS:E3F6, 4, 1, f), then
//   swap16(&DS:E3F6) and swap16(&DS:E3F8) (R030+0x3122 (1767:00D2) matched: exchanges the two bytes):
//   the map's width and height are big-endian words.
public sealed class WorldMapData
{
    private const int HeaderSize = 4;
    private const int RowOffsetSize = 4;

    private readonly byte[] terrainIds;
    private readonly (int Offset, int Length)[] compressedRows;
    private readonly byte[] file;

    private WorldMapData(int width, int height, byte[] terrainIds, (int Offset, int Length)[] compressedRows, byte[] file)
    {
        this.file = file;
        Width = width;
        Height = height;
        this.terrainIds = terrainIds;
        this.compressedRows = compressedRows;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Source extent consumed while decoding one row, excluding any trailing bytes.</summary>
    public (int Offset, int Length) CompressedRowRange(int row)
    {
        if ((uint)row >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(row));
        return compressedRows[row];
    }

    /// <summary>The file's byte at <paramref name="offset"/>, as the original's reader (fgetc) gives it: 0xFF (the low byte of EOF, -1) past the end.</summary>
    // orig: R030+0x1DD4 reads the rows with root 06A1:12DC (fgetc) and stores its AL
    public byte FileByte(int offset) => (uint)offset < (uint)file.Length ? file[offset] : (byte)0xFF;

    /// <summary>
    /// Reads the terrain ID at the zero-based map column and row.
    /// </summary>
    public byte ReadTile(int column, int row)
    {
        if ((uint)column >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
        if ((uint)row >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return terrainIds[(row * Width) + column];
    }

    /// <summary>
    /// Decodes the big-endian dimensions, little-endian row offsets, and
    /// three-bit-count/five-bit-terrain row runs in a <c>DARKLAND.MAP</c> file.
    /// </summary>
    public static WorldMapData Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("DARKLAND.MAP is too short to contain its dimensions.");
        }

        var width = BinaryPrimitives.ReadUInt16BigEndian(data[..2]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("DARKLAND.MAP dimensions must be non-zero.");
        }

        var offsetsEnd = HeaderSize + ((long)height * RowOffsetSize);
        if (offsetsEnd > data.Length)
        {
            throw new InvalidDataException("DARKLAND.MAP is too short to contain all row offsets.");
        }

        var tileCount = (long)width * height;
        if (tileCount > int.MaxValue)
        {
            throw new InvalidDataException("DARKLAND.MAP dimensions exceed the supported in-memory size.");
        }

        var terrainIds = new byte[(int)tileCount];
        var compressedRows = new (int Offset, int Length)[height];
        for (var row = 0; row < height; row++)
        {
            var offsetIndex = HeaderSize + (row * RowOffsetSize);
            var rowOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offsetIndex, RowOffsetSize));
            if (rowOffset < offsetsEnd || rowOffset >= data.Length)
            {
                throw new InvalidDataException(
                    $"DARKLAND.MAP row {row} points outside its row-data region.");
            }

            var sourceOffset = (int)rowOffset;
            var decodedColumn = 0;
            while (decodedColumn < width)
            {
                if (sourceOffset >= data.Length)
                {
                    throw new InvalidDataException(
                        $"DARKLAND.MAP row {row} ends before its declared width is decoded.");
                }

                var run = data[sourceOffset++];
                var runLength = run >> 5;
                if (runLength == 0)
                {
                    throw new InvalidDataException($"DARKLAND.MAP row {row} contains a zero-length run.");
                }
                if (runLength > width - decodedColumn)
                {
                    throw new InvalidDataException($"DARKLAND.MAP row {row} exceeds its declared width.");
                }

                var terrainId = (byte)(run & 0x1F);
                var destinationOffset = (row * width) + decodedColumn;
                terrainIds.AsSpan(destinationOffset, runLength).Fill(terrainId);
                decodedColumn += runLength;
            }
            compressedRows[row] = ((int)rowOffset, sourceOffset - (int)rowOffset);
        }

        return new WorldMapData(width, height, terrainIds, compressedRows, data.ToArray());
    }
}
