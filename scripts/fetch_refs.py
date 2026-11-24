#!/usr/bin/env python3
"""Fetch only the server layer from the pinned public image, without running it."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import tarfile
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
LOCK = json.loads((ROOT / "image.lock.json").read_text())


def request(url, headers=None):
    for attempt in range(4):
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers=headers or {}), timeout=60)
        except (OSError, urllib.error.URLError):
            if attempt == 3:
                raise
            time.sleep(attempt + 1)


def main():
    repo = LOCK["repository"]
    with request(f"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repo}:pull") as response:
        token = json.load(response)["token"]
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/vnd.docker.distribution.manifest.v2+json"}
    base = f"https://registry-1.docker.io/v2/{repo}/"
    with request(base + "manifests/" + LOCK["digest"], headers) as response:
        raw = response.read()
    if "sha256:" + hashlib.sha256(raw).hexdigest() != LOCK["digest"]:
        raise ValueError("Image manifest digest mismatch")
    manifest = json.loads(raw)
    layer = next(layer for layer in manifest["layers"] if layer["digest"] == LOCK["serverLayer"])
    destination = ROOT / "refs"
    destination.mkdir(exist_ok=True)
    if any(destination.iterdir()):
        raise SystemExit("refs/ is not empty; preserve or move it before extracting a different reference set.")
    with tempfile.TemporaryDirectory(prefix="jellyfin-lookup-") as temporary:
        archive = Path(temporary) / "server.tar.gz"
        print(f"Downloading pinned server layer ({layer['size'] / 1024**2:.1f} MiB)", flush=True)
        with request(base + "blobs/" + layer["digest"], headers) as source, archive.open("wb") as target:
            shutil.copyfileobj(source, target)
        with archive.open("rb") as source:
            hasher = hashlib.sha256()
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                hasher.update(chunk)
            digest = hasher.hexdigest()
        if "sha256:" + digest != layer["digest"]:
            raise ValueError("Server layer digest mismatch")
        with tarfile.open(archive) as members:
            for member in members:
                path = PurePosixPath(member.name)
                if member.isfile() and path.parent == PurePosixPath("jellyfin") and path.suffix in (".dll", ".xml", ".json"):
                    with members.extractfile(member) as source, (destination / path.name).open("wb") as target:
                        shutil.copyfileobj(source, target)
    runtime = json.loads((destination / "jellyfin.runtimeconfig.json").read_text())["runtimeOptions"]
    if runtime["tfm"] != "net10.0":
        raise ValueError("Unexpected target framework")
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(destination.glob("*.dll"))}
    (destination / "provenance.json").write_text(json.dumps({"image": LOCK, "assemblies": hashes}, indent=2) + "\n")
    print(f"Verified and extracted {len(hashes)} DLLs. These are ignored by Git and never packaged.")


if __name__ == "__main__":
    main()
