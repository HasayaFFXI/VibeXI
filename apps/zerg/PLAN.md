# Zerg — the damage meter as a native Windows app

Goal: a HorizonXI player downloads one file, double-clicks it, and gets a real
application window showing their damage. No Python, no console, no browser tab,
and pop-outs that genuinely float over the game.

Nothing is built yet; this file is the whole plan.

**`../damage-meter/` stays exactly as it is.** Zerg is a second *host* for the
same front end, not a replacement. The Python server keeps working for
development and for anyone who prefers it; nothing in this plan edits a file
under `apps/damage-meter/` (see Decision 2 for the one place that might have to
change, and the rule for when).

---

## What is being built

```
Zerg.exe  (C# / .NET 10, WinForms, WebView2)
 ├─ MainForm ─ WebView2 ─ https://zerg.vibexi/index.html   (damage-meter/web, embedded)
 │                 │
 │                 ├─ GET /shared/*    → embedded shared-ui
 │                 ├─ GET /api/events  → EventTailer  ──tails──▶ %LOCALAPPDATA%\VibeXI\events\*.jsonl
 │                 ├─ GET /api/alpha   → PopoutForm.Opacity (by window title)
 │                 └─ window.open()    → new PopoutForm (TopMost, own WebView2, same process)
 └─ PopoutForm × n
```

There is **no HTTP server and no port.** WebView2's `WebResourceRequested` answers
every request in-process, so the front end's `fetch('/api/events?…')` and
`fetch('/api/alpha?…')` keep working byte-for-byte while nothing listens on a
socket. That removes the port walk, the firewall question and the
second-instance-on-8731 failure from the PyInstaller plan in one go.

---

## Decisions

Written down so they don't get argued over again.

**1. WinForms, .NET 10 LTS, x64 only.** WinForms over WPF because the host has
nothing to *draw*: it is two window types that each hold one WebView2, and
`Form.TopMost`, `Form.Opacity` and `FormBorderStyle` are exactly the knobs
needed. Confirmed by the Phase 0 spike, which also showed `TransparencyKey`
gives true per-pixel see-through, so the WPF fallback is no longer needed. FFXI is a 32-bit game, but the meter is a separate process and
there is no reason for it to be x86.

**2. The front end is shared, not forked.** Zerg embeds
`../damage-meter/web/` and `../../shared-ui/{css,js}` at build time. Every fix to
`stats.js` or `app.js` lands in both hosts with no copying. Zerg-specific
behaviour goes in a single injected script, `src/Zerg/host.js`, added via
`AddScriptToExecuteOnDocumentCreatedAsync` so it runs *before* the page's own
scripts. Its first job is one line: delete `documentPictureInPicture`, which
makes `popout.js` take its existing `window.open` path (line 713), and that is
the path Zerg turns into a native always-on-top window.

If `damage-meter/web` ever needs to know which host it is in, the rule is a
feature test on `window.chrome.webview`, added in the damage-meter repo with
the Python host still working — never a Zerg-only fork of a shared file. If the
two UIs really do diverge, copy `web/` into `apps/zerg/web/` *then*, as a
recorded decision, not quietly.

**3. Assets are embedded resources, served from a dictionary.** No files on disk
next to the exe, so the single-file publish actually is one file. The URL → resource
lookup is an exact-match table built at startup, so `resolve_static_path`'s
traversal containment check has no equivalent to port: a path that isn't
a key is a 404.

**4. Origin `https://zerg.vibexi/`.** A fixed virtual host, so `localStorage`
(theme, excluded characters, pop-out opacity) survives across runs. It is a
different origin from `http://localhost:8731/`, so settings do **not** carry over
from the Python meter. That is acceptable; say so in the README.

**5. WebView2 user data in `%LOCALAPPDATA%\VibeXI\zerg\WebView2`.** The default is
next to the exe, which may be unwritable (Program Files, a read-only zip
preview). Same parent as the addon's `events\` directory, for the same reasons.

**6. Self-contained single-file publish.** `PublishSingleFile`,
`SelfContained`, `EnableCompressionInSingleFile`,
`IncludeNativeLibrariesForSelfExtract`. Roughly 50–70 MB, with no runtime for the
player to install. Framework-dependent would be ~2 MB but would send strangers
to a Microsoft download page on first launch — that's the "worry about what the
user has installed" problem this project exists to remove. No ReadyToRun, no
trimming at first (WinForms is not trim-safe); revisit size only if it matters.

