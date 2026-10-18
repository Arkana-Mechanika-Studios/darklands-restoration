using System.Buffers.Binary;

namespace Darklands.Content.Formats.Graphics;

/// <summary>Decoded IMC metadata, frame offsets and row-run payload.</summary>
/// <remarks>
/// Resource layout consumed by original 0014:3D2C, particularly 3E17..4158.
/// Metadata retains the original words, including high bytes the owner may
/// ignore. Frame offsets are payload-relative byte offsets rather than DOS
/// segments. Catalog selection, HITSEQ selection, actor metadata publication
/// and animation selection are caller behavior, not this format decoder.
/// </remarks>
public sealed class ImcSprite
{
    private readonly byte[] metadata;
    private readonly byte[] payload;
    private readonly int[] frameOffsets;

    private ImcSprite(byte[] metadata, byte[] payload, int[] frameOffsets, int frameCount)
    {
        this.metadata = metadata;
        this.payload = payload;
        this.frameOffsets = frameOffsets;
        FrameCount = frameCount;
        FrameByteOffsets = Array.AsReadOnly(frameOffsets);
    }

    public const int DirectionCount = 8;
    public int FrameCount { get; }
    public ReadOnlySpan<byte> Metadata => metadata;
    public ReadOnlySpan<byte> Payload => payload;
    public IReadOnlyList<int> FrameByteOffsets { get; }

    public static ImcSprite Decode(ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < 26) throw new InvalidDataException("IMC metadata header is truncated.");
        // Twenty optional metadata bytes follow the thirteen leading words.
        int cursor = 26 + (BinaryPrimitives.ReadUInt16LittleEndian(decoded.Slice(24, 2)) == 0xFFFF ? 0 : 20);
        if (decoded.Length - cursor < 38) throw new InvalidDataException("IMC direction metadata is truncated.");
        // One metadata word and two eight-word direction vectors precede
        // the frame-count word. Original 4025/4102 consumes its signed low byte.
        int frames = unchecked((sbyte)decoded[cursor + 34]);
        if (frames <= 0) throw new InvalidDataException("IMC requires a supported positive frame count.");
        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(decoded.Slice(cursor + 36, 2));
        int table = cursor + 38;
        int metadataLength = table + frames * DirectionCount * 2;
        if (metadataLength > decoded.Length || decoded.Length - metadataLength != payloadLength)
            throw new InvalidDataException("IMC frame table and declared payload do not match the decoded resource.");
        var offsets = new int[frames * DirectionCount];
        for (int index = 0; index < offsets.Length; index++)
        {
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(decoded.Slice(table + index * 2, 2)) * 16;
            if (offset > payloadLength - 2)
                throw new InvalidDataException("IMC frame offset lies outside its row-run payload.");
            offsets[index] = offset;
        }
        return new ImcSprite(decoded.Slice(0, metadataLength).ToArray(),
            decoded.Slice(metadataLength).ToArray(), offsets, frames);
    }

    /// <summary>Returns a frame's encoded row-run source within the retained payload.</summary>
    public ReadOnlySpan<byte> GetFrameBytes(int frame, int direction)
    {
        if ((uint)frame >= (uint)FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        if ((uint)direction >= DirectionCount) throw new ArgumentOutOfRangeException(nameof(direction));
        return payload.AsSpan(frameOffsets[frame * DirectionCount + direction]);
    }

    /// <summary>Applies a supplied color translation to the payload after its four-byte prefix.</summary>
    /// <remarks>
    /// Original 0014:1060A transforms every supplied payload byte after offset 4,
    /// including encoded row data. The caller owns translation-table selection
    /// and its persistent state. This resource operation requires a nonempty
    /// suffix and does not represent the original zero-count wrapping path.
    /// </remarks>
    public ImcSprite WithPaletteTranslation(ReadOnlySpan<byte> translation)
    {
        if (translation.Length != 256) throw new ArgumentException("IMC color translation requires 256 entries.", nameof(translation));
        if (payload.Length <= 4) throw new InvalidDataException("IMC palette translation requires a nonempty payload suffix.");
        byte[] translated = payload.ToArray();
        for (int index = 4; index < translated.Length; index++) translated[index] = translation[translated[index]];
        return new ImcSprite(metadata, translated, frameOffsets, FrameCount);
    }
}
