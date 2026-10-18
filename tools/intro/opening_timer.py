"""The opening's timing against the original's shared-clock capture.

The capture (a DOSBox-X run of the original with the Roland driver and a Sound Blaster, recorded on
one clock: every MPU-401 data byte, every PAN frame shown, every sample's DMA start) is the JSON
given by its path (it is not part of this repository).
The trace is written by the headless host:

  Darklands.Host.Headless <game folder> <out> intro sound:config trace:<out>/intro.trace wait:9500

  compare <capture.json> <trace>   the MPU byte stream, the cel times and the sample starts, side by side
  derive  <capture.json> <trace> <out.txt>
                                   the timer track (ticks per frame) and the per-file tick counts that make
                                   the emulated timer follow the original's (src/Darklands.Presentation/
                                   Resources/OpeningTimer.txt, OpeningTiming.Files)

Times are compared in frames of the original's timer (14.268048 ms, the VGA's 70.086 Hz), from the first
cel of OPENING2. A sample starts in the Roland driver's tick about 44 ms after the tick began (131 700
instructions at DOSBox's 3000 per ms); the trace gives that delay with each transfer.
"""
import collections
import hashlib
import json
import math
import sys

CAPTURE_FRAME = 0.014268047714   # seconds per frame in the capture
FILES = ['OPENING2.PAN', 'OPENING3.PAN', 'OPENING4.PAN', 'OPENIN6Z.PAN', 'OPENING7.PAN', 'OPENING8.PAN', 'OPENING9.PAN']
BYTE_PHASE = 0.12                 # MPU bytes of a plain tick come this many frames before the frame times of the capture's cels


def load_capture(path):
    with open(path, encoding='utf-8') as fh:
        j = json.load(fh)
    return {
        'bytes': bytes.fromhex(j['mpu']['bytes_hex']),
        'byte_times': [x / 1e9 for x in j['mpu']['byte_offset_ns']],
        'frames': [x / 1e9 for x in j['pan']['frame_offset_ns']],
        'parts': j['pan']['scene_partitions'],
        'samples': [(c['dsp_dma_programmed_offset_ns'] / 1e9, int(c['selector_ax'], 16), c['sample_count'])
                    for c in j['sampled_audio']['descriptor_callbacks']],
        'tunes': [e['offset_ns'] / 1e9 for e in j['runtime_session']['key_events'] if e['label'] == 'selected_module_trampoline'],
        'sha_mpu': j.get('mpu', {}).get('sha256'),
    }


def load_trace(path):
    t = {'rate': 44100, 'ticks': {}, 'midi': [], 'samples': [], 'cels': [], 'cues': [], 'requests': []}
    with open(path, encoding='utf-8') as fh:
        for line in fh:
            p = line.split()
            if not p:
                continue
            kind = p[0]
            if kind == 'rate':
                t['rate'] = int(p[1])
            elif kind == 'tick':
                t['ticks'][int(p[1])] = int(p[2])
            elif kind == 'midi':
                t['midi'].append((int(p[1]), int(p[2]), p[3], int(p[4], 16)))
            elif kind == 'sample':
                t['samples'].append((int(p[1]), int(p[2]), p[3], int(p[4]), float(p[5])))
            elif kind == 'cel':
                t['cels'].append((int(p[1]), p[2], int(p[3])))
            elif kind == 'cue':
                t['cues'].append((int(p[1]), int(p[2], 16)))
            elif kind == 'request':
                t['requests'].append((int(p[1]), int(p[2]), int(p[3], 16)))
    return t


def aligned_midi(tr, cap):
    """The trace's MPU bytes from the capture's first byte on. A trace that begins after the driver's start-up bytes
    (the host attached it later) gets the capture's start-up bytes, without times, in front; offset: their count."""
    midi = tr['midi']
    ours = bytes(b for _, _, _, b in midi)
    if ours[:64] == cap['bytes'][:64]:
        return midi, 0
    offset = cap['bytes'].find(ours[:64])
    if offset < 0:
        return midi, 0
    return [(None, None, 's', b) for b in cap['bytes'][:offset]] + midi, offset


