namespace Darklands.Audio;

/// <summary>
/// A MIDI sound module the Roland driver's bytes go to (an MT-32 or CM-32L emulation). It renders
/// 16-bit stereo at the rate it was opened with.
/// </summary>
public interface IMidiSynth : IDisposable
{
    /// <summary>A channel or system message: status in bits 0..7, the data bytes in bits 8..15 and 16..23.</summary>
    void PlayShortMessage(uint message);

    /// <summary>A system-exclusive message, F0h .. F7h.</summary>
    void PlaySysex(ReadOnlySpan<byte> sysex);

    /// <summary>The next frames, left then right.</summary>
    void Render(Span<short> interleavedStereo);
}

/// <summary>
/// Groups the bytes a driver writes to the MPU-401 into MIDI messages: running status, the
/// system-exclusive messages the driver uses to set up the MT-32's timbres and reverb, and
/// real-time bytes (which may come between the bytes of another message).
/// New code (the MIDI 1.0 byte format, not a reconstruction).
/// </summary>
public sealed class MidiStream(IMidiSynth synth)
{
    private readonly List<byte> _sysex = new();
    private int _status;
    private int _needed;
    private int _count;
    private uint _message;
    private bool _inSysex;

    /// <summary>Every byte, in the order the driver sent them (for tests and traces).</summary>
    public Action<byte>? ByteSent { get; set; }

    public void Add(byte value)
    {
        ByteSent?.Invoke(value);
        if (value >= 0xF8)
        {
            synth.PlayShortMessage(value);
            return;
        }
        if (_inSysex)
        {
            if (value < 0x80)
            {
                _sysex.Add(value);
                return;
            }
            _inSysex = false;
            if (value == 0xF7)
            {
                _sysex.Add(value);
                synth.PlaySysex(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sysex));
                return;
            }
            // a status byte ends an unterminated exclusive message; it is dropped
        }
        if (value == 0xF0)
        {
            _inSysex = true;
            _status = 0;
            _sysex.Clear();
            _sysex.Add(value);
            return;
        }
        if (value >= 0x80)
        {
            _status = value;
            _count = 0;
            _message = value;
            _needed = value switch
            {
                < 0xC0 => 2,
                < 0xE0 => 1,
                < 0xF0 => 2,
                0xF1 or 0xF3 => 1,
                0xF2 => 2,
                _ => 0,
            };
            if (_needed == 0)
            {
                synth.PlayShortMessage(value);
                _status = 0;
            }
            return;
        }
        if (_status == 0)
        {
            return; // a data byte without a status
        }
        _message |= (uint)value << (8 * ++_count);
        if (_count < _needed)
        {
            return;
        }
        synth.PlayShortMessage(_message);
        if (_status >= 0xF0)
        {
            _status = 0; // system common messages cancel the running status
        }
        _count = 0;
        _message = (uint)_status;
    }
}
