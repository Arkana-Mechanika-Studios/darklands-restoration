"""The state capture's hot loop run in Python, block for block (ORIGINAL side: in the recordings' key through orig_side.py).

Session.capture runs the original's saved-game writer (R037+0x2ACE) on a copy of the machine that is thrown away afterwards. Most of its
time is spent in R003+0x2264, the compressor's search for the longest earlier match (a hash-chain walk with `cmp`, `cmpsb` and
`repe cmpsb`: one translated block per compared byte). run() executes that function's blocks R003+0x2284 .. R003+0x24AF here instead of
in Unicorn, with exactly what the emulation does at each of them:

* the machine's state: the registers (EFLAGS computed as Unicorn computes them from the last flag-setting instruction), every memory byte
  the blocks write, the same blocks in the same order (a `repe cmpsb` iteration is a block of its own, as Unicorn translates it);
* the per-block work of Session._on_block / Machine._on_block: `count` (+ the block's bytes), `recent`, and with coverage the block's
  instructions, its conditional branch and the (branch, next block) edge, through the same `_decoded` entries and coverage epoch;
* anything else is left to the emulation: run() gives the machine back (registers, memory, CS:IP at the block) at the first block where a
  timer interrupt is due (count >= next_irq with IF set), where the progress line or the instruction budget falls, at the blocks it does not
  model (the entry and exit around the stack switch R003+0x2264..0x2281 / 0x24BC..0x24CF, whose `mov ss` / `sti` shape the next block,
  and the `int3` at R003+0x2450). The emulation then carries on from that block as it would have, its own _on_block taking the interrupt
  there.
Memory is read and written in place (Session maps the machine's megabyte from a buffer of its own, dosmachine.Machine.direct_ram), the
registers are read and written once per run (uc_reg_read_batch / uc_reg_write_batch).

Nothing here changes what the original does or when: the proof is the identical saved games, logs and coverage of the recordings."""
from __future__ import annotations

RECORD, ENTRY, END = 3, 0x2264, 0x24D2
FIRST, LAST = 0x2284, 0x24AF            # the modelled blocks lie between the stack switches
# the blocks (payload offset -> bytes) as Unicorn translates them on the paths through the function
SIZE = {0x2284: 66, 0x22C6: 6, 0x22CC: 24, 0x22D7: 13, 0x22E4: 5, 0x22EA: 16, 0x22FA: 8, 0x2302: 3, 0x2305: 5, 0x230A: 28, 0x2316: 16,
        0x2318: 14, 0x2326: 4, 0x232A: 3, 0x232D: 7, 0x2332: 2, 0x2334: 2, 0x2336: 12, 0x2337: 11, 0x2342: 20, 0x2356: 6, 0x235C: 4,
        0x2361: 6, 0x2368: 8, 0x2370: 5, 0x2375: 29, 0x238C: 6, 0x2392: 11, 0x239D: 15, 0x23A8: 4, 0x23AC: 29, 0x23BA: 15, 0x23C9: 17,
        0x23CA: 16, 0x23DA: 6, 0x23E0: 20, 0x23F4: 22, 0x23FC: 14, 0x240A: 3, 0x240E: 6, 0x2414: 6, 0x241A: 10, 0x2424: 11, 0x242F: 18,
        0x2434: 13, 0x243F: 2, 0x2441: 2, 0x2443: 13, 0x2444: 12, 0x2451: 5, 0x2456: 3, 0x245A: 17, 0x246B: 3, 0x246E: 6, 0x2474: 3,
        0x2477: 18, 0x247A: 15, 0x2489: 11, 0x2494: 24, 0x24A2: 10, 0x24AC: 3, 0x24AF: 13}
CYCLE = frozenset((0x2326, 0x2318, 0x2316, 0x230A))     # the blocks the match test's cycle is entered at
# the lazy flags: the last flag-setting operation and its operands, as the emulator keeps them
LIT, SUBW, SUBB, ADDW, LOGICW, DECW = range(6)
ARITH = 0x8D5                           # CF PF AF ZF SF OF
PARITY = [0x04 if bin(v).count("1") % 2 == 0 else 0 for v in range(256)]


def flags(op, a, b, cf):
    """The arithmetic flags (CF PF AF ZF SF OF) after `op`: a, b its operands (LOGICW and DECW: a the result; DECW: cf the carry before)."""
    if op == LIT:
        return a
    if op == SUBW or op == SUBB:
        m, s = (0xFFFF, 0x8000) if op == SUBW else (0xFF, 0x80)
        r = (a - b) & m
        f = (a < b) | (0x800 if (a ^ b) & (a ^ r) & s else 0) | ((a ^ b ^ r) & 0x10)
    elif op == ADDW:
        m, s = 0xFFFF, 0x8000
        r = (a + b) & m
        f = (a + b > m) | (0x800 if ~(a ^ b) & (a ^ r) & s else 0) | ((a ^ b ^ r) & 0x10)
    elif op == LOGICW:
        m, s, r, f = 0xFFFF, 0x8000, a, 0
    else:                               # DECW
        m, s, r = 0xFFFF, 0x8000, a
        f = cf | (0x800 if r == 0x7FFF else 0) | ((r ^ (r + 1) ^ 1) & 0x10)
    return f | PARITY[r & 0xFF] | (0x40 if r == 0 else 0) | (0x80 if r & s else 0)


