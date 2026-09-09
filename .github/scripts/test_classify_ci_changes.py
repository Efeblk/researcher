#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).with_name("classify-ci-changes.py")
SPEC = importlib.util.spec_from_file_location("classifier", SCRIPT)
assert SPEC and SPEC.loader
classifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(classifier)


class ClassifierTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self.temporary_directory.name)
        self.git("init", "-q")
        self.git("config", "user.email", "ci@example.invalid")
        self.git("config", "user.name", "CI Test")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def git(self, *arguments: str) -> str:
        return subprocess.run(
            ["git", *arguments], cwd=self.repository, check=True,
            stdout=subprocess.PIPE, text=True, encoding="utf-8"
        ).stdout.strip()

    def commit_files(self, message: str, files: dict[str, str]) -> str:
        for name, contents in files.items():
            path = self.repository / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(contents, encoding="utf-8")
        self.git("add", "-A")
        self.git("commit", "-qm", message)
        return self.git("rev-parse", "HEAD")

    def paths(self, base: str, head: str) -> list[str]:
        previous = Path.cwd()
        try:
            os.chdir(self.repository)
            return classifier.changed_paths(base, head)
        finally:
            os.chdir(previous)

    def assert_docs_only(self, paths: list[str]) -> None:
        self.assertTrue(paths)
        self.assertTrue(all(classifier.is_documentation_text(path) for path in paths))

    def test_documentation_text_only(self) -> None:
        base = self.commit_files("base", {"README.md": "one"})
        head = self.commit_files("docs", {"README.md": "two", "docs/notes.TXT": "note"})
        self.assert_docs_only(self.paths(base, head))

    def test_mixed_change_runs_ci(self) -> None:
        base = self.commit_files("base", {"README.md": "one"})
        head = self.commit_files("mixed", {"README.md": "two", "config.json": "{}"})
        self.assertTrue(any(not classifier.is_documentation_text(path) for path in self.paths(base, head)))

    def test_code_renamed_to_documentation_runs_ci(self) -> None:
        base = self.commit_files("base", {"sample.cs": "class Sample {}"})
        self.git("mv", "sample.cs", "sample.md")
        self.git("commit", "-qm", "rename")
        paths = self.paths(base, self.git("rev-parse", "HEAD"))
        self.assertEqual(["sample.cs", "sample.md"], paths)
        self.assertTrue(any(not classifier.is_documentation_text(path) for path in paths))

    def test_documentation_deletion_is_documentation_only(self) -> None:
        base = self.commit_files("base", {"obsolete.rst": "old"})
        (self.repository / "obsolete.rst").unlink()
        self.git("commit", "-qam", "delete")
        self.assert_docs_only(self.paths(base, self.git("rev-parse", "HEAD")))

    def test_unicode_documentation_path(self) -> None:
        base = self.commit_files("base", {"README.md": "one"})
        head = self.commit_files("unicode", {"docs/araştırma.md": "iki"})
        self.assertEqual(["docs/araştırma.md"], self.paths(base, head))

    def test_common_ignore_file_only(self) -> None:
        base = self.commit_files("base", {"README.md": "one"})
        head = self.commit_files("ignore", {"tools/.dockerignore": "bin/"})
        self.assert_docs_only(self.paths(base, head))

    def test_ignore_file_mixed_with_code_runs_ci(self) -> None:
        base = self.commit_files("base", {"README.md": "one"})
        head = self.commit_files("mixed", {".gitignore": "bin/", "Program.cs": "class Program {}"})
        self.assertTrue(any(not classifier.is_documentation_text(path) for path in self.paths(base, head)))

    def test_missing_base_fails_closed(self) -> None:
        head = self.commit_files("base", {"README.md": "one"})
        with self.assertRaises(subprocess.CalledProcessError):
            self.paths("f" * 40, head)


if __name__ == "__main__":
    unittest.main()
