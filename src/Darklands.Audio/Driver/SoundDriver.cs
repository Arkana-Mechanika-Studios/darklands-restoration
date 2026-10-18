using Darklands.Audio.Machine;

namespace Darklands.Audio.Driver;

/// <summary>
/// One of the game's sound drivers running in the emulated PC, called exactly as the game
/// calls it: the driver's slots 100..104 are reached through the far-jump thunks the game
/// patches at root 047C:0783.. (7 bytes each, root 047C:08C8 copies the slot table into them).
/// </summary>
// orig: root 047C:08C8 (047c:08c8) asm - thunk k at 047C:04C7+7k
// orig: root 047C:07CE (047c:07ce) asm - DOS 4B03 overlay load
internal sealed class SoundDriver : ITimerClient
{
    /// <summary>Where the driver image is loaded (the game uses a DOS block; any free segment does).</summary>
    public const ushort LoadSegment = 0x1000;

    /// <summary>Where a sampled-sound file (.DGT) is placed for the driver.</summary>
    public const ushort SampleSegment = 0x2000;

    private const int StartSlot = 100;     // root 047C:0783
    private const int PlaySlot = 101;      // root 047C:078A sound_play
    private const int StopSlot = 102;      // root 047C:0791
    private const int FrameTickSlot = 103; // root 047C:0798
    private const int FastTickSlot = 104;  // root 047C:079F

    private readonly PcMachine _machine;
    private readonly DriverImage _image;

    public SoundDriver(PcMachine machine, byte[] file)
    {
        _machine = machine;
        _image = DriverImage.Parse(file);
        _image.LoadInto(machine.Cpu.Memory, LoadSegment);
    }

    /// <summary>
    /// Initialises the driver and the card, with an optional sampled-sound file.
    /// </summary>
    // orig: root 047C:0783 (047c:0783) asm - slot 100,
    //   called as (sample segment, size low, size high, port, card, irq, dma) by R000+0x052A
    //   (draft), root 03A1:000A and
    //   R133+0x186C; ASOUND.DC slot at image 2048h reads the
    //   arguments at [bp+6..12h]. tests: DriverTests
    // C: the game passes the segment half of the far pointer DS:A74E (its offset is 0: a DOS block)
    //   and the 32-bit size DS:A74A/DS:A74C as two words; zeros when no sample is loaded.
    public void Start(SoundCardSettings card, byte[]? sample)
    {
        ushort segment = 0, low = 0, high = 0;
        if (sample is not null)
        {
            var at = SampleSegment * 16;
            var length = Math.Min(sample.Length, Cpu8086.MemorySize - at - 0x10000);
            Array.Copy(sample, 0, _machine.Cpu.Memory, at, length);
            segment = SampleSegment;
            low = (ushort)length;
            high = (ushort)(length >> 16);
        }
        Call(StartSlot, true, segment, low, high, (ushort)card.Port, (ushort)card.Card, (ushort)card.Irq, (ushort)card.Dma);
    }

    /// <summary>The driver's one command: a piece of music, a sound, or a control number.</summary>
    // orig: root 047C:078A (047c:078a) root seg 047C asm - sound_play(n),
    //   slot 101; ASOUND.DC image 20BFh: numbers above 3Bh are ignored, 5 returns the number last
    //   played, the others are stored and dispatched through the word table at image 1FD0h.
    //   tests: DriverTests
    public int Play(int number) => (short)Call(PlaySlot, false, (ushort)number);

    /// <summary>Silences the card and releases it (before another driver is loaded, or at exit).</summary>
    // orig: root 047C:0791 (047c:0791) root seg 047C asm - slot 102
    //   (ASOUND.DC image 2123h; it also sets the DOS clock from the BIOS clock, int 1Ah / int 21h 2Dh).
    public void Stop() => Call(StopSlot, false);

    /// <summary>The driver's work once per game frame; returns its request for the fast timer.</summary>
    // orig: root 047C:0798 (047c:0798) asm - slot 103,
    //   called by root 047C:000A each game frame (ASOUND.DC image 214Bh: returns [CA62h] and clears it:
    //   1 wants the fast timer, -1 no longer, 0 no change).
    public int FrameTick() => (short)Call(FrameTickSlot, false);

    /// <summary>The driver's work on each fast timer interrupt (noise and pitch effects).</summary>
    // orig: root 047C:079F (047c:079f) asm - slot 104,
    //   called by the timer interrupt root 047C:01EA while the timer runs fast (ASOUND.DC image 215Fh).
    public void FastTick() => Call(FastTickSlot, false);

    private ushort Call(int slot, bool waitsForCard, params ushort[] arguments) =>
        _machine.CallFar(LoadSegment, _image.EntryPoint(slot), waitsForCard, arguments);
}
