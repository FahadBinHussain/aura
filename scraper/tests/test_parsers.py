"""Parser tests - the markup is the thing that breaks, so it gets a fixture.

Run:
    .venv-scraper\\Scripts\\python.exe -m unittest discover -s scraper\\tests -v
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(
    0, str(Path(__file__).resolve().parents[1] / "src")
)

from aura_scraper.images import sniff_format  # noqa: E402
from aura_scraper.sites.alphacoders import AlphaCodersSite  # noqa: E402
from aura_scraper.sites.desktopnexus import DesktopNexusSite  # noqa: E402

FIXTURES = Path(__file__).resolve().parent / "fixtures"


class _OfflineSite:
    """Mixin: serve a fixture instead of the network."""

    fixture = ""

    def html(self, url: str) -> str:
        return (FIXTURES / self.fixture).read_text(encoding="utf-8")


class AlphaCodersParserTest(_OfflineSite, unittest.TestCase):
    fixture = "alphacoders_listing.html"

    def setUp(self) -> None:
        self.site = AlphaCodersSite.__new__(AlphaCodersSite)
        self.site.name = "alphacoders"
        self.site.html_transport = "bypass"
        self.site.referer = "https://wall.alphacoders.com/"
        self.site.html = lambda url: self.fixture_html(url)  # type: ignore[method-assign]

    def fixture_html(self, url: str) -> str:
        return (FIXTURES / self.fixture).read_text(encoding="utf-8")

    def test_parses_items(self) -> None:
        from aura_scraper.sites.base import Category

        items = self.site.listings(Category(key="nature", name="Nature"), 1)
        self.assertEqual(len(items), 1, "the animated item must be rejected")
        item = items[0]
        self.assertEqual(item.item_id, "3597480")
        self.assertEqual(item.title, "Firewatch Sunset Nature Mountain HD Wallpaper")
        self.assertEqual(
            item.thumb_url, "https://images8.alphacoders.com/136/thumbbig-1363709.webp"
        )
        self.assertEqual(
            item.full_url, "https://images8.alphacoders.com/136/thumb-1920-1363709.png"
        )
        self.assertEqual(
            item.source_url, "https://wall.alphacoders.com/big.php?i=1363709"
        )
        self.assertEqual(item.extra["image_id"], "1363709")


class AlphaCodersCategoryTest(_OfflineSite, unittest.TestCase):
    fixture = "alphacoders_category_index.html"

    def test_parses_categories(self) -> None:
        site = AlphaCodersSite.__new__(AlphaCodersSite)
        site.name = "alphacoders"
        site.html_transport = "bypass"
        site.html = lambda url: (FIXTURES / self.fixture).read_text(encoding="utf-8")
        categories = site.categories()
        keys = [c.key for c in categories]
        self.assertIn("anime", keys)
        self.assertIn("video-game", keys)
        self.assertNotIn("anime-phone-wallpapers", keys)


class DesktopNexusParserTest(_OfflineSite, unittest.TestCase):
    fixture = "desktopnexus_gallery.html"

    def setUp(self) -> None:
        self.site = DesktopNexusSite.__new__(DesktopNexusSite)
        self.site.name = "desktopnexus"
        self.site.html_transport = "direct"
        self.site.referer = "https://www.desktopnexus.com/"
        self.site.html = lambda url: (FIXTURES / self.fixture).read_text(encoding="utf-8")

    def test_parses_slug_ids_and_forces_jpeg(self) -> None:
        from aura_scraper.sites.base import Category

        items = self.site.listings(Category(key="nature", name="Nature"), 1)
        self.assertEqual(len(items), 2)
        first = items[0]
        self.assertEqual(first.item_id, "s58qV0z")
        self.assertEqual(first.title, "Aurora Borealis Over Waterfall")
        self.assertNotIn(
            "format=auto", first.thumb_url, "format=auto serves AVIF to browsers"
        )
        self.assertIn("format=jpg", first.thumb_url)
        self.assertEqual(
            first.full_url,
            "https://assets.desktopnexus.com/d91f2cb5-7e2a-45a5-7ca6-560f8fd20c00/original",
        )


class DesktopNexusCategoriesTest(_OfflineSite, unittest.TestCase):
    fixture = "desktopnexus_galleries.html"

    def test_parses_galleries(self) -> None:
        site = DesktopNexusSite.__new__(DesktopNexusSite)
        site.name = "desktopnexus"
        site.html_transport = "direct"
        site.html = lambda url: (FIXTURES / self.fixture).read_text(encoding="utf-8")
        categories = site.categories()
        keys = [c.key for c in categories]
        self.assertIn("nature", keys)
        self.assertIn("www", keys)


class SniffTest(unittest.TestCase):
    def test_detects_html(self) -> None:
        self.assertEqual(sniff_format(b"<!DOCTYPE html><html>"), "html")

    def test_detects_mp4(self) -> None:
        self.assertEqual(sniff_format(b"\x00\x00\x00\x18ftypmp42"), "mp4")

    def test_detects_png(self) -> None:
        self.assertEqual(sniff_format(b"\x89PNG\r\n\x1a\n"), "png")

    def test_detects_jpeg(self) -> None:
        self.assertEqual(sniff_format(b"\xff\xd8\xff\xe0"), "jpeg")

    def test_detects_avif(self) -> None:
        self.assertEqual(sniff_format(b"\x00\x00\x00\x20ftypavif"), "avif")

    def test_detects_webp(self) -> None:
        self.assertEqual(sniff_format(b"RIFF\x00\x00\x00\x00WEBPVP8 "), "webp")


if __name__ == "__main__":
    unittest.main()
