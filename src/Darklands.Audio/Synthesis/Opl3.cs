namespace Darklands.Audio.Synthesis;

/// <summary>
/// New code (not a reconstruction of the game): a Yamaha YMF262 (OPL3) FM synthesiser, which also behaves as the YM3812 (OPL2) of an AdLib
/// card until its OPL3 mode is switched on. Written from the chips' documented behaviour:
/// log-sine and exponent look-ups, a 9-bit attenuation envelope in 0.1875 dB steps with
/// 64 rates, phase modulation, the tremolo and vibrato oscillators, rhythm mode and the
/// OPL3 additions (second register bank, eight waveforms, four-operator channels, stereo).
/// It produces one stereo sample per call at <see cref="SampleRate"/>.
/// </summary>
public sealed class Opl3
{
    /// <summary>The chip's output rate: a 14.31818 MHz crystal divided by 288 (OPL2: 3.58 MHz / 72).</summary>
    public const double SampleRate = 14318180.0 / 288.0;

    private const int MaxAttenuation = 0x1FF;
    private static readonly ushort[] LogSine = BuildLogSine();
    private static readonly ushort[] Exponent = BuildExponent();
    private static readonly int[] MultiplierTimesTwo = { 1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30 };
    private static readonly int[] KeyScaleLevelBase = { 0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64 };
    private static readonly int[] KeyScaleShift = { 8, 1, 2, 0 }; // 0, 3, 1.5 and 6 dB per octave

    // Envelope increments: which of eight clock ticks step, for the four fine rates.
    private static readonly byte[,] EnvelopeSteps =
    {
        { 0, 1, 0, 1, 0, 1, 0, 1 },
        { 0, 1, 0, 1, 1, 1, 0, 1 },
        { 0, 1, 1, 1, 0, 1, 1, 1 },
        { 0, 1, 1, 1, 1, 1, 1, 1 },
    };

    // Register offsets 00..15 of a bank to operators (-1: no operator).
    private static readonly int[] OperatorAt =
    {
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1, 12, 13, 14, 15, 16, 17,
    };

    private readonly Operator[] _operators = new Operator[36];
    private readonly Channel[] _channels = new Channel[18];
    private readonly byte[] _registers = new byte[0x200];
    private uint _envelopeClock;
    private uint _lfoClock;
    private uint _noise = 1;
    private bool _opl3;
    private bool _waveformSelect;
    private bool _noteSelect;
    private bool _deepTremolo;
    private bool _deepVibrato;
    private bool _rhythm;
    private int _fourOperator;

    // Timers (status register): only what drivers need to detect the card.
    private byte _status;

    public Opl3()
    {
        for (var i = 0; i < _operators.Length; i++)
        {
            _operators[i] = new Operator();
        }
        for (var c = 0; c < 18; c++)
        {
            var bank = c / 9;
            var local = c % 9;
            var modulator = bank * 18 + OperatorAt[local % 3 + local / 3 * 8];
            _channels[c] = new Channel(_operators[modulator], _operators[modulator + 3]);
            _operators[modulator].Channel = _channels[c];
            _operators[modulator + 3].Channel = _channels[c];
        }
    }

    /// <summary>True once OPL3 mode is on (register 105h bit 0).</summary>
    public bool Opl3Mode => _opl3;

    /// <summary>The status register (read at 388h): timer overflow flags.</summary>
    public byte Status => _status;

    public byte RegisterValue(int register) => _registers[register & 0x1FF];

