"""Side-by-side pictures of the original and the C# rewrite at the same step of a lockstep session (for judging visual-only differences by eye).

usage:
  python -I -B vis10.py run <script.steps> <out folder> [--save <DKSAVEn.SAV>] [--headless <exe>]   one lockstep session, both screens dumped
  python -I -B vis10.py compose <out folder> <name> <label-original> <label-restoration> <result stem> [--scale 3]
        <name>: a shot name (one image) or a series name (the frames <name>_00 ... as an animated GIF)
  python -I -B vis10.py jobs [<id> ...]      the jobs of vis10-sessions/jobs.txt (runs, then composes into scratch/vis10)
  python -I -B vis10.py index [<notes.json>]   scratch/vis10/index.html from the pictures there and a {file or id: remark} table
  python -I -B vis10.py wait-commit                                                            blocks until >= 4 GB of commit is free

The tester's machine draws nothing (every graphics-driver slot is a stub); the session class made here loads the game's own MGRAPHIC.EXE
through the game's loader and installer, so the original draws into A000:0000 as it does on a PC. Do not use `state:` steps with it.
Pairing is by script step (the original's idle moment, the host's frame after the step's waits); a moving picture is paired by pump or pass.

The script is an ordinary tools/oracle/lockstep-sessions script with extra step kinds (below), ignored by lockstep.py itself:

  shot:<name>              both sides save the screen at that step (the C# host's own step; the original at the same idle moment).
                           <out>/orig/<name>.idx and <out>/cs/<name>.idx: 64000 palette indices then the 256 colours as 8-bit R, G, B
                           (the C# host's .idx format; the original's colours are the VGA DAC's 6-bit values widened as (v << 2) | (v >> 4)).
  shotsave:<name>          (original only) the screen at the next file the game creates: the "Saving Game" box of a save. The C# host
                           has no such moment between two frames: its step is rec:<n> (frames rec00001.idx ... the host presents).
  series:<name>,<n>,<ms>   (before a key / click / rclick step) frames <name>_00 .. <name>_<n-1>, one every <ms> milliseconds, from the
                           moment that input is taken: the original dumps the screen on its emulated clock, the C# host after waits of
                           ms / 16 pumps (its pump is 16 ms; the host's first frame comes two pumps after the input). The times agree to
                           about two pumps, not exactly. series:<name>,<n>,p<k>: every k battle passes instead (in a battle the C# host's
                           frame is a pass and the emulated original's milliseconds are not the host's; the host's first frame is three
                           passes after the input).

This module only reads the emulated machine (the 320x200 mode-13h framebuffer A000:0000 and the DAC it tracks through ports 3C8 / 3C9 and
INT 10h AH=10h): it changes nothing the original does, it replaces no recording (it runs live into its own recordings folder), and it is
not part of any recording's key. The original is run by orig_side's Session subclass ShotSession through lockstep.main().
"""
from __future__ import annotations

import base64
import ctypes
import os
import struct
import sys
import time
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

# the project root (holding code\, game\, scratch\): DARKLANDS_ROOT, else the folder that holds the repository (a worktree in
# worktrees\<round> needs the variable: its parent folder is worktrees\)
ROOT = Path(os.environ.get("DARKLANDS_ROOT") or HERE.parent.parent.parent)
VGA_BASE = 0xA0000
W, H = 320, 200


# ---- memory guard (README: >= 4 GB free commit before each run) ----------------------------------------------------------
class _Mem(ctypes.Structure):
    _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong), ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong), ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong), ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


def free_commit_gb() -> float:
    m = _Mem()
    m.dwLength = ctypes.sizeof(m)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    return m.ullAvailPageFile / 2**30


def wait_commit(gb: float = 4.0):
    while True:
        f = free_commit_gb()
        if f >= gb:
            return f
        print("waiting: %.1f GB commit free (< %.1f)" % (f, gb), flush=True)
        time.sleep(30)