def histogram(values):
    return ', '.join(f'{k:+d}: {v}' for k, v in sorted(collections.Counter(values).items()))


def compare(capture_path, trace_path):
    cap = load_capture(capture_path)
    tr = load_trace(trace_path)
    rate = tr['rate']
    midi, offset = aligned_midi(tr, cap)
    ours = bytes(b for _, _, _, b in midi)
    n = len(cap['bytes'])
    same = ours[:n] == cap['bytes']
    first_diff = next((i for i in range(min(n, len(ours))) if ours[i] != cap['bytes'][i]), None)
    note = ' (the trace began after the start-up bytes of the driver: those are taken from the capture)' if offset else ''
    verdict = 'identical' if same else f'first difference at byte {first_diff}'
    print(f'MPU bytes: {len(ours)} sent{note}; the capture has {n}: {verdict}'
          f' (SHA-256 of the first {n}: {hashlib.sha256(ours[:n]).hexdigest().upper()[:16]}...)')
    if len(tr['cels']) != len(cap['frames']):
        print(f'cels: {len(tr["cels"])} shown, the capture has {len(cap["frames"])}')
    cel_frames = [tr['ticks'][tick] for tick, _, _ in tr['cels']]
    frame = 1 / rate / CAPTURE_FRAME              # our output frames in capture frames
    ours0 = cel_frames[0]
    cap0 = cap['frames'][0]
    cel_err = [round((f - ours0) * frame - (c - cap0) / CAPTURE_FRAME) for f, c in zip(cel_frames, cap['frames'])]
    worst_ms = max(abs((f - ours0) / rate - (c - cap0)) for f, c in zip(cel_frames, cap['frames'])) * 1000
    print(f'cels: {len(cel_frames)} times against the capture, in frames: {histogram(cel_err)}; largest difference {worst_ms:.1f} ms')
    first = 0
    for name, part in zip(FILES, cap['parts']):
        a, c = part['first_frame_index'], part['frame_count']
        d0 = (cel_frames[a] - ours0) / rate - (cap['frames'][a] - cap0)
        d1 = (cel_frames[a + c - 1] - ours0) / rate - (cap['frames'][a + c - 1] - cap0)
        print(f'  {name}: first cel {d0 * 1000:+.1f} ms, last cel {d1 * 1000:+.1f} ms')
        first += c
    # MPU byte times, from the first cue byte on (the start-up bytes come when the program starts)
    cue2f = next(f for tick, f, number in tr['requests'] if number == 0x2F)
    start = next(i for i, (_, f, ctx, _) in enumerate(midi) if f is not None and f >= cue2f and ctx == 'r')
    errs = []
    for i in range(max(start, 1168), min(n, len(ours))):
        f = midi[i][1]
        errs.append(round((f - ours0) * frame - ((cap['byte_times'][i] - cap0) / CAPTURE_FRAME + BYTE_PHASE)))
    within = sum(1 for e in errs if e == 0)
    print(f'MPU byte times (bytes {max(start, 1168)}..{min(n, len(ours)) - 1}), in frames: {histogram(errs)}'
          f' ({within / max(1, len(errs)):.1%} in the same frame)')
    starts = [(f / rate + delay, length) for tick, f, ctx, length, delay in tr['samples'] if ctx == 'f']
    print(f'sample starts: {len(starts)}, the capture has {len(cap["samples"])}')
    serr = []
    for (t, length), (ct, selector, count) in zip(starts, cap['samples']):
        d = (t - ours0 / rate) - (ct - cap0)
        serr.append(d)
        print(f'  selector {selector}: ours {t - ours0 / rate:8.3f} s, capture {ct - cap0:8.3f} s, {d * 1000:+6.1f} ms')
    if serr:
        print(f'sample starts: largest difference {max(abs(x) for x in serr) * 1000:.1f} ms, '
              f'{sum(1 for x in serr if abs(x) <= CAPTURE_FRAME)} of {len(serr)} within one frame')
    return 0 if same else 1


