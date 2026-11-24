#!/usr/bin/env python3
"""Regenerate the fixed Jellyfin catalog from public GitHub release assets."""
import hashlib
import io
import json
import re
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
REPO = "ruols/jellyfin-provider-id-lookup"


def download(url):
    with urlopen(url, timeout=30) as response:
        return response.read()


def main():
    catalog_path = ROOT / "manifest.json"
    catalog = json.loads(catalog_path.read_text())
    versions = {}
    page = 1
    while True:
        releases = json.loads(download(f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}"))
        if not releases:
            break
        for release in releases:
            tag = release["tag_name"]
            if release["draft"] or not re.fullmatch(r"v\d+\.\d+\.\d+\.\d+", tag):
                continue
            version = tag[1:]
            assets = {asset["name"]: asset for asset in release["assets"]}
            package = f"ProviderIdLookup-{version}.zip"
            if package not in assets or "manifest.json" not in assets:
                continue
            entry = json.loads(download(assets["manifest.json"]["browser_download_url"]))[0]
            matches = [v for v in entry["versions"] if v["version"] == version]
            if entry["guid"] != catalog[0]["guid"] or len(matches) != 1:
                raise ValueError(f"Unexpected manifest identity for {tag}")
            candidate = matches[0]
            asset = assets[package]
            if candidate["sourceUrl"] != asset["browser_download_url"]:
                raise ValueError(f"Unexpected ZIP URL for {tag}")
            data = download(candidate["sourceUrl"])
            if hashlib.md5(data).hexdigest() != candidate["checksum"]:
                raise ValueError(f"ZIP checksum mismatch for {tag}")
            with ZipFile(io.BytesIO(data)) as archive:
                if archive.namelist() != ["Jellyfin.Plugin.ProviderIdLookup.dll"] or archive.testzip():
                    raise ValueError(f"Unexpected ZIP contents for {tag}")
            versions[version] = candidate
        if len(releases) < 100:
            break
        page += 1
    if not versions:
        raise ValueError("No verified public releases found")
    catalog[0]["versions"] = [versions[v] for v in sorted(
        versions, key=lambda version: tuple(map(int, version.split("."))), reverse=True)]
    catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n")
    print("Verified catalog versions:", ", ".join(v["version"] for v in catalog[0]["versions"]))


if __name__ == "__main__":
    main()
