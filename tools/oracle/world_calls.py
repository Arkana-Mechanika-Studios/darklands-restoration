"""Direct calls of single functions of the ORIGINAL outside battle (travel, rules, cards, the resident code) from machine states taken inside
whole lockstep sessions (a tools addition: it changes none of the original-side modules, so no recording expires).

usage:
  python -I -B world_calls.py capture <out folder> --sessions pressburg-map,mapsave [--recordings <folder>] [--only loaded,map]
  python -I -B world_calls.py export <jobs.json.gz> --states <states folder> <fuzz folder of one function> ...
  (the library, for world_fuzz.py: WState(path), WState.call(coordinate, args, mutations))

capture   plays a session of lockstep-sessions/ as lockstep.py does (same steps, saved game, the start-point snapshot of the recordings folder
          when there is one) with a Session subclass that, at every `state:<name>` step, also copies the whole machine and keeps the saved
          game the original's writer made there: <out>/<session>-<name>.state (gzip of a pickle, the layout battle_calls.State reads, plus
          "sav": the writer's bytes, "random", "card"). The first idle moment after the saved game is loaded is the session's `loaded` state.
WState    battle_calls.State for these states, plus: roots (`root 0000:0DD2`) by coordinate, the clock seeds the function seeds with, and the
          saved game the original's own writer (R037+0x2ACE) makes of the machine before and after the call (on a copy that is thrown away):
          the rewrite is asked to make the same call on the game loaded from the "before" bytes and its saved game is compared with "after".
export    the kept inputs of world_fuzz.py with their states as the jobs file the rewrite's test reads (tests/Darklands.WorldCalls.Tests).
"""
from __future__ import annotations

import argparse
import base64
import copy
import gzip
import json
import os
import pickle
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import battle_calls as bc  # noqa: E402
import orig_side  # noqa: E402
from dosmachine import DS, P, REG, SENTINEL_SEG, Stop, linear  # noqa: E402

REPO = HERE.parent.parent
GAME = bc.GAME
SRAND = orig_side.SRAND
SAV_WRITE = orig_side.SAV_WRITE
HOLDRAND = orig_side.HOLDRAND


# ---------------------------------------------------------------------------------------------------- capture
class WorldCapture(orig_side.Session):
    """A Session that copies the machine at every state:<name> step (after the original's own saved-game writer ran on a copy)."""
    SNAPSHOT_SKIP = orig_side.Session.SNAPSHOT_SKIP
    taken: dict = {}
    only: set | None = None

    def capture(self, name: str):
        super().capture(name)
        sav, rnd, card = self.states[name]
        key, n = name, 1
        while key in WorldCapture.taken:
            n += 1
            key = "%s_%d" % (name, n)
        if WorldCapture.only is not None and name not in WorldCapture.only:
            return
        WorldCapture.taken[key] = {"snap": self.snapshot_state(("capture",), {"name": name}, []), "sav": sav, "random": rnd, "card": card,
                                   "ms": self.ms, "stack": list(self.call_stack[-4:])}


def capture(session: str, out: Path, game: str = str(GAME), recordings: str | None = None, only=None):
    import lockstep
    import recording
    import rerun_all
    exe = Path(game) / "DARKLAND.EXE"
    steps = lockstep.read_script(HERE / "lockstep-sessions" / (session + ".steps"))
    name = rerun_all.SESSIONS[session]
    save = rerun_all.resolve_save(name, Path(game) / "SAVES") if name else None
    recordings = recordings or str(recording.default_folder())
    parts = {"kind": "session", "steps": steps, "budget": 2_000_000_000, "battle_trace": session in rerun_all.BATTLE_RECORDS,
             "battle_records": session in rerun_all.BATTLE_RECORDS, "no_wall_draws": False, **recording.common_parts(exe, game, ("orig_side",), save)}
    sparts, n = orig_side.session_start(steps)
    ahead = next(((k, v) for k, v in list(steps)[n:] if k in ("key", "move", "click", "option")), None)
    snapshots = recording.Snapshot(recordings, "session", {**{k: parts[k] for k in recording.COMMON_KEYS}, **sparts, "ahead": ahead, "coverage": True})
    out.mkdir(parents=True, exist_ok=True)
    orig_side.Session = WorldCapture
    WorldCapture.taken, WorldCapture.only = {}, only
    t = time.time()
    try:
        orig_side.record_session(exe, game, steps, save, 2_000_000_000, session in rerun_all.BATTLE_RECORDS, False, True, snapshots)
    finally:
        orig_side.Session = WorldCapture.__mro__[1]
    for key, d in WorldCapture.taken.items():
        with gzip.open(out / ("%s-%s.state" % (session, key)), "wb", compresslevel=1) as f:
            pickle.dump({"fight": "%s:%s" % (session, key), "pass": 0, "state": d["snap"], "sav": d["sav"], "random": d["random"], "card": d["card"],
                         "stack": d["stack"], "ms": d["ms"]}, f, protocol=4)
    print("captured", session, sorted(WorldCapture.taken), "%.0f s" % (time.time() - t), flush=True)


