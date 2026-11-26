"""aura-scraper - a real scraper for the Aura wallpaper app.

The app used to hotlink every site live from the UI thread. That breaks in
three different ways, all of them loud in the UI:

1. a Cloudflare JS challenge (alphacoders) - no http client can pass it,
2. a site redesign (desktopnexus) - the parser matches nothing,
3. a dead domain (wallpapercave) - the host does not resolve.

This package reads html through the local Cloudflare bypass server and
fetches the assets directly, then normalizes every image to JPEG so the
app always gets something it can decode.
"""

__version__ = "0.1.0"

__all__ = ["__version__"]
