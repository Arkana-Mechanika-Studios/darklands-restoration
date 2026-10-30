"""Back up the irreplaceable part of the shared local/ folder to G: (2026-10-30: local/ was deleted through a junction and E: could not undelete).

    python -I -B tools/backup_local.py [--src <local>] [--dest G:\\darklands-backup\\local] [--dry-run] [--prune]

Mirror, copy only new or changed files (size or modification time), never delete in the backup unless --prune (which then removes
backup files that are no longer selected from the source). Selected from the source:
  overnight/  tools/  checks/  merge-audit-*/  oracle-recordings/  oracle-saves/  (all files)
  forbidden-identifiers.txt and the *.md at the top level
  batch/**: *.md *.tsv *.csv *.txt *.json under 5 MB
Run captures are skipped inside batch/ (folders replay-*, live-*, fights, sessions). Refuses to write anywhere on E:, and to run when
the source or destination is a reparse point (junction/symlink); reparse points inside the source are skipped, never followed.
"""
from __future__ import annotations

import argparse
import fnmatch
import os
import shutil
import stat
import sys
from pathlib import Path

REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
DEFAULT_DEST = r"G:\darklands-backup\local"
FORBIDDEN_DRIVES = {"E"}
WHOLE_DIRS = ["overnight", "tools", "checks", "oracle-recordings", "oracle-saves"]
WHOLE_GLOBS = ["merge-audit-*"]
TOP_FILES = ["forbidden-identifiers.txt"]
TOP_GLOBS = ["*.md"]
BATCH_EXT = {".md", ".tsv", ".csv", ".txt", ".json"}
BATCH_MAX = 5 * 1024 * 1024
CAPTURE_DIRS = ["replay-*", "live-*", "fights", "sessions"]
MTIME_SLACK = 2.0


def local_dir_default() -> str:
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import local_paths
    return local_paths.local_dir()


def is_reparse(path) -> bool:
    try:
        st = os.lstat(path)
    except OSError:
        return False
    return stat.S_ISLNK(st.st_mode) or bool(getattr(st, "st_file_attributes", 0) & REPARSE)


def drive_of(path) -> str:
    return os.path.splitdrive(os.path.abspath(path))[0].rstrip(":").upper()


def check_paths(src: str, dest: str) -> None:
    """Raise SystemExit on an unsafe source or destination."""
    if drive_of(dest) in FORBIDDEN_DRIVES:
        sys.exit("REFUSED: the destination %s is on E:, where the originals live and where deleted data is unrecoverable" % dest)
    if not os.path.isdir(src):
        sys.exit("REFUSED: the source %s is not a folder" % src)
    if is_reparse(src):
        sys.exit("REFUSED: the source %s is a reparse point (junction/symlink)" % src)
    # the destination (or its nearest existing parent) must not be a link either, and must not resolve onto E:
    p = os.path.abspath(dest)
    while p and not os.path.exists(p):
        parent = os.path.dirname(p)
        if parent == p:
            break
        p = parent
    if is_reparse(p) or is_reparse(dest):
        sys.exit("REFUSED: the destination %s is (inside) a reparse point" % dest)
    if drive_of(os.path.realpath(p)) in FORBIDDEN_DRIVES:
        sys.exit("REFUSED: the destination %s resolves onto E:" % dest)
    a, b = os.path.normcase(os.path.realpath(src)), os.path.normcase(os.path.realpath(dest))
    if a == b or b.startswith(a + os.sep) or a.startswith(b + os.sep):
        sys.exit("REFUSED: the source and the destination overlap")


def is_capture(name: str) -> bool:
    return any(fnmatch.fnmatch(name.lower(), g) for g in CAPTURE_DIRS)


def walk(root: str, rel: str, accept, skip_dir, skipped: list):
    """Yield (relative path, absolute path, size, mtime) of the files below root/rel that `accept(relpath, size)`; skips links."""
    stack = [rel]
    while stack:
        r = stack.pop()
        d = os.path.join(root, r) if r else root
        try:
            with os.scandir(d) as it:
                entries = sorted(it, key=lambda e: e.name)
        except OSError as e:
            skipped.append("%s: %s" % (d, e))
            continue
        for e in entries:
            relp = os.path.join(r, e.name) if r else e.name
            if is_reparse(e.path):
                skipped.append("%s: reparse point, not followed" % e.path)
                continue
            if e.is_dir(follow_symlinks=False):
                if skip_dir(relp, e.name):
                    skipped.append("%s: run capture folder" % e.path)
                    continue
                stack.append(relp)
            elif e.is_file(follow_symlinks=False):
                try:
                    st = e.stat(follow_symlinks=False)
                except OSError as ex:
                    skipped.append("%s: %s" % (e.path, ex))
                    continue
                if accept(relp, st.st_size):
                    yield relp, e.path, st.st_size, st.st_mtime


