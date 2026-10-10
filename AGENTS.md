# aura - project notes

## layout

- `web/` = the Next.js app (this is also the Vercel project's Root Directory)
- `winui/`, `wpf/` = desktop app variants; `.github/workflows/build.yml` builds
  ONLY the WinUI app (dotnet publish + GitHub release `latest` tag) - it does
  not touch the web app.
- `pnpm` is the package manager (`web/pnpm-lock.yaml` is tracked - dependabot
  watches it).

## security: next.js advisory GHSA-2xp9-vwfh-vxw4 (2026-09-06)

- **unauthenticated RCE in the image optimization API when AVIF files are
  optimized** (libheif/sharp), critical 9.5. affected: next `>=10 <15.5.24`
  and `>=16.0.0 <16.3.3`; patched: 15.5.24 / 16.3.3.
- this repo was affected: lockfile pinned `next@16.2.6`. it is RELEVANT here
  because `web/next.config.ts` enables the optimizer (`images.remotePatterns`
  -> picsum.photos), so the `/_next/image` endpoint is live on production.
- fix: bump `"next"` in `web/package.json` to `^16.3.3` (lockfile resolves to
  16.3.8 as of 2026-09-06), commit `5861ed2`. when upgrading next, the build
  regenerates `web/next-env.d.ts` - commit it with the bump.
- dependabot alert clears on the next scan after the lockfile push.

## backiee site reversing (2026-10-02)

- plain fetchers get 403; a browser UA works. in-app, `BackieeNetworkClient`
  (HttpClient UA `Mozilla/5.0 ... Aura/1.0` + curl fallback) is the only
  sanctioned fetch path - use it for all backiee URLs.
- category slugs are FLAT: `sitemap-categories.xml` lists exactly 19
  `/categories/<slug>` pages, no nested subcategory URLs.
- `api/wallpaper/list.php?action=paging_list&category=<slug>` filters
  correctly (`ThemeCat` matches the slug) - categories grid reuses this via
  `BackieeWallpaperSection.ForCategory()`.
- per-category "Popular <cat> searches" chips link to `/search/<term>` - this
  is the only subcategory-like level; search has NO api (`action=search` ->
  550, `args=` ignored), it is server-rendered HTML with `?page=N` pagination
  (rel=next, `data-pagination`), scraped by `BackieeHtmlParser`.
- list markup (both `/categories/<slug>` and `/search/<term>` pages):
  `a.wall-link[href=/wallpaper/<slug>/<id>]` > `.wall-body > h3` title,
  `picture > img.wall-cover[src]` thumb, `.flag-pill` quality badge,
  `.stat-pill` (heart, download) counts in that order.
- category cards on `/categories`: `a.category-card` > img
  `static/wallpapers/categories/<slug>.jpg` (url is derivable from slug),
  accent `--category-accent: #hex`, name in `h3`.
- chips on category pages: `a.detail-chip[href=/search/<term>]`.

## drill-down + infinite scroll, verified live (2026-10-06)

end-to-end on the installed CI build (UIA + vision, see automata
`windows-ui-automation`): Home -> `Categories` (19 cards) -> `Abstract`
(`Abstract wallpapers` title, 30-card grid, 13 labeled chips) -> `digital+art`
chip (24 search cards, orange active pill, server HTML `?page=N`).

- chips had a blank-label bug (WinUI: `Foreground = null` local value beats the
  theme style setter) - fixed in `ApplyChipStyle` via
  `ClearValue(Control.ForegroundProperty)`, commit `6fd48cb`.
- pagination (`CategoryWallpapersPage`): `MainScrollViewer.ViewChanged` ->
  `offset >= ScrollableHeight * 0.4` (`_loadMoreThreshold`) -> +30 items per
  server page (`_itemsPerPage = 30`; api `page=0..N` verified disjoint by
  curl). it also auto-fires when the window is restored/resized while near the
  bottom, and works with the window minimized.
- **load-more failure handling (fixed 2026-10-06)**: an exception in
  `LoadMoreWallpapers` used to set `_hasMoreItems = false` permanently and
  silently - the error line only rendered when the grid was empty - so ONE
  transient network failure killed scroll-load for the rest of the page
  session (observed live: first bottom-jump fetched nothing, no bar, no
  message; only re-navigation recovered). now a failed page keeps
  `_hasMoreItems` armed (retry happens on the next scroll/resize/restore near
  the bottom; no storm possible because `ViewChanged` only fires on real view
  changes) and ALWAYS shows `ErrorTextBlock` with the message, cleared on the
  next successful page. `_currentPage` only increments on success, so a retry
  re-requests the same page. `LatestWallpapersPage` had the identical catch
  with NO error UI at all - same contract there plus a new `ErrorTextBlock`
  overlay (same id/style as the category page). live-verified on the CI build
  (2026-10-06, hosts-blocked `backiee.com`): failed jump showed the error line
  at **60 items** within 2.4s (the old code showed nothing there), and after
  restoring the network the next bottom-jump retried the SAME page and
  cleared the line (60 -> 90, error absent). retry needs a REAL view change:
  a second jump from an already-bottom position is a no-op that fires no
  `ViewChanged` (scroll away + back / resize / window restore are the
  triggers).

## merged global/local categories, verified live (2026-10-06)

commit `544d27b` (CI run 37437947067 green, installed over the local build).
one Categories page merges every platform's categories behind a scope button;
everything below was driven shell-only (UIA patterns, zero synthetic input)
against the installed build:

- `ScopeButton` / `ScopeButtonTextBlock`: starts `Global` = **25 cards** (19
  backiee categories + 4K + Harvest Rain + Backgrounds + Places + Curated);
  flyout items `Global (all platforms) | Backiee | AlphaCoders | Pixabay |
  Pexels`; pick `Backiee` -> label `Local · Backiee` + **19 cards**, pick
  `Global` -> 25 again.
- a merged card with >1 source opens a platform chooser (`Nature` =
  `Backiee | Pixabay | Pexels`, `Space` = 2 sources); a card with exactly 1
  source takes the direct-navigate fast path (`Sources.Count == 1`).
- chooser -> `Pixabay` = title `Pixabay`, empty grid, and a LOUD
  `StatusInfoBar` error (`Pixabay support needs a Pixabay API key. Add it in
  Settings > API Keys, then try again.`) - never a silent empty state; chooser
  -> `Backiee` = title `Nature wallpapers` + 30-card grid.
- UIA ids for future checks: titles `PageTitleTextBlock` (backiee/alphacoders)
  vs `TitleTextBlock` (public sources), grids `CategoriesGridView` /
  `WallpapersGridView`, errors `StatusInfoBar` (read its child Texts - its own
  Name is empty). `Nature` (3 sources) and `Space` (2) are the merged
  canaries.
- flyout menus need the offscreen-visible window protocol (while minimized
  the popup is never even created) - recipe in automata
  `windows-ui-automation` AGENTS.md.

## all-9-platform categories, reversed + ported (2026-10-06)

the Categories page now scopes over **every implemented site** (supersedes the
25-card state above): Global = **112 merged cards**; per-scope counts: backiee
19, alphacoders 63, pixabay 20, wallpaperhub 17, artstation 5, pexels 3,
wallhaven 3, bing 1, simple desktops 1. `CategoryPlatforms` (9 entries) is the
scope menu; every other platform contributes `PublicWallpaperService.GetModes()`
as its category cards. per-site reversal evidence + curl recipes live in
`automata-private/<site>/AGENTS.md` (8 new folders + `wall.alphacoders.com`
extended). key facts the code depends on:

- **wallhaven**: `categories` bitmask order = general/anime/people (proven by
  reading the per-result `category` field: `100`->only general, `010`->anime,
  `001`->people) -> modes General/Anime/People; legacy Latest/Random/Toplist
  still resolve with 111. the mode switch is lowercased (slideshow passes
  lowercase modes).
- **pixabay**: categories = the site's curated Collections index, live-loaded
  (see the dated section below) - the old 20-API-category list, the API calls,
  and the API key requirement are GONE.
- **wallpaperhub**: the 17 collections (id+title) = `WallpaperHubCollections`,
  LIVE-LOADED from the site's own Collections index (see the dated section
  below) - a mode that matches a collection title routes to `/collections/<id>`
  and parses `pageProps.collectionWallpapers` (identical `{entity:...}`
  wrappers as `initWallpapers`); anything else = `/wallpapers`
  (`initWallpapers`). both SSR shapes serve everything at page 1. `?tags=` is
  SSR-ignored - never build fetch URLs on it.
- **alphacoders**: uniform `https://alphacoders.com/<slug>-wallpapers?page=N`
  (`4k` special -> `/resolution/4k-wallpapers`; plain `4k-wallpapers` = 404).
  the category list is LIVE-LOADED from the site's real category page
  `https://alphacoders.com/tag/is-category` (24 desktop rows over the index's 2
  pages; `GetCategoryIndexAsync`
  + `SetCategories`, same loud contract as backiee - a failed fetch = a loud
  error line, never a silent empty list) - the merged grid AND
  `AlphaCodersGridPage` titles both read `AlphaCodersService.Categories`;
  deep-linked categories leave the 3 quick buttons unselected instead of faking
  4K. the old 63-entry homepage-curated table was REMOVED 2026-10-08 (see the
  dated section below).
- **artstation**: the subject-matter taxonomy is POST + CSRF (anonymous =
  `Invalid CSRF Token`; GET filter params silently ignored - all baselines stay
  47230), but the site's own **Channels directory** is anonymous-open - that is
  the category source now (see the dated section below). live index
  `api/v2/community/channels/channels.json` (`GetChannelsIndexAsync` +
  `SetChannels`, 64 published channels) + drill
  `community/channels/projects.json?channel_id=&sorting=trending&per_page=45`
  (`GetChannelProjectsAsync`, same card shape as the search endpoint minus
  `is_adult_content` - the adult flag there is `hide_as_adult`).
  `ArtStationGridPage` takes `channel:<id>` as its navigation parameter;
  sorting buttons exit channel mode (and neither chip is highlighted while
  in it).
- **bing + simple desktops**: proven zero taxonomy => one honest entry each
  (`Daily` / `Minimal`) so no scope ever renders an empty grid; their fetchers
  ignore the mode. (SUPERSEDED 2026-10-09: both are now rejected from the
  category page entirely via the dynamic exclusion list - see the dated
  section; their grid chips keep the entries.)
