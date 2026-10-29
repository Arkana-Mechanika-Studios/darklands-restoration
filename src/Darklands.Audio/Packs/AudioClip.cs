using System.Buffers.Binary;

namespace Darklands.Audio.Packs;

/// <summary>A decoded sound file: stereo float samples (left, right, -1..1) at the mixer's rate.</summary>
/// <remarks>New code (replacement sound, not part of the game).</remarks>
public sealed class AudioClip
{
    private AudioClip(float[] samples, int sampleRate)
    {
        Samples = samples;
        SampleRate = sampleRate;
    }

    /// <summary>Interleaved stereo samples.</summary>
    public float[] Samples { get; }

    public int SampleRate { get; }

    public int Frames => Samples.Length / 2;

    /// <summary>Reads a .wav (PCM 8/16/24/32-bit or 32-bit float) or .ogg (Ogg Vorbis) file and converts it to <paramref name="sampleRate"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a sound this reads.</exception>
    public static AudioClip Load(string path, int sampleRate)
    {
        var (samples, channels, rate) = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => ReadWav(File.ReadAllBytes(path)),
            ".ogg" => ReadOgg(path),
            _ => throw new InvalidDataException("not a .wav or .ogg file"),
        };
        return new AudioClip(Resample(ToStereo(samples, channels), rate, sampleRate), sampleRate);
    }

    /// <summary>A clip from stereo samples already at their rate (tests, generated sounds).</summary>
    public static AudioClip FromStereo(float[] interleaved, int sampleRate) => new(interleaved, sampleRate);

    private static (float[] Samples, int Channels, int Rate) ReadOgg(string path)
    {
        try
        {
            using var reader = new NVorbis.VorbisReader(path);
            var channels = reader.Channels;
            var rate = reader.SampleRate;
            var all = new List<float>();
            var buffer = new float[channels * 4096];
            int read;
            while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                all.AddRange(buffer.AsSpan(0, read));
            }
            if (channels < 1 || rate < 1)
            {
                throw new InvalidDataException("no audio stream");
            }
            return ([.. all], channels, rate);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EndOfStreamException or NotSupportedException)
        {
            throw new InvalidDataException("not an Ogg Vorbis file (" + e.Message + ")", e);
        }
    }

    /// <summary>The samples of a RIFF WAVE file: PCM (format 1), IEEE float (3) or WAVE_FORMAT_EXTENSIBLE of either.</summary>
    internal static (float[] Samples, int Channels, int Rate) ReadWav(byte[] file)
    {
        var data = file.AsSpan();
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("not a RIFF WAVE file");
        }
        int format = 0, channels = 0, rate = 0, bits = 0;
        var at = 12;
        while (at + 8 <= data.Length)
        {
            var id = data.Slice(at, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 4, 4)), (uint)(data.Length - at - 8));
            var body = data.Slice(at + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && size >= 26)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels < 1 || rate < 1)
                {
                    throw new InvalidDataException("the data comes before the format");
                }
                return (Decode(body, format, bits), channels, rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("no sound data");
    }

    private static float[] Decode(ReadOnlySpan<byte> body, int format, int bits)
    {
        var bytes = bits / 8;
        if (!(format == 1 && bits is 8 or 16 or 24 or 32) && !(format == 3 && bits == 32))
        {
            throw new InvalidDataException($"format {format} with {bits}-bit samples is not supported (PCM 8/16/24/32-bit or 32-bit float)");
        }
        var samples = new float[body.Length / bytes];
        for (var i = 0; i < samples.Length; i++)
        {
            var s = body.Slice(i * bytes, bytes);
            samples[i] = (format, bits) switch
            {
                (3, _) => BinaryPrimitives.ReadSingleLittleEndian(s),
                (_, 8) => (s[0] - 128) / 128f,
                (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                (_, 24) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
            };
        }
        return samples;
    }

    /// <summary>Mono is put on both sides; of more channels the first two are kept.</summary>
    private static float[] ToStereo(float[] samples, int channels)
    {
        if (channels == 2)
        {
            return samples;
        }
        var frames = samples.Length / channels;
        var stereo = new float[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            stereo[2 * f] = samples[f * channels];
            stereo[2 * f + 1] = samples[f * channels + (channels > 1 ? 1 : 0)];
        }
        return stereo;
    }

    /// <summary>Linear interpolation to the mixer's rate.</summary>
    internal static float[] Resample(float[] stereo, int from, int to)
    {
        if (from == to || stereo.Length < 4)
        {
            return stereo;
        }
        var frames = stereo.Length / 2;
        var outFrames = (int)((long)frames * to / from);
        var result = new float[outFrames * 2];
        var step = (double)from / to;
        for (var f = 0; f < outFrames; f++)
        {
            var position = f * step;
            var i = (int)position;
            var t = (float)(position - i);
            var j = Math.Min(i + 1, frames - 1);
            result[2 * f] = stereo[2 * i] + (stereo[2 * j] - stereo[2 * i]) * t;
            result[2 * f + 1] = stereo[2 * i + 1] + (stereo[2 * j + 1] - stereo[2 * i + 1]) * t;
        }
        return result;
    }
}
