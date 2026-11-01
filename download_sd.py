"""Download new and changed files from the Ecowitt gateway's SD card page (cardInfo.html).

The gateway's file server offers no Last-Modified/ETag/Content-Length and rejects HEAD,
and the listing only reports rounded sizes ("3MB"). So a file is (re)downloaded when:
  - it does not exist locally, or
  - its listed size differs from the one recorded at the last download, or
  - its month (YYYYMM name prefix) was not yet over when it was last downloaded,
    i.e. the gateway may have appended rows since.
Use --all to download everything regardless.

With --rest the script instead polls the gateway's local REST API (never the Ecowitt
cloud) and saves each endpoint's JSON to download_rest/, each at its own interval.

All URLs, paths and timings are read from config.yaml (or --config) at startup.
"""

import argparse
import gzip
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timedelta
from pathlib import Path

import yaml

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG = SCRIPT_DIR / "config.yaml"

# set from the config file by load_config()
INFO_URL: str
FILE_BASE: str
OUT_DIR: Path
STATE_FILE: Path
TIMEOUT: float
SETTLE_MARGIN: timedelta
REST_OUT_DIR: Path
REST_ENDPOINTS: list[dict]   # each {"url", "file", "interval", optional "paged"}

MONTH_RE = re.compile(r"^(\d{4})(\d{2})")


def load_config(path: Path) -> None:
    global INFO_URL, FILE_BASE, OUT_DIR, STATE_FILE, TIMEOUT, SETTLE_MARGIN
    global REST_OUT_DIR, REST_ENDPOINTS
    cfg = yaml.safe_load(path.read_text(encoding="utf-8"))
    gw, sd, rest = cfg["gateway"], cfg["sd"], cfg["rest"]
    host = str(gw["host"])

    def url(u: str) -> str:
        return u.format(host=host)

    TIMEOUT = float(gw["timeout"])
    INFO_URL = url(sd["info_url"])
    FILE_BASE = url(sd["file_base"]).rstrip("/")
    OUT_DIR = SCRIPT_DIR / sd["out_dir"]
    STATE_FILE = OUT_DIR / sd["state_file"]
    SETTLE_MARGIN = timedelta(hours=float(sd["settle_margin_hours"]))
    REST_OUT_DIR = SCRIPT_DIR / rest["out_dir"]
    REST_ENDPOINTS = [{**e, "url": url(e["url"]), "interval": float(e["interval"])}
                      for e in rest["endpoints"]]


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


def fetch_paged(url: str, pages: int) -> list:
    """Fetch url?page=1..N (e.g. get_sensors_info) and return all pages merged into one list."""
    items = []
    for page in range(1, pages + 1):
        items += json.loads(fetch(f"{url}?page={page}"))
    return items


def poll_rest(endpoints: list[dict], pages: int) -> tuple[int, int]:
    """Fetch the given endpoints once and save them; returns (failures, sensor page count)."""
    failed = 0
    for endpoint in endpoints:
        try:
            if endpoint.get("paged"):
                data = fetch_paged(endpoint["url"], pages)
            else:
                data = json.loads(fetch(endpoint["url"]))
                if isinstance(data, dict) and "sensorid_page" in data:   # get_version
                    pages = int(data["sensorid_page"] or 1)
        except Exception as exc:
            print(f"{endpoint['url']}: FAILED: {exc}", file=sys.stderr)
            failed += 1
            continue
        target = REST_OUT_DIR / f"{endpoint['file']}.json"
        tmp = target.with_suffix(".tmp")
        tmp.write_text(json.dumps(data, indent=1, ensure_ascii=False), encoding="utf-8")
        tmp.replace(target)
    return failed, pages


def run_rest(override: float | None) -> int:
    """Poll each endpoint at its own interval (or all at `override`; 0 = poll once and exit)."""
    REST_OUT_DIR.mkdir(exist_ok=True)
    intervals = [e["interval"] if override is None else override for e in REST_ENDPOINTS]
    due = [0.0] * len(REST_ENDPOINTS)   # monotonic time of each endpoint's next poll
    pages = 1                           # sensor pages, learned from get_version
    while True:
        now = time.monotonic()
        batch = [i for i, t in enumerate(due) if t <= now]   # config order: get_version first
        failed, pages = poll_rest([REST_ENDPOINTS[i] for i in batch], pages)
        stamp = datetime.now().isoformat(sep=" ", timespec="seconds")
        names = ", ".join(REST_ENDPOINTS[i]["file"] for i in batch)
        print(f"{stamp}: {len(batch) - failed}/{len(batch)} saved ({names})")
        if override is not None and override <= 0:
            return 1 if failed else 0
        for i in batch:
            due[i] = now + max(intervals[i], 1.0)
        time.sleep(max(0.0, min(due) - time.monotonic()))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--all", action="store_true", help="download every file, ignoring the sync state")
    parser.add_argument("--rest", action="store_true", help="poll the gateway's local REST API instead of the SD card")
    parser.add_argument("--interval", type=float,
                        help="seconds between REST polls for all endpoints, overriding the config "
                             "(0 = poll once and exit)")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG,
                        help=f"settings file (default {DEFAULT_CONFIG.name})")
    args = parser.parse_args()
    load_config(args.config)

    if args.rest:
        try:
            return run_rest(args.interval)
        except KeyboardInterrupt:
            return 0

    OUT_DIR.mkdir(exist_ok=True)
    state = load_state()
    ok, skipped, failed = sync("", state, args.all)
    save_state(state)
    print(f"\nDone: {ok} downloaded, {skipped} up to date, {failed} failed -> {OUT_DIR}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