# ---------------------------------------------------------------------------------------------------- memory
def private_mb() -> float:
    """Private bytes of this process in MB (0 where it cannot be read)."""
    try:
        import ctypes
        import ctypes.wintypes as W

        class PMC(ctypes.Structure):
            _fields_ = [("cb", W.DWORD), ("pf", W.DWORD), ("peak", ctypes.c_size_t), ("ws", ctypes.c_size_t), ("a", ctypes.c_size_t), ("b", ctypes.c_size_t),
                        ("c", ctypes.c_size_t), ("d", ctypes.c_size_t), ("priv", ctypes.c_size_t), ("peakpf", ctypes.c_size_t)]
        k, ps = ctypes.WinDLL("kernel32"), ctypes.WinDLL("psapi")
        k.GetCurrentProcess.restype = ctypes.c_void_p
        ps.GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.c_void_p, W.DWORD]
        p = PMC()
        p.cb = ctypes.sizeof(p)
        ps.GetProcessMemoryInfo(k.GetCurrentProcess(), ctypes.byref(p), p.cb)
        return p.priv / 1e6
    except Exception:
        return 0.0


# The original's loader and writer (m.call of R037) leak native memory in the emulator (about 0.6 MB a call: the translated blocks it drops when
# the code cache is flushed at every restore are not given back); a machine built afresh gives it all back. So the machine is rebuilt every
# REBUILD_EVERY calls, and a process whose private memory still passes RSS_CAP_MB aborts (exit 3) instead of eating the computer.
REBUILD_EVERY = int(os.environ.get("WORLD_REBUILD_EVERY", "20"))
RSS_CAP_MB = float(os.environ.get("WORLD_RSS_CAP_MB", "1500"))


# ---------------------------------------------------------------------------------------------------- calls
class Catalog2(bc.Catalog):
    """battle_calls.Catalog plus the resident root's functions (`root 0000:0DD2`)."""

    FAR = {"R039+0x1712"}           # functions whose extent holds a jump table: the scan finds no return

    def info(self, coord: str) -> dict:
        if coord in self.FAR:
            r = self.rows[coord]
            seg, off = (int(x, 16) for x in r["linked"].split(":"))
            th = self.thunk.get(coord)
            return {"coord": coord, "record": int(coord[1:4]), "seg": P(seg), "off": off, "near": False, "size": int(r["size"]),
                    "thunk": None if th is None else (P(int(th.split(":")[0], 16)), int(th.split(":")[1], 16))}
        if not coord.startswith("root "):
            return super().info(coord)
        r = self.rows[coord]
        seg, off = (int(x, 16) for x in r["linked"].split(":"))
        base = linear(P(seg), off) - orig_side.BASE
        rets = {i.mnemonic for i in self.md.disasm(bytes(self._root_image()[base:base + int(r["size"])]), 0) if i.mnemonic in ("ret", "retf")}
        if len(rets) != 1:
            raise ValueError("%s returns %s" % (coord, rets))
        return {"coord": coord, "record": "root", "seg": P(seg), "off": off, "near": rets == {"ret"}, "size": int(r["size"]), "thunk": None}

    _root = None

    def _root_image(self):
        if Catalog2._root is None:
            import dosmachine
            Catalog2._root = dosmachine.Machine(self.exe, str(GAME)).root
        return Catalog2._root


