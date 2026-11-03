#!/usr/bin/env python3
"""
AvatarTuner .unitypackage builder.

Builds a real Unity package archive (gzip-compressed TAR with one GUID directory per
asset: `asset`, `asset.meta`, `pathname`) from the repository's source files and their
existing Unity .meta files. No Unity Editor or Unity license is required, so the same
script runs locally and in CI.

Deterministic by construction: entries are sorted, tar metadata is zeroed and the gzip
header timestamp is fixed - the same inputs produce a byte-identical archive.

Usage:
    python scripts/build_unitypackage.py                  # version from package.json
    python scripts/build_unitypackage.py --version 1.0.0  # explicit

Output (default ./dist/):
    AvatarTuner-v<version>.unitypackage
    AvatarTuner-v<version>.unitypackage.sha256

Failure policy: missing .meta for any packaged FILE is a hard error (Unity metadata is
what keeps GUIDs and references stable). Folders without .meta get a deterministic
path-derived GUID (stable across builds). Excluded dev files are verified to be absent.
"""

import argparse
import gzip
import hashlib
import io
import json
import os
import re
import sys
import tarfile

# ---------------------------------------------------------------- configuration

INSTALL_ROOT = "Assets/AvatarTuner"

# Files and directories that make up the shippable package.
INCLUDE_DIRS = ["Core", "Editor"]
INCLUDE_FILES = ["package.json", "LICENSE", "THIRD_PARTY_NOTICES.md"]

# Things that must never end up in the package (verified after collecting).
EXCLUDE_PATTERNS = [
    "PROGRESS.md",
    ".github",
    "scripts",
    "Tests",
    "Tools~",
    "docs",
    "Assets/AvatarTuner-Generated",
    ".gitignore",
    "CHANGELOG.md",
    "README.md",
    "README.th.md",
    "README.zh-Hans.md",
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

GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)

FOLDER_META = (
    "fileFormatVersion: 2\n"
    "guid: {guid}\n"
    "folderAsset: yes\n"
    "DefaultImporter:\n"
    "  externalObjects: {{}}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


# ---------------------------------------------------------------- helpers

def fail(message):
    """Prints an error and returns False (failure) for validation helpers."""
    print("error: " + message, file=sys.stderr)
    return False


def read_guid(meta_path):
    with open(meta_path, "r", encoding="utf-8") as handle:
        match = GUID_RE.search(re.sub(r"\r\n?", "\n", handle.read()))
    return match.group(1) if match else None


def deterministic_folder_guid(relative_path):
    # Stable across builds: derived from the path, so re-packing never moves references.
    digest = hashlib.sha256(("AvatarTuner-folder:" + relative_path).encode("utf-8"))
    return digest.hexdigest()[:32]


def posix(path):
    return path.replace(os.sep, "/")


def collect(repo_root):
    """Returns a list of (project_relative_path, absolute_file_path) for package files."""
    entries = []
    for directory in INCLUDE_DIRS:
        for dirpath, _dirnames, filenames in os.walk(os.path.join(repo_root, directory)):
            for filename in filenames:
                if filename.endswith(".meta"):
                    continue
                absolute = os.path.join(dirpath, filename)
                entries.append((posix(os.path.relpath(absolute, repo_root)), absolute))
    for filename in INCLUDE_FILES:
        absolute = os.path.join(repo_root, filename)
        if not os.path.isfile(absolute):
            raise FileNotFoundError("required package file is missing: " + filename)
        entries.append((filename, absolute))
    entries.sort(key=lambda entry: entry[0])
    return entries


def all_folders(paths):
    """Every folder containing a packaged file, plus Assets and the install root."""
    folders = {"Assets", INSTALL_ROOT}
    for relative, _absolute in paths:
        parts = relative.split("/")
        # The repository files land under Assets/AvatarTuner/<repo-relative>.
        accumulated = INSTALL_ROOT
        for part in parts[:-1]:
            accumulated = accumulated + "/" + part
            folders.add(accumulated)
    return sorted(folders)


def build_archive(repo_root, out_path):
    paths = collect(repo_root)

    # ---- validation: every file needs a .meta with a GUID
    errors = []
    for relative, absolute in paths:
        meta_path = absolute + ".meta"
        if not os.path.isfile(meta_path):
            errors.append("missing .meta for packaged file: " + relative)
        elif read_guid(meta_path) is None:
            errors.append("invalid .meta (no guid): " + relative + ".meta")

    for required in REQUIRED_PACKAGE_FILES:
        if not os.path.isfile(os.path.join(repo_root, required)):
            errors.append("required package file is missing: " + required)

    for excluded in EXCLUDE_PATTERNS:
        for relative, _absolute in paths:
            if relative == excluded or relative.startswith(excluded + "/"):
                errors.append("excluded file would be packaged: " + relative)

    if errors:
        for error in errors:
            print("error: " + error, file=sys.stderr)
        return False

    # ---- folder entries (deterministic GUID when the repo has no folder .meta)
    folder_entries = []  # (pathname, guid, meta_text)
    for folder in all_folders(paths):
        repo_relative = folder[len(INSTALL_ROOT) + 1:] if folder.startswith(INSTALL_ROOT + "/") else None
        guid = None
        if repo_relative:
            meta_path = os.path.join(repo_root, repo_relative + ".meta")
            if os.path.isfile(meta_path):
                guid = read_guid(meta_path)
        if guid is None:
            guid = deterministic_folder_guid(folder)
        folder_entries.append((folder, guid, FOLDER_META.format(guid=guid)))

    # ---- uniqueness + path safety
    seen_guids = set()
    for _folder, guid, _meta in folder_entries:
        if guid in seen_guids:
            return fail("duplicate GUID generated for folder entries: " + guid)
        seen_guids.add(guid)
    for relative, absolute in paths:
        guid = read_guid(absolute + ".meta")
        if guid in seen_guids:
            return fail("duplicate GUID in package: " + guid + " (" + relative + ")")
        seen_guids.add(guid)
        target = INSTALL_ROOT + "/" + relative
        if target.startswith("/") or ".." in target.split("/"):
            return fail("unsafe package path: " + target)

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)

    # ---- deterministic gzip+tar (fixed timestamps -> reproducible archive)
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w") as tar:

        def add(name, data):
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mtime = 0
            info.uid = 0
            info.gid = 0
            info.uname = ""
            info.gname = ""
            tar.addfile(info, io.BytesIO(data))

        for folder, guid, meta_text in folder_entries:
            add(guid + "/pathname", (folder + "\n00\n").encode("utf-8"))
            add(guid + "/asset.meta", meta_text.encode("utf-8"))

        for relative, absolute in paths:
            guid = read_guid(absolute + ".meta")
            target = INSTALL_ROOT + "/" + relative
            with open(absolute, "rb") as handle:
                add(guid + "/asset", handle.read())
            with open(absolute + ".meta", "rb") as handle:
                add(guid + "/asset.meta", handle.read())
            add(guid + "/pathname", (target + "\n00\n").encode("utf-8"))

    with open(out_path, "wb") as handle:
        with gzip.GzipFile(filename="", mode="wb", fileobj=handle, mtime=0) as gz:
            gz.write(raw.getvalue())
    return True


