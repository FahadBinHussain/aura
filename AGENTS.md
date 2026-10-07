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
- **pixabay**: 20 documented category values (docs `category str` row) =
  `PixabayCategories`; every mode maps lowercased-or-default `backgrounds`.
- **wallpaperhub**: 17 collections (id+title) = `WallpaperHubCollections`; a
  mode that matches a collection title routes to `/collections/<id>` and parses
  `pageProps.collectionWallpapers` (identical `{entity:...}` wrappers as
  `initWallpapers`); anything else = `/wallpapers` (`initWallpapers`). both
  SSR shapes serve everything at page 1. `?tags=` is SSR-ignored - never build
  fetch URLs on it.
- **alphacoders**: uniform `https://alphacoders.com/<slug>-wallpapers?page=N`
  (`4k` special -> `/resolution/4k-wallpapers`; plain `4k-wallpapers` = 404).
  the 63-entry catalog lives in `AlphaCodersService.Categories` - the merged
  grid AND `AlphaCodersGridPage` titles both read it; deep-linked categories
  leave the 3 quick buttons unselected instead of faking 4K.
- **artstation**: the subject-matter taxonomy is POST + CSRF (anonymous =
  `Invalid CSRF Token`; GET filter params silently ignored - all baselines stay
  47230) => honest **query entries** (wallpaper/landscape/nature/space/abstract)
  through GET `api/v2/search/projects.json?query=` (`SearchProjectsAsync`,
  cards map `smaller_square_cover_url`/`url`, `is_adult_content` filtered).
  `ArtStationGridPage` takes the query as its navigation parameter; sorting
  buttons exit query mode (and neither chip is highlighted while in it).
- **bing + simple desktops**: proven zero taxonomy => one honest entry each
  (`Daily` / `Minimal`) so no scope ever renders an empty grid; their fetchers
  ignore the mode.
- **pexels**: API has no categories at all (Curated/Nature/Space stay), the
  website is Cloudflare-walled (403 / "Just a moment") - documented, not retried.
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
  SearchProjectsAsync` / `PublicWallpaperService.GetWallpapersAsync`) through
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
    keyless install shows exactly 2 lines (Pexels + Pixabay API keys). the
    fill failure paths never touch the session cache, so the next Categories
    open retries them. verified menuless + vision on the local build
    (2026-10-07): the complaint cards `Aura Farming..Cyberpunk` all real,
    alpha 0/63 failed, 0 duplicates except one honest source overlap -
    `animal` and `cat` pages serve the SAME first thumb
    (`thumbbig-20658.webp`, curl-verified), so those two cards legitimately
    show one photo.

## focus-free verification: menuless protocol (2026-10-07)

- **never open a flyout while the user is working**: with the window parked
  offscreen (-4000,-4000) a `MenuFlyout` renders clamped at the screen's
  TOP-LEFT corner, visibly, and it activates (focus steal) - the user called
  it "dropdown coming in the top left and stealing focus". while minimized
  the popup is never created at all. so the only acceptable interactive
  check while the user is at the machine is MENULESS: Global card count +
  direct-navigate drills (every single-source merged card skips the chooser:
  `Celebration` `Batman` `General` `Windows 11` `Wallpaper` `Backgrounds`
  `Curated` `Daily` `Minimal`) - zero scope menu, zero chooser, zero popup.
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
  writes park coords). status: paused, nothing running; resume = the cdb
  loop-ender catcher, recipe in automata `windows-ui-automation`.

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
  `mainframe\vercel-usage-table.ps1` (repo -> profile mapping) - no hardcoded
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
