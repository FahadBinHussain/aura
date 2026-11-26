"""The one row shape every site maps into."""

from __future__ import annotations

from dataclasses import dataclass, field, asdict


@dataclass
class Wallpaper:
    site: str
    category: str
    item_id: str
    title: str
    thumb_url: str
    full_url: str = ""
    source_url: str = ""
    width: int | None = None
    height: int | None = None
    extra: dict = field(default_factory=dict)

    def as_dict(self) -> dict:
        return asdict(self)