class Regs:
    """One native call for many registers (uc_reg_read_batch / uc_reg_write_batch: the same reads and writes as one uc.reg_read / reg_write
    each, in the order given)."""

    def __init__(self, uc, names):
        import ctypes
        from unicorn.unicorn_py3.unicorn import uclib
        from dosmachine import REG
        self.ctypes, self.lib, self.uch, self.n = ctypes, uclib, uc._uch, len(names)
        self.ids = (ctypes.c_int * self.n)(*[REG[x] for x in names])
        self.vals = (ctypes.c_uint64 * self.n)()
        self.ptrs = (ctypes.c_void_p * self.n)(*[ctypes.addressof(self.vals) + 8 * i for i in range(self.n)])

    def read(self):
        self.ctypes.memset(self.vals, 0, 8 * self.n)
        if self.lib.uc_reg_read_batch(self.uch, self.ids, self.ptrs, self.n):
            raise RuntimeError("uc_reg_read_batch")
        return list(self.vals)

    def write(self, values):
        self.vals[:] = values
        if self.lib.uc_reg_write_batch(self.uch, self.ids, self.ptrs, self.n):
            raise RuntimeError("uc_reg_write_batch")


IN = ("AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP", "DS", "ES", "SS", "CS", "EFLAGS")
OUT = ("AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP", "DS", "ES", "SS", "EFLAGS")


