"""Download new and changed files from the Ecowitt gateway's SD card page (cardInfo.html).

The gateway's file server offers no Last-Modified/ETag/Content-Length and rejects HEAD,
and the listing only reports rounded sizes ("3MB"). So a file is (re)downloaded when:
  - it does not exist locally, or
  - its listed size differs from the one recorded at the last download, or
  - its month (YYYYMM name prefix) was not yet over when it was last downloaded,
    i.e. the gateway may have appended rows since.
Use --all to download everything regardless.
"""

import argparse
import gzip
import json
import re
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timedelta
from pathlib import Path

HOST = "192.168.0.10"
INFO_URL = f"http://{HOST}/get_sdmmc_info"   # JSON listing used by cardInfo.html
FILE_BASE = f"http://{HOST}:81"              # files are served from port 81
OUT_DIR = Path(__file__).resolve().parent / "download"
STATE_FILE = OUT_DIR / ".sync_state.json"
TIMEOUT = 60
# a month's file counts as complete once downloaded this long after the month ended
SETTLE_MARGIN = timedelta(days=1)

MONTH_RE = re.compile(r"^(\d{4})(\d{2})")


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


def load_state() -> dict:
    try:
        return json.loads(STATE_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def save_state(state: dict) -> None:
    tmp = STATE_FILE.with_suffix(".tmp")
    tmp.write_text(json.dumps(state, indent=1, sort_keys=True), encoding="utf-8")
    tmp.replace(STATE_FILE)


def month_end(name: str) -> datetime | None:
    """Start of the month after the one in the file name, or None if it has no YYYYMM prefix."""
    m = MONTH_RE.match(name)
    if not m:
        return None
    year, month = int(m[1]), int(m[2])
    if not 1 <= month <= 12:
        return None
    return datetime(year + month // 12, month % 12 + 1, 1)


def update_reason(remote: str, item: dict, target: Path, state: dict) -> str | None:
    """Why the file needs downloading, or None if the local copy is current."""
    if not target.exists():
        return "new"
    entry = state.get(remote)
    if entry is None:
        # downloaded before state tracking existed: trust the file's mtime and current size
        entry = {"size": item.get("size"),
                 "downloaded": datetime.fromtimestamp(target.stat().st_mtime).isoformat()}
        state[remote] = entry
    if entry.get("size") != item.get("size"):
        return f"size {entry.get('size')} -> {item.get('size')}"
    end = month_end(Path(remote).name)
    if end is None:
        return "no month in name"
    if datetime.fromisoformat(entry["downloaded"]) < end + SETTLE_MARGIN:
        return "month still open at last download"
    return None


def sync(path: str, state: dict, force: bool) -> tuple[int, int, int]:
    ok = skipped = failed = 0
    for item in list_dir(path):
        name = item["name"]
        remote = f"{path}/{name}"
        if is_dir(item):
            sub = sync(remote, state, force)
            ok, skipped, failed = ok + sub[0], skipped + sub[1], failed + sub[2]
            continue

        target = OUT_DIR / remote.lstrip("/")
        reason = "forced" if force else update_reason(remote, item, target, state)
        if reason is None:
            skipped += 1
            continue

        target.parent.mkdir(parents=True, exist_ok=True)
        url = FILE_BASE + urllib.parse.quote(remote)
        print(f"{remote} ({item.get('size', '?')}, {reason}) ... ", end="", flush=True)
        started = datetime.now()
        try:
            data = fetch(url)
        except Exception as exc:
            print(f"FAILED: {exc}")
            failed += 1
            continue
        tmp = target.with_name(target.name + ".part")
        tmp.write_bytes(data)
        tmp.replace(target)
        # record when the download started: rows written during it may be missing
        state[remote] = {"size": item.get("size"), "downloaded": started.isoformat(timespec="seconds")}
        save_state(state)
        print(f"{len(data)} bytes")
        ok += 1
    return ok, skipped, failed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--all", action="store_true", help="download every file, ignoring the sync state")
    args = parser.parse_args()

    OUT_DIR.mkdir(exist_ok=True)
    state = load_state()
    ok, skipped, failed = sync("", state, args.all)
    save_state(state)
    print(f"\nDone: {ok} downloaded, {skipped} up to date, {failed} failed -> {OUT_DIR}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
