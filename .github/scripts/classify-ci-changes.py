#!/usr/bin/env python3
"""Classify a Git range as documentation text only or CI-relevant."""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import PurePosixPath


DOCUMENTATION_SUFFIXES = {".md", ".rst", ".txt"}
IGNORED_CONFIGURATION_NAMES = {
    ".dockerignore",
    ".eslintignore",
    ".gitignore",
    ".ignore",
    ".npmignore",
    ".prettierignore",
}
ZERO_SHA = "0" * 40


def git(*arguments: str) -> bytes:
    return subprocess.run(
        ["git", *arguments], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    ).stdout


def changed_paths(base: str, head: str, merge_base: bool = False) -> list[str]:
    if not base or not head or base == ZERO_SHA:
        raise ValueError("a complete, non-zero Git range is required")

    git("rev-parse", "--verify", f"{base}^{{commit}}")
    git("rev-parse", "--verify", f"{head}^{{commit}}")
    revision_range = f"{base}...{head}" if merge_base else f"{base}..{head}"
    fields = git("diff", "--name-status", "-z", "--find-renames", revision_range).split(b"\0")
    paths: list[str] = []
    index = 0
    while index < len(fields) and fields[index]:
        status = fields[index].decode("ascii")
        index += 1
        path_count = 2 if status.startswith(("R", "C")) else 1
        if index + path_count > len(fields):
            raise ValueError("Git returned an incomplete name-status record")
        for raw_path in fields[index : index + path_count]:
            paths.append(raw_path.decode("utf-8"))
        index += path_count
    return paths


def is_documentation_text(path: str) -> bool:
    parsed_path = PurePosixPath(path)
    return (
        parsed_path.suffix.lower() in DOCUMENTATION_SUFFIXES
        or parsed_path.name.lower() in IGNORED_CONFIGURATION_NAMES
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--merge-base", action="store_true")
    parser.add_argument("--output", default=os.environ.get("GITHUB_OUTPUT"))
    arguments = parser.parse_args()

    try:
        paths = changed_paths(arguments.base, arguments.head, arguments.merge_base)
        # An empty or unclassifiable range must run CI.
        run_ci = not paths or any(not is_documentation_text(path) for path in paths)
        result = "true" if run_ci else "false"
        print(f"run_ci={result}")
        for path in paths:
            print(path)
        if arguments.output:
            with open(arguments.output, "a", encoding="utf-8") as output:
                output.write(f"run_ci={result}\n")
        return 0
    except (OSError, UnicodeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"Unable to classify changed files: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
