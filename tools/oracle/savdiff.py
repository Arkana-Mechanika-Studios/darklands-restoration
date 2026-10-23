"""Saved-game differences by section, the form the oracle corpora store them in.

A .SAV (docs/SAVES.md) is cut into: the header after the two texts (+0x64..+0x189: the game's
variables, the party block), the character records (0x22A each), the events (0x30 each), the
places (0x3A each) and the cache file. A section whose record count is unchanged is stored as
{record index: {byte offset in the record: byte}} for the records that changed; a section whose
count changed is stored whole (a list of hex records). Empty sections are left out.
CardOracleTests (SaveSections) cuts the C#'s saved games the same way.
"""
from __future__ import annotations

import struct

TEXTS_END, HEAD_END = 0x64, 0x189
CHAR, EVENT, PLACE = 0x22A, 0x30, 0x3A


def sections(b: bytes) -> dict:
    n = struct.unpack_from("<H", b, 0xF1)[0]
    at = HEAD_END
    chars = [b[at + k * CHAR:at + (k + 1) * CHAR] for k in range(n)]
    at += n * CHAR
    m = struct.unpack_from("<H", b, at)[0]
    at += 2
    events = [b[at + k * EVENT:at + (k + 1) * EVENT] for k in range(m)]
    at += m * EVENT
    p = struct.unpack_from("<H", b, at)[0]
    at += 2
    places = [b[at + k * PLACE:at + (k + 1) * PLACE] for k in range(p)]
    at += p * PLACE
    return {"head": [b[TEXTS_END:HEAD_END]], "chars": chars, "events": events, "places": places, "cache": [b[at:]]}


def diff(start: bytes, final: bytes) -> dict:
    a, b = sections(start), sections(final)
    out = {}
    for name in ("head", "chars", "events", "places", "cache"):
        x, y = a[name], b[name]
        if len(x) != len(y) or any(len(p) != len(q) for p, q in zip(x, y)):
            out[name] = [r.hex().upper() for r in y]
            continue
        d = {}
        for k, (p, q) in enumerate(zip(x, y)):
            if p != q:
                d[str(k)] = {"%X" % i: "%02X" % q[i] for i in range(len(q)) if p[i] != q[i]}
        if d:
            out[name] = d
    return out


# the .BSV's blocks are imploded (PKWARE DCL); explode() reads one
LENLEN = (2, 35, 36, 53, 38, 23)
DISTLEN = (2, 20, 53, 230, 247, 151, 248)
LITLEN = (11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8, 9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24, 12, 11, 7, 9, 11, 12, 6, 7, 22, 5,
          7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12, 8, 12, 5, 38, 5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27,
          44, 253, 253, 253, 252, 252, 252, 13, 12, 45, 12, 45, 12, 61, 12, 45, 44, 173)
BASE = (3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264)
EXTRA = (0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8)


def huff(rep):
    length = []
    for r in rep:
        length += [r & 15] * ((r >> 4) + 1)
    count = [0] * 14
    for l in length:
        count[l] += 1
    offs = [0] * 15
    for l in range(1, 14):
        offs[l + 1] = offs[l] + count[l]
    sym = [0] * len(length)
    for s, l in enumerate(length):
        if l:
            sym[offs[l]] = s
            offs[l] += 1
    return count, sym


LEN, DIST, LIT = huff(LENLEN), huff(DISTLEN), huff(LITLEN)


def explode(data: bytes) -> bytes:
    """An imploded block (PKWARE DCL: the literal-mode byte, the dictionary bits, the bit stream) as its bytes."""
    pos, buf, cnt = 0, 0, 0

    def bits(n):
        nonlocal pos, buf, cnt
        while cnt < n:
            buf |= data[pos] << cnt
            pos += 1
            cnt += 8
        v = buf & ((1 << n) - 1)
        buf >>= n
        cnt -= n
        return v

    def decode(h):
        count, sym = h
        code = first = index = 0
        for l in range(1, 14):
            code |= bits(1) ^ 1
            c = count[l]
            if code - first < c:
                return sym[index + code - first]
            index += c
            first += c
            first <<= 1
            code <<= 1
        raise ValueError("bad code")

    lit, dictbits = data[0], data[1]
    pos = 2
    out = bytearray()
    while True:
        if bits(1):
            s = decode(LEN)
            n = BASE[s] + bits(EXTRA[s])
            if n == 519:
                break
            sh = 2 if n == 2 else dictbits
            d = (decode(DIST) << sh) + bits(sh) + 1
            for _ in range(n):
                out.append(out[-d])
        else:
            out.append(decode(LIT) if lit else bits(8))
    return bytes(out)


BSV_HEADER = 0x2A


def bsv_blocks(b: bytes) -> list[tuple[str, bytes]]:
    """A .BSV cut into its header and its imploded blocks, in the file's order (the writer R003+0x00DC): the variables
    (header word 0), each level (words 1..16, while non-zero), then the files DS:1164 (words 19, 20: raw, packed) and DS:1162 (17, 18)."""
    if len(b) < BSV_HEADER:
        return [("short", b)]
    w = struct.unpack_from("<21H", b, 0)
    out, at = [("header", b[:BSV_HEADER])], BSV_HEADER
    sizes = [("vars", w[0])] + [("level%d" % i, w[i]) for i in range(1, 17) if w[i]]
    sizes += [(n, p) for n, r, p in (("file1164", w[19], w[20]), ("file1162", w[17], w[18])) if r not in (0, 0xFFFF)]
    for name, n in sizes:
        out.append((name, b[at:at + n]))
        at += n
    if at < len(b):
        out.append(("tail", b[at:]))
    return out