**7. The WebView2 *runtime* is assumed, and checked.** Windows 11 ships it and
Windows 10 has had it pushed through Windows Update since 2021. On startup,
`CoreWebView2Environment.GetAvailableBrowserVersionString()`; if it throws,
show a plain dialog with the Evergreen bootstrapper link and exit. No bundled
fixed-version runtime (+150 MB) unless real users hit this.

**8. Closing the main window exits the app.** Pop-outs close with it. No tray
icon in v1 — if you want the meter hidden but running, minimise it.

**9. Version in three places**: the csproj `<Version>` (which becomes the
file-properties resource for free), the main window's title, and an
`/api/version` endpoint so the page footer can show it later.

**10. Fonts.** `index.html` pulls Google Fonts. That works online and falls back
to system fonts offline, same as today. Embedding the font files is a possible
later change, made in `shared-ui`, not here.

---

## The host contract (what Zerg must reproduce)

Ported from `../damage-meter/damage-meter.py`, which remains the reference:

| Endpoint | Behaviour to match exactly |
|---|---|
| `GET /api/events?file=&offset=` | newest `*.jsonl` by mtime; `reset` when the newest file's name ≠ `file`, or when the file shrank below `offset`; read at most 8 MiB; trim back to the last `\n` and report `nextOffset` past it; split on `\n` after normalising `\r\n`; drop the trailing empty element; ASCII-escaped JSON out; `Cache-Control: no-store`. Response keys: `ok file dir mtime reset offset nextOffset size lines`, and the no-file shape `{ok, file:null, reset:true, offset:0, nextOffset:0, size:0, lines:[], dir}` |
| `GET /api/alpha?title=&value=…&key=` | `value` clamped 10–100. Find the `PopoutForm` whose document title equals `title` and set `Opacity = value/100`. `x y w h dpr` are accepted and ignored — Zerg owns its windows and doesn't need to find one by its geometry. **`key` is honoured** (Phase 0 proved it works here): when present, set `TransparencyKey` and `BackColor` to that colour and the WebView2 background transparent; when absent, clear it. `popout.js` already paints `--pop-key` and toggles `key-bg`, so the card background really does disappear in Zerg, which it never did in Chrome. Reply `{ok, applied, supported:true, method:"form", window:<title>}` |
| static | `/` → `index.html`; `/shared/*` → shared-ui; MIME table as in the Python file; 404 otherwise |

**File sharing is free in .NET:** `new FileStream(path, FileMode.Open,
FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)` is exactly what
`_open_shared` goes through `CreateFileW` to get. The game's writer must never
be blocked by this tool — that rule carries over unchanged.

**Do not move interpretation into the host.** Same hard rule as the Python
server: it returns raw lines. If you're tempted to parse JSON in C#, that
belongs in `source.js`.

---

## Layout

```
apps/zerg/
  PLAN.md               this file
  README.md             player-facing: download, run, what SmartScreen says
  CLAUDE.md             working notes, once there is code to have notes about
  Zerg.slnx
  src/Zerg.Core/        net10.0, no UI -- everything testable without a window
    EventFiles.cs       newest_events + _open_shared + read_tail port
    EventsApi.cs        events_payload + the /api/events handler, JSON body
    Query.cs            parse_qs / _first / _int semantics
  src/Zerg/             net10.0-windows WinForms exe, references Zerg.Core
    Zerg.csproj         EmbeddedResource globs over ../../../damage-meter/web and ../../../../shared-ui
    Program.cs          [STAThread], single-instance mutex, WebView2 runtime check
    MainForm.cs         main window, shared CoreWebView2Environment, window.open hook
    PopoutForm.cs       TopMost, owns one WebView2, reports its title
    HostRouter.cs       WebResourceRequested → /api/* or embedded asset
    Assets.cs           resource-name → (bytes, mime) table
    WindowState.cs      remembers main/pop-out bounds in %LOCALAPPDATA%\VibeXI\zerg\windows.json
    host.js             injected before page scripts
    zerg.ico
  tests/Zerg.Core.Tests/
    EventFilesTests.cs, EventsApiTests.cs, QueryTests.cs
    Parity/             reference.py + ParityTests.cs: live comparison with damage-meter.py
  build/
    publish.ps1         clean → test → publish → zip + SHA256SUMS
```

