#!/usr/bin/env python3
"""
Extracts the release notes for one version from CHANGELOG.md (the single source of truth
for public release descriptions).

Usage:
    python scripts/extract_release_notes.py --version 1.0.0 [--out notes.md]

The section "## v1.0.0" (or "## 1.0.0") is extracted up to the next "## " heading. If the
version has no section, the script fails with an actionable message - the changelog entry
must be prepared before tagging.
"""

import argparse
import os
import re
import sys


def extract(changelog_path, version):
    with open(changelog_path, "r", encoding="utf-8") as handle:
        text = handle.read().replace("\r\n", "\n")

    pattern = re.compile(
        r"^##\s+v?" + re.escape(version) + r"\b[^\n]*\n(?P<body>.*?)(?=^##\s|\Z)",
        re.MULTILINE | re.DOTALL)
    match = pattern.search(text)
    if match is None:
        return None
    return match.group("body").strip() + "\n"


def main():
    parser = argparse.ArgumentParser(description="Extract release notes for one version")
    parser.add_argument("--version", required=True, help="version without the v prefix (x.y.z)")
    parser.add_argument("--changelog", default=None, help="defaults to repo CHANGELOG.md")
    parser.add_argument("--out", default=None, help="write the notes here (default: stdout)")
    args = parser.parse_args()

    changelog = args.changelog or os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "CHANGELOG.md")
    if not os.path.isfile(changelog):
        print("error: changelog not found: " + changelog, file=sys.stderr)
        return 1

    notes = extract(changelog, args.version)
    if notes is None:
        print(
            "error: CHANGELOG.md has no section for version " + args.version + ".\n"
            "Prepare the changelog entry (\"## v" + args.version + "\") before tagging.",
            file=sys.stderr)
        return 1

    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(notes)
        print("wrote " + args.out)
    else:
        sys.stdout.write(notes)
    return 0


if __name__ == "__main__":
    sys.exit(main())
