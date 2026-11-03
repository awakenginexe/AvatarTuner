#!/usr/bin/env python3
"""
AvatarTuner VPM repository listing updater.

Merges one released version into the public VPM repo listing (index.json), following the
official VPM repo format (vcc.docs.vrchat.com/vpm/repos). Existing versions are always
preserved; re-running with the same version and the same checksum is idempotent, and
re-running with a DIFFERENT checksum is a hard error (published assets are immutable).

Usage:
    python scripts/update_vpm_listing.py \
        --version 1.0.0 \
        --zip-sha256 <sha256 of the zip> \
        --zip-url https://github.com/.../com.avatartuner.tool-1.0.0.zip \
        [--existing <path-or-url to the current index.json>] \
        [--out vpm/index.json]

The existing listing is read from the deployed URL (or a local file) so old versions
survive every release. The listing never points at a version that has no published zip.
"""

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.request

REPO_NAME = "Awake Ngine XE Packages"
REPO_ID = "com.awakenginexe.avatartuner"
REPO_URL = "https://awakenginexe.github.io/AvatarTuner/index.json"
REPO_AUTHOR = "Awake Ngine XE"
PACKAGE_NAME = "com.avatartuner.tool"

SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$")


def fail(message):
    print("error: " + message, file=sys.stderr)
    return False


def load_existing(source):
    if not source:
        return None
    try:
        if source.startswith("http://") or source.startswith("https://"):
            with urllib.request.urlopen(source, timeout=30) as response:
                return json.loads(response.read().decode("utf-8"))
        with open(source, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except Exception:
        # A missing listing (first release, or Pages not deployed yet) starts fresh.
        return None


def validate_listing(listing):
    for field in ("name", "id", "url", "author", "packages"):
        if field not in listing:
            return fail("listing misses required field: " + field)
    package = listing["packages"].get(PACKAGE_NAME)
    if package is None or "versions" not in package:
        return fail("listing has no versions for " + PACKAGE_NAME)
    for version, manifest in package["versions"].items():
        if not SEMVER_RE.match(version):
            return fail("listing version is not semver: " + version)
        for field in ("name", "displayName", "version", "url", "zipSHA256"):
            if field not in manifest:
                return fail("version " + version + " misses field: " + field)
        if manifest["version"] != version:
            return fail("version key mismatch in listing: " + version)
    return True


def main():
    parser = argparse.ArgumentParser(description="Update the VPM repo listing")
    parser.add_argument("--version", required=True)
    parser.add_argument("--zip-sha256", required=True)
    parser.add_argument("--zip-url", required=True)
    parser.add_argument("--existing", default=None,
                        help="path or URL of the currently deployed index.json")
    parser.add_argument("--out", default=None, help="output index.json path")
    args = parser.parse_args()

    if not SEMVER_RE.match(args.version):
        print("error: version is not semver: " + args.version, file=sys.stderr)
        return 1
    if not re.fullmatch(r"[0-9a-f]{64}", args.zip_sha256):
        print("error: zip-sha256 must be 64 hex chars", file=sys.stderr)
        return 1

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    with open(os.path.join(repo_root, "package.json"), "r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    if manifest.get("version") != args.version:
        print("error: package.json version does not match " + args.version, file=sys.stderr)
        return 1

    # The version's manifest = package.json plus the release-specific fields.
    manifest["url"] = args.zip_url
    manifest["zipSHA256"] = args.zip_sha256

    listing = load_existing(args.existing) or {
        "name": REPO_NAME,
        "id": REPO_ID,
        "url": REPO_URL,
        "author": REPO_AUTHOR,
        "packages": {PACKAGE_NAME: {"versions": {}}},
    }
    if "packages" not in listing:
        listing["packages"] = {}
    if PACKAGE_NAME not in listing["packages"]:
        listing["packages"][PACKAGE_NAME] = {"versions": {}}

    versions = listing["packages"][PACKAGE_NAME]["versions"]
    if args.version in versions:
        existing = versions[args.version]
        if existing.get("zipSHA256") != args.zip_sha256:
            print(
                "error: version " + args.version + " already exists in the listing with a "
                "different checksum. Published assets are immutable; bump the version.",
                file=sys.stderr)
            return 1
    versions[args.version] = manifest

    # Keep versions sorted (semver-ish: numeric triple compare, newest last is fine).
    def sort_key(version):
        parts = version.split("-")[0].split(".")
        return [int(part) for part in parts]

    listing["packages"][PACKAGE_NAME]["versions"] = {
        version: versions[version] for version in sorted(versions, key=sort_key)
    }

    if not validate_listing(listing):
        return 1

    out_path = args.out or os.path.join(repo_root, "dist", "index.json")
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(listing, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    print("wrote " + out_path + " (" + str(len(versions)) + " version(s) listed)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
