namespace Darklands.Audio.Machine;

/// <summary>I/O ports of the emulated PC.</summary>
internal interface IPortBus
{
    byte In(ushort port);

    void Out(ushort port, byte value);
}

/// <summary>
/// New code (not a reconstruction of the game): a real-mode 8086 (with the few 80186 additions MicroProse's assembler could emit), enough to
/// run the game's sound drivers. One megabyte of memory, no wait states, no prefetch queue.
/// </summary>
internal sealed class Cpu8086
{
    public const int MemorySize = 0x100000;

    // Flag bits.
    public const int Carry = 0x0001;
    public const int Parity = 0x0004;
    public const int AuxCarry = 0x0010;
    public const int Zero = 0x0040;
    public const int Sign = 0x0080;
    public const int Trap = 0x0100;
    public const int InterruptEnable = 0x0200;
    public const int Direction = 0x0400;
    public const int Overflow = 0x0800;

    // Register numbers as the instruction encoding uses them.
    public const int AX = 0, CX = 1, DX = 2, BX = 3, SP = 4, BP = 5, SI = 6, DI = 7;
    public const int ES = 0, CS = 1, SS = 2, DS = 3;

    private readonly ushort[] _reg = new ushort[8];
    private readonly ushort[] _seg = new ushort[4];
    private readonly IPortBus _ports;
    private int _flags = 0x0002;

    // The instruction being executed.
    private int _segmentOverride = -1;
    private int _mod, _regField, _rm;
    private int _eaSegment;
    private ushort _eaOffset;

    public Cpu8086(IPortBus ports)
    {
        _ports = ports;
    }

    public byte[] Memory { get; } = new byte[MemorySize];

    public ushort IP { get; set; }

    public int Flags
    {
        get => _flags;
        set => _flags = (value & 0x0FD5) | 0x0002;
    }

    public bool InterruptsEnabled => (_flags & InterruptEnable) != 0;

    public bool Halted { get; set; }

    /// <summary>Instructions executed so far.</summary>
    public long Instructions { get; private set; }

    /// <summary>Handles a software interrupt in the host; returns false to use the vector table.</summary>
    public Func<int, Cpu8086, bool>? SoftwareInterrupt { get; set; }

    public ushort Reg(int r) => _reg[r];

    public void SetReg(int r, ushort value) => _reg[r] = value;

    public ushort Seg(int s) => _seg[s];

    public void SetSeg(int s, ushort value) => _seg[s] = value;

    public byte Reg8(int r) => r < 4 ? (byte)_reg[r] : (byte)(_reg[r - 4] >> 8);

    public void SetReg8(int r, byte value)
    {
        if (r < 4)
        {
            _reg[r] = (ushort)((_reg[r] & 0xFF00) | value);
        }
        else
        {
            _reg[r - 4] = (ushort)((_reg[r - 4] & 0x00FF) | (value << 8));
        }
    }

    public bool GetFlag(int flag) => (_flags & flag) != 0;

    public void SetFlag(int flag, bool on) => _flags = on ? _flags | flag : _flags & ~flag;

    // ---------------------------------------------------------------- memory

    public static int Linear(int segment, int offset) => ((segment << 4) + (offset & 0xFFFF)) & (MemorySize - 1);

    public byte Read8(int segment, int offset) => Memory[Linear(segment, offset)];

    public ushort Read16(int segment, int offset) =>
        (ushort)(Memory[Linear(segment, offset)] | (Memory[Linear(segment, offset + 1)] << 8));

    public void Write8(int segment, int offset, byte value) => Memory[Linear(segment, offset)] = value;

    public void Write16(int segment, int offset, ushort value)
    {
        Memory[Linear(segment, offset)] = (byte)value;
        Memory[Linear(segment, offset + 1)] = (byte)(value >> 8);
    }

    public void Push(ushort value)
    {
        _reg[SP] -= 2;
        Write16(_seg[SS], _reg[SP], value);
    }

    public ushort Pop()
    {
        var value = Read16(_seg[SS], _reg[SP]);
        _reg[SP] += 2;
        return value;
    }

    private byte Fetch8()
    {
        var value = Read8(_seg[CS], IP);
        IP++;
        return value;
    }

    private ushort Fetch16()
    {
        var value = Read16(_seg[CS], IP);
        IP += 2;
        return value;
    }

    // ---------------------------------------------------------------- interrupts