def run(m, uc, address: int, size: int) -> bool:
    """At the block callback of `address` (R003 resident, inside a capture): run the modelled blocks from there. True when at least one block
    was run (the machine then continues at the block where run() stopped), False when the emulation is to run this block itself."""
    base = m._fast_base
    pc = address - base
    if SIZE.get(pc) != size or m.count >= m.next_irq or m.ram is None:
        return False
    io = m._fast_io
    if io is None:
        io = m._fast_io = (Regs(uc, IN), Regs(uc, OUT))
    ax, bx, cx, dx, si, di, bp, sp, ds, es, ss, cs, ef = io[0].read()
    if (ef & 0x400 and pc != 0x2284) or not 0 <= base + ENTRY - (cs << 4) <= base + END - (cs << 4) <= 0x10000:
        return False                    # DF set after the `cld` of R003+0x2297, or the function not inside CS
    if ds >= 0xF000 or es >= 0xF000 or ss >= 0xF000:
        return False                    # a word at offset FFFF would read past the machine's memory
    mv = m.ram
    if mv[base + ENTRY:base + END] != m.image(RECORD)[ENTRY:END]:
        return False
    bds, bes, bss = ds << 4, es << 4, ss << 4
    fhi = ef & ~ARITH & 0xFFFF
    fop, fa, fb, fcf = LIT, ef & ARITH, 0, 0
    c = m.count
    lim = m.next_irq if ef & 0x200 else 1 << 62
    lim = min(lim, m.progress + 1, m.budget * 3 - 0x1000)
    E = set()                           # (previous block << 16 | block): the transitions, for the coverage's edges
    eadd = E.add
    prev = 0
    trail = []
    ap = trail.append
    tr = m._fast_trace                  # the capture bench's per-block trace (None in the runs)
    SZ = SIZE.get

    def r16(a):
        return mv[a] | (mv[a + 1] << 8)

    def w16(a, v):
        mv[a] = v & 0xFF
        mv[a + 1] = (v >> 8) & 0xFF

    while c < lim:
        if pc in CYCLE and es == ds and not bds <= bss + ((bp - 8) & 0xFFFF) < bds + 0x10001:
            # the match test's cycle (R003+0x230A .. 0x2336) with the locals [bp-4], [bp-6], [bp-8] in variables (only [bp-8] is written
            # there: back on leaving; DS, which the cycle reads, does not reach it)
            a8 = bss + ((bp - 8) & 0xFFFF)
            p8 = mv[a8] | (mv[a8 + 1] << 8)
            v4 = r16(bss + ((bp - 4) & 0xFFFF))
            v6 = r16(bss + ((bp - 6) & 0xFFFF))
            while True:
                if pc == 0x2326:                            # cmp [bx+di], al; jne 2318
                    if c >= lim:
                        break
                    eadd(prev << 16 | 0x2326)
                    prev = 0x2326
                    ap(0x2326)
                    if tr is not None:
                        tr(base + 0x2326, 4, c)
                    c += 4
                    a, b = mv[bds + ((bx + di) & 0xFFFF)], ax & 0xFF
                    fop, fa, fb = SUBB, a, b
                    if a != b:
                        pc = 0x2318
                        continue
                    # 232a: cmpsb; jne 2316
                    if c >= lim:
                        pc = 0x232A
                        break
                    eadd(0x2326 << 16 | 0x232A)
                    prev = 0x232A
                    ap(0x232A)
                    if tr is not None:
                        tr(base + 0x232A, 3, c)
                    c += 3
                    a, b = mv[bds + si], mv[bds + di]
                    si, di = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF
                    fop, fa, fb = SUBB, a, b
                    if a != b:
                        pc = 0x2316
                        continue
                    # 232d: mov cx,202h; inc si; inc di; repe cmpsb (its first iteration)
                    if c >= lim:
                        pc = 0x232D
                        break
                    eadd(0x232A << 16 | 0x232D)
                    prev = 0x232D
                    ap(0x232D)
                    if tr is not None:
                        tr(base + 0x232D, 7, c)
                    c += 7
                    si, di = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF
                    a, b = mv[bds + si], mv[bds + di]
                    si, di, cx = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF, 0x201
                    fop, fa, fb = SUBB, a, b
                    if a == b:
                        # 2332: the further iterations, a block each (equal bytes leave the same flags whichever they are)
                        if c >= lim:
                            pc = 0x2332
                            break
                        n_max = (lim - c + 1) // 2
                        eadd(0x232D << 16 | 0x2332)
                        prev = 0x2332
                        n = 0
                        stay = True
                        while n < n_max:
                            if tr is not None:
                                tr(base + 0x2332, 2, c + n * 2)
                            n += 1
                            if cx == 0:
                                stay = False
                                break
                            a, b = mv[bds + si], mv[bds + di]
                            si, di, cx = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF, cx - 1
                            if a != b:
                                fop, fa, fb = SUBB, a, b
                                stay = False
                                break
                        c += n * 2
                        trail.extend([0x2332] * min(n, 64))
                        if stay:
                            pc = 0x2332
                            break
                    # 2334: je 2337
                    if c >= lim:
                        pc = 0x2334
                        break
                    eadd(prev << 16 | 0x2334)
                    prev = 0x2334
                    ap(0x2334)
                    if tr is not None:
                        tr(base + 0x2334, 2, c)
                    c += 2
                    pc = 0x2337 if fa == fb else 0x2336
                    # 2336 / 2337: (inc cx); sub cx,205h; not cx; cmp [bp-6],cx; ja 230a
                    if c >= lim:
                        break
                    eadd(0x2334 << 16 | pc)
                    prev = pc
                    ap(pc)
                    if pc == 0x2336:
                        if tr is not None:
                            tr(base + 0x2336, 12, c)
                        c += 12
                        cx = (cx + 1) & 0xFFFF
                    else:
                        if tr is not None:
                            tr(base + 0x2337, 11, c)
                        c += 11
                    cx = (~(cx - 0x205)) & 0xFFFF
                    fop, fa, fb = SUBW, v6, cx
                    pc = 0x230A if v6 > cx else 0x2342
                    if pc == 0x2342:
                        break
                else:                                       # 2318 / 2316 / 230a
                    if c >= lim:
                        break
                    eadd(prev << 16 | pc)
                    prev = pc
                    ap(pc)
                    if pc == 0x2318:
                        if tr is not None:
                            tr(base + 0x2318, 14, c)
                        c += 14
                    elif pc == 0x2316:                      # mov si,cx
                        if tr is not None:
                            tr(base + 0x2316, 16, c)
                        c += 16
                        si = cx
                    else:                                   # 230a: mov bx,[bp-6]; dec bx; mov si,[bp-4]; mov al,[bx+si]; mov cx,si; nop; mov si,cx
                        if tr is not None:
                            tr(base + 0x230A, 28, c)
                        c += 28
                        bx = (v6 - 1) & 0xFFFF
                        si = cx = v4
                        ax = (ax & 0xFF00) | mv[bds + ((bx + si) & 0xFFFF)]
                    # 2318: mov di,[bp-8]; inc di; inc di; mov [bp-8],di; mov di,[di]; cmp di,dx; jae 22fa
                    p8 = (p8 + 2) & 0xFFFF
                    a = bds + p8
                    di = mv[a] | (mv[a + 1] << 8)
                    fop, fa, fb = SUBW, di, dx
                    if di >= dx:
                        pc = 0x22FA
                        break
                    pc = 0x2326
            mv[a8] = p8 & 0xFF
            mv[a8 + 1] = p8 >> 8
            if pc in CYCLE:
                continue
            if c >= lim:
                break
        sz = SZ(pc)
        if sz is None:
            break
        if pc == 0x2284 or pc == 0x24AF:
            if r16(bds + 0x6F0C if pc == 0x2284 else bss + sp) >= 0xF000:
                break                   # the segment the block loads is not modelled
        if pc == 0x2332 or pc == 0x243F:
            # a run of `repe cmpsb` iterations, one block each (no conditional branch: no edge after them)
            n_max = (lim - c + sz - 1) // sz
            eadd(prev << 16 | pc)
            prev = pc
            n = 0
            nxt = pc + 2
            while n < n_max:
                if tr is not None:
                    tr(base + pc, sz, c + n * sz)
                n += 1
                if cx == 0:
                    break
                a, b = mv[bds + si], mv[bes + di]
                si, di, cx = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF, cx - 1
                fop, fa, fb = SUBB, a, b
                if a != b:
                    break
            else:
                nxt = pc
            c += n * sz
            trail.extend([pc] * min(n, 64))
            pc = nxt
            continue
        eadd(prev << 16 | pc)
        prev = pc
        ap(pc)
        if tr is not None:
            tr(base + pc, sz, c)
        c += sz
        # ---- the block's instructions ----
        if pc == 0x2326:                                    # cmp [bx+di], al; jne 2318
            a, b = mv[bds + ((bx + di) & 0xFFFF)], ax & 0xFF
            fop, fa, fb = SUBB, a, b
            pc = 0x2318 if a != b else 0x232A
        elif pc == 0x2318 or pc == 0x2316 or pc == 0x230A:
            if pc == 0x230A:                                # mov bx,[bp-6]; dec bx; mov si,[bp-4]; mov al,[bx+si]; mov cx,si; nop
                a = bss + ((bp - 6) & 0xFFFF)
                bx = ((mv[a] | (mv[a + 1] << 8)) - 1) & 0xFFFF
                a = bss + ((bp - 4) & 0xFFFF)
                si = mv[a] | (mv[a + 1] << 8)
                ax = (ax & 0xFF00) | mv[bds + ((bx + si) & 0xFFFF)]
                cx = si
            if pc != 0x2318:
                si = cx                                     # 2316: mov si, cx
            a = bss + ((bp - 8) & 0xFFFF)                   # 2318: mov di,[bp-8]; inc di; inc di; mov [bp-8],di
            di = ((mv[a] | (mv[a + 1] << 8)) + 2) & 0xFFFF
            mv[a] = di & 0xFF
            mv[a + 1] = di >> 8
            a = bds + di                                    # mov di,[di]; cmp di,dx; jae 22fa
            di = mv[a] | (mv[a + 1] << 8)
            fop, fa, fb = SUBW, di, dx
            pc = 0x22FA if di >= dx else 0x2326
        elif pc == 0x232A:                                  # cmpsb; jne 2316
            a, b = mv[bds + si], mv[bes + di]
            si, di = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF
            fop, fa, fb = SUBB, a, b
            pc = 0x2316 if a != b else 0x232D
        elif pc == 0x232D:                                  # mov cx,202h; inc si; inc di; repe cmpsb (first iteration: cx != 0)
            si, di = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF
            a, b = mv[bds + si], mv[bes + di]
            si, di, cx = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF, 0x201
            fop, fa, fb = SUBB, a, b
            pc = 0x2334 if a != b else 0x2332
        elif pc == 0x2334 or pc == 0x2441:                  # je +1
            z = (fa == fb) if fop == SUBB or fop == SUBW else bool(flags(fop, fa, fb, fcf) & 0x40)
            pc = pc + 3 if z else pc + 2
        elif pc == 0x2336 or pc == 0x2337:                  # (inc cx); sub cx,205h; not cx; cmp [bp-6],cx; ja 230a
            if pc == 0x2336:
                cx = (cx + 1) & 0xFFFF
            cx = (~(cx - 0x205)) & 0xFFFF
            a = r16(bss + ((bp - 6) & 0xFFFF))
            fop, fa, fb = SUBW, a, cx
            pc = 0x230A if a > cx else 0x2342
        elif pc == 0x2342:                                  # lea ax,[si-1]; sub ax,di; mov bx,ss:[6F0A]; mov [bx],ax; mov [bp-6],cx; cmp cx,0Eh; jb 230a
            ax = (si - 1 - di) & 0xFFFF
            bx = r16(bss + 0x6F0A)
            w16(bds + bx, ax)
            w16(bss + ((bp - 6) & 0xFFFF), cx)
            fop, fa, fb = SUBW, cx, 0x0E
            pc = 0x230A if cx < 0x0E else 0x2356
        elif pc == 0x23BA or pc == 0x23AC:
            if pc == 0x23AC:                                # mov [bp-e],si; mov [bp-c],di; mov bx,[bp-8]; mov di,[bx]; add di,cx; inc di
                w16(bss + ((bp - 0x0E) & 0xFFFF), si)
                w16(bss + ((bp - 0x0C) & 0xFFFF), di)
                bx = r16(bss + ((bp - 8) & 0xFFFF))
                di = (r16(bds + bx) + cx + 1) & 0xFFFF
            di = (di - 1) & 0xFFFF                          # 23ba: dec di; mov si,cx; mov bx,si; mov si,ss:[bx+si+6F12]; cmp si,-1; jne 23ca
            bx = cx
            si = r16(bss + ((bx + cx + 0x6F12) & 0xFFFF))
            fop, fa, fb = SUBW, si, 0xFFFF
            pc = 0x23CA if si != 0xFFFF else 0x23C9
        elif pc == 0x23CA or pc == 0x23C9:
            if pc == 0x23C9:                                # inc si
                si = (si + 1) & 0xFFFF
            a = bss + ((bp - 8) & 0xFFFF)                   # add word [bp-8],2; mov bx,[bp-8]; mov ax,[bx]; mov [bp-a],ax; cmp ax,dx; jae 2361
            bx = (r16(a) + 2) & 0xFFFF
            w16(a, bx)
            ax = r16(bds + bx)
            w16(bss + ((bp - 0x0A) & 0xFFFF), ax)
            fop, fa, fb = SUBW, ax, dx
            pc = 0x2361 if ax >= dx else 0x23DA
        elif pc == 0x23DA:                                  # add ax,si; cmp ax,di; jb 23ca
            ax = (ax + si) & 0xFFFF
            fop, fa, fb = SUBW, ax, di
            pc = 0x23CA if ax < di else 0x23E0
        elif pc == 0x23E0:                                  # bx = [bp-6]+[bp-a]; ax = [bx-2]; bx = [bp-4]+[bp-6]; cmp [bx-2],ax; je 2424
            v6 = r16(bss + ((bp - 6) & 0xFFFF))
            bx = (v6 + r16(bss + ((bp - 0x0A) & 0xFFFF))) & 0xFFFF
            ax = r16(bds + ((bx - 2) & 0xFFFF))
            bx = (r16(bss + ((bp - 4) & 0xFFFF)) + v6) & 0xFFFF
            a = r16(bds + ((bx - 2) & 0xFFFF))
            fop, fa, fb = SUBW, a, ax
            pc = 0x2424 if a == ax else 0x23F4
        elif pc == 0x2424:                                  # mov cx,si; mov ax,[bp-a]; add ax,cx; cmp ax,di; je 2434
            cx = si
            ax = (r16(bss + ((bp - 0x0A) & 0xFFFF)) + cx) & 0xFFFF
            fop, fa, fb = SUBW, ax, di
            pc = 0x2434 if ax == di else 0x242F
        elif pc == 0x241A:                                  # mov cx,2; mov [bp-a],di; add di,cx; jmp 2434
            cx = 2
            w16(bss + ((bp - 0x0A) & 0xFFFF), di)
            fop, fa, fb = ADDW, di, 2
            di = (di + 2) & 0xFFFF
            pc = 0x2434
        elif pc == 0x242F or pc == 0x2434:
            if pc == 0x242F:                                # sub cx,cx; mov di,[bp-a]
                cx = 0
                di = r16(bss + ((bp - 0x0A) & 0xFFFF))
            si = (r16(bss + ((bp - 4) & 0xFFFF)) + cx) & 0xFFFF   # mov si,[bp-4]; add si,cx; sub cx,205h; not cx; repe cmpsb (first iteration)
            fop, fa, fb = SUBW, cx, 0x205
            cx = (~(cx - 0x205)) & 0xFFFF
            if cx == 0:
                pc = 0x2441
            else:
                a, b = mv[bds + si], mv[bes + di]
                si, di, cx = (si + 1) & 0xFFFF, (di + 1) & 0xFFFF, cx - 1
                fop, fa, fb = SUBB, a, b
                pc = 0x2441 if a != b else 0x243F
        elif pc == 0x2443 or pc == 0x2444:                  # (inc cx); sub cx,205h; not cx; cmp cx,204h; jbe 2451 (else int3)
            if pc == 0x2443:
                cx = (cx + 1) & 0xFFFF
            cx = (~(cx - 0x205)) & 0xFFFF
            fop, fa, fb = SUBW, cx, 0x204
            pc = 0x2451 if cx <= 0x204 else 0x2450
        elif pc == 0x2451:                                  # cmp [bp-6],cx; jbe 245a
            a = r16(bss + ((bp - 6) & 0xFFFF))
            fop, fa, fb = SUBW, a, cx
            pc = 0x245A if a <= cx else 0x2456
        elif pc == 0x2456 or pc == 0x246B or pc == 0x24AC:  # jmp 23ba
            pc = 0x23BA
        elif pc == 0x245A:                                  # lea ax,[si-1]; sub ax,di; mov bx,ss:[6F0A]; mov [bx],ax; cmp [bp-6],cx; jb 246e
            ax = (si - 1 - di) & 0xFFFF
            bx = r16(bss + 0x6F0A)
            w16(bds + bx, ax)
            a = r16(bss + ((bp - 6) & 0xFFFF))
            fop, fa, fb = SUBW, a, cx
            pc = 0x246E if a < cx else 0x246B
        elif pc == 0x23FC or pc == 0x23F4:
            if pc == 0x23F4:                                # mov bx,[bp-6]; dec bx; dec bx; mov si,[bp-4]
                bx = (r16(bss + ((bp - 6) & 0xFFFF)) - 2) & 0xFFFF
                si = r16(bss + ((bp - 4) & 0xFFFF))
            a = bss + ((bp - 8) & 0xFFFF)                   # mov di,[bp-8]; inc di; inc di; mov [bp-8],di; mov di,[di]; cmp di,dx; jb 240e
            di = (r16(a) + 2) & 0xFFFF
            w16(a, di)
            di = r16(bds + di)
            fop, fa, fb = SUBW, di, dx
            pc = 0x240E if di < dx else 0x240A
        elif pc == 0x240E:                                  # mov ax,[bx+di]; cmp [bx+si],ax; jne 23fc
            ax = r16(bds + ((bx + di) & 0xFFFF))
            a = r16(bds + ((bx + si) & 0xFFFF))
            fop, fa, fb = SUBW, a, ax
            pc = 0x23FC if a != ax else 0x2414
        elif pc == 0x2414:                                  # mov al,[di]; cmp [si],al; jne 23fc
            ax = (ax & 0xFF00) | mv[bds + di]
            a = mv[bds + si]
            fop, fa, fb = SUBB, a, ax & 0xFF
            pc = 0x23FC if a != (ax & 0xFF) else 0x241A
        elif pc == 0x22C6:                                  # inc bx; inc bx; cmp [bx],di; jb 22c6
            bx = (bx + 2) & 0xFFFF
            a = r16(bds + bx)
            fop, fa, fb = SUBW, a, di
            pc = 0x22C6 if a < di else 0x22CC
        elif pc == 0x22CC or pc == 0x22D7:
            if pc == 0x22CC:                                # mov [bp-8],bx; xchg bx,ax; mov bx,[bp-a]; mov [bx+si+9A8h],ax
                w16(bss + ((bp - 8) & 0xFFFF), bx)
                ax = bx
                bx = r16(bss + ((bp - 0x0A) & 0xFFFF))
                w16(bds + ((bx + si + 0x9A8) & 0xFFFF), ax)
            bx = r16(bss + ((bp - 8) & 0xFFFF))             # 22d7: mov bx,[bp-8]; mov di,[bx]; mov dx,[bp-10h]; dec dx; cmp di,dx; jb 22ea
            di = r16(bds + bx)
            dx = (r16(bss + ((bp - 0x10) & 0xFFFF)) - 1) & 0xFFFF
            fop, fa, fb = SUBW, di, dx
            pc = 0x22EA if di < dx else 0x22E4
        elif pc == 0x22EA:                                  # mov si,[bp-10h]; mov cx,si; mov [bp-4],si; mov bx,[bp-6]; dec bx; mov al,[bx+si]; jmp 2326
            si = r16(bss + ((bp - 0x10) & 0xFFFF))
            cx = si
            w16(bss + ((bp - 4) & 0xFFFF), si)
            fcf = flags(fop, fa, fb, fcf) & 1
            bx = (r16(bss + ((bp - 6) & 0xFFFF)) - 1) & 0xFFFF
            fop, fa, fb = DECW, bx, 0
            ax = (ax & 0xFF00) | mv[bds + ((bx + si) & 0xFFFF)]
            pc = 0x2326
        elif pc == 0x22FA:                                  # mov ax,[bp-6]; cmp ax,2; jb 2305
            ax = r16(bss + ((bp - 6) & 0xFFFF))
            fop, fa, fb = SUBW, ax, 2
            pc = 0x2305 if ax < 2 else 0x2302
        elif pc == 0x2302:                                  # jmp 24af
            pc = 0x24AF
        elif pc == 0x2305:                                  # xor ax,ax; jmp 24af
            ax = 0
            fop, fa, fb = LOGICW, 0, 0
            pc = 0x24AF
        elif pc == 0x22E4:                                  # sub ax,ax; jmp 24af
            ax = 0
            fop, fa, fb = SUBW, 0, 0
            pc = 0x24AF
        elif pc == 0x2356:                                  # cmp cx,204h; jne 2368
            fop, fa, fb = SUBW, cx, 0x204
            pc = 0x2368 if cx != 0x204 else 0x235C
        elif pc == 0x235C:                                  # xchg cx,ax; jmp 24af
            cx, ax = ax, cx
            pc = 0x24AF
        elif pc == 0x2361:                                  # mov ax,[bp-6]; jmp 24af
            ax = r16(bss + ((bp - 6) & 0xFFFF))
            pc = 0x24AF
        elif pc == 0x2368:                                  # mov bx,[bp-8]; cmp [bx+2],dx; jae 2361
            bx = r16(bss + ((bp - 8) & 0xFFFF))
            a = r16(bds + ((bx + 2) & 0xFFFF))
            fop, fa, fb = SUBW, a, dx
            pc = 0x2361 if a >= dx else 0x2370
        elif pc == 0x2370:                                  # cmp [bx+4],dx; jae 230a
            a = r16(bds + ((bx + 4) & 0xFFFF))
            fop, fa, fb = SUBW, a, dx
            pc = 0x230A if a >= dx else 0x2375
        elif pc == 0x2375 or pc == 0x238C:
            if pc == 0x2375:                                # sub si,si; mov ss:[6F14],si; mov ss:[6F12],FFFFh; mov di,1; mov bx,[bp-4]; mov cx,[bp-6]
                si = 0
                w16(bss + 0x6F14, 0)
                w16(bss + 0x6F12, 0xFFFF)
                di = 1
                bx = r16(bss + ((bp - 4) & 0xFFFF))
                cx = r16(bss + ((bp - 6) & 0xFFFF))
            ax = (ax & 0xFF00) | mv[bds + ((bx + di) & 0xFFFF)]   # 238c: mov al,[bx+di]; cmp [bx+si],al; je 239d
            a = mv[bds + ((bx + si) & 0xFFFF)]
            fop, fa, fb = SUBB, a, ax & 0xFF
            pc = 0x239D if a == (ax & 0xFF) else 0x2392
        elif pc == 0x2392:                                  # shl si,1; mov si,ss:[si+6F12]; test si,si; jns 23a8
            si = r16(bss + ((((si << 1) & 0xFFFF) + 0x6F12) & 0xFFFF))
            fop, fa, fb = LOGICW, si, 0
            pc = 0x23A8 if not si & 0x8000 else 0x239D
        elif pc == 0x239D or pc == 0x23A8:
            if pc == 0x239D:                                # inc si; inc di; shl di,1; mov ss:[di+6F12],si; shr di,1
                si = (si + 1) & 0xFFFF
                di = ((di + 1) << 1) & 0xFFFF
                w16(bss + ((di + 0x6F12) & 0xFFFF), si)
                di >>= 1
            fop, fa, fb = SUBW, cx, di                      # 23a8: cmp cx,di; ja 238c
            pc = 0x238C if cx > di else 0x23AC
        elif pc == 0x240A:                                  # jmp 2361
            pc = 0x2361
        elif pc == 0x246E:                                  # cmp cx,204h; jne 2477
            fop, fa, fb = SUBW, cx, 0x204
            pc = 0x2477 if cx != 0x204 else 0x2474
        elif pc == 0x2474:                                  # jmp 235c
            pc = 0x235C
        elif pc == 0x2477 or pc == 0x247A:
            if pc == 0x2477:                                # mov [bp-6],cx
                w16(bss + ((bp - 6) & 0xFFFF), cx)
            si = r16(bss + ((bp - 4) & 0xFFFF))             # 247a: mov si,[bp-4]; mov bx,[bp-c]; mov al,[bx+si]; mov bx,[bp-e]; cmp [bx+si],al; je 2494
            bx = r16(bss + ((bp - 0x0C) & 0xFFFF))
            ax = (ax & 0xFF00) | mv[bds + ((bx + si) & 0xFFFF)]
            bx = r16(bss + ((bp - 0x0E) & 0xFFFF))
            a = mv[bds + ((bx + si) & 0xFFFF)]
            fop, fa, fb = SUBB, a, ax & 0xFF
            pc = 0x2494 if a == (ax & 0xFF) else 0x2489
        elif pc == 0x2489:                                  # shl bx,1; mov bx,ss:[bx+6F12]; test bx,bx; jns 24a2
            bx = r16(bss + ((((bx << 1) & 0xFFFF) + 0x6F12) & 0xFFFF))
            fop, fa, fb = LOGICW, bx, 0
            pc = 0x24A2 if not bx & 0x8000 else 0x2494
        elif pc == 0x2494 or pc == 0x24A2:
            if pc == 0x2494:                                # inc bx; inc word [bp-c]; mov si,[bp-c]; shl si,1; mov ss:[si+6F12],bx
                bx = (bx + 1) & 0xFFFF
                a = bss + ((bp - 0x0C) & 0xFFFF)
                v = (r16(a) + 1) & 0xFFFF
                w16(a, v)
                si = (v << 1) & 0xFFFF
                w16(bss + ((si + 0x6F12) & 0xFFFF), bx)
            w16(bss + ((bp - 0x0E) & 0xFFFF), bx)           # 24a2: mov [bp-e],bx; mov ax,[bp-c]; cmp cx,ax; ja 247a
            ax = r16(bss + ((bp - 0x0C) & 0xFFFF))
            fop, fa, fb = SUBW, cx, ax
            pc = 0x247A if cx > ax else 0x24AC
        elif pc == 0x2284:
            # push bp; mov bp,sp; sub sp,0Eh; push bx; push di; push si; mov word [bp-6],1; push ds
            sp = (sp - 2) & 0xFFFF
            w16(bss + sp, bp)
            bp = sp
            sp = (sp - 0x0E) & 0xFFFF
            for v in (bx, di, si):
                sp = (sp - 2) & 0xFFFF
                w16(bss + sp, v)
            w16(bss + ((bp - 6) & 0xFFFF), 1)
            sp = (sp - 2) & 0xFFFF
            w16(bss + sp, ds)
            ds = r16(bds + 0x6F0C)                          # mov ds,[6F0C]; cld
            bds = ds << 4
            fhi &= ~0x400
            di = bx                                         # mov di,bx; mov al,[bx+1]; mov bl,[bx]; sub bh,bh; sub ah,ah; mov cl,3; shl bx,cl; add bx,ax; shl bx,1
            ax = mv[bds + ((bx + 1) & 0xFFFF)]
            bx = mv[bds + bx]
            cx = (cx & 0xFF00) | 3
            bx = (((bx << 3) + ax) << 1) & 0xFFFF
            w16(bss + ((bp - 0x0A) & 0xFFFF), bx)           # mov [bp-a],bx; pop es; push es (the same word back)
            es = r16(bss + sp)
            bes = es << 4
            si = r16(bes + 0x6F0A)                          # mov si,es:[6F0A]; mov bx,[bx+si+9A8h]; mov [bp-8],bx; sub di,[si+0Ch]; inc di
            bx = r16(bds + ((bx + si + 0x9A8) & 0xFFFF))
            w16(bss + ((bp - 8) & 0xFFFF), bx)
            di = (di - r16(bds + ((si + 0x0C) & 0xFFFF)) + 1) & 0xFFFF
            w16(bss + ((sp - 2) & 0xFFFF), ds)              # push ds; pop es
            es, bes = ds, bds
            a = r16(bds + bx)                               # cmp [bx],di; jae 22d7
            fop, fa, fb = SUBW, a, di
            pc = 0x22D7 if a >= di else 0x22C6
        elif pc == 0x24AF:                                  # pop ds; pop si; pop di; mov sp,bp; pop bp; cmp word [6F08],0; je 24c7
            ds = r16(bss + sp)
            si = r16(bss + ((sp + 2) & 0xFFFF))
            di = r16(bss + ((sp + 4) & 0xFFFF))
            sp = bp
            bp = r16(bss + sp)
            sp = (sp + 2) & 0xFFFF
            bds = ds << 4
            a = r16(bds + 0x6F08)
            fop, fa, fb = SUBW, a, 0
            pc = 0x24C7 if a == 0 else 0x24BC
        else:
            raise RuntimeError("R003+0x%04X: a block fast_capture.SIZE lists without its instructions" % pc)
    if prev == 0:
        return False
    # ---- the machine back at block pc (CS:IP last: the emulation restarts there) ----
    io[1].write((ax, bx, cx, dx, si, di, bp, sp, ds, es, ss, fhi | flags(fop, fa, fb, fcf)))
    uc.reg_write(UC_IP, (base + pc - (cs << 4)) & 0xFFFF)      # one write of the PC, as Session's interrupt does it: the emulation restarts there
    m.count = c
    m.recent.extend([base + p for p in trail[-64:]])
    if m.coverage:
        # what Machine._on_block adds per block: its instructions (once per coverage epoch, through the `_decoded` entry), and after a block
        # that ends in a conditional branch the (branch, next block) edge; a set either way, so in any order
        epoch, ci, decoded, md = m._cov_epoch, m.cov_insns, m._decoded, m.md
        cache = m._fast_cov             # the entries and transitions already added in this coverage epoch (nothing new to add for them)
        if cache is None or cache[0] is not decoded or cache[1] != epoch:
            cache = m._fast_cov = [decoded, epoch, {}, set()]
        ents, done = cache[2], cache[3]

        def visit(p):
            ent = ents.get(p)
            if ent is not None:
                return ent
            lin = base + p
            ent = decoded.get(lin)
            if ent is None:
                code = bytes(mv[lin:lin + SIZE[p]])
                lst = [(lin + i.address, code[i.address:i.address + i.size], i.mnemonic) for i in md.disasm(code, 0)]
                ent = decoded[lin] = [lst, lst[-1][0] if lst and lst[-1][2] in CONDITIONAL else None, -1]
            if ent[2] != epoch:
                for a, code, mn in ent[0]:
                    if a not in ci:
                        ci[a] = code
                ent[2] = epoch
            ents[p] = ent
            return ent
        edges = m.cov_edges
        for k in E - done:
            p, q = k >> 16, k & 0xFFFF
            visit(q)
            if p == 0:
                if m._last_block_jcc is not None:
                    edges.add((m._last_block_jcc, base + q))
            else:
                done.add(k)
                j = visit(p)[1]
                if j is not None:
                    edges.add((j, base + q))
        m._last_block_jcc = visit(prev)[1]
    return True


from run_unit import CONDITIONAL  # noqa: E402
from unicorn.x86_const import UC_X86_REG_IP as UC_IP  # noqa: E402
