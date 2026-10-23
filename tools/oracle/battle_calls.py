"""Direct calls of single battle functions of the ORIGINAL from machine states taken inside whole fights (a tools addition: it
changes none of the original-side modules, so no recording expires).

usage:
  python -I -B battle_calls.py capture <out folder> --fights street,wild --at 100,300,600 [--recordings <folder>]
  python -I -B battle_calls.py export <jobs.json.gz> --states <states folder> <fuzz folder of one function> ...
  (the library, for battle_fuzz.py and the pilot drivers: State(path), State.call(coordinate, args, mutations))

capture   plays the fights of battle_oracle.FIGHTS in the DOS machine as battle_orig.run_original does (same steps, request, paused key,
          the battle-entry snapshot of the recordings folder; the Session class is swapped for a subclass that copies the whole
          machine at the head of the asked passes, R014+0x3166) and stores <out>/<fight>-p<pass>.state (gzip of a pickle:
          orig_side.Session.snapshot_state, local and trusted).
State     puts such a state into a fresh Session (the same stubs and hooks as any run), applies mutations (memory writes), calls
          one original function by its coordinate (a far one through a sentinel return, a near one with a return to its own entry that a
          hook ends, one of an overlay that is not resident through its thunk) and reports what it did: AX/DX, the DS and far-segment bytes
          that changed, the random generator's state and every draw's state before it, the coverage in original coordinates.
export    the kept inputs of battle_fuzz.py with the states they start from, as the jobs file the rewrite's test reads
          (tests/Darklands.Battles.Tests/BattleCalls).
"""
from __future__ import annotations

import argparse
import gzip
import json
import pickle
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import orig_side  # noqa: E402
from dosmachine import DS, P, REG, SENTINEL_SEG, linear, Stop  # noqa: E402
from orig_side import COMBATANTS, RECORD  # noqa: E402

HOLDRAND = 0x7B20
LOOT_FILE = "cfile.dat"                    # the loot file in the machine's file system
RAND_FN = (P(0x06A1), 0x29B6)          # root 06A1:29B6, the generator (rand)
FAR_SEG = 0x20A5                       # link-time segment of the level image, the paths and the loot piles
PACK_SEG = 0x2E38                      # link-time segment of the packs (party 003E + 0x180 k, enemies 0BDE + 0x60 k): the first PACK_SIZE bytes
PACK_SIZE = 0x1100
FAR2_VAR = 0x7D4A                    # DS word: the segment of the twin doors (+0xC00) and the doors opened (+0xBB6)
REPO = HERE.parent.parent
GAME = REPO.parent / "game" / "DARKLAND"       # as rerun_all.py: the game folder beside the code folder


def u32(b: bytes) -> int:
    return struct.unpack("<I", b)[0]


# ---------------------------------------------------------------------------------------------------- capture
class CaptureSession(orig_side.Session):
    """A Session that copies itself at the head of the battle passes asked for (taken from the pass line the fight's trace writes)."""
    SNAPSHOT_SKIP = orig_side.Session.SNAPSHOT_SKIP | {"_bt"}
    want: set = set()
    taken: dict = {}

    @property
    def battle_trace(self):
        return self._bt

    @battle_trace.setter
    def battle_trace(self, trace):
        self._bt = trace
        if trace is not None:
            inner = trace.append

            def append(line, _inner=inner, _self=self, _trace=trace):
                _inner(line)
                n = len(_trace)
                if n in CaptureSession.want and n not in CaptureSession.taken:
                    CaptureSession.taken[n] = _self.snapshot_state(("code",), {"pass": n, "line": line}, [])
                    if n >= max(CaptureSession.want):
                        _self.budget = 0
            trace.append = append


