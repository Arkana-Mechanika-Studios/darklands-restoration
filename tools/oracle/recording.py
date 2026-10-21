"""Recordings of the ORIGINAL side of the oracles (battle_oracle.py, lockstep.py).

The original, run in the DOS machine of dosmachine.py, is deterministic and takes ~98% of a fight's or session's time. The first
(live) run stores everything the C# side is given and the comparison reads (per pass of a fight: the pass lines and the clock
marks; per session: the saved games, random state, card, written files, clock seeds, set-up ticks, the log) with the original's
coverage. Later runs run only the C# and compare against the recording.

A recording is reused only if the SHA-256 of every input that can change the original's run is the same:
  * the EXE bytes (dosmachine.py also insists on the canonical one);
  * every non-hidden file of the game folder (Vfs.HIDDEN: the temporaries, the log and SAVES are never read from disk) by relative
    path and bytes, and docs/source-map/original-functions.csv (names the thunks, so the coverage records);
  * the run's arguments: a session's steps, saved game bytes (.SAV and .BSV; SAVES/DEFAULT when none), budget and trace flags;
    a fight's request words, passes, key pause, steps and saved game bytes;
  * the source (line ends normalised) of every tools/oracle module the entry module imports, transitively and wherever the import
    stands (lockstep.py, dosmachine.py, run_unit.py, savdiff.py, ... and battle_oracle.py for the fights), so hooks such as the
    shop stack restore, the stale-word hooks, the idle rules, the Trace class are covered; this module and rerun_all.py are not
    (they only store and fetch); FORMAT below is raised when what is stored changes;
  * the Python, unicorn and capstone versions.
Any difference -> a live run, which replaces the recording; nothing stale is used. --live forces the live run.

Files: <folder>/<kind>/<name>[.<variant>].rec (gzip of a pickle: parts, key, payload; the files are local, trusted).
"""
from __future__ import annotations

import ast
import gzip
import hashlib
import json
import os
import pickle
import platform
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
FORMAT = 1
NOT_HASHED = {"recording", "rerun_all"}


def default_folder() -> Path:
    return Path(os.environ.get("DARKLANDS_ORACLE_RECORDINGS") or REPO / "local" / "oracle-recordings")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def source_hashes(entry: str) -> dict:
    """{module: sha256 of the source} for the entry module and every tools/oracle module it imports (transitively)."""
    seen: dict[str, str] = {}
    todo = [entry]
    while todo:
        name = todo.pop()
        if name in seen or name in NOT_HASHED:
            continue
        data = (HERE / (name + ".py")).read_bytes()
        seen[name] = sha(data.replace(b"\r\n", b"\n"))
        for node in ast.walk(ast.parse(data)):
            mods = [a.name for a in node.names] if isinstance(node, ast.Import) else [node.module] if isinstance(node, ast.ImportFrom) and node.module and not node.level else []
            for m in mods:
                top = m.split(".")[0]
                if (HERE / (top + ".py")).is_file():
                    todo.append(top)
    return dict(sorted(seen.items()))


def game_hash(game: str | Path, hidden) -> str:
    """One hash over the relative names and bytes of the game folder's files the original can read."""
    root = Path(game)
    h = hashlib.sha256()
    for p in sorted(root.rglob("*"), key=lambda q: q.relative_to(root).as_posix().lower()):
        rel = p.relative_to(root).as_posix().lower()
        if p.is_file() and not hidden(rel):
            h.update(rel.encode() + b"\0" + sha(p.read_bytes()).encode() + b"\n")
    return h.hexdigest()


def environment() -> dict:
    import capstone
    import unicorn
    return {"python": platform.python_version(), "unicorn": getattr(unicorn, "__version__", "?"),
            "capstone": getattr(capstone, "__version__", "?")}


def save_parts(save: Path | None, game: str) -> dict:
    """The saved game the original is given (and the file it takes when there is none)."""
    if save:
        bsv = save.with_suffix(".BSV")
        return {"sav": sha(save.read_bytes()), "bsv": sha(bsv.read_bytes()) if bsv.exists() else None}
    default = Path(game) / "SAVES" / "DEFAULT"
    return {"default": sha(default.read_bytes()) if default.is_file() else None}


def common_parts(exe: Path, game: str, entry: str, save: Path | None) -> dict:
    from dosmachine import Vfs
    probe = Vfs(None)
    return {"format": FORMAT, "exe": sha(exe.read_bytes()), "game": game_hash(game, probe.hidden),
            "functions": sha((REPO / "docs" / "source-map" / "original-functions.csv").read_bytes()),
            "code": source_hashes(entry), "environment": environment(), "save": save_parts(save, game)}


class Recording:
    def __init__(self, folder, kind: str, name: str, parts: dict, live: bool = False, replay_only: bool = False, variant: str = ""):
        self.path = Path(folder) / kind / (name + ("." + variant if variant else "") + ".rec")
        self.parts = json.loads(json.dumps(parts, sort_keys=True))
        self.key = sha(json.dumps(self.parts, sort_keys=True).encode())
        self.live, self.replay_only = live, replay_only
        self.why = "forced live" if live else "no recording"

    def load(self, need_coverage: bool = False):
        """The stored payload, or None when a live run is needed (self.why says why); with replay_only a stale one stops the run."""
        payload = None
        if not self.live and self.path.is_file():
            try:
                with gzip.open(self.path, "rb") as f:
                    rec = pickle.load(f)
            except Exception as e:      # a partial or unreadable file
                rec, self.why = None, "unreadable recording (%s)" % type(e).__name__
            if rec is not None:
                if rec.get("format") == FORMAT and rec.get("key") == self.key:
                    if need_coverage and rec["payload"].get("cov") is None:
                        self.why = "recording has no coverage"
                    else:
                        payload = rec["payload"]
                else:
                    old = rec.get("parts", {})
                    self.why = "stale recording (changed: %s)" % (", ".join(sorted(k for k in set(old) | set(self.parts) if old.get(k) != self.parts.get(k))) or "format")
        if payload is None and self.replay_only:
            raise SystemExit("%s: %s and --replay-only" % (self.path.name, self.why))
        print("original %s: %s" % (self.path.name, "replayed" if payload is not None else "live (%s)" % self.why), flush=True)
        return payload

    def store(self, payload: dict):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_name(self.path.name + ".%d.tmp" % os.getpid())
        with gzip.open(tmp, "wb", compresslevel=3) as f:
            pickle.dump({"format": FORMAT, "key": self.key, "parts": self.parts, "payload": payload}, f, protocol=4)
        os.replace(tmp, self.path)


def add_arguments(ap):
    ap.add_argument("--live", action="store_true", help="run the original live and refresh its recording (default: replay a valid recording)")
    ap.add_argument("--replay-only", action="store_true", help="never run the original: stop if there is no valid recording")
    ap.add_argument("--recordings", default=str(default_folder()), help="recordings folder (default local/oracle-recordings, or DARKLANDS_ORACLE_RECORDINGS)")
