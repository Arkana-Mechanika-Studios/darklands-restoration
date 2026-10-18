using Darklands.Audio.Synthesis;

namespace Darklands.Audio.Machine;

/// <summary>
/// The FM chip's I/O ports: an AdLib at 388h (and the OPL3's second bank at 38Ah), mirrored
/// where a Sound Blaster Pro decodes them (base+0..3 and base+8..9).
/// </summary>
internal sealed class FmPorts(Opl3 chip, int soundBlasterBase)
{
    private readonly int[] _index = new int[2];

    public Opl3 Chip { get; } = chip;

    public bool Handles(int port) =>
        port is >= 0x388 and <= 0x38B
        || (port >= soundBlasterBase && port <= soundBlasterBase + 3)
        || port == soundBlasterBase + 8 || port == soundBlasterBase + 9;

    public byte In(int port) => Chip.Status;

    public void Out(int port, byte value)
    {
        var offset = port is >= 0x388 and <= 0x38B ? port - 0x388
            : port >= soundBlasterBase + 8 ? port - soundBlasterBase - 8
            : port - soundBlasterBase;
        var bank = offset >> 1;
        if ((offset & 1) == 0)
        {
            _index[bank] = value;
        }
        else
        {
            Chip.Write((bank << 8) | _index[bank], value);
        }
    }
}

/// <summary>
/// The four 8-bit channels of the PC's 8237 DMA controller, with their page registers: the
/// Sound Blaster reads its samples through one of them.
/// </summary>
internal sealed class DmaController
{
    private static readonly int[] PagePorts = { 0x87, 0x83, 0x81, 0x82 };
    private readonly DmaChannel[] _channels = { new(), new(), new(), new() };
    private bool _highByte;

    public bool Handles(int port) => port is >= 0x00 and <= 0x0F || Array.IndexOf(PagePorts, port) >= 0;

    public byte In(int port)
    {
        if (port >= 0x08)
        {
            return 0;
        }
        var channel = _channels[port >> 1];
        var value = (port & 1) == 0 ? channel.Address : channel.Count;
        var result = _highByte ? (byte)(value >> 8) : (byte)value;
        _highByte = !_highByte;
        return result;
    }

    public void Out(int port, byte value)
    {
        var page = Array.IndexOf(PagePorts, port);
        if (page >= 0)
        {
            _channels[page].Page = value;
            return;
        }
        if (port < 0x08)
        {
            var channel = _channels[port >> 1];
            if ((port & 1) == 0)
            {
                channel.BaseAddress = Merge(channel.BaseAddress, value);
                channel.Address = channel.BaseAddress;
            }
            else
            {
                channel.BaseCount = Merge(channel.BaseCount, value);
                channel.Count = channel.BaseCount;
            }
            _highByte = !_highByte;
            return;
        }
        switch (port)
        {
            case 0x0A: _channels[value & 3].Masked = (value & 4) != 0; break;
            case 0x0B: _channels[value & 3].Mode = value; break;
            case 0x0C: _highByte = false; break;
            case 0x0D:
                _highByte = false;
                foreach (var c in _channels)
                {
                    c.Masked = true;
                }
                break;
            case 0x0F:
                for (var i = 0; i < 4; i++)
                {
                    _channels[i].Masked = (value & (1 << i)) != 0;
                }
                break;
        }
    }

    private ushort Merge(ushort old, byte value) =>
        _highByte ? (ushort)((old & 0x00FF) | (value << 8)) : (ushort)((old & 0xFF00) | value);

    /// <summary>Takes the next byte of a transfer from memory; false when the channel is masked.</summary>
    public bool TryRead(int channelNumber, byte[] memory, out byte value)
    {
        var channel = _channels[channelNumber & 3];
        value = 0x80;
        if (channel.Masked)
        {
            return false;
        }
        value = memory[((channel.Page << 16) | channel.Address) & (Cpu8086.MemorySize - 1)];
        channel.Address = (ushort)(channel.Address + ((channel.Mode & 0x20) != 0 ? -1 : 1));
        if (channel.Count-- == 0)
        {
            if ((channel.Mode & 0x10) != 0)
            {
                channel.Address = channel.BaseAddress;
                channel.Count = channel.BaseCount;
            }
            else
            {
                channel.Masked = true;
                channel.Count = 0xFFFF;
            }
        }
        return true;
    }

    private sealed class DmaChannel
    {
        public ushort BaseAddress { get; set; }
        public ushort Address { get; set; }
        public ushort BaseCount { get; set; }
        public ushort Count { get; set; }
        public byte Page { get; set; }
        public byte Mode { get; set; }
        public bool Masked { get; set; } = true;
    }
}

/// <summary>
/// A Sound Blaster's digital sound processor: 8-bit samples by DMA (single-cycle and
/// auto-initialised) or written directly, at the rate of its time constant, with an
/// interrupt at the end of each block.
/// </summary>
internal sealed class SoundBlasterDsp(int basePort, int dmaChannel, DmaController dma, byte[] memory, Action raiseInterrupt)
{
    private readonly Queue<byte> _output = new();
    private readonly List<byte> _arguments = new();
    private int _command = -1;
    private int _argumentCount;
    private bool _resetting;
    private bool _speaker;
    private bool _playing;
    private bool _autoInit;
    private bool _paused;
    private int _blockLength = 0x7FF;
    private int _remaining;
    private double _position;
    private int _timeConstant = 0xA5;
    private byte _previous = 0x80;
    private byte _current = 0x80;

