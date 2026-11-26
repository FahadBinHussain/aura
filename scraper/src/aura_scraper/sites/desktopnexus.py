"""desktopnexus.com - the 2026 redesign, re-reversed.

Why this exists (verified 2026-11-26): the old parser matched
`<a href="https://<sub>.desktopnexus.com/wallpaper/<digits>/" alt="...">` +
an `assets.desktopnexus.com/<uuid>/thumbnail` image. The site was rebuilt
and ALL of that changed, so the app's fetcher returned 0 items and every
card failed with `no items returned for "All"`.

What the live site looks like now:

- the gallery is the SUBDOMAIN ROOT: `https://nature.desktopnexus.com/all/`
  redirects to `https://nature.desktopnexus.com/` (the /all/ path is gone).
- the category catalog is still `https://www.desktopnexus.com/galleries/`
  and lists every subdomain (www + 14 galleries).
- items:
      <article class="wallpaper-card">
        <a class="wallpaper-card__image" href="https://nature.desktopnexus.com/wallpaper/s58qV0z/">
          <img src="https://assets.desktopnexus.com/<uuid>/trim=124;0;124;0,w=450,h=311,fit=cover,quality=85,format=auto">
  - item ids are SLUGS now (`s58qV0z`), not numbers.
- the assets are imgix urls: `format=auto` serves AVIF to a client that
  offers `image/avif` (verified: 200 image/avif with a browser Accept
  header) and the app has NO avif decoder - so the scraper forces
  `format=jpg` on every asset url. `/original` still serves the full jpeg.
- pagination is CURSOR based: the grid carries `data-next-cursor` and the
  next page is `https://<sub>.desktopnexus.com/?cursor=<base64 json>`
  (verified: 48 items per page, page 2 disjoint from page 1).
"""

from __future__ import annotations

import re
from urllib.parse import urlparse, parse_qs

from bs4 import BeautifulSoup

from ..errors import ParseError
from ..models import Wallpaper
from .base import Category, Site

GALLERIES_URL = "https://www.desktopnexus.com/galleries/"


class DesktopNexusSite(Site):
    name = "desktopnexus"
    html_transport = "direct"
    referer = "https://www.desktopnexus.com/"

    def categories(self) -> list[Category]:
        html = self.html(GALLERIES_URL)
        soup = BeautifulSoup(html, "html.parser")

        categories: list[Category] = []
        seen: set[str] = set()
        for link in soup.select("a[href]"):
            href = link.get("href", "")
            match = re.match(
                r"^https://(?P<sub>[a-z0-9]+)\.desktopnexus\.com/?$", href
            )
            if not match:
                continue
            subdomain = match.group("sub")
            if subdomain in seen:
                continue
            seen.add(subdomain)
            categories.append(
                Category(
                    key=subdomain,
                    name=self._display_name(subdomain, link),
                )
            )

        if not categories:
            raise ParseError(
                f"{GALLERIES_URL} parsed to 0 galleries - the catalog markup changed."
            )
        return categories

    def listings(self, category: Category, page: int) -> list[Wallpaper]:
        # the gallery is the subdomain ROOT - except the "www" gallery, whose
        # listing lives under /all/ (it 302s to /all/freshness/); the home
        # page itself carries no cards (verified live 2026-11-26).
        listing_path = "/all/freshness/" if category.key == "www" else "/"

        if page <= 1:
            url = f"https://{category.key}.desktopnexus.com{listing_path}"
        else:
            url = self._cursor_url(category, page, listing_path)

        html = self.html(url)
        soup = BeautifulSoup(html, "html.parser")
        articles = soup.select("article.wallpaper-card")
        if not articles:
            raise ParseError(
                f"{url} parsed to 0 wallpaper cards - the gallery markup changed "
                f"(expected article.wallpaper-card)."
            )

        wallpapers: list[Wallpaper] = []
        for article in articles:
            link = article.select_one("a.wallpaper-card__image")
            image = article.find("img")
            if not link or not image:
                continue
            href = link.get("href", "")
            source_url = link.get("href", "")
            if not href:
                continue

            item_id = href.rstrip("/").rsplit("/", 1)[-1]
            title = (
                image.get("alt")
                or link.get("aria-label", "").replace("View ", "")
                or item_id
            )

            asset = self._force_jpeg(image.get("src", ""))
            if not asset:
                continue

            full_url = self._original_url(asset)
            subdomain = urlparse(source_url).netloc.split(".")[0]

            wallpapers.append(
                Wallpaper(
                    site=self.name,
                    category=category.key,
                    item_id=item_id,
                    title=title,
                    thumb_url=asset,
                    full_url=full_url,
                    source_url=source_url,
                    extra={"subdomain": subdomain},
                )
            )
        return wallpapers

    def _cursor_url(self, category: Category, page: int, listing_path: str) -> str:
        """Follow `data-next-cursor` until the requested page is reached."""
        url = f"https://{category.key}.desktopnexus.com{listing_path}"
        for _ in range(page - 1):
            html = self.html(url)
            soup = BeautifulSoup(html, "html.parser")
            root = soup.select_one("[data-next-cursor]")
            if not root:
                raise ParseError(
                    f"{url} has no data-next-cursor - the gallery pagination "
                    f"changed (page {page} unreachable)."
                )
            cursor = root.get("data-next-cursor")
            url = (
                f"https://{category.key}.desktopnexus.com{listing_path}"
                f"?cursor={cursor}"
            )
        return url

    @staticmethod
    def _display_name(subdomain: str, link) -> str:
        """The gallery's clean title.

        the catalog links carry the wallpaper count with the name
        ("Nature 405.6k wallpapers"), so the <strong> label is preferred and
        the count is dropped. the www gallery is the app's "All".
        """
        if subdomain == "www":
            return "All"

        strong = link.find("strong")
        if strong is not None:
            text = strong.get_text(" ", strip=True)
            if text:
                return text

        text = link.get_text(" ", strip=True)
        text = re.split(r"\d[\d.,]*\s*[kmb]?\s*wallpapers", text, flags=re.I)[0]
        return text.strip() or subdomain.replace("-", " ").title()

    @staticmethod
    def _force_jpeg(url: str) -> str:
        """imgix `format=auto` serves AVIF to browsers - force jpeg."""
        if not url:
            return ""
        if "format=auto" in url:
            return url.replace("format=auto", "format=jpg")
        return url

    @staticmethod
    def _original_url(asset_url: str) -> str:
        """.../<uuid>/<imgix params> -> .../<uuid>/original (the full jpeg)."""
        parsed = urlparse(asset_url)
        folder = parsed.path.rsplit("/", 1)[0]
        return f"{parsed.scheme}://{parsed.netloc}{folder}/original"
