#!/usr/bin/env python3
"""
AvatarTuner VPM package builder (zip format for the VRChat Creator Companion).

Builds a VPM-compatible ZIP from the repository's source files and their existing Unity
.meta files. The ZIP root IS the package root (package.json sits at the archive root, as
required by the VPM package format). Same source revision as the .unitypackage - one
codebase, two distributions.

Deterministic by construction: entries are sorted, timestamps fixed - the same inputs
produce a byte-identical archive.

Usage:
    python scripts/build_vpmpackage.py --version 1.0.0 --url <download-url>

Output (default ./dist/):
    com.avatartuner.tool-<version>.zip
    com.avatartuner.tool-<version>.zip.sha256
"""

import argparse
import hashlib
import io
import json
import os
import re
import sys
import zipfile

# Same include/exclude contract as the .unitypackage builder.
INCLUDE_DIRS = ["Core", "Editor"]
INCLUDE_FILES = ["package.json", "LICENSE", "THIRD_PARTY_NOTICES.md"]

EXCLUDE_PATTERNS = [
    "PROGRESS.md", ".github", "scripts", "Tests", "Tools~", "docs",
    "Assets/AvatarTuner-Generated", ".gitignore", "CHANGELOG.md",
    "README.md", "README.th.md", "README.zh-Hans.md",
]

REQUIRED_PACKAGE_FILES = [
    "package.json",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "Core/AvatarTuner.Core.asmdef",
    "Editor/AvatarTuner.Editor.asmdef",
    "Editor/NDMF/AvatarTuner.Editor.NDMF.asmdef",
    "Editor/UI/AvatarTunerWindow.cs",
    "Editor/UI/AvatarTunerWindow.uss",
    "Editor/Localization/Translations/en.json",
    "Editor/Localization/Translations/th.json",
    "Editor/Localization/Translations/zh-Hans.json",
]

FIXED_DATE = (2026, 1, 1, 0, 0, 0)


def posix(path):
    return path.replace(os.sep, "/")


def collect(repo_root):
    entries = []
    for directory in INCLUDE_DIRS:
        for dirpath, _dirnames, filenames in os.walk(os.path.join(repo_root, directory)):
            for filename in filenames:
                if filename.endswith(".meta"):
                    continue  # .meta files are written alongside their asset
                absolute = os.path.join(dirpath, filename)
                relative = posix(os.path.relpath(absolute, repo_root))
                entries.append((relative, absolute))
    for filename in INCLUDE_FILES:
        absolute = os.path.join(repo_root, filename)
        if not os.path.isfile(absolute):
            raise FileNotFoundError("required package file is missing: " + filename)
        entries.append((filename, absolute))
    entries.sort(key=lambda entry: entry[0])
    return entries


def fail(message):
    print("error: " + message, file=sys.stderr)
    return False


def build_zip(repo_root, out_path, download_url):
    entries = collect(repo_root)

    errors = 0
    for relative, absolute in entries:
        # .meta files ship inside the zip too; every non-meta file needs one.
        if not relative.endswith(".meta") and not os.path.isfile(absolute + ".meta"):
            fail("missing .meta for packaged file: " + relative)
            errors += 1
        for excluded in EXCLUDE_PATTERNS:
            if relative == excluded or relative.startswith(excluded + "/"):
                fail("excluded file would be packaged: " + relative)
                errors += 1
    for required in REQUIRED_PACKAGE_FILES:
        if not os.path.isfile(os.path.join(repo_root, required)):
            fail("required package file is missing: " + required)
            errors += 1
    if errors > 0:
        return False

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)

    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for relative, absolute in entries:
            if relative == "package.json":
                # Inject the per-version download URL into the packaged manifest copy.
                with open(absolute, "r", encoding="utf-8") as handle:
                    manifest = json.load(handle)
                manifest["url"] = download_url
                data = json.dumps(manifest, ensure_ascii=False, indent=2).encode("utf-8")
                info = zipfile.ZipInfo(relative, FIXED_DATE)
                archive.writestr(info, data)
                meta_path = absolute + ".meta"
                if os.path.isfile(meta_path):
                    info_meta = zipfile.ZipInfo(relative + ".meta", FIXED_DATE)
                    with open(meta_path, "rb") as handle:
                        archive.writestr(info_meta, handle.read())
                continue

            for name in (relative, relative + ".meta"):
                source = os.path.join(repo_root, name)
                if not os.path.isfile(source):
                    continue
                info = zipfile.ZipInfo(name, FIXED_DATE)
                with open(source, "rb") as handle:
                    archive.writestr(info, handle.read())

    return True


def verify_zip(out_path, version):
    with zipfile.ZipFile(out_path, "r") as archive:
        names = archive.namelist()
        if not names:
            return fail("zip archive is empty")
        if "package.json" not in names:
            return fail("package.json is not at the archive root")
        manifest = json.loads(archive.read("package.json").decode("utf-8"))
        if manifest.get("version") != version:
            return fail("packaged version mismatch: " + manifest.get("version", ""))
        for field in ("name", "displayName", "unity", "description", "author", "url"):
            if field not in manifest:
                return fail("packaged manifest misses field: " + field)
        for name in names:
            if name.startswith("/") or ".." in name.split("/"):
                return fail("unsafe zip entry: " + name)
            if name.endswith(".meta"):
                continue
            if name + ".meta" not in names:
                return fail("zip entry lacks its .meta: " + name)
        if not any(name.startswith("Core/") for name in names):
            return fail("Core sources missing from the zip")
        if not any(name.startswith("Editor/") for name in names):
            return fail("Editor sources missing from the zip")
        # Every archived member must extract.
        for name in names:
            archive.read(name)
    return True


def main():
    parser = argparse.ArgumentParser(description="Build the AvatarTuner VPM package (zip)")
    parser.add_argument("--version", required=True)
    parser.add_argument("--url", required=True,
                        help="direct download URL of this version's zip")
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        print("error: version must be semantic (x.y.z), got: " + repr(args.version), file=sys.stderr)
        return 1

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    with open(os.path.join(repo_root, "package.json"), "r", encoding="utf-8") as handle:
        if json.load(handle).get("version") != args.version:
            print("error: package.json version does not match " + args.version, file=sys.stderr)
            return 1

    name = "com.avatartuner.tool"
    out_path = args.out or os.path.join(
        repo_root, "dist", name + "-" + args.version + ".zip")

    if not build_zip(repo_root, out_path, args.url):
        return 1
    if not verify_zip(out_path, args.version):
        return 1

    digest = hashlib.sha256()
    with open(out_path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    sha_path = out_path + ".sha256"
    with open(sha_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(digest.hexdigest() + "  " + os.path.basename(out_path) + "\n")

    print("built " + out_path + " (" + str(os.path.getsize(out_path)) + " bytes)")
    print("sha256 " + digest.hexdigest())
    return 0


if __name__ == "__main__":
    sys.exit(main())