    /// <summary>Enters interrupt <paramref name="vector"/> through the vector table.</summary>
    public void Interrupt(int vector)
    {
        Push((ushort)_flags);
        Push(_seg[CS]);
        Push(IP);
        _flags &= ~(InterruptEnable | Trap);
        IP = Read16(0, vector * 4);
        _seg[CS] = Read16(0, vector * 4 + 2);
        Halted = false;
    }

    private void SoftwareInt(int vector)
    {
        if (SoftwareInterrupt?.Invoke(vector, this) == true)
        {
            return;
        }
        Interrupt(vector);
    }

    // ---------------------------------------------------------------- operands

    private void DecodeModRm()
    {
        var modrm = Fetch8();
        _mod = modrm >> 6;
        _regField = (modrm >> 3) & 7;
        _rm = modrm & 7;
        if (_mod == 3)
        {
            return;
        }
        int offset;
        var segment = DS;
        switch (_rm)
        {
            case 0: offset = _reg[BX] + _reg[SI]; break;
            case 1: offset = _reg[BX] + _reg[DI]; break;
            case 2: offset = _reg[BP] + _reg[SI]; segment = SS; break;
            case 3: offset = _reg[BP] + _reg[DI]; segment = SS; break;
            case 4: offset = _reg[SI]; break;
            case 5: offset = _reg[DI]; break;
            case 6:
                if (_mod == 0)
                {
                    offset = Fetch16();
                }
                else
                {
                    offset = _reg[BP];
                    segment = SS;
                }
                break;
            default: offset = _reg[BX]; break;
        }
        if (_mod == 1)
        {
            offset += (sbyte)Fetch8();
        }
        else if (_mod == 2)
        {
            offset += Fetch16();
        }
        _eaOffset = (ushort)offset;
        _eaSegment = _segmentOverride >= 0 ? _segmentOverride : segment;
    }

    private int DataSegment => _segmentOverride >= 0 ? _seg[_segmentOverride] : _seg[DS];

    private byte ReadRm8() => _mod == 3 ? Reg8(_rm) : Read8(_seg[_eaSegment], _eaOffset);

    private ushort ReadRm16() => _mod == 3 ? _reg[_rm] : Read16(_seg[_eaSegment], _eaOffset);

    private void WriteRm8(int value)
    {
        if (_mod == 3)
        {
            SetReg8(_rm, (byte)value);
        }
        else
        {
            Write8(_seg[_eaSegment], _eaOffset, (byte)value);
        }
    }

    private void WriteRm16(int value)
    {
        if (_mod == 3)
        {
            _reg[_rm] = (ushort)value;
        }
        else
        {
            Write16(_seg[_eaSegment], _eaOffset, (ushort)value);
        }
    }

    private int ReadRm(bool word) => word ? ReadRm16() : ReadRm8();

    private void WriteRm(bool word, int value)
    {
        if (word)
        {
            WriteRm16(value);
        }
        else
        {
            WriteRm8(value);
        }
    }

    private int ReadRegField(bool word) => word ? _reg[_regField] : Reg8(_regField);

    private void WriteRegField(bool word, int value)
    {
        if (word)
        {
            _reg[_regField] = (ushort)value;
        }
        else
        {
            SetReg8(_regField, (byte)value);
        }
    }

    // ---------------------------------------------------------------- arithmetic

    private static bool EvenParity(int value) => (System.Numerics.BitOperations.PopCount((uint)(value & 0xFF)) & 1) == 0;

    private void SetSignZeroParity(int result, bool word)
    {
        var masked = word ? result & 0xFFFF : result & 0xFF;
        SetFlag(Zero, masked == 0);
        SetFlag(Sign, (masked & (word ? 0x8000 : 0x80)) != 0);
        SetFlag(Parity, EvenParity(masked));
    }

