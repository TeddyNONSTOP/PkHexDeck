#!/usr/bin/env python3
"""Create a source-pinned local Flatpak build from a committed repository revision."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--revision", default="HEAD")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    revision = git(root, "rev-parse", "--verify", f"{args.revision}^{{commit}}").decode().strip()
    props = git(root, "show", f"{revision}:Directory.Build.props").decode()
    match = re.search(r"<UIVersion>(\d+\.\d+\.\d+)</UIVersion>", props)
    if match is None:
        sys.exit("Could not resolve release version from the source revision")
    release_date = git(root, "show", "-s", "--format=%cs", revision).decode().strip()
    if args.output.exists():
        sys.exit("Destination already exists; choose a fresh directory to avoid stale build sources")
    args.output.mkdir(parents=True)
    source = args.output / "source.tar"
    with source.open("wb") as output:
        # Git archive applies checkout EOL conversion; Windows' native EOL is CRLF even with
        # autocrlf=false. Both overrides are process-local and preserve the source's LF blobs.
        subprocess.run(["git", "-c", "core.autocrlf=false", "-c", "core.eol=lf", "-C", str(root),
                        "archive", "--format=tar", revision], stdout=output, check=True)
    replacements = {
        "@VERSION@": match[1], "@SOURCE_REVISION@": revision,
        "@RELEASE_DATE@": release_date,
        "@SOURCE_SHA256@": hashlib.sha256(source.read_bytes()).hexdigest(),
    }
    template = git(root, "show", f"{revision}:packaging/flatpak/io.github.realgarit.PKHeX-Avalonia.json.in").decode()
    for token, value in replacements.items():
        template = template.replace(token, value)
    manifest = json.loads(template)
    manifest_path = args.output / f"{manifest['app-id']}.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    sources = git(root, "show", f"{revision}:packaging/flatpak/nuget-sources.json")
    (args.output / "nuget-sources.json").write_bytes(sources)
    print(f"Prepared {manifest_path} from {revision}, version {match[1]}")


if __name__ == "__main__":
    main()
