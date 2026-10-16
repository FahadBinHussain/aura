# adding a new wallpaper site to Aura

the repeatable path for porting a site end to end: recon -> reversal -> registry row ->
fetcher -> category audit -> menuless verification -> docs -> publish.

the worked example throughout is **Bing Editions** (`bingwallpaper.anerg.com`, ported
2026-10-16). its full reversal evidence lives in
`automata-private/bingwallpaper.anerg.com/AGENTS.md`.

the whole port is ONE registry row plus ONE private fetcher. every platform surface
(picker card, drill routing, slideshow checklist, Categories scope, thumbnails) derives
from the registry, so nothing else needs touching.

---

## 0. recon: what the site actually is

before writing any code, answer these with live requests, not guesses:

1. **surface inventory** - what top-level browse axes exist (editions/countries, months,
   tags, collections, feeds)? the anerg case: 11 country editions, each carrying its two
   newest months, plus `/archive/<code>/<YYYYMM>` month pages back to `201110`.
2. **paging semantics** - how does page N differ from page 1? is there an end of feed?
   anerg: page 1 = the edition page (two newest months), page N>=2 = month
   `newest - N`; an empty list or a 404 month = end of feed.
3. **item markup** - title, thumbnail, detail url, full-size url. capture the real HTML
   to `C:/tmp/<site>/` and validate the regex against it in PowerShell BEFORE writing C#.
4. **full-size url** - is it on the item page, derivable from the thumbnail, or an api
   field? anerg: derivable - the imgproxy thumbnail path ends in base64 chunks that
   decode to the public origin.
5. **transport** - does the site pass .NET `HttpClient` with the app's UA
   (`Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0`)? if it 403s .NET but passes
   `curl.exe`, the platform needs `CurlClient` (cara/pixabay/pexels precedent). test
   both, side by side, and record which one won in the evidence file. anerg: plain
   .NET everywhere, no curl, no key.
6. **counts** - items per page, months/galleries per index, so the verification step has
   real numbers to assert.

keep the raw HTML you captured; the verification probe re-parses shapes from it.

## 1. identity: name it so it cannot collide

the picker already lists 26 sites and two of them are called "Bing" - the Microsoft feed
is **`Bing Wallpaper Archive`** and the site's own picker label for the third-party
archive is literally "Bing editions", so the port is **`Bing Editions`**. when a new site
shares a brand with an existing one, pick the name the site itself uses and make sure
both cards stay visible in the picker (the probe asserts both).

## 2. taxonomy audit -> `CategoryCapable`

decide what a *category* is here: a browsable content taxonomy the site itself curates.
not: a sort, a content-type filter, a size class, a time axis, or a search query over
unbounded UGC. anerg's only axes are country edition (region) and month (time) - no
content taxonomy - so the row ships `CategoryCapable: false` and the reason + date go in
the row comment.

**judge it with the user before flipping it.** present the evidence (what the site's own
nav offers, what each axis really is) and let them rule; a `false` row is rejected from
the Categories page scope menu, the merged card grid, and the slideshow category
checklist, while the platform's OWN grid chips + picker card stay untouched.

**how to present the audit** (standing format, set 2026-10-16):

1. **link the site** - the live URL(s) that were reversed (the landing/edition page, a
   month or index page, one item page) - so the user can click through and check the
   axes themselves before ruling.
2. open with the verdict + the ask: `Judge the category for <Platform> - my verdict:
   reject (Nth "no" / accept). Evidence below.`
3. numbered evidence, <= 5 items: each axis the site actually offers and what it really
   is (region / time / content-type / sort / search query), plus the best "fake
   alternative" spelled out (e.g. "the 11 country editions as category cards").
4. say what each ruling changes: accepted = which counts move (scope menu, Global
   merged cards, checklist universe); rejected = which counts stay.
5. ship the row `CategoryCapable: false` with a `pending user audit (<date>)` comment
   BEFORE the ruling - confirming it is then a no-op, and only a surprise "accept"
   needs the bool flipped + counts re-verified.

## 3. the fetcher contract

one private method per platform, usually
`Get<Site>WallpapersAsync(int page, string mode, CancellationToken ct)`, registered as the
row's `Fetch`. the contract every public grid page relies on:

- **page 1** = the site's newest/default listing for the requested mode.
- **page N (>=2)** = the next slice. an empty list means "end of feed" and the page stops
  asking - so a 404/empty month must return an empty list, never throw.
- **unknown mode** = throw with the offending value. never silently fall back to a
  default: the grid shows the throw reason in its loud `StatusInfoBar`.
- **no silent degradation**: if the full-size origin cannot be derived, throw
  (`InvalidOperationException`) instead of quietly serving the thumbnail as the original.
- tag each item with its honest `Category` (or leave it empty - the history pill is
  derived from it; a guessed pill is worse than none).

anerg specifics worth copying: the `(Label, PagePath, Code)` edition table drives both
`Modes` and the fetcher's mode lookup (`FirstOrDefault` over the table; a miss throws);
the newest month per edition is cached (`BingEditionsMonthLock` +
`BingEditionsNewestMonth`) because page 1 discovers it and page 2 needs it; month
arithmetic is a tiny `ShiftBingEditionsMonth` helper with a floor at `201110`.

## 4. register: exactly one row

`winui/Services/PublicWallpaperService.cs` holds the registry - one `PlatformSpec` row
per site (`Name`, `Kind`, `CategoryCapable`, `LiveIndex`, `SoloThumbGate`, `Description`,
`Modes`, `Fetch`, `ItemCategory`). add the row next to its siblings, sorted into place.
that is the whole wiring: picker list, not-implemented dialog text, categories scope,
drill routing, slideshow checklist, thumb gates.