    /// <summary>Writes a register: 000..0FF in the first bank, 100..1FF in the second (OPL3).</summary>
    public void Write(int register, byte value)
    {
        register &= 0x1FF;
        var bank = register >> 8;
        var address = register & 0xFF;
        if (bank == 1 && !_opl3 && address != 0x05)
        {
            return; // the second bank only exists in OPL3 mode
        }
        _registers[register] = value;
        if (bank == 0)
        {
            switch (address)
            {
                case 0x01: _waveformSelect = (value & 0x20) != 0; return;
                case 0x02:
                case 0x03: return;
                case 0x04:
                    if ((value & 0x80) != 0)
                    {
                        _status = 0;
                        return;
                    }
                    // A started timer is reported as overflowed at once: the drivers only start
                    // timers to see that a chip answers.
                    if ((value & 0x01) != 0 && (value & 0x40) == 0)
                    {
                        _status |= 0xC0;
                    }
                    if ((value & 0x02) != 0 && (value & 0x20) == 0)
                    {
                        _status |= 0xA0;
                    }
                    return;
                case 0x08: _noteSelect = (value & 0x40) != 0; return;
                case 0xBD:
                    _deepTremolo = (value & 0x80) != 0;
                    _deepVibrato = (value & 0x40) != 0;
                    _rhythm = (value & 0x20) != 0;
                    UpdateKeys();
                    return;
            }
        }
        else
        {
            switch (address)
            {
                case 0x04: _fourOperator = value & 0x3F; return;
                case 0x05: _opl3 = (value & 0x01) != 0; return;
            }
        }

        if (address is >= 0x20 and <= 0x95 || address >= 0xE0)
        {
            var slot = address & 0x1F;
            if (slot >= OperatorAt.Length || OperatorAt[slot] < 0)
            {
                return;
            }
            var op = _operators[bank * 18 + OperatorAt[slot]];
            switch (address & 0xE0)
            {
                case 0x20:
                    op.Tremolo = (value & 0x80) != 0;
                    op.Vibrato = (value & 0x40) != 0;
                    op.Sustained = (value & 0x20) != 0;
                    op.KeyScaleRate = (value & 0x10) != 0;
                    op.Multiplier = MultiplierTimesTwo[value & 0x0F];
                    break;
                case 0x40:
                    op.KeyScaleLevel = KeyScaleShift[value >> 6];
                    op.TotalLevel = (value & 0x3F) << 2;
                    break;
                case 0x60:
                    op.AttackRate = value >> 4;
                    op.DecayRate = value & 0x0F;
                    break;
                case 0x80:
                    op.SustainLevel = (value >> 4) == 15 ? 0x1F0 : (value >> 4) << 4;
                    op.ReleaseRate = value & 0x0F;
                    break;
                default:
                    op.Waveform = value & 0x07;
                    break;
            }
            return;
        }

        if (address is >= 0xA0 and <= 0xA8 || address is >= 0xB0 and <= 0xB8 || address is >= 0xC0 and <= 0xC8)
        {
            var channel = _channels[bank * 9 + (address & 0x0F)];
            switch (address & 0xF0)
            {
                case 0xA0:
                    channel.Number = (channel.Number & 0x300) | value;
                    break;
                case 0xB0:
                    channel.Number = (channel.Number & 0xFF) | ((value & 0x03) << 8);
                    channel.Block = (value >> 2) & 0x07;
                    channel.KeyOn = (value & 0x20) != 0;
                    UpdateKeys();
                    break;
                default:
                    channel.Feedback = (value >> 1) & 0x07;
                    channel.Additive = (value & 0x01) != 0;
                    channel.Left = (value & 0x10) != 0;
                    channel.Right = (value & 0x20) != 0;
                    break;
            }
        }
    }

    /// <summary>Starts or releases each operator from the channel key bits and the rhythm bits.</summary>
    private void UpdateKeys()
    {
        var drums = _registers[0xBD];
        for (var c = 0; c < 18; c++)
        {
            var channel = _channels[c];
            var modulatorKey = channel.KeyOn;
            var carrierKey = channel.KeyOn;
            if (_rhythm && c is >= 6 and <= 8)
            {
                switch (c)
                {
                    case 6: // bass drum: both operators
                        modulatorKey |= (drums & 0x10) != 0;
                        carrierKey |= (drums & 0x10) != 0;
                        break;
                    case 7: // hi-hat and snare drum
                        modulatorKey |= (drums & 0x01) != 0;
                        carrierKey |= (drums & 0x08) != 0;
                        break;
                    default: // tom-tom and cymbal
                        modulatorKey |= (drums & 0x04) != 0;
                        carrierKey |= (drums & 0x02) != 0;
                        break;
                }
            }
            channel.Modulator.SetKey(modulatorKey);
            channel.Carrier.SetKey(carrierKey);
        }
    }