    /// <summary>The eight arithmetic operations of opcodes 00..3F and group 80..83.</summary>
    private int Alu(int operation, int a, int b, bool word)
    {
        var mask = word ? 0xFFFF : 0xFF;
        var sign = word ? 0x8000 : 0x80;
        int result;
        switch (operation)
        {
            case 0: // add
            case 2: // adc
                var carryIn = operation == 2 && GetFlag(Carry) ? 1 : 0;
                result = a + b + carryIn;
                SetFlag(Carry, result > mask);
                SetFlag(Overflow, ((a ^ result) & (b ^ result) & sign) != 0);
                SetFlag(AuxCarry, ((a ^ b ^ result) & 0x10) != 0);
                break;
            case 3: // sbb
            case 5: // sub
            case 7: // cmp
                var borrowIn = operation == 3 && GetFlag(Carry) ? 1 : 0;
                result = a - b - borrowIn;
                SetFlag(Carry, result < 0);
                SetFlag(Overflow, ((a ^ b) & (a ^ result) & sign) != 0);
                SetFlag(AuxCarry, ((a ^ b ^ result) & 0x10) != 0);
                break;
            case 1: result = a | b; Logic(); break;
            case 4: result = a & b; Logic(); break;
            default: result = a ^ b; Logic(); break;
        }
        SetSignZeroParity(result, word);
        return result & mask;

        void Logic()
        {
            SetFlag(Carry, false);
            SetFlag(Overflow, false);
            SetFlag(AuxCarry, false);
        }
    }

    private int IncDec(int value, bool word, bool decrement)
    {
        var carry = GetFlag(Carry);
        var result = Alu(decrement ? 5 : 0, value, 1, word);
        SetFlag(Carry, carry);
        return result;
    }

    private int Shift(int operation, int value, int count, bool word)
    {
        count &= 0x1F;
        if (count == 0)
        {
            return value;
        }
        var mask = word ? 0xFFFF : 0xFF;
        var top = word ? 0x8000 : 0x80;
        var original = value;
        var carry = GetFlag(Carry);
        for (var i = 0; i < count; i++)
        {
            switch (operation)
            {
                case 0: // rol
                    carry = (value & top) != 0;
                    value = ((value << 1) | (carry ? 1 : 0)) & mask;
                    break;
                case 1: // ror
                    carry = (value & 1) != 0;
                    value = (value >> 1) | (carry ? top : 0);
                    break;
                case 2: // rcl
                    var outRcl = (value & top) != 0;
                    value = ((value << 1) | (carry ? 1 : 0)) & mask;
                    carry = outRcl;
                    break;
                case 3: // rcr
                    var outRcr = (value & 1) != 0;
                    value = (value >> 1) | (carry ? top : 0);
                    carry = outRcr;
                    break;
                case 4: // shl
                case 6:
                    carry = (value & top) != 0;
                    value = (value << 1) & mask;
                    break;
                case 5: // shr
                    carry = (value & 1) != 0;
                    value >>= 1;
                    break;
                default: // sar
                    carry = (value & 1) != 0;
                    value = (value >> 1) | (value & top);
                    break;
            }
        }
        SetFlag(Carry, carry);
        var msb = (value & top) != 0;
        switch (operation)
        {
            case 0:
            case 2:
                SetFlag(Overflow, msb != carry);
                break;
            case 1:
            case 3:
                SetFlag(Overflow, msb != ((value & (top >> 1)) != 0));
                break;
            case 4:
            case 6:
                SetSignZeroParity(value, word);
                SetFlag(Overflow, msb != carry);
                break;
            case 5:
                SetSignZeroParity(value, word);
                SetFlag(Overflow, (original & top) != 0);
                break;
            default:
                SetSignZeroParity(value, word);
                SetFlag(Overflow, false);
                break;
        }
        return value;
    }

