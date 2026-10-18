#!/usr/bin/env python3
"""Pixel comparison of a 320x200 frame with an original screenshot.

  pixdiff.py <ours.png> <reference.png> [<out prefix>] [x0 y0 x1 y1]

The reference may be the DOSBox(-X) capture at 640x400 (each game pixel a 2x2 block; it is reduced
by taking the top-left pixel of each block) or 320x200. Two pixels agree when the sum of their
channel differences is at most 12 (the 6-bit DAC widened to 8 bits differs by up to 3 per channel
between emulators). Prints the number of differing pixels (in the optional region, and in the
party panel / card window / card text area) and, with an output prefix, writes <prefix>_side.png:
ours | reference | differences in magenta, at twice the size. Needs Pillow and numpy.
"""
import sys

import numpy as np
from PIL import Image

TOLERANCE = 12
AREAS = {'panel': (0, 0, 60, 200), 'card': (60, 0, 320, 200), 'text': (72, 12, 310, 105)}


def load(path):
    a = np.array(Image.open(path).convert('RGB')).astype(int)
    if a.shape[:2] == (400, 640):
        a = a[0::2, 0::2]
    if a.shape[:2] != (200, 320):
        sys.exit(f'{path}: expected 320x200 or 640x400, got {a.shape[1]}x{a.shape[0]}')
    return a


def compare(ours_path, ref_path, out=None, region=(0, 0, 320, 200)):
    ours, ref = load(ours_path), load(ref_path)
    mask = np.abs(ours - ref).sum(axis=2) > TOLERANCE
    x0, y0, x1, y1 = region
    count = int(mask[y0:y1, x0:x1].sum())
    if out:
        vis = (ours // 4).astype(np.uint8)
        vis[mask] = (255, 0, 255)
        side = np.concatenate([ours.astype(np.uint8), ref.astype(np.uint8), vis], axis=1)
        Image.fromarray(side).resize((side.shape[1] * 2, side.shape[0] * 2), Image.NEAREST).save(out + '_side.png')
    return count, mask


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    out = sys.argv[3] if len(sys.argv) > 3 else None
    region = tuple(int(v) for v in sys.argv[4:8]) if len(sys.argv) >= 8 else (0, 0, 320, 200)
    count, mask = compare(sys.argv[1], sys.argv[2], out, region)
    print(f'{count} pixels differ in {region}')
    for name, (a, b, c, d) in AREAS.items():
        print(f'  {name:6s} {int(mask[b:d, a:c].sum())}')
    if count:
        ys, xs = np.nonzero(mask)
        print(f'  differences within x {xs.min()}..{xs.max()}, y {ys.min()}..{ys.max()}')


if __name__ == '__main__':
    main()
