# zerg — working notes

Zerg is the HorizonXI damage meter as a **native Windows app**: C# / .NET 10 /
WinForms, with the meter's existing web front end running inside WebView2. It
exists so a player can run the meter without Python, a console or a browser tab,
and so floating panels can sit over the game with real transparency.

**Two apps live here during the migration** (NATIVE-PLAN.md): `src/Zerg.Web`
(`ZergWeb.exe`) is that WinForms + WebView2 host, frozen as the reference, and
`src/Zerg` (`Zerg.exe`) is the new WPF app, grown one phase at a time. Most of
this file describes the frozen host; the native app's parts are marked, and
NATIVE-PLAN.md has the rest.

- **`NATIVE-PLAN.md` — the current direction.** Zerg is being migrated to a
  Windows 11-styled WPF app (`Zerg.exe`), built piece by piece in a new
  `src/Zerg` while today's host moves to `src/Zerg.Web` as a frozen reference.
  It is a full rebrand: the finished app has no reference to "Damage Meter" or
  "Metrics". `apps/damage-meter` and all web code are deleted at the end (N9).
  Read this first.
- `RULES.md` — the event contract, and the reason behind each counting and
  session rule, in the native app's terms (N9, step 3). Read it before
  changing any number Zerg prints.
- `PLAN.md` — how Zerg reached its current form (a WebView2 host, Phases 0–3),
  with the decisions behind it and what was measured.
- This file — everything needed to work on the code day to day.
- `../damage-meter/CLAUDE.md` — the front end Zerg hosts: the event contract,
  how the page computes everything, the pop-out module. Most "how does the meter
  work" questions are answered there, not here.

## Where it sits

```
FFXI ─▶ addons/VibeXI (Lua, in-game) ─writes─▶ %LOCALAPPDATA%\VibeXI\events\<Char>_<date>.jsonl
                                                            │ tails
                     ┌──────────────────────────────────────┴───────────┐
          apps/damage-meter/damage-meter.py (Python, browser tab)   apps/zerg (this)
                     └──────── both serve apps/damage-meter/web + shared-ui ────────┘
```

**Two hosts, one front end, for now.** `apps/damage-meter/` (the Python server)
is the original. Zerg embeds `../damage-meter/web/` and `../../shared-ui/` at
build time; it has no copy of the UI. `apps/damage-meter` stays as a frozen
reference for the native migration (no parallel maintenance) and is deleted
once native Zerg is complete and tested (NATIVE-PLAN.md, N9).

## Working with this user

- **The user handles git.** Don't commit, branch or push unless asked.
- **Don't break `apps/damage-meter/`.** Editing the shared front end
  (`damage-meter/web/`, `shared-ui/`) is fine when the user asks for a UI
  change, but it must keep working under `damage-meter.py` in Chrome too.
  Behaviour only Zerg should have goes in `src/Zerg.Web/host.js` (see below). If
  the two UIs ever need to diverge for real, propose copying `web/` into
  `apps/zerg/web/` as an explicit decision (PLAN.md Decision 2); don't fork
  quietly.
- **Verify in the running app**, not just by building. The recipes are under
  "Verifying a change". The user often has FFXI running behind the windows;
  screen captures will include the game.
- **Ask before visual or behavioural choices** that have more than one
  reasonable answer (e.g. what the opacity slider should mean). Mechanical
  fixes don't need asking.

## Environment

