using Darklands.Audio.Driver;
using Darklands.Audio.Machine;
using Darklands.Audio.Synthesis;
using Darklands.Content;
using Darklands.Platform;

namespace Darklands.Audio;

/// <summary>
/// The game's own music and sound effects: the player's sound drivers run in an emulated PC
/// whose FM chip, Sound Blaster and MPU-401 are provided here (the Roland driver's MIDI goes to
/// an <see cref="IMidiSynth"/>), and the result is mixed to 16-bit stereo for any
/// <see cref="IPcmOutput"/>. Music numbers go to the towns-and-map driver (xSOUND.DLC/.DC),
/// effect numbers to the battle driver (.DLB/.DB); the drivers are swapped as the game swaps
/// them. The emulated timer is the game's timer (<see cref="IGameTimer"/>): requests are carried
/// out at the tick they name, and the presentation reads the ticks the player has heard.
/// </summary>
public sealed class SynthAudio : IAudio, IPcmSource, IGameTimer
{
    /// <summary>The sampled-sound file each opening and ending piece needs (the drivers play its clips).</summary>
    // orig: root 03A1:000A matched - loads opendark.dgt and restarts the driver with it (047C:0783)
    //   before pieces 2Fh..35h, for the "a" and "p" drivers only.
    // orig: R133+0x186C (1971:04dc) matched - enddark1.dgt before pieces 2Dh, 36h..38h; enddark2.dgt
    //   before 39h..3Bh.
    // DIVERGENCE: the game loads the file when the opening or ending starts and frees it afterwards
    //   (without telling the driver); here the driver is restarted with the file when a piece needs a
    //   file other than the one it has, and the file stays in the emulated memory.
    private static string? SampleFileFor(int piece) => piece switch
    {
        >= 0x2F and <= 0x35 => "OPENDARK.DGT",
        0x2D or (>= 0x36 and <= 0x38) => "ENDDARK1.DGT",
        >= 0x39 and <= 0x3B => "ENDDARK2.DGT",
        _ => null,
    };

    /// <summary>FM and sample levels in the mix (the cards mixed them at about these levels).</summary>
    private const int FmGain = 2;
    private const int SampleGain = 192;
    private const int SpeakerGain = 6000;

    /// <summary>Ring of the output positions of the last ticks (for the ticks heard).</summary>
    private const int TickHistory = 4096;

    private readonly List<(long Tick, bool Battle, int Number)> _requests = new();
    private readonly object _requestLock = new();
    private readonly object _lock = new();
    private readonly GameFiles _files;
    private readonly SoundSetup _setup;
    private readonly Opl3 _chip = new();
    private readonly PcMachine _machine;
    private readonly IMidiSynth? _midi;
    private readonly double _step;
    private readonly long[] _tickPositions = new long[TickHistory];
    private readonly object _tickLock = new();
    private IPcmOutput? _output;
    private SoundDriver _driver;
    private readonly FrameTicker _ticker = new();
    private GameTimer _timer = null!;
    private bool _battle;
    private string? _sampleFile;
    private double _untilInterrupt;
    private double _position = 1;
    private int _previousLeft, _previousRight, _currentLeft, _currentRight;
    private long _renderedFrames;
    private readonly TracedClient _traced;
    private char _context = 's';
    private long _ticks;

    /// <summary>Plays the FM drivers of a card (the default: a Sound Blaster).</summary>
    public SynthAudio(GameFiles files, int sampleRate, SoundCardSettings? card = null)
        : this(files, sampleRate, SoundSetup.ForFmCard(files, card ?? new SoundCardSettings(SoundCard.SoundBlaster)))
    {
    }

