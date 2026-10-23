namespace Darklands.Audio.Machine;

/// <summary>
/// A Roland MPU-401 as the Roland driver uses it: data port 330h, command and status port 331h.
/// A command (FFh reset, 3Fh UART mode) is acknowledged with FEh on the data port; in UART mode
/// every byte written to the data port is a MIDI byte for the sound module, handed to
/// <see cref="MidiOut"/>. The interface is always ready to take a byte (status bit 6 clear);
/// status bit 7 is clear while an acknowledgement waits to be read.
/// New code (not a reconstruction): it stands in for the card the driver expects.
/// </summary>
// orig: RSOUND.DLC image 0444 (command: wait for bit 6 of 331h clear, write, read 330h until FEh),
//   0508 (reset FFh, up to five tries, then a delay of [58h] loops), 047D (data: wait for bit 6, out 330h
//   at image 0495, then read 330h while bit 7 is clear) - the ports are fixed, not taken from CONFIG.DRK.
internal sealed class Mpu401
{
    public const int DataPort = 0x330;
    public const int StatusPort = 0x331;
    private const byte Acknowledge = 0xFE;
    private readonly Queue<byte> _input = new();

    /// <summary>Receives each MIDI byte the driver sends.</summary>
    public Action<byte>? MidiOut { get; set; }

    /// <summary>True after the driver switched the interface to UART mode (command 3Fh).</summary>
    public bool Uart { get; private set; }

    public static bool Handles(int port) => port is DataPort or StatusPort;

    public byte In(int port)
    {
        if (port == DataPort)
        {
            return _input.Count > 0 ? _input.Dequeue() : Acknowledge;
        }
        // bit 6 clear: ready to receive; bit 7 clear: a byte to read
        return (byte)(_input.Count > 0 ? 0x3F : 0xBF);
    }

    public void Out(int port, byte value)
    {
        if (port == StatusPort)
        {
            // FFh resets (and leaves UART mode), 3Fh enters UART mode; both are acknowledged
            Uart = value == 0x3F;
            _input.Clear();
            _input.Enqueue(Acknowledge);
            return;
        }
        MidiOut?.Invoke(value);
    }
}

/// <summary>
/// Channel 0 of the PC's 8253 timer as far as a driver reads it: a latch command (00h to 43h)
/// and two reads of 40h give the count, which runs down by one every few instructions (the
/// Roland driver times a short loop with it to size its delays).
/// New code (not a reconstruction).
/// </summary>
// orig: RSOUND.DLC image 00CF (latch, read, 256 loops, latch, read: the difference, summed over 16 tries by 00FF
//   into the delay [58h] after an MPU reset).
// C: the speed of the emulated processor against the timer (4 instructions per count, a fast 386); the
//   driver only scales a delay by it.
internal sealed class TimerCounter(Func<long> instructions)
{
    private const int InstructionsPerCount = 4;
    private ushort _latched;
    private bool _highByte;

    public static bool Handles(int port) => port is 0x40 or 0x43;

    public byte In(int port)
    {
        var value = _highByte ? (byte)(_latched >> 8) : (byte)_latched;
        _highByte = !_highByte;
        return value;
    }

    public void Out(int port, byte value)
    {
        if (port == 0x43 && (value & 0xF0) == 0)
        {
            // C: the count runs down from FFFFh and wraps
            _latched = (ushort)~(instructions() / InstructionsPerCount);
            _highByte = false;
        }
    }
}