# ---- the original side ---------------------------------------------------------------------------------------------------
def write_idx(path: Path, pixels: bytes, dac: bytes):
    pal = bytearray()
    for v in dac:
        pal.append(((v & 63) << 2) | ((v & 63) >> 4))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bytes(pixels) + bytes(pal))


class ShotList(list):
    """The session's script: popping a shot step dumps the screen, popping the input a series was armed for starts the series."""

    def __init__(self, items, owner):
        super().__init__(items)
        self.owner = owner

    def pop(self, index=-1):
        item = super().pop(index)
        o = self.owner
        if item[0] == "shot":
            o.dump(item[1])
        elif item[0] == "shotsave":
            o.on_create = item[1]
        elif item[0] == "series":
            name, n, spec = item[1].split(",")
            o.armed = (name, int(n), spec)
        elif item[0] in ("key", "click", "rclick") and o.armed is not None:
            name, n, spec = o.armed
            o.armed = None
            if spec.startswith("p"):         # every p battle passes (a battle's time is its passes; the host's first frame is three passes after the input)
                o.series = [name, n, int(spec[1:]), 0, o.battle_passes + 3, True]
            else:                            # every ms milliseconds (the host's first frame is two pumps after the input)
                o.series = [name, n, int(spec), 0, o.count + 2 * 16 * o.bytes_per_ms, False]
        return item


