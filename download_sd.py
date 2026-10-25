"""Download all files listed on the Ecowitt gateway's SD card page (cardInfo.html)."""

import gzip
import json
import sys
import urllib.parse
import urllib.request
from pathlib import Path

HOST = "192.168.68.210"
INFO_URL = f"http://{HOST}/get_sdmmc_info"   # JSON listing used by cardInfo.html
FILE_BASE = f"http://{HOST}:81"              # files are served from port 81
OUT_DIR = Path(__file__).resolve().parent / "download"
TIMEOUT = 60


def fetch(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
        data = resp.read()
        if resp.headers.get("Content-Encoding") == "gzip":
            data = gzip.decompress(data)
        return data


def list_dir(path: str = "") -> list[dict]:
    url = INFO_URL + (f"?path={urllib.parse.quote(path)}" if path else "")
    info = json.loads(fetch(url))
    if info.get("sd_status", "normal") != "normal":
        print(f"warning: SD status is {info['sd_status']!r}", file=sys.stderr)
    return info.get("file_list") or []


def is_dir(item: dict) -> bool:
    # cardInfo.html treats type == 2 as a folder
    return str(item.get("type")).lower() in ("2", "dir", "folder", "directory")


def download_all(path: str = "") -> tuple[int, int]:
    ok = failed = 0
    for item in list_dir(path):
        name = item["name"]
        remote = f"{path}/{name}"
        if is_dir(item):
            sub_ok, sub_failed = download_all(remote)
            ok += sub_ok
            failed += sub_failed
            continue

        target = OUT_DIR / remote.lstrip("/")
        target.parent.mkdir(parents=True, exist_ok=True)
        url = FILE_BASE + urllib.parse.quote(remote)
        print(f"{remote} ({item.get('size', '?')}) ... ", end="", flush=True)
        try:
            data = fetch(url)
        except Exception as exc:
            print(f"FAILED: {exc}")
            failed += 1
            continue
        target.write_bytes(data)
        print(f"{len(data)} bytes")
        ok += 1
    return ok, failed


def main() -> int:
    OUT_DIR.mkdir(exist_ok=True)
    ok, failed = download_all()
    print(f"\nDone: {ok} downloaded, {failed} failed -> {OUT_DIR}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
