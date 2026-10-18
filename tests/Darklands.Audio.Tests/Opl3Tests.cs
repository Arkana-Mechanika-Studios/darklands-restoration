using Darklands.Audio.Synthesis;

namespace Darklands.Audio.Tests;

/// <summary>The FM synthesiser's documented behaviour.</summary>
public sealed class Opl3Tests
{
    private static int[] Render(Opl3 chip, double seconds)
    {
        var samples = new int[(int)(seconds * Opl3.SampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            chip.Generate(out samples[i], out _);
        }
        return samples;
    }

    /// <summary>Channel 0 as a plain sine: silent modulator, carrier at full level, instant attack.</summary>
    private static Opl3 SineAt(int number, int block)
    {
        var chip = new Opl3();
        chip.Write(0x20, 0x21); // modulator: sustained, multiplier 1
        chip.Write(0x40, 0x3F); // modulator silent
        chip.Write(0x60, 0xF0);
        chip.Write(0x23, 0x21); // carrier: sustained, multiplier 1
        chip.Write(0x43, 0x00);
        chip.Write(0x63, 0xF0); // attack 15, decay 0
        chip.Write(0x83, 0x0F); // sustain 0, release 15
        chip.Write(0xC0, 0x00);
        chip.Write(0xA0, (byte)number);
        chip.Write(0xB0, (byte)(0x20 | (block << 2) | (number >> 8)));
        return chip;
    }

    [Fact]
    public void ANewChipIsSilent()
    {
        Assert.All(Render(new Opl3(), 0.05), s => Assert.Equal(0, s));
    }

    [Fact]
    public void TheSineTablePeaksAtFullScale()
    {
        Assert.Equal(4084, Opl3.Wave(0, 256, 0));
        Assert.Equal(-4084, Opl3.Wave(0, 768, 0));
        Assert.Equal(0, Opl3.Wave(1, 600, 0)); // half sine: the negative half is silent
        Assert.Equal(Opl3.Wave(0, 100, 0), Opl3.Wave(2, 612, 0)); // absolute sine mirrors
    }

    [Fact]
    public void AttenuationHalvesPerOctaveOfLogUnits()
    {
        var full = Opl3.Wave(0, 256, 0);
        var half = Opl3.Wave(0, 256, 256);
        Assert.InRange(half, full / 2 - 2, full / 2 + 2);
    }

    [Fact]
    public void FNumber580InBlock4Is440Hz()
    {
        var samples = Render(SineAt(0x244, 4), 1.0);
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
            {
                crossings++;
            }
        }
        Assert.InRange(crossings, 438, 442);
        Assert.InRange(samples.Max(), 4000, 4084);
    }

    [Fact]
    public void KeyOffReleasesTheNote()
    {
        var chip = SineAt(0x244, 4);
        Render(chip, 0.1);
        chip.Write(0xB0, (byte)((4 << 2) | 0x02)); // key off
        var after = Render(chip, 0.05);
        Assert.All(after[^200..], s => Assert.Equal(0, s));
    }

    [Fact]
    public void TheSecondBankNeedsOpl3Mode()
    {
        var chip = new Opl3();
        chip.Write(0x1A0, 0x55);
        Assert.Equal(0, chip.RegisterValue(0x1A0));
        chip.Write(0x105, 0x01);
        chip.Write(0x1A0, 0x55);
        Assert.True(chip.Opl3Mode);
        Assert.Equal(0x55, chip.RegisterValue(0x1A0));
    }

    [Fact]
    public void AStartedTimerReportsItsOverflow()
    {
        var chip = new Opl3();
        chip.Write(0x04, 0x21);
        Assert.Equal(0xC0, chip.Status & 0xE0);
        chip.Write(0x04, 0x80);
        Assert.Equal(0, chip.Status);
    }

    [Fact]
    public void Opl3ChannelsCanBePannedToOneSide()
    {
        var chip = SineAt(0x244, 4);
        chip.Write(0x105, 0x01);
        chip.Write(0xC0, 0x10); // left only
        var right = 0;
        var left = 0;
        for (var i = 0; i < 2000; i++)
        {
            chip.Generate(out var l, out var r);
            left = Math.Max(left, Math.Abs(l));
            right = Math.Max(right, Math.Abs(r));
        }
        Assert.True(left > 1000);
        Assert.Equal(0, right);
    }
}
