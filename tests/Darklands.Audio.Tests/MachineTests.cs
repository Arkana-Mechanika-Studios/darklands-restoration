using Darklands.Audio.Driver;
using Darklands.Audio.Machine;
using Darklands.Audio.Synthesis;

namespace Darklands.Audio.Tests;

/// <summary>The emulated PC: far calls, the FM ports, the Sound Blaster and the game's timer.</summary>
public sealed class MachineTests
{
    private static PcMachine NewMachine(out Opl3 chip)
    {
        chip = new Opl3();
        return new PcMachine(chip, new SoundCardSettings(SoundCard.SoundBlaster));
    }

    [Fact]
    public void FarCallsPassCArgumentsAndReturnAx()
    {
        var machine = NewMachine(out _);
        byte[] add = { 0x55, 0x8B, 0xEC, 0x8B, 0x46, 0x06, 0x03, 0x46, 0x08, 0x5D, 0xCB };
        Array.Copy(add, 0, machine.Cpu.Memory, 0x10000, add.Length);
        var sp = machine.Cpu.Reg(Cpu8086.SP);
        Assert.Equal(5, machine.CallFar(0x1000, 0, false, 2, 3));
        Assert.Equal(sp, machine.Cpu.Reg(Cpu8086.SP));
    }

    [Fact]
    public void AdLibPortsProgramTheChip()
    {
        var machine = NewMachine(out var chip);
        var written = new List<ushort>();
        machine.PortWritten = (port, _) => written.Add(port);
        byte[] code = { 0xBA, 0x88, 0x03, 0xB0, 0x20, 0xEE, 0x42, 0xB0, 0x01, 0xEE, 0xCB };
        Array.Copy(code, 0, machine.Cpu.Memory, 0x10000, code.Length);
        machine.CallFar(0x1000, 0, false);
        Assert.Equal(0x01, chip.RegisterValue(0x20));
        Assert.Equal(new ushort[] { 0x388, 0x389 }, written);
    }

    [Fact]
    public void TheDspAnswersAResetAndItsVersion()
    {
        var machine = NewMachine(out _);
        machine.Out(0x226, 1);
        machine.Out(0x226, 0);
        Assert.Equal(0xAA, machine.In(0x22A));
        machine.Out(0x22C, 0xE1);
        Assert.Equal(2, machine.In(0x22A));
        Assert.Equal(1, machine.In(0x22A));
    }

    [Fact]
    public void ASingleCycleTransferPlaysItsBytesThenStops()
    {
        var machine = NewMachine(out _);
        byte[] samples = { 0x80, 0xC0, 0x40, 0xFF };
        Array.Copy(samples, 0, machine.Cpu.Memory, 0x21000, samples.Length);
        machine.Out(0x0A, 5);       // mask channel 1
        machine.Out(0x0B, 0x49);    // single, read, channel 1
        machine.Out(0x0C, 0);
        machine.Out(0x02, 0x00);    // address 1000h
        machine.Out(0x02, 0x10);
        machine.Out(0x03, 0x03);    // count 4 - 1
        machine.Out(0x03, 0x00);
        machine.Out(0x83, 0x02);    // page 2
        machine.Out(0x0A, 1);       // unmask
        machine.Out(0x22C, 0xD1);   // speaker on
        machine.Out(0x22C, 0x40);
        machine.Out(0x22C, 0x83);   // time constant: 1e6 / 125 = 8000 Hz
        Assert.Equal(8000, machine.Dsp.Rate, 3);
        machine.Out(0x22C, 0x14);
        machine.Out(0x22C, 0x03);
        machine.Out(0x22C, 0x00);
        var output = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            output.Add(machine.Dsp.Next(8000));
        }
        Assert.Equal(new[] { 0, 0, 0x40, -0x40 }, output); // each byte sounds from the next output sample on
        Assert.False(machine.Dsp.Transferring);
    }

    [Fact]
    public void TheSpeakerSwitchSilencesTheDsp()
    {
        var machine = NewMachine(out _);
        machine.Out(0x22C, 0x10);
        machine.Out(0x22C, 0xFF);   // direct output
        Assert.Equal(0, machine.Dsp.Next(8000));
        machine.Out(0x22C, 0xD1);
        Assert.Equal(0x7F, machine.Dsp.Next(8000));
    }

    private sealed class Client(params int[] requests) : ITimerClient
    {
        private int _frame;

        public int Frames { get; private set; }

        public int FastTicks { get; private set; }

        public int FrameTick()
        {
            Frames++;
            return _frame < requests.Length ? requests[_frame++] : 0;
        }

        public void FastTick() => FastTicks++;
    }

    [Fact]
    public void TheMusicClockRunsAtSixtyHertz()
    {
        var timer = new GameTimer();
        var client = new Client();
        for (var i = 0; i < 70; i++)
        {
            timer.Interrupt(client);
        }
        Assert.Equal(4, GameTimer.FastMultiplier);
        Assert.Equal(60, client.Frames);
        Assert.Equal(0, client.FastTicks);
        Assert.Equal(0x4280, GameTimer.FramePeriod);
        Assert.Equal(0x4280 / GameTimer.PitClock, timer.InterruptInterval, 9);
    }

    [Fact]
    public void TheDriverCanAskForTheFastTimerAndGiveItUp()
    {
        var timer = new GameTimer();
        var client = new Client(1);
        timer.Interrupt(client);              // frame 1: asks for the fast timer
        Assert.True(timer.Fast);
        timer.Interrupt(client);              // frame 2 starts at the fast rate
        Assert.Equal(0x4280 / 4 / GameTimer.PitClock, timer.InterruptInterval, 9);
        Assert.Equal(2, client.FastTicks);
        var slow = new Client(1, -1);
        var again = new GameTimer();
        again.Interrupt(slow);
        again.Interrupt(slow);                // frame 2 gives it up
        Assert.False(again.Fast);
    }
}