## 5. build

```
dotnet build winui\Aura.csproj
```

no `-p:Platform`: that lands in `bin\x64\Debug\win-x64\` while the probes launch the flat
`bin\Debug\Aura.exe`. assert the exe's `LastWriteTime` is fresh before trusting a probe
run - a stale exe has faked success before.

## 6. verify menuless (UIA only, zero focus steal)

probe template: `C:/tmp/aura-bingeditions.ps1` (probes stay in C:/tmp, never committed).
what it must cover, in order:

1. **settings dance** - back up `%APPDATA%\Aura\slideshow_settings.json`, pause the
   user's ticker (`DesktopSlideshowInterval` -> `12 hours`, basis -> `Latest`, and
   normalize the legacy `DesktopSlideshowCategory` to `Latest Wallpapers` so the
   restored-Latest loader is not fed a foreign mode string), restore byte-exact + relaunch
   at the end, in a `finally` so a failed run still restores.
2. **hidden-start aware launch** - Aura now starts hidden in the tray, so
   `MainWindowHandle` stays 0: launch once, launch again a few seconds later to signal
   `ShowFromTray`, then wait for a handle that HOSTS UIA text and is still the main
   window. park it offscreen (`SetWindowPos -4000,-4000`) and resize to `900x3600` so the
   picker's ItemsRepeater realizes every card.
3. **picker** - the new card is present, and any same-brand sibling is still there.
4. **grid page** - invoke the card, then assert `TitleTextBlock` = the platform name,
   `DescriptionTextBlock` names the site, the mode chips (read every **Button** by name -
   `ModeButtonsPanel` is a bare StackPanel and has NO UIA peer) match the expected mode
   list, `WallpapersGridView` realized items >= 20, and `BarText` = clean (reads both
   `ErrorTextBlock` and `StatusInfoBar` child texts).
5. **mode switch** - invoke a second chip and assert the reload brought different items
   with a clean bar.
6. **page 2** - **resize the window down to `900x950` first**: a 3600px-tall window fits
   the whole first page, so the load-more (fires within 360px of the bottom) can never
   trigger. then walk the grid with `ScrollItemPattern.ScrollIntoView()` on the LAST
   realized `ListItem`, one round at a time, comparing realized captions (read the
   template's **Text** elements - a `ListItem`'s own Name is just
   `Aura.Models.WallpaperItem`) against the page-1 set. >= 10 new captions means a real
   second page (a page-1 tail can explain at most a handful); the anerg run walked 2
   rounds and realized 20 August-2026 captions.
7. **detail page** - invoke the first tile (walk up from the ListItem to the first
   element with `InvokePattern`), assert the public detail marker `PlatformTextBlock`,
   the resolution text (`3840x2160` for anerg), a clean bar, `ViewOnWebButton` present,
   then back via `BackArrowButton`.
8. **back keeps the mode** - the public grid page is not navigation-cached, so a back
   from a detail builds a FRESH page; the probe asserts the previously selected mode came
   back (France captions, not the default United States).
9. **log hygiene** - read the app.log region since launch and assert no error lines for
   the new platform.
10. **capture + vision** - `automata-private\window-capture\Capture-WindowBackground.ps1`
    at the target handle (never a full-screen grab), then read the PNG: real tiles, chips,
    title, zero banners.
11. **cleanup** - kill, restore settings byte-exact (sha256), relaunch, assert one hidden
    process and the foreground restored. the relaunch may race an abandoned
    single-instance mutex, so retry the launch up to 3 times until a process sticks.

### probe traps that have bitten (all hit on the anerg port)

- a bare `StackPanel` has no UIA peer -> read Buttons by name, not by the panel's id.
- `ListItem.Current.Name` is the model type name -> read the item template's `Text`
  children.
- a tall window silently disables paging -> resize before the scroll phase.
- `"round $round: ..."` is a PowerShell PARSE error (`$round:` reads as a scope) -> use
  `"round ${round}: ..."` or avoid the colon.
- `Get-Process Aura` right after killing the old instance can take the "second launch"
  path and exit -> retry.
- the settings patch must normalize the legacy category field (see step 1).

## 7. document

- `automata-private/<site>/AGENTS.md` - the reversal record: endpoints, markup shapes,
  the origin-derivation recipe, transport results, counts, traps. anything the code
  depends on belongs here, so the next agent does not re-reverse it.
- `aura/AGENTS.md` - a dated section: what was ported, the registry row, the counts, the
  verification result, and what it supersedes.

## 8. publish + ship

- `dotnet publish winui\Aura.csproj -c Debug -o "%LOCALAPPDATA%\Programs\Aura"` (stop the
  running Aura first - the exe is locked while it runs), then relaunch it hidden.
- commit + push; if the change is user-visible in the web app too, deploy and verify the
  live URL.

## definition of done

- [ ] registry row + private fetcher, nothing else wired by hand
- [ ] `dotnet build winui\Aura.csproj` green, `bin\Debug\Aura.exe` fresh
- [ ] taxonomy judged with the user; `CategoryCapable` matches the verdict + reason
- [ ] menuless probe: PASS on picker / title / description / chips / page 1 / mode switch
      / page 2 / detail / back / bar clean / settings restored byte-exact
- [ ] vision confirmed on a window-handle capture
- [ ] `automata-private/<site>/AGENTS.md` + `aura/AGENTS.md` section written
- [ ] published, committed, pushed
