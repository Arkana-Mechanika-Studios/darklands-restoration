namespace Darklands.Audio.Machine;

/// <summary>
/// The PC speaker as the ISOUND drivers drive it: channel 2 of the 8253/8254 timer (port 43h control words for counter 2,
/// the count by port 42h, low byte then high byte) and port 61h (bit 0 the counter's gate, bit 1 the speaker's enable).
/// Mode 3 (the drivers' 0B6h: counter 2, low then high byte, square wave, binary) is a square wave of PIT clock / count:
/// high for the first half (the longer one for an odd count), low for the second; a count written while counting takes
/// effect at the end of the half-cycle in progress, the first count after a control word at once. The gate low stops
/// the counter with its output high; the speaker follows the output while bit 1 is set and rests otherwise.
/// New code (not a reconstruction): it stands in for the hardware the drivers expect.
/// </summary>
internal sealed class PcSpeaker
{
    public const double PitClock = 1_193_182.0;

    private byte _port61;
    private int _reload = 0x10000;
    private int _pendingReload = -1;
    private bool _lowByteNext = true;
    private int _lowByte;
    private bool _waitingForCount = true;
    private bool _outputHigh = true;
    private double _remaining;
    private double _filterIn;
    private double _filterOut;

    public static bool Handles(int port) => port is 0x42 or 0x61;

    /// <summary>A control word of port 43h for counter 2 (bits 7..6 = 2).</summary>
    public static bool IsCounter2Control(byte value) => value >> 6 == 2;

    /// <summary>Port 61h: bits 0..1 as written, bit 5 the counter's output (the other bits: 0).</summary>
    public byte In(int port) => port == 0x61 ? (byte)((_port61 & 0x0F) | (_outputHigh ? 0x20 : 0)) : (byte)0xFF;

    public void Out(int port, byte value)
    {
        if (port == 0x61)
        {
            var gateWasOn = (_port61 & 1) != 0;
            _port61 = value;
            if ((value & 1) != 0 && !gateWasOn)
            {
                // the gate's rising edge restarts the count
                StartHalf(true, restart: true);
            }
            return;
        }
        if (_lowByteNext)
        {
            _lowByte = value;
            _lowByteNext = false;
            return;
        }
        _lowByteNext = true;
        var count = _lowByte | (value << 8);
        var reload = count == 0 ? 0x10000 : count;
        if (_waitingForCount)
        {
            _waitingForCount = false;
            _reload = reload;
            _pendingReload = -1;
            StartHalf(true, restart: true);
        }
        else
        {
            _pendingReload = reload;
        }
    }

    /// <summary>A control word for counter 2: the mode is set, the output goes high and the counter waits for its count.</summary>
    public void Control(byte value)
    {
        if ((value & 0x30) == 0)
        {
            return; // a latch command: the speaker's sound does not change
        }
        _lowByteNext = true;
        _waitingForCount = true;
        _outputHigh = true;
    }

    /// <summary>The speaker's level averaged over the next sample of a clock at <paramref name="outputRate"/>, about −1..1.</summary>
    public double Next(double outputRate)
    {
        var level = Level(PitClock / outputRate);
        // the cone does not hold a constant level: a high-pass of about 30 Hz
        var output = level - _filterIn + _filterOut * (1 - 2 * Math.PI * 30 / outputRate);
        _filterIn = level;
        _filterOut = output;
        return output;
    }

    private double Level(double counts)
    {
        var enabled = (_port61 & 2) != 0;
        var gate = (_port61 & 1) != 0;
        if (!gate || _waitingForCount)
        {
            if (!gate)
            {
                _outputHigh = true;
            }
            return enabled ? (_outputHigh ? 1 : -1) : 0;
        }
        var sum = 0.0;
        var left = counts;
        while (left > 0)
        {
            var step = Math.Min(left, _remaining);
            sum += _outputHigh ? step : -step;
            left -= step;
            _remaining -= step;
            if (_remaining <= 0)
            {
                if (_pendingReload >= 0)
                {
                    _reload = _pendingReload;
                    _pendingReload = -1;
                }
                StartHalf(!_outputHigh, restart: false);
            }
        }
        return enabled ? sum / counts : 0;
    }

    private void StartHalf(bool high, bool restart)
    {
        _outputHigh = high;
        // an odd count is high for (n + 1) / 2 counts and low for (n − 1) / 2
        var half = Math.Max(1, high ? (_reload + 1) / 2 : _reload / 2);
        // a new half-cycle goes on from where the last one ended (the counts it overran are its own)
        _remaining = restart ? half : _remaining + half;
    }
}
