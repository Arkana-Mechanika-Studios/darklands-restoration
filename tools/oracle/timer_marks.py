"""Where the original's timer callbacks land inside three short stretches of code that read the clock or the stack more than once (an
original-side module of the sessions run with lockstep.py --timer-marks; the others do not import it, so their recordings keep their keys
and the default run is unchanged).

  * the endurance regain R014+0x45AE reads the combat clock DS:07D4 anew for each member (R014+0x45CF): a callback inside its loop gives
    the later members a time one higher (gen-carry-11: member 0 at 2097, members 1..3 at 2098), and one that comes after the loop, before
    fight-input's head (R014+0x057B), leaves them all one below the head's clock (gen-carry-33: 3913 after the loot screen, head 3914).
    Logged, when one call's reads differ or the clock moved on after them, as "regain clocks c0,c1,c2,c3,c4 then c (battle pass n)" (-1 for
    a member the loop skips before its read: neither able nor unconscious; c the clock after the loop);
  * trigger-check R014+0x4442 sets the next activations time from the clock read at its end (R014+0x459E: DS:DA4F = DS:07D4 + 120): a
    callback during the check moves it (gen-carry-12: entered at 40756, DS:DA4F = 40877). Marked as the clock rise -8 among the phase
    marks (ClockTrace.phase_clocks, in their order with -7, -1 and -2), so clock_jumps lists it with the pass's jump;
  * fighter-step R014+0x1624 reads the clock anew for the member's next step time (R014+0x17CB: DS:D9CA[i] = delay + DS:07D4): a
    callback during the tick's actions moves it (gen-carry-12: member 1's 40802 against the C#'s 40801, the pass after the in-battle
    save). Marked as the clock rise -20 - i among the draws (ClockTrace.sched_clocks: in its place before the next draw);
  * the mine's pile filler R009+0x4D56 takes the stale word SS:[bp-0x0C] one past its list of kinds (R009+0x4EAE): a timer interrupt
    between the pile roll and the call pushes over it (gen-carry-01: 0x5E37 where no interrupt leaves 0). Logged as "pile word w" at each
    fill that reaches the list.

The hooks only read, but a hooked address is a block start where the tester's timer interrupt is not taken (dosmachine), so a run with
them may place its interrupts a few instructions apart from the default run's: what is recorded is that run's own, and the C# is given
exactly that run's landing points (lockstep.py: DARKLANDS_BATTLE_REGAIN_RISES, the -8 rises of DARKLANDS_BATTLE_CLOCK_JUMPS,
DARKLANDS_MINE_PILE_WORDS).
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orig_side  # noqa: E402
from dosmachine import DS, P  # noqa: E402

REGAIN_READ = (P(0x0E76), 0x45CF)       # endurance-regain: mov dx, [07D4] for the member at SI (DI = DS:DA55 + 2 * slot)
REGAIN_END = (P(0x0E76), 0x465A)        # its loop's end
REGAIN_BACK = (P(0x0E76), 0x057B)       # fight-main-loop after the regain's call, before the call of fight-input R014+0x3166
STEP_CLOCK = (P(0x0E76), 0x17CB)        # fighter-step: add ax, [07D4] for the next step time (the member: [bp+4])
TRIGGER_CLOCK = (P(0x0E76), 0x459E)     # trigger-check's end: mov ax, [07D4]; add ax, 0x78; mov [DA4F], ax
PILE_WORD = (P(0x182C), 0x0F0E)         # R009+0x4EAE: mov si, [bp-0Ch] (the stale word after the ten kinds)
SLOTS = 0xDA55


def session_class(base):
    """base (orig_side.Session or a subclass) with the marks' hooks."""

    class TimerMarkSession(base):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.regain_reads = self.regain_done = None
            self.hook(*REGAIN_READ, self._regain_read, record="R014")
            self.hook(*REGAIN_END, self._regain_end, record="R014")
            self.hook(*REGAIN_BACK, self._regain_back, record="R014")
            self.hook(*STEP_CLOCK, self._step_clock, record="R014")
            self.hook(*TRIGGER_CLOCK, self._trigger_clock, record="R014")
            self.hook(*PILE_WORD, self._pile_word, record="R009")

        def _regain_read(self, m):
            if self.regain_reads is None:
                self.regain_reads = [-1] * 5
            self.regain_reads[(m.reg("DI") - SLOTS) // 2] = m.word(DS, orig_side.FIGHT_CLOCK)

        def _regain_end(self, m):
            self.regain_done, self.regain_reads = self.regain_reads, None

        def _regain_back(self, m):
            reads, self.regain_done = self.regain_done, None
            if not reads:
                return
            now = m.word(DS, orig_side.FIGHT_CLOCK)
            seen = [c for c in reads if c >= 0]
            if seen and (len(set(seen)) > 1 or now != seen[-1]):
                m.log.append("regain clocks %s then %d (battle pass %d)" % (",".join(str(c) for c in reads), now, m.battle_passes))

        def _step_clock(self, m):
            t = m.battle_trace
            if isinstance(t, orig_side.ClockTrace):
                t.sched_clocks.append((-20 - m.word(m.reg("SS"), m.reg("BP") + 4), len(t.draw_clocks), m.word(DS, orig_side.FIGHT_CLOCK)))

        def _trigger_clock(self, m):
            t = m.battle_trace
            if isinstance(t, orig_side.ClockTrace):
                t.phase_clocks.append((-8, m.word(DS, orig_side.FIGHT_CLOCK)))

        def _pile_word(self, m):
            m.log.append("pile word %04X" % m.word(m.reg("SS"), m.reg("BP") - 0x0C))

    return TimerMarkSession