- **.NET SDK 10.0.401, x64**, in `C:\Program Files\dotnet`. An x86 SDK may also
  be present under `Program Files (x86)`; it is not on PATH and must not be
  used. `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set at user level at the user's
  request. A shell started before that may not have it, so set it in the
  command when running `dotnet`.
- **WebView2 SDK** `Microsoft.Web.WebView2` 1.0.4258.31; **runtime** 154.x
  (Evergreen, ships with Windows 11).
- **Python 3.14** on PATH: needed only for the parity tests and the fixture
  generator.
- **Windows 11 Home**, so there is no Windows Sandbox. **Two monitors**: the
  primary is 3440×1440 at (0,0); the second is at x=3440 with a different
  scale.
- The shell tools are Windows PowerShell 5.1 and Git Bash. **Never edit source
  with PS 5.1 `Get-Content`/`Set-Content`**: it reads UTF-8 without a BOM as
  ANSI and mangles non-ASCII (an em dash became `â€”`). Use the editor tools.
  PowerShell scripts in this repo stay ASCII-only for the same reason.

## Commands

```bash
# all tests (unit + parity against the real damage-meter.py and the real JS)
dotnet test apps/zerg/Zerg.slnx

# the same without the ~6.5 min mutation check
dotnet test apps/zerg/Zerg.slnx --filter Category!=Mutation

# the native app (WPF, Zerg.exe)
dotnet run --project apps/zerg/src/Zerg
dotnet run --project apps/zerg/src/Zerg -- --events-dir <dir>

# the frozen web build (ZergWeb.exe). The default launch profile is --dev:
# web/ is served from disk, so a front-end edit is F5 in the window.
dotnet run --project apps/zerg/src/Zerg.Web
dotnet run --project apps/zerg/src/Zerg.Web --launch-profile "Zerg (embedded web)"

# play a fixture into the file the native app is following (press Start first)
python apps/zerg/tools/replay.py <fixture.jsonl> <dir>\<live>.jsonl [--squeeze 60] [--extras]

# the synthetic fixture (tools/events/, ignored by git), or an import-ready parse
python apps/zerg/tools/gen-test-events.py
python apps/zerg/tools/gen-test-events.py --export <file>.zerg --seed 7 --date 2026-07-31

# point either at a synthetic event file instead of the real addon output
python apps/zerg/tools/gen-test-events.py --out <dir>\Hasaya_2026.07.30.jsonl
dotnet run --project apps/zerg/src/Zerg.Web -- --dev --events-dir <dir>
```

**The user's desktop shortcut** (`Zerg.lnk` on the OneDrive Desktop) runs
`apps/zerg/dist/Zerg/Zerg.exe`, a Release build with the web files embedded.
It's a snapshot of the web build from before the split, and it stays as it is
until N9 replaces it with the native `Zerg.exe`. **Don't publish over it**: a
publish of `src/Zerg.Web` now produces `ZergWeb.exe`, and the native app isn't
ready. If a published web build is needed for checking, put it elsewhere:

```bash
dotnet publish apps/zerg/src/Zerg.Web -c Release -r win-x64 --self-contained false -o apps/zerg/dist/ZergWeb
```

`ZergWeb.exe` options: `--events-dir <dir>`, `--web-root <dir>`, `--dev`
(`src/Zerg.Web/Options.cs`). The native `Zerg.exe` takes `--events-dir <dir>`.

After scripted builds run `dotnet build-server shutdown`: a lingering MSBuild
node has held directory locks here. A running `Zerg.exe` or `ZergWeb.exe`
locks its output, so close it before rebuilding (`CloseMainWindow`, or the X).

## Layout

```
Zerg.slnx
src/Zerg.Core/             net10.0, no UI, so it is testable without a window
  EventFiles.cs            newest *.jsonl; open shared R|W|Delete; read whole lines from a byte offset
  EventsApi.cs             the /api/events response, field for field as damage-meter.py sends it
  Query.cs                 query strings with Python parse_qs semantics
  EventTail.cs             follows the newest file: offset carried, reset on a file switch or shrink
  EventReader.cs, Roster.cs, Events.cs   lines → damage rows, heals, roster (N1)
  Session.cs               the session clock: arm, latch, pause, At/Elapsed
  Counting.cs, Totals.cs   filter, credit, collapse, connects, aggregate, cumulative, distribution
  Healing.cs, Compare.cs   heal totals; A/B measure, diff, pace
  CompareSheet.cs          everything the Compare section prints for two runs, as text and bar
                           lengths: tiles, both cumulative lines, every table, who each row is,
                           a change (B - A) with its sign and tone; the session as a parse (N6)
  ParseFile.cs             export / import of a paused parse; the name an export is offered under
  Format.cs                numbers and clocks, spelled as a browser spells them for en-US
  Js.cs                    the JavaScript rules the numbers depend on (coercion, sort, JSON text)
  Charts/                  where a chart puts things, no drawing: Ticks, LineLayout, BarsLayout,
                           HistogramLayout, HoverCard, TextFit, SmallLines (N2), Sampling (N3)
  Tracker.cs               the live session: file followed, rows, clock, Start / Pause / Cancel,
                           and Count(), the whole pipeline in one call: damage (N3) and healing (N5).
                           Also an imported parse, read only (Tracker.Of), and Export() (N7)
  Cast.cs, Shades.cs       colour slots (owner in slot 0), shades for a shared job, hidden-name labels
  SessionView.cs           the two session buttons and the session's wording, as data
  PanelOpacities.cs        how see-through each floating panel is: a default, and a panel's own value (N4)
  KeyChord.cs              a key with its modifiers, as text ("Ctrl+Alt+Z") and as Windows wants it (N8)