def make_session_class():
    import orig_side
    base = orig_side.Session

    class ShotSession(base):
        out_dir: Path = Path(".")

        # ---- the graphics driver: the tester's machine stubs every driver slot (nothing is drawn); here MGRAPHIC.EXE's own code is
        # loaded as the game's loader (root 047C:07CE with DOS function 4B03h) and installed (root 047C:08C8), and its slots are no
        # longer stubbed, so the original draws into A000:0000. misc.exe's slots (keys, mouse ...) and the sound driver stay stubbed.
        gfx_slots: set = frozenset()
        gfx_loading = False
        gfx_range = None

        def stub(self, seg, off, handler, name=None, record=None):
            if (seg, off) == orig_side.DRIVER_LOAD:
                real = handler

                def load(m, real=real):
                    nm = m.cstr(orig_side.DS, m.arg(0)).lower()
                    if nm == b"mgraphic.exe":
                        m.gfx_loading = True
                        return None                         # the game's own loader runs
                    return real(m)
                handler = load
            elif (seg, off) == orig_side.DRIVER_SLOTS:
                real = handler

                def install(m, real=real):
                    if not m.gfx_loading:
                        return real(m)
                    m.gfx_loading = False
                    base = m.arg(0)
                    first, count = m.word(base, 0x2E), m.word(base, 0x30)
                    m.gfx_slots = frozenset(range(first, first + count))
                    m.log.append("graphics driver at %04X: slots %d..%d" % (base, first, first + count - 1))
                    return None                             # the game's own installer runs
                handler = install
            elif seg == orig_side.DRIVER_SEG and orig_side.DRIVER_FIRST - 2 <= off < orig_side.DRIVER_LAST and (off - 0x4C5) % 7 in (0, 2):
                # slot k is 7 bytes from 047C:04C5 + 7k: `00 00 EA <far pointer>` (stubbed at its EA) or `E8 <rel to 048A> <far pointer>`
                # (a lazy slot, stubbed at its call): the installer writes the far pointer at +3
                real, k = handler, (off - 0x4C5) // 7

                def slot(m, real=real, k=k):
                    return None if k in m.gfx_slots else real(m)
                handler = slot
            return super().stub(seg, off, handler, name, record)

        def _valid(self, lin):
            return (self.gfx_range is not None and self.gfx_range[0] <= lin < self.gfx_range[1]) or super()._valid(lin)

        def _load_overlay(self):
            """INT 21h AX=4B03h: DS:DX the file name, ES:BX = {load segment, relocation factor}: the EXE's image without its header, relocated."""
            name = self._dsdx_name()
            data = self.vfs.get(name)
            if data is None:
                self._carry(True, 2)
                return
            data = bytes(data)
            cparhdr, nreloc, reloff = struct.unpack_from("<H", data, 8)[0], struct.unpack_from("<H", data, 6)[0], struct.unpack_from("<H", data, 0x18)[0]
            cblp, cp = struct.unpack_from("<HH", data, 2)[1], struct.unpack_from("<H", data, 4)[0]
            size = (cp - 1) * 512 + (cblp or 512)
            image = bytearray(data[cparhdr * 16:size])
            seg, factor = self.word(self.reg("ES"), self.reg("BX")), self.word(self.reg("ES"), self.reg("BX") + 2)
            for k in range(nreloc):
                roff, rseg = struct.unpack_from("<HH", data, reloff + 4 * k)
                at = rseg * 16 + roff
                struct.pack_into("<H", image, at, (struct.unpack_from("<H", image, at)[0] + factor) & 0xFFFF)
            self.write(seg, 0, bytes(image))
            self.gfx_range = (seg * 16, seg * 16 + len(image) + 0x10000)
            self._valid_memo = {}
            self._carry(False, 0)

        def __init__(self, *a, **kw):
            self.dac = bytearray(768)
            self.vmode = 3
            self.dac_index = self.dac_sub = 0
            self.dac_read = self.dac_read_sub = 0
            self.armed = None
            self.on_create = None
            self.series = None
            self.bytes_per_ms = orig_side.BYTES_PER_MS
            self._script = ShotList([], self)
            super().__init__(*a, **kw)

        @property
        def script(self):
            return self._script

        @script.setter
        def script(self, value):
            self._script = ShotList(value, self)

        def save_machine(self):
            self._dac_kept = bytes(self.dac)
            return super().save_machine()

        def load_machine(self, keep):
            super().load_machine(keep)
            self.dac[:] = self._dac_kept

        # ---- the framebuffer and the DAC
        def dump(self, name: str):
            pixels = bytes(self.uc.mem_read(VGA_BASE, W * H))
            write_idx(self.out_dir / (name + ".idx"), pixels, self.dac)
            self.log.append("shot %s at %d ms" % (name, self.ms))

        def _on_port(self, uc, port, size, *rest):
            if len(rest) == 1 and port == 0x3C9:
                # the DAC read back (the original's palette fades read the colours they start from): the tester's ports answer 0
                v = self.dac[self.dac_read * 3 + self.dac_read_sub]
                self.dac_read_sub += 1
                if self.dac_read_sub == 3:
                    self.dac_read_sub, self.dac_read = 0, (self.dac_read + 1) & 255
                return v
            if len(rest) == 2 and port == 0x3C7:
                self.dac_read, self.dac_read_sub = rest[0] & 0xFF, 0
            if len(rest) == 2:
                value = rest[0] & 0xFF
                if port == 0x3C8:
                    self.dac_index, self.dac_sub = value, 0
                elif port == 0x3C9:
                    self.dac[self.dac_index * 3 + self.dac_sub] = value & 63
                    self.dac_sub += 1
                    if self.dac_sub == 3:
                        self.dac_sub, self.dac_index = 0, (self.dac_index + 1) & 255
            return super()._on_port(uc, port, size, *rest)

        def _on_int(self, uc, intno, _):
            if intno == 0x21 and self.reg("AX") == 0x4B03:
                return self._load_overlay()
            if intno == 0x21 and (self.reg("AX") >> 8) == 0x3C and self.on_create:
                # the saved game's file is created: the "Saving Game" box is up (R037+0x2ACE draws it before it writes)
                name, self.on_create = self.on_create, None
                self.dump(name)
            if intno == 0x10 and (self.reg("AX") >> 8) == 0x00:
                # set video mode: AL (bit 7: do not clear the video memory); mode 13h is 320x200 at A000:0000
                self.vmode = self.reg("AX") & 0xFF
                if not self.vmode & 0x80 and self.vmode & 0x7F == 0x13:
                    self.write(0xA000, 0, bytes(W * H))
                return
            if intno == 0x10 and (self.reg("AX") >> 8) == 0x0F:
                # get video mode: AL the mode, AH the columns, BH the page
                self.set_reg("AX", ((0x28 if self.vmode & 0x7F == 0x13 else 0x50) << 8) | self.vmode)
                self.set_reg("BX", self.reg("BX") & 0x00FF)
                return
            if intno == 0x10 and (self.reg("AX") >> 8) == 0x10:
                al, bx, cx, dx = self.reg("AX") & 0xFF, self.reg("BX"), self.reg("CX"), self.reg("DX")
                if al == 0x10:                                  # one register: BX, DH = red, CH = green, CL = blue
                    self.dac[(bx & 255) * 3:(bx & 255) * 3 + 3] = bytes([(dx >> 8) & 63, (cx >> 8) & 63, cx & 63])
                elif al == 0x12:                                # a block: BX first, CX count, ES:DX the RGB triples
                    data = self.read(self.reg("ES"), dx, cx * 3)
                    for k in range(cx):
                        i = (bx + k) & 255
                        self.dac[i * 3:i * 3 + 3] = bytes(v & 63 for v in data[k * 3:k * 3 + 3])
                self.log.append("palette INT 10h AX=%04X" % self.reg("AX"))
            return super()._on_int(uc, intno, _)

        def _on_block(self, uc, address, size, user):
            s = self.series
            if s is not None and (self.battle_passes if s[5] else self.count) >= s[4]:
                self.dump("%s_%02d" % (s[0], s[3]))
                s[3] += 1
                s[4] += s[2] if s[5] else s[2] * self.bytes_per_ms
                if s[3] >= s[1]:
                    self.series = None
            return super()._on_block(uc, address, size, user)

    return ShotSession


