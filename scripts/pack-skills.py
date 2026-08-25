#!/usr/bin/env python3
"""Package each skill under seed/skills/<name>/ into seed/skills/<name>.zip.

Produces the layout the importer expects (agentskills.io):

    travel-planning.zip
    └── travel-planning/
        ├── SKILL.md
        └── assets/*.json

Output is byte-reproducible: entries are sorted and every timestamp is pinned, so
re-packing unchanged sources yields an identical file and does not churn git.

The extension allowlist mirrors SkillPackageImporter's. Enforcing it here too means a
stray file is caught at pack time rather than as a rejected upload.

Usage:  python3 scripts/pack-skills.py [--check]
        --check  verify existing zips are up to date; exit 1 if not (for CI)
"""

from __future__ import annotations

import argparse
import hashlib
import sys
import zipfile
from pathlib import Path

SEED_ROOT = Path(__file__).resolve().parent.parent / "seed" / "skills"
ALLOWED_EXTENSIONS = {".md", ".json", ".txt", ".yaml", ".yml", ".png", ".svg"}
FIXED_TIMESTAMP = (1980, 1, 1, 0, 0, 0)


def collect(skill_dir: Path) -> list[Path]:
    """Every packable file under skill_dir, sorted, with dotfiles skipped."""
    files = [
        p
        for p in skill_dir.rglob("*")
        if p.is_file() and not any(part.startswith(".") for part in p.relative_to(skill_dir).parts)
    ]
    return sorted(files, key=lambda p: p.relative_to(skill_dir).as_posix())


def build(skill_dir: Path) -> tuple[bytes, list[str]]:
    """Return the zip bytes and the entry names written, or raise on a bad package."""
    name = skill_dir.name

    if not (skill_dir / "SKILL.md").is_file():
        raise ValueError(f"{name}: no SKILL.md at the package root")

    files = collect(skill_dir)
    for path in files:
        if path.suffix.lower() not in ALLOWED_EXTENSIONS:
            rel = path.relative_to(skill_dir).as_posix()
            raise ValueError(f"{name}: '{rel}' has a non-allowlisted extension '{path.suffix}'")

    import io

    buffer = io.BytesIO()
    entries: list[str] = []
    # Deflate at a fixed level; ZipFile writes no extra metadata beyond what we set here.
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            entry = f"{name}/{path.relative_to(skill_dir).as_posix()}"
            info = zipfile.ZipInfo(entry, date_time=FIXED_TIMESTAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16  # regular file, no symlink/setuid bits
            archive.writestr(info, path.read_bytes())
            entries.append(entry)

    return buffer.getvalue(), entries


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify zips are current; do not write")
    args = parser.parse_args()

    if not SEED_ROOT.is_dir():
        print(f"error: {SEED_ROOT} does not exist", file=sys.stderr)
        return 1

    skill_dirs = sorted(p for p in SEED_ROOT.iterdir() if p.is_dir())
    if not skill_dirs:
        print(f"error: no skill directories under {SEED_ROOT}", file=sys.stderr)
        return 1

    stale = False
    for skill_dir in skill_dirs:
        target = SEED_ROOT / f"{skill_dir.name}.zip"
        try:
            payload, entries = build(skill_dir)
        except ValueError as exc:
            print(f"FAIL {exc}", file=sys.stderr)
            return 1

        digest = hashlib.sha256(payload).hexdigest()[:12]

        if args.check:
            current = target.read_bytes() if target.is_file() else b""
            if current != payload:
                print(f"STALE {target.name} (run scripts/pack-skills.py)")
                stale = True
            else:
                print(f"ok    {target.name}  {len(payload):>6} bytes  sha256:{digest}")
            continue

        target.write_bytes(payload)
        print(f"wrote {target.name}  {len(payload):>6} bytes  sha256:{digest}")
        for entry in entries:
            print(f"        {entry}")

    return 1 if stale else 0


if __name__ == "__main__":
    sys.exit(main())
