using System.IO.Compression;
using System.Security.Cryptography;
using Darklands.Audio;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The opening on one clock, against the original's shared-clock capture (OpeningCapture.txt.gz:
/// its 1 171 cel times, its 32 sample starts, its MPU byte times and the SHA-256 of its 13 364 MPU
/// bytes). The real Cinematics plays the opening on a host whose clock is the sound's: the Roland
/// driver runs in the emulated PC, the game timer follows the measured track, and the cels, the
/// MIDI and the samples must fall where the original's did. Needs the game files.
/// </summary>
public sealed class OpeningTimingTests
{
    private const int Rate = 22050;

    [Fact]
    public void TheTrackIsTheWholeOpening()
    {
        var track = OpeningTiming.Track;
        Assert.Equal(9104, track.TicksPerFrame.Count);
        Assert.Equal(7258, track.Ticks);
        Assert.All(track.TicksPerFrame, t => Assert.InRange(t, 0, 2));
        // the cels: 32 ticks after the first tune, six ticks apart, the next file's first cel the measured ticks after its tune
        var counts = new[] { 358, 100, 190, 70, 167, 231, 55 };
        long tune = 0, last = 0;
        for (var n = 0; n < counts.Length; n++)
        {
            tune = n == 0 ? 0 : last + OpeningTiming.Files[n].TuneAfterLastCel;
            last = tune + OpeningTiming.Files[n].FirstCelAfterTune + 6L * (counts[n] - 1);
        }
        Assert.Equal(track.Ticks, last);
    }

    [Fact]
    public void TheCelsTheMusicAndTheSamplesKeepTheOriginalsTime()
    {
        if (GameFolder.Files is not { } files || !files.Exists("RSOUND.DLC") || !files.Exists("OPENING2.PAN"))
        {
            return;
        }
        var capture = Capture.Load();
        var events = new List<AudioEvent>();
        var setup = SoundSetup.ForChoice(files, "mt32") with { Card = new SoundCardSettings(SoundCard.SoundBlasterPro2, 0x220, 5, 1) };
        var audio = new SynthAudio(files, Rate, setup, new NoModule(), events.Add);
        var host = new SoundClockHost(audio);
        var ui = new GameUi(host, new GameAssets(files));
        var cels = new List<long>();
        ui.Trace = line =>
        {
            if (line.StartsWith("cel ", StringComparison.Ordinal))
            {
                cels.Add(long.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture));
            }
        };
        new Cinematics(ui).Opening();
        host.Sleep(3000); // the capture's MPU bytes go on 2.2 s after the last cel
        Assert.Null(audio.Fault);

        // the MPU bytes: exactly the original's
        var midi = events.Where(e => e.Kind == AudioEventKind.MidiByte).ToList();
        var bytes = midi.Select(e => (byte)e.Value).ToArray();
        Assert.True(bytes.Length >= capture.MpuCount, $"{bytes.Length} MPU bytes");
        Assert.Equal(capture.MpuSha256, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, capture.MpuCount))));

        // the cels: on the original's frames (the capture's frames are 14.268048 ms; ours 17024 timer counts)
        var tickFrames = events.Where(e => e.Kind == AudioEventKind.Tick).GroupBy(e => e.Tick).ToDictionary(g => g.Key, g => g.First().Frame);
        Assert.Equal(capture.Cels.Length, cels.Count);
        var start = tickFrames[cels[0]];
        double Ms(long frame) => (frame - start) * 1000.0 / Rate;
        for (var j = 0; j < cels.Count; j++)
        {
            Assert.InRange(Ms(tickFrames[cels[j]]) - capture.Cels[j] / 1000.0, -5.0, 5.0);
        }

        // the samples: each within one frame of the original's DMA start (the driver's work before it included)
        var samples = events.Where(e => e.Kind == AudioEventKind.SampleTransfer && e.Context == 'f').ToList();
        Assert.Equal(capture.Samples.Length, samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            var ours = Ms(samples[i].Frame) + samples[i].Delay * 1000;
            Assert.InRange(ours - capture.Samples[i].Time / 1000.0, -14.3, 14.3);
        }

        // the MIDI's times: nearly all in the same frame as the original's
        var same = 0;
        for (var i = 0; i < capture.ByteTimes.Length; i++)
        {
            var frames = (Ms(midi[1168 + i].Frame) - capture.ByteTimes[i] / 1000.0) / 14.268048;
            same += Math.Abs(frames - 0.12) < 0.5 ? 1 : 0; // a plain tick's bytes came 0.12 frame before the capture's cel times
        }
        Assert.True(same > capture.ByteTimes.Length * 95 / 100, $"{same} of {capture.ByteTimes.Length} MPU bytes in the original's frame");
    }

    private sealed record Capture(int MpuCount, string MpuSha256, long[] Cels, (long Time, int Selector, int Length)[] Samples, long[] ByteTimes)
    {
        public static Capture Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OpeningCapture.txt.gz");
            using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
            int count = 0;
            var sha = "";
            List<long> cels = [], bytes = [];
            List<(long, int, int)> samples = [];
            static long N(string s) => long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            while (reader.ReadLine() is { } line)
            {
                var p = line.Split(' ');
                switch (p[0])
                {
                    case "mpu": count = (int)N(p[1]); sha = p[2]; break;
                    case "cels": cels.AddRange(p.Skip(1).Select(N)); break;
                    case "bytes": bytes.AddRange(p.Skip(1).Select(N)); break;
                    case "sample": samples.Add((N(p[1]), (int)N(p[2]), (int)N(p[3]))); break;
                }
            }
            return new Capture(count, sha, [.. cels], [.. samples], [.. bytes]);
        }
    }

    /// <summary>A host whose clock is the sound's: waiting renders the sound, the timer is the sound's ticks.</summary>
    private sealed class SoundClockHost(SynthAudio audio) : IHost, IVideo, IInput, IClock
    {
        private readonly short[] _buffer = new short[2 * Rate];
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => audio;
        public IClock Clock => this;
        public IGameTimer Timer => audio;
        public bool QuitRequested => false;
        public MouseState Mouse => new(0, 0, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Pump() => Advance(16);

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Sleep(int milliseconds) => Advance(Math.Max(1, milliseconds));

        private void Advance(int milliseconds)
        {
            _now += milliseconds;
            var frames = _now * Rate / 1000 - audio.RenderedFrames;
            while (frames > 0)
            {
                var n = (int)Math.Min(frames, _buffer.Length / 2);
                audio.Render(_buffer.AsSpan(0, 2 * n));
                frames -= n;
            }
        }
    }

    private sealed class NoModule : IMidiSynth
    {
        public void PlayShortMessage(uint message) { }

        public void PlaySysex(ReadOnlySpan<byte> sysex) { }

        public void Render(Span<short> interleavedStereo) => interleavedStereo.Clear();

        public void Dispose() { }
    }
}