class WState(bc.State):
    """A captured state with the extra reports of world calls (see the module's docstring)."""

    def __init__(self, path: Path, game: str = str(GAME), coverage: bool = True, near=(), snap=None):
        self._init_args = (path, game, coverage, near)
        self.calls_since_build = 0
        with gzip.open(path, "rb") as f:
            d = pickle.load(f)
        self.name = Path(path).name[:-len(".state")]
        self.sav, self.card0, self.random0 = d["sav"], d["card"], d["random"]
        self.snap = d["state"]
        # (a copy of State.__init__ with the extended Catalog)
        self.fight, self.pass_no = d["fight"], d["pass"]
        self.snap["given"] = []
        exe = (Path(game) / "DARKLAND.EXE").read_bytes()
        self.m = orig_side.Session(exe, game, max_instructions=400_000_000, coverage=coverage)
        self.m.script = []
        self.cat = Catalog2(exe)
        self.draws = []
        self.seeds = []
        self.m.hook(*bc.RAND_FN, lambda m: self.draws.append(bc.u32(m.read(DS, bc.HOLDRAND, 4))))
        self.m.hook(*SRAND, self._srand)
        self.stop_at = None
        self.in_writer = False
        # the saved-game writer also writes the battle file (R003+0x00DC, 31 million bytes of code): not part of what is compared (the .SAV is)
        self.m.stub(P(0x0E76), 0x00DC, lambda m: (0, 0) if self.in_writer else None, "battle_save", record="R003")
        self.in_load = False
        # the load redraws the menu bar after it sets the difficulty (root 0000:1438): drawing, 50 million bytes of code, not part of the state
        self.m.stub(P(0x0000), 0x1438, lambda m: (0, 0) if self.in_load else None, "set_difficulty_menu", record="root")
        self.m.stub(P(0x0E76), 0x0CE8, lambda m: (0, 0) if self.in_load else None, "battle_restore", record="R003")
        for coord in near:
            i = self.cat.info(coord)
            self.m.hook(i["seg"], i["off"], self._near_end, record=i["record"] if i["record"] == "root" else "R%03d" % i["record"])
        self.restore()

    def rebuild(self):
        """A fresh machine after the emulator itself failed (an access violation leaves the engine unusable)."""
        import gc
        self.m = None
        gc.collect()
        self.__init__(*self._init_args)
        gc.collect()

    def _srand(self, m):
        ret = linear(m.word(m.reg("SS"), m.reg("SP") + 2), m.word(m.reg("SS"), m.reg("SP")))
        clock = bytes(m.uc.mem_read(ret - 14, 5)) == orig_side.SRAND_CLOCK_CALL
        self.seeds.append((m.arg(0), int(clock)))

    def writer(self) -> bytes:
        """The saved game the original's own writer makes of the machine now (on a copy that is put back)."""
        m = self.m
        keep = m.save_machine()
        used = (m.count, m.budget, m.coverage)
        m.count, m.budget, m.coverage = 0, 200_000_000, False          # set-up, not the call under test: no coverage
        self.in_writer = True
        try:
            for k in [k for k in m.vfs.files if k.startswith("saves") and "dksave" in k]:
                del m.vfs.files[k]
            m.ensure(37)
            m.call(*SAV_WRITE, [m.push_string("Darklands")])
            names = [k for k in m.vfs.files if k.startswith("saves") and "dksave" in k and k.endswith(".sav")]
            return bytes(m.vfs.files[names[0]])
        finally:
            self.in_writer = False
            m.load_machine(keep)
            m.count, m.budget, m.coverage = used
            m._last_block_jcc = None

    def load(self, sav: bytes):
        """The original's own loader (R037+0x8ED4) on a saved game: the machine then holds what the game holds after loading those bytes.
        Its battle file and the menu-bar redraw are left out (the .SAV is what is compared)."""
        m = self.m
        m.vfs.put(r"saves\dksave1.sav", sav)
        m.vfs.files.pop(r"saves\dksave1.bsv", None)
        m.vfs.deleted.add(r"saves\dksave1.bsv")
        used = (m.count, m.budget, m.coverage)
        m.count, m.budget, m.coverage = 0, 100_000_000, False
        self.in_load = True
        try:
            m.ensure(37)
            m.call(P(0x1C7D), 0x0D24, [m.push_string("DKSAVE1.SAV"), DS])
        finally:
            self.in_load = False
            m.count, m.budget, m.coverage = used
            m._last_block_jcc = None

    def call(self, coord: str, args=(), sav_edit: bytes | None = None, ds_edits=(), ctx=(), budget=None, keep_state=False, saves=True) -> dict:
        """Restore; `sav_edit` (a whole edited saved game) is loaded by the original; call the function.
        out["sav_before"]: the saved game of the machine just before the call (the edited bytes as the original's writer holds them),
        out["sav_after"]: after it, out["random0"]: the generator's state before the call, out["seeds"]: the srand calls."""
        self.calls_since_build += 1
        if self.calls_since_build > REBUILD_EVERY:
            self.rebuild()
        if RSS_CAP_MB and private_mb() > RSS_CAP_MB:
            print("ABORT: private memory %.0f MB over the cap of %.0f MB" % (private_mb(), RSS_CAP_MB), flush=True)
            sys.exit(3)
        self.restore()
        m = self.m
        m.script = [("key", "enter")] * 40          # a message that waits for a key gets one
        sav = self.sav
        if sav_edit is not None:
            sav = sav_edit
            self.load(sav)
            sav = self.writer() if saves else sav
        for o, data in ds_edits:        # globals the saved game does not hold (the map view's registers, the travel counters ...)
            m.write(DS, o, data)
        self.seeds.clear()
        random0 = bc.u32(m.read(DS, bc.HOLDRAND, 4))
        ds0 = {a: m.word(DS, a) for a in ctx}
        mem0 = self._memory() if saves else None
        m.cov_insns, m.cov_edges, m._last_block_jcc = {}, set(), None
        out = self._call_plain(coord, args, budget, keep_state)
        out["seeds"] = list(self.seeds)
        out["random0"] = random0
        out["ds"] = ds0
        out["sav_before"] = sav
        if saves and out["error"] is None:
            out["sav_after"] = sav if self._memory() == mem0 else self.writer()
        return out

    def _memory(self) -> bytes:
        """The whole memory but the stack's window (a call's own pushes)."""
        m = self.m
        mem = bytearray(m.uc.mem_read(0, 0x100000))
        top = linear(m.reg("SS"), m.reg("SP"))
        lo = max(0, top - 0x3000)
        mem[lo:top + 0x400] = bytes(top + 0x400 - lo)
        return bytes(mem)

    def _call_plain(self, coord, args, budget, keep_state):
        # State.call minus the restore and mutations (done by the caller)
        m = self.m
        info = self.cat.info(coord)
        if info["record"] != "root" and not (info["record"] in m.resident) and info["thunk"] is None:
            m.ensure(info["record"])
        before = bytes(m.uc.mem_read(DS * 16, 0x10000))
        far2_seg = self.far2_seg
        self.draws.clear()
        m.count = 0
        m.budget = budget or 3_000_000
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
        for i in list(range(0, lo, 64)) + list(range(0xFE00, 0x10000, 64)):
            if before[i:i + 64] != after[i:i + 64]:
                for j in range(i, i + 64):
                    if before[j] != after[j]:
                        changed[j] = after[j]
        res = {"ax": ax, "dx": dx, "error": error, "changed": changed, "random": bc.u32(after[bc.HOLDRAND:bc.HOLDRAND + 4]),
               "draws": list(self.draws), "seconds": secs, "instructions": m.count, "coverage": m.coverage_result()}
        if keep_state:
            res["after"], res["before"] = after, before
        return res

    def restore(self):
        m = self.m
        # the snapshot's attributes are given to the machine by reference: a run that changes one (the allocation table, the overlays in memory ...)
        # would change the snapshot itself, so every restore starts from a deep copy
        m.restore_state({**self.snap, "attrs": copy.deepcopy(self.snap["attrs"])})
        m.script = []
        m.cov_insn_rec, m.cov_edge_rec, m.cov_insns, m.cov_edges = {}, {}, {}, set()
        self.sp0 = m.reg("SP")
        self.stop_at = None
        self.base = bytes(m.uc.mem_read(DS * 16, 0x10000))
        self.far2_seg = 0