# ---- the C# side: series steps become shot / wait steps -------------------------------------------------------------------
def expand_series(args: list[str]) -> list[str]:
    out, pending = [], None
    for a in args:
        if a.startswith("series:"):
            name, n, spec = a[7:].split(",")
            pending = (name, int(n), spec)
            continue
        out.append(a)
        if pending and a.split(":")[0] in ("key", "click", "rclick", "mapclick"):
            name, n, spec = pending
            pending = None
            every = int(spec[1:]) if spec.startswith("p") else max(1, int(spec) // 16)
            for j in range(n):
                if j:
                    out.append("wait:%d" % every)
                out.append("shot:%s_%02d" % (name, j))
    return out


def run(script: str, out: str, save: str | None, headless: str | None):
    import lockstep
    import orig_side
    import subprocess
    outp = Path(out)
    outp.mkdir(parents=True, exist_ok=True)
    cls = make_session_class()
    cls.out_dir = outp / "orig"
    shutil_rm(outp / "orig")
    orig_side.Session = cls
    lockstep.Session = cls
    real = subprocess.run

    def patched(args, *a, **kw):
        if isinstance(args, list) and any(isinstance(x, str) and x.startswith("series:") for x in args):
            args = expand_series(args)
        return real(args, *a, **kw)
    subprocess.run = patched
    exe = ROOT / "game" / "DARKLAND" / "DARKLAND.EXE"
    argv = ["lockstep.py", str(exe), str(exe.parent), script, str(outp), "--live", "--no-snapshot", "--recordings", str(outp / "rec")]
    if save:
        argv += ["--save", save]
    if headless:
        argv += ["--headless", headless]
    wait_commit()
    sys.argv = argv
    lockstep.main()


def shutil_copy(a: Path, b: Path):
    import shutil
    shutil.copyfile(a, b)


def shutil_rm(p: Path):
    import shutil
    shutil.rmtree(p, ignore_errors=True)


# ---- composing -----------------------------------------------------------------------------------------------------------
def read_idx(path: Path):
    b = path.read_bytes()
    return b[:W * H], [tuple(b[W * H + i * 3:W * H + i * 3 + 3]) for i in range(256)]


_FONT = None


def font() -> bytes | None:
    global _FONT
    if _FONT is None:
        for p in (ROOT / "code" / "local" / "dosboxx_int10_font_16.b64", HERE.parent.parent / "local" / "dosboxx_int10_font_16.b64"):
            if p.is_file():
                _FONT = base64.b64decode(p.read_text().strip())
                break
        else:
            _FONT = b""
    return _FONT or None


def text_pixels(text: str, scale: int = 2):
    """Rows of booleans for the text in the 8x16 font, scale times larger."""
    f = font()
    cols = len(text) * 8 * scale
    rows = [[False] * cols for _ in range(16 * scale)]
    if not f:
        return rows
    for k, ch in enumerate(text):
        c = ord(ch) & 255
        for y in range(16):
            bits = f[c * 16 + y]
            for x in range(8):
                if bits & (0x80 >> x):
                    for dy in range(scale):
                        for dx in range(scale):
                            rows[y * scale + dy][(k * 8 + x) * scale + dx] = True
    return rows


def frame_rgb(left, right, labels, scale: int, step_text: str = "") -> tuple[int, int, list[bytes]]:
    """A composite frame as rows of RGB bytes: label bar, the two screens scaled, a gap."""
    gap, bar = 12, 40
    width = 2 * W * scale + gap
    height = bar + H * scale
    bg = bytes((24, 24, 28))
    rows = [bytearray(bg * width) for _ in range(height)]
    for side, (img, pal) in enumerate((left, right)):
        x0 = side * (W * scale + gap)
        rgbpal = [bytes(c) for c in pal]
        for y in range(H):
            line = bytearray()
            src = img[y * W:(y + 1) * W]
            for p in src:
                line += rgbpal[p] * scale
            for dy in range(scale):
                rows[bar + y * scale + dy][x0 * 3:(x0 + W * scale) * 3] = line
        t = labels[side]
        tp = text_pixels(t, 2)
        for y, trow in enumerate(tp):
            row = rows[4 + y]
            for x, on in enumerate(trow):
                if on:
                    row[(x0 + 6 + x) * 3:(x0 + 6 + x) * 3 + 3] = b"\xf0\xf0\xf0"
    if step_text:
        tp = text_pixels(step_text, 1)
        for y, trow in enumerate(tp):
            row = rows[bar - 18 + y]
            for x, on in enumerate(trow):
                if on:
                    row[(width - 8 * len(step_text) - 8 + x) * 3:(width - 8 * len(step_text) - 8 + x) * 3 + 3] = b"\x90\x90\x98"
    return width, height, [bytes(r) for r in rows]


def png_bytes(width: int, height: int, rows: list[bytes]) -> bytes:
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = b"".join(b"\0" + r for r in rows)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")


def quantize(colours: dict[tuple, int], limit: int = 255) -> list[tuple]:
    """Median cut over weighted unique colours."""
    boxes = [list(colours.items())]
    while len(boxes) < limit:
        boxes.sort(key=lambda b: -len(b))
        big = boxes[0]
        if len(big) < 2:
            break
        rng = [max(c[0][i] for c in big) - min(c[0][i] for c in big) for i in range(3)]
        ch = rng.index(max(rng))
        big.sort(key=lambda c: c[0][ch])
        total, acc, cut = sum(w for _, w in big), 0, 1
        for k, (_, w) in enumerate(big):
            acc += w
            if acc * 2 >= total:
                cut = max(1, min(len(big) - 1, k + 1))
                break
        boxes[0:1] = [big[:cut], big[cut:]]
    out = []
    for b in boxes:
        tw = sum(w for _, w in b) or 1
        out.append(tuple(round(sum(c[i] * w for c, w in b) / tw) for i in range(3)))
    return out


def lzw(indices: bytes, min_bits: int = 8) -> bytes:
    clear, end = 1 << min_bits, (1 << min_bits) + 1
    out, cur, nbits = bytearray(), 0, 0
    bits = min_bits + 1
    table = {bytes([i]): i for i in range(clear)}
    nxt = end + 1

    def emit(code):
        nonlocal cur, nbits
        cur |= code << nbits
        nbits += bits
        while nbits >= 8:
            out.append(cur & 255)
            cur >>= 8
            nbits -= 8
    emit(clear)
    w = b""
    for p in indices:
        wc = w + bytes([p])
        if wc in table:
            w = wc
        else:
            emit(table[w])
            table[wc] = nxt
            nxt += 1
            if nxt > (1 << bits) and bits < 12:
                bits += 1
            elif nxt >= 4096:
                emit(clear)
                table = {bytes([i]): i for i in range(clear)}
                nxt, bits = end + 1, min_bits + 1
            w = bytes([p])
    if w:
        emit(table[w])
    emit(end)
    if nbits:
        out.append(cur & 255)
    return bytes(out)


def gif_bytes(frames: list[tuple[int, int, list[bytes]]], delay_cs: int) -> bytes:
    width, height = frames[0][0], frames[0][1]
    counts: dict[tuple, int] = {}
    for _, _, rows in frames:
        for r in rows:
            for i in range(0, len(r), 3):
                c = (r[i], r[i + 1], r[i + 2])
                counts[c] = counts.get(c, 0) + 1
    pal = quantize(counts) if len(counts) > 255 else list(counts)
    lookup: dict[bytes, int] = {bytes(c): k for k, c in enumerate(pal)} if len(counts) <= 255 else {}

    def idx(key: bytes) -> int:
        v = lookup.get(key)
        if v is None:
            c = tuple(key)
            v = lookup[key] = min(range(len(pal)), key=lambda k: (pal[k][0] - c[0]) ** 2 + (pal[k][1] - c[1]) ** 2 + (pal[k][2] - c[2]) ** 2)
        return v
    pal = pal + [(0, 0, 0)] * (256 - len(pal))
    out = bytearray(b"GIF89a" + struct.pack("<HHBBB", width, height, 0xF7, 0, 0))
    for c in pal:
        out += bytes(c)
    out += b"\x21\xff\x0bNETSCAPE2.0\x03\x01\x00\x00\x00"
    for _, _, rows in frames:
        ind = bytearray()
        for r in rows:
            ind += bytes([idx(r[i:i + 3]) for i in range(0, len(r), 3)])
        out += b"\x21\xf9\x04\x00" + struct.pack("<H", delay_cs) + b"\x00\x00"
        out += b"\x2c" + struct.pack("<HHHHB", 0, 0, width, height, 0) + b"\x08"
        data = lzw(bytes(ind))
        for k in range(0, len(data), 255):
            blk = data[k:k + 255]
            out += bytes([len(blk)]) + blk
        out += b"\x00"
    return bytes(out + b"\x3b")


def compose(out: str, name: str, lab_o: str, lab_r: str, stem: str, scale: int = 3, delay_cs: int = 12):
    outp = Path(out)
    if "+" in name:
        # series+shot: the shot is appended to the series as its last frame (what the screen shows much later)
        name, _, extra = name.partition("+")
        k = 0
        while (outp / "orig" / ("%s_%02d.idx" % (name, k))).is_file():
            k += 1
        last = outp / "orig" / ("%s_%02d.idx" % (name, k - 1))
        if not (k and last.read_bytes() == (outp / "orig" / (extra + ".idx")).read_bytes() and
                (outp / "cs" / ("%s_%02d.idx" % (name, k - 1))).read_bytes() == (outp / "cs" / (extra + ".idx")).read_bytes()):
            for side in ("orig", "cs"):
                shutil_copy(outp / side / (extra + ".idx"), outp / side / ("%s_%02d.idx" % (name, k)))
    single = (outp / "orig" / (name + ".idx")).is_file()
    if single and not (outp / "cs" / (name + ".idx")).is_file():
        # a moment the C# host cannot stop at (a box that is up only while a file is written): of the frames it presented (rec:<n>),
        # the one whose picture is nearest to the original's
        o = read_idx(outp / "orig" / (name + ".idx"))
        orgb = [bytes(o[1][p]) for p in o[0]]
        best = None
        for f in sorted((outp / "cs").glob("rec*.idx")):
            c = read_idx(f)
            d = sum(1 for k, p in enumerate(c[0]) if bytes(c[1][p]) != orgb[k])
            if best is None or d < best[0]:
                best = (d, f)
        if best is None:
            raise SystemExit("no frame to pair with %s" % name)
        print("%s: the C# frame %s (%d pixels differ)" % (name, best[1].name, best[0]))
        shutil_copy(best[1], outp / "cs" / (name + ".idx"))
    if single:
        o, c = read_idx(outp / "orig" / (name + ".idx")), read_idx(outp / "cs" / (name + ".idx"))
        w, h, rows = frame_rgb(o, c, (lab_o, lab_r), scale)
        Path(stem + ".png").write_bytes(png_bytes(w, h, rows))
        return stem + ".png"
    frames = []
    k = 0
    while (outp / "orig" / ("%s_%02d.idx" % (name, k))).is_file() and (outp / "cs" / ("%s_%02d.idx" % (name, k))).is_file():
        o = read_idx(outp / "orig" / ("%s_%02d.idx" % (name, k)))
        c = read_idx(outp / "cs" / ("%s_%02d.idx" % (name, k)))
        frames.append(frame_rgb(o, c, (lab_o, lab_r), scale, "frame %d" % k))
        k += 1
    if not frames:
        raise SystemExit("no shots named %s in %s" % (name, out))
    Path(stem + ".gif").write_bytes(gif_bytes(frames, delay_cs))
    return stem + ".gif"


def find_save(name: str) -> str | None:
    if name == "-":
        return None
    for d in (ROOT / "game" / "DARKLAND" / "SAVES", ROOT / "scratch" / "vis10" / "saves", ROOT / "code" / "local" / "oracle-saves"):
        if (d / name).is_file():
            return str(d / name)
    raise SystemExit("saved game %s not found" % name)


def jobs(ids: list[str], headless: str, results: Path):
    """Runs the jobs of vis10-sessions/jobs.txt (one after the other: one oracle worker) and composes their pictures into results."""
    import subprocess
    table = [l.strip().split("|") for l in (HERE / "vis10-sessions" / "jobs.txt").read_text(encoding="utf-8").splitlines() if l.strip() and not l.startswith("#")]
    for jid, steps, save, composes in table:
        if ids and jid not in ids:
            continue
        out = results / "run" / jid
        if not (out / "report.json").is_file():
            wait_commit()
            cmd = [sys.executable, "-I", "-B", str(HERE / "vis10.py"), "run", str(HERE / "vis10-sessions" / steps), str(out), "--headless", headless]
            if find_save(save):
                cmd += ["--save", find_save(save)]
            print("run %s" % jid, flush=True)
            subprocess.run(cmd, check=False)
        for pair in composes.split(","):
            shot, _, stem = pair.partition("=")
            try:
                print(compose(str(out), shot, "Original", "Restoration", str(results / stem)), flush=True)
            except (SystemExit, OSError) as e:
                print("%s: %s not composed (%s)" % (jid, shot, e), flush=True)


ROWS = [
    ("D-ecc6f5", "Sneaking card keeps the previous screen",
     "When the sneaking card opens, the original leaves the previous screen (picture and texts) on show where the restoration draws the card's own picture and text at once."),
    ("D-d4f515", "Before the city walls keeps the previous screen",
     "When the 'before the city walls' card opens, the original leaves the previous screen on show where the restoration draws the card at once."),
    ("D-99119d", "Empty prison cell text",
     "In the empty prison cell the original draws the text without waiting and goes on to the battle, where the restoration shows the text and waits for a key."),
    ("D-af566f", "Barter screen after a character sheet",
     "Back on the barter screen after a character sheet, the original redraws the picture without setting its colours again; the restoration sets the picture's own colours."),
    ("D-56a675", "Loot screen palette",
     "On the loot screen the original redraws the picture without setting its colours again; the restoration sets the picture's own colours."),
    ("D-09fbc1", "Battle clouds",
     "In a battle the original sorts a cloud's edges in with the fighters by depth and clips the puffs to the cloud's box; the restoration draws the edges over everyone and clips to the view."),
    ("D-5de66d", "Card picture shown at the first text",
     "When a card opens, the original keeps the old screen until the card's first text and then shows picture and text together; the restoration shows the new picture at once."),
    ("D-b0c9a6", "Character creation: new member's row",
     "In character creation a new member's row in the party column shows the stock shield picture (file SHIELDA.PIC) in the original until H is pressed; the restoration shows the shield of the member's own record."),
    ("D-751050", "Load screen: button after 'no'",
     "On the load screen, after Alt-Q is answered 'no' and a button is pressed again, the original draws no pressed-button picture and the restoration still does."),
    ("D-b797f7", "Saving Game box in a battle",
     "The 'Saving Game' box of a save made in a battle is drawn in colour 9 by the original; the restoration draws it in the box's own colour."),
]


def index(results: Path, notes: dict):
    """results/index.html: every picture of results/<id>-*.png|gif under its row, with one plain sentence."""
    import html
    parts = ["<!doctype html><html><head><meta charset='utf-8'><title>Visual differences: original and restoration</title>",
             "<style>body{font-family:sans-serif;max-width:1000px;margin:2em auto;padding:0 1em;background:#fff;color:#111}"
             "img{max-width:100%;border:1px solid #888;margin:.3em 0}h2{margin-top:2em;border-top:1px solid #ccc;padding-top:.6em}"
             ".note{color:#444;font-size:.9em}code{background:#eee}</style></head><body>",
             "<h1>Original and restoration, side by side</h1>",
             "<p>Left: the original program run in the lockstep tester's emulator with its own graphics driver (MGRAPHIC.EXE) loaded, "
             "its screen read from the video memory and the colour registers. Right: the restoration's headless host at the same step "
             "of the same script. Both are enlarged three times.</p>",
             "<p class='note'>Read with care: the emulator is much slower than a real PC, so in a moving picture (GIF) the original's "
             "times are not real times, and the original's pictures can come later than they would on a real machine. "
             "Each picture is one step of a scripted session (tools/oracle/vis10-sessions); a card started from a saved game "
             "has the load screen as its 'previous screen'.</p>"]
    for rid, title, sentence in ROWS:
        files = sorted(f.name for f in results.iterdir() if f.name.startswith(rid + "-") and f.suffix in (".png", ".gif"))
        parts.append("<h2>%s: %s</h2><p>%s</p>" % (rid, html.escape(title), html.escape(sentence)))
        for f in files:
            parts.append("<div><img src='%s' alt='%s'><div class='note'>%s</div></div>" % (f, html.escape(f), html.escape(notes.get(f, ""))))
        if not files:
            parts.append("<p><b>No picture.</b> %s</p>" % html.escape(notes.get(rid, "not produced")))
        elif rid in notes:
            parts.append("<p class='note'>%s</p>" % html.escape(notes[rid]))
    parts.append("</body></html>")
    (results / "index.html").write_text(chr(10).join(parts), encoding="utf-8")
    return results / "index.html"


def main():
    a = sys.argv[1:]
    if not a:
        raise SystemExit(__doc__)
    if a[0] == "wait-commit":
        print("%.1f GB" % wait_commit())
    elif a[0] == "run":
        save = a[a.index("--save") + 1] if "--save" in a else None
        headless = a[a.index("--headless") + 1] if "--headless" in a else None
        run(a[1], a[2], save, headless)
    elif a[0] == "jobs":
        headless = a[a.index("--headless") + 1] if "--headless" in a else str(HERE.parent.parent / "src" / "Darklands.Host.Headless" / "bin" / "Release" / "net10.0" / "Darklands.Host.Headless.exe")
        jobs([x for x in a[1:] if not x.startswith("--") and x != headless], headless, ROOT / "scratch" / "vis10")
    elif a[0] == "index":
        import json
        notes = json.loads(Path(a[1]).read_text(encoding="utf-8")) if len(a) > 1 else {}
        print(index(ROOT / "scratch" / "vis10", notes))
    elif a[0] == "compose":
        scale = int(a[a.index("--scale") + 1]) if "--scale" in a else 3
        delay = int(a[a.index("--delay") + 1]) if "--delay" in a else 12
        print(compose(a[1], a[2], a[3], a[4], a[5], scale, delay))
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
