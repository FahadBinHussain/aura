"""Scraper errors that always carry the reason (rule 8: no silent degradation).

Every failure in this package raises a `ScraperError` with a message that
names WHAT failed, WHICH url/host, and WHAT the operator has to do about it.
Nothing here ever returns an empty result to hide a failure.
"""


class ScraperError(Exception):
    """Base class: every scraper failure is loud and self-explaining."""


class BypassUnavailable(ScraperError):
    """The local Cloudflare bypass server is not reachable."""


class SiteGone(ScraperError):
    """The site's domain no longer resolves / the site was shut down."""


class Blocked(ScraperError):
    """The site answered with an anti-bot challenge instead of content."""


class NotAnImage(ScraperError):
    """The bytes fetched from an image url are not an image (html error page)."""


class ParseError(ScraperError):
    """The markup changed: the parser found nothing it recognizes."""


class TransportError(ScraperError):
    """A plain network failure (dns, tls, timeout, http status)."""
