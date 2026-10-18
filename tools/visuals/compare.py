#!/usr/bin/env python3
"""Plays the Quickstart opening in the headless host and compares each screen with original captures.

  compare.py <game folder> <reference folder> <output folder> [<headless exe>]

The reference folder holds DOSBox-X captures (640x400) of a Quickstart game that started in Goslar
(the headless seed 27 draws Goslar as the original's clock did for the capture), with the pointer where
the capture had it. Expected names (any missing one is skipped):

  navigator_batch_01.png   the tavern introduction (also quickstart_tavern_intro_ref.png)
  navigator_batch_02.png   "You look around at each other ..."
  navigator_batch_03.png   "Everyone nods ..."
  navigator_batch_04.png   "There is an unhappy look on Ebhard's face ..."
  navigator_batch_05.png   "After discussing these issues ..." before the fade (the option lines invisible)
  navigator_batch_06.png   the end of the fade, pointer hidden, before the option lines are recoloured
                           (also quickstart_departure_choice_ref.png)
  navigator_batch_07.png   the departure choice as it stays (also navigator_batch_08.png)

Writes <screen>_side.png (ours | original | differences) for each and prints the differing pixels.
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixdiff import compare  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_EXE = os.path.join(REPO, 'src', 'Darklands.Host.Headless', 'bin', 'Release', 'net10.0', 'Darklands.Host.Headless.exe')

SPACE = ['key:space', 'wait:10']
# (shot name, steps from the previous shot, reference file names)
SCREENS = [
    ('intro', ['seed:27', 'key:q', 'wait:10', 'move:160,68', 'wait:1'], ['navigator_batch_01.png', 'quickstart_tavern_intro_ref.png']),
    ('look', ['move:141,199'] + SPACE, ['navigator_batch_02.png']),
    ('nods', ['move:127,199'] + SPACE, ['navigator_batch_03.png']),
    ('ebhard', SPACE, ['navigator_batch_04.png']),
    ('discuss', SPACE, ['navigator_batch_05.png']),
    # the fade presents one frame per step: the tenth frame after the key shows its end (the original hides the pointer there)
    ('fade_end', ['move:330,210', 'key:space'] + ['shot:skip'] * 9, ['navigator_batch_06.png', 'quickstart_departure_choice_ref.png']),
    ('departure', ['move:127,199', 'wait:40'], ['navigator_batch_07.png', 'navigator_batch_08.png']),
]


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    game, refs, out = sys.argv[1:4]
    exe = sys.argv[4] if len(sys.argv) > 4 else DEFAULT_EXE
    os.makedirs(out, exist_ok=True)
    steps = []
    for name, before, _ in SCREENS:
        steps += before + [f'shot:{name}']
    subprocess.run([exe, game, out] + steps, check=True, stdout=subprocess.DEVNULL)
    total = 0
    for name, _, references in SCREENS:
        for ref in references:
            path = os.path.join(refs, ref)
            if not os.path.exists(path):
                continue
            count, _ = compare(os.path.join(out, name + '.png'), path, os.path.join(out, f'{name}__{os.path.splitext(ref)[0]}'))
            total += count
            print(f'{name:10s} vs {ref:36s} {count:6d} pixels differ')
    print(f'total {total}')


if __name__ == '__main__':
    main()