    /// <summary>Plays <paramref name="setup"/>'s drivers; a Roland driver needs <paramref name="midi"/>; <paramref name="traced"/>: see <see cref="Traced"/>.</summary>
    public SynthAudio(GameFiles files, int sampleRate, SoundSetup setup, IMidiSynth? midi = null, Action<AudioEvent>? traced = null)
    {
        Traced = traced;
        if (setup.Kind is not (DriverKind.Fm or DriverKind.Roland or DriverKind.Speaker))
        {
            throw new ArgumentException($"{setup.TownsDriverName} is not a driver this audio plays ({setup.Kind})", nameof(setup));
        }
        if (setup.Kind == DriverKind.Roland && midi is null)
        {
            throw new ArgumentException("the Roland driver needs a MIDI synthesiser", nameof(midi));
        }
        _files = files;
        _setup = setup;
        _midi = setup.Kind == DriverKind.Roland ? midi : null;
        SampleRate = sampleRate;
        _step = Opl3.SampleRate / sampleRate;
        _machine = new PcMachine(_chip, setup.Card);
        _traced = new TracedClient(this);
        if (_midi is not null)
        {
            Midi = new MidiStream(_midi)
            {
                ByteSent = b => Traced?.Invoke(new AudioEvent(AudioEventKind.MidiByte, _ticks, _renderedFrames, _context, b, 0)),
            };
            _machine.Mpu.MidiOut = Midi.Add;
        }
        _machine.Dsp.TransferStarted = (delay, length) =>
            Traced?.Invoke(new AudioEvent(AudioEventKind.SampleTransfer, _ticks, _renderedFrames, _context, length, delay));
        _driver = LoadDriver(setup.TownsDriver);
        // orig: R000+0x052A (0e76:052a) draft - after loading the driver and installing the timer the game plays number 7.
        _driver.Play(7);
    }

    /// <summary>Starts playing on <paramref name="output"/>.</summary>
    public SynthAudio(GameFiles files, IPcmOutput output, SoundSetup setup, IMidiSynth? midi = null, Action<AudioEvent>? traced = null)
        : this(files, output.SampleRate, setup, midi, traced)
    {
        _output = output;
        output.Start(this);
    }

    public int SampleRate { get; }

    public SoundSetup Setup => _setup;

    /// <summary>The MIDI the Roland driver sends (null for the FM drivers).</summary>
    public MidiStream? Midi { get; }

    /// <summary>Output volume, 0..1 (the drivers have none of their own).</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>The reason the drivers stopped, if one failed; silence follows.</summary>
    public string? Fault { get; private set; }

    /// <summary>Frames rendered so far.</summary>
    public long RenderedFrames => Interlocked.Read(ref _renderedFrames);

    /// <summary>Ticks of the game timer rendered so far.</summary>
    public long RenderedTicks => Interlocked.Read(ref _ticks);

    /// <summary>What the drivers do, as it is rendered (tests and traces); called on the rendering thread.</summary>
    public Action<AudioEvent>? Traced { get; set; }