    public bool Handles(int port) => port is >= 0 && (port - basePort) is 0x6 or 0xA or 0xC or 0xE or 0x4 or 0x5;

    /// <summary>How long the driver's call has been running when it starts a transfer (the transfer starts that much later).</summary>
    public Func<double>? CallTime { get; init; }

    /// <summary>Seconds before the transfer started last really begins.</summary>
    private double _startDelay;

    /// <summary>Called when a transfer starts: the delay (seconds) and the block's length in bytes.</summary>
    public Action<double, int>? TransferStarted { get; set; }

    /// <summary>Samples per second set by the time constant.</summary>
    public double Rate => 1_000_000.0 / (256 - _timeConstant);

    public bool Transferring => _playing && !_paused;

    public byte In(int port)
    {
        switch (port - basePort)
        {
            case 0xA: return _output.Count > 0 ? _output.Dequeue() : (byte)0xFF;
            case 0xC: return 0x00; // always ready for a command
            case 0xE: return _output.Count > 0 ? (byte)0xFF : (byte)0x7F; // reading also acknowledges the interrupt
            default: return 0xFF;
        }
    }

    public void Out(int port, byte value)
    {
        switch (port - basePort)
        {
            case 0x6:
                if ((value & 1) != 0)
                {
                    _resetting = true;
                }
                else if (_resetting)
                {
                    _resetting = false;
                    Reset();
                    _output.Enqueue(0xAA);
                }
                return;
            case 0xC:
                Command(value);
                return;
        }
    }

    private void Reset()
    {
        _output.Clear();
        _arguments.Clear();
        _command = -1;
        _playing = false;
        _paused = false;
        _speaker = false;
        _current = _previous = 0x80;
    }

    private void Command(byte value)
    {
        if (_command < 0)
        {
            _command = value;
            _arguments.Clear();
            _argumentCount = value switch
            {
                0x10 or 0x40 or 0xE0 => 1,
                0x14 or 0x16 or 0x17 or 0x24 or 0x48 or 0x74 or 0x75 or 0x76 or 0x77 or 0x80 => 2,
                _ => 0,
            };
        }
        else
        {
            _arguments.Add(value);
        }
        if (_arguments.Count < _argumentCount)
        {
            return;
        }
        var command = _command;
        _command = -1;
        var word = _arguments.Count >= 2 ? _arguments[0] | (_arguments[1] << 8) : 0;
        switch (command)
        {
            case 0x10: // direct output: the DAC takes the value at once
                _previous = _current = _arguments[0];
                break;
            case 0x14: // 8-bit single-cycle DMA
            case 0x91:
                StartBlock(command == 0x91 ? _blockLength : word, autoInit: false);
                break;
            case 0x1C: // 8-bit auto-initialised DMA
            case 0x90:
                StartBlock(_blockLength, autoInit: true);
                break;
            case 0x40: _timeConstant = _arguments[0]; break;
            case 0x48: _blockLength = word; break;
            case 0x80: // silence, then an interrupt
                raiseInterrupt();
                break;
            case 0xD0: _paused = true; break;
            case 0xD4: _paused = false; break;
            case 0xD1: _speaker = true; break;
            case 0xD3: _speaker = false; break;
            case 0xD8: _output.Enqueue(_speaker ? (byte)0xFF : (byte)0x00); break;
            case 0xDA: _autoInit = false; break;
            case 0xE0: _output.Enqueue((byte)~_arguments[0]); break;
            case 0xE1: // DSP version 2.01
                _output.Enqueue(2);
                _output.Enqueue(1);
                break;
            case 0x20: _output.Enqueue(0x80); break;
            case 0xF2: raiseInterrupt(); break;
        }
    }

    private void StartBlock(int lengthMinusOne, bool autoInit)
    {
        _remaining = lengthMinusOne + 1;
        _startDelay = CallTime?.Invoke() ?? 0;
        TransferStarted?.Invoke(_startDelay, _remaining);
        _autoInit = autoInit;
        _playing = true;
        _paused = false;
    }

    /// <summary>The DSP's output for the next sample of a clock at <paramref name="outputRate"/>, -128..127.</summary>
    public int Next(double outputRate)
    {
        if (Transferring && _startDelay > 0)
        {
            // the driver is still busy in the call that started the transfer
            _startDelay -= 1 / outputRate;
        }
        else if (Transferring)
        {
            _position += Rate / outputRate;
            while (_position >= 1 && Transferring)
            {
                _position -= 1;
                TakeByte();
            }
        }
        if (!_speaker)
        {
            return 0;
        }
        var fraction = Math.Clamp(_position, 0, 1);
        return (int)(_previous + (_current - _previous) * fraction) - 128;
    }

    /// <summary>Finishes the block in progress at once (the driver is waiting for it).</summary>
    public void FinishBlock()
    {
        _startDelay = 0;
        var guard = 0x20000;
        while (Transferring && guard-- > 0)
        {
            TakeByte();
        }
    }

    private void TakeByte()
    {
        dma.TryRead(dmaChannel, memory, out var sample);
        _previous = _current;
        _current = sample;
        if (--_remaining > 0)
        {
            return;
        }
        if (_autoInit)
        {
            _remaining = _blockLength + 1;
        }
        else
        {
            _playing = false;
        }
        raiseInterrupt();
    }
}