src/Zerg/                  net10.0-windows WPF exe, Zerg.exe: the native app (NATIVE-PLAN.md)
  App.xaml(.cs)            Fluent theme, startup (single instance first), options, crash dialog
  MainWindow               command bar (the Damage | Healing | Compare switch first), character
                           chips (not over Compare), then the section on screen: five tiles and
                           its cards (or the stand-in for one that is floating), or the Compare
                           section; status line
  MainViewModel            holds a Tracker; Recount() on new lines or a filter change, Tick() 4x/s.
    (.Damage, .Healing)    One file per section; both sections are drawn on every count
    (.Parse)               Export, Import, Back to live. `live` is the session and is always fed;
                           `Shown` is the import while there is one, and is what gets drawn
  CompareViewModel.cs      the Compare section (MainViewModel.Compare): two slots (RunSlot), the
                           switches, and the rows drawn from a CompareSheet. Redraws only on a
                           change, and only while it is on screen
  ParseDialog.cs           the Open dialog for an exported parse (*.zerg and older *.json), and
                           the Save dialog for an export
  Rows.cs                  the list rows, kept and updated in place (Rows.Sync)
  Panels.cs                PanelSet / PanelInfo: which cards float, their opacity, the open windows (N4);
                           which were out last time (Reopen, Leave), click-through, Dock all (N8)
  TrayMenu.xaml            the tray icon's menu (N8)
  PanelWindow              a floating panel: one card in a borderless, always-on-top, see-through
                           window that never takes the keyboard; its bar, tint, sizes and placement
  Views/                   one UserControl per card (LineCard, BarsCard, ActionsCard, DrillCard, and
                           the Healing section's HealLineCard, HealBarsCard, HealActionsCard,
                           HealDrillCard), each with a docked and a floating form switched by
                           Float.On; Away (the stand-in for a floating card), Grow (a bar's eased
                           length), Cells (table-row panel), WheelChain, ActionRowTemplates,
                           HiddenConverter, PresentConverter; CompareSection (the whole Compare
                           section) and its cells: RunPair (A over B), ChangeText (B - A),
                           RunBars (a bar per run)
  EventFeed.cs             250 ms poll on the dispatcher + FileSystemWatcher to poll early
  Settings.cs              %LOCALAPPDATA%\VibeXI\zerg\settings.json
  AppTheme.cs              light or dark: sets the Fluent theme, merges Themes/Light|Dark.xaml;
                           after a switch, makes a screen reader look at every list again
  Themes/                  Zerg's own colours per theme: the 18 fallback slots, the job colours,
                           the chart surface, the two runs of a comparison
  Charts/                  the chart elements, which only paint: Chart (base), LineChart, BarChart,
                           HistogramChart, Models (their inputs), DrawnText (tabular figures)
  Native/                  TaskDialog (TaskDialogIndirect), WindowPlacement (Get/SetWindowPlacement,
                           and a panel's frame in screen pixels), Overlay (tool-window and
                           no-activate styles; a move or resize followed by hand; click-through),
                           SingleInstance (mutex + pipe), HotKey (RegisterHotKey), TrayIcon
                           (Shell_NotifyIcon) (N8)
  app.manifest             PerMonitorV2 + common controls v6
