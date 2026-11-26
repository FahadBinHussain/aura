"""wallpapercave.com - the domain is gone (kept here as a loud tombstone).

Verified 2026-11-26:

- `wallpapercave.com` has NO dns record any more (`Resolve-DnsName` ->
  "DNS name does not exist"). The app's fetch therefore died with
  "No such host is known. (wallpapercave.com:443)" and every one of its
  cards failed.
- `papercave.com` / `www.papercave.com` DO resolve, but they are a parked
  lander: the body is a one-line script that redirects to "/lander". It is
  not the wallpaper site.

There is nothing to scrape, so this adapter fails loudly instead of
pretending. The app must drop the platform (its picker row and its
category cards) rather than keep showing a dead site's cards.
"""

from __future__ import annotations

from ..errors import SiteGone
from .base import Category, Site


class WallpaperCaveSite(Site):
    name = "wallpapercave"
    html_transport = "direct"

    def categories(self) -> list[Category]:
        raise SiteGone(
            "wallpapercave.com no longer resolves and papercave.com is a "
            "parked lander (verified 2026-11-26) - the site is gone, drop the "
            "platform instead of scraping it."
        )

    def listings(self, category: Category, page: int) -> list:
        raise SiteGone(
            "wallpapercave.com no longer resolves and papercave.com is a "
            "parked lander (verified 2026-11-26) - the site is gone, drop the "
            "platform instead of scraping it."
        )
