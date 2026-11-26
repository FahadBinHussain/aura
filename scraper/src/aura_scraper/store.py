"""The dataset on disk.

Layout (everything under `scraper/data/`):

    <site>/
      thumbs/<category>/<item_id>.jpg
      full/<category>/<item_id>.jpg
      index.json

`index.json` is the contract the app (or an api in front of it) reads:

    {
      "site": "alphacoders",
      "generated_at": "2026-11-26T10:12:33+06:00",
      "categories": ["nature", "anime", ...],
      "items": [
        {
          "site": "alphacoders",
          "category": "nature",
          "item_id": "3597480",
          "title": "Firewatch Sunset Nature Mountain HD Wallpaper",
          "thumb": "thumbs/nature/3597480.jpg",
          "full": "full/nature/3597480.jpg",
          "source_url": "https://wall.alphacoders.com/wallpaper.php?i=3597480",
          "width": 3840, "height": 2160,
          "bytes": 120433
        }
      ]
    }

Files are written atomically (temp file + replace) so a crashed run never
leaves a half written index behind.
"""

from __future__ import annotations

import json
import os
import tempfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from .models import Wallpaper


@dataclass
class StoredItem:
    wallpaper: Wallpaper
    thumb_path: str
    full_path: str
    bytes: int
    width: int | None
    height: int | None


class DatasetStore:
    def __init__(self, data_root: Path):
        self.data_root = Path(data_root)

    def site_root(self, site: str) -> Path:
        return self.data_root / site

    def thumb_path(self, site: str, category: str, item_id: str) -> Path:
        return self.site_root(site) / "thumbs" / category / f"{item_id}.jpg"

    def full_path(self, site: str, category: str, item_id: str) -> Path:
        return self.site_root(site) / "full" / category / f"{item_id}.jpg"

    def has_thumb(self, site: str, category: str, item_id: str) -> bool:
        return self.thumb_path(site, category, item_id).exists()

    def write_index(
        self,
        site: str,
        items: list[StoredItem],
        categories: list[dict],
        failures: list[dict] | None = None,
    ) -> Path:
        index_path = self.site_root(site) / "index.json"
        index_path.parent.mkdir(parents=True, exist_ok=True)

        # MERGE with what is already on disk: crawling ONE category must not
        # wipe the other categories from the index (the app reads the index as
        # the site's whole catalogue). the items of the categories this run
        # touched are replaced, the rest are kept as they are.
        previous = self.load_index(site) or {}
        touched = {str(c.get("key", "")) for c in categories}
        kept_items = [
            item
            for item in previous.get("items", [])
            if str(item.get("category", "")) not in touched
        ]
        kept_categories = [
            c
            for c in previous.get("categories", [])
            if isinstance(c, dict) and str(c.get("key", "")) not in touched
        ]
        kept_failures = [
            f
            for f in previous.get("failures", [])
            if str(f.get("category", "")) not in touched
        ]

        payload = {
            "site": site,
            "generated_at": datetime.now(timezone.utc).astimezone().isoformat(),
            "categories": kept_categories + list(categories),
            "item_count": len(kept_items) + len(items),
            "failures": kept_failures + list(failures or []),
            "items": kept_items
            + [
                {
                    "site": item.wallpaper.site,
                    "category": item.wallpaper.category,
                    "item_id": item.wallpaper.item_id,
                    "title": item.wallpaper.title,
                    "thumb": item.thumb_path,
                    "full": item.full_path,
                    "source_url": item.wallpaper.source_url,
                    "width": item.width,
                    "height": item.height,
                    "bytes": item.bytes,
                }
                for item in items
            ],
        }

        _atomic_write_text(
            index_path, json.dumps(payload, indent=2, ensure_ascii=False)
        )
        return index_path

    def load_index(self, site: str) -> dict | None:
        index_path = self.site_root(site) / "index.json"
        if not index_path.exists():
            return None
        return json.loads(index_path.read_text(encoding="utf-8"))


def _atomic_write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temp_name = tempfile.mkstemp(
        dir=str(path.parent), prefix=path.name, suffix=".tmp"
    )
    try:
        with os.fdopen(handle, "w", encoding="utf-8") as stream:
            stream.write(text)
        os.replace(temp_name, path)
    except BaseException:
        Path(temp_name).unlink(missing_ok=True)
        raise