src/Zerg.Web/              net10.0-windows WinForms exe, ZergWeb.exe: the frozen web host
  Program.cs               log, crash dialog, options, WebView2 runtime check
  MainForm.cs              main window + WebView2; owns the pop-outs; handles window.open
  HostRouter.cs            answers every request to https://zerg.vibexi/ — the "server"
  Assets.cs                static files: embedded resources, or disk with --web-root/--dev
  host.js                  injected before the page's scripts: Zerg's only page adaptation
  PopoutForm.cs            one floating panel: always on top, colour-keyed background
  TintForm.cs              the darkening behind a keyed panel
  WindowPlacement.cs       remembers window positions
  Links.cs, Log.cs, Options.cs, AppInfo.cs
  Properties/launchSettings.json
tests/Zerg.Core.Tests/     xUnit v2: unit tests + Parity/ (reference.py runs the real server code)
                           + JsParity/ (the real JS under Jint; MutationTests proves it can fail;
                           Suite.Charts.cs runs chart.js against a canvas that records its calls)
tools/cdp.cs               drive the live page over the DevTools protocol (file-based app)
tools/drive.cs             drive the native windows: screen grabs, UI Automation, the real mouse (move,
                           click, drag), text fields, Enter and typed keys (for a file dialog), a
                           process's window list, zoom into a grab, a key chord (a global hot key),
                           the tray icon (file-based app)
tools/replay.py            play a fixture into a live event file, re-timed to now
tools/measure-tint.ps1     measure real on-screen darkening behind a panel
spike/OverlaySpike/        Phase 0 throwaway; not in the solution, not shipped
```

## How it works

### Requests (`HostRouter`)

The page is loaded from `https://zerg.vibexi/`, a made-up host. There is no
HTTP server and no port: `WebResourceRequested` (filter `https://zerg.vibexi/*`)
answers everything in-process.

| Path | Answer |
|---|---|
| `/api/events?file=&offset=` | `EventsApi.FromQuery` → raw lines since the offset. Read off the UI thread under a deferral |
| `/api/alpha?title=&value=&key=` | a pop-out's opacity / colour key, matched by `document.title` (below) |
| `/api/version` | `{ok, version}` |
| `/shared/*` | `shared-ui/` |
| anything else | `damage-meter/web/`; `/` is `index.html`; missing = 404 |

The host stays dumb, the same rule as the Python server: `/api/events` returns
raw lines, and all interpretation lives in `web/lib/source.js` and `stats.js`.

Embedded resources are named `web\…` and `shared\…` (see `Zerg.Web.csproj`). Files
starting with `_` in `web/` are left out (`web/_kirin-tmp.json` is scratch).
In `--dev`, `DiskAssets` serves the same paths from disk, with the Python
server's containment check per root.

### Pop-outs

Every card with `data-popout` gets a **Pop Out** button from
`damage-meter/web/lib/popout.js`, which moves the card's real DOM into another
window with `adoptNode`. In Chrome that window is Document Picture-in-Picture.
In Zerg:

1. `host.js` replaces `window.documentPictureInPicture` with a shim. Its
   `requestWindow()` is `window.open('', 'dpsPanel_<key>', 'popup=yes,…')`. The
   key comes from the click event in progress, so each panel has a stable
   window name. **Don't just delete the API:** `popout.js` disables the button
   when PiP is missing.
