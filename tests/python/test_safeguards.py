"""Unit tests of the safeguards: local_dir, wt_remove.py, backup_local.py.

    tools/python/Scripts/python.exe -I -B tests/python/test_safeguards.py
"""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
TOOLS = REPO / "tools"
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / "oracle"))
import backup_local  # noqa: E402
import local_paths  # noqa: E402
import wt_remove  # noqa: E402


def git(cwd, *args):
    r = subprocess.run(["git", "-C", str(cwd), "-c", "user.name=t", "-c", "user.email=t@t", *args], capture_output=True, text=True)
    assert r.returncode == 0, (args, r.stderr)
    return r.stdout


def make_junction(link: Path, target: Path) -> bool:
    if os.name == "nt":
        r = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)], capture_output=True, text=True)
        return r.returncode == 0
    try:
        os.symlink(target, link, target_is_directory=True)
        return True
    except OSError:
        return False


def remove_link(link: Path):
    os.rmdir(link) if os.name == "nt" else os.unlink(link)   # never touches the target


class Env:
    def __init__(self, **kv):
        self.kv = kv

    def __enter__(self):
        self.old = {k: os.environ.get(k) for k in self.kv}
        for k, v in self.kv.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v

    def __exit__(self, *a):
        for k, v in self.old.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v


class LocalDir(unittest.TestCase):
    def test_env_local_wins(self):
        with Env(DARKLANDS_LOCAL=r"X:\shared\local", DARKLANDS_ROOT=r"Y:\proj"):
            self.assertEqual(local_paths.local_dir(r"Z:\repo"), r"X:\shared\local")

    def test_root_when_no_local(self):
        with Env(DARKLANDS_LOCAL=None, DARKLANDS_ROOT=r"Y:\proj"):
            self.assertEqual(local_paths.local_dir(r"Z:\repo"), os.path.join(r"Y:\proj", "code", "local"))

    def test_repo_local_when_neither(self):
        with Env(DARKLANDS_LOCAL=None, DARKLANDS_ROOT=None):
            self.assertEqual(local_paths.local_dir(r"Z:\repo"), os.path.join(r"Z:\repo", "local"))
            self.assertEqual(local_paths.local_dir(), str(REPO / "local"))

    def test_worktree_layout(self):
        # <root>\code (main) and <root>\worktrees\r1 (worktree): the worktree reaches <root>\code\local through DARKLANDS_ROOT
        with Env(DARKLANDS_LOCAL=None, DARKLANDS_ROOT=r"R:\proj"):
            self.assertEqual(local_paths.local_dir(r"R:\proj\worktrees\r1"), r"R:\proj\code\local")

    def test_oracle_paths_reexport(self):
        import paths
        with Env(DARKLANDS_LOCAL=r"Q:\l"):
            self.assertEqual(paths.local_dir(), r"Q:\l")

    def test_recording_default_folder(self):
        import recording
        with Env(DARKLANDS_ORACLE_RECORDINGS=None, DARKLANDS_LOCAL=r"Q:\l"):
            self.assertEqual(recording.default_folder(), Path(r"Q:\l") / "oracle-recordings")