def capture(fights, passes_at, out: Path, game: str = str(GAME), recordings: str | None = None):
    import battle_oracle
    import battle_orig
    import recording
    exe = Path(game) / "DARKLAND.EXE"
    save = Path(game) / "SAVES" / "DKSAVE0.SAV"
    recordings = recordings or str(recording.default_folder())
    steps = battle_oracle.battle_steps(1500)
    rec_parts = recording.common_parts(exe, game, ("battle_orig",), save)
    snapshots = None
    if len(steps) > 3 and steps[-3][0] == "wait" and steps[-2] == ("key", "space") and steps[-1][0] == "wait":
        snapshots = recording.Snapshot(recordings, "fight", {**{k: rec_parts[k] for k in recording.COMMON_KEYS}, "prefix": steps[:-3],
                                                             "ahead": steps[-2], "coverage": True})
    out.mkdir(parents=True, exist_ok=True)
    orig_side.Session = CaptureSession
    seconds = {}
    for name in fights:
        request = battle_oracle.FIGHTS[name]
        CaptureSession.want, CaptureSession.taken = set(passes_at), {}
        t = time.time()
        battle_orig.run_original(exe, game, steps, save, request, max(passes_at), False, battle_oracle.PAUSED, snapshots)
        for n, state in CaptureSession.taken.items():
            with gzip.open(out / ("%s-p%d.state" % (name, n)), "wb", compresslevel=1) as f:
                pickle.dump({"fight": name, "pass": n, "state": state}, f, protocol=4)
        seconds[name] = round(time.time() - t, 1)
        print("captured", name, sorted(CaptureSession.taken), "%.0f s" % seconds[name], flush=True)
    orig_side.Session = CaptureSession.__mro__[1]
    return seconds


# ---------------------------------------------------------------------------------------------------- calls
class Catalog:
    """The functions of docs/source-map/original-functions.csv by coordinate: where they are (link-time far address, as the machine's
    segment in memory), whether they return near or far (the last return of the extent), the thunk that loads their overlay."""

    def __init__(self, exe: bytes):
        import csv
        import re
        import capstone
        from run_unit import Rtlink
        rt = Rtlink(exe)
        rows = {r["coordinate"]: r for r in csv.DictReader(open(HERE.parent.parent / "docs" / "source-map" / "original-functions.csv", encoding="utf-8"))}
        self.rt, self.exe, self.rows = rt, exe, rows
        self.thunk = {r["target"].split(" ")[0]: r["linked"] for r in rows.values() if r["kind"] == "thunk" and r["target"]}
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
        self._re = re

    def info(self, coord: str) -> dict:
        r = self.rows[coord]
        rec, pay, size = int(coord[1:4]), int(coord.split("+")[1], 16), int(r["size"])
        s = self.rt.sections[rec]
        img = self.exe[s["file_offset"]:s["file_offset"] + s["length"]]
        rets = {i.mnemonic for i in self.md.disasm(img[pay:pay + size], pay) if i.mnemonic in ("ret", "retf")}
        if len(rets) != 1:
            raise ValueError("%s returns %s" % (coord, rets))
        seg, off = (int(x, 16) for x in r["linked"].split(":"))
        th = self.thunk.get(coord)
        return {"coord": coord, "record": rec, "seg": P(seg), "off": off, "near": rets == {"ret"}, "size": size,
                "thunk": None if th is None else (P(int(th.split(":")[0], 16)), int(th.split(":")[1], 16))}


