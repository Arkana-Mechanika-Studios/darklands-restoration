namespace Darklands.Audio;

/// <summary>Produces sound: 16-bit signed stereo frames, left then right.</summary>
public interface IPcmSource
{
    void Render(Span<short> interleavedStereo);
}

/// <summary>A sound device (or file) that pulls frames from a source at its own rate.</summary>
public interface IPcmOutput : IDisposable
{
    int SampleRate { get; }

    /// <summary>Starts pulling from <paramref name="source"/>, on the device's own thread.</summary>
    void Start(IPcmSource source);

    /// <summary>Frames the listener has heard so far (an estimate: what was pulled less what waits in the device).</summary>
    long FramesPlayed { get; }

    /// <summary>The most frames the device pulls ahead of what is heard.</summary>
    int LatencyFrames { get; }
}

/// <summary>Writes 16-bit stereo PCM as a .wav file.</summary>
public static class WavFile
{
    public static void Write(string path, ReadOnlySpan<short> interleavedStereo, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataBytes = interleavedStereo.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)2);          // stereo
        writer.Write(sampleRate);
        writer.Write(sampleRate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in interleavedStereo)
        {
            writer.Write(sample);
        }
    }
}