class WtRemove(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.repo = self.root / "code"
        self.wdir = self.root / "worktrees"
        self.repo.mkdir()
        self.wdir.mkdir()
        git(self.repo, "init", "-b", "master")
        (self.repo / "a.txt").write_text("a")
        git(self.repo, "add", "a.txt")
        git(self.repo, "commit", "-m", "init")
        git(self.repo, "worktree", "add", str(self.wdir / "w1"), "-b", "w1", "master")
        self.links = []

    def tearDown(self):
        for l in self.links:
            if os.path.lexists(l):
                remove_link(l)
        self.tmp.cleanup()

    def run_tool(self, name, *extra):
        with Env(DARKLANDS_ROOT=str(self.root)):
            return subprocess.run([sys.executable, "-I", "-B", str(TOOLS / "wt_remove.py"), name, "--repo", str(self.repo),
                                   "--worktrees", str(self.wdir), *extra], capture_output=True, text=True)

    def test_clean_worktree_removed(self):
        r = self.run_tool("w1")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertFalse((self.wdir / "w1").exists())

    def test_dry_run_keeps_it(self):
        r = self.run_tool("w1", "--dry-run")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue((self.wdir / "w1").exists())

    def test_missing_and_path_names_refused(self):
        self.assertNotEqual(self.run_tool("nope").returncode, 0)
        self.assertNotEqual(self.run_tool("..\\code").returncode, 0)
        self.assertTrue(self.repo.exists())

    def test_unmerged_commit_refused(self):
        w = self.wdir / "w1"
        (w / "b.txt").write_text("b")
        git(w, "add", "b.txt")
        git(w, "commit", "-m", "work")
        r = self.run_tool("w1")
        self.assertNotEqual(r.returncode, 0)
        self.assertTrue(w.exists())

    def test_foreign_untracked_refused_but_build_output_ok(self):
        w = self.wdir / "w1"
        (w / "bin").mkdir()
        (w / "bin" / "x.dll").write_text("x")
        (w / "obj").mkdir()
        (w / "obj" / "y").write_text("y")
        self.assertEqual(self.run_tool("w1", "--dry-run").returncode, 0)
        (w / "notes.txt").write_text("precious")
        r = self.run_tool("w1")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("notes.txt", r.stderr)
        self.assertTrue((w / "notes.txt").exists())

    def test_modified_tracked_file_refused(self):
        w = self.wdir / "w1"
        (w / "a.txt").write_text("changed")
        self.assertNotEqual(self.run_tool("w1").returncode, 0)
        self.assertEqual((w / "a.txt").read_text(), "changed")

    def test_junction_refused_target_untouched(self):
        target = self.root / "shared_local"
        target.mkdir()
        (target / "keep.txt").write_text("irreplaceable")
        link = self.wdir / "w1" / "bin"          # inside the build-output exclusion on purpose: the link scan must still catch it
        if not make_junction(link, target):
            self.skipTest("cannot create a junction/symlink here")
        self.links.append(link)
        r = self.run_tool("w1")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("bin", r.stderr)
        self.assertTrue((self.wdir / "w1").exists())
        self.assertEqual((target / "keep.txt").read_text(), "irreplaceable")

    def test_junction_dry_run_refused_too(self):
        target = self.root / "shared_local"
        target.mkdir()
        (target / "keep.txt").write_text("k")
        link = self.wdir / "w1" / "local"
        if not make_junction(link, target):
            self.skipTest("cannot create a junction/symlink here")
        self.links.append(link)
        self.assertNotEqual(self.run_tool("w1", "--dry-run").returncode, 0)
        self.assertTrue((target / "keep.txt").exists())

    def test_find_reparse_points_does_not_follow(self):
        target = self.root / "t"
        (target / "sub").mkdir(parents=True)
        (target / "sub" / "f").write_text("f")
        d = self.root / "scan"
        d.mkdir()
        link = d / "lnk"
        if not make_junction(link, target):
            self.skipTest("cannot create a junction/symlink here")
        self.links.append(link)
        self.assertEqual(wt_remove.find_reparse_points(d), [str(link)])


class Backup(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.src = self.root / "local"
        self.dst = self.root / "backup"
        for rel, text in {
            "overnight/TODO.md": "t", "tools/a.py": "x", "checks/c.txt": "c", "forbidden-identifiers.txt": "f", "NOTES.md": "n",
            "merge-audit-1/x.csv": "m", "oracle-recordings/sessions/s.rec": "r", "oracle-saves/S.SAV": "s",
            "batch/b1/README.md": "b", "batch/b1/data.tsv": "d", "batch/b1/pic.png": "p", "batch/b1/live-latest/log.txt": "no",
            "batch/b1/fights/f.json": "no", "replay-latest/summary.json": "no", "other/x.md": "no", "big.bin": "no",
        }.items():
            p = self.src / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text)

    def tearDown(self):
        self.tmp.cleanup()

    def files(self):
        return sorted(str(p.relative_to(self.dst)).replace("\\", "/") for p in self.dst.rglob("*") if p.is_file())

    def test_selection_and_incremental_and_prune(self):
        self.assertEqual(backup_local.main(["--src", str(self.src), "--dest", str(self.dst)]), 0)
        self.assertEqual(self.files(), sorted([
            "overnight/TODO.md", "tools/a.py", "checks/c.txt", "forbidden-identifiers.txt", "NOTES.md", "merge-audit-1/x.csv",
            "oracle-recordings/sessions/s.rec", "oracle-saves/S.SAV", "batch/b1/README.md", "batch/b1/data.tsv"]))
        self.assertTrue(backup_local.same(1, (self.src / "NOTES.md").stat().st_mtime, str(self.dst / "NOTES.md")))
        (self.src / "NOTES.md").unlink()                # never deleted in the backup without --prune
        backup_local.main(["--src", str(self.src), "--dest", str(self.dst)])
        self.assertIn("NOTES.md", self.files())
        backup_local.main(["--src", str(self.src), "--dest", str(self.dst), "--prune"])
        self.assertNotIn("NOTES.md", self.files())

    def test_changed_file_copied(self):
        backup_local.main(["--src", str(self.src), "--dest", str(self.dst)])
        (self.src / "tools" / "a.py").write_text("longer text")
        backup_local.main(["--src", str(self.src), "--dest", str(self.dst)])
        self.assertEqual((self.dst / "tools" / "a.py").read_text(), "longer text")

    def test_refuses_a_forbidden_drive_destination(self):
        with self.assertRaises(SystemExit):
            backup_local.main(["--src", str(self.src), "--dest", r"E:\somewhere\backup", "--forbid-drive", "E"])

    def test_refuses_without_a_destination(self):
        old = os.environ.pop("DARKLANDS_BACKUP", None)
        try:
            with self.assertRaises(SystemExit):
                backup_local.main(["--src", str(self.src)])
        finally:
            if old is not None:
                os.environ["DARKLANDS_BACKUP"] = old

    def test_refuses_reparse_source(self):
        link = self.root / "lnk"
        if not make_junction(link, self.src):
            self.skipTest("cannot create a junction/symlink here")
        try:
            with self.assertRaises(SystemExit):
                backup_local.main(["--src", str(link), "--dest", str(self.dst)])
        finally:
            remove_link(link)
        self.assertTrue((self.src / "NOTES.md").exists())


if __name__ == "__main__":
    unittest.main()