    /// <summary>Produces the next output sample (left and right, before clipping).</summary>
    public void Generate(out int left, out int right)
    {
        _envelopeClock++;
        _lfoClock++;
        // 23-bit noise generator of the rhythm instruments.
        _noise = (_noise & 1) != 0 ? (_noise >> 1) ^ 0x400181 : _noise >> 1;

        // Tremolo: a triangle over 210 steps of 64 samples (3.7 Hz), 4.8 dB or 1 dB deep.
        var tremoloStep = (int)((_lfoClock >> 6) % 210);
        var tremolo = tremoloStep < 105 ? tremoloStep : 209 - tremoloStep;
        tremolo = _deepTremolo ? tremolo >> 2 : tremolo >> 4;
        var vibratoPosition = (int)((_lfoClock >> 10) & 7);

        foreach (var op in _operators)
        {
            op.Advance(_envelopeClock, vibratoPosition, _deepVibrato, _noteSelect);
        }

        left = 0;
        right = 0;
        for (var c = 0; c < 18; c++)
        {
            if (c >= 9 && !_opl3)
            {
                break;
            }
            var channel = _channels[c];
            var local = c % 9;
            int output;
            if (_opl3 && local is 3 or 4 or 5 && (_fourOperator & (1 << (local - 3 + 3 * (c / 9)))) != 0)
            {
                continue; // the second half of a four-operator channel
            }
            if (_opl3 && local is 0 or 1 or 2 && (_fourOperator & (1 << (local + 3 * (c / 9)))) != 0)
            {
                output = FourOperator(channel, _channels[c + 3], tremolo);
            }
            else if (_rhythm && c is >= 6 and <= 8)
            {
                output = Rhythm(c, tremolo);
            }
            else
            {
                output = TwoOperator(channel, tremolo);
            }
            if (!_opl3 || channel.Left)
            {
                left += output;
            }
            if (!_opl3 || channel.Right)
            {
                right += output;
            }
        }
    }


    private int FeedbackOf(Channel channel) =>
        channel.Feedback == 0 ? 0 : (channel.Modulator.Previous + channel.Modulator.Output) >> (9 - channel.Feedback);

    private int TwoOperator(Channel channel, int tremolo)
    {
        var first = Operate(channel.Modulator, channel.Modulator.PhaseIndex + FeedbackOf(channel), tremolo);
        channel.Modulator.Remember(first);
        if (channel.Additive)
        {
            return first + Operate(channel.Carrier, channel.Carrier.PhaseIndex, tremolo);
        }
        return Operate(channel.Carrier, channel.Carrier.PhaseIndex + first, tremolo);
    }

    private int FourOperator(Channel first, Channel second, int tremolo)
    {
        var a = Operate(first.Modulator, first.Modulator.PhaseIndex + FeedbackOf(first), tremolo);
        first.Modulator.Remember(a);
        var b = first.Carrier;
        var c = second.Modulator;
        var d = second.Carrier;
        switch ((first.Additive ? 1 : 0) | (second.Additive ? 2 : 0))
        {
            case 0: // A > B > C > D
                var ab = Operate(b, b.PhaseIndex + a, tremolo);
                var abc = Operate(c, c.PhaseIndex + ab, tremolo);
                return Operate(d, d.PhaseIndex + abc, tremolo);
            case 2: // (A > B) + (C > D)
                var cd = Operate(c, c.PhaseIndex, tremolo);
                return Operate(b, b.PhaseIndex + a, tremolo) + Operate(d, d.PhaseIndex + cd, tremolo);
            case 1: // A + (B > C > D)
                var bOut = Operate(b, b.PhaseIndex, tremolo);
                var bc = Operate(c, c.PhaseIndex + bOut, tremolo);
                return a + Operate(d, d.PhaseIndex + bc, tremolo);
            default: // A + (B > C) + D
                var bAlone = Operate(b, b.PhaseIndex, tremolo);
                return a + Operate(c, c.PhaseIndex + bAlone, tremolo) + Operate(d, d.PhaseIndex, tremolo);
        }
    }