    /// <summary>True when the folder has the FM drivers of the card.</summary>
    public static bool CanPlay(GameFiles files, SoundCard card = SoundCard.SoundBlaster)
    {
        try
        {
            SoundSetup.ForFmCard(files, new SoundCardSettings(card));
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- the game's requests

    // orig: root 047C:078A asm - the driver's play slot on the towns-and-map driver, as the tune function R043+0x09AE calls it.
    public void PlayMusic(int tune) => Request(RequestTick, false, tune);

    public void PlayMusicAt(int tune, long tick) => Request(tick, false, tune);

    // orig: root 0000:11F2 draft - the music hot key: music off plays number 1 (the drivers fade the piece out); also the card
    //   loop's shutdown R043+0x054E.
    public void StopMusic() => Request(RequestTick, false, 1);

    // orig: R014+0x465E (0e76:465e) draft - the battle sound calls the play slot with n on the battle driver.
    public void PlaySound(int effect) => Request(RequestTick, true, effect);

    /// <summary>Carries out a music number at once and returns the AX the towns-and-map driver leaves (5: the number last played).</summary>
    // orig: root 047C:078A asm - the game's call is synchronous: the driver has carried out every earlier number, and the play slot
    //   returns in AX (5 gives the word [CA60h] of ASOUND.DC, the last number stored; another number gives what its handler leaves).
    // C: outside a battle the game has the towns-and-map driver loaded; a battle driver still loaded here (the swap waits for the next
    //   request) is swapped back first, as the game does at the end of a battle.
    // DIVERGENCE: requests still waiting for their tick are carried out first, earlier by at most the output's latency.
    public int PlayMusicNow(int tune)
    {
        lock (_lock)
        {
            if (Fault is not null)
            {
                return -1;
            }
            try
            {
                CarryDue(everything: true);
                _context = 'r';
                Traced?.Invoke(new AudioEvent(AudioEventKind.Request, _ticks, _renderedFrames, _context, tune, 0));
                return Carry(false, tune);
            }
            catch (InvalidOperationException e)
            {
                Fault = e.Message;
                return -1;
            }
        }
    }

    private void Request(long tick, bool battle, int number)
    {
        lock (_requestLock)
        {
            // in tick order; requests for the same tick keep the order they were made in
            var at = _requests.Count;
            while (at > 0 && _requests[at - 1].Tick > tick)
            {
                at--;
            }
            _requests.Insert(at, (tick, battle, number));
        }
    }

    /// <summary>Carries out the requests due at the ticks rendered so far.</summary>
    private void CarryDue(bool sameDriverOnly = false, bool everything = false)
    {
        while (true)
        {
            (long Tick, bool Battle, int Number) request;
            lock (_requestLock)
            {
                if (_requests.Count == 0 || (!everything && _requests[0].Tick > _ticks) || (sameDriverOnly && _requests[0].Battle != _battle))
                {
                    return;
                }
                request = _requests[0];
                _requests.RemoveAt(0);
            }
            _context = 'r';
            Traced?.Invoke(new AudioEvent(AudioEventKind.Request, _ticks, _renderedFrames, _context, request.Number, 0));
            Carry(request.Battle, request.Number);
        }
    }

    private void ServiceInterrupts()
    {
        _context = 'i';
        _machine.ServiceInterrupts();
    }

    // ---------------------------------------------------------------- the game timer

    /// <summary>Ticks the player has heard: the tick of the output position being played.</summary>
    public long Ticks
    {
        get
        {
            var played = _output?.FramesPlayed ?? RenderedFrames;
            lock (_tickLock)
            {
                var tick = _ticks;
                while (tick > 0 && _ticks - tick < TickHistory - 1 && _tickPositions[tick % TickHistory] > played)
                {
                    tick--;
                }
                return tick;
            }
        }
    }

    /// <summary>The ticks heard plus the output's latency (the requests made now cannot be carried out sooner).</summary>
    public long RequestTick
    {
        get
        {
            if (_output is null)
            {
                return RenderedTicks;
            }
            var latencySeconds = (double)_output.LatencyFrames / SampleRate;
            return Ticks + (long)Math.Ceiling(latencySeconds / (FrameTicker.FrameSeconds * 7 / 6)) + 1;
        }
    }

    public void Follow(TimerTrack? track, long afterTick)
    {
        lock (_lock)
        {
            _ticker.Follow(track, afterTick);
        }
    }

    private void OnTickStarting(long tick)
    {
        lock (_tickLock)
        {
            _ticks = tick;
            _tickPositions[tick % TickHistory] = _renderedFrames;
        }
        Traced?.Invoke(new AudioEvent(AudioEventKind.Tick, tick, _renderedFrames, _context, 0, 0));
    }

    private void OnTick(long tick)
    {
        // between two ticks of one frame the driver is idle: a request for this tick is carried out now (one that swaps the
        // drivers waits until the interrupt is over)
        CarryDue(sameDriverOnly: true);
    }

    /// <summary>One timer interrupt, then the requests it made due (outside the driver's timer work).</summary>
    private void Interrupt()
    {
        _timer.Interrupt(_traced);
        ServiceInterrupts();
        _untilInterrupt += _timer.InterruptInterval;
        CarryDue();
    }

    // ---------------------------------------------------------------- rendering

    public void Render(Span<short> interleavedStereo)
    {
        lock (_lock)
        {
            if (Fault is not null)
            {
                interleavedStereo.Clear();
                return;
            }
            try
            {
                CarryDue();
                if (_midi is not null)
                {
                    MixMidi(interleavedStereo);
                }
                else
                {
                    MixFm(interleavedStereo);
                }
            }
            catch (InvalidOperationException e)
            {
                Fault = e.Message;
                interleavedStereo.Clear();
            }
        }
    }

    private int Carry(bool battle, int number)
    {
        if (battle != _battle)
        {
            SwapDriver(battle);
        }
        // C: DS:A876 == 'a' || DS:A876 == 'p' - every driver of those letters gets the file; the AdLib driver has no DSP to play it.
        var name = battle || !_setup.Card.PlaysSamples ? null : SampleFileFor(number);
        if (name is not null && name != _sampleFile && _files.Exists(name))
        {
            _sampleFile = name;
            _driver.Start(_setup.Card, _files.Read(name));
        }
        return _driver.Play(number);
    }

    /// <summary>Unloads one driver and loads the other, as the game does on entering and leaving a battle.</summary>
    // orig: R002+0x0000 (0e76:0000) draft - the battle: at the start and the end of a battle 047C:0791
    //   stop, 047C:00A9 timer off, 047C:08F9 free, load "%csound.dlb" / "%csound.dlc", 047C:0783 with no sample, 047C:0066
    //   timer on (only when DS:A876 is not 'n'/'N').
    // orig: R037+0x8B76 (1c7d:09c6) matched - loading a game does the same swap back to the towns driver
    //   when a game is loaded during a battle.
    // DIVERGENCE: the game swaps when a battle starts and ends, whatever is played; here the swap
    //   happens at the first effect (battle driver) or music (towns driver) request after the change.
    private void SwapDriver(bool battle)
    {
        _driver.Stop();
        _battle = battle;
        _driver = LoadDriver(battle ? _setup.BattleDriver : _setup.TownsDriver);
        _sampleFile = null;
    }

    private SoundDriver LoadDriver(byte[] file)
    {
        var driver = new SoundDriver(_machine, file);
        driver.Start(_setup.Card, null);
        // the driver's timer starts again (047C:0066); the game's ticks go on
        _timer = new GameTimer(_ticker) { TickStarting = OnTickStarting, Ticked = OnTick };
        _untilInterrupt = _timer.InterruptInterval;
        return driver;
    }

    private void MixFm(Span<short> output)
    {
        for (var i = 0; i + 1 < output.Length; i += 2)
        {
            while (_position >= 1)
            {
                _position -= 1;
                _previousLeft = _currentLeft;
                _previousRight = _currentRight;
                NextChipFrame(out _currentLeft, out _currentRight);
            }
            var left = _previousLeft + (_currentLeft - _previousLeft) * _position;
            var right = _previousRight + (_currentRight - _previousRight) * _position;
            output[i] = Clip(left * Volume);
            output[i + 1] = Clip(right * Volume);
            _position += _step;
            Interlocked.Increment(ref _renderedFrames);
        }
    }

    private void NextChipFrame(out int left, out int right)
    {
        _untilInterrupt -= 1.0 / Opl3.SampleRate;
        while (_untilInterrupt <= 0)
        {
            Interrupt();
        }
        _chip.Generate(out left, out right);
        var sample = _machine.Dsp.Next(Opl3.SampleRate) * SampleGain;
        // the ISOUND drivers' PC speaker (silent for the other drivers, which leave port 61h alone)
        sample += (int)(_machine.Speaker.Next(Opl3.SampleRate) * SpeakerGain);
        ServiceInterrupts();
        left = left * FmGain + sample;
        right = right * FmGain + sample;
    }

    /// <summary>The Roland driver: the synthesiser renders the stretch up to each timer interrupt, the Sound Blaster's samples are added.</summary>
    private void MixMidi(Span<short> output)
    {
        var frames = output.Length / 2;
        var done = 0;
        while (done < frames)
        {
            var due = (int)Math.Ceiling(_untilInterrupt * SampleRate - 1e-9);
            if (due <= 0)
            {
                Interrupt();
                continue;
            }
            var n = Math.Min(due, frames - done);
            var span = output.Slice(done * 2, n * 2);
            _midi!.Render(span);
            for (var i = 0; i < n; i++)
            {
                var sample = _machine.Dsp.Next(SampleRate) * SampleGain;
                ServiceInterrupts();
                span[2 * i] = Clip((span[2 * i] + sample) * Volume);
                span[2 * i + 1] = Clip((span[2 * i + 1] + sample) * Volume);
            }
            _untilInterrupt -= (double)n / SampleRate;
            done += n;
            Interlocked.Add(ref _renderedFrames, n);
        }
    }

    private static short Clip(double value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    /// <summary>The driver as the timer calls it, noting which of its ticks is running (for the traces).</summary>
    private sealed class TracedClient(SynthAudio audio) : ITimerClient
    {
        public int FrameTick()
        {
            audio._context = 'f';
            return audio._driver.FrameTick();
        }

        public void FastTick()
        {
            audio._context = 'x';
            audio._driver.FastTick();
        }
    }
}

public enum AudioEventKind
{
    /// <summary>A tick of the game timer (<see cref="AudioEvent.Frame"/>: its output position).</summary>
    Tick,

    /// <summary>A music or sound request carried out (<see cref="AudioEvent.Value"/>: its number).</summary>
    Request,

    /// <summary>A byte the Roland driver sent to the MPU-401.</summary>
    MidiByte,

    /// <summary>A Sound Blaster transfer started (value: bytes; delay: seconds before it really begins).</summary>
    SampleTransfer,
}

/// <summary>
/// One thing the drivers did: at tick <see cref="Tick"/>, output frame <see cref="Frame"/>, in the
/// context <see cref="Context"/> ('f' a tick's driver work, 'x' a fast tick, 'r' a request, 'i' the
/// Sound Blaster's interrupt, 's' a driver start).
/// </summary>
public readonly record struct AudioEvent(AudioEventKind Kind, long Tick, long Frame, char Context, int Value, double Delay);