- **pexels**: categories = the site's own Wallpapers discover index, live
  loaded (see the dated pexels section below - the old read "API has no
  categories at all (Curated/Nature/Space stay), the website is
  Cloudflare-walled" is SUPERSEDED: the site passes with curl + the full
  browser header set).
- chips overflow fix: up to 17-20 mode chips per platform -> `ModeButtonsPanel`
  now sits in a horizontal `ScrollViewer` (Auto horizontal bar, Disabled
  vertical, ZoomMode Disabled).
- **category card thumbnails (fixed 2026-10-07)**: only backiee cards ever got
  real art - `BuildMerged` left every other platform's card on the shared
  placeholder forever (the reported bug: "cards after the first backiee ones
  show the same placeholder"). now `FillThumbnailsAsync` (fire-and-forget
  right after `ApplyScope`) gives every non-backiee card ONE representative
  wallpaper = page 1's first item of its own drill-down fetch (alpha
  `GetWallpapersByCategoryAsync(key,1,1)` / `ArtStationService
  GetChannelProjectsAsync` / `PublicWallpaperService.GetWallpapersAsync`) through
  `WallpaperItem.LoadImageAsync`, session-cached in a static
  `ConcurrentDictionary`. three traps found live:
  - **`AlphaCodersService`'s scrape cache is STATIC** (list + lastPage +
    currentCategory) and NOT thread-safe: the first build fetched through a
    4-wide gate and exactly the 4 simultaneous cards came back empty (plus
    cross-category image mixups) while every sequential scrape - the grid
    page, curl - worked. alpha fills therefore run through their own
    `AlphaCodersThumbGate = 1` (alone); other platforms share the 4-wide gate
    because each news its own service instance per fetch.
  - **`WallpaperItem`'s `GetOutputStreamAt`/`AsStreamForWrite`/`FlushAsync`
    stream path threw WIC `0x88982F50` with an EMPTY `ex.Message` on 3/93
    loads** (an empty reason in the error bar) -> rewritten to
    `BackieeCategory`'s proven `stream.WriteAsync(bytes.AsBuffer())` +
    `Seek(0)` + `SetSourceAsync` (its 19/19 pattern); empty reasons now
    coalesce to `<Type> hresult=0x<code>` so the bar can never go blank.
  - loud contract: the bar lists EVERY platform with >= 1 failed card as
    `<platform> category thumbnails: <n>/<total> failed - <first reason>`; a
    keyless install shows exactly 1 line (the Pexels API key - pixabay needs no
    key since the 2026-10-08 collections rework; since the 2026-10-09 pexels
    discover re-source even pexels FILLS need no key - the chip photos come
    from the CDN - so a keyless install shows no fill line at all: the loud
    key error surfaces on the first pexels DRILL instead). the
    fill failure paths never touch the session cache, so the next Categories
    open retries them. verified menuless + vision on the local build
    (2026-10-07): the complaint cards `Aura Farming..Cyberpunk` all real,
    alpha 0/63 failed, 0 duplicates except one honest source overlap -
    `animal` and `cat` pages serve the SAME first thumb
    (`thumbbig-20658.webp`, curl-verified), so those two cards legitimately
    show one photo. second instance (2026-10-08): pixabay `people` and
    `travel` both serve photo id `10506740` first (identical bytes, verified
    live) - pixabay's per-request random url prefix makes the first-hit URLs
    LOOK different while serving the same image, so compare hit ids (or file
    hashes), never url strings. duplicate thumbs are honest source overlap,
    not a cache bug, unless hit ids differ.

## 7 more keyless platforms ported, verified live (2026-10-08)

the picker listed 28 platforms; 9 were implemented, now **16**: the new 7 =
DesktopNexus, Digital Blasphemy, HDwallpapers, Pixiv, Cara, Wallpaper Cave,
Wallpaper Engine. the 2 dead domains (Kuvva, Vladstudio) were removed from
the picker entirely on 2026-10-08, so it lists **26**. Global categories =
**149 merged cards** (was 112).
per-site reversal evidence + curl recipes live in
`automata-private/<site>/AGENTS.md` (7 new folders). key facts:

- **single source of truth** = `PublicWallpaperService.GetSupportedPlatformNames()`:
  it drives the picker's not-implemented dialog (`ShowNotImplementedMessage`),
  `CategoryPlatforms`, `BuildMerged`'s platform loop, and `NavigateToSource`'s
  routing (default case = `IsSupportedPlatform` -> `PublicWallpaperGridPage`).
  that last one was the 4th hardcoded list - a `case "Pixabay": ... case
  "WallpaperHub":` switch arm - which made every new platform's card drill die
  with `No drill-down page exists for X categories`. no hardcoded platform
  lists remain.
- honest caps: Digital Blasphemy full = 640x480 preview (membership originals),
  Wallpaper Engine full = preview_url (file url needs auth), Cara thumb == full
  (single full-size image), Wallpaper Cave titles = `<h1> #<n>` (no per-item
  titles exist), DesktopNexus grid `/preview` + full `/original`, HDW full =
  `/previews/<slug>-<id>.jpg` (dropping `thumb_` from the cdn url is 404).
- content policy: pixiv `restrict=safe` pinned on every request + per-item
  `xRestrict != 0` skipped; steam parts tagged Questionable/Mature/Adult/
  NSFW/18+ dropped (2/30 on page 1); cara has no filter - no NSFW modes added.
- pximg referer: `WallpaperItem.GetRefererFor` returns `https://www.pixiv.net/`
  for i.pximg.net (alphacoders referer = 403 there) - proven live, pixiv tiles
  render real art.
- **cara needs curl, not .NET**: cara.app + images.cara.app return 403 to
  SocketsHttpHandler's TLS fingerprint (http/1.1 AND http/2; curl + same UA =
  200) -> `Services/CurlClient.cs` is cara's ONLY transport (html via
  `GetStringAsync`, images via `WallpaperItem.IsCaraUrl`), single method not a
  fallback, failure = curl exit code + stderr in the loud bar.
  `BackieeNetworkClient`'s curl part now delegates to CurlClient (identical
  invocation, unchanged behavior).
- **steam split regex fails one-typo-silent**: a verbatim `Regex.Split`
  literal one backslash short after the colon matches nothing -> 0 items, no
  exception, clean bar. the parse harness missed it by mirroring patterns;
  the check that works is decoding the `@"..."` literal out of the C# SOURCE
  (`""` -> `"`) and asserting IsMatch against a captured body.
- the other 9 listed platforms stay unimplemented ON PURPOSE: 7 are
  WAF/anti-bot-walled (Dribbble, Newgrounds, Peakpx, CGSociety, Behance,
  ArtFol, CharacterDesignReferences) and 2 need credentials (Unsplash
  = API key with hosting-terms caveat, DeviantArt = OAuth). the dead 2 were
  dropped from the list (see above), and Artgram left this list the same day
  (see the Artgram section below). the picker dialog text derives from
  SupportedPlatforms, so it always matches reality.
