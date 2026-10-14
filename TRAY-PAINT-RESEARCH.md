# How explorer decides to RENDER a notify icon on Windows 11 build 26100 (24H2) — and why a never-before-seen icon doesn't paint

Research report. Every claim carries a source (URL, file path + line/offset, or "local dump" with reproducible method). Anything inferred is tagged **INFERRED**. Anything I could not establish is tagged **NOT ESTABLISHED**.

## Verdict up front

1. **The render decision is made entirely inside the shell** — specifically in `Taskbar.dll` (state/placement) and the XAML SystemTray in `Taskbar.View.dll` (visible pixels), both loaded by explorer.exe on this build — **not** by any app-visible API call. Local string dumps on this machine prove ownership of every registry value, window class, and UI area name involved (§1, §7).
2. **No app-side action is known — documented, first-party, or reverse-engineered — that forces an icon to paint.** All documented app-facing levers (`NIM_SETVERSION`, `NIF_STATE/NIS_HIDDEN`, `ITrayNotify::SetPreference`, promotion) control *placement/visibility of already-rendered icons*, not pixel rendering (§4).
3. **`IsPromoted` is a placement cause for already-painted icons, not a render gate. `IconSnapshot` is an effect of having been painted, not a cause.** Evidence in §3.
4. **Why a never-before-seen icon fails on this build: NOT ESTABLISHED.** No public source documents the NIM_ADD→first-paint path on 26100, and no matching public bug report exists. What the evidence does show is that the failure is shell-side (per-icon shell state), not app-side (§2).
5. **Single most decisive next experiment:** dump the paint store for a working slot vs the phantom slot (`iImage` + toolbar image-list pixels, or the XAML SystemTray item, depending on which window owns the pixels). Full procedure + decision table in §5. About an hour.

---

## 1. The decision path, with a source per step

### Step 1 — API entry: `Shell_NotifyIconW` in SHELL32.dll
- `NIM_ADD` registers the icon; returns TRUE if the shell accepted the registration. **Source:** Microsoft Learn, Shell_NotifyIconW — https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw
- The docs say nothing about rendering. `NIM_SETVERSION` "must be called every time an icon is added" — but it only selects callback message semantics (mouse/keyboard event format), never pixels. **Source:** same page, Remarks; and NOTIFYICONDATAW `uVersion` — https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
- Locally, `C:\Windows\System32\SHELL32.dll` contains the Unicode strings `Shell_TrayWnd` and `ToolbarWindow32` — i.e. shell32 locates the tray windows to service the call. **Source:** local dump, §7.

