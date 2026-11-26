"""Transports: how bytes are fetched, and WHICH method fetched them.

Two transports, and they are NOT interchangeable:

- `BypassTransport` reads HTML through the local Cloudflare bypass server
  (a patched stealth Chromium that solves the JS challenge). It is the only
  method that gets real HTML from a challenged host.
- `DirectTransport` fetches plain assets (image CDNs). Those hosts are not
  challenged, and going through the browser for a 100 KB jpeg would be
  pointless and slow.

Rule 8: there is no "try direct, then try bypass" chain. Each site declares
which transport reads its HTML, and a failure raises - loudly, naming the
transport - instead of falling back to another method.
"""

from __future__ import annotations

import time
from urllib.parse import quote

import requests

from .config import Settings
from .errors import (
    BypassUnavailable,
    Blocked,
    ScraperError,
    TransportError,
)

# Cloudflare's managed challenge page: served with a 403 but the body is the
# real HTML challenge, so the status code alone is not the tell.
_CHALLENGE_MARKERS = (
    "just a moment",
    "cf-browser-verification",
    "challenge-platform",
    "cf_chl_opt",
    "attention required! | cloudflare",
)


class Transport:
    """Base transport. `name` shows up in every error message."""

    name = "transport"

    def __init__(self, settings: Settings):
        self.settings = settings

    def get_bytes(self, url: str, **kwargs) -> bytes:  # pragma: no cover
        raise NotImplementedError

    def get_text(self, url: str, **kwargs) -> str:
        return self.get_bytes(url, **kwargs).decode(
            "utf-8", errors="replace"
        )


class DirectTransport(Transport):
    """Plain HTTPS with a browser header set. Used for image assets."""

    name = "direct"

    def __init__(self, settings: Settings):
        super().__init__(settings)
        self._session = requests.Session()
        self._session.headers.update(
            {
                "User-Agent": settings.user_agent,
                "Accept-Language": "en-US,en;q=0.9",
            }
        )

    def get_bytes(self, url: str, referer: str | None = None, **_) -> bytes:
        headers = {}
        if referer:
            headers["Referer"] = referer
        try:
            response = self._session.get(
                url,
                headers=headers,
                timeout=self.settings.timeout,
                allow_redirects=True,
            )
        except requests.RequestException as exc:
            raise TransportError(f"{self.name} GET {url} failed: {exc}") from exc

        if response.status_code >= 400:
            raise TransportError(
                f"{self.name} GET {url} returned HTTP {response.status_code}"
                f" ({len(response.content)} bytes)"
            )
        return response.content


class BypassTransport(Transport):
    """Reads html through the local Cloudflare bypass server.

    GET {base}/html?url=<encoded> returns the page html after the challenge
    is solved, with the final url in the `x-cf-bypasser-final-url` header.
    """

    name = "cf-bypass"

    def __init__(self, settings: Settings):
        super().__init__(settings)
        self._session = requests.Session()

    def health(self) -> dict:
        """Probe /cache/stats - raises BypassUnavailable with the fix command."""
        try:
            response = self._session.get(
                f"{self.settings.bypass_base_url}/cache/stats", timeout=10
            )
            response.raise_for_status()
            return response.json()
        except requests.RequestException as exc:
            raise BypassUnavailable(
                f"the Cloudflare bypass server is not answering at "
                f"{self.settings.bypass_base_url} ({exc}). Start it with:\n"
                f"  Start-Process .venv\\Scripts\\python.exe -ArgumentList "
                f"'server.py','--host','127.0.0.1','--port','8000' "
                f"-WorkingDirectory C:\\Users\\Admin\\Downloads\\CloudflareBypassForScraping"
            ) from exc

    def get_bytes(self, url: str, **_) -> bytes:
        endpoint = (
            f"{self.settings.bypass_base_url}/html?url={quote(url, safe='')}"
        )
        last_error: Exception | None = None
        for attempt in range(self.settings.retries + 1):
            try:
                response = self._session.get(
                    endpoint, timeout=self.settings.timeout + 30
                )
                response.raise_for_status()
                return response.content
            except requests.RequestException as exc:
                last_error = exc
                if attempt < self.settings.retries:
                    time.sleep(2)
        raise BypassUnavailable(
            f"cf-bypass GET {url} failed after "
            f"{self.settings.retries + 1} attempts: {last_error}. "
            f"Check the bypass server log (C:\\tmp\\cfbypass.err.log)."
        )

    def get_text(self, url: str, **kwargs) -> str:
        html = super().get_text(url, **kwargs)
        lowered = html[:4000].lower()
        for marker in _CHALLENGE_MARKERS:
            if marker in lowered:
                raise Blocked(
                    f"cf-bypass GET {url} still returned a Cloudflare challenge"
                    f" page (marker '{marker}') - the bypass did not solve it."
                )
        return html
