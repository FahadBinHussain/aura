# aura scraper

A real scraper for the Aura wallpaper app. The app used to hotlink every
site live from the UI thread; that breaks in three different ways, and the
app's error bar was the only thing telling you about it:

| what breaks | how it looked in the app |
| --- | --- |
| alphacoders.com sits behind a Cloudflare JS challenge | `alphacoders categories are from a web.archive.org snapshot (15 Jun 2026)` + `Alpha Coders category thumbnails: 1/1 failed - Image cannot be loaded. Available decoders: ...` |
| desktopnexus.com was redesigned | `DesktopNexus category thumbnails: 7/7 failed - no items returned for "All".` |
| wallpapercave.com lost its dns record | `Wallpaper Cave category thumbnails: 7/7 failed - No such host is known. (wallpapercave.com:443)` |

This package replaces "fetch it live from the UI" with an offline pipeline:
html through the local Cloudflare bypass server, assets direct, every image
validated and normalized to JPEG, everything written to a dataset the app
can read.

## how it works

```
site html  --cf-bypass-->  parse  -->  asset urls  --direct-->  validate  -->  jpeg  -->  dataset
```

- **cf-bypass**: `alphacoders.com` answers every non-browser client with
  HTTP 403 + a "Just a moment..." page. The bypass server (a patched
  stealth Chromium, `C:\Users\Admin\Downloads\CloudflareBypassForScraping`)
  solves the challenge and returns the real html. It is a separate process
  and is never started implicitly: if it is down the scraper fails loudly
  with the exact command to start it.
- **direct**: image CDNs (`images*.alphacoders.com`, `assets.desktopnexus.com`)
  are not challenged, so they are fetched with plain requests.
- **validate + normalize**: the bytes must be an image (an html error page
  raises with the url and the first bytes), and they are re-encoded to JPEG
  so the app never meets a format it cannot decode (it has no AVIF decoder,
  and several sites serve AVIF to a browser Accept header).

## setup

```powershell
cd C:\Users\Admin\Downloads\aura
uv venv .venv-scraper --python 3.12
uv pip install --python .venv-scraper\Scripts\python.exe -e scraper
```

The bypass server (once per session):

```powershell
Start-Process .venv\Scripts\python.exe -ArgumentList 'server.py','--host','127.0.0.1','--port','8000' `
  -WorkingDirectory C:\Users\Admin\Downloads\CloudflareBypassForScraping
```

## usage

```powershell
$py = "C:\Users\Admin\Downloads\aura\.venv-scraper\Scripts\python.exe"

& $py -m aura_scraper.cli doctor                              # every site, with the reason when it fails
& $py -m aura_scraper.cli categories alphacoders              # the site's own category list
& $py -m aura_scraper.cli crawl alphacoders --pages 2         # scrape into scraper/data/alphacoders/
& $py -m aura_scraper.cli crawl desktopnexus --category nature --full
& $py -m aura_scraper.cli index alphacoders                   # what the dataset holds
& $py -m aura_scraper.cli install                             # point the INSTALLED app at this dataset
```

## how the app finds the dataset

the app resolves its dataset root in this order:

1. the `AURA_SCRAPER_DATA` environment variable,
2. `<repo>/scraper/data` (a dev checkout),
3. `%LOCALAPPDATA%\Aura\dataset` (the installed app).

`aura-scraper install` links (or copies) the dataset into the third
location, so the installed build reads the same files the dev checkout
does. The app treats the dataset as the PRIMARY source and falls back to
the live network path with its loud labels (rule 8).

## the dataset

```
scraper/data/<site>/
  thumbs/<category>/<item_id>.jpg
  full/<category>/<item_id>.jpg
  index.json
```

`index.json` is the contract:

```json
{
  "site": "alphacoders",
  "generated_at": "2026-11-26T10:12:33+06:00",
  "categories": ["nature", "comic"],
  "item_count": 30,
  "failures": [],
  "items": [
    {
      "site": "alphacoders",
      "category": "nature",
      "item_id": "3597480",
      "title": "Firewatch Sunset Nature Mountain HD Wallpaper",
      "thumb": "thumbs/nature/3597480.jpg",
      "full": "full/nature/3597480.jpg",
      "source_url": "https://wall.alphacoders.com/big.php?i=1363709",
      "width": 600,
      "height": 375,
      "bytes": 69784
    }
  ]
}
```

`failures` is part of the contract: a crawl that saved 40 of 60 items says
so in the index, it never pretends it saved 60 (rule 8).

## sites

| site | html transport | state (2026-11-26) |
| --- | --- | --- |
| alphacoders | cf-bypass | 24 categories live, browse pages parse, assets direct |
| desktopnexus | direct | 16 galleries, cursor pagination, imgix assets forced to jpeg |
| wallpapercave | - | the domain is gone; the adapter fails loudly on purpose |

## adding a site

1. subclass `Site` in `src/aura_scraper/sites/`
2. implement `categories()` and `listings(category, page)`
3. register it in `src/aura_scraper/sites/__init__.py`
4. `aura-scraper doctor` - it must print OK, or the exact reason it does not
