using Darklands.Audio.Synthesis;

namespace Darklands.Audio.Machine;

/// <summary>
/// Just enough of a 1992 PC to run a Darklands sound driver: the 8086, memory, the FM chip,
/// a Sound Blaster with its DMA channel, an MPU-401 MIDI interface, the timer's counter 0, the PC speaker (counter 2 and port 61h) and the interrupt controller's mask. The host calls
/// into the driver with <see cref="CallFar"/>, as the game's thunks did.
/// New code (not a reconstruction): it stands in for the hardware and DOS the drivers expect.
/// </summary>
internal sealed class PcMachine : IPortBus
{
    // Where the host's calls return to: a far address nothing else uses.
    private const ushort ReturnSegment = 0xF000;
    private const ushort ReturnOffset = 0xFFF0;
    private const ushort IretOffset = 0xFF00;
    private const ushort StackSegment = 0x9000;
    private const long BusyWaitInstructions = 20_000;
    private const long RunawayInstructions = 50_000_000;

    private readonly FmPorts _fm;
    private readonly DmaController _dma = new();
    private readonly SoundBlasterDsp _dsp;
    private readonly TimerCounter _counter;
    private readonly int _irq;
    private byte _interruptMask = 0xFF;
    private bool _interruptPending;
    private int _depth;
    private long _callStart;

    /// <summary>
    /// How fast the emulated processor is taken to run, for the time the drivers' work takes:
    /// 3000 instructions per millisecond, DOSBox's default speed, at which the reference capture of
    /// the original was made (a sample's DMA transfer started about 44 ms into the Roland driver's
    /// tick there, after its 131 700 instructions).
    /// </summary>
    public const double InstructionsPerSecond = 3_000_000;

    /// <summary>Seconds the current call into a driver has run so far, at <see cref="InstructionsPerSecond"/>.</summary>
    public double SecondsIntoCall => _depth == 0 ? 0 : (Cpu.Instructions - _callStart) / InstructionsPerSecond;

    public PcMachine(Opl3 chip, SoundCardSettings card)
    {
        Cpu = new Cpu8086(this) { SoftwareInterrupt = DosAndBios };
        _irq = card.Irq;
        _fm = new FmPorts(chip, card.Port);
        _dsp = new SoundBlasterDsp(card.Port, card.Dma, _dma, Cpu.Memory, () => _interruptPending = true) { CallTime = () => SecondsIntoCall };
        _counter = new TimerCounter(() => Cpu.Instructions);
        // Every vector points at an IRET until a driver hooks one.
        Cpu.Write8(ReturnSegment, IretOffset, 0xCF);
        for (var vector = 0; vector < 256; vector++)
        {
            Cpu.Write16(0, vector * 4, IretOffset);
            Cpu.Write16(0, vector * 4 + 2, ReturnSegment);
        }
        Cpu.SetSeg(Cpu8086.SS, StackSegment);
        Cpu.SetReg(Cpu8086.SP, 0xFFFE);
    }

    public Cpu8086 Cpu { get; }

    public SoundBlasterDsp Dsp => _dsp;

    /// <summary>The PC speaker of the ISOUND drivers.</summary>
    public PcSpeaker Speaker { get; } = new();

    /// <summary>The MPU-401 MIDI interface of the Roland driver.</summary>
    public Mpu401 Mpu { get; } = new();

    /// <summary>
    /// Calls a far function with C arguments (pushed right to left) and returns AX. With
    /// <paramref name="waitsForCard"/>, a long-running call is taken as the driver waiting for the
    /// card (its interrupt test) and the Sound Blaster finishes its block at once.
    /// </summary>
    public ushort CallFar(ushort segment, ushort offset, bool waitsForCard, params ushort[] arguments)
    {
        for (var i = arguments.Length - 1; i >= 0; i--)
        {
            Cpu.Push(arguments[i]);
        }
        Cpu.Push(ReturnSegment);
        Cpu.Push(ReturnOffset);
        Cpu.SetSeg(Cpu8086.CS, segment);
        Cpu.IP = offset;
        Cpu.Flags |= Cpu8086.InterruptEnable; // the game calls with interrupts on (its timer handler does STI first)
        Run(waitsForCard);
        Cpu.SetReg(Cpu8086.SP, (ushort)(Cpu.Reg(Cpu8086.SP) + 2 * arguments.Length)); // C: the caller pops (cdecl)
        return Cpu.Reg(Cpu8086.AX);
    }

    /// <summary>Delivers a pending Sound Blaster interrupt while the driver is idle.</summary>
    public void ServiceInterrupts()
    {
        if (!_interruptPending || _depth > 0 || (_interruptMask & (1 << _irq)) != 0)
        {
            return;
        }
        Cpu.Flags |= Cpu8086.InterruptEnable;
        Cpu.Push((ushort)Cpu.Flags);
        Cpu.Push(ReturnSegment);
        Cpu.Push(ReturnOffset);
        EnterInterrupt();
        Run(waitsForCard: false);
    }

