"""Run every fight (battle_oracle.FIGHTS) and every session (lockstep-sessions/*.steps) with N workers, each its own process.

usage: python -I -B rerun_all.py <out folder> [--workers N] [--fights a,b|none] [--sessions a,b|none] [--live] [--replay-only]
           [--no-coverage] [--recordings DIR] [--headless <Darklands.Host.Headless.exe>] [--exe E] [--game G]

Outputs (as the two tools write them): <out>/fights/<fight>/report.json, <out>/fights/<fight>/<fight>/cov/cov-0.jsonl (a fight
that agrees), <out>/sessions/<session>/report.json and cov/cov-0.jsonl (a session whose states all agree), <out>/<job>.log, and
<out>/summary.json with one row per job: verdict, first-difference point, whether the original was replayed or run live, seconds.

The original side is replayed from recordings (recording.py) when they are valid, else run live and recorded; --live forces live (from the
snapshot of the run's start point when there is one, else it makes it; --no-snapshot starts every live run from the beginning). The jobs are
started slowest first (the last live durations, <recordings>/durations.json).
RULE: a full --live rerun before every release, and periodically (the recordings only say what the original did; the live run is
the proof that they still do).

Workers: default the core count, bounded by the free memory (a live original takes up to ~400 MB; a job is started only while
that much plus a reserve is free).
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
REPO = HERE.parent.parent
GAME = REPO.parent / "game" / "DARKLAND"

# DKSAVE40 (written by another tool: party size 6, party slots whose status is 0) as a normalised copy under local/ (divergence D-72509c: the
# original'"'"'s writer R037+0x2ACE recounts the party and skips a status-0 slot): the copy is made by local/overnight/dk40/normalise.py when missing
NORMALISED = "local/oracle-saves/DKSAVE40N.SAV"
NORMALISING = {NORMALISED: ("DKSAVE40.SAV", "local/overnight/dk40/normalise.py")}
LOCAL = Path(os.environ.get("DARKLANDS_LOCAL", REPO / "local"))     # the git-ignored local/ folder (shared by worktrees through the variable)

# the saved game each session starts from (None: the start screen, no saved game)
SESSIONS = {"pressburg-map": "DKSAVE0.SAV", "tyrnau": "DKSAVE0.SAV", "residence": "DKSAVE0.SAV", "save-load": "DKSAVE0.SAV",
            "gate-battle": "DKSAVE0.SAV", "shop": "DKSAVE0.SAV", "cache": "Dksave10.sav", "saints": "Dksave10.sav",
            "stay": "Dksave10.sav", "sheet": "Dksave10.sav", "wildcamp": "DKSAVE17.SAV", "alchemy": "Dksave10.sav",
            "inventory": "Dksave10.sav", "chargen-party": None, "notices": "DKSAVE0.SAV", "university": "DKSAVE0.SAV",
            "smiths": "DKSAVE0.SAV", "healer": "DKSAVE0.SAV", "churches": "DKSAVE0.SAV", "wharves": "DKSAVE0.SAV",
            "university-shop": "DKSAVE0.SAV",
            "stables": "DKSAVE0.SAV", "pawnshop": "DKSAVE0.SAV", "foreigntrader": "DKSAVE0.SAV", "banks": "DKSAVE0.SAV",
            "menubar": "DKSAVE0.SAV", "menubar2": "DKSAVE0.SAV", "roster": "DKSAVE0.SAV", "roster4": "DKSAVE0.SAV",
            "wien-rathaus": "DKSAVE0.SAV", "mapsave": "DKSAVE17.SAV",
            # the inn's roster screen (local/overnight/roster2): DKSAVE0 (five listed), DKSAVE8 (Hamburg, four rows), Dksave10 (Ulm, nine rows)
            "roster-colours": "DKSAVE0.SAV", "roster-moves": "DKSAVE0.SAV", "roster-wrap": "DKSAVE8.SAV", "roster-mouse": "DKSAVE8.SAV",
            "roster-create2": "DKSAVE8.SAV", "roster-ulm": "Dksave10.sav",
            # the player's orders in a battle (local/overnight/orders): Prague's gate DKSAVE40, Luebeck's Dksave21, the others the gate of DKSAVE0
            "orders-select": "DKSAVE0.SAV", "orders-walk": "DKSAVE0.SAV", "orders-missile": "DKSAVE0.SAV", "orders-attack-a": "DKSAVE0.SAV",
            "orders-attack-b": "DKSAVE0.SAV", "orders-attack-b2": "DKSAVE0.SAV", "orders-attack-v": "DKSAVE0.SAV", "orders-attacks": "DKSAVE0.SAV",
            "orders-sheet": "DKSAVE0.SAV", "orders-save": "DKSAVE0.SAV", "orders-potion": "Dksave21.sav", "luebeck-potion": "Dksave21.sav", "orders-potion2": NORMALISED,
            "orders-potion3": NORMALISED, "orders-prague-missile": NORMALISED,
            "sheet-dksave0": "DKSAVE0.SAV",
            # the loot screen after the gate fight of DKSAVE0 (local/overnight/loot): keys, mouse, capital keys and Esc
            "loot-keys": "DKSAVE0.SAV", "loot-mouse": "DKSAVE0.SAV", "loot-esc": "DKSAVE0.SAV",
            # the camp and the pack outside a battle (local/overnight/camp): DKSAVE99 and Dksave22 are parties on the map, DKSAVE0 is at the Pressburg inn
            "camp-wild-brew": "DKSAVE99.SAV", "camp-wild-risk": "Dksave22.sav", "camp-inn-broke": "DKSAVE0.SAV", "camp-rough": "DKSAVE0.SAV", "camp-mouse": "DKSAVE99.SAV",
            "pack-potions": "DKSAVE99.SAV", "pack-stacks": "DKSAVE99.SAV",
            # the mine of Rammelsburg (local/overnight/dungeon): DKSAVE99 on the map, the walk to the town, the mine's battle
            "dungeon-mine-doors": "DKSAVE99.SAV", "dungeon-mine-orders": "DKSAVE99.SAV", "dungeon-mine-group": "DKSAVE99.SAV"}
# the sessions run with --battle-records (the combatant records of every battle pass compared: report.json's battle)
BATTLE_RECORDS = {"orders-select", "orders-walk", "orders-missile", "orders-attack-a", "orders-attack-b", "orders-attack-b2", "orders-attack-v",
                  "orders-attacks", "orders-sheet", "orders-save", "orders-potion", "luebeck-potion", "orders-potion2", "orders-potion3", "orders-prague-missile",
                  "dungeon-mine-doors", "dungeon-mine-orders", "dungeon-mine-group"}      # the minimal case of the sheet's allocation error (R031+0x0B22): ends, does not agree
IGNORED = {"state", "file", "text state", "texts", "party seed"}
RESERVE_MB, JOB_MB = 1500, 450


def free_mb() -> int:
    if sys.platform == "win32":
        import ctypes

        class Status(ctypes.Structure):
            _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong), ("total", ctypes.c_ulonglong), ("avail", ctypes.c_ulonglong),
                        ("tpage", ctypes.c_ulonglong), ("apage", ctypes.c_ulonglong), ("tvirt", ctypes.c_ulonglong), ("avirt", ctypes.c_ulonglong),
                        ("ext", ctypes.c_ulonglong)]
        st = Status(length=ctypes.sizeof(Status))
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(st))
        return int(st.avail >> 20)
    for line in open("/proc/meminfo"):
        if line.startswith("MemAvailable"):
            return int(line.split()[1]) // 1024
    return 1 << 20


def resolve_save(name: str, saves: Path) -> Path:
    """A saved game of the game folder, or a normalised copy under local/ (made when missing)."""
    if not name.startswith("local/"):
        return saves / name
    target = LOCAL / name[len("local/"):]
    if not target.exists():
        source, script = NORMALISING[name]
        subprocess.run([sys.executable, "-I", "-B", str(LOCAL / script[len("local/"):]), str(saves / source), str(target)], check=False)
        if not target.exists():
            raise SystemExit("%s is missing and %s could not make it from SAVES/%s" % (target, script, source))
    return target


def verdict_fight(folder: Path, name: str):
    report = json.loads((folder / "report.json").read_text(encoding="utf-8"))[name]
    ok = report["clock"] is None
    where = "agrees over %d ticks" % report["ticks agreeing"] if ok else "clock %s after %d ticks: %s" % (
        report["clock"], report["ticks agreeing"], json.dumps(report["differences"][:1]))
    return ok, where, report["original run"], report["seconds"]


def verdict_session(folder: Path, name: str):
    report = json.loads((folder / "report.json").read_text(encoding="utf-8"))
    first = next((r for r in report["states"] if set(r) - IGNORED), None)
    where = "agrees" if first is None else "first divergence: %s" % (first.get("state") or first["file"])
    battle = report.get("battle")
    if battle and battle.get("clock") is not None:
        first, where = battle, "battle parts at clock %s%s" % (battle["clock"], "" if first is None else "; " + where)
    return first is None, where, report["original run"], report["seconds"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=os.cpu_count() or 4)
    ap.add_argument("--fights", default="all")
    ap.add_argument("--sessions", default="all")
    ap.add_argument("--no-coverage", action="store_true")
    ap.add_argument("--exe", default=str(GAME / "DARKLAND.EXE"))
    ap.add_argument("--game", default=str(GAME))
    ap.add_argument("--headless")
    ap.add_argument("--timeout", type=int, default=7200)
    import recording
    recording.add_arguments(ap)
    a = ap.parse_args()
    import battle_oracle
    fights = list(battle_oracle.FIGHTS) if a.fights == "all" else [] if a.fights == "none" else a.fights.split(",")
    sessions = sorted(SESSIONS) if a.sessions == "all" else [] if a.sessions == "none" else a.sessions.split(",")
    unknown = {p.stem for p in (HERE / "lockstep-sessions").glob("*.steps")} - set(SESSIONS)
    if unknown and a.sessions == "all":
        raise SystemExit("sessions without a saved game in SESSIONS: %s" % ", ".join(sorted(unknown)))
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    py = [sys.executable, "-I", "-B", "-u"]
    common = ["--recordings", a.recordings] + (["--live"] if a.live else []) + (["--replay-only"] if a.replay_only else []) + (["--no-snapshot"] if a.no_snapshot else []) \
        + (["--coverage"] if not a.no_coverage else []) + (["--headless", a.headless] if a.headless else [])
    saves = Path(a.game) / "SAVES"
    jobs = []       # (kind, name, command)
    for f in fights:
        jobs.append(("fight", f, py + [str(HERE / "battle_oracle.py"), a.exe, a.game, str(out / "fights" / f), "--save", str(saves / "DKSAVE0.SAV"),
                                       "--fights", f] + common))
    for s in sessions:
        save = ["--save", str(resolve_save(SESSIONS[s], saves))] if SESSIONS[s] else []
        jobs.append(("session", s, py + [str(HERE / "lockstep.py"), a.exe, a.game, str(HERE / "lockstep-sessions" / (s + ".steps")),
                                         str(out / "sessions" / s)] + save + (["--battle-records"] if s in BATTLE_RECORDS else []) + common))
    # the slowest jobs first (the last live durations kept in <recordings>/durations.json; a job never timed goes first, as it may be the slowest)
    durations_file = Path(a.recordings) / "durations.json"
    try:
        known = json.loads(durations_file.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        known = {}
    jobs.sort(key=lambda j: -known.get("%s/%s" % (j[0], j[1]), float("inf")))
    workers = max(1, min(a.workers, (free_mb() - RESERVE_MB) // JOB_MB))
    print("%d jobs, %d workers (%d MB free)" % (len(jobs), workers, free_mb()), flush=True)
    running, done, t0 = {}, [], time.time()
    pending = list(jobs)
    while pending or running:
        while pending and len(running) < workers and (not running or free_mb() - RESERVE_MB >= JOB_MB):
            kind, name, cmd = pending.pop(0)
            log = open(out / ("%s-%s.log" % (kind, name)), "w", encoding="utf-8")
            running[name] = (kind, subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT), log, time.time())
        time.sleep(1)
        for name, (kind, proc, log, started) in list(running.items()):
            late = time.time() - started > a.timeout
            if proc.poll() is None and not late:
                continue
            if late:
                proc.kill()
            log.close()
            del running[name]
            row = {"kind": kind, "name": name, "exit": proc.returncode, "wall": round(time.time() - started)}
            try:
                folder = out / ("fights" if kind == "fight" else "sessions") / name
                ok, where, how, seconds = (verdict_fight if kind == "fight" else verdict_session)(folder, name)
                row.update(agrees=ok, point=where, original=how, seconds=seconds)
            except Exception as e:
                row.update(agrees=False, point="no report (%s)" % type(e).__name__, original="?")
            done.append(row)
            print("%-8s %-16s %-9s %s [%s, %ds]" % (kind, name, "agrees" if row["agrees"] else "DIFFERS", row["point"][:140], row["original"], row["wall"]), flush=True)
    for row in done:
        if row["original"] == "live":
            known["%s/%s" % (row["kind"], row["name"])] = row["wall"]
    durations_file.parent.mkdir(parents=True, exist_ok=True)
    durations_file.write_text(json.dumps(dict(sorted(known.items())), indent=1), encoding="utf-8")
    summary = {"seconds": round(time.time() - t0), "workers": workers, "jobs": sorted(done, key=lambda r: (r["kind"], r["name"]))}
    (out / "summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print("%d jobs in %d s; %d agree, %d differ" % (len(done), summary["seconds"], sum(r["agrees"] for r in done), sum(not r["agrees"] for r in done)))


if __name__ == "__main__":
    main()