2. The shim adds one `<style id="zerg-keyed">` to the new document, making the
   page background transparent in `.key-bg` mode.
3. `MainForm.OnNewWindowRequested` takes a deferral, creates a `PopoutForm`,
   initialises its WebView2 in the **same** environment, attaches the router to
   it (the child's stylesheets are fetched by the child view), and sets
   `e.NewWindow`. The DOM `popout.js` has already written survives this, and
   `window.opener` holds.
4. `popout.js` then calls `/api/alpha` with the window's title, the slider value
   and `key=010203`.

**Transparency.** A colour key is all-or-nothing per pixel, and it **never**
drops pixels WebView2 drew (DirectComposition, as in Chrome). It drops the
*form's own background* wherever the page is transparent. So in keyed mode
(`popout.js`'s default):

- `PopoutForm`: `BackColor = TransparencyKey = #010203`, WebView2 background
  transparent, `Opacity = 1.0`, so text and bars are never faded.
- `TintForm` behind it: borderless black, click-through
  (`WS_EX_TRANSPARENT|LAYERED|NOACTIVATE`), sized to the panel's client area,
  `Opacity` = the slider. The **panel is owned by the tint**, which keeps it
  directly above. The tint follows `Move`/`Resize` and closes with the panel.
  Its `Opacity` must never be exactly 1.0 (it is layered from birth, and
  WinForms would drop the layered attributes, so it wouldn't draw).
- Without the key (`DPS.popout.keyBg('<key>', false)` in the page): no tint,
  and the slider fades the whole panel.

Panels are `SizableToolWindow`: thin caption, off the taskbar and Alt+Tab,
`TopMost`. Several can be open at once. Dock, the window's X, and closing the
main window all dock or close them correctly. Each panel's position is saved
under `popout:dpsPanel_<key>`.

### State on disk

All under `%LOCALAPPDATA%\VibeXI\zerg\`, shared by both builds. Their files
don't overlap:

| | |
|---|---|
| `logs\zerg.log` | **native app** log: startup, settings and theme changes, file switches, crashes |
| `settings.json` | **native app** settings (NATIVE-PLAN.md Decision 7): filters, theme, panel opacity, placement under `windows` (`main`, and `panel:<key>` for each floating panel), `openPanels` (the panels to bring back at the next start) and `clickThroughKey` (the hot key, "Ctrl+Alt+Z") |
| `zerg.log` | startup steps, crashes; in Debug builds every non-poll request and every alpha call. The first thing to read when something is wrong |
| `windows.json` | main and per-panel placement. Off-screen positions are never saved: Windows parks hidden windows at ~(−32000, −32000) |
| `WebView2\` | the browser profile, so the page's `localStorage` (theme, opacity default, exclusions…). These keys are listed in `../damage-meter/CLAUDE.md` |

Zerg's origin (`https://zerg.vibexi`) differs from the Python meter's
(`http://localhost:8731`), so settings are not shared between the two.

### WebView2 settings (`MainForm.Configure`, every view)

- DevTools only in Debug builds (F12).
- Status bar off.
- **`IsReputationCheckingRequired = false`.** SmartScreen otherwise looks up
  `zerg.vibexi` on every navigation, times out, and costs 2 s per launch. It
  also sends the URL to Microsoft.
- Navigation off our origin is cancelled and handed to the user's browser,
  http(s) only (`Links`).

## Verifying a change

1. **Tests:** `dotnet test apps/zerg/Zerg.slnx`, 278 tests (204 without the
   mutation check). The `Parity/` tests run the real `damage-meter.py` on the
   same bytes; any change to `EventFiles` or `EventsApi` must keep them green.
   The `JsParity/` tests run the real `source.js`, `stats.js`, `compare.js` and
   `chart.js` under Jint and compare every counting function field by field,
   and every chart's layout call by call; any change to the counting or chart
   layout code in `Zerg.Core` must keep them green, and
   `MutationTests` proves they can fail (NATIVE-PLAN.md, N1 handoff). Without
   Python both report *skipped*.
2. **The live page over CDP.** Launch with the debug port, for that run only:
   ```powershell
   $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9333'
   Start-Process apps\zerg\src\Zerg.Web\bin\Debug\net10.0-windows\ZergWeb.exe -ArgumentList '--events-dir','<dir>'
   dotnet run apps/zerg/tools/cdp.cs -- 9333 eval zerg.vibexi "DPS.popout.panels.line.focusBtn.click()"
   ```
   `DPS.*` is the page's console API (`../damage-meter/CLAUDE.md`). Useful
   handles:
   - `DPS.popout.panels.<key>` — `.mode`, `.win`, `.osAlpha`
   - `DPS.popout.alpha(key, v)` and `DPS.popout.dock(key)`
   - `#startBtn`, `#themeBtn`

   To test live updates, press Start, then append event lines to the file with
   `t` = now (copy a line from the fixture). Close the instance afterwards;
   don't leave a debug port open.

   **Check first whether the user already has Zerg running**:
   ```powershell
   Get-CimInstance Win32_Process -Filter "Name like 'Zerg%'" | Select ProcessId, CommandLine
   ```
   A second instance shares the same WebView2 profile. If its browser
   arguments differ (the debug port), it can't attach: startup throws
   `0x8007139F` and a crash dialog keeps the process alive. Ask the user to close
   theirs, and never stop a process you didn't start. Phase 4's single-instance
   work will turn a second launch into "focus the existing window".
3. **What's actually on screen.** Colour keying, opacity and the tint are
   invisible to CDP screenshots. Open a panel on the primary monitor over
   something uniform, then run
   `powershell -ExecutionPolicy Bypass -File apps/zerg/tools/measure-tint.ps1`.
   It prints inside/outside brightness, which should be about (1 − slider). Last
   measured: 85% → 0.16, 50% → 0.49, 20% → 0.76. It measures whichever Zerg
   is running, the user's included, and only reads the screen.
4. **`zerg.log`** for request and alpha traces (Debug builds).
5. **The native app.** It only counts what arrives after Start, so a
   fixture in the past has to be played in:
   ```bash
   : > <dir>/Hasaya_2026.10.03.jsonl
   apps/zerg/src/Zerg/bin/Debug/net10.0-windows/Zerg.exe --events-dir <dir> &
   dotnet run --file apps/zerg/tools/drive.cs -- click <pid> Start
   python apps/zerg/tools/replay.py apps/zerg/tools/events/Hasaya_2026.07.30.jsonl <dir>/Hasaya_2026.10.03.jsonl --extras
   # wait ~27 s for the squeezed session to pass, then:
   dotnet run --file apps/zerg/tools/drive.cs -- click <pid> Pause
   ```
   `tools/drive.cs` then reads and drives the window: `front <pid>`,
   `snap <pid> <png>`, `text <pid>` (every piece of text on screen, table
   cells included, one per line), `names <pid>` (controls only),
   `click <pid> <name>` (buttons, toggles, chips, action rows as
   `<character>, <action>`, expanders), `set <pid> <slider> <value>`,
   `scroll <pid> <percent>`, `hover <pid> <x> <y>` (moves the real mouse
   pointer to a point in a grab's own pixels, for hover cards and tooltips;
   it takes the pointer from the user), and
   `zoom <png> <x> <y> <w> <h> <times> <out>`.
   - The exact session times are in `logs\zerg.log` (`session paused: armed
     …, zero …, paused …`). To compare with the reference page, copy the live
     file into `apps/damage-meter/tools/events/` (ignored by git), start
     `damage-meter-fixture`, **navigate to the page again**, set
     `DPS.app.session` to those times, and `DPS.meter.render()`, all in one
     script call. Remove the copy afterwards.
   - It writes the real `settings.json`: filters toggled in a check are
     saved. Copy the file aside first and put it back.
   - Placement and theme live in `settings.json`: edit it between runs to
     test restore (a narrow window, the second monitor, maximized, -32000).
   - **The section** is switched with `click <pid> "Healing section"` (or
     `"Damage section"`). `replay.py --extras` also writes three pet heals,
     so the Pet Healing column and a healer with no healing of their own
     have something in them.
   - **The Compare section** is `click <pid> "Compare section"`. A slot is
     filled through the real Open dialog: `click <pid> "Open a parse into A"`,
     `windows <pid>` for the dialog's `h<hwnd>`, then
     `type h<hwnd> "File name:" <path>` and `enter h<hwnd>`. The switches are
     `"Compare damage"`, `"Compare healing"`, `"Compare by character"`,
     `"Compare by job"`, `"Swap A and B"`, `"Use current for A"`,
     `"Clear A"`; a row opens by its name; the chart's table is
     `"Data table of both runs"`. To compare with the reference's Compare
     page, load the same two files into it with
     `DPS.compareView.load('a', name, text)` and compare its text with
     `text`'s (NATIVE-PLAN.md, N6, has what to allow for).
   - **Import and export** are `click <pid> Import` and `click <pid> Export`
     (the session must be paused), then the dialog by its `h<hwnd>` from
     `windows <pid>`. An Open dialog takes `type h<hwnd> "File name:" <path>`
     and `enter`. **A Save dialog ignores `type`** and saves under the name
     it offered, in whatever folder it opened in: use
     `keys h<hwnd> <full path>` and then `enter`. `click <pid> "Back to live"`
     leaves an import.
   - **`where <target> <name>`** prints the middle of an element in a grab's
     own pixels, for `hover`, `press` and `drag`. A drag may end outside the
     window it began in: that is how a file is dropped from an Explorer
     window onto a slot.
   - **With the pointer resting on something that has a tooltip, `<pid>` can
     address the tooltip** (a tiny `snap`, an empty `text`). Use the window's
     `h<hwnd>`.
   - **A floating panel is not the process's main window.** `windows <pid>`
     lists every window as `h<hwnd>` with its title, frame and styles
     (`tool noactivate topmost layered` for a panel); use that `h<hwnd>` as
     the target of any other command. Panels are opened with
     `click <pid> "Pop out Cumulative damage"` (or `Damage by character`,
     `Actions`, `Drill-down`, `Cumulative healing`, `Healing by character`,
     `Heals`, `Heal drill-down`) and closed with `click h<hwnd> Dock`.
   - **Only one `Zerg.exe` runs at a time** (N8). Starting a second brings
     the first forward and exits with code 0, without a word: before a
     check, look for one already running (the command under item 2), and
     if it is the user's, ask them to close it.
   - **Click-through** is `chord Ctrl+Alt+Z` (the real keys, to whatever has
     the keyboard), `click <pid> "Click-through panels"` in the main window,
     or the tray menu. `windows <pid>` marks a click-through panel
     `clickthrough`. To prove a click passes through, lay the panel over a
     control of the main window (a character chip), `press` the panel there,
     and read `settings.json` for what changed.
   - **The tray icon.** `tray <h> rect` says where the icon is in screen
     pixels. On Windows 11 it starts in the overflow: with that closed the
     answer is the ^ button (48x72 here); `at <x> <y>` on it, wait a second,
     and `tray <h> rect` again gives the icon itself (60x60). Then
     `at <x> <y>` in its middle is a real click, and `at <x> <y> right`
     opens the menu, which is a window of the process with no title
     (`windows <pid>`): `names`, `snap` and `click h<menu> <line>` work on
     it, and the submenu is another such window. `tray <h> menu` opens the
     menu without the mouse, for reading only (NATIVE-PLAN.md, N8's traps).
   - **Minimized, Zerg is hidden in the tray**, and `<pid>` no longer finds
     its window: take its `h<hwnd>` first, or from `windows <pid> all`.
   - UI Automation presses a button without a click, so it cannot show
     whether a panel takes the keyboard. `press <target> <x> <y>` and
     `drag <target> <x1> <y1> <x2> <y2>` use the real left button, at points
     in a grab's own pixels; `fg` prints the foreground window. Run `fg`
     before and after: it must not become the panel. Panels are all topmost
     and cover each other, so move them apart first, or the press lands on
     whichever is on top.

## Traps already hit (each cost a debugging round)

- **The MIME type comes from the resolved file, not the URL.** `/` has no
  extension. Served as `application/octet-stream`, it became a *download*:
  WebView2 aborted after ~2 s and dropped `download (n)` files into the user's
  Downloads folder.
- **A deferral completes exactly once.** `using var deferral` plus
  `Complete()` throws 0x8000000E. Only `/api/events` takes one.
- **Async WinForms event handlers swallow nothing.** Exceptions reach
  `Application.ThreadException`, which logs and shows a dialog. If the page
  stays on `about:blank`, read `zerg.log`.
- **A class named `WindowState`** collides with `Form.WindowState` inside a
  Form (hence `WindowPlacement`).
- **The WebView2 NuGet package references its WPF assembly** even in WinForms
  (MSB3277). `Zerg.csproj` removes the reference in a target, rather than
  silencing the warning.
- **UI Automation lists a panel under an unnamed window** (its tint, the
  owner), not at the top level.
- **Screen captures from PowerShell are DPI-unaware**, so on the second monitor
  they grab the wrong region.
- **.NET 10 file-based apps** (`dotnet run x.cs`) have reflection-based JSON off.
  Use `JsonObject`, not anonymous types.

## Status and what's next

**Native migration:** NATIVE-PLAN.md phases S and N0 to N8 are done: the
whole feature checklist (N7, import / export, on 2026-10-03) and the four
extras (N8, on 2026-10-04: single instance, click-through panels with a hot
key, reopening panels, a tray icon). N9 is last and half done (2026-10-04):
step 3 is finished (`RULES.md`, `tools/gen-test-events.py`, `tools/exports/`)
and the user has their sign-off list. Publishing `Zerg.exe` over the old
snapshot and deleting the old code wait for the user to say the sign-off is
done and committed. The list and the handoff are under "N9, first half" in
the plan. `spike/` in the layout above no longer exists.

Phases 0–3 of `PLAN.md` are done (2026-10-02):
- Core tailer with parity tests.
- Main window.
- Native always-on-top pop-outs with see-through backgrounds and the tint.

**The user wants to iterate on the app before packaging.** Phase 4 (single-file
self-contained `Zerg.exe`, zip, icon, version resource, single instance) and
Phase 5 (clean-machine testing, SmartScreen, player README, release) are
waiting.

Known gaps and open ideas, none started:
- **A second launch:** it shows a crash dialog if its WebView2 options differ,
  and otherwise opens a second meter on the same file. Phase 4's
  single-instance mutex should focus the running window instead.
- **Pop-out tooltip:** it says "Only one panel at a time". That's true of
  Chrome's PiP, not of Zerg; the string lives in `popout.js`.
- **Frameless panels:** dragged by the page's own bar instead of a caption.
- **Click-through panels:** with a hotkey to take them back. The spike proved
  Ctrl+Alt+Z with `WS_EX_TRANSPARENT`.
- **Dark title bars:** for the main window and panels
  (`DWMWA_USE_IMMERSIVE_DARK_MODE`), following the page theme.
- **Missing favicon:** `favicon.ico` 404s; there is no icon yet.
- **Fonts need internet:** Google Fonts load online and fall back to system
  fonts offline.
- **Open questions in PLAN.md:** the exe name, whether Zerg should also host the
  calculators, and signing.