    private void EnterInterrupt()
    {
        _interruptPending = false;
        var vector = _irq < 8 ? 8 + _irq : 0x70 + _irq - 8;
        // The pushes were made by the caller; jump through the vector as the CPU would.
        Cpu.Flags &= ~(Cpu8086.InterruptEnable | Cpu8086.Trap);
        Cpu.IP = Cpu.Read16(0, vector * 4);
        Cpu.SetSeg(Cpu8086.CS, Cpu.Read16(0, vector * 4 + 2));
    }

    private void Run(bool waitsForCard)
    {
        if (_depth++ == 0)
        {
            _callStart = Cpu.Instructions;
        }
        var start = Cpu.Instructions;
        var nextBusyCheck = start + BusyWaitInstructions;
        try
        {
            while (Cpu.Seg(Cpu8086.CS) != ReturnSegment || Cpu.IP != ReturnOffset)
            {
                if (_interruptPending && Cpu.InterruptsEnabled && (_interruptMask & (1 << _irq)) == 0)
                {
                    Cpu.Interrupt(_irq < 8 ? 8 + _irq : 0x70 + _irq - 8);
                    _interruptPending = false;
                }
                Cpu.Step();
                if (Cpu.Halted)
                {
                    throw new InvalidOperationException("sound driver halted the processor");
                }
                if (Cpu.Instructions >= nextBusyCheck)
                {
                    if (waitsForCard)
                    {
                        // The driver is waiting in a loop (for its test interrupt): let the card finish.
                        _dsp.FinishBlock();
                    }
                    nextBusyCheck += BusyWaitInstructions;
                    if (Cpu.Instructions - start > RunawayInstructions)
                    {
                        throw new InvalidOperationException($"sound driver does not return ({Cpu.Seg(Cpu8086.CS):X4}:{Cpu.IP:X4})");
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>The DOS and BIOS services the drivers use: the clock (to set DOS time after the timer was fast).</summary>
    private bool DosAndBios(int vector, Cpu8086 cpu)
    {
        switch (vector)
        {
            case 0x1A: // BIOS clock: report midnight, no error
            case 0x21: // DOS: set time and the like are accepted and ignored
                if (vector == 0x21 && cpu.Reg8(4) == 0x35)
                {
                    var number = cpu.Reg8(0);
                    cpu.SetReg(Cpu8086.BX, cpu.Read16(0, number * 4));
                    cpu.SetSeg(Cpu8086.ES, cpu.Read16(0, number * 4 + 2));
                }
                else if (vector == 0x21 && cpu.Reg8(4) == 0x25)
                {
                    var number = cpu.Reg8(0);
                    cpu.Write16(0, number * 4, cpu.Reg(Cpu8086.DX));
                    cpu.Write16(0, number * 4 + 2, cpu.Seg(Cpu8086.DS));
                }
                else if (vector == 0x1A)
                {
                    cpu.SetReg(Cpu8086.CX, 0);
                    cpu.SetReg(Cpu8086.DX, 0);
                }
                cpu.SetFlag(Cpu8086.Carry, false);
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- ports

    public byte In(ushort port)
    {
        if (_fm.Handles(port))
        {
            return _fm.In(port);
        }
        if (_dsp.Handles(port))
        {
            return _dsp.In(port);
        }
        if (_dma.Handles(port))
        {
            return _dma.In(port);
        }
        if (Mpu401.Handles(port))
        {
            return Mpu.In(port);
        }
        if (PcSpeaker.Handles(port))
        {
            return Speaker.In(port);
        }
        if (TimerCounter.Handles(port))
        {
            return _counter.In(port);
        }
        return port switch
        {
            0x21 => _interruptMask,
            _ => 0xFF,
        };
    }

    /// <summary>Observes every port write (tests and traces).</summary>
    public Action<ushort, byte>? PortWritten { get; set; }

    public void Out(ushort port, byte value)
    {
        PortWritten?.Invoke(port, value);
        if (_fm.Handles(port))
        {
            _fm.Out(port, value);
        }
        else if (_dsp.Handles(port))
        {
            _dsp.Out(port, value);
        }
        else if (_dma.Handles(port))
        {
            _dma.Out(port, value);
        }
        else if (Mpu401.Handles(port))
        {
            Mpu.Out(port, value);
        }
        else if (PcSpeaker.Handles(port))
        {
            Speaker.Out(port, value);
        }
        else if (port == 0x43 && PcSpeaker.IsCounter2Control(value))
        {
            Speaker.Control(value);
        }
        else if (TimerCounter.Handles(port))
        {
            _counter.Out(port, value);
        }
        else if (port == 0x21)
        {
            _interruptMask = value;
        }
    }
}
