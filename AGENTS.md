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
  overlay (same id/style as the category page).

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