    private void Group3(bool word)
    {
        var value = ReadRm(word);
        switch (_regField)
        {
            case 0:
            case 1:
                Alu(4, value, word ? Fetch16() : Fetch8(), word);
                break;
            case 2:
                WriteRm(word, ~value);
                break;
            case 3:
                var result = Alu(5, 0, value, word);
                SetFlag(Carry, value != 0);
                WriteRm(word, result);
                break;
            case 4: // mul
                if (word)
                {
                    var product = (uint)_reg[AX] * (uint)value;
                    _reg[AX] = (ushort)product;
                    _reg[DX] = (ushort)(product >> 16);
                    SetFlag(Carry, _reg[DX] != 0);
                    SetFlag(Overflow, _reg[DX] != 0);
                }
                else
                {
                    _reg[AX] = (ushort)(Reg8(AX) * value);
                    SetFlag(Carry, (_reg[AX] & 0xFF00) != 0);
                    SetFlag(Overflow, (_reg[AX] & 0xFF00) != 0);
                }
                break;
            case 5: // imul
                if (word)
                {
                    var product = (short)_reg[AX] * (short)value;
                    _reg[AX] = (ushort)product;
                    _reg[DX] = (ushort)(product >> 16);
                    var fits = product == (short)product;
                    SetFlag(Carry, !fits);
                    SetFlag(Overflow, !fits);
                }
                else
                {
                    var product = (sbyte)Reg8(AX) * (sbyte)value;
                    _reg[AX] = (ushort)product;
                    var fits = product == (sbyte)product;
                    SetFlag(Carry, !fits);
                    SetFlag(Overflow, !fits);
                }
                break;
            case 6: // div
                if (value == 0)
                {
                    Interrupt(0);
                    return;
                }
                if (word)
                {
                    var dividend = ((uint)_reg[DX] << 16) | _reg[AX];
                    var quotient = dividend / (uint)value;
                    if (quotient > 0xFFFF)
                    {
                        Interrupt(0);
                        return;
                    }
                    _reg[DX] = (ushort)(dividend % (uint)value);
                    _reg[AX] = (ushort)quotient;
                }
                else
                {
                    var dividend = _reg[AX];
                    var quotient = dividend / value;
                    if (quotient > 0xFF)
                    {
                        Interrupt(0);
                        return;
                    }
                    _reg[AX] = (ushort)(((dividend % value) << 8) | quotient);
                }
                break;
            default: // idiv
                if (value == 0)
                {
                    Interrupt(0);
                    return;
                }
                if (word)
                {
                    var dividend = (int)(((uint)_reg[DX] << 16) | _reg[AX]);
                    var divisor = (short)value;
                    var quotient = dividend / divisor;
                    if (quotient != (short)quotient)
                    {
                        Interrupt(0);
                        return;
                    }
                    _reg[DX] = (ushort)(dividend % divisor);
                    _reg[AX] = (ushort)quotient;
                }
                else
                {
                    var dividend = (short)_reg[AX];
                    var divisor = (sbyte)value;
                    var quotient = dividend / divisor;
                    if (quotient != (sbyte)quotient)
                    {
                        Interrupt(0);
                        return;
                    }
                    _reg[AX] = (ushort)((((dividend % divisor) & 0xFF) << 8) | (quotient & 0xFF));
                }
                break;
        }
    }

    private bool Condition(int code)
    {
        var result = (code >> 1) switch
        {
            0 => GetFlag(Overflow),
            1 => GetFlag(Carry),
            2 => GetFlag(Zero),
            3 => GetFlag(Carry) || GetFlag(Zero),
            4 => GetFlag(Sign),
            5 => GetFlag(Parity),
            6 => GetFlag(Sign) != GetFlag(Overflow),
            _ => GetFlag(Zero) || GetFlag(Sign) != GetFlag(Overflow),
        };
        return (code & 1) == 0 ? result : !result;
    }

    // ---------------------------------------------------------------- string instructions

    private void StringOp(int opcode, int repeat)
    {
        var word = (opcode & 1) != 0;
        var step = (GetFlag(Direction) ? -1 : 1) * (word ? 2 : 1);
        var compares = opcode is 0xA6 or 0xA7 or 0xAE or 0xAF;
        while (true)
        {
            if (repeat != 0 && _reg[CX] == 0)
            {
                return;
            }
            switch (opcode)
            {
                case 0xA4: // movs
                case 0xA5:
                    if (word)
                    {
                        Write16(_seg[ES], _reg[DI], Read16(DataSegment, _reg[SI]));
                    }
                    else
                    {
                        Write8(_seg[ES], _reg[DI], Read8(DataSegment, _reg[SI]));
                    }
                    _reg[SI] = (ushort)(_reg[SI] + step);
                    _reg[DI] = (ushort)(_reg[DI] + step);
                    break;
                case 0xA6: // cmps
                case 0xA7:
                    Alu(7, word ? Read16(DataSegment, _reg[SI]) : Read8(DataSegment, _reg[SI]),
                        word ? Read16(_seg[ES], _reg[DI]) : Read8(_seg[ES], _reg[DI]), word);
                    _reg[SI] = (ushort)(_reg[SI] + step);
                    _reg[DI] = (ushort)(_reg[DI] + step);
                    break;
                case 0xAA: // stos
                case 0xAB:
                    if (word)
                    {
                        Write16(_seg[ES], _reg[DI], _reg[AX]);
                    }
                    else
                    {
                        Write8(_seg[ES], _reg[DI], Reg8(AX));
                    }
                    _reg[DI] = (ushort)(_reg[DI] + step);
                    break;
                case 0xAC: // lods
                case 0xAD:
                    if (word)
                    {
                        _reg[AX] = Read16(DataSegment, _reg[SI]);
                    }
                    else
                    {
                        SetReg8(AX, Read8(DataSegment, _reg[SI]));
                    }
                    _reg[SI] = (ushort)(_reg[SI] + step);
                    break;
                default: // scas
                    Alu(7, word ? _reg[AX] : Reg8(AX), word ? Read16(_seg[ES], _reg[DI]) : Read8(_seg[ES], _reg[DI]), word);
                    _reg[DI] = (ushort)(_reg[DI] + step);
                    break;
            }
            if (repeat == 0)
            {
                return;
            }
            _reg[CX]--;
            if (compares && (repeat == 0xF3) != GetFlag(Zero))
            {
                return;
            }
        }
    }