# ---------------------------------------------------------------------------------------------------- export
def export(fuzz_folders, states_folder: Path, out: Path):
    """The kept inputs of world_fuzz runs and the states they were made from, as the jobs file the rewrite's test reads."""
    states, jobs = {}, []
    for folder in fuzz_folders:
        for f in sorted(Path(folder).glob("*.json")):
            d = json.loads(f.read_text(encoding="utf-8"))
            name = d["state"]
            if name not in states:
                with gzip.open(Path(states_folder) / (name + ".state"), "rb") as fh:
                    s = pickle.load(fh)
                states[name] = {"session": s["fight"], "sav": base64.b64encode(gzip.compress(s["sav"], 6)).decode(), "card": s["card"], "random": s["random"]}
            for item in d["kept"]:
                args = item["args"]
                if d["function"] == "R029+0x281C" and "derived" not in item:
                    # runs kept before world_fuzz stored the derived argument: the original was called with the month of the
                    # saved game it loaded (its header 0x6A), 12 for 11 and an odd raw argument
                    month = struct.unpack_from("<H", gzip.decompress(base64.b64decode(item["sav_before"])), 0x6A)[0]
                    args = [month + 1 if args[0] & 1 and month == 11 else month]
                jobs.append({"id": "%s|%s#%d" % (d["function"], name, item["iteration"]), "function": d["function"], "state": name, "args": args,
                             "sav_before": item["sav_before"], "ds": item.get("ds", {}), "ops": item["ops"], "random0": item["random0"],
                             "orig": {"ax": item["ax"], "dx": item["dx"], "random": item["random"], "draws": item["draws"], "seeds": [[a, int(c)] for a, c in item["seeds"]],
                                      "sav_after": item["sav_after"], "changed": item.get("changed", {})}})
    with gzip.open(out, "wt", encoding="utf-8", compresslevel=6) as f:
        json.dump({"states": states, "jobs": jobs}, f)
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
    c.add_argument("--sessions", required=True)
    c.add_argument("--game", default=str(GAME))
    c.add_argument("--recordings")
    c.add_argument("--only")
    a = ap.parse_args()
    if a.cmd == "export":
        export(a.folders, Path(a.states), Path(a.out))
    if a.cmd == "capture":
        for s in a.sessions.split(","):
            capture(s, Path(a.out), a.game, a.recordings, set(a.only.split(",")) if a.only else None)


if __name__ == "__main__":
    main()
