"""Site adapters: every site maps its own markup into Wallpaper rows."""

from __future__ import annotations

from dataclasses import dataclass

from ..config import Settings
from ..models import Wallpaper
from ..transport import BypassTransport, DirectTransport


@dataclass(frozen=True)
class Category:
    key: str
    name: str


class Site:
    """Base class.

    `bypass` reads html (only transport that survives a JS challenge),
    `direct` fetches plain image assets.
    """

    name: str = "site"
    # html transport: "bypass" = challenged host, "direct" = plain host
    html_transport: str = "direct"
    referer: str = ""

    def __init__(self, settings: Settings):
        self.settings = settings
        self.bypass = BypassTransport(settings)
        self.direct = DirectTransport(settings)

    def html(self, url: str) -> str:
        if self.html_transport == "bypass":
            return self.bypass.get_text(url)
        return self.direct.get_text(url)

    def categories(self) -> list[Category]:  # pragma: no cover
        raise NotImplementedError

    def listings(self, category: Category, page: int) -> list[Wallpaper]:  # pragma: no cover
        raise NotImplementedError
