"""The crawl loop: listings -> validated, normalized images -> index.json.

The loud contract (rule 8): every failure is counted, named and written
into the dataset's index.json under "failures" - a crawl that saved 40 of
60 items says so, it does not pretend it saved 60.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from .images import fetch_image, save_jpeg
from .models import Wallpaper
from .sites.base import Category, Site
from .store import DatasetStore, StoredItem


@dataclass
class CrawlReport:
    site: str
    items: list[StoredItem] = field(default_factory=list)
    failures: list[dict] = field(default_factory=list)
    categories: list[str] = field(default_factory=list)

    @property
    def ok_count(self) -> int:
        return len(self.items)

    @property
    def fail_count(self) -> int:
        return len(self.failures)


def crawl_site(
    site: Site,
    store: DatasetStore,
    categories: list[Category],
    pages: int = 1,
    want_thumb: bool = True,
    want_full: bool = False,
    limit: int | None = None,
    skip_existing: bool = True,
) -> CrawlReport:
    report = CrawlReport(site=site.name)
    report.categories = [
        {"key": category.key, "name": category.name}
        for category in categories
    ]

    for category in categories:
        for page in range(1, pages + 1):
            try:
                wallpapers = site.listings(category, page)
            except Exception as exc:
                report.failures.append(
                    {
                        "stage": "listing",
                        "category": category.key,
                        "page": page,
                        "error": f"{type(exc).__name__}: {exc}",
                    }
                )
                continue

            if not wallpapers:
                report.failures.append(
                    {
                        "stage": "listing",
                        "category": category.key,
                        "page": page,
                        "error": "the listing returned 0 items",
                    }
                )
                continue

            for wallpaper in wallpapers:
                if limit is not None and len(report.items) >= limit:
                    break
                stored = _store_item(
                    site=site,
                    store=store,
                    wallpaper=wallpaper,
                    report=report,
                    want_thumb=want_thumb,
                    want_full=want_full,
                    skip_existing=skip_existing,
                )
                if stored is not None:
                    report.items.append(stored)

    return report


def _store_item(
    site: Site,
    store: DatasetStore,
    wallpaper: Wallpaper,
    report: CrawlReport,
    want_thumb: bool,
    want_full: bool,
    skip_existing: bool,
) -> StoredItem | None:
    thumb_path = store.thumb_path(site.name, wallpaper.category, wallpaper.item_id)
    full_path = store.full_path(site.name, wallpaper.category, wallpaper.item_id)

    if skip_existing and want_thumb and not want_full and thumb_path.exists():
        # already on disk: keep the index in sync without re-downloading
        return StoredItem(
            wallpaper=wallpaper,
            thumb_path=_relative(store, site, thumb_path),
            full_path=_relative(store, site, full_path),
            bytes=thumb_path.stat().st_size,
            width=None,
            height=None,
        )

    try:
        thumb_image = fetch_image(site.direct, wallpaper.thumb_url, site.referer)
    except Exception as exc:
        report.failures.append(
            {
                "stage": "thumb",
                "category": wallpaper.category,
                "item_id": wallpaper.item_id,
                "url": wallpaper.thumb_url,
                "error": f"{type(exc).__name__}: {exc}",
            }
        )
        return None

    size, width, height = save_jpeg(thumb_path, thumb_image)

    full_size = 0
    if want_full and wallpaper.full_url:
        try:
            full_image = fetch_image(
                site.direct, wallpaper.full_url, site.referer
            )
            full_size, _, _ = save_jpeg(full_path, full_image, max_width=2560)
        except Exception as exc:
            report.failures.append(
                {
                    "stage": "full",
                    "category": wallpaper.category,
                    "item_id": wallpaper.item_id,
                    "url": wallpaper.full_url,
                    "error": f"{type(exc).__name__}: {exc}",
                }
            )

    return StoredItem(
        wallpaper=wallpaper,
        thumb_path=_relative(store, site, thumb_path),
        full_path=_relative(store, site, full_path),
        bytes=size + full_size,
        width=width,
        height=height,
    )


def _relative(store: DatasetStore, site: Site, path: Path) -> str:
    """Index paths are relative to the SITE root, posix style (thumbs/cat/id.jpg)."""
    return path.relative_to(store.site_root(site.name)).as_posix()
