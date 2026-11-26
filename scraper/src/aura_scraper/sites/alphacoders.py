"""alphacoders.com - live scrape through the Cloudflare bypass server.

Why this exists (verified 2026-11-26):

- alphacoders.com sits behind a Cloudflare managed JS challenge since
  ~2026-11-20. curl, HttpClient and every plain http client get HTTP 403 +
  a "Just a moment..." page. The app therefore fell back to a pinned
  web.archive.org snapshot, which is stale AND hands out html wrappers
  instead of images (the app's "Image cannot be loaded" red line).
- the CF bypass server (a patched stealth Chromium) reads the live site
  fine: `GET /html?url=...` returned the real category index (20 rows) and
  a real browse page (15 items) in ~9s.

Markup (verified live on https://alphacoders.com/nature-wallpapers):

    <div id="content_3597480" class="thumb-container-wallpaper-desktop" ...>
      <meta itemprop="contentUrl"   content="https://images8.alphacoders.com/136/1363709.png">
      <meta itemprop="url"          content="https://wall.alphacoders.com/big.php?i=1363709">
      <meta itemprop="name"         content="Firewatch Sunset Nature Mountain HD Wallpaper">
      <img class="thumb" src="https://images8.alphacoders.com/136/thumbbig-1363709.webp">

The image CDN (images*.alphacoders.com) is NOT challenged - plain requests
get the real bytes - so assets are fetched directly, html through the bypass.

Item ids: the container id is the API's `content_id`; the image id (the
number in the cdn path) is kept as `image_id` because the cdn urls and the
initiate download url are derived from it.
"""

from __future__ import annotations

import re
from urllib.parse import urlparse

from bs4 import BeautifulSoup

from ..errors import ParseError
from ..models import Wallpaper
from .base import Category, Site

CATEGORY_INDEX_URL = "https://alphacoders.com/tag/is-category"
BROWSE_URL = "https://alphacoders.com/{slug}-wallpapers?page={page}"

# the grid thumb is the `img.thumb` src; these are the formats the app can
# decode (webp included - the app re-encodes with ImageSharp).
_IMAGE_SUFFIXES = (".jpg", ".jpeg", ".png", ".webp")
_VIDEO_SUFFIXES = (".mp4", ".webm", ".mov", ".m4v")


class AlphaCodersSite(Site):
    name = "alphacoders"
    html_transport = "bypass"
    referer = "https://wall.alphacoders.com/"

    def categories(self) -> list[Category]:
        """The site's own category index, live (the API has no category list)."""
        categories: list[Category] = []
        seen: set[str] = set()

        # the index paginates: page 1 = 20 rows, page 2 = 4 more, page 3 = 0
        for page in range(1, 11):
            html = self.html(f"{CATEGORY_INDEX_URL}?page={page}")
            soup = BeautifulSoup(html, "html.parser")
            rows = soup.select("h3")
            if not rows:
                break

            new_rows = 0
            for row in rows:
                link = row.find("a", href=True)
                if not link:
                    continue
                href = link["href"]
                match = re.match(
                    r"^https://alphacoders\.com/([a-z0-9-]+)$", href
                )
                if not match:
                    continue
                slug = match.group(1)
                if slug in seen:
                    continue
                seen.add(slug)
                new_rows += 1
                name = link.get_text(" ", strip=True)
                categories.append(
                    Category(key=slug, name=name or slug.replace("-", " ").title())
                )

            if new_rows == 0:
                break

        if not categories:
            raise ParseError(
                f"{CATEGORY_INDEX_URL} parsed to 0 category rows - the markup "
                f"changed (the bypass returned html, but not the index)."
            )
        return categories

    def listings(self, category: Category, page: int) -> list[Wallpaper]:
        url = BROWSE_URL.format(slug=category.key, page=page)
        html = self.html(url)
        soup = BeautifulSoup(html, "html.parser")

        containers = soup.select("div.thumb-container-wallpaper-desktop")
        if not containers:
            raise ParseError(
                f"{url} parsed to 0 item containers - the browse markup changed "
                f"(expected div.thumb-container-wallpaper-desktop)."
            )

        wallpapers: list[Wallpaper] = []
        for container in containers:
            wallpaper = self._parse_item(container, category)
            if wallpaper is not None:
                wallpapers.append(wallpaper)
        return wallpapers

    def _parse_item(self, container, category: Category) -> Wallpaper | None:
        content_id = (container.get("id") or "").replace("content_", "")
        if not content_id:
            return None

        def meta(name: str) -> str:
            tag = container.find("meta", attrs={"itemprop": name})
            return (tag.get("content") or "").strip() if tag else ""

        content_url = meta("contentUrl")
        name = meta("name")
        page_url = meta("url")

        image = container.find("img", class_="thumb")
        thumb_url = (image.get("src") if image else "") or meta("thumbnailUrl")
        if not thumb_url:
            return None

        # an animated item (some categories serve mp4/webm/gif as the thumb)
        # has no image to show - the caller records it as a loud failure.
        if thumb_url.lower().endswith(_VIDEO_SUFFIXES):
            if content_url and content_url.lower().endswith(_IMAGE_SUFFIXES):
                thumb_url = content_url
            else:
                return None

        full_url = self._derive_1920(content_url) or content_url
        image_id = self._image_id(content_url) or self._image_id(thumb_url)

        return Wallpaper(
            site=self.name,
            category=category.key,
            item_id=content_id,
            title=name or f"AlphaCoders {content_id}",
            thumb_url=thumb_url,
            full_url=full_url,
            source_url=page_url,
            extra={
                "image_id": image_id,
                "original_url": content_url,
                "category_name": category.name,
            },
        )

    @staticmethod
    def _image_id(url: str) -> str:
        if not url:
            return ""
        name = urlparse(url).path.rsplit("/", 1)[-1]
        match = re.search(r"(\d+)\.[a-z0-9]+$", name)
        return match.group(1) if match else ""

    @staticmethod
    def _derive_1920(content_url: str) -> str:
        """.../136/1363709.png -> .../136/thumb-1920-1363709.png (verified live)."""
        if not content_url:
            return ""
        parsed = urlparse(content_url)
        folder = parsed.path.rsplit("/", 1)[0]
        name = parsed.path.rsplit("/", 1)[-1]
        match = re.match(r"^(?P<id>\d+)\.(?P<ext>[a-z0-9]+)$", name)
        if not match:
            return ""
        return (
            f"{parsed.scheme}://{parsed.netloc}{folder}"
            f"/thumb-1920-{match.group('id')}.{match.group('ext')}"
        )
