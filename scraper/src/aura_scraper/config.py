"""Scraper configuration.

The Cloudflare bypass server is the ONLY way to read html from a challenged
host (alphacoders.com sits behind a managed JS challenge since 2026-11-20 -
every plain http client gets 403 + a "Just a moment..." page).

The bypass server is a separate local process
(`C:\\Users\\Admin\\Downloads\\CloudflareBypassForScraping`, see
`automata-private/cloudflare.com/AGENTS.md`). It is NOT started implicitly:
if it is not running the scraper fails LOUDLY with the exact command to start
it, instead of silently degrading to plain requests (rule 8).
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path

DEFAULT_BYPASS_URL = "http://127.0.0.1:8000"
DEFAULT_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)

# The repo root is scraper/ -> parent
REPO_ROOT = Path(__file__).resolve().parents[3]
DATA_ROOT = REPO_ROOT / "scraper" / "data"


@dataclass
class Settings:
    bypass_base_url: str = DEFAULT_BYPASS_URL
    user_agent: str = DEFAULT_USER_AGENT
    data_root: Path = field(default_factory=lambda: DATA_ROOT)
    timeout: float = 45.0
    # how many times a failing fetch is retried (same method, not a fallback)
    retries: int = 2

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(
            bypass_base_url=os.environ.get(
                "AURA_BYPASS_URL", DEFAULT_BYPASS_URL
            ).rstrip("/"),
            user_agent=os.environ.get("AURA_USER_AGENT", DEFAULT_USER_AGENT),
            data_root=Path(
                os.environ.get("AURA_SCRAPER_DATA", str(DATA_ROOT))
            ),
        )