### Step 2 — explorer's internal object: `CTrayNotify` / `ITrayNotify(8)`
- explorer implements `ITrayNotify` (IID `{FB852B2C-6BAD-4605-9551-F15F87830935}`) and `ITrayNotifyWin8` (IID `{D133CE13-3537-48BA-93A7-AFCD5D2053B4}`), coclass `CLSID_TrayNotify {25DEAD04-1EAC-4911-9E3A-AD0A4AB560FD}`. Methods: `RegisterCallback(INotificationCB*)`, `SetPreference(NOTIFYITEM const*)`, `EnableAutoTray(BOOL)` (+ `UnregisterCallback`, `DoAction` on the Win8 vtable). **Sources:** Geoff Chappell (names taken from Microsoft symbol files) — https://www.geoffchappell.com/studies/windows/shell/explorer/interfaces/itraynotify/index.htm ; Chromium's mirror of the same interfaces — https://github.com/chromium/chromium/blob/main/chrome/browser/ui/views/status_icons/status_tray_state_changer_win.cc ; vtable-faithful C++ gist (tested Win10 15063) — https://gist.github.com/ysc3839/25e8ed113c4e975b6781c9759ed4ee87
- `NOTIFYITEM { pszExeName, pszTip, hIcon, hWnd, dwPreference, uID, guidItem }`, where `dwPreference`: 0 = "hide when inactive", 1 = "always hide", 2 = "always show" (the UI strings behind those values are spelled out in https://gist.github.com/anaisbetts/e90c7d89624d1b1adc72 — `NOTIFYITEM_PREFERENCE` enum comments). **Source:** https://www.geoffchappell.com/studies/windows/shell/explorer/interfaces/notifyitem.htm
- `RegisterCallback` makes explorer synchronously enumerate every tray icon and its preference back to the caller; `SetPreference` changes it. **Source:** Chromium code comments in `status_tray_state_changer_win.cc` (lines: `RegisterCallbackWin8` — "The following two lines cause Windows Explorer to call us back with all the existing tray icons and their preference").

### Step 3 — persistence/state: `HKCU\Control Panel\NotifyIconSettings`, all owned by Taskbar.dll
- Value names found **verbatim in `C:\Windows\System32\Taskbar.dll` on this build (26100)**: `IsPromoted`, `IconGuid`, `ExecutablePath`, `InitialTooltip`, `UID`, `Version`, `UIOrderList`, `IconSnapshot` (and a DACL string adjacent to `IsPromoted`, i.e. Taskbar.dll creates/ACLs these keys). **Source:** local dump, §7 (offsets ~941,118–941,250 and ~1,044,804).
- `UIOrderList` = 8-byte-aligned `TRAYNOTIFYICONID`s (little-endian) matching the NotifyIconSettings subkey names; it defines icon order; changes only apply after explorer restart; the writer module is `Taskbar.dll` (community RE, corroborated by my independent string dump). **Source:** Stack Overflow 78494726 question body — https://stackoverflow.com/questions/78494726/how-to-rearrange-the-list-order-of-system-tray-icons-on-win11
- `IconSnapshot` is a PNG of the icon; `InitialTooltip` its first tooltip; `IconGuid`/`UID`+`ExecutablePath` identify the entry. Third-party code reads these to reconstruct the tray. **Source:** Seelen-UI issue #1885 (Oct 2026) — https://github.com/eythaann/Seelen-UI/issues/1885
- Mapping `NOTIFYITEM.dwPreference` ↔ registry `IsPromoted` is **INFERRED** (same semantics, same owner module, no source states the mapping explicitly).

### Step 4 — the three areas (placement, not rendering)
- The notification area splits into **"User Promoted Notification Area"** (visible icons), **"System Promoted Notification Area"** (temporary system previews, e.g. while a balloon shows), and the **"Overflow Notification Area"** behind the **"Notification Chevron"**. **Source:** Raymond Chen (Microsoft), "Enumerating notification icons via UI Automation" — https://devblogs.microsoft.com/oldnewthing/20141013-00/?p=43863
- On this machine (26100) those four names live in **`C:\Windows\System32\en-US\Taskbar.dll.mui`** — i.e. the area UI is Taskbar.dll's, not explorer.exe's own resources. **Source:** local dump, §7.
- Official policy: "When an icon is added to the notification area on Windows 7, it is added to the **overflow section** by default... **Only the user can promote an icon** from the overflow to the notification area." **Source:** Microsoft, "Notifications and the Notification Area" — https://learn.microsoft.com/en-us/windows/win32/shell/notification-area (same doc: recommended sequence `NIM_ADD` then `NIM_SETVERSION(NOTIFYICON_VERSION_4)` — for callback semantics only).
- Registry `IsPromoted=1` = "tray icon is visible always", flips immediately without restarting explorer — **but for icons the shell is already painting**. **Source:** answered answer on Super User 1332399 — https://superuser.com/questions/1332399/where-is-the-icon-tray-notification-area-registry-stored

### Step 5 — geometry/bookkeeping: `TrayNotifyWnd` → `ToolbarWindow32`
- The classic windows still exist and answer `TB_*` messages on 26100 (given: TBSTATE read via `TB_GETBUTTON`, 32 px slot growth per registration). Strings `Shell_TrayWnd`, `TrayNotifyWnd`, `ToolbarWindow32` are present in `Taskbar.dll` on this build. **Source:** local dump, §7.
- Caveat from community RE: "After Win11 22H2... Microsoft has rewritten the taskbar UI using Xaml" and `TB_BUTTONCOUNT` reportedly stopped returning tray info for that author (SO 78494726, link above). That contradicts the TB-message observations made locally here, so treat it as a report about a different/changed window, not as settled fact.

### Step 6 — the pixels: XAML SystemTray in Taskbar.View.dll (where the answer lives)
- explorer.exe on this machine loads: `Taskbar.dll`, `Taskbar.View.dll` (`C:\Windows\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\Taskbar.View.dll`), `SettingsHandlers_DesktopTaskbar.dll`. **Source:** local module enumeration of the `explorer` process, §7.
- `Taskbar.View.dll` contains the Windows 11 XAML SystemTray type names, verbatim: `SystemTray.NotificationAreaIconsDataModel`, `SystemTray.NotifyIconView`, `SystemTray.IconViewModel`, `SystemTray.ImageIconContent` / `IImageIconContent` / `IconData` / `IconContentMetadata`, `SystemTray.ChevronSystemTrayIconDataModel2`, `SystemTray.ChevronIconView`, plus per-item data models for system icons (`VolumeSystemTrayIconDataModel`, `NetworkSystemTrayIconDataModel`, `ClockSystemTrayIconDataModel`, …) and glyph styling (`GlyphPadding`, `GlyphFontFamily`, `GlyphFontSize`). Also `GetNotificationAreaIcons`, `GetNotifyIconStackViewModel`. **Source:** local dump, §7 (identifier extraction).
- `Taskbar.dll` contains `TopLevelWindowForOverflowXamlIsland`, `Overflow`, `Promoted`, `SystemTray`, `com.microsoft.windows.extensions.xaml.systemtray`. **Source:** local dump, §7 (offsets ~1,040,712 and ~1,043,664).
- **INFERRED:** on 26100 the *visible* notify-icon pixels are produced by this XAML SystemTray view (fed by Taskbar.dll's model), while the classic `ToolbarWindow32` remains as bookkeeping/hit-test/geometry — which is consistent with every local observation (button exists, TBSTATE normal, toolbar grows, yet no pixels; multiple phantom icons sharing one `Shell_NotifyIconGetRect` rect because they aren't distinct items in the drawing model). **Which layer actually draws a working icon's pixels is NOT ESTABLISHED** — that is exactly what the experiment in §5 settles first.
- `Shell_NotifyIconGetRect` success ≠ visible: Qt's first-party code comments: "Windows 10 returns S_FALSE if the icon is hidden"; "Windows 11 seems to return a geometry outside of the current monitor's geometry in case of the icon being hidden". Seelen's testing: `S_OK` = "icons Explorer currently has", `E_FAIL` = unknown hwnd+uid; Explorer's own system icons return `E_FAIL`. **Sources:** https://github.com/qt/qtbase/blob/dev/src/plugins/platforms/windows/qwindowssystemtrayicon.cpp (`isIconVisible`); Seelen issue #1885. So a "valid rect" for a phantom, and one shared rect for several phantoms, is fully consistent with a fallback/aggregate answer rather than a per-icon painted slot — **INFERRED**.

**What is NOT ESTABLISHED in §1:** the code path inside Taskbar.dll/Taskbar.View.dll from "icon registered" to "first frame drawn". No public disassembly-level writeup of that path was found (searched; see §6 queries). The pediy (看雪) RE thread referenced by the ITrayNotify gist author (bbs.pediy.com/thread-202658) was not retrievable during this research.

---

## 2. Why a never-before-seen icon would be gated out on 26100 while old ones render

**Direct answer: NOT ESTABLISHED by any source that owns the claim.** What can be said honestly:

1. **It is not the app.** A byte-identical payload from a plain .NET process fails identically (given). First-party consumers (Chromium, Electron, Qt, .NET WinForms — §4) contain no additional call that has been shown to matter, and the calls they *do* make differently (SETVERSION, NIS_HIDDEN, SetPreference) were already tried locally or produce no paint change (given).
2. **The old-vs-new split is per-icon shell state, not per-process behavior** (**INFERRED**): old icons (Discord, Steam, Ditto, Flameshot) completed the full shell pipeline at some earlier point — NotifyIconSettings record, `IconSnapshot`, model item, paint — and keep rendering from that state; never-seen icons get as far as record + toolbar button (given) but never reach paint or snapshot. Both populations run through the same NIM_ADD code today; what differs is the icon's accumulated state in Taskbar.dll's model.
3. **No build-level bug report matches this symptom.** Searches for 24H2/26100 tray render failures surfaced only (a) whole-tray disappearance reports (different failure mode: clock/network/volume gone too — e.g. https://learn.microsoft.com/en-us/answers/questions/5932948/bug-report-system-tray-icons-battery-wifi-bluetoot on 26100.8115) and (b) generic "icons missing" folklore, all of which is about *promotion/overflow* (§4). Microsoft's release-health pages were searched for notification-area rendering issues; none matched. (Absence of evidence — I did not crawl every KB article.)
4. **Two candidate classes remain, unselected between:**
   - **Machine user-state:** something in this user's Taskbar.dll state (NotifyIconSettings/model) is stuck such that *new* registrations never reach the XAML item stage. A fresh user profile would render new icons normally.
   - **Build-26100 code path:** the new-icon registration path is broken for everything on this build. A fresh profile would fail too.
   The given observation "old icons render" does not discriminate: they may simply have painted *before* the machine entered its current state.
5. The observed **small fixed glyph** (icon-independent, static, appears when a new icon registers; absent when the entry carries `IsPromoted=1`, flat slot instead) is consistent with **INFERRED**: the XAML SystemTray rendering a data-model item that has *no image content* (Taskbar.View.dll ships glyph styling and content-type variants — `ImageIconContent` vs `TextIconContent` vs `BadgeIconContent`, §1 step 6), i.e. an item created without the bitmap. Its identity is not proven. An alternative (icon reaching the overflow/chevron presentation) is not excluded.

---

## 3. Is `IsPromoted` / `IconSnapshot` cause or effect?

| Value | Verdict | Evidence |
|---|---|---|
| `IconSnapshot` | **Effect of having been painted.** Not a cause of rendering. | (a) The shell only writes it for icons it already painted (local observation, given). (b) Fabricating a real `IconSnapshot` PNG + `InitialTooltip` makes the Settings row appear and the toggle work, yet the icon still doesn't paint (given) → not sufficient. (c) Taskbar.dll owns the value (local dump §7); consumers read it as cached display data — Settings row and third-party tools (Seelen #1885). |
| `IsPromoted` | **Cause of placement (promoted ↔ overflow) for already-rendered icons. Not a render gate.** | (a) Writing it flips visible/hidden immediately without restarting explorer — for painted icons (Super User 1332399 answer, link in §1). (b) Writing `IsPromoted=1` before or after a fresh `NIM_ADD` does not make the bitmap appear (given). (c) Internal contradiction check: painted long-lived apps *also* carry `IsPromoted=1` (given), so `IsPromoted=1` cannot be what blocks painting. |
| Observed correlation (`IsPromoted=1` ↔ flat slot; absent ↔ glyph) | **Correlation only, as stated.** Consistent with: entries at different stages of the shell's per-icon state machine produce different no-paint presentations (flat vs placeholder glyph). | Given observations + §1 model. **INFERRED.** |

---

## 4. What first-party consumers actually do — and whether any of it can force a paint

| Consumer | `NIM_ADD` payload | After ADD | SETVERSION | Other shell-state calls | Source |
|---|---|---|---|---|---|
| .NET WinForms `NotifyIcon` | `NIF_MESSAGE\|NIF_ICON\|NIF_TIP` (same shape as the failing probe) | `NIM_MODIFY` for later changes | **never** | none | https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs (`UpdateIcon`) |
| Chromium `StatusIconWin` | `NIF_MESSAGE` only | `NIM_MODIFY` with `NIF_ICON`, then `NIF_MODIFY` with `NIF_TIP` | **never** | `TaskbarCreated` → delete+re-add (`ResetIcon`); `ForceVisible()` → `ITrayNotify::SetPreference(PREFERENCE_SHOW_ALWAYS)` unless user chose "hide" | https://github.com/chromium/chromium/blob/main/chrome/browser/ui/views/status_icons/status_icon_win.cc ; status_tray_win.cc ; status_tray_state_changer_win.cc |
| Electron `NotifyIcon` | `NIF_MESSAGE` only (optional `NIF_GUID`) | `NIM_MODIFY` with `NIF_ICON` | **never** | `TaskbarCreated` → `ResetIcon` | https://github.com/electron/electron/blob/main/shell/browser/ui/win/notify_icon.cc ; notify_icon_host.cc |
| Qt `QWindowsSystemTrayIcon` | `NIF_MESSAGE\|NIF_ICON\|NIF_TIP\|NIF_SHOWTIP` (+ `NIF_STATE/NIS_HIDDEN` when hidden) | `NIM_MODIFY` | **yes**, `NIM_SETVERSION` with `NOTIFYICON_VERSION_4` immediately after every successful ADD | visibility via `NIM_MODIFY+NIS_HIDDEN` | https://github.com/qt/qtbase/blob/dev/src/plugins/platforms/windows/qwindowssystemtrayicon.cpp (`sendTrayMessage`) |
| Microsoft docs / SDK sample | ADD with `NIF_ICON\|NIF_TIP` (+`NIF_GUID` preferred on Win7+) | — | **yes**, recommended (`uVersion=NOTIFYICON_VERSION_4`) | — | https://learn.microsoft.com/en-us/windows/win32/shell/notification-area |

**Falsification reading (visibility vs rendering):**
- Every *answered* public solution to "my tray icon doesn't show" is about **visibility/promotion**: flip `IsPromoted`, use the legacy `shell:::{05d7b0f4-2121-4eff-bf6b-ed3f69b894d9}` panel ("Always show all icons"), delete `IconStreams`, etc. (Super User 1332399; registry-tweak pages). All of those move *already-painted* icons between areas. None produces pixels for an icon the shell never painted.
- Microsoft's own troubleshooting for "icon not seen / calls fail" covers only **GUID misuse or a moved binary** — i.e. identification failures where `Shell_NotifyIcon` returns FALSE. It does not describe TRUE-but-never-painted. (NOTIFYICONDATAW "Troubleshooting" section, link in §1.)
- No public report matching this exact symptom (TRUE + valid rect + toolbar button + never painted) was found in searches (§6).

**Answer to Q4:** **There is no documented, first-party, or reverse-engineered app-side action that forces the shell to paint the bitmap.** Every app-visible API found across the SDK docs and the four first-party codebases controls registration, placement, callback semantics, or notifications — never rendering. The local A/B tests (SETVERSION, GUID, SetPreference, RegisterCallback, NIF_INFO, AUMID, UIOrderList, TrayNotify store, IsPromoted, IconSnapshot — all given as ruled out) already exercised everything in that table. Rendering is a shell/user-state problem on this build.

- The **one untried first-party variation**: Chromium's and Electron's two-step sequence — `NIM_ADD` with `NIF_MESSAGE` only, *then* `NIM_MODIFY` carrying `NIF_ICON`. Listed for completeness and because it is a two-minute test; **expected effect is low** (it hits the same explorer ingestion path) and it is **not** claimed as a fix — it is the only behavioral difference left in the first-party corpus.

**Provenance caveat (freshly checked):** the "NIM_GET reports `dwState & NIS_HIDDEN == 0`" datum rests on a message that is **not part of the public API**. The Windows SDK header `C:\Program Files (x86)\Windows Kits\10\Include\10.0.18362.0\um\shellapi.h` defines only `NIM_ADD 0`, `NIM_MODIFY 1`, `NIM_DELETE 2`, `NIM_SETFOCUS 3`, `NIM_SETVERSION 4` (lines 1114–1118); there is no `NIM_GET`. Whatever value was sent, its explorer-side meaning should be pinned before that datum is relied on.

---

## 5. The single most decisive next experiment

**Paint-store probe: compare a known-good slot against the phantom slot.**

Two leading explanations to discriminate:
- **H1 — ingestion failure:** explorer/Taskbar.dll never put the bitmap into whatever store the painter reads (icon→model/hand-off broken for first-seen icons).
- **H2 — presentation gating:** the bitmap is in the store, but the render layer deliberately/conditionally doesn't show it (visibility/model flag/clip).

Procedure (~1 hour, all local, read-only):

1. **Find the painter.** For the center of one *working* icon and the center of the *phantom* slot: `WindowFromPoint()` then walk parents; record window classes. Two outcomes: classic `ToolbarWindow32`, or a XAML bridge window (e.g. `Windows.UI.Composition.DesktopWindowContentBridge` / `DesktopWindowXamlSource` hosted by Taskbar.View).
2. **Dump the store at that layer:**
   - **If `ToolbarWindow32` owns the pixels:** `SendMessage(TB_GETBUTTON)` for the working and phantom button indices → compare `TBBUTTON.iImage`; then `TB_GETIMAGELIST`, and `ImageList_GetImageInfo`/`ImageList_GetIcon` for those indices → save both as PNG. Also `TB_GETITEMRECT` for both buttons.
   - **If a XAML bridge owns the pixels:** UIA walk of the tray at both points (à la Raymond Chen's enumerator) → compare the working vs phantom item: does an item exist at all, what's its Name/automation id, and does it expose image content?
3. **Decision table:**

| Reading | Conclusion |
|---|---|
| Phantom `iImage == -1`, or its image-list entry is transparent/blank, while working shows the real bitmap | **H1**: bitmap never entered the paint store → next work: Taskbar.dll's NIM_ADD→model hand-off |
| Phantom store contains the exact bitmap (e.g. the solid red icon) but screen is flat | **H2**: present-but-suppressed → next work: composition/visibility of that slot |
| XAML: no SystemTray item for the phantom although the toolbar button exists | Gate is between Taskbar.dll model and Taskbar.View — model item never created (**H1** at model level) |
| XAML: item identical including image content, pixels still flat | Composition-level suppression (**H2**) |

Bonus readout from the same run: if the phantom buttons have **distinct** `TB_GETITEMRECT` rects yet `Shell_NotifyIconGetRect` returns **one** rect for all phantoms, then GetRect is not reading the toolbar — supporting the INFERRED reading that explorer's icon table treats phantoms as absent/aggregate.

Second-choice discriminator (not chosen because it localizes scope but not mechanism): run the same probe in a **fresh user profile** — paints ⇒ machine user-state; fails ⇒ build-wide 26100 behavior.

---

## 6. What I searched and could not find

- Queries run (DuckDuckGo HTML + direct fetches): `ITrayNotify SetPreference gist IsPromoted IconSnapshot`; `Shell_NotifyIcon returns TRUE but icon not visible`; `Windows 11 24H2 26100 tray icon not showing`; `NotifyIconSettings IconSnapshot reverse engineering`; `"TrayNotifyWnd" toolbar imagelist paint`; `"24H2" OR "26100" Shell_NotifyIcon not displayed`; `user promoted system promoted notification area`; plus the sources those queries surfaced (§1–§4).
- **Not found anywhere:** a source that documents the explorer-internal rule deciding whether/when a registered icon's bitmap is drawn (NIM_ADD → first painted frame) on Windows 10/11; any first-party statement about `IconSnapshot` being read for *rendering*; any 24H2-specific known issue matching TRUE-but-unpainted; the pediy RE thread (bbs.pediy.com/thread-202658, referenced from the ITrayNotify gist) — not retrievable.
- Unverified claims deliberately *not* promoted to fact: the SO 78494726 "TB_BUTTONCOUNT no longer works after 22H2" claim (contradicted locally); assorted registry-tweak blogs (low-trust, visibility-only advice).

---

## 7. Local evidence (this machine, build 26100) — method so it is reproducible

Method: read file bytes → `[Text.Encoding]::Unicode.GetString` at both byte alignments → substring/regex match for patterns below.

1. **Explorer's loaded modules** (`Get-Process explorer | % Modules | ? ModuleName -match 'taskbar|shell32'`):
   - `SHELL32.dll @ C:\Windows\System32\SHELL32.dll`
   - `Taskbar.dll @ C:\Windows\System32\Taskbar.dll`
   - `SettingsHandlers_DesktopTaskbar.dll @ C:\Windows\System32\SettingsHandlers_DesktopTaskbar.dll`
   - `Taskbar.View.dll @ C:\Windows\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\Taskbar.View.dll`
2. **`C:\Windows\System32\Taskbar.dll` string hits:** `NotifyIconSettings`, `IsPromoted`, `IconSnapshot`, `UIOrderList`, `InitialTooltip`, `ExecutablePath`, `IconGuid`, `UID`, `Version`, `TrayNotifyWnd`, `Shell_TrayWnd`, `ToolbarWindow32`, `Promoted`, `Overflow`, `SystemTray`, `IconStream`, `TopLevelWindowForOverflowXamlIsland` (context at decoded offset ~1,040,712: `...TopLevelWindowForOverflowXamlIsland.Before..After...Overflow....Promoted...`; registry cluster at ~941,118: `...D:(A;CIOI;KA;;;SY)(A;CIOI;KA;;;LA)(A;CIOI;KR;;;WD)..IsPromoted...IconGuid...ExecutablePath...InitialTooltip...UID...Version.UIOrderList...`; `IconSnapshot` at ~1,044,804 near `TaskbarEndAccessibilityElement`). Absent: `User Promoted Notification Area`, `Notification Chevron`, `Chevron`, `ITrayNotify`, `SetPreference`.
3. **`C:\Windows\System32\en-US\Taskbar.dll.mui` string hits:** `User Promoted Notification Area`, `System Promoted Notification Area`, `Notification Chevron`, `Overflow Notification Area`. Absent from `C:\Windows\en-US\explorer.exe.mui`.
4. **`C:\Windows\System32\explorer.exe`:** only `Shell_TrayWnd`. **`C:\Windows\System32\SHELL32.dll`:** `Shell_TrayWnd`, `ToolbarWindow32`.
5. **`Taskbar.View.dll` identifier extraction** (regex over decoded Unicode): full `SystemTray.*` identifier list including `NotificationAreaIconsDataModel`, `NotifyIconView`, `IconViewModel`, `ImageIconContent`, `IImageIconContent`, `IconData`, `ChevronSystemTrayIconDataModel2`, `ChevronIconView`, `GlyphPadding/GlyphFontFamily/GlyphFontSize`, `GetNotificationAreaIcons`, `GetNotifyIconStackViewModel`, plus per-system-icon `*SystemTrayIconDataModel` classes (Volume, Network, Power, Clock, Privacy, Recall, StudioEffects, …).
6. **SDK header check:** `shellapi.h` (SDK 10.0.18362) lines 1114–1142: `NIM_ADD 0x0`, `NIM_MODIFY 0x1`, `NIM_DELETE 0x2`, `NIM_SETFOCUS 0x3`, `NIM_SETVERSION 0x4`; `NIF_MESSAGE/ICON/TIP/STATE/INFO/GUID/REALTIME/SHOWTIP`; `NIS_HIDDEN 0x1`, `NIS_SHAREDICON 0x2`. **No `NIM_GET`.**

---

## 8. Honest gaps

- **The exact render gate is not established.** No source — official, RE, or first-party — documents it, and I did not disassemble Taskbar.dll/Taskbar.View.dll here. Everything past Step 6 in §1 is INFERRED from ownership (which module contains the strings) rather than from control flow.
- **Which window draws a working icon's pixels is not established** (classic toolbar vs XAML) — §5 resolves this first, and several interpretations (including the identity of the "small glyph") hang on it.
- **Machine-state vs build-26100 is not selected between** (§2.4).
- **`dwPreference ↔ IsPromoted` is inferred**, not sourced.
- The public consumer corpus cannot prove the negative that *no* app-side action exists; it can only show that everything the four first-party implementations plus the SDK do has been tried or is demonstrably unrelated to paint (§4).
