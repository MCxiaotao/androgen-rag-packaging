from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--bundle", required=True)
    parser.add_argument("--url", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--channel", default="stable")
    parser.add_argument("--app-id", default="AndrogenRAG")
    parser.add_argument("--min-launcher-version", default="1.0.0")
    parser.add_argument("--notes", default="")
    parser.add_argument("--delta-base-version", default="")
    parser.add_argument("--delta-url", default="")
    parser.add_argument("--delta-bundle", default="")
    args = parser.parse_args()

    bundle = Path(args.bundle).resolve()
    payload = {
        "app_id": args.app_id,
        "channel": args.channel,
        "version": args.version,
        "pub_date": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "notes": args.notes,
        "min_launcher_version": args.min_launcher_version,
        "windows": {
            "arch": "x64",
            "url": args.url,
            "sha256": sha256_of(bundle),
            "size": bundle.stat().st_size,
        },
    }

    if args.delta_base_version and args.delta_url and args.delta_bundle:
        delta_bundle = Path(args.delta_bundle).resolve()
        payload["windows"]["delta"] = {
            "base_version": args.delta_base_version,
            "algorithm": "placeholder",
            "url": args.delta_url,
            "sha256": sha256_of(delta_bundle),
            "size": delta_bundle.stat().st_size,
        }

    out_path = Path(args.out).resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
