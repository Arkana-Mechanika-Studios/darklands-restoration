using Darklands.Audio.Machine;

namespace Darklands.Audio.Tests;

/// <summary>The 8086 interpreter on small hand-assembled programs.</summary>
public sealed class Cpu8086Tests
{
    private sealed class Ports : IPortBus
    {
        public List<(ushort Port, byte Value)> Written { get; } = new();

        public byte In(ushort port) => (byte)(port & 0xFF);

        public void Out(ushort port, byte value) => Written.Add((port, value));
    }

    private static (Cpu8086 Cpu, Ports Ports) Run(params byte[] code)
    {
        var ports = new Ports();
        var cpu = new Cpu8086(ports);
        Array.Copy(code, 0, cpu.Memory, 0x10000, code.Length);
        cpu.SetSeg(Cpu8086.CS, 0x1000);
        cpu.SetSeg(Cpu8086.DS, 0x2000);
        cpu.SetSeg(Cpu8086.ES, 0x3000);
        cpu.SetSeg(Cpu8086.SS, 0x4000);
        cpu.SetReg(Cpu8086.SP, 0xFFFE);
        cpu.IP = 0;
        while (cpu.IP < code.Length && cpu.Instructions < 10_000)
        {
            cpu.Step();
        }
        return (cpu, ports);
    }

    [Fact]
    public void AddSetsResultAndCarry()
    {
        var (cpu, _) = Run(0xB8, 0x34, 0x12, 0x05, 0x00, 0xF0); // mov ax,1234h; add ax,0F000h
        Assert.Equal(0x0234, cpu.Reg(Cpu8086.AX));
        Assert.True(cpu.GetFlag(Cpu8086.Carry));
        Assert.False(cpu.GetFlag(Cpu8086.Zero));
        Assert.False(cpu.GetFlag(Cpu8086.Overflow));
    }

    [Fact]
    public void SubtractingFromZeroBorrows()
    {
        var (cpu, _) = Run(0xB0, 0x00, 0x2C, 0x01); // mov al,0; sub al,1
        Assert.Equal(0xFF, cpu.Reg8(0));
        Assert.True(cpu.GetFlag(Cpu8086.Carry));
        Assert.True(cpu.GetFlag(Cpu8086.Sign));
    }

    [Fact]
    public void SignedOverflowIsFlagged()
    {
        var (cpu, _) = Run(0xB8, 0xFF, 0x7F, 0x40); // mov ax,7FFFh; inc ax
        Assert.Equal(0x8000, cpu.Reg(Cpu8086.AX));
        Assert.True(cpu.GetFlag(Cpu8086.Overflow));
    }

    [Fact]
    public void MultiplyFillsDxAx()
    {
        var (cpu, _) = Run(0xB8, 0x00, 0x10, 0xBB, 0x00, 0x10, 0xF7, 0xE3); // mov ax,1000h; mov bx,1000h; mul bx
        Assert.Equal(0x0100, cpu.Reg(Cpu8086.DX));
        Assert.Equal(0x0000, cpu.Reg(Cpu8086.AX));
        Assert.True(cpu.GetFlag(Cpu8086.Carry));
    }

    [Fact]
    public void DivideGivesQuotientAndRemainder()
    {
        var (cpu, _) = Run(0xB8, 0x64, 0x00, 0xB3, 0x07, 0xF6, 0xF3); // mov ax,100; mov bl,7; div bl
        Assert.Equal(14, cpu.Reg8(0));
        Assert.Equal(2, cpu.Reg8(4));
    }

    [Fact]
    public void RepeatedMoveCopiesBytes()
    {
        var ports = new Ports();
        var cpu = new Cpu8086(ports);
        byte[] code = { 0xFC, 0xB9, 0x03, 0x00, 0xBE, 0x00, 0x00, 0xBF, 0x10, 0x00, 0xF3, 0xA4 }; // cld; mov cx,3; mov si,0; mov di,10h; rep movsb
        Array.Copy(code, 0, cpu.Memory, 0x10000, code.Length);
        cpu.Memory[0x20000] = 1;
        cpu.Memory[0x20001] = 2;
        cpu.Memory[0x20002] = 3;
        cpu.SetSeg(Cpu8086.CS, 0x1000);
        cpu.SetSeg(Cpu8086.DS, 0x2000);
        cpu.SetSeg(Cpu8086.ES, 0x3000);
        while (cpu.IP < code.Length)
        {
            cpu.Step();
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, cpu.Memory[0x30010..0x30013]);
        Assert.Equal(0, cpu.Reg(Cpu8086.CX));
        Assert.Equal(3, cpu.Reg(Cpu8086.SI));
    }