# the bytes of a .enm record (0x122 each) the level loader R020+0x0000 writes; the others are what its buffer held (a known divergence,
# LevelEnemyFile): not compared. The texts at +0x80 (4) and +0x84 (0x17) are compared up to their zero.
ENM_RECORD = 0x122
ENM_WRITTEN = sorted({0x1E, 0x1F, 0x22, 0x24, 0x48, 0x49, 0x4B, 0x4C, 0x4F, 0x50, 0x51, 0x55, 0x58, 0x5A, 0x5B, 0x5C, 0x9B, 0x9C}
                     | set(range(0x5D, 0x7F)) | set(range(0x9E, ENM_RECORD)))
FLOOR = 0x33FC
BSV_CLOCK = 0x1877
BSV_POINTER = 0x15A3


def _enm_view(level: bytes) -> bytes:
    """A level block's bytes as compared: the floor image and the .atv data whole, of the .enm records only what the loader writes."""
    if len(level) <= FLOOR:
        return level
    at = FLOOR + 1 + 30 * level[FLOOR]
    if at >= len(level):
        return level
    out = bytearray(level[:at + 1])
    for k in range(level[at]):
        r = level[at + 1 + k * ENM_RECORD:at + 1 + (k + 1) * ENM_RECORD]
        if len(r) < ENM_RECORD:
            out += r
            break
        for o, n in ((0x80, 4), (0x84, 0x17)):
            t = r[o:o + n]
            out += t[:t.index(0) + 1] if 0 in t else t
        out += bytes(r[i] for i in ENM_WRITTEN)
    return bytes(out)


def bsv_diff(rewrite: bytes, original: bytes, given: bytes | None = None) -> dict:
    """The .BSV by block, each exploded: {block: {"sizes": [rewrite, original]} or {"first": offset, "count": bytes differing, ...}};
    "packed" when only the imploded bytes differ. given: the .BSV the session started from (its battle file, read by the load): a byte of
    the variables where the original still holds the given file's and the C# not (the per-combatant tables a battle made here does not
    take from the program's battle file: a known divergence, TacticalBattle.WriteBattleFile) is counted apart ("kept"), not a difference."""
    if rewrite == original:
        return {}
    a, b = dict(bsv_blocks(rewrite)), dict(bsv_blocks(original))
    g = dict(bsv_blocks(given)) if given else {}
    out = {}
    for name in list(dict.fromkeys(list(b) + list(a))):
        x, y = a.get(name, b""), b.get(name, b"")
        if x == y:
            continue
        if name not in ("header", "short", "tail"):
            try:
                x, y = explode(x), explode(y)
            except (IndexError, ValueError):
                pass
            if name.startswith("level"):
                x, y = _enm_view(x), _enm_view(y)
            if x == y:
                continue
        if name == "header":
            continue                    # the packed sizes: the blocks' own comparison says more
        if len(x) != len(y):
            out[name] = {"sizes": [len(x), len(y)]}
            continue
        k = [i for i in range(len(y)) if x[i] != y[i]]
        kept = []
        if name == "vars" and "vars" in g:
            z = explode(g["vars"])
            if len(z) == len(y):
                kept = [i for i in k if y[i] == z[i]]
                k = [i for i in k if y[i] != z[i]]
        if name == "vars" and len(y) > BSV_CLOCK + 1 and y[0] == 0 and set(k) & {BSV_CLOCK, BSV_CLOCK + 1}:
            # +0x1877, the combat clock DS:07D4, in a battle file saved outside a battle (+0 = 0): the original's timer counts it on
            # outside the battle (emulated time here), the C# keeps the last battle's or the last load's value: reported apart
            out.setdefault("kept", {})["clock"] = "%02X%02X/%02X%02X" % (x[BSV_CLOCK + 1], x[BSV_CLOCK], y[BSV_CLOCK + 1], y[BSV_CLOCK])
            k = [i for i in k if i not in (BSV_CLOCK, BSV_CLOCK + 1)]
        if name == "vars" and set(k) & {BSV_POINTER, BSV_POINTER + 1}:
            # +0x15A3, the pointer shape in use (DS:A68F): a segment of a DOS memory block (R000's commonsp.img buffer) plus an offset, the
            # machine's memory layout (the emulator's here); the C# keeps the file's (TacticalBattle.WriteBattleFile): reported apart
            out.setdefault("kept", {})["pointer"] = "%02X%02X/%02X%02X" % (x[BSV_POINTER + 1], x[BSV_POINTER], y[BSV_POINTER + 1], y[BSV_POINTER])
            k = [i for i in k if i not in (BSV_POINTER, BSV_POINTER + 1)]
        if k:
            out[name] = {"first": "%X" % k[0], "count": len(k), "at": {"%X" % i: "%02X/%02X" % (x[i], y[i]) for i in k[:40]}}
        if kept:
            out.setdefault("kept", {})[name] = len(kept)
    if not out:
        out["packed"] = True                # the same bytes, imploded otherwise
    return out