def derive(capture_path, trace_path, out_path):
    """Ticks per frame for the track: cels and MPU bytes of the capture put each emulated tick on a frame."""
    cap = load_capture(capture_path)
    tr = load_trace(trace_path)
    P = CAPTURE_FRAME
    g0 = cap['frames'][0]
    fw = [round((x - g0) / P) for x in cap['frames']]
    midi, _ = aligned_midi(tr, cap)
    ours = bytes(b for _, _, _, b in midi)
    assert ours[:len(cap['bytes'])] == cap['bytes'], 'the MPU bytes differ from the capture: fix the tunes\' ticks first'
    tune_ticks = {tick for tick, _, _ in tr['requests']}
    seen = collections.defaultdict(set)
    for i in range(1168, len(cap['bytes'])):
        tick, _, ctx, _ = midi[i]
        if ctx == 'f' and tick not in tune_ticks:
            seen[tick].add(round((cap['byte_times'][i] - g0) / P + BYTE_PHASE))
    known = {k: min(v) for k, v in seen.items() if len(v) == 1}
    cel = {tick: fw[i] for i, (tick, _, _) in enumerate(tr['cels'])}
    # a sample start ran into its tick by the delay of the trace
    starts = [(tick, delay) for tick, f, ctx, length, delay in tr['samples'] if ctx == 'f']
    for (tick, delay), (t, _, _) in zip(starts, cap['samples']):
        known.setdefault(tick, round((t - g0) / P + BYTE_PHASE - delay / P))
    for tick, w in cel.items():
        known[tick] = w
    changed = True
    while changed:
        changed = False
        ks = sorted(known)
        for a, b in zip(ks, ks[1:]):
            if known[b] < known[a]:
                victim = a if a not in cel else b
                if victim in cel:
                    raise SystemExit(f'cels at ticks {a} and {b} out of order')
                del known[victim]
                changed = True
                break
    tune = cap['tunes'][1]                       # the capture's call of tune 2Fh
    known[0] = min(math.floor((tune - g0) / P + BYTE_PHASE), min(w for k, w in known.items() if k > 0) - 1)
    last = max(cel)
    ks = sorted(k for k in known if k <= last)
    frame_of = {}
    for a, b in zip(ks, ks[1:]):
        for k in range(a, b):
            frame_of[k] = round(known[a] + (known[b] - known[a]) * (k - a) / (b - a))
    frame_of[last] = known[last]
    per = collections.Counter(frame_of[k] for k in range(1, last + 1))
    digits = ''.join(str(per.get(f, 0)) for f in range(frame_of[0] + 1, frame_of[last] + 1))
    with open(out_path, 'w', encoding='utf-8', newline='\n') as fh:
        for i in range(0, len(digits), 100):
            fh.write(digits[i:i + 100] + '\n')
    tunes = [tick for tick, _ in tr['cues']]
    firsts = [tr['cels'][p['first_frame_index']][0] for p in cap['parts']]
    lasts = [tr['cels'][p['first_frame_index'] + p['frame_count'] - 1][0] for p in cap['parts']]
    files = [(0 if i == 0 else tunes[i] - lasts[i - 1], firsts[i] - tunes[i]) for i in range(len(firsts))]
    print(f'{len(digits)} frames (from frame {frame_of[0] + 1} to {frame_of[last]}), {sum(map(int, digits))} ticks; {len(known)} ticks placed by the capture')
    print('OpeningTiming.Files (tune after the last cel, first cel after the tune):', files)
    return 0


if __name__ == '__main__':
    if len(sys.argv) >= 4 and sys.argv[1] == 'compare':
        sys.exit(compare(sys.argv[2], sys.argv[3]))
    if len(sys.argv) >= 5 and sys.argv[1] == 'derive':
        sys.exit(derive(sys.argv[2], sys.argv[3], sys.argv[4]))
    print(__doc__)
    sys.exit(2)
