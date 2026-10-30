"""The ORIGINAL side of the battle oracle (battle_oracle.py): the gate-battle session played in the DOS machine up to the battle's entry
(R002+0x0000, the main loop's call with 12 words), the fight's arguments put on the stack there, the party and the enemies fighting for
as many passes as asked, every pass's line (random state, 13 combatant records, queued damage, attack rates) taken at its head.

Everything here drives, hooks or feeds the emulated original, or decides what is taken from it; the comparison with the C# is in
battle_oracle.py. The recordings hash this module with orig_side.py, dosmachine.py and run_unit.py, not battle_oracle.py.
"""
from __future__ import annotations

import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import orig_side  # noqa: E402
from dosmachine import DS, P, Stop  # noqa: E402
from orig_side import COMBATANTS, COUNT, RECORD, stale_destinations  # noqa: E402,F401

BATTLE_ENTRY = (P(0x0E76), 0x0000)     # R002+0x0000, called by the main loop root 03A1:000A with 12 words
QUEUED, RATES = 0xD70C, 0xD493     # 2 bytes a combatant (endurance, strength); 1 byte a combatant
PAUSED = 297                        # passes the battle stays paused before its key: the C# host's pause under the script's wait:300 (298 passes at one clock value)

def run_original(exe: Path, game: str, steps, save: Path, request, passes: int, coverage: bool, paused: int = PAUSED, snapshots=None):
    """(trace lines, the battle's arguments as the card gave them, log, how it ended, set-up ticks, coverage)."""
    s = orig_side.Session(exe.read_bytes(), game, max_instructions=4_000_000_000, coverage=coverage)
    s.vfs.put("saves/dksave0.sav", save.read_bytes())
    if save.with_suffix(".BSV").exists():
        s.vfs.put("saves/dksave0.bsv", save.with_suffix(".BSV").read_bytes())
    s.script = list(steps)
    given = []

    snap = snapshots.load() if snapshots is not None else None
    taking = [snapshots is not None and snap is None]

    def entry(m):
        if taking[0]:
            # the snapshot of the run up to the battle's entry: the same for every fight (the request is put on the stack just below), the script
            # then holds the wait before the battle's key, the key and the wait after it; the block entered here began at this address, so entering it again redoes only what it did
            taking[0] = False
            if len(m.script) == 3 and m.script[0][0] == "wait" and m.script[1] == ("key", "space") and m._block is not None and m._block[0] == orig_side.linear(*BATTLE_ENTRY):
                snapshots.store(m.snapshot_state(("code",), {"seeds": list(seeds), "stale": list(stale), "given": list(given), "given_keys": list(given_keys),
                                                             "trace": orig_side.trace_state(s.battle_trace)}, list(steps)))
        given.append([m.sarg(k) for k in range(12)])
        if request is not None:
            for k, v in enumerate(request):
                m.put_word(m.reg("SS"), m.reg("SP") + 4 + 2 * k, v & 0xFFFF)
        m.log.append("battle %s -> %s at %d ms" % (given[-1], request, m.ms))
    s.hook(*BATTLE_ENTRY, entry, record="R002")

    # The one-section mine arm (R009+0x01E0, 1432:01E0) reads its rotation from a stack word it never sets: the word R009+0x3FA2
    # (182C:0002) left in the frame (the second wall-tile draw, 1..3). An interrupt that lands between that return and the arm (the
    # emulator's timing, about one battle in 10^5 on a real machine) pushes a register over the word and turns the mine another way.
    # The word is kept at the return and put back at the arm's entry, so the original runs as if no interrupt hit the window.
    stale = []
    s.hook(P(0x1432), 0x0087, lambda m: stale.append(m.word(m.reg("SS"), (m.reg("SP") - 8) & 0xFFFF)), record="R009")
    s.hook(P(0x1432), 0x01E5, lambda m: stale and m.put_word(m.reg("SS"), (m.reg("BP") - 2) & 0xFFFF, stale[-1]), record="R009")

    class Trace(list):
        draw_clocks: list = []      # the combat clock at each random draw (rand, root 06A1:29B6)
        draws_at: list = []         # the draws made before each pass head
        phase_clocks: list = []     # (-1 or -2, combat clock) where a tick's scheduler (1DB7:0D36) or melee exchanges (0E76:2A4A) begin
        phases_at: list = []        # the phase marks made before each pass head

        def append(self, line):
            self.draws_at.append(len(self.draw_clocks))
            self.phases_at.append(len(self.phase_clocks))
            records = s.read(DS, COMBATANTS, COUNT * RECORD)
            super().append("%s cbt %s q %s ar %s" % (line, records.hex().upper(), s.read(DS, QUEUED, 2 * COUNT).hex().upper(),
                                                    s.read(DS, RATES, COUNT).hex().upper()))
            if len(self) >= passes:
                s.budget = 0            # the next block stops the run (Stop: instruction budget)
    s.battle_trace = Trace()
    s.battle_trace.draw_clocks, s.battle_trace.draws_at = [], []
    s.battle_trace.phase_clocks, s.battle_trace.phases_at = [], []
    s.hook(P(0x1DB7), 0x0D36, lambda m: s.battle_trace.phase_clocks.append((-1, m.word(DS, 0x07D4))), record="R014")
    s.hook(P(0x0E76), 0x2A4A, lambda m: s.battle_trace.phase_clocks.append((-2, m.word(DS, 0x07D4))), record="R014")
    s.hook(P(0x06A1), 0x29B6, lambda m: s.battle_trace.draw_clocks.append(m.word(DS, 0x07D4)))
    # the battle's pause (after its first ticks) waits for Space; lockstep's idle test (no random draw for IDLE_MS) never passes on a
    # battlefield whose animated pieces draw at every frame: in a battle the key is given once the clock has stood still PAUSED passes
    idle_test = s._next_key
    given_keys = []

    def next_key():
        t = s.battle_trace
        while len(t) > paused and s.script and s.script[0][0] in ("wait", "shot"):
            s.script.pop(0)             # the C#'s steps
        if (len(t) > paused and s.script and s.script[0][0] == "key"
                and len({line.split()[2] for line in t[-paused:]}) == 1):
            # peeked as key-waiting-test does; read-key takes it from the script
            if not given_keys or given_keys[-1] != len(t):
                given_keys.append(len(t))
                s.log.append("%s due in the battle's pause at pass %d" % (s.script[0][1], len(t)))
            return orig_side.key_code(s.script[0][1])
        return idle_test()
    s._next_key = next_key
    seeds = []

    def srand(m):
        ret = orig_side.linear(m.word(m.reg("SS"), m.reg("SP") + 2), m.word(m.reg("SS"), m.reg("SP")))
        if bytes(m.uc.mem_read(ret - 14, 5)) == orig_side.SRAND_CLOCK_CALL:
            seeds.append(m.arg(0))
    s.hook(*orig_side.SRAND, srand)
    # sound: every sound-start(n) and music_play(x) in order, for battle_oracle.py's comparison with the C# host's requests (patch 06; orig_side.run_original has the same)
    s.log.append("sound log on")
    s.hook(*orig_side.SOUND_START, lambda m: m.log.append("sound %d at %d ms in %s" % (m.sarg(0), m.ms, " < ".join(m.call_stack[-2:]))))
    s.hook(*orig_side.MUSIC_PLAY, lambda m: m.log.append("music %d at %d ms in %s" % (m.sarg(0), m.ms, " < ".join(m.call_stack[-2:]))), record="R043")
    if snap is not None:
        s.restore_state(snap)
        x = snap["extra"]
        seeds[:], stale[:], given[:], given_keys[:] = x["seeds"], x["stale"], x["given"], x["given_keys"]
        orig_side.set_trace_state(s.battle_trace, x["trace"])
    try:
        if snap is not None:
            s.resume_state(snap)
        else:
            s.start()
        end = "the program ended"
    except Stop as e:
        end = str(e)[:300]
    cov = s.coverage_result() if coverage else None
    return s.battle_trace, given, seeds, [l for l in s.log if not l.startswith("INT")], end, s.set_up_ticks, cov


def record_fight(exe: Path, game: str, steps, save: Path, request, passes: int, coverage: bool, paused: int, snapshots=None) -> dict:
    """The live run of a fight, as the payload the recording stores and the comparison reads."""
    t = time.time()
    trace, given, seeds, log, end, set_up, cov = run_original(exe, game, steps, save, request, passes, coverage, paused, snapshots)
    return {"lines": list(trace), "draw_clocks": trace.draw_clocks, "draws_at": trace.draws_at, "phase_clocks": trace.phase_clocks,
            "phases_at": trace.phases_at, "given": given, "seeds": seeds, "log": log, "end": end, "set_up": set_up, "cov": cov,
            "seconds": time.time() - t}
