#!/usr/bin/env python3
"""Create a candidate ZIP and a Jellyfin manifest; does not publish anything."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ASSEMBLY = "Jellyfin.Plugin.ProviderIdLookup"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", default="ruols/jellyfin-provider-id-lookup")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository):
        parser.error("Invalid owner/repository")
    version = ET.parse(ROOT / "src" / ASSEMBLY / (ASSEMBLY + ".csproj")).findtext(".//Version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Jellyfin requires a four-part numeric version")
    dll = ROOT / "src" / ASSEMBLY / "bin/Release/net10.0" / (ASSEMBLY + ".dll")
    if not dll.is_file():
        raise SystemExit("Run the Release build and tests first.")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"ProviderIdLookup-{version}.zip"
    # Only the plugin: do not ship Jellyfin, framework, test, or NuGet dependency assemblies.
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        info = zipfile.ZipInfo(dll.name, (2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        package.writestr(info, dll.read_bytes())
    data = archive.read_bytes()
    manifest = json.loads((ROOT / "manifest.json").read_text())
    manifest[0]["versions"] = [{
        "version": version,
        "changelog": "Experimental candidate for the pinned 261006-amd64-testing image. Keep the Go proxy until acceptance tests pass.",
        "targetAbi": "12.2.0.0",
        "sourceUrl": f"https://github.com/{args.repository}/releases/download/v{version}/{archive.name}",
        "checksum": hashlib.md5(data).hexdigest(),  # Required by Jellyfin's repository format.
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
    }]
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
    (output / "SHA256SUMS").write_text(hashlib.sha256(data).hexdigest() + "  " + archive.name + "\n")
    print(f"Candidate only: {archive}")
    print("Generated sourceUrl is reserved for a future Release; it is not evidence of publication.")


if __name__ == "__main__":
    main()