`.gitignore` additions: `apps/zerg/**/bin/`, `apps/zerg/**/obj/`, `apps/zerg/dist/`.

---

## Phases

### Phase 0 — toolchain and the transparency spike — **DONE 2026-10-02**

**Result: WinForms is settled.** `spike/OverlaySpike/` (throwaway, not shipped)
was run over FFXI in windowed mode, and every check passed:

| Check | Result |
|---|---|
| `Form.Opacity` < 1 with WebView2 inside | renders, keeps repainting (animated bars and a 100 ms clock stayed live), small text readable |
| `TopMost` over the game | stays above it, including after clicking into the game and alt-tabbing |
| **Keyed background** — `TransparencyKey = #010203`, form `BackColor` the same, WebView2 `DefaultBackgroundColor = Transparent`, page `body { background: transparent }` | **works per-pixel**: game visible between the cards, cards solid. This is the punch-out that `LWA_COLORKEY` never achieved on a Chrome window (see `../damage-meter/winalpha.py`) |
| Click-through (`WS_EX_TRANSPARENT \| WS_EX_LAYERED`) | clicks reach the game; Ctrl+Alt+Z global hotkey restores it |
| Frameless + drag by a native grip strip | works (`ReleaseCapture` + `WM_NCLBUTTONDOWN/HTCAPTION`) |

Toolchain: .NET SDK 10.0.401 x64, WebView2 SDK 1.0.4258.31, runtime 154.x.
`DOTNET_CLI_TELEMETRY_OPTOUT=1` set at user level.

Carry-overs into Phase 1+:
- The WebView2 NuGet package references `Microsoft.Web.WebView2.Wpf.dll`
  unconditionally on .NET 5+, which raises MSB3277 (WindowsBase 4 vs 5) in a
  WinForms project. Harmless — the assembly is never loaded — but the real
  csproj should remove that `Reference` in a target after the package's
  targets run, rather than suppressing the warning.
- Click-through needs the window layered, and WinForms only layers below 100%
  or with a key, so the spike holds opacity at 99% while it's on. Keep that.
- Run `dotnet build-server shutdown` after building from a script: a
  lingering MSBuild node held a directory lock during the spike.

Original task list, kept for the record:

1. Install the SDK: `winget install Microsoft.DotNet.SDK.10`. (This machine has
   only the .NET 6 *runtime*, no SDK.) WebView2 runtime is present (154.x).
2. **Spike, throwaway code:** one WinForms window, one WebView2 showing a
   coloured page, `TopMost = true`, an opacity slider bound to `Form.Opacity`.
   Run FFXI in **windowed / borderless** mode and check:
   - does `Form.Opacity` (which makes the window layered) still render WebView2
     at all, and at what cost?
   - does it stay above the game?
   - can `DefaultBackgroundColor = Transparent` plus a borderless form give
     per-pixel see-through (the "punch-out" that Chrome never honoured)?
3. Decide from the results: WinForms `Opacity` is good enough → carry on as
   planned. It doesn't render → WPF `WebView2CompositionControl`, or WinForms
   with WebView2's composition (visual hosting) mode. Either way, record the
   answer here before Phase 1.

This comes first because it is the one question that could change the choice
of UI framework, and it costs an afternoon.

**Done when:** a translucent, always-on-top WebView2 window is visible over the
running game, and this section records how.

### Phase 1 — headless core, tested against the Python server — **DONE 2026-10-02**

`src/Zerg.Core/` (plain `net10.0`, no WinForms) holds `EventFiles`
(`Newest`, `OpenShared`, `ReadTail`), `EventsApi` (`events_payload` + the
`/api/events` handler, JSON body) and `Query` (`parse_qs`/`_first`/`_int`
semantics). Phase 2's router only has to dispatch to `EventsApi.FromQuery`.

`tests/Zerg.Core.Tests/`: 47 tests, all passing.
- **Unit tests**: newest-file choice, partial last line held back, CRLF,
  shrink, the 8 MiB cap across two polls, invalid UTF-8, JSON key order and
  ASCII-only output, and that the writer can append to *and delete* the file
  while it's open for reading.