    [Fact]
    public void ShiftsAndRotatesCarry()
    {
        var (shl, _) = Run(0xB8, 0x01, 0x80, 0xB1, 0x01, 0xD3, 0xE0); // mov ax,8001h; mov cl,1; shl ax,cl
        Assert.Equal(0x0002, shl.Reg(Cpu8086.AX));
        Assert.True(shl.GetFlag(Cpu8086.Carry));

        var (rcr, _) = Run(0xF9, 0xB0, 0x02, 0xD0, 0xD8); // stc; mov al,2; rcr al,1
        Assert.Equal(0x81, rcr.Reg8(0));
        Assert.False(rcr.GetFlag(Cpu8086.Carry));
    }

    [Fact]
    public void CallAndReturn()
    {
        // call +3; jmp to end (skipped by ret target); mov ax,5; ret
        var (cpu, _) = Run(0xE8, 0x02, 0x00, 0xEB, 0x04, 0xB8, 0x05, 0x00, 0xC3);
        Assert.Equal(5, cpu.Reg(Cpu8086.AX));
        Assert.Equal(0xFFFE, cpu.Reg(Cpu8086.SP));
    }

    [Fact]
    public void ConditionalJumps()
    {
        // mov ax,1; cmp ax,2; jl +3 (taken); mov bx,1; ...; jg +3 (not taken); mov cx,1
        var (cpu, _) = Run(0xB8, 0x01, 0x00, 0x3D, 0x02, 0x00, 0x7C, 0x03, 0xBB, 0x01, 0x00, 0x7F, 0x03, 0xB9, 0x01, 0x00);
        Assert.Equal(0, cpu.Reg(Cpu8086.BX));
        Assert.Equal(1, cpu.Reg(Cpu8086.CX));
    }

    [Fact]
    public void PortsGoThroughTheBus()
    {
        var (cpu, ports) = Run(0xBA, 0x88, 0x03, 0xB0, 0x20, 0xEE, 0x42, 0xEC); // mov dx,388h; mov al,20h; out dx,al; inc dx; in al,dx
        Assert.Equal((ushort)0x388, ports.Written[0].Port);
        Assert.Equal(0x20, ports.Written[0].Value);
        Assert.Equal(0x89, cpu.Reg8(0));
    }

    [Fact]
    public void SoftwareInterruptsUseTheHostOrTheVectorTable()
    {
        var ports = new Ports();
        var cpu = new Cpu8086(ports);
        var handled = new List<int>();
        cpu.SoftwareInterrupt = (vector, _) =>
        {
            handled.Add(vector);
            return vector == 0x21;
        };
        byte[] code = { 0xCD, 0x21, 0xCD, 0x60 };
        Array.Copy(code, 0, cpu.Memory, 0x10000, code.Length);
        cpu.Write16(0, 0x60 * 4, 0x0100);
        cpu.Write16(0, 0x60 * 4 + 2, 0x5000);
        cpu.SetSeg(Cpu8086.CS, 0x1000);
        cpu.SetSeg(Cpu8086.SS, 0x4000);
        cpu.SetReg(Cpu8086.SP, 0xFFFE);
        cpu.Step();
        Assert.Equal(2, cpu.IP);
        cpu.Step();
        Assert.Equal(new[] { 0x21, 0x60 }, handled);
        Assert.Equal(0x5000, cpu.Seg(Cpu8086.CS));
        Assert.Equal(0x0100, cpu.IP);
    }

    [Fact]
    public void SegmentOverrideReadsTheOtherSegment()
    {
        var ports = new Ports();
        var cpu = new Cpu8086(ports);
        byte[] code = { 0x26, 0xA0, 0x05, 0x00 }; // mov al,es:[5]
        Array.Copy(code, 0, cpu.Memory, 0x10000, code.Length);
        cpu.Memory[0x30005] = 0x77;
        cpu.SetSeg(Cpu8086.CS, 0x1000);
        cpu.SetSeg(Cpu8086.DS, 0x2000);
        cpu.SetSeg(Cpu8086.ES, 0x3000);
        cpu.Step();
        Assert.Equal(0x77, cpu.Reg8(0));
    }

    [Fact]
    public void WordsWrapWithinTheirSegment()
    {
        var cpu = new Cpu8086(new Ports());
        cpu.Write16(0x2000, 0xFFFF, 0xBEEF);
        Assert.Equal(0xEF, cpu.Memory[0x2FFFF]);
        Assert.Equal(0xBE, cpu.Memory[0x20000]);
        Assert.Equal(0xBEEF, cpu.Read16(0x2000, 0xFFFF));
    }

    [Fact]
    public void InterruptFlagFollowsCliAndSti()
    {
        var (cpu, _) = Run(0xFB);
        Assert.True(cpu.InterruptsEnabled);
        var (off, _) = Run(0xFB, 0xFA);
        Assert.False(off.InterruptsEnabled);
    }
}
