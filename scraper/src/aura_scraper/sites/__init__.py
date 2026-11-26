"""Site registry - one place that knows every site the scraper can drive."""

from __future__ import annotations

from .alphacoders import AlphaCodersSite
from .base import Category, Site
from .desktopnexus import DesktopNexusSite
from .wallpapercave import WallpaperCaveSite

SITES: dict[str, type[Site]] = {
    AlphaCodersSite.name: AlphaCodersSite,
    DesktopNexusSite.name: DesktopNexusSite,
    WallpaperCaveSite.name: WallpaperCaveSite,
}


def get_site(name: str, settings) -> Site:
    if name not in SITES:
        raise KeyError(
            f"unknown site '{name}' - known sites: {', '.join(sorted(SITES))}"
        )
    return SITES[name](settings)


__all__ = [
    "SITES",
    "AlphaCodersSite",
    "Category",
    "DesktopNexusSite",
    "Site",
    "WallpaperCaveSite",
    "get_site",
]