class State:
    """One captured machine state, reusable for any number of calls (each call starts from the stored bytes).
    near: coordinates of the near-returning functions that will be called (their entries get the hook that ends a call)."""

    def __init__(self, path: Path, game: str = str(GAME), coverage: bool = True, near=(), snap=None):
        if snap is None:
            with gzip.open(path, "rb") as f:
                d = pickle.load(f)
            self.fight, self.pass_no, snap = d["fight"], d["pass"], d["state"]
        else:
            self.fight, self.pass_no = "?", 0
        self.snap = snap
        self.snap["given"] = []
        exe = (Path(game) / "DARKLAND.EXE").read_bytes()
        self.m = orig_side.Session(exe, game, max_instructions=400_000_000, coverage=coverage)
        self.m.script = []
        self.cat = Catalog(exe)
        self.draws: list[int] = []
        self.m.hook(*RAND_FN, lambda m: self.draws.append(u32(m.read(DS, HOLDRAND, 4))))
        self.stop_at = None             # (linear entry, SP when the near function has returned)
        for coord in near:
            i = self.cat.info(coord)
            self.m.hook(i["seg"], i["off"], self._near_end, record="R%03d" % i["record"])
        self.restore()

    def _near_end(self, m):
        if self.stop_at is not None and m.reg("SP") == self.stop_at[1] and linear(m.reg("CS"), m.reg("IP")) == self.stop_at[0]:
            m.uc.emu_stop()
            m.pending = ("return",)

    def restore(self):
        m = self.m
        m.restore_state(self.snap)
        m.script = []
        m.cov_insn_rec, m.cov_edge_rec, m.cov_insns, m.cov_edges = {}, {}, {}, set()
        self.sp0 = m.reg("SP")
        self.stop_at = None
        self.base = bytes(m.uc.mem_read(DS * 16, 0x10000))
        self.far_base = bytes(m.uc.mem_read(P(FAR_SEG) * 16, 0x10000))
        self.far2_seg = m.word(DS, FAR2_VAR)          # the segment DS:7D4A names (the twin doors at +0xC00, the doors opened at +0xBB6)
        self.far2_base = bytes(m.uc.mem_read(self.far2_seg * 16, 0x10000))
        self.far3_base = bytes(m.uc.mem_read(P(PACK_SEG) * 16, PACK_SIZE))
        loot = m.vfs.files.get(LOOT_FILE)
        self.loot_base = None if loot is None else bytes(loot)           # the loot file (DS:1162 names it): the piles and chests keep offsets into it

    def image(self) -> dict:
        """The state's DS and far (level image, paths, loot piles) bytes as the jobs file keeps them."""
        import base64
        return {"fight": self.fight, "pass": self.pass_no, "ds": base64.b64encode(gzip.compress(self.base, 1)).decode(),
                "far": base64.b64encode(gzip.compress(self.far_base, 1)).decode(), "far2": base64.b64encode(gzip.compress(self.far2_base, 1)).decode(),
                "far3": base64.b64encode(gzip.compress(self.far3_base, 1)).decode(),
                "loot": "absent" if self.loot_base is None else base64.b64encode(gzip.compress(self.loot_base, 1)).decode(),
                "randomState": u32(self.base[HOLDRAND:HOLDRAND + 4])}

    # ---- memory helpers (DS offsets, or a segment in memory)
    def ds(self, off: int, n: int) -> bytes:
        return bytes(self.m.read(DS, off, n))

    def put(self, off: int, data: bytes, seg: int = DS):
        self.m.write(seg, off, data)

    def record(self, k: int) -> bytearray:
        return bytearray(self.ds(COMBATANTS + RECORD * k, RECORD))

    def invoke(self, seg: int, off: int, args, near: bool):
        """Run the function at seg:off (a far one through the sentinel, a near one with a return to its own entry that the hook ends)."""
        m = self.m
        saved = {n: m.uc.reg_read(r) for n, r in REG.items()}
        m.depth += 1
        try:
            ss = m.reg("SS")
            sp = (m.reg("SP") - 0x40) & 0xFFFF
            words = [a & 0xFFFF for a in args]
            sp -= 2 * len(words)
            m.write(ss, sp, struct.pack("<%dH" % len(words), *words))
            if near:
                sp -= 2
                m.write(ss, sp, struct.pack("<H", off))
                self.stop_at = (linear(seg, off), sp + 2)
            else:
                sp -= 4
                m.write(ss, sp, struct.pack("<HH", m.depth, SENTINEL_SEG))
            m.uc.reg_write(REG["SP"], sp)
            m.uc.reg_write(REG["DS"], DS)
            m.uc.reg_write(REG["ES"], DS)
            m.uc.reg_write(REG["CS"], seg)
            m.uc.reg_write(REG["IP"], off)
            m._drive()
            return m.reg("AX"), m.reg("DX")
        finally:
            m.depth -= 1
            for n, r in REG.items():
                m.uc.reg_write(r, saved[n])

    def call(self, coord: str, args=(), mutations=(), budget: int | None = None, keep_state: bool = False) -> dict:
        """Restore, mutate ((segment or None for DS, offset, bytes) items), call the function, report."""
        self.restore()
        m = self.m
        for seg_, o, data in mutations:
            if seg_ == "loot":
                m.vfs.files[LOOT_FILE] = bytearray(data)
                continue
            m.write(DS if seg_ is None else seg_, o, data)
        info = self.cat.info(coord)
        if not (info["record"] in m.resident or info["record"] == "root") and info["thunk"] is None:
            m.ensure(info["record"])
        if coord in KEYED and (args[0] & 0xFFFF) == 0:
            m.script = []                                      # no key waits: the held direction keys (DS:0880 / DS:0881) give the arrow
            m.waits, m.idle_since = 0, m.ms
            args = ()
        elif coord in KEYED:
            name = "#%04x" % (args[0] & 0xFFFF)
            orig_side.KEY_NAMES[name] = args[0] & 0xFFFF
            m.script = [("key", name)]
            m.waits, m.idle_since = orig_side.IDLE_POLLS, -orig_side.IDLE_MS         # the game has been idle long enough: the first look at the keyboard finds the key
            m.idle_mark = (m.read(DS, orig_side.HOLDRAND, 4), m.word(DS, orig_side.CARD))
            args = ()
        if coord in MOUSED:
            m.releases = [args[0] & 0xFFFF, 0]
            m.presses = [0, 0]
            m.write(*orig_side.KEYBOARD_FLAGS, bytes([4 if args[1] else 0]))
            m.mouse = [m.word(DS, orig_side.MOUSE_X), m.word(DS, orig_side.MOUSE_Y), m.byte(DS, orig_side.MOUSE_BUTTONS) & 1]
            args = ()
        before = bytes(m.uc.mem_read(DS * 16, 0x10000))
        far_before = bytes(m.uc.mem_read(P(FAR_SEG) * 16, 0x10000))
        far2_before = bytes(m.uc.mem_read(self.far2_seg * 16, 0x10000))
        far3_before = bytes(m.uc.mem_read(P(PACK_SEG) * 16, PACK_SIZE))
        self.draws.clear()
        m.count = 0
        m.budget = budget or 2_000_000          # a call that goes on for more is not a function call of the game (bytes executed / 3)
        error = None
        ax = dx = None
        t = time.time()
        try:
            if info["thunk"] is not None and info["record"] not in m.resident:
                ax, dx = self.invoke(*info["thunk"], args, False)
            else:
                ax, dx = self.invoke(info["seg"], info["off"], args, info["near"])
        except Stop as e:
            error = str(e)[:300]
        secs = time.time() - t
        after = bytes(m.uc.mem_read(DS * 16, 0x10000))
        lo = (self.sp0 - 0x1200) & 0xFFFF
        changed = {}
        for i in list(range(0, lo, 64)) + list(range(0xFE00, 0x10000, 64)):      # not the stack's window; the words above its top are a function's output buffer
            if before[i:i + 64] != after[i:i + 64]:
                for j in range(i, i + 64):
                    if before[j] != after[j]:
                        changed[j] = after[j]
        far_after = bytes(m.uc.mem_read(P(FAR_SEG) * 16, 0x10000))
        far_changed = {}
        if far_before != far_after:
            for i in range(0, 0x10000, 64):
                if far_before[i:i + 64] != far_after[i:i + 64]:
                    for j in range(i, i + 64):
                        if far_before[j] != far_after[j]:
                            far_changed[j] = far_after[j]
        far2_after = bytes(m.uc.mem_read(self.far2_seg * 16, 0x10000))
        far2_changed = {j: far2_after[j] for j in range(0x0B00, 0x0D00) if far2_before[j] != far2_after[j]}      # the part the rewrite models
        far3_after = bytes(m.uc.mem_read(P(PACK_SEG) * 16, PACK_SIZE))
        far3_changed = {j: far3_after[j] for j in range(PACK_SIZE) if far3_before[j] != far3_after[j]}
        loot = m.vfs.files.get(LOOT_FILE)
        out = {"ax": ax, "dx": dx, "error": error, "loot": "absent" if loot is None else bytes(loot).hex(), "changed": changed, "far_changed": far_changed, "far2_changed": far2_changed, "far3_changed": far3_changed, "random": u32(after[HOLDRAND:HOLDRAND + 4]), "draws": list(self.draws),
               "seconds": secs, "instructions": m.count, "coverage": m.coverage_result()}
        if keep_state:
            out["after"], out["before"] = after, before
        return out