    // ---------------------------------------------------------------- execution

    /// <summary>Executes one instruction (with its prefixes).</summary>
    public void Step()
    {
        if (Halted)
        {
            return;
        }
        Instructions++;
        _segmentOverride = -1;
        var repeat = 0;
        var start = IP;
        int opcode;
        while (true)
        {
            opcode = Fetch8();
            switch (opcode)
            {
                case 0x26: _segmentOverride = ES; continue;
                case 0x2E: _segmentOverride = CS; continue;
                case 0x36: _segmentOverride = SS; continue;
                case 0x3E: _segmentOverride = DS; continue;
                case 0xF0: continue;
                case 0xF2:
                case 0xF3: repeat = opcode; continue;
            }
            break;
        }

        if (opcode < 0x40 && (opcode & 7) < 6)
        {
            Arithmetic(opcode);
            return;
        }
        var word = (opcode & 1) != 0;
        switch (opcode)
        {
            case 0x06: Push(_seg[ES]); break;
            case 0x07: _seg[ES] = Pop(); break;
            case 0x0E: Push(_seg[CS]); break;
            case 0x16: Push(_seg[SS]); break;
            case 0x17: _seg[SS] = Pop(); break;
            case 0x1E: Push(_seg[DS]); break;
            case 0x1F: _seg[DS] = Pop(); break;
            case 0x27: Daa(); break;
            case 0x2F: Das(); break;
            case 0x37: AsciiAdjust(true); break;
            case 0x3F: AsciiAdjust(false); break;
            case >= 0x40 and <= 0x47: _reg[opcode & 7] = (ushort)IncDec(_reg[opcode & 7], true, false); break;
            case >= 0x48 and <= 0x4F: _reg[opcode & 7] = (ushort)IncDec(_reg[opcode & 7], true, true); break;
            case >= 0x50 and <= 0x57:
                Push(opcode == 0x54 ? (ushort)(_reg[SP] - 2) : _reg[opcode & 7]);
                break;
            case >= 0x58 and <= 0x5F: _reg[opcode & 7] = Pop(); break;
            case 0x60: // pusha
                var sp = _reg[SP];
                for (var r = 0; r < 8; r++)
                {
                    Push(r == SP ? sp : _reg[r]);
                }
                break;
            case 0x61: // popa
                for (var r = 7; r >= 0; r--)
                {
                    var v = Pop();
                    if (r != SP)
                    {
                        _reg[r] = v;
                    }
                }
                break;
            case 0x68: Push(Fetch16()); break;
            case 0x6A: Push((ushort)(sbyte)Fetch8()); break;
            case 0x69:
            case 0x6B:
                DecodeModRm();
                var factor = opcode == 0x69 ? (short)Fetch16() : (sbyte)Fetch8();
                var imul = (short)ReadRm16() * factor;
                _reg[_regField] = (ushort)imul;
                SetFlag(Carry, imul != (short)imul);
                SetFlag(Overflow, imul != (short)imul);
                break;
            case >= 0x70 and <= 0x7F:
                var displacement = (sbyte)Fetch8();
                if (Condition(opcode & 0x0F))
                {
                    IP = (ushort)(IP + displacement);
                }
                break;
            case 0x80:
            case 0x81:
            case 0x82:
            case 0x83:
                DecodeModRm();
                var a = ReadRm(word);
                var b = opcode == 0x81 ? Fetch16() : opcode == 0x83 ? (ushort)(sbyte)Fetch8() : Fetch8();
                var r1 = Alu(_regField, a, b, word);
                if (_regField != 7)
                {
                    WriteRm(word, r1);
                }
                break;
            case 0x84:
            case 0x85:
                DecodeModRm();
                Alu(4, ReadRm(word), ReadRegField(word), word);
                break;
            case 0x86:
            case 0x87:
                DecodeModRm();
                var x = ReadRm(word);
                WriteRm(word, ReadRegField(word));
                WriteRegField(word, x);
                break;
            case 0x88:
            case 0x89:
                DecodeModRm();
                WriteRm(word, ReadRegField(word));
                break;
            case 0x8A:
            case 0x8B:
                DecodeModRm();
                WriteRegField(word, ReadRm(word));
                break;
            case 0x8C:
                DecodeModRm();
                WriteRm16(_seg[_regField & 3]);
                break;
            case 0x8D:
                DecodeModRm();
                _reg[_regField] = _eaOffset;
                break;
            case 0x8E:
                DecodeModRm();
                _seg[_regField & 3] = ReadRm16();
                break;
            case 0x8F:
                DecodeModRm();
                WriteRm16(Pop());
                break;
            case 0x90: break;
            case >= 0x91 and <= 0x97:
                (_reg[AX], _reg[opcode & 7]) = (_reg[opcode & 7], _reg[AX]);
                break;
            case 0x98: _reg[AX] = (ushort)(sbyte)Reg8(AX); break;
            case 0x99: _reg[DX] = (ushort)((_reg[AX] & 0x8000) != 0 ? 0xFFFF : 0); break;
            case 0x9A:
                var callOffset = Fetch16();
                var callSegment = Fetch16();
                Push(_seg[CS]);
                Push(IP);
                _seg[CS] = callSegment;
                IP = callOffset;
                break;
            case 0x9B: break;
            case 0x9C: Push((ushort)(_flags | 0xF000)); break;
            case 0x9D: Flags = Pop(); break;
            case 0x9E: _flags = (_flags & 0xFF00) | (Reg8(4) & 0xD5) | 0x02; break;
            case 0x9F: SetReg8(4, (byte)_flags); break;
            case 0xA0: SetReg8(AX, Read8(DataSegment, Fetch16())); break;
            case 0xA1: _reg[AX] = Read16(DataSegment, Fetch16()); break;
            case 0xA2: Write8(DataSegment, Fetch16(), Reg8(AX)); break;
            case 0xA3: Write16(DataSegment, Fetch16(), _reg[AX]); break;
            case >= 0xA4 and <= 0xA7:
            case >= 0xAA and <= 0xAF:
                StringOp(opcode, repeat);
                break;
            case 0xA8: Alu(4, Reg8(AX), Fetch8(), false); break;
            case 0xA9: Alu(4, _reg[AX], Fetch16(), true); break;
            case >= 0xB0 and <= 0xB7: SetReg8(opcode & 7, Fetch8()); break;
            case >= 0xB8 and <= 0xBF: _reg[opcode & 7] = Fetch16(); break;
            case 0xC0:
            case 0xC1:
                DecodeModRm();
                var shiftValue = ReadRm(word);
                WriteRm(word, Shift(_regField, shiftValue, Fetch8(), word));
                break;
            case 0xC2:
                var release = Fetch16();
                IP = Pop();
                _reg[SP] += release;
                break;
            case 0xC3: IP = Pop(); break;
            case 0xC4:
            case 0xC5:
                DecodeModRm();
                _reg[_regField] = Read16(_seg[_eaSegment], _eaOffset);
                _seg[opcode == 0xC4 ? ES : DS] = Read16(_seg[_eaSegment], _eaOffset + 2);
                break;
            case 0xC6:
            case 0xC7:
                DecodeModRm();
                WriteRm(word, word ? Fetch16() : Fetch8());
                break;
            case 0xC8: // enter
                var frameSize = Fetch16();
                var level = Fetch8() & 0x1F;
                Push(_reg[BP]);
                var frame = _reg[SP];
                for (var i = 1; i < level; i++)
                {
                    _reg[BP] -= 2;
                    Push(Read16(_seg[SS], _reg[BP]));
                }
                if (level > 0)
                {
                    Push(frame);
                }
                _reg[BP] = frame;
                _reg[SP] -= frameSize;
                break;
            case 0xC9:
                _reg[SP] = _reg[BP];
                _reg[BP] = Pop();
                break;
            case 0xCA:
                var farRelease = Fetch16();
                IP = Pop();
                _seg[CS] = Pop();
                _reg[SP] += farRelease;
                break;
            case 0xCB:
                IP = Pop();
                _seg[CS] = Pop();
                break;
            case 0xCC: SoftwareInt(3); break;
            case 0xCD: SoftwareInt(Fetch8()); break;
            case 0xCE:
                if (GetFlag(Overflow))
                {
                    SoftwareInt(4);
                }
                break;
            case 0xCF:
                IP = Pop();
                _seg[CS] = Pop();
                Flags = Pop();
                break;
            case 0xD0:
            case 0xD1:
            case 0xD2:
            case 0xD3:
                DecodeModRm();
                var count = opcode >= 0xD2 ? Reg8(CX) : 1;
                WriteRm(word, Shift(_regField, ReadRm(word), count, word));
                break;
            case 0xD4: // aam
                var aamBase = Fetch8();
                if (aamBase == 0)
                {
                    Interrupt(0);
                    break;
                }
                var al = Reg8(AX);
                _reg[AX] = (ushort)(((al / aamBase) << 8) | (al % aamBase));
                SetSignZeroParity(_reg[AX], false);
                break;
            case 0xD5: // aad
                var aadBase = Fetch8();
                _reg[AX] = (ushort)((Reg8(4) * aadBase + Reg8(AX)) & 0xFF);
                SetSignZeroParity(_reg[AX], false);
                break;
            case 0xD6: SetReg8(AX, (byte)(GetFlag(Carry) ? 0xFF : 0)); break;
            case 0xD7: SetReg8(AX, Read8(DataSegment, _reg[BX] + Reg8(AX))); break;
            case >= 0xD8 and <= 0xDF: DecodeModRm(); break; // no coprocessor
            case 0xE0:
            case 0xE1:
            case 0xE2:
                var loopDisplacement = (sbyte)Fetch8();
                _reg[CX]--;
                var loops = _reg[CX] != 0 && (opcode == 0xE2 || (opcode == 0xE1) == GetFlag(Zero));
                if (loops)
                {
                    IP = (ushort)(IP + loopDisplacement);
                }
                break;
            case 0xE3:
                var jcxz = (sbyte)Fetch8();
                if (_reg[CX] == 0)
                {
                    IP = (ushort)(IP + jcxz);
                }
                break;
            case 0xE4: SetReg8(AX, _ports.In(Fetch8())); break;
            case 0xE5:
                var inPort = Fetch8();
                _reg[AX] = (ushort)(_ports.In(inPort) | (_ports.In((ushort)(inPort + 1)) << 8));
                break;
            case 0xE6: _ports.Out(Fetch8(), Reg8(AX)); break;
            case 0xE7:
                var outPort = Fetch8();
                _ports.Out(outPort, (byte)_reg[AX]);
                _ports.Out((ushort)(outPort + 1), (byte)(_reg[AX] >> 8));
                break;
            case 0xE8:
                var near = Fetch16();
                Push(IP);
                IP = (ushort)(IP + near);
                break;
            case 0xE9:
                var jump = Fetch16();
                IP = (ushort)(IP + jump);
                break;
            case 0xEA:
                var jumpOffset = Fetch16();
                _seg[CS] = Fetch16();
                IP = jumpOffset;
                break;
            case 0xEB:
                var shortJump = (sbyte)Fetch8();
                IP = (ushort)(IP + shortJump);
                break;
            case 0xEC: SetReg8(AX, _ports.In(_reg[DX])); break;
            case 0xED: _reg[AX] = (ushort)(_ports.In(_reg[DX]) | (_ports.In((ushort)(_reg[DX] + 1)) << 8)); break;
            case 0xEE: _ports.Out(_reg[DX], Reg8(AX)); break;
            case 0xEF:
                _ports.Out(_reg[DX], (byte)_reg[AX]);
                _ports.Out((ushort)(_reg[DX] + 1), (byte)(_reg[AX] >> 8));
                break;
            case 0xF4: Halted = true; break;
            case 0xF5: SetFlag(Carry, !GetFlag(Carry)); break;
            case 0xF6:
            case 0xF7:
                DecodeModRm();
                Group3(word);
                break;
            case 0xF8: SetFlag(Carry, false); break;
            case 0xF9: SetFlag(Carry, true); break;
            case 0xFA: SetFlag(InterruptEnable, false); break;
            case 0xFB: SetFlag(InterruptEnable, true); break;
            case 0xFC: SetFlag(Direction, false); break;
            case 0xFD: SetFlag(Direction, true); break;
            case 0xFE:
                DecodeModRm();
                if (_regField > 1)
                {
                    throw Unsupported(start, opcode);
                }
                WriteRm8(IncDec(ReadRm8(), false, _regField == 1));
                break;
            case 0xFF:
                DecodeModRm();
                Group5(start, opcode);
                break;
            default:
                throw Unsupported(start, opcode);
        }
    }

