using System.Security.Cryptography;
using Darklands.Platform;

namespace Darklands.DevTools.Tests;

/// <summary>The inspector names what drew a pixel and where the host is, and never changes the frame.</summary>
public sealed class InspectorTests
{
    private static Screen Frame(out int owner)
    {
        var frame = new Screen();
        owner = ScreenOwners.IdOf("TEST.PIC#inspector", 40, 30);
        var at = 12 * Screen.Width + 34;
        frame.Pixels[at] = 0x2A;
        frame.Owners[at] = owner;
        frame.Texels[at] = ScreenTexel.Pack(5, 7, flipX: true);
        return frame;
    }

    [Fact]
    public void APixelNamesItsAssetItsTexelAndTheStep()
    {
        var frame = Frame(out _);
        var lines = Inspector.Describe(frame, 34, 12, new InspectedMoment(17));
        Assert.Equal(
        [
            "pixel 34,12: palette index 42",
            "asset TEST.PIC#inspector, its pixel 5,7 (mirrored left to right), size 40x30",
            "step 17",
        ], lines);
    }

    [Fact]
    public void APixelNobodyOwnsSaysSoAndAHostWithoutStepsGivesNone()
    {
        var lines = Inspector.Describe(Frame(out _), 0, 0, new InspectedMoment(-1));
        Assert.Equal(["pixel 0,0: palette index 0", "asset: none recorded"], lines);
    }

    [Fact]
    public void APointOutsideTheFrameIsSaid()
    {
        Assert.Equal(["pixel 320,5: outside the 320x200 frame"], Inspector.Describe(new Screen(), 320, 5, new InspectedMoment(0)));
    }

    [Fact]
    public void TheFrameIsOnlyRead()
    {
        var frame = Frame(out _);
        static string Hash(Screen s) => Convert.ToHexString(SHA256.HashData(
            s.Pixels.Concat(s.Owners.SelectMany(BitConverter.GetBytes)).Concat(s.Texels.SelectMany(BitConverter.GetBytes)).ToArray()));
        var before = Hash(frame);
        for (var y = 0; y < Screen.Height; y += 7)
        {
            for (var x = 0; x < Screen.Width; x += 7)
            {
                Inspector.Describe(frame, x, y, new InspectedMoment(1));
            }
        }
        Assert.Equal(before, Hash(frame));
    }
}
