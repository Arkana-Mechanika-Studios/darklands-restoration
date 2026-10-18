using System.Buffers.Binary;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;

namespace Darklands.Core.Tests;

/// <summary>
/// map_turn's point and box rules (R004 1091: 074e, 0bb6, 0da4, 0fb6, 1334): only the fields a
/// case writes change, and the mirror works on the values just stored (values worked out from
/// the C by hand).
/// </summary>
public sealed class MapTurnTests
{
    private const int PartyX = 0x1F4C, PartyY = 0x1F4E, ObjectCount = 0x2184, FirstObject = 0x2186;

    private static short Word(byte[] image, int at) => BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(at));

    private static void PutWord(byte[] image, int at, int v) => BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(at), (short)v);

    [Fact]
    public void TheMirrorAloneLeavesThePointsRowAsItWas()
    {
        var level = new BattlefieldLevel();
        PutWord(level.Image, PartyX, 0x40);
        PutWord(level.Image, PartyY, 0x31);
        MapTurn.Apply(level, 4, turnExitTargets: false);
        // C: x1f4c = (0x140 - 0x40 / 4) << 2; x1f4e is not written.
        Assert.Equal((0x140 - 0x10) << 2, Word(level.Image, PartyX));
        Assert.Equal(0x31, Word(level.Image, PartyY));
    }

    [Fact]
    public void AQuarterTurnStoresTheSizesAsBytesAndTheMirrorReadsThemBack()
    {
        var level = new BattlefieldLevel();
        var image = level.Image;
        image[ObjectCount] = 1;
        PutWord(image, FirstObject, 8);
        PutWord(image, FirstObject + 2, 0x11);
        image[FirstObject + 4] = 0x10;
        image[FirstObject + 5] = 0xC8;
        MapTurn.Apply(level, 5, turnExitTargets: false);
        // C: x = 2, y = 8, w = 4, h = 100; x0 = (0x140 - 100 - 8) << 2 = 848, x2 = 2 << 1, x4 = (byte)(100 << 2) = 144, x5 = 4 << 1;
        //    mirror: x0 = (0x140 - (144 >> 2) - 848 / 4) << 2 = 288.
        Assert.Equal(288, Word(image, FirstObject));
        Assert.Equal(4, Word(image, FirstObject + 2));
        Assert.Equal(144, image[FirstObject + 4]);
        Assert.Equal(8, image[FirstObject + 5]);
    }

    [Fact]
    public void AHalfTurnKeepsTheSizes()
    {
        var level = new BattlefieldLevel();
        var image = level.Image;
        image[ObjectCount] = 1;
        PutWord(image, FirstObject, 0x20);
        PutWord(image, FirstObject + 2, 0x30);
        image[FirstObject + 4] = 0x13;
        image[FirstObject + 5] = 0x07;
        MapTurn.Apply(level, 2, turnExitTargets: false);
        // C: x = 8, y = 0x18, w = 4, h = 3: x0 = (0x140 - 4 - 8) << 2, x2 = (0x140 - 3 - 0x18) << 1; x4 and x5 are not written.
        Assert.Equal((0x140 - 4 - 8) << 2, Word(image, FirstObject));
        Assert.Equal((0x140 - 3 - 0x18) << 1, Word(image, FirstObject + 2));
        Assert.Equal(0x13, image[FirstObject + 4]);
        Assert.Equal(0x07, image[FirstObject + 5]);
    }
}
