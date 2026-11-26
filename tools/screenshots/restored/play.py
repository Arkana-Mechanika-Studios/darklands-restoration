"""Plays the recorded paths of the Restored pages (docs/restored/) with the headless host and writes their screenshots, three times
the game's size, into docs/screenshots/restored/<page>/.

usage: python -I -B tools/screenshots/restored/play.py <game folder> <work folder> [<path file> ...]

<game folder> holds the English game (DARKLAND.EXE and its files). <work folder> receives each run's own output; it must not hold a
run of the same name (nothing is overwritten there). Without path files, every *.steps file beside this script is played. The page
is the path file's name up to the first dot (prison-rescue.bribe.steps: docs/screenshots/restored/prison-rescue/). A path whose
comments name a community save (fixtures/saves/... of the project folder, DARKLANDS_ROOT) gets that save copied into its saves
folder first, its names in capitals. The headless host is the
Release build (dotnet build src/Darklands.Host.Headless -c Release).
"""
import os
import re
import shutil
import struct
import subprocess
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
HOST = REPO / "src" / "Darklands.Host.Headless" / "bin" / "Release" / "net10.0" / "Darklands.Host.Headless.exe"
SHOTS = REPO / "docs" / "screenshots" / "restored"


def read_png(path):
    """The pixels of an 8-bit RGB or RGBA PNG (what the headless host writes), as rows of RGB bytes."""
    data = path.read_bytes()
    pos, idat, width, height, kind = 8, b"", 0, 0, 0
    while pos < len(data):
        length, name = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if name == b"IHDR":
            width, height, depth, kind = struct.unpack(">IIBB", body[:10])
            if depth != 8 or kind not in (2, 6):
                raise SystemExit(f"{path}: not an 8-bit RGB(A) PNG")
        elif name == b"IDAT":
            idat += body
        pos += 12 + length
    channels = 4 if kind == 6 else 3
    raw, stride, rows, prev = zlib.decompress(idat), width * channels, [], bytearray(width * channels)
    for y in range(height):
        f, line = raw[y * (stride + 1)], bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b, c = prev[x], prev[x - channels] if x >= channels else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        rows.append(bytes(line) if channels == 3 else b"".join(bytes(line[k:k + 3]) for k in range(0, stride, 4)))
    return width, height, rows


def write_png_scaled(path, image, n):
    """The image n times its size (each pixel an n x n square), as an RGB PNG."""
    width, height, rows = image
    out = []
    for row in rows:
        wide = b"".join(row[x * 3:x * 3 + 3] * n for x in range(width))
        out.extend([b"\x00" + wide] * n)

    def chunk(name, body):
        return struct.pack(">I", len(body)) + name + body + struct.pack(">I", zlib.crc32(name + body) & 0xFFFFFFFF)

    head = struct.pack(">IIBBBBB", width * n, height * n, 8, 2, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", head) + chunk(b"IDAT", zlib.compress(b"".join(out), 9)) + chunk(b"IEND", b""))


def play(game, work, steps_file):
    text = steps_file.read_text(encoding="utf-8")
    steps = [line.strip() for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")]
    run = work / steps_file.stem
    if run.exists():
        raise SystemExit(f"{run} exists: a run is never overwritten")
    saves = work / (steps_file.stem + "-saves")
    saves.mkdir(parents=True)
    for save in re.findall(r"fixtures/saves/([\w./-]+?)\.sav", text, re.IGNORECASE):
        source = Path(os.environ.get("DARKLANDS_ROOT", REPO.parent)) / "fixtures" / "saves" / save
        for found in source.parent.glob(source.name + ".*"):
            if found.suffix.lower() in (".sav", ".bsv"):
                shutil.copy(found, saves / found.name.upper())
    subprocess.run([str(HOST), str(game), str(run), "saves:" + str(saves), *steps], check=True)
    page = SHOTS / steps_file.name.split(".")[0]
    page.mkdir(parents=True, exist_ok=True)
    for shot in sorted(run.glob("*.png")):
        write_png_scaled(page / shot.name, read_png(shot), 3)
        print(page / shot.name)


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    game, work = Path(sys.argv[1]), Path(sys.argv[2])
    files = [Path(a) for a in sys.argv[3:]] or sorted(HERE.glob("*.steps"))
    for f in files:
        play(game, work, f)


if __name__ == "__main__":
    main()