    private int Rhythm(int c, int tremolo)
    {
        var hiHat = _channels[7].Modulator;
        var cymbal = _channels[8].Carrier;
        var hiHatPhase = hiHat.PhaseIndex;
        var cymbalPhase = cymbal.PhaseIndex;
        var ring = ((((hiHatPhase >> 2) ^ (hiHatPhase >> 7)) | (hiHatPhase >> 3)) & 1) != 0
            || (((cymbalPhase >> 3) ^ (cymbalPhase >> 5)) & 1) != 0;
        var noise = (_noise & 1) != 0;
        switch (c)
        {
            case 6: // bass drum: an ordinary channel, the modulator ignored when additive
                var channel = _channels[6];
                var first = Operate(channel.Modulator, channel.Modulator.PhaseIndex + FeedbackOf(channel), tremolo);
                channel.Modulator.Remember(first);
                var drum = channel.Additive
                    ? Operate(channel.Carrier, channel.Carrier.PhaseIndex, tremolo)
                    : Operate(channel.Carrier, channel.Carrier.PhaseIndex + first, tremolo);
                return drum * 2;
            case 7: // hi-hat (modulator) and snare drum (carrier)
                var hiHatIndex = ring ? (noise ? 0x2D0 : 0x234) : (noise ? 0x034 : 0x0D0);
                var snareIndex = ((hiHatPhase & 0x100) != 0 ? 0x200 : 0x100) ^ (noise ? 0x100 : 0);
                return (Operate(hiHat, hiHatIndex, tremolo) + Operate(_channels[7].Carrier, snareIndex, tremolo)) * 2;
            default: // tom-tom (modulator) and top cymbal (carrier)
                var tom = _channels[8].Modulator;
                return (Operate(tom, tom.PhaseIndex, tremolo) + Operate(cymbal, ring ? 0x300 : 0x100, tremolo)) * 2;
        }
    }

    /// <summary>One operator's output (signed, 13 bits) at a phase (10 bits, 1024 = one cycle).</summary>
    private int Operate(Operator op, int phase, int tremolo)
    {
        var attenuation = op.Envelope + op.TotalLevel + op.KeyScaleAttenuation + (op.Tremolo ? tremolo : 0);
        if (attenuation >= MaxAttenuation)
        {
            return 0;
        }
        var waveform = _opl3 ? op.Waveform : _waveformSelect ? op.Waveform & 3 : 0;
        return Wave(waveform, phase & 0x3FF, attenuation << 3);
    }

    /// <summary>The waveform's value at a phase, attenuated by a logarithmic amount (1/256 octave units).</summary>
    internal static int Wave(int waveform, int phase, int attenuation)
    {
        int level;
        var negative = false;
        switch (waveform)
        {
            case 0: // sine
                negative = (phase & 0x200) != 0;
                level = Quarter(phase);
                break;
            case 1: // half sine
                if ((phase & 0x200) != 0)
                {
                    return 0;
                }
                level = Quarter(phase);
                break;
            case 2: // absolute sine
                level = Quarter(phase);
                break;
            case 3: // quarter sine pulses
                if ((phase & 0x100) != 0)
                {
                    return 0;
                }
                level = LogSine[phase & 0xFF];
                break;
            case 4: // alternating sine at twice the rate
                if ((phase & 0x200) != 0)
                {
                    return 0;
                }
                negative = (phase & 0x100) != 0;
                level = Quarter(phase << 1);
                break;
            case 5: // alternating absolute sine at twice the rate
                if ((phase & 0x200) != 0)
                {
                    return 0;
                }
                level = Quarter(phase << 1);
                break;
            case 6: // square
                negative = (phase & 0x200) != 0;
                level = 0;
                break;
            default: // logarithmic sawtooth
                negative = (phase & 0x200) != 0;
                level = (negative ? ~phase & 0x1FF : phase & 0x1FF) << 3;
                break;
        }
        var total = level + attenuation;
        if (total >= 0x1A00)
        {
            return 0;
        }
        var magnitude = ((Exponent[~total & 0xFF] | 0x400) << 1) >> (total >> 8);
        return negative ? -magnitude : magnitude;
    }

    private static int Quarter(int phase) => (phase & 0x100) != 0 ? LogSine[~phase & 0xFF] : LogSine[phase & 0xFF];