def select(src: str, skipped: list):
    """All files to back up, as (relative path, absolute path, size, mtime)."""
    never = lambda rel, name: False  # noqa: E731
    yes = lambda rel, size: True  # noqa: E731
    try:
        top = sorted(os.listdir(src))
    except OSError as e:
        sys.exit("cannot read %s: %s" % (src, e))
    for name in top:
        full = os.path.join(src, name)
        if is_reparse(full):
            skipped.append("%s: reparse point, not followed" % full)
            continue
        if os.path.isdir(full):
            if name in WHOLE_DIRS or any(fnmatch.fnmatch(name, g) for g in WHOLE_GLOBS):
                yield from walk(src, name, yes, never, skipped)
            elif name == "batch":
                def acc(rel, size):
                    return os.path.splitext(rel)[1].lower() in BATCH_EXT and size < BATCH_MAX
                yield from walk(src, name, acc, lambda rel, n: is_capture(n), skipped)
        elif os.path.isfile(full):
            if name in TOP_FILES or any(fnmatch.fnmatch(name, g) for g in TOP_GLOBS):
                st = os.stat(full)
                yield name, full, st.st_size, st.st_mtime


def same(src_size, src_mtime, dst) -> bool:
    try:
        st = os.stat(dst)
    except OSError:
        return False
    return st.st_size == src_size and abs(st.st_mtime - src_mtime) <= MTIME_SLACK


def copy_file(src_path: str, dst_path: str) -> None:
    os.makedirs(os.path.dirname(dst_path), exist_ok=True)
    tmp = dst_path + ".part"
    shutil.copyfile(src_path, tmp)
    st = os.stat(src_path)
    os.utime(tmp, (st.st_atime, st.st_mtime))
    os.replace(tmp, dst_path)


def prune(dest: str, wanted: set, dry: bool) -> int:
    removed = 0
    for dirpath, dirnames, filenames in os.walk(dest, topdown=False, followlinks=False):
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, dest)
            if rel not in wanted and not is_reparse(full):
                removed += 1
                if not dry:
                    os.remove(full)
        for dn in dirnames:
            full = os.path.join(dirpath, dn)
            if not is_reparse(full) and not dry:
                try:
                    os.rmdir(full)   # only when empty
                except OSError:
                    pass
    return removed


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", help="the local folder (default: DARKLANDS_LOCAL, else DARKLANDS_ROOT/code/local, else <repo>/local)")
    ap.add_argument("--dest", default=DEFAULT_DEST)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--prune", action="store_true", help="also delete backup files that are no longer selected from the source")
    a = ap.parse_args(argv)
    src = os.path.abspath(a.src or local_dir_default())
    dest = os.path.abspath(a.dest)
    check_paths(src, dest)
    if not a.dry_run:
        os.makedirs(dest, exist_ok=True)

    skipped: list = []
    copied = unchanged = errors = 0
    nbytes = 0
    wanted = set()
    for rel, path, size, mtime in select(src, skipped):
        wanted.add(rel)
        dst = os.path.join(dest, rel)
        if same(size, mtime, dst):
            unchanged += 1
            continue
        if a.dry_run:
            copied += 1
            nbytes += size
            continue
        try:
            copy_file(path, dst)
            copied += 1
            nbytes += size
        except OSError as e:   # a file being written or removed by a running pass: the next run takes it
            errors += 1
            print("ERROR %s: %s" % (rel, e), file=sys.stderr)
    pruned = prune(dest, wanted, a.dry_run) if a.prune and os.path.isdir(dest) else 0

    total = 0
    nfiles = 0
    if os.path.isdir(dest):
        for dp, _dn, fns in os.walk(dest):
            for fn in fns:
                try:
                    total += os.path.getsize(os.path.join(dp, fn))
                    nfiles += 1
                except OSError:
                    pass
    print("backup %s -> %s%s" % (src, dest, " (dry run)" if a.dry_run else ""))
    print("selected %d files: copied %d (%.1f MB), unchanged %d, errors %d, skipped entries %d, pruned %d"
          % (len(wanted), copied, nbytes / 1048576, unchanged, errors, len(skipped), pruned))
    print("backup now holds %d files, %.2f GB" % (nfiles, total / 1073741824))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
