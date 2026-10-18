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