MOUSED = {"R014+0xB576"}          # functions that read the mouse: the arguments are the left-button releases the driver counted and whether Ctrl is held
KEYED = {"R016+0x0000"}          # functions that read a key: the first argument is the key code (scan << 8 | character), given through the keyboard stubs

GAPS: dict = {}          # (the loot is carried now: loot_after_fight is judged)


def _runs(cat: "Catalog", coord: str, item: dict) -> bool:
    """True when the run executed an instruction inside the function (payload extent of the catalog)."""
    r = cat.rows[coord]
    rec, lo, size = "R%03d" % int(coord[1:4]), int(coord.split("+")[1], 16), int(r["size"])
    return any(lo <= p < lo + size for p in item["coverage"]["insns"].get(rec, []))


def export(fuzz_folders, states_folder: Path, out: Path):
    """The kept inputs of battle_fuzz runs and the states they were made from, as the jobs file the rewrite's test reads."""
    import battle_oracle
    cat = Catalog((GAME / "DARKLAND.EXE").read_bytes())
    states, jobs, fights = {}, [], {}
    for folder in fuzz_folders:
        for f in sorted(Path(folder).glob("*.json")):
            d = json.loads(f.read_text(encoding="utf-8"))
            name = d["state"]
            if name not in states:
                st = State(Path(states_folder) / (name + ".state"), coverage=False)
                states[name] = st.image()
                fights[st.fight] = battle_oracle.FIGHTS[st.fight]
            for item in d["kept"]:
                # functions the original ran that the rewrite's state does not carry (the enemies' belongings): the job is told, not judged
                gaps = [why for coord, why in GAPS.items() if _runs(cat, coord, item)]
                jobs.append({"gaps": gaps, "id": "%s|%s#%d" % (d["function"], d.get("label", name), item["iteration"]), "function": d["function"], "state": name,
                             "args": item["args"], "ops": item["ops"],
                             "orig": {k: item[k] for k in ("ax", "dx", "changed", "far_changed", "far2_changed", "far3_changed", "random", "draws", "loot") if k in item}})
    with gzip.open(out, "wt", encoding="utf-8", compresslevel=6) as f:
        json.dump({"fights": fights, "states": states, "jobs": jobs}, f)
    print("exported", len(jobs), "jobs,", len(states), "states ->", out)


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    e = sub.add_parser("export")
    e.add_argument("out")
    e.add_argument("--states", required=True)
    e.add_argument("folders", nargs="+")
    c = sub.add_parser("capture")
    c.add_argument("out")
    c.add_argument("--fights", required=True)
    c.add_argument("--at", required=True)
    c.add_argument("--game", default=str(GAME))
    c.add_argument("--recordings")
    a = ap.parse_args()
    if a.cmd == "export":
        export(a.folders, Path(a.states), Path(a.out))
    if a.cmd == "capture":
        capture(a.fights.split(","), [int(x) for x in a.at.split(",")], Path(a.out), a.game, a.recordings)


if __name__ == "__main__":
    main()