- **Parity tests**: `Parity/reference.py` loads the real `damage-meter.py` and
  answers queries through the exact calls `_api_events` makes. The C# side
  answers the same queries on the same files, and every field must match;
  `mtime` must match within 10 µs, because Python goes through a float. The
  cases are:
  - the damage meter's own generated fixture, at line boundaries and mid-line;
  - a file of awkward bytes (BOM, CRLF, lone CR, raw multibyte, five kinds of
    invalid UTF-8, an unfinished line), read from **every byte offset**;
  - 20 query-string oddities;
  - tiny and empty files, a missing directory, and the newest of several files;
  - the read cap.
  Parity is checked live rather than against a committed snapshot, because
  this repo's `autocrlf` would rewrite the fixtures' line endings. Without
  Python on PATH these tests report *skipped*, not passed.
- **Mutation-checked:** removing the CRLF normalisation makes the parity suite
  fail and show the exact line that differs.

Deliberate divergence: a negative `offset` is treated as a shrink (read again
from zero, `reset`); Python raises on the seek and answers 500. The page never
sends one. Other edge-case differences in `Query.Int` are listed in its comment
(Python accepts `1_000` and unbounded integers).

Original task list:

1. `EventTailer` — port `newest_events` and `read_tail`, including the
   truncated/shrunk and partial-last-line cases.
2. Tests against `../damage-meter/tools/events/Hasaya_2026.07.30.jsonl` and
   synthetic files: half-written last line, CRLF, file shrinks, newest file
   changes, empty directory, missing directory, 8 MiB cap.
3. **Parity check:** for a set of `(file, offset)` pairs, the JSON from
   `EventTailer` equals what `python damage-meter.py` returns from
   `/api/events`. Same bytes in, same lines out. This is how we know the port is
   faithful rather than just plausible.

**Done when:** tests pass and parity holds for every offset in the fixture's
line boundaries plus a handful of mid-line ones.

### Phase 2 — the main window — **DONE 2026-10-02**

`src/Zerg/` runs the meter from a single window with no console and no server.
Verified against the running app over CDP (debug port enabled for test runs
only), with the generated fixture as the event source:

| Check | Result |
|---|---|
| All 11 assets served from embedded resources (`_kirin-tmp.json` and `shared-ui/README.md` excluded) | yes; only `favicon.ico` 404s, as it does under Python |
| `/api/events` reads the fixture | 809 lines, `nextOffset` = file size |
| Live tail: Start, then append two events to the file | Total Damage 1,300 within one poll; chip and chart appear |
| `documentPictureInPicture` hidden by `host.js` | `undefined` |
| `isSecureContext`, `showSaveFilePicker` available (Export path) | yes, both |
| Theme toggle survives a restart | yes (WebView2 profile localStorage) |
| Window position/size restored | exactly (523,227 1296×899) |
| `--dev`: served from disk, title says so | yes |
| Traversal in disk mode: `..\`, drive path, UNC path | all 404 |
| Startup: navigate → page loaded | 216 ms, after disabling SmartScreen (was 2.2 s) |

Added beyond the task list: `zerg.log` (startup decisions, crashes, and in
Debug every non-poll request) with a crash dialog that points at it. Without a
console it's the only place a player's failure can be read.
`Properties/launchSettings.json` makes `dotnet run` use `--dev`.

**Not verified here — needs a person:** the native Open/Save dialogs behind
Import and Export (they open, but can't be driven from CDP), and how pop-outs
behave before Phase 3 (WebView2's default popup, not on top).

Traps hit on the way are in `CLAUDE.md`: the MIME type for `/` made the page a
download, a double-completed deferral, and SmartScreen's 2 s lookup.

Original task list:

1. `MainForm` + WebView2, shared `CoreWebView2Environment` with the user-data
   folder from Decision 5.
2. `HostRouter` on `AddWebResourceRequestedFilter("https://zerg.vibexi/*")`:
   embedded assets, `/api/events`, `/api/version`.
3. `host.js` injected; `documentPictureInPicture` removed.
4. Main window bounds remembered; title shows the version.
5. Dev convenience: `--web-root <dir>` to serve from disk instead of embedded
   resources, so front-end edits are an F5 instead of a rebuild — the same
   workflow `damage-meter.py` gives today. DevTools on F12 in Debug builds only.

**Done when:** the meter renders and updates live from
`tools/gen-test-events.py` output, the theme toggle and import/export work, and
no console window appears.

### Phase 3 — native pop-outs — **DONE 2026-10-02**

Every card's **Pop Out** now opens a native, always-on-top `PopoutForm`
(thin tool-window caption, off the taskbar and Alt+Tab), several at once, each
remembering its own position. Nothing in `damage-meter/web` changed; the
adaptation is entirely in `host.js`, `MainForm` and `PopoutForm`.

How it fits together:
- `host.js` **provides** `documentPictureInPicture` rather than removing it.
  `popout.js` disables Pop Out outright when PiP is missing (`focusBtn.disabled
  = !PIP`), so the Phase 2 approach would have left the button dead. The shim's
  `requestWindow()` is a `window.open('', 'dpsPanel_<key>', …)`; the key comes
  from the click event in progress, so each panel has a stable window name.
- `MainForm.OnNewWindowRequested` takes a deferral, builds the `PopoutForm`,
  initialises its WebView2 in the same environment, attaches the router to it
  (the child's stylesheets are requested from the child view), and sets
  `e.NewWindow`. The DOM `popout.js` writes into the window synchronously
  survives the attach.
- `/api/alpha` finds the form by `document.title` and sets `Opacity`; with
  `key=` it sets the form's `BackColor` and `TransparencyKey` to that colour and
  the WebView2 background to transparent.

**The punch-out needed one more step than Phase 0 implied.** A colour key does
not touch pixels WebView2 draws (DirectComposition, as in Chrome); it drops the
*form's* background wherever the page is transparent. `popout.js` paints the
keyed background `#010203` itself, so the first cut was only uniformly dimmed.
`host.js` now adds one rule to each pop-out document, making the background
transparent in `.key-bg` mode only, and the form's key colour shows through and
is dropped.