    private static ushort[] BuildLogSine()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (ushort)Math.Round(-Math.Log2(Math.Sin((i + 0.5) * Math.PI / 512.0)) * 256.0);
        }
        return table;
    }

    private static ushort[] BuildExponent()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (ushort)Math.Round((Math.Pow(2.0, i / 256.0) - 1.0) * 1024.0);
        }
        return table;
    }

    // ---------------------------------------------------------------- channels and operators

    private sealed class Channel(Operator modulator, Operator carrier)
    {
        public Operator Modulator { get; } = modulator;
        public Operator Carrier { get; } = carrier;
        public int Number { get; set; }        // F-number, 10 bits
        public int Block { get; set; }         // octave
        public bool KeyOn { get; set; }
        public int Feedback { get; set; }
        public bool Additive { get; set; }
        public bool Left { get; set; } = true;
        public bool Right { get; set; } = true;
    }

    private enum Stage
    {
        Attack,
        Decay,
        Sustain,
        Release,
    }

    private sealed class Operator
    {
        private uint _phase; // 20 bits, the top 10 are the phase index
        private Stage _stage = Stage.Release;
        private bool _key;

        public Channel Channel { get; set; } = null!;
        public bool Tremolo { get; set; }
        public bool Vibrato { get; set; }
        public bool Sustained { get; set; }
        public bool KeyScaleRate { get; set; }
        public int Multiplier { get; set; } = 1;
        public int KeyScaleLevel { get; set; } = 8;
        public int TotalLevel { get; set; }
        public int AttackRate { get; set; }
        public int DecayRate { get; set; }
        public int SustainLevel { get; set; }
        public int ReleaseRate { get; set; }
        public int Waveform { get; set; }

        public int Envelope { get; private set; } = MaxAttenuation;
        public int KeyScaleAttenuation { get; private set; }
        public int PhaseIndex => (int)(_phase >> 10);
        public int Output { get; private set; }
        public int Previous { get; private set; }

        public void Remember(int output)
        {
            Previous = Output;
            Output = output;
        }

        public void SetKey(bool on)
        {
            if (on && !_key)
            {
                _phase = 0;
                _stage = Stage.Attack;
            }
            else if (!on && _key)
            {
                _stage = Stage.Release;
            }
            _key = on;
        }

        public void Advance(uint clock, int vibratoPosition, bool deepVibrato, bool noteSelect)
        {
            var number = Channel.Number;
            var block = Channel.Block;

            // Key scaling of level: 3/1.5/6 dB per octave above a point set by the F-number.
            var level = KeyScaleLevelBase[number >> 6] * 4 - ((8 - block) << 5);
            KeyScaleAttenuation = level <= 0 || KeyScaleLevel == 8 ? 0 : level >> KeyScaleLevel;

            // Phase: F-number shifted by the block, times the multiplier; vibrato bends the F-number.
            if (Vibrato)
            {
                var range = (number >> 7) & 7;
                var bend = (vibratoPosition & 3) switch
                {
                    0 => 0,
                    1 or 3 => range >> 1,
                    _ => range,
                };
                if (!deepVibrato)
                {
                    bend >>= 1;
                }
                number += vibratoPosition >= 4 ? -bend : bend;
            }
            _phase = (_phase + (uint)((((number << block) >> 1) * Multiplier))) & 0xFFFFF;

            // Envelope.
            var rate = _stage switch
            {
                Stage.Attack => AttackRate,
                Stage.Decay => DecayRate,
                Stage.Sustain => Sustained ? 0 : ReleaseRate,
                _ => ReleaseRate,
            };
            var increment = 0;
            if (rate != 0)
            {
                var keyScale = (block << 1) | ((number >> (noteSelect ? 8 : 9)) & 1);
                var effective = Math.Min(63, rate * 4 + (KeyScaleRate ? keyScale : keyScale >> 2));
                var coarse = effective >> 2;
                var fine = effective & 3;
                if (coarse < 12)
                {
                    var shift = 12 - coarse;
                    if ((clock & ((1u << shift) - 1)) == 0)
                    {
                        increment = EnvelopeSteps[fine, (clock >> shift) & 7];
                    }
                }
                else
                {
                    increment = EnvelopeSteps[fine, clock & 7] << (coarse - 12);
                }
                if (_stage == Stage.Attack && effective >= 60)
                {
                    Envelope = 0;
                }
            }

            switch (_stage)
            {
                case Stage.Attack:
                    if (Envelope == 0)
                    {
                        _stage = Stage.Decay;
                    }
                    else if (increment != 0)
                    {
                        Envelope += (~Envelope * increment) >> 3;
                        if (Envelope <= 0)
                        {
                            Envelope = 0;
                            _stage = Stage.Decay;
                        }
                    }
                    break;
                case Stage.Decay:
                    if (Envelope >= SustainLevel)
                    {
                        _stage = Stage.Sustain;
                    }
                    else
                    {
                        Envelope = Math.Min(MaxAttenuation, Envelope + increment);
                    }
                    break;
                default:
                    Envelope = Math.Min(MaxAttenuation, Envelope + increment);
                    break;
            }
        }
    }
}