    private void Arithmetic(int opcode)
    {
        var operation = opcode >> 3;
        var word = (opcode & 1) != 0;
        int a, b;
        switch (opcode & 7)
        {
            case 0:
            case 1:
                DecodeModRm();
                a = ReadRm(word);
                b = ReadRegField(word);
                var toRm = Alu(operation, a, b, word);
                if (operation != 7)
                {
                    WriteRm(word, toRm);
                }
                break;
            case 2:
            case 3:
                DecodeModRm();
                a = ReadRegField(word);
                b = ReadRm(word);
                var toReg = Alu(operation, a, b, word);
                if (operation != 7)
                {
                    WriteRegField(word, toReg);
                }
                break;
            case 4:
                var al = Alu(operation, Reg8(AX), Fetch8(), false);
                if (operation != 7)
                {
                    SetReg8(AX, (byte)al);
                }
                break;
            default:
                var ax = Alu(operation, _reg[AX], Fetch16(), true);
                if (operation != 7)
                {
                    _reg[AX] = (ushort)ax;
                }
                break;
        }
    }

    private void Group5(ushort start, int opcode)
    {
        switch (_regField)
        {
            case 0: WriteRm16(IncDec(ReadRm16(), true, false)); break;
            case 1: WriteRm16(IncDec(ReadRm16(), true, true)); break;
            case 2:
                var target = ReadRm16();
                Push(IP);
                IP = target;
                break;
            case 3:
                var offset = Read16(_seg[_eaSegment], _eaOffset);
                var segment = Read16(_seg[_eaSegment], _eaOffset + 2);
                Push(_seg[CS]);
                Push(IP);
                _seg[CS] = segment;
                IP = offset;
                break;
            case 4: IP = ReadRm16(); break;
            case 5:
                IP = Read16(_seg[_eaSegment], _eaOffset);
                _seg[CS] = Read16(_seg[_eaSegment], _eaOffset + 2);
                break;
            case 6: Push(ReadRm16()); break;
            default: throw Unsupported(start, opcode);
        }
    }

