"""The ONLY way to remove a helper worktree (2026-10-30: `git worktree remove` deleted the shared local/ through a junction).

    python -I -B tools/wt_remove.py <name> [--dry-run]

<name> is a folder under <project root>\\worktrees\\ (project root: DARKLANDS_ROOT, else the folder that holds the repository).
Checks, in this order; the first failure stops with a non-zero exit and nothing further runs:
  a) the worktree exists under worktrees\\ and git knows it;
  b) its HEAD is an ancestor of master;
  c) `git status --porcelain --ignored` shows nothing that master lacks, except build output (bin/, obj/);
  d) no reparse point (junction or symlink) anywhere in the worktree (listed, never followed);
  e) only then `git worktree remove` (never --force), and the folder must be gone afterwards.
--dry-run runs a) to d) and removes nothing. The branch itself stays.
"""
from __future__ import annotations

import argparse
import os
import stat
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
BUILD_DIRS = {"bin", "obj"}


def fail(msg: str, code: int = 1):
    print("REFUSED: " + msg, file=sys.stderr)
    sys.exit(code)


def git(repo: Path, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, encoding="utf-8", errors="replace")


def is_reparse(path) -> bool:
    """A symlink, a junction or any other reparse point (never followed)."""
    try:
        st = os.lstat(path)
    except OSError:
        return False
    return stat.S_ISLNK(st.st_mode) or bool(getattr(st, "st_file_attributes", 0) & REPARSE)


def find_reparse_points(top: Path) -> list[str]:
    """Every reparse point below (and including) `top`; it never descends into one."""
    found: list[str] = []
    if is_reparse(top):
        return [str(top)]
    stack = [str(top)]
    while stack:
        d = stack.pop()
        try:
            with os.scandir(d) as it:
                entries = list(it)
        except OSError as e:
            found.append("%s (cannot be scanned: %s)" % (d, e))
            continue
        for e in entries:
            if is_reparse(e.path):
                found.append(e.path)
            elif e.is_dir(follow_symlinks=False):
                stack.append(e.path)
    return sorted(found)


def only_build_output(path: str) -> bool:
    parts = [p for p in path.replace("\\", "/").strip('"').split("/") if p]
    return any(p in BUILD_DIRS for p in parts)


def pycache_is_build_output(top: Path) -> bool:
    """A __pycache__ folder counts as build output only when it holds nothing but *.pyc files and no reparse point."""
    if not top.is_dir() or is_reparse(top):
        return False
    stack = [top]
    while stack:
        d = stack.pop()
        try:
            entries = list(os.scandir(d))
        except OSError:
            return False
        for e in entries:
            if is_reparse(e.path) or e.is_dir(follow_symlinks=False) or not e.name.lower().endswith(".pyc"):
                return False
    return True


def pycache_ok(path: str, wt: Path | None) -> bool:
    parts = [p for p in path.replace("\\", "/").strip('"').split("/") if p]
    if wt is None or "__pycache__" not in parts:
        return False
    i = parts.index("__pycache__")
    return pycache_is_build_output(wt.joinpath(*parts[: i + 1]))


def foreign_status(lines: list[str], wt: Path | None = None) -> list[str]:
    bad = []
    for ln in lines:
        if not ln.strip():
            continue
        path = ln[3:]
        if " -> " in path:
            path = path.split(" -> ")[-1]
        if ln.startswith("!!") or ln.startswith("??"):
            if only_build_output(path) or pycache_ok(path, wt):
                continue
        bad.append(ln)
    return bad


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("name", help="worktree folder name under <root>\\worktrees\\")
    ap.add_argument("--dry-run", action="store_true", help="check only, remove nothing")
    ap.add_argument("--repo", default=str(REPO), help="the main repository (default: this one)")
    ap.add_argument("--worktrees", help="the worktrees folder (default: <DARKLANDS_ROOT or the repository's parent>\\worktrees)")
    ap.add_argument("--master", default="master", help="the branch a worktree must be merged into")
    a = ap.parse_args(argv)

    repo = Path(a.repo).resolve()
    root = Path(os.environ.get("DARKLANDS_ROOT") or repo.parent)
    wdir = Path(a.worktrees) if a.worktrees else root / "worktrees"
    wdir = wdir.resolve()

    # a) exists under worktrees\ and git knows it
    if not a.name or a.name in (".", "..") or "/" in a.name or "\\" in a.name or ":" in a.name:
        fail("the name must be a plain folder name, not a path: %r" % a.name)
    wt = wdir / a.name
    if not wt.is_dir():
        fail("%s does not exist" % wt)
    if is_reparse(wt):
        fail("%s is itself a link" % wt)
    if wt.resolve().parent != wdir:
        fail("%s is not directly under %s" % (wt, wdir))
    r = git(repo, "worktree", "list", "--porcelain")
    if r.returncode != 0:
        fail("git worktree list failed: " + r.stderr.strip())
    known = [Path(ln[len("worktree "):]).resolve() for ln in r.stdout.splitlines() if ln.startswith("worktree ")]
    if wt.resolve() not in known:
        fail("git does not list %s as a worktree of %s" % (wt, repo))
    if wt.resolve() == repo:
        fail("%s is the main checkout" % wt)
    print("a) %s is a worktree of %s" % (wt, repo))

    # b) HEAD is an ancestor of master
    r = git(wt, "rev-parse", "HEAD")
    if r.returncode != 0:
        fail("cannot read HEAD: " + r.stderr.strip())
    head = r.stdout.strip()
    r = git(repo, "merge-base", "--is-ancestor", head, a.master)
    if r.returncode != 0:
        fail("HEAD %s of %s is not an ancestor of %s (unmerged commits)" % (head[:10], a.name, a.master))
    print("b) HEAD %s is merged into %s" % (head[:10], a.master))

    # c) nothing that master lacks
    r = git(wt, "status", "--porcelain", "--ignored", "--untracked-files=all")
    if r.returncode != 0:
        fail("git status failed: " + r.stderr.strip())
    bad = foreign_status(r.stdout.splitlines(), wt)
    if bad:
        print("\n".join("   " + b for b in bad), file=sys.stderr)
        fail("%d change(s)/file(s) in %s that master lacks (listed above); only bin/, obj/ and pure-.pyc __pycache__ output may remain" % (len(bad), a.name))
    print("c) nothing but build output beyond master")

    # d) no reparse points
    links = find_reparse_points(wt)
    if links:
        print("\n".join("   " + p for p in links), file=sys.stderr)
        fail("%d link(s) (junction/symlink) inside %s; remove each yourself (cmd /c rmdir <link>, which never touches the target) after "
             "checking its target, then run this again" % (len(links), a.name))
    print("d) no junctions or symlinks")

    if a.dry_run:
        print("DRY RUN: %s would be removed (git worktree remove, no --force)" % wt)
        return 0

    # e) remove
    r = git(repo, "worktree", "remove", str(wt))
    if r.returncode != 0:
        fail("git worktree remove failed: " + (r.stderr.strip() or r.stdout.strip()), 2)
    if wt.exists():
        fail("%s still exists after git worktree remove" % wt, 2)
    print("e) removed %s (branch %s stays)" % (wt, a.name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