def verify_archive(out_path):
    """Re-reads the archive and checks structure, pathnames and extraction."""
    count_assets = 0
    with tarfile.open(out_path, "r:gz") as tar:
        members = tar.getmembers()
        if not members:
            return fail("archive is empty") or False
        by_guid = {}
        for member in members:
            parts = member.name.split("/")
            if len(parts) != 2 or not re.fullmatch(r"[0-9a-f]{32}", parts[0]):
                return fail("unexpected archive entry: " + member.name)
            by_guid.setdefault(parts[0], set()).add(parts[1])
            extracted = tar.extractfile(member)
            if extracted is None:
                return fail("archive member is unreadable: " + member.name)
            if extracted.read() is None:
                return fail("archive member failed to extract: " + member.name)
        for guid, kinds in by_guid.items():
            if "pathname" not in kinds or "asset.meta" not in kinds:
                return fail("archive entry misses pathname/asset.meta: " + guid)
            if "asset" in kinds:
                count_assets += 1
    if count_assets == 0:
        return fail("archive contains no assets")
    return True


def main():
    parser = argparse.ArgumentParser(description="Build the AvatarTuner .unitypackage")
    parser.add_argument("--version", default=None, help="override package.json version")
    parser.add_argument("--out", default=None, help="output archive path")
    args = parser.parse_args()

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    with open(os.path.join(repo_root, "package.json"), "r", encoding="utf-8") as handle:
        package = json.load(handle)
    version = args.version or package.get("version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        print("error: version must be semantic (x.y.z), got: " + repr(version), file=sys.stderr)
        return 1

    out_path = args.out or os.path.join(
        repo_root, "dist", "AvatarTuner-v" + version + ".unitypackage")

    if not build_archive(repo_root, out_path):
        return 1
    if not verify_archive(out_path):
        return 1

    digest = hashlib.sha256()
    with open(out_path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    sha_path = out_path + ".sha256"
    with open(sha_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(digest.hexdigest() + "  " + os.path.basename(out_path) + "\n")

    size = os.path.getsize(out_path)
    print("built " + out_path + " (" + str(size) + " bytes)")
    print("sha256 " + digest.hexdigest())
    return 0


if __name__ == "__main__":
    sys.exit(main())