    private void Daa() => DecimalAdjust(+1);

    private void Das() => DecimalAdjust(-1);

    private void DecimalAdjust(int direction)
    {
        var original = Reg8(AX);
        var originalCarry = GetFlag(Carry);
        var al = (int)original;
        var carry = false;
        if ((al & 0x0F) > 9 || GetFlag(AuxCarry))
        {
            al += 6 * direction;
            carry = originalCarry || al is < 0 or > 0xFF;
            SetFlag(AuxCarry, true);
        }
        else
        {
            SetFlag(AuxCarry, false);
        }
        if (original > 0x99 || originalCarry)
        {
            al += 0x60 * direction;
            carry = true;
        }
        SetReg8(AX, (byte)al);
        SetFlag(Carry, carry);
        SetSignZeroParity(al, false);
    }

    private void AsciiAdjust(bool add)
    {
        if ((Reg8(AX) & 0x0F) > 9 || GetFlag(AuxCarry))
        {
            _reg[AX] = (ushort)(add ? _reg[AX] + 0x106 : _reg[AX] - 0x106);
            SetFlag(AuxCarry, true);
            SetFlag(Carry, true);
        }
        else
        {
            SetFlag(AuxCarry, false);
            SetFlag(Carry, false);
        }
        SetReg8(AX, (byte)(Reg8(AX) & 0x0F));
    }

    private InvalidOperationException Unsupported(ushort start, int opcode) =>
        new($"8086: unsupported instruction {opcode:X2} at {_seg[CS]:X4}:{start:X4}");
}