- verification recipe (menuless, zero focus steal, ~9 min):
  `C:\tmp\aura-platformcheck.ps1` (probes stay in C:\tmp, never committed).  it: waits for a window handle that HOSTS UIA text and is still the main
  window (the splash/transient window hands you a stale handle whose tree
  reads empty forever), drills the picker for Pixiv via a 900x3600 resize
  (the picker's ItemsRepeater realizes only the visible 16/28 otherwise),
  then drills 6 single-source cards (source grep proved none of
  Explore/Trending/Soulslike/All/Free/Latest is shared -> the chooser
  MenuFlyout never opens), waits 180s of thumbnail fill reading BOTH
  ErrorTextBlock and StatusInfoBar (an ERR line masks the IB fill lines if
  you return early), deep-scrolls via ScrollItemPattern and restores to top,
  captures via `automata-private\window-capture\Capture-WindowBackground.ps1`
  (moved from `tools\` on 2026-10-08) for rule-15 vision checks. assert
  items>=5 per drill, bar clean, cards>=100. the known open silent death
  (exit=0) fired on 2 of 7 runs - paced re-runs pass; root cause still open
  (see the menuless section below).

## 17th platform: Artgram, reversed + ported (2026-10-08, same day)

Artgram had been triaged as WAF-blocked - that was wrong: a live re-probe found
the gallery fully anonymous-reachable (reversal + curl recipes in
`automata-private/www.artgram.co/AGENTS.md`). ported as platform **17**; all
derived lists (picker dialog, categories, drill routing) follow
SupportedPlatforms automatically. Global categories = **150 merged cards**:
Artgram's Trending and Latest MERGE into the existing Wallpaper Engine /
HDwallpapers cards (same mode titles), so only **Oldest** adds a card.

- images: `fsn1.your-objectstorage.com` presigned urls, 1h expiry, minted per
  page render - covers (512x512) go on the grid and FullPhotoUrl; opening an
  item runs `PublicWallpaperDetailPage.UpgradeArtgramImageAsync()`, which
  refetches the art's detail page, swaps FullPhotoUrl to the artworks/
  original (render/set/download all flip together via GetBestImageUrl) and
  posts `Artgram original image loaded.` - failure keeps the cover and posts
  the reason as a Warning InfoBar. public-source items open the PUBLIC detail
  page (`Views/PublicSources/PublicWallpaperDetailPage`) - the AlphaCoders
  `WallpaperDetailPage` never sees artgram items (its DebugBigThumbTextBlock
  hook read ABSENT in the first verify run for exactly that reason).
- **verification recipe impact**: `aura-platformcheck.ps1`'s drill list
  `Explore, Trending, Soulslike, All, Free, Latest` is no longer chooser-free -
  Trending now has 2 sources (Wallpaper Engine + Artgram) and Latest 2
  (HDwallpapers + Artgram): clicking either would OPEN the chooser MenuFlyout
  (forbidden while the user works). swap both for `Oldest` (Artgram-only) in
  future full runs. verified menuless the same day: cards=150, Oldest drill
  items=20 (viewport-realized; grid is virtualized) title=`Artgram` bar clean,
  detail upgrade Success line exact, both backs OK, 90s fill clean, focus
  restored.
- known limit: the slideshow never opens detail pages, so it shows Artgram's
  512 covers (documented, not silently broken).

## alphacoders categories now live from the real category page (2026-10-08)

user audit: the 63-card AlphaCoders set was derived from the homepage's
curated slug links, not the site's own category taxonomy. fixed at the source:
`AlphaCodersService` holds NO static category list now -
`CategoriesPage.LoadCategoriesAsync` fetches
`https://alphacoders.com/tag/is-category` (runs alongside the backiee load)
through `GetCategoryIndexAsync` + `SetCategories` and parses its **24 `<h3>`
rows across 2 pages** - the index PAGINATES (`?page=1` = 20 rows + a
`?page=2` link, `?page=2` = 4 more: Dark/Technology/Religious/Humor with no
further link, `?page>=3` = 200 but 0 rows) and the loader walks pages until
the first empty one (hard cap 10; blowing the cap = a thrown error -> the loud
bar, never a silently truncated list) (name = row text, first letter of each word up-cased - "video game" ->
"Video Game", the page's own "TV Show" casing survives; browse key = the h3
slug -> `<slug>-wallpapers?page=N`, which is exactly the row's own "Desktop
Wallpapers" cell href). the `-phone`/pfp/gif cells can never parse (h3-anchored
regex). fetch failure = a loud error bar line (same contract as backiee), and
the error bar now also shows under the Local - AlphaCoders scope (visibility
conditions extended to it).

- counts: per-scope AlphaCoders = **24** (was 63), Global = **113 merged
  cards** (was 150 at the 63-era; 110 when only page 1 loaded - Dark,
  Religious, Humor add cards, `Technology` merges into the hdwallpapers one).
  the grid page's `4k`/`harvest`/`rain` quick
  buttons are page chips, not category rows - unaffected (`4k` still resolves
  to the resolution page).
- menuless canary: `Batman` is gone (not a real category) - drill **`Vehicle`**
  for the alpha single-source check. verified menuless three times: 113 cards,
  24/24 real names on the grid, 0 legacy names, `Vehicle` title + 15 items,
  bar clean, stable under a 60s watch.
- the back-lands-on-Home anomaly observed TWICE now: the 2026-10-08 alpha run
  and the 2026-10-09 pexels run 3 (both at the LAST back of a drill sequence:
  scope reads `ABSENT`, `Select Your Wallpaper Source` present, cards=-1;
  re-navigating to Categories works) - not reproduced between them, root
  cause unknown.

## wallpaper cave thumbs hit an AVIF wall (2026-10-08)

`wallpapercave.com/mwp/<file>` now transcodes to **image/avif
unconditionally** (`Accept: image/jpeg` does NOT negotiate it back; file magic
`ftypavif`), and WinUI has no AVIF decoder (its decoder list: TGA/QOI/GIF/
JPEG/TIFF/PBM/BMP/Webp/PNG) - so every Wallpaper Cave card decode-failed and
the loud bar showed `Wallpaper Cave category thumbnails: 8/8 failed - Image
cannot be loaded. Available decoders: ...` at ~35s into every fill. fix =
`ImageUrl` now uses the site's OWN desktop `<source>`
`https://wallpapercave.com/dwp1x/<file>` (JPEG, 12/12 verified across 4
albums); `FullPhotoUrl` stays `/wp/` (JPEG original). re-verified menuless:
bar clean through the full 60s fill watch.

## pixabay categories re-sourced to the real collections page (2026-10-08)

user audit: pixabay's 20 "categories" were the API's filter values (docs
`category str` row), not the site's own browsable categories. the real surface
= `https://pixabay.com/collections/` ("Curated Collections" by Pixabay) -
**63 collections** over 2 pages (`?pagi=1` = 40 + `?pagi=2` = 23; TRAP:
`?pagi>=3` SILENTLY WRAPS to page 1 - the pager on page 2 even links to
`?pagi=3` - so `GetPixabayCollectionsIndexAsync` stops at the first page with
no NEW slugs, hard cap 10 + thrown error past it). `CategoriesPage`
live-loads it (runs alongside the alpha + backiee loads) via
`SetPixabayCollections`, same loud contract, error-bar visibility extended to
the Local - Pixabay scope; the drill fetcher scrapes each collection's own
page (`?pagi=N`, ~15 tiles; `data-pk` id, img `alt` as title, `__340.jpg`
tile -> `_1280.jpg` full - SINGLE underscore, `__1280`/`__640`/bare = 403).

- **the API is GONE from the pixabay path** (it cannot filter by collection):
  pixabay needs NO API key anymore. the Settings key box was removed (saving
  now clears any legacy stored key via the empty second slot), the keyless
  bar-line count drops to exactly 1 (Pexels), `GetDescription` and the README
  say so.
- **transport = curl, like cara**: Cloudflare fingerprints the TLS client -
  the SAME headers pass through curl.exe and 403 through SocketsHttpHandler
  (http/1.1 `RequestVersionExact` included), so html fetches route through
  `CurlClient.GetStringAsync` with `Sec-Fetch-Mode: navigate` +
  `Sec-Fetch-Site: none` REQUIRED (plain curl = 403; the `... Aura/1.0` UA
  passes with them). the CDN (cdn.pixabay.com) and `/api/` pass .NET - only
  the HTML pages route through curl.
- **Cloudflare rate-limits rapid pixabay fetches** (~4+ back to back = 403
  challenge, loud as curl exit 22 in the bar, retried next open - failures
  are never cached): the thumb fill got its own `PixabayThumbGate = 1` (like
  alpha's) and the full 61-card pixabay fill then ran 0 challenges.
- counts: per-scope Pixabay = **63** (was 20), Global = **159 merged cards**
  (was 113; collection names like Food/Fashion/Sports merge with other
  platforms' cards, adding 46 net). public-grid drill titles stay the
  PLATFORM name (`Pixabay`), not the collection name.
- menuless canary: `Halloween` (pixabay-only; 15-item drill, alt-text
  captions render). verified menuless 2026-10-08 (probe
  `C:\tmp\aura-pixabaycollectioncheck.ps1`): 159 cards, 14/14 sampled
  collection names, 0 old API-category names, Halloween + alpha `Vehicle`
  drills OK, bar clean through the whole fill + a 90s watch, no flip. vision
  (zengate, `C:\tmp\zengate-vision.ps1`): real Halloween artwork tiles, no
  broken/placeholder tiles.
- **the slideshow's `latest` mode = the site's own newest wallpaper results**
  (fix 2026-10-10): `/images/search/wallpaper/?order=latest&pagi=N` via the
  same curl transport. ~100 tiles/page, `?pagi=99` serves older DISTINCT ids
  (NO wrap like the collections index), every tile ships a schema.org
  ImageObject JSON-LD block (`contentUrl` = full `_1280`, `acquireLicensePage`
  = item href, `name` = title; parse independent per-key, no key-order
  contract). TRAP: the visible `<img>` is eager on only ~19 tiles (the rest
  render `src=/static/img/blank.gif` with the URL living ONLY in the JSON-LD)
  - the collection-page tile-img parse matches 19/100 here. before this fix,
  `mode=latest` (Latest basis + the All fast path) fell into the
  collection-name lookup and threw `No pixabay collection named 'latest' is
  loaded` on EVERY start; it only surfaced when the user added pixabay to the
  desktop platform list (their original 8-platform config had no pixabay, so
  no probe ever hit it). full facts in
  `automata-private\pixabay.com\AGENTS.md` under "latest wallpaper search".

## wallpaperhub categories live from the collections index (2026-10-08)

user audit: the 17 wallpaperhub "categories" were a hand-reversed static table.
fixed at the source: `PublicWallpaperService` now live-loads
`https://www.wallpaperhub.app/collections` on every Categories open
(`GetWallpaperHubCollectionsIndexAsync` + `SetWallpaperHubCollections`, runs
alongside the backiee/alpha/pixabay loads, same loud contract, error-bar
visibility extended to the Local - WallpaperHub scope).

- index facts: **17 collections**, ONE page today - `?page=N` is IGNORED
  (page 2 re-serves the same rows), so the loader walks pages until the first
  one brings no NEW ids (hard cap 10, thrown error past it = the loud bar,
  never a silently truncated list). card markup = `<h3>name</h3>` ...
  `href="/collections/<id>"` (the row's View link); the parse matched 17/17
  against the old static table (0 mismatches - titles/ids unchanged).
- **no curl**: wallpaperhub accepts .NET's SocketsHttpHandler fingerprint
  (IWR 200) - unlike pixabay/cara, the index AND the drill fetches stay on
  the plain HttpClient.
- the static `WallpaperHubCollections` array is GONE (replaced by a locked
  live snapshot). `GetModes` reads the snapshot property; an empty list (load
  failed) = no wallpaperhub cards + the loud error line, and the drill
  fetcher's `FirstOrDefault` miss falls through to the `/wallpapers` route
  exactly as before.
- menuless canary: `Windows 11` (single-source card; collection has 6 items,
  grid title = platform name `WallpaperHub`). verified 2026-10-08 (probe
  `C:\tmp\aura-wallpaperhubindexcheck.ps1`): 159 cards, 17/17 live names on
  the grid, `Windows 11` 6 items + alpha `Vehicle` 15 items, bar clean
  through the whole fill + a 90s watch, no flip. vision (zengate): 4 real
  Windows 11 tiles (bloom/logo/glow/sunrise), mode chips = live collection
  titles, zero errors.

## artstation categories live from the channels directory (2026-10-09)

user audit: the channel picker on artstation's search sidebar (Abstract,
Anatomy, Animals & Wildlife, ... "All channels") IS the site's category
taxonomy - reversed and ported as artstation's categories, replacing the 5
honest query entries.

- endpoints (all anonymous, plain HttpClient - no curl): list
  `GET api/v2/community/channels/channels.json` = `{total_count, data[]}`
  (64 published channels: 61 global + 1 hashtag + 2 sponsored; fields id,
  name, uri, state, type) and drill
  `GET api/v2/community/channels/projects.json?channel_id=<id>&page=N&sorting=trending&per_page=45`
  (card shape = search/projects.json minus `is_adult_content` - adult items
  carry only `hide_as_adult`, filtered; `total_count` caps at 10000; trending
  page2 can overlap page1 by ~2/45). `sorting` is RESTRICTED to
  trending|latest|popular - anything else = 400 with the valid list in the
  error body. all 64 ids verified 200-with-items in one pass.
- discovery: the list endpoint was found in the Angular search bundle - the
  `/search` document 403s without full `Sec-Fetch-Mode: navigate` headers,
  and the JS bundles need `Referer` + `Sec-Fetch-Dest: script`. GET filter
  params on `search/projects.json` stay silently ignored (baseline
  unchanged), and `/api/v2/search/channels` 500s on every shape tried - the
  community/channels pair above is the real surface.
- code: `ArtStationService` holds a locked live snapshot (`ChannelsLock` /
  `SetChannels` / `GetChannelsIndexAsync`) like alpha/pixabay/wallpaperhub;
  `SearchProjectsAsync` and the static 5-entry `ArtStationCategories` table
  are GONE. keys = `channel:<id>`; the thumb fill and `ArtStationGridPage`'s
  OnNavigatedTo parse it STRICT (a non-`channel:<id>` key throws - loud,
  never a silent browse-mode grid). grid title = `ArtStation <channel name>`
  (snapshot lookup; `#<id>` label if the snapshot ever lacks it). the 5th
  live-load block runs with the others and `"ArtStation"` joined both
  error-visibility conditions.
- counts: per-scope ArtStation = **64** (was 5), Global = **221 merged cards**
  (was 159; 2 channel names merged into existing cards - Abstract among
  them). the old query names survive on their non-artstation owners (pixiv:
  Wallpaper/Landscape/Nature, pexels: Space, backiee+alpha+pixabay:
  Abstract), so none of those cards disappeared.
- menuless canary: **`Book Illustration`** (artstation-only channel,
  chooser-free) -> title `ArtStation Book Illustration`, 44 items, bar clean.
  verified 2026-10-09 with probe `C:\tmp\aura-artstationchannelcheck.ps1`:
  221 cards, 64/64 channel names on the grid, Book Illustration + alpha
  `Vehicle` + the pixabay `Nature videos` regression all OK, bar clean
  through the whole fill + an 18x5s watch, no flip, focus restored. vision
  (zengate): real book-illustration/fantasy artwork tiles (horned creature,
  Witcher covers), zero errors.

## pixabay tile parse: lazy imgs + video collections (2026-10-09)

found by the artstation probe's long bar watch: a persistent
`Pixabay category thumbnails: 1/61 failed - no items returned for "Nature
videos"` line - earlier probes sampled the bar before the 1-wide pixabay fill
reached that card, so it looked clean. root cause: the shipped tile regex
matched ONE shape - eager `src=` photo/illustration jpg tiles - while every
collection page mixes shapes:

- only the first ~15 tiles render eagerly; the rest are
  `src="/static/img/blank.gif" data-lazy="..."` (halloween page: 15 eager +
  33 lazy of 49 main-grid tiles - every drill silently served ~15/page).
- posters: `__340.jpg`, `__340.png` (full = `_1280` + SAME extension -
  `_1280.jpg` on a png asset = 403), video `_tiny.jpg`; tile links span
  `/photos` `/illustrations` `/vectors` `/videos`.
- "Nature videos" is the ONE video collection of 63 - `class="item
  video-item"` tiles whose poster still ships a full ladder: `tiny` 1280x720
  -> small 1920 -> medium 2560 -> `large` 3840x2160 (all 200; `big` = 404).
  it parsed 0 items = the permanent loud error line.
- fix = two-pass regex (eager `src=` + lazy `data-lazy=`), generic tile href
  (a future media type degrades to skipping that tile, not a broken
  SourceUrl), jpg|png, video included; both passes merged and sorted by
  document index, deduped by `data-pk` id. `FullPhotoUrl`: video
  `_tiny.` -> `_large.`, photo `__340` -> `_1280` (extension preserved).
- verified: parse == tile-div count on 3 captured pages (50/49/50, was
  15/0/0 with the shipped pattern), probe drill `Nature videos` = 20 items
  (viewport realized) title `Pixabay` bar clean, vision = real nature stills
  (beach-spit frame = the probed video), full watch bar clean.

## categories page lag: off-thread image conversion + parallel opens (2026-10-09)

user report: the Global categories page was "lagging super hard" (stutter
while the ~200 card thumbs streamed in, dead time on every open). two causes,
both fixed:

- **`WallpaperItem.LoadImageAsync` / `LoadFullImageAsync` ran their CPU on the
  UI thread**: every `await` in them resumes on the WinUI SynchronizationContext,
  and ImageSharp's `Image.LoadAsync` + `SaveAsPngAsync` over MemoryStreams
  complete synchronously - so each fill did a FULL-resolution decode + FULL-
  size PNG re-encode inline on the dispatcher (tens to hundreds of ms x ~200
  fills per open; a detail page = one big stutter). both now wrap decode +
  encode in `Task.Run` (pool thread); `LoadImageAsync` also resizes to <=500
  wide BEFORE encoding (`DecodePixelWidth = 500` discarded the full-res PNG
  anyway - the encode is thumb-sized now; sources <=500 wide are untouched, so
  the DecodePixelWidth upscale path behaves exactly as before). `BitmapImage`
  is a DependencyObject: created back on the UI thread after the pool hop. the
  full path ALSO left the old `GetOutputStreamAt`/`AsStreamForWrite`/
  `FlushAsync` WIC form (the 0x88982F50-prone one from the thumb fix above) for
  the proven `WriteAsync` + `Seek(0)` + `SetSourceAsync` pattern.
- **the open awaited the 5 index loads sequentially** (alpha -> pixabay ->
  wallpaperhub -> artstation -> backiee + 19 eager thumbs) before the first
  card could render = every open paid the SUM of all round trips, growing with
  every platform added. now `Task.WhenAll` over 5 local funcs inside
  `LoadCategoriesAsync`, each returning ITS OWN `List<string>` of errors (one
  shared list appended from parallel tasks would race), merged after WhenAll;
  a `finally` keeps the progress bar + `_isLoading` reset even if WhenAll ever
  faulted (a stuck `_isLoading` would dead-end the page silently).
- verified menuless (re-run of `C:\tmp\aura-artstationchannelcheck.ps1`): 221
  cards, 64/64 channels, all 3 drills OK, bar clean through the whole fill +
  an 18x5s watch whose cadence never slipped (UIA reads = the UI-thread canary
  - the old build starved them during fills), focus restored. one run died
  with the KNOWN open exit=0 signature mid-drill; the paced re-run passed.
- **UI-thread peg on the full grid is PRE-EXISTING, measured A/B 2026-10-09**
  (during the pexels port, to rule the port out): with ZERO probe walkers on
  an idle Categories page, one thread burns ~1 core - and it is the UI thread
  (per-thread UserProcessorTime delta + GetWindowThreadProcessId). HEAD
  `6b88dee` at **221 cards**: CPU 5.58s/6s, GridCount-style walk 9541 ms,
  FindText 3132 ms. pexels build at **296 cards**: 5.36s/6s, 18098 ms,
  7239 ms. Home (small tree) = 0.2s/10s + 13 ms walks. so the peg predates
  the pexels port and scales with realized card count - it is NOT a pexels
  regression; what the UI thread actually burns CPU on is NOT yet root-caused
  (a managed stack dump is the next step; the probe deaths below correlate
  with walking this pegged thread). measurement gotcha: `dotnet build
  winui\` (the folder) resolves `Aura.sln` whose default platform is ARM64
  and lands in `bin\ARM64\Debug\win-arm64\` - which does NOT run on this x64
  box; build `winui\Aura.csproj` directly for the flat `bin\Debug\Aura.exe`
  the probes launch.

## pexels categories live from the discover index (2026-10-09)

user audit: pexels' categories = the SECTIONS on its own Wallpapers
discover page (`https://www.pexels.com/discover/wallpapers/`), minus the
"Phone & mobile" section (a size class, not a style - user decision). the
old read "the API has no categories, the website is CF-walled" is REVERSED
+ ported: the wall was a bare-curl artifact, and the taxonomy lives in the
page's Next.js payload, not the API.

- structure: `__NEXT_DATA__` script payload (match on the id alone - the
  other attrs vary) -> `props.pageProps.topics[]` = **9 topics x ~10
  `terms[]`** = 88 terms (`resolution phone desktop dark colors aesthetic
  nature seasonal subjects`; `dark` has 8, the rest 10). the `phone` topic
  is skipped per user decision -> **78 terms over 8 topics** (10/10/8/10/
  10/10/10/10). every term = `{term, mediaId, imageUrl, totalResults}` and
  the term is BOTH the card name (the pill's lowercase text) and the API
  search query. the pill href is `/search/<term>/` with NO orientation
  param, so the drill dropped `orientation=landscape` to match (`vertical
  wallpaper` resolves portrait). one pill (`live wallpaper`) links to
  `/search/videos/` on the site but its PHOTO surface serves hundreds of
  results - the photo query is what this image app ships (automata
  `www.pexels.com/AGENTS.md` has the curl recipe).
- transport: `www.pexels.com` 403s .NET's TLS fingerprint AND bare/minimal
  curl (UA alone, pixabay's Sec-Fetch pair) - only curl + the FULL browser
  header set passes (6 headers, side-by-side proof 2026-10-09) -> pexels
  html routes through `CurlClient` like pixabay/cara, header set lives in
  `PexelsBrowserHeaders`. a challenge shell fails the `__NEXT_DATA__`
  presence check -> thrown error -> loud bar, never a silently empty list.
- code: `PublicWallpaperService` holds a locked live snapshot
  (`PexelsDiscoverLock` / `SetPexelsDiscoverTerms` / `PexelsDiscoverTerms`
  property / `GetPexelsDiscoverIndexAsync`, same pattern as alpha/pixabay/
  wallpaperhub/artstation); `GetModes(Pexels)` reads it; the 6th
  `Task.WhenAll` load block in `LoadCategoriesAsync` feeds it and
  `"Pexels"` joined both error-visibility conditions. **card thumbs = the
  pill's own `images.pexels.com` photo** (`imageUrl + ?h=420&w=420&fit=
  crop&dpr=1`; pill ImageUrls carry NO query string today, so the append is
  safe) - ZERO API calls: the free tier caps 200 req/hour and 78 search
  fills per open would eat 40% before any browsing. thumb case lives in
  `LoadRepresentativeThumbnailAsync` (a `WallpaperItem` over the CDN url ->
  the shared `LoadImageAsync`, which already does webp under the app's
  image Accept); `GetPexelsTermImageUrl` miss = loud throw, never a fake
  placeholder. the drill still searches (`/v1/search?query=<term>`, key
  required; `/v1/curated` and the orientation param are GONE from the code).
- counts: per-scope Pexels = **78** (was 3), Global = **296 merged cards**
  (was 221 = 221 - 1 Curated + 78 - 2 merges: pixabay already owns
  collections named `black wallpaper` and `aesthetic wallpaper`, so those
  two pexels terms merged into them instead of adding cards). Curated had
  no other owner and disappeared with the port; Nature/Space survive on
  backiee/pixiv/desktopnexus/hdwallpapers. `GetDescription` says
  discover-loaded + mentions the key for drills.
- keyless contract: fills need NO key (CDN pill photos) - a keyless
  install's fill bar is CLEAN and the loud key error surfaces on the first
  pexels DRILL instead (see the thumbnail-fix section above).
- menuless canary: **`dark academia wallpaper`** (pexels-only term,
  chooser-free) -> grid title `Pexels`, description = the discover line,
  mode chips = the live terms, search returns 30 (viewport-realized 20).
- verified menuless + vision 2026-10-09 with probe
  `C:\tmp\aura-pexelsdiscovercheck.ps1`: 296 cards, 78/78 term names on the
  grid, Nature/Space present, Curated gone, 5 cross-platform spot checks,
  all 3 drills (dark academia wallpaper / alpha Vehicle / pixabay Nature
  videos) OK, bar clean through the fill. vision (zengate, 3 shots):
  Categories top = real art everywhere, drill = 4 real pexels photos +
  title `Pexels`, scrolled pill zone = 9 pexels cards all real artwork, no
  placeholders, no errors. runs 1+2 died with the known exit=0 (see the
  death section); run 3 passed end to end after pacing + an adaptive
  settle that waits for UIA walk latency instead of a fixed sleep.

## history rows open the wallpaper's detail page (2026-10-09)

user choice: clicking a Wallpaper History row opens the IN-APP detail page
for that wallpaper (never a browser URL). rows store a serializable
navigation snapshot at AddEntry time:

- schema (`Services/WallpaperHistoryService.cs`): `HistoryNavigation {Page,
  Platform, Wallpaper}` where `Wallpaper` is a `HistoryWallpaper` DTO -
  the string/bool fields of `WallpaperItem` (ImageUrl, FullPhotoUrl,
  SourceUrl, Title, Description, ...), explicitly EXCLUDING ImageSource +
  DownloadCommand (not serializable). `AddEntry(..., WallpaperItem? wallpaper,
  string? page = null, string? platform = null)` - optional tail params, so
  SlideshowService's call is untouched and yields `Navigation = null`
  (slideshow does not open detail pages - honest, not broken).
- page discriminators: `"Public" | "Backiee" | "AlphaCoders" |
  "ArtStation"`; `HistoryPage.HistoryEntry_Click` switches on them ->
  `PublicSources.PublicWallpaperDetailPage` (+`PublicWallpaperNavigationParameter`),
  `Backiee.WallpaperDetailPage`, `AlphaCoders.WallpaperDetailPage`,
  `ArtStation.ArtStationDetailPage`. null Navigation or an unknown Page =
  LOUD `StatusInfoBar` (row 0 in the XAML, `StatusInfoBar` + `ShowStatus`),
  never a silent no-op; a navigate() returning false = loud "could not
  open" with the reason. the exact null message: `No detail page is stored
  for this entry — it was set by the Slideshow or saved before click-through.
  Set the wallpaper again from its source page to make it clickable.`
- rows are `HistoryEntryButton : Button` (a Button subclass because
  `ProtectedCursor` is protected) - sets `InputSystemCursor.Create(Hand)`,
  `AutomationProperties.Name` = the title (clean UIA: rows findable by
  their title as a Button), hover swaps to `CardBackgroundFillColorSecondary`.
- the 4 pre-existing entries were BACKFILLED by hand (ids/urls curl-verified
  live): backiee 376636, alphacoders 996764 (x2 - desktop + lock screen),
  simple-desktops traffic. **backiee SourceUrl trap**: the default
  `https://backiee.com/wallpaper/{Id}` 404s - the real URL needs the
  category slug (`/wallpaper/anime/376636`; `/wallpaper/376636` 301s there)
  - the backfill stores the canonical URL.
- **artstation detail refetch = curl-only (CF fingerprint)**: `/projects/
  <hash>.json` 403s .NET even with full browser headers while
  `projects.json` / search / channels all pass .NET; bare curl gets a CF
  challenge page (looks like JSON), and curl + `Accept:
  application/json,text/plain,image/*,*/*` + `Sec-Fetch-Mode: cors` +
  `Sec-Fetch-Site: same-origin` = 200 (side-by-side proof 2026-10-09) ->
  `GetProjectDetailsAsync` routes through `CurlClient` with exactly that
  header triple (single method, not a fallback; failure = curl exit code +
  stderr surfaced loud). same class as pixabay/cara/pexels. before this
  fix the B4 probe run showed `Could not load ArtStation artwork: 403` +
  empty title.
- UIA verify gotcha: x:Name `TitleTextBlock` appears TWICE in the
  backiee/alpha detail name scopes (XAML name-scope collision) - assert
  titles via a TEXT element with the literal string, not AutomationId.
  per-page unique markers: backiee `SetAsWallpaperButton`, alpha
  `DebugBigThumbTextBlock`, public `PlatformTextBlock`, artstation
  `ArtworkImage`.
- probe (stays in `C:\tmp`, never committed): `aura-historyclick.ps1` -
  stage A appends a `Source=(probe none)` no-Navigation row (rerun-safe:
  the file may already be backfilled) and asserts the loud path; backfill
  strips it, adds Navigation to nav-less real entries + a temp
  `ArtStation (probe temp)` row for the 4th switch arm; stage B clicks all
  4 rows (assert title + page marker + bar clean + back OK each time);
  cleanup strips every `(probe ...)` row; final relaunch leaves History
  visible. two probe-authoring traps: (1) `ConvertFrom-Json` PSCustomObject
  rejects `$e.Nav = ...` assignment - use `Add-Member -NotePropertyName`;
  (2) capture BEFORE the back-navigation - a Shot after GoDetail returns
  shows History, not the detail page (first run wasted 4 captures that
  way; `aura-histdetail-shot.ps1` did the visual proof attach-and-capture).
- verified end-to-end 2026-10-09 (local `bin\Debug` build): stage A loud
  exact message OK; B1 Backiee / B2 AlphaCoders (big thumb resolved live)
  / B3 Public SimpleDesktops / B4 ArtStation (curl fix) all OK - title +
  marker + clean bar + back each time; file ends with 4/4 entries carrying
  Navigation, temp rows gone. vision (zengate): loud red InfoBar text
  exact; backiee detail = real Santorini artwork + title + Set/Download/
  View-on-Web buttons; public detail = `Traffic` + `Simple Desktops` +
  2560x1600 image; final History = 4 rows, thumbnails render, no bars.
  history file: `%APPDATA%\Aura\wallpaper_history.json`.

## backiee view-on-web 404 + selectable detail titles (2026-10-09)

- **backiee has NO id-only permalink**: `/wallpaper/<id>` = 404, and the
  older AGENTS claim that it 301s is WRONG - it 404s with no redirect (all
  of `/wallpapers/<id>`, `/wallpaper-detail/<id>`, `/detail/<id>`,
  `/wallpaper/<id>/` 404 too). but ANY slug in `/wallpaper/<slug>/<id>`
  resolves: `zzzznope/376636` -> 301 -> canonical `/wallpaper/anime/376636`
  (200). canonical slug = server-side (title slug or theme cat, not
  derivable client-side).
- the detail APIs (`detail_page_v2` root = WallpaperColors/Tags/Comments/
  Publisher + related lists; v1 `detail_page` root = TagList/ColorList/
  Publisher/YouMayList/SelectionList/CommentList) carry **no URL for the
  current wallpaper anywhere** - no string in the 108KB response even
  contains the id. the `else` branches in both detail pages' publishers
  therefore ALWAYS fired and clobbered every good canonical SourceUrl with
  the slugless 404 -> View on Web broke for every backiee detail opened
  (that was the user's 404 screenshot). fix: else branches deleted on
  `Views\Backiee` + `Views\AlphaCoders` `WallpaperDetailPage.xaml.cs` (alpha
  page's "DetailApiBaseUrl" is also backiee's detail_page_v2 - copy-paste);
  API value still used when present.
- remaining empty-SourceUrl fallbacks go through
  `BackieeApiParser.BuildWallpaperUrl(id, themeCat)` =
  `https://backiee.com/wallpaper/{themeCat|detail}/{id}` - the any-slug 301
  resolves to canonical in the browser. call sites: parser fallback (was
  line 39), both OnNavigatedTo defaults. sweep confirmed no slugless
  construction left (grep `backiee.com/wallpaper` = helper + comments only).
- curl-proven: `detail/376636` -> 301 canonical, `anime/376636` -> 200.
- **selectable titles**: `IsTextSelectionEnabled="True"` on the LIVE
  `TitleTextBlock` of all 4 detail pages (Backiee line 35, AlphaCoders line
  35 - the line-20 twins are commented out; PublicSources line 45,
  ArtStation line 14). XamlCompiler ACCEPTED the property (build green) -
  valid WinUI 3 TextBlock prop, contrary to the silent-exit-1 quirk which
  applies to properties that don't exist.
- menuless proof recipe (no synthetic input, no focus steal): UIA
  TextPattern on the title = present; `DocumentRange.Select()` then
  `GetSelection()` reads back the full exact title = selection live.
  UIA `SetFocus()` on a WinUI TextBlock throws "Target element cannot
  receive focus" (automation peer says not focusable), and with the window
  ACTIVE but the TextBlock unfocused the highlight renders NOWHERE in
  PrintWindow captures - so a visual-highlight proof menuless is NOT
  possible; the highlight appears on real pointer drag (focus goes to the
  TextBlock then). screenshot proof of selection needs the window already
  foreground - if fg is the user's app, ABORT loudly, never activate.
  probes: `C:\tmp\aura-selectfix*.ps1` (4 = screen-grab attempt).

## slideshow died silently on load + next-change status (2026-10-09)

user report: desktop slideshow set to `1 Minutes` never changed the wallpaper,
and the page had no "when is it expected to change" status. the log stopped
right after `Toggle enabled: True` - nothing else ever logged. root causes +
fixes:

- **one broken platform silently killed the whole batch**: both loaders ran
  every platform inside ONE try/catch with an EMPTY catch, and the platform
  loop included `platform == "ArtStation" || PublicWallpaperService
  .IsSupportedPlatform(platform)` - but `GetWallpapersAsync` has NO ArtStation
  arm (`_ => throw new NotSupportedException`) - so ArtStation (3rd in the
  user's list of 8) threw, the catch swallowed it, the local list was
  discarded, `_desktopWallpapers.Count == 0` and `StartDesktopSlideshow`
  returned BEFORE creating the timer: no timer, no next-change time, no
  countdown, no log line, forever. now each platform loads in its OWN
  try/catch (failures collected as `<platform>: <reason>`, logged), and
  ArtStation routes to `ArtStationService.GetProjectsAsync(sorting, batch)`
  (`https://www.artstation.com/projects.json?sorting=latest|trending` -
  curl-proven; plain .NET passes per the artstation section) instead of the
  public service. backiee's parse also throws LOUD on a non-array response
  shape (challenge/error page) instead of an unhandled `EnumerateArray`.
- **loud contract (rule: fail loudly, never a dead timer)**: the empty-batch
  return now sets `DesktopLoadError`/`LockScreenLoadError` (exact per-platform
  reasons) and raises a new `ErrorsChanged` event; the page renders it in two
  new per-column InfoBars (`DesktopSlideshowInfoBar` / `LockScreenSlideshowInfoBar`
  - Error + "Slideshow not running" when stopped, Warning + "Slideshow
  warning" when running with skipped platforms). apply failures (no image URL,
  download refused, Windows refused the change) set `*ApplyError` with the
  wallpaper title + reason and clear on the next successful set; the one-shot
  tick timers restart in `finally` (the old code only restarted on success,
  so one thrown cycle = permanent silent death) and log cycle failures;
  settings/restore read failures report via `ReportRestoreError` instead of
  empty catches.
- **public mode mapping**: the dialog's categories are backiee/alpha names -
  `NormalizePublicMode` maps `Latest Wallpapers`/`4K Wallpapers`/`8K UltraHD`/
  `AI Generated`/`Harvest Wallpapers`/`Rain Wallpapers`/`All` -> `latest` for
  the public platforms (the old `category.ToLower()` fed wallhaven's toplist
  default and pexels' literal search query with `latest wallpapers`).
- **status (the user's ask)**: countdown text never silently hides while
  enabled - `Starting slideshow...` / `Next wallpaper at HH:mm:ss (in 56s)` /
  `Changing wallpaper...` (<60s overdue) / `Overdue - expected at HH:mm:ss` /
  `Slideshow is not running`. the old code collapsed it when next-change time
  was MinValue (never started) and hid it >30s past due - exactly the reported
  blank state.
- **blank preview card**: `SlideshowPage.xaml` referenced
  `ms-appx:///Assets/placeholder.png` which DOES NOT EXIST (the real asset is
  `placeholder-wallpaper-1000.png`, used everywhere else) - both Image
  Sources fixed; the card now shows the live wallpaper.
- **restart guard**: page `RestoreSlideshows` + `MainWindow`
  .RestoreSlideshowsOnStartupAsync skip when `DesktopRunning ||
  DesktopStarting` (every Slideshow-page visit used to restart the running
  slideshow - resetting the countdown and re-adding a history row), and the
  page's singleton-event subscriptions moved to Loaded/Unloaded with `-=`
  first (dead-page handlers used to accumulate on `SlideshowService.Instance`).
- verified menuless with the user's exact config (probe
  `C:\tmp\aura-slideshowstatus.ps1`): `Loaded 224 wallpapers from 8
  platform(s)`, 0 platform failures, status line exact
  `8 platforms - Latest Wallpapers (Refresh: 1 Minutes)`, countdown
  `Next wallpaper at 15:28:58 (in 56s)` (vision: verbatim blue line, real
  preview photo, zero error banners), history gained `Desktop/Slideshow`
  rows at 15:25:39 / 15:26:56 / 15:27:58 / 15:28:59 = exactly 60s apart,
  2+ `next change time` log lines, info bar closed throughout.

## history site + category stickers (2026-10-09)

user ask: each Wallpaper History row should also show WHERE the wallpaper
came from (site) and WHICH category it was set from - next to the existing
`Desktop`/`Slideshow` pills.

- **schema**: `HistoryEntry` gained `Platform` + `Category` (nullable - old
  rows deserialize to null). `AddEntry` gained a tail `string category = ""`;
  entry `Platform` = explicit `platform` arg, else `wallpaper.Platform`;
  entry `Category` = explicit `category` arg, else `wallpaper.Category`.
  slideshow calls pass `wallpaper, category: _desktopCategory` (page stays
  empty so `Navigation` stays null - the click contract above is untouched).
  backiee/alpha/artstation detail calls now pass explicit `platform:` (the
  public one already passed `_platformName`).
- **items get tagged at the source** (`WallpaperItem.Category`): backiee
  `ThemeCat` in `BackieeApiParser.CreateWallpaperItem`; public mode in
  `GetWallpapersAsync` (the per-platform switch arms already `await`, so the
  wrapper assigns the switch result directly - an outer `await` there is a
  CS1929); alpha category key in `ScrapeWallpapersByCategoryAsync` (covers
  the grid AND the slideshow - both go through that scrape). artstation has
  no per-item category (honest absence, no pill).
- **old rows** (`RecoverSlideshowStickers`, one-shot from
  `MainWindow.RestoreSlideshowsOnStartupAsync` after both starts):
  `Source == "Slideshow"` rows take `Category` from the configured settings
  and recover `Platform` by matching the local filename id
  (`wallpaper-<id>.<ext>` / `lockscreen-<id>.<ext>`, first-dash split) against
  the new read-only `SlideshowService.DesktopBatch` / `LockScreenBatch`. rows
  whose id rolled off the live pages stay empty - never guessed (11/30 stayed
  empty in the verify run; category filled 30/30).
- **rendering** (`HistoryPage`): `MakeBadge` helper + pills in order type
  (gray) / source (SteelBlue Manual, SeaGreen otherwise) / site
  (MediumPurple, only when it differs from Source - public rows store the
  site IN `Source`) / category (Chocolate, only when known). old rows go
  through `DeriveSite`: `Platform` -> `Navigation.Platform` ->
  `Navigation.Page` (Backiee/AlphaCoders/ArtStation) -> `Source` when it is
  not Manual/Slideshow.
- gotchas hit: `Windows.UI.Colors` does NOT exist in WinUI 3 (it is
  `Microsoft.UI.Colors`; the helper's parameter type is `Windows.UI.Color`);
  this run's XamlCompiler MSB3073 exit-1 was a CASCADE of the two C# errors
  and went green once they were fixed (a standalone XAML failure would have
  stayed red - see the quirk section below); `HistoryListPanel` (StackPanel)
  has NO UIA peer so it always reads `ABSENT` - probe page detection uses
  `ClearHistoryButton`; `wallpaper_history.json` is NOT append-ordered (find
  new rows by `Timestamp`, never by array position).
- verified menuless with probe `C:\tmp\aura-historystickers.ps1` (stays in
  C:\tmp): run 2 full PASS - restore + next-change line OK, missing category
  0, missing site no-regression vs baseline, badge counts 37 category /
  30 site texts across 7 distinct sites, `StatusInfoBar` closed, a new
  AddEntry row (`Garchomp`, ArtStation) carried both fields, focus restored.
  vision (zengate): rows show exactly 4 pills - gray `Desktop`, green
  `Slideshow`, purple site (`ArtStation`/`Simple Desktops`/...), orange
  `Latest Wallpapers` - thumbnails render, zero error banners.

## taxonomy-less sites rejected from the category universe (2026-10-09)

user decision: sites with proven zero taxonomy (bing, simple desktops) stay
FULLY supported in the platform picker, but they are REJECTED from every
category surface - a category list built from them would be fake entries
(`Daily`/`Minimal` are fetcher modes, not taxonomy). enforcement is one
DYNAMIC list so future rejects are a one-line add:

- `PublicWallpaperService.CategoryExcludedPlatforms` (today `{ Bing,
  SimpleDesktops }`) + `IsCategoryExcluded(name)`. consulted in
  `CategoriesPage` twice: `CategoryPlatforms` (the scope menu - a rejected
  scope would always render an empty grid) and `BuildMerged`'s GetModes loop
  (no fake cards). `GetModes` itself stays factual on purpose: the
  `Daily`/`Minimal` entries still drive each platform's OWN grid chips +
  default mode (the platform picker is untouched by this rejection).
- counts: Global **296 -> 294** (both were single-source; a live-index curl
  check found no exact-name merge owner - wallpaperhub's "daily" hit is
  description text, pexels' `minimalist wallpaper` is its own card); scope
  menu 17 -> 15 platforms. supersessions: the "one honest entry each" bullet
  in the all-9-platform section above, and the menuless canary list's
  `Daily` `Minimal` (those two cards no longer exist).
- pending slideshow category-selector feature: the checklist universe must
  consult `IsCategoryExcluded` too - in Category mode bing/SD get no ticks
  and take the LOUD per-platform skip (supersedes the earlier design call
  where each resolved its own `Daily`/`Minimal` entry). (SHIPPED 2026-10-10
  - see the dated section below.)
- verified menuless with probe `C:\tmp\aura-categoryexclusion.ps1` (stays in
  C:\tmp): cards=294, `Daily`+`Minimal` gone from the grid, 11 spot names
  present, alpha `Vehicle` drill 15 items + title + bar clean, back rebuilds
  294 with scope `Global`, 4x5s watch stable (no flip, bar clean). full-grid
  FindText stays ~4-9s throughout - that is the PRE-EXISTING UI-thread peg
  (measured 2026-10-09 at 296 cards), not a regression of this change, and
  it means the probe's settle loop can never gate on "<1.5s walks": it is a
  paced delay with heartbeats (the first run died silently at the tool
  timeout because its 30x10s cap had no progress prints - heartbeats added,
  cap 15).

## focus-free verification: menuless protocol (2026-10-07)

- **never open a flyout while the user is working**: with the window parked
  offscreen (-4000,-4000) a `MenuFlyout` renders clamped at the screen's
  TOP-LEFT corner, visibly, and it activates (focus steal) - the user called
  it "dropdown coming in the top left and stealing focus". while minimized
  the popup is never created at all. so the only acceptable interactive
  check while the user is at the machine is MENULESS: Global card count +
  direct-navigate drills (every single-source merged card skips the chooser:
  `Celebration` `Vehicle` `General` `Windows 11` `Book Illustration`
  `Backgrounds` `dark academia wallpaper`; `Daily` `Minimal` were retired
  2026-10-09 with the category-rejection list) - zero scope menu, zero chooser,
  zero popup.
- one menuless pass takes ~90s and verified everything on the CI build
  (2026-10-07): Global = **112** cards, scope `Global`, no error; drill
  titles `Celebration wallpapers` / `Batman` / `Wallhaven` / `WallpaperHub` /
  `ArtStation Wallpaper` / `Pixabay` / `Pexels` / `Bing Wallpaper Archive` /
  `Simple Desktops`; grids 30 / 15 / 18 / 6 / 44 / - / - / 8 / 18; loud
  `StatusInfoBar` key errors on Pixabay + Pexels; wallhaven chips
  `General` `Anime` `People` present; hygiene = 0 popups ever, 5ms max
  focus exposure, foreground never left with the app. probe scripts ran
  from `C:\tmp` (never committed) - rebuild from the recipe here if needed.
- **silent process death while probing - OPEN (2026-10-07, root cause not
  found; the earlier "external close" resolution is DISPROVEN)**: three real
  deaths, ALL while a probe drove rapid menuless drills (~5-10s per page for
  minutes); idle instances never died on their own ("it's on for some time
  now, not dying"). every real death had the same signature: **exit=0** off
  the held handle, no `app.log`, no WER/System/Defender trace, and `main()`
  returned normally (cdb stack: `ucrtbase!common_exit` <-
  `Aura_exe!__scrt_common_main_seh`) = the WinUI message loop ended ITSELF
  (not `Environment.Exit`, not a crash); the whole UIA tree reads ABSENT
  ~500ms before the process exits, so the window dies first. ruled out:
  external `WM_CLOSE` (the closing handler is `HKCU\SOFTWARE\Aura`'s only
  writer and its last-write stayed at the PREVIOUS day's 20:49 on every
  death day - nothing closed the window), `Environment.Exit`, the hot-reload
  `Application.Current.Exit()` (invoke logs show only `CategoriesButton` +
  `MergedCategory` were ever invoked), unhandled managed exceptions (they
  log + dialog + survive). one run caught the app calling `PostThreadMessageW`
  at the death moment (message value not captured). keep while parked:
  `WS_EX_TOOLWINDOW` (hides taskbar + alt-tab entry, strip in the
  end-restore). truth-first recipe: launch with `Start-Process -PassThru`,
  read `$proc.ExitCode` off that held handle - **0 = clean main-return death
  | 1 = external force kill | 0xC0000354 = STATUS_DEBUGGER_INACTIVE = a
  debugger killed it (probe ARTIFACT, discard the run) | any other negative
  = NTSTATUS crash** - and the registry last-write check (only a close
  writes park coords). status: THREE real exit=0 deaths on 2026-10-09 (artstation probe
  drill phase right before the Book Illustration drill + the pexels probe's
  runs 1 and 2, both mid-drill-sequence during the pre-existing UI-thread
  peg above; every paced re-run passed - pexels run 3 went green end to
  end). same signature, root cause still open; otherwise nothing parked;
  resume = the cdb loop-ender catcher, recipe in automata
  `windows-ui-automation`.

## xamlcompiler quirk: invalid property = SILENT exit 1 (2026-10-06)

`Orientation="Horizontal"` on a `ScrollViewer` (no such property - `Orientation`
belongs to `StackPanel`) makes `Microsoft.UI.Xaml.Markup.Compiler` **crash with
exit code 1, empty stdout/stderr, and NO diagnostics anywhere** - not in
`obj/**/output.json` (it keeps its last successful-run timestamp), not the
event log. MSBuild only reports the wrapper `MSB3073`, so the failure reads
like a broken toolchain or flaky env, not bad XAML (2+ builds + a direct
XamlCompiler.exe run to confirm determinism).

diagnosis order that worked: (1) verify `output.json` has zero `ErrorCode`
entries, (2) revert the last XAML edit -> build -> green = the edit is guilty,
(3) bisect that edit's attributes. the compiling form:

```xml
<ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled" ZoomMode="Disabled">
    <StackPanel x:Name="ModeButtonsPanel" Orientation="Horizontal" Spacing="8"/>
</ScrollViewer>
```

## desktop app (winui)

- run-from-CI build: artifact name is `Aura-x64` (no `.zip`), the file inside
  is `Aura-x64.zip`; `gh run download <id> -n Aura-x64.zip` fails with "no
  artifact matches". retention is 1 day - older runs must come from the
  `latest` release asset instead.
- startup autolaunch already ships: Settings -> "Start with Windows" toggle =
  HKCU `...\CurrentVersion\Run` value `Aura` -> current exe path (unpackaged
  app, so registry Run key, not StartupTask). no code change needed to add it;
  just make sure the exe lives somewhere stable (temp dirs get wiped and the
  Run key silently points at nothing).

## security: dependabot transitives (2026-10-02)

- 18 open alerts fixed in one pass (commit `78583dd`): js-yaml (4.1.1 ->
  4.3.2), brace-expansion (1.1.13 -> 1.1.21), fflate (0.6.10 -> 0.6.11 AND
  0.8.2 -> 0.8.3), nanoid (3.3.12 -> 3.3.18), browserslist (4.28.1 ->
  4.28.7), baseline-browser-mapping (2.10.x -> 2.11.0), postcss (8.5.14 ->
  8.5.23, incl. next's own copy), smol-toml (1.6.1 -> 1.7.1), @babel/core
  (7.29.0 -> 7.29.6), @humanfs/node (0.16.7 -> 0.16.8).
- **overrides live in `web/pnpm-workspace.yaml`, NOT the package.json
  `overrides` block.** editing package.json `overrides` alone is silently
  ignored (pnpm reports "Lockfile is up to date, resolution step is skipped"
  while nothing changes) - edit `pnpm-workspace.yaml` `overrides:`, keep the
  package.json block mirrored for symmetry, then `pnpm install` and verify
  resolved versions in `pnpm-lock.yaml` directly (grep old vs new).
- two fflate lines coexist (0.6.x via three-stdlib, 0.8.x via @types/three) -
  a single flat override would break one side; scoped selectors
  (`fflate@^0.6.0: 0.6.11` / `fflate@^0.8.0: 0.8.3`) fix both.
- next>postcss pins next's bundled postcss separately - bump it alongside the
  plain `postcss` override or the postcss advisory stays open.

## deploy (vercel)

- project: `aura`, domain `https://aura2027.vercel.app`. owner account: check
  `automata-private\vercel.com\vercel-usage-table.ps1` (repo -> profile mapping) - no hardcoded
  emails here, this repo is PUBLIC.
- **Root Directory is `web`** -> `vercel link` and `vercel deploy --prod` must
  run from the REPO ROOT, never from `web/` (deploying from inside `web/`
  uploads a tree with no `web/` dir and fails with: `The specified Root
  Directory "web" does not exist`).
- helper flow (from repo root):
  `mainframe\vercel-account.ps1 run <owner-email> link --yes --project aura --scope <team-slug>`
  then `... run <owner-email> deploy --prod` - watch for `✓ Ready` + the
  `Aliases https://aura2027.vercel.app` line, then curl the domain to verify.
- the link writes root `.vercel/` + `.env.local` (OIDC token) - both are
  gitignored (root `.gitignore` `.vercel` + `.env*`), never commit them.

## slideshow basis: Latest/Category selector with a category checklist (2026-10-10)

user ask: the Set-slideshow dialog should choose the slideshow's BASIS -
rotate through everything new (Latest) or through ONE OR MORE CATEGORIES
(Category) - replacing the old single-category dropdown. the pending note in
the taxonomy-less section above is discharged here: the checklist universe
consults `IsCategoryExcluded`, so bing/SD get no ticks and take the LOUD
per-platform skip instead.

- dialog (`SlideshowPage.ShowSlideshowSettingsDialog`): a `Slideshow basis`
  radio pair (Latest/Category) + a `Pick one or more categories` checklist,
  lazily loaded on first Category select; the old category ComboBox is GONE.
  outer ScrollViewer MaxHeight 460 (Set button stays outside the scroll),
  checklist inner ScrollViewer MaxHeight 300.
- universe = `SlideshowCategoryCatalog` (new service): the DIALOG's 7
  category-capable platforms only (bing/SD excluded through the shared
  exclusion list), merged case-insensitively + alphabetically = **257
  names** today. it PUBLISHES the shared index snapshots
  (SetPixabayCollections / SetWallpaperHubCollections /
  SetAlphaCodersCategories / SetChannels / SetPexelsDiscoverTerms) so one
  fetch serves dialog + Categories page, is cached for the process
  lifetime, and any failed or empty source THROWS - the dialog shows
  `Category list failed to load: <reason>` (loud, never a silently partial
  list). per-site naming: backiee drops `8k-ultrahd`/`AI Generated` (no
  sitemap slugs); the alpha legacy trio 4K/Harvest/Rain stays and
  ResolveKey maps it to the live keys 4k/harvest/rain; artstation keys =
  `channel:<id>`; public platforms keep their GetModes keys.
- keys (`slideshow_settings.json`): `DesktopSlideshowBasis` +
  `DesktopSlideshowCategories` (+ `LockScreen*` twins); a missing basis =
  Latest, so every pre-feature file keeps working. a Latest save keeps the
  ticked list (switching back restores it), normalizes `_desktopCategory`
  to `Latest Wallpapers`, and still writes the legacy
  `DesktopSlideshowCategory` for RecoverSlideshowStickers.
- loader: Category basis resolves each tick through `ResolveKey` and loads
  per platform (`LoadCategoryTickAsync` - backiee `category=<slug>`
  paging_list, alpha `ScrapeWallpapersByCategoryAsync(key,batch,batch)`
  SEQUENTIAL (its static scrape cache is not thread-safe), artstation
  `GetChannelProjectsAsync`, public `GetWallpapersAsync(platform,batch,key)`).
  every item is tagged `Category = <ticked name>` so history stickers name
  the tick (`Nature|Backiee`), not the mode. bing/SD = LOUD skip
  `categoryless platform - no categories exist here (use the Latest basis,
  or untick it)`; a ticked name that no longer exists on a site = quiet
  skip; a ticked fetch failure = `platform [tick]: reason`.
- guards: Category basis + 0 saved categories at Start = loud not-started
  error; dialog save validation = checklist still Loading -> `Categories
  Still Loading` error dialog, Failed -> `Category List Failed` + reason,
  0 ticks -> `No Category Selected`. status line = `{n} platforms -
  Category: A, B (Refresh: ...)`; Latest keeps its exact old format.
- two bugs the menuless probe caught (both fixed in this change): (1) the
  checklist success path never re-ran `ApplyBasisVisibility`, so a
  SUCCESSFUL load landed in a collapsed ScrollViewer - invisible to the
  user AND to UIA (the dialog looked broken while the load had worked);
  (2) the running-with-skips InfoBar rendered as Error `Slideshow not
  running` because the warning fires BEFORE the timer exists
  (`DesktopRunning` is timer-backed) - `DesktopStarting`/
  `LockScreenStarting` now count as running for bar severity (Warning +
  `Slideshow warning`).
- verified menuless with probe `C:\tmp\aura-slideshowbasis.ps1` (stays in
  C:\tmp): full PASS 2026-10-10 with the user's exact config - radios
  Latest/Category present, 257 checklist boxes (8 spot names in, 5 stale
  names out incl. the retired Daily/Minimal), tick Nature+Space -> Set ->
  status `8 platforms - Category: Nature, Space (Refresh: 12 hours)`,
  `Loaded 115 wallpapers from 8 platform(s) (2 failed)`, amber warning bar
  carrying both loud skips, settings basis=Category cats=[Nature,Space],
  a Next-button history row `Nature|Backiee|Desktop/Slideshow`, reopen ->
  Latest -> Set keeps the ticked list + exact Latest status, restore +
  relaunch -> exact `Refresh: 1 Minutes` status. vision (zengate, 3
  shots): dialog renders, Category status + amber skip bar + real preview
  photo, final restored state clean. NOTE: shot 1 shows the dialog's TOP
  slice (platform checklist) - the basis block sits below the 460px
  scroll fold; UIA carries that part of the proof.
- probe gotchas (menuless, WinUI): WinUI RadioButton exposes
  SelectionItemPattern ONLY - `Select()` is the way to check a radio
  (Invoke/Toggle read nopattern); group the card toolbars GEOMETRICALLY
  (Pane parents report empty RuntimeIds; a row of exactly 4, x-stride 56,
  leftmost/topmost = desktop, child 2 = Edit, child 1 = Next) and poll
  until the geometry is stable (cards split rows mid-arrangement);
  wallpaper_history.json is capped at 200 rows - find new rows by
  Timestamp, never by -Skip the baseline count; post-Set asserts must poll
  PAST the first wallpaper download (Start awaits it, 10-20s, and
  SaveSettings runs after Start returns); a probe's first launch can hang
  on the splash window (title = exe path, UIA text = 0, Responding=True)
  - kill + relaunch.

### All categories master + bigger dialog (2026-10-10, same day)

user follow-up: "wheres the all category checkbox ... make that all
selected by default ... the modal is kinda small and ive to scroll a lot".

- **`All categories` master tick** = `SlideshowCategoryCatalog
  .AllCategories` (the const IS the sentinel string, checkbox label too).
  it sits OUTSIDE the checklist scroll (visible whenever the list is
  Ready), is checked by DEFAULT whenever the saved list is empty (fresh
  Category = fully working out of the box, zero scrolling), unchecking ANY
  category drops it, checking every category lifts it back. an all-checked
  checklist saves as the ONE sentinel `["All categories"]` (children are
  the save-time source of truth - the count match collapses them), never
  the ~270-name list.
- **loader fast path**: sentinel ticked OR every one of a platform's own
  names ticked = NO filter -> one latest-feed fetch, items tagged
  `Category = All categories` (~7 requests per start, not ~270). the
  per-platform Latest switch was factored out of BOTH loaders into
  `LoadLatestFeedAsync(platform, category, mode, batch)` (identical code
  moved) and serves the Latest basis + this path. the loud bing/SD
  categoryless skip still runs FIRST (All mode skips them too), and
  `GetPlatformNames(platform)` (new catalog API over the per-platform
  lists, built before the universe merge) powers the per-platform
  all-ticked check.
- **bigger dialog**: `dialog.MaxHeight/MaxWidth` derive from the XamlRoot
  content (`availH - 96` / `availW - 96`, clamped 480..1040), the outer
  ScrollViewer lost its fixed 460 cap (the dialog clamps itself), the
  checklist inner scroll went 300 -> 420, the 9 platform checkboxes wrap
  into horizontal rows of 4 (was 9 vertical rows ~230px), the two basis
  radios sit on one row. at the default 900x640 window the default path
  needs no scrolling at all (Set stays outside the scroll).
- re-verified by the updated `C:\tmp\aura-slideshowbasis.ps1` (the
  settings patch now also normalizes basis/cats so step 5 sees the fresh
  default; flow = default-All assert -> untick All -> Nature+Space cycle
  -> All cycle -> Latest cycle): full PASS first run 2026-10-10 -
  `All master default: On` + spot children On, subset save
  `cats=[Nature,Space]`, reopen restore `All=Off Nature=On`, sentinel
  status `8 platforms - Category: All categories (Refresh: 12 hours)`,
  settings `cats=[All categories]`, history sticker `All categories`,
  Latest keeps the sentinel, final restore exact.
- `C:\tmp\aura-slideshowbasis-tall.ps1` = the 900x1200 dialog shot for
  the UX change (vision: All checked, category rows below it,
  Latest/Category one row with Category selected, platforms rows of 4,
  Set/Cancel, no errors). capture trap: a shot taken right after
  resizing the PARKED window to 900x1200 caught an unpainted swapchain
  (11KB flat white, vision confirmed empty content) - settle 2s +
  recapture produced the real 121KB frame; both restores end with exact
  `Refresh: 1 Minutes` status.
- **pixabay latest-route fix (same day, found by the user's live run)**:
  the user added Pixabay to the platform list (8 -> 9) and got `Pixabay:
  No pixabay collection named 'latest' is loaded - the collections index
  fetch failed` in the warning bar - mode `latest` (Latest basis AND this
  fast path) fell into the collections-name lookup and ALWAYS threw for
  pixabay (the earlier probes never had pixabay selected). fix =
  `GetPixabayWallpapersAsync` routes empty/`latest` modes to
  `GetPixabayLatestAsync` (search page + JSON-LD parse, facts in the
  pixabay section above); every other mode stays a loud collection-name
  throw. probe hardened in the same pass: platform count derived from
  the settings file (8 hardcoded -> `$platCount` = 9), startup clean
  assert (0 platform failures), both info-bar asserts require NO
  `Pixabay` line, the final-relaunch expected status builds from the
  CAPTURED original basis/cats (was hardcoded Latest - caught by run 1
  showing `9 platforms - Category: All categories (Refresh: 1 Minutes)`
  as a probe-assert miss, app state was correct), and the settings
  patch also normalizes the legacy `DesktopSlideshowCategory` to
  `Latest Wallpapers` (leaving the user's `All categories` there fed
  the restored-Latest loader a foreign mode string). full PASS second
  run 2026-10-10: startup `Loaded 324 ... 0 platform failures`, both
  bars bing/SD-only, final restore + relaunch exact with the user's
  real `Category: All categories` state loading 324/324 clean.

### history pill = the item's own category (2026-10-10, same day)

user follow-up: every row under the `All categories` basis showed the
SAME `All categories` chip - that string was the SLIDESHOW's basis, not
the picture's category. the chip now shows the item's OWN category when
its source knows it, and NO chip when it does not (empty = no pill).

- **one choke point owns the contract**: `WallpaperHistoryService.
  AddEntry` runs every value through `NormalizeCategory(platform, value)`
  - the three mode labels (`latest`, `Latest Wallpapers`, the old
  `All categories` blanket stamp) are fetch context -> empty = no pill;
  any real value (backiee ThemeCat slug, alpha's `4k` key, wallhaven's
  per-hit facet, a tick name) canonicalizes via the catalog's new
  `TryCanonicalName` (reverse lookup: the name whose key equals the
  value, e.g. `fantasy` -> `Fantasy`, `4k` -> `4K Wallpapers`).
  `TryCanonicalName` never throws (unlike ResolveKey): unloaded
  universe (fresh Latest-basis install never opened the dialog) = null
  -> the source value stays verbatim, still honest, re-canonicalizes at
  the next Category-basis startup.
- **slideshow call sites dropped the `category:` arg entirely** (all 4 -
  desktop + lock): AddEntry's documented fallback to
  `wallpaper.Category` takes over, so the Latest basis no longer stamps
  its mode label onto rows - and wallhaven items keep showing their
  facet (`General`/`Anime`/`People`) even under Latest. the fast path
  also stopped rewriting items; it passes source values through.
- **wallhaven now records its per-hit facet** (the API's `category`
  field general/anime/people -> `ToTitleCase` into the item's
  `Category`; the parser's own `wallpaper` description-default maps to
  empty) - the one source that gained a category in this pass.
  known-vs-unknown today: backiee + wallhaven + alpha always pill,
  pixabay/pexels/artstation/wallpaperhub never do (their latest
  surfaces carry no per-item category), bing/SD are loud-skipped under
  Category anyway.
- **startup recovery migrates legacy rows** (runs every startup,
  idempotent): the sentinel stamp recovers the item's real category
  from the live batch (same id match as the Platform backfill) then
  normalizes; every other value normalizes too (`latest`/`Latest
  Wallpapers` -> empty, raw slug `fantasy` -> `Fantasy`); the settings
  backfill stays NULL-only (a recorded "" = the source knows nothing -
  never re-stamped) and its fill value is normalized (a Latest-basis
  install's `Latest Wallpapers` legacy field fills nothing, correct).
- probe (same file): the All-cycle history assert now wants a new row
  WITHOUT the sentinel (plus non-empty Category required when the row
  lands on Backiee/Wallhaven/AlphaCoders - their sources always know),
  and the final assert wants 0 mode-label rows file-wide. full PASS
  2026-10-10: All row `Anime|Backiee` (slug -> canonical), alpha rows
  `4K Wallpapers` (key `4k` -> canonical), tick rows `Space|Backiee`
  unchanged, `mode-label rows left: 0`, all 16 legacy `fantasy` rows
  migrated to `Fantasy`.
