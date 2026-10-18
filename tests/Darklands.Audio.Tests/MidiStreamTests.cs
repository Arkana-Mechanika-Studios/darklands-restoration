namespace Darklands.Audio.Tests;

/// <summary>The MPU-401 byte stream grouped into MIDI messages.</summary>
public sealed class MidiStreamTests
{
    internal sealed class Recorder : IMidiSynth
    {
        public List<uint> Short { get; } = [];

        public List<byte[]> Sysex { get; } = [];

        public void PlayShortMessage(uint message) => Short.Add(message);

        public void PlaySysex(ReadOnlySpan<byte> sysex) => Sysex.Add(sysex.ToArray());

        public void Render(Span<short> interleavedStereo) => interleavedStereo.Clear();

        public void Dispose()
        {
        }
    }

    private static Recorder Feed(params byte[] bytes)
    {
        var recorder = new Recorder();
        var stream = new MidiStream(recorder);
        foreach (var b in bytes)
        {
            stream.Add(b);
        }
        return recorder;
    }

    [Fact]
    public void RunningStatusRepeatsTheLastStatus()
    {
        // the Roland driver's first bytes: all notes off, reset controllers, volume 100, pan 64 on channel 10
        var r = Feed(0xB9, 0x7B, 0x00, 0x79, 0x00, 0x07, 0x64, 0x0A, 0x40);
        Assert.Equal([0x007BB9u, 0x0079B9u, 0x6407B9u, 0x400AB9u], r.Short);
    }

    [Fact]
    public void ProgramChangesHaveOneDataByte()
    {
        var r = Feed(0xC1, 0x05, 0x06, 0x91, 0x3C, 0x7F);
        Assert.Equal([0x05C1u, 0x06C1u, 0x7F3C91u], r.Short);
    }

    [Fact]
    public void ExclusiveMessagesAreKeptWhole()
    {
        var r = Feed(0xF0, 0x41, 0x10, 0x16, 0x12, 0x10, 0x00, 0x01, 0x05, 0x6A, 0xF7, 0x90, 0x40, 0x40);
        Assert.Single(r.Sysex);
        Assert.Equal(new byte[] { 0xF0, 0x41, 0x10, 0x16, 0x12, 0x10, 0x00, 0x01, 0x05, 0x6A, 0xF7 }, r.Sysex[0]);
        Assert.Equal([0x404090u], r.Short);
    }

    [Fact]
    public void RealTimeBytesPassBetweenTheBytesOfAMessage()
    {
        var r = Feed(0x90, 0x3C, 0xF8, 0x64);
        Assert.Equal([0xF8u, 0x643C90u], r.Short);
    }

    [Fact]
    public void DataWithoutAStatusIsDropped()
    {
        var r = Feed(0x3C, 0x40, 0xF6, 0x10);
        Assert.Equal([0xF6u], r.Short);
    }
}