Verified over the running game (FFXI behind the panel), by sampling screen
brightness inside the panel against the game just outside it:

| Mode | inside | outside | |
|---|---|---|---|
| keyed, 85% | 61.2 | 60.8 | background gone |
| not keyed, 85% | 30.3 | 58.6 | dimmed — the old Chrome look |
| keyed, 100% | 57.9 | 57.9 | gone even at full opacity, which Chrome could never do |

Also checked over CDP:
- **Moving the card:** it moves out of the page and is still reachable through
  `DPS.popout.byId`. Its canvas lays out at 448×240, all 3 stylesheets load, and
  `window.opener` is intact.
- **Two at once:** two panels float at the same time.
- **Docking:** the panel's **Dock** button and the window's own X both dock the
  card. Closing the main window closes every panel.
- **Live data:** Start pressed *inside* a panel arms the session, and appended
  events reach the panel's total and strip (2,000; Rhyllis 1,580, Hasaya 420).
- **Theme:** the toggle reaches open panels.
- **Placement:** saved per panel and restored on reopen.

**Follow-up the same day: dark tint, solid text.** With the background
punched out completely, the slider only faded the text and cards, and there was
no darkening over the game, which the user wanted. A colour key is
all-or-nothing per pixel, so each keyed panel now has a `TintForm` behind it:
borderless black, click-through, never activated, sized to the panel's client
area. The panel is *owned* by it, so it always sits directly above it. In keyed
mode the slider sets the tint's darkness and the panel stays at 100%. Measured
inside/outside brightness: 85% → 0.16, 50% → 0.49, 20% → 0.76, 100% → 0.03.
The tint follows a move and a resize exactly, and closes with the panel. Without
the key, the slider fades the whole window as before.

Found on the way: Windows can park a hidden window at about (−32000, −32000)
without reporting it minimized, and that was being saved as a placement.
`WindowPlacement.Remember` now refuses positions on no screen.

Known and left alone:
- `popout.js`'s Pop Out tooltip still says "Only one panel at a time". That's
  true of Chrome's PiP and false in Zerg, and it's a damage-meter string.
- Frameless panels (no caption, dragged by the page's own bar) and click-through
  stay open questions (below); the caption is what moves and resizes a panel
  today.

Original task list:

1. `CoreWebView2.NewWindowRequested` on the main view: take a deferral, create a
   `PopoutForm` with its own WebView2 **in the same environment**, set
   `e.NewWindow = popout.CoreWebView2`, apply `e.WindowFeatures` for size and
   position. Setting `NewWindow` is what keeps `window.opener` and same-origin
   scripting intact — which `popout.js` relies on, because it *moves* the card's
   DOM into the child with `adoptNode`.
2. `PopoutForm`: `TopMost`, bounds remembered per pop-out title, `window.close()`
   from script closes the form (`WindowCloseRequested`), and closing the form
   lets `popout.js` see an ordinary close and dock the card back.
3. `/api/alpha` → look up the open `PopoutForm` by document title, set
   `Opacity`. The `geometry()` guard in `popout.js` ("never ask for the main
   window") stays harmless: Zerg only ever looks among pop-outs.
4. Multiple pop-outs at once. PiP allowed only one; `window.open` has no such
   limit, so this is a feature gained, not built.

**Done when:** several cards are popped out at once, each floats over the game,
each keeps its own opacity across restarts, and closing one docks it back.

### Phase 4 — packaging

1. `build/publish.ps1`: `dotnet test`, then `dotnet publish -c Release -r win-x64`
   with Decision 6's flags, then `dist/Zerg-<version>.zip` containing `Zerg.exe`
   and `README.txt`, plus `SHA256SUMS`.
2. Icon, `<Product>`, `<Company>`, `<Copyright>`, `<Version>` in the csproj, so
   file properties look legitimate.
3. Single-instance: a second launch brings the existing window forward instead of
   opening a second meter tailing the same file.
4. Optional: a GitHub Actions `windows-latest` job running `publish.ps1` on tag,
   attaching the zip to a release. Building in CI means the published binary
   is reproducible from the tagged source, which matters when strangers ask
   whether it is safe.

**Done when:** the zip, unpacked on **a Windows machine that has never had
.NET or Python installed**, runs on double-click. That clause is the point, and
it cannot be checked on this machine — use a fresh Windows Sandbox
(`WindowsSandbox.exe` is available on Pro/Enterprise only; this machine is Home,
so a VM or a second PC).

### Phase 5 — distribution hygiene

Carried over from `../damage-meter/PACKAGING.md` Phase 3, most of which applies
unchanged: SmartScreen "unrecognised app" walkthrough with a screenshot,
Defender on and default, non-admin user, path with spaces, 125%/150% display
scaling, the addon never loaded (no events directory). Player README that
starts from "you downloaded a file". False-positive submission to Microsoft if
Defender complains. Code signing remains the real SmartScreen fix and remains a
budget question.

---

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| ~~WebView2 in a layered (`Opacity < 1`) WinForms window renders badly or not at all~~ | retired 2026-10-02 | Phase 0 spike passed over FFXI; WinForms confirmed |
| FFXI in exclusive fullscreen draws over every desktop window | medium | nothing a desktop overlay can do; document "use windowed/borderless", which most Horizon players already run |
| ~~`NewWindowRequested` + `adoptNode` across WebView2 instances behaves differently from Chrome~~ | retired 2026-10-02 | Phase 3: DOM written before the attach survives, opener intact, canvases lay out |
| WebView2 runtime missing on an old Windows 10 | low | Decision 7's check and dialog |
| Front-end change in `damage-meter/web` breaks Zerg | low | shared on purpose; Phase 2's `--web-root` makes checking both hosts a reload each |
| AV flags a fresh unsigned exe | medium | far better profile than PyInstaller (a normal .NET app, no self-extracting Python); no packers; hashes published; signing if it persists |
| ~60 MB download | low | it's a one-off download; framework-dependent build is the fallback if it ever matters |

---

## Open questions

1. **Name on disk.** `Zerg.exe`, or does the player-facing name differ
   ("VibeXI Damage Meter") with Zerg as the internal codename?
2. **Should Zerg also host `ws-calculator` / `penta-calculator`?** Same trick,
   different virtual path. It's cheap once Phase 2 exists, and it's the
   "one exe, whole toolset" idea from `PACKAGING.md` Phase 4.
3. **Does the PyInstaller plan in `../damage-meter/PACKAGING.md` still happen?**
   If Zerg ships, that plan has no audience left. Suggest marking it superseded
   once Zerg reaches Phase 4, and leaving the Python server as the dev host only.
4. **Click-through pop-outs** (`WS_EX_TRANSPARENT`, so clicks reach the game
   behind the meter) — wanted? Easy to add, but then the window needs a hotkey
   to become clickable again.
