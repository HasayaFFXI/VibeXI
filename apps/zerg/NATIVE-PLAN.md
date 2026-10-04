# Zerg native — a Windows 11 app on WPF

Goal: **`Zerg.exe`**, a native Windows 11 application on WPF. Every window is a
WPF window, every screen is drawn by WPF, and the Windows features it needs come
from native APIs. **All existing functionality is kept**; the look is free to
change. When Zerg is complete and tested, `apps/damage-meter/` is deleted.

Decided with the user on 2026-10-02:
- **WPF**, built piece by piece, with a working app at every step.
- **A full rebrand to Zerg** (the naming rule below).
- **Windows 11 style.** Styling can change freely; functionality can't.
- **No parallel maintenance.** Today's web build and `apps/damage-meter` are
  frozen references until they are deleted. Nothing is fixed in them unless
  the user asks.
- **JavaScript is transitional only.** The web front end is the reference while
  native is built, then it goes. JS survives only if a native piece actually
  ships needing it (the chart contingency below), and only with the user's
  agreement.
- **No checks are kept permanently.** The parity tests against the old code are
  scaffolding, and they are deleted with it.

`PLAN.md` is the record of how Zerg reached its current form (a WinForms +
WebView2 host, Phases 0–3). This file is the plan from here. Phases S and N0
to N8 are done: every row of the feature checklist, and the four extras. What
is left is N9, and it is half done (2026-10-04): step 3 is finished, the
user's sign-off list is written, and the rest (publishing, deleting the old
code, the gate) waits for the user to run that list. **Start at "N9, first
half" under N9**: it has the list and the handoff for the rest.

---

## The naming rule

The finished Zerg contains **no reference to "Damage Meter" or "Metrics"**, in
any form. This covers UI text, window titles, code identifiers, comments,
assembly metadata (today's `AssemblyTitle` is "Zerg damage meter"), file names,
log lines, docs and tests. The product is **Zerg**; the exe is **`Zerg.exe`**.
Describe features by what they show (damage, DPS, healing, per-character
breakdown), not by the old product's name.

- The rules the old code encodes are kept, but are described in Zerg's own
  words, with their reasons and without naming where they came from.
- Today's "Metrics table" becomes the **per-character table**.
- **Allowed until N9 only:** this plan, the frozen reference build and the
  transitional parity tests may name `apps/damage-meter`, because they point at
  it. N9 removes all of them.
- **The N9 gate:** a case-insensitive search of `apps/zerg/` for
  `damage.?meter` and `metrics` finds nothing. Any hit is fixed, not excused.

## How the migration runs

**Two projects, one of them frozen.**

```
src/Zerg.Web/   ZergWeb.exe  today's WinForms + WebView2 host, moved here in N0. Frozen.
                             The reference to check native against. Deleted in N9.
src/Zerg/       Zerg.exe     the new WPF app, growing one view per phase.
```

Separate projects keep WinForms and WPF out of each other's way (both define
`Application`, `MessageBox`, `Control` and more), and mean the native app is
never built around the thing it replaces. The user's desktop shortcut runs the
published snapshot in `dist/Zerg/`, so it keeps working through the move; the
new `Zerg.exe` replaces that snapshot at N9.

**Checking against the reference.** Before each phase is done, its view is run
next to `ZergWeb.exe` (or `damage-meter.py` in Chrome) on the same event file.
What must match is **numbers and behaviour**: every figure, row, ordering,
state change and interaction. The look is expected to differ.

**The chart contingency (the only JavaScript fallback).** If the native chart
control (N2) blocks a later phase, a chart can be drawn temporarily by the real
`chart.js` in a WebView2 inside the native window, fed from C# one way (C# owns
all state; the page only draws). This works because `chart.js` is already
data-in, canvas-out and the C# port produces the same shapes. It is built only
if needed, and removed before N9 unless the user agrees to keep it, in which
case `chart.js` is copied into Zerg and renamed. Nothing else has a web
fallback: the rest of `app.js` is written against the page's global state and
can't be lifted out piece by piece.

Rejected: **running the old JS under Jint at runtime** as a compute fallback.
It would be a second source of truth and would hide bugs in the port.

```
S   spikes: Fluent theme + text, chart cost
N0  project split; native shell (Fluent, Mica, settings, live status line)
N1  counting logic in C#, proved equal to the old JS
N2  chart control
N3  Damage section      N4  floating panels
N5  Healing section     N6  Compare section     N7  import / export
N8  extras (only what the user wants)
N9  sign-off; publish Zerg.exe; delete the old code; rebrand gate
```

## What carries over, and what is rebuilt

| Today | Native |
|---|---|
| `Zerg.Core/EventFiles.cs` (newest file, shared open, whole lines from an offset) | **kept**, read directly by the app; comments reworded for the naming rule |
| `Zerg.Core/EventsApi.cs`, `Query.cs` (the old `/api/events` shape) | used only by the frozen web build; deleted in N9 |
| `MainForm`, `PopoutForm`, `TintForm`, `HostRouter`, `Assets`, `host.js` | frozen in `Zerg.Web`; deleted in N9 |
| `WindowPlacement`, `Log`, `Options`, `Links`, `AppInfo` | rebuilt in the WPF app (placement on `Get/SetWindowPlacement`) |
| `web/lib/source.js` (lines → events, roster, jobs, heals, export/import) | ported to C# in `Zerg.Core` |
| `web/lib/stats.js` (session clock, filter, credit, collapse, accuracy, aggregate, cumulative, distribution, healing, formatters) | ported to C# in `Zerg.Core` |
| `web/lib/compare.js` (measure, diff, pace, actions) | ported to C# in `Zerg.Core` |
| `web/lib/chart.js` (line, bars, histogram) | its behaviour ported to a WPF chart control, restyled |
| `web/app.js`, `web/compare.js` (all DOM writing, ~2,750 lines) | rebuilt as WPF views and view models, redesigned |
| `web/lib/popout.js` | rebuilt as native panel windows |
| `shared-ui` theme tokens | replaced by the Fluent theme plus Zerg's own data colours (below) |
| Inter, JetBrains Mono, Shippori Mincho from Google Fonts | dropped for Windows' own fonts: nothing embedded, nothing online |

About 6,600 lines of JS and 1,750 of CSS are replaced. The counting logic is the
part that must not drift: what counts as a hit, skillchain and pet crediting,
collapsing per `use`, the session latch. `../damage-meter/CLAUDE.md` records why
each rule is the way it is; that reasoning moves into Zerg's docs in N9, in
Zerg's words.

## Look and feel: Windows 11

Freedom to restyle, inside these lines:

- **The built-in Fluent theme** (`Application.ThemeMode`, in WPF since .NET 9).
  It is still marked experimental in .NET 10 (diagnostic `WPF0001`, present in
  the installed 10.0.12 reference assemblies), so the project suppresses that
  one diagnostic; spike S1 confirms it is solid enough. No third-party UI
  library.
- **Mica** on the main window. **System accent colour** for primary actions and
  selection. **Rounded corners** (DWM on normal windows; drawn by the panel on
  `AllowsTransparency` panels, which DWM doesn't round).
- **Light, dark or follow Windows.** Today's theme toggle becomes a three-way
  setting, defaulting to System.
- **Segoe UI Variable** for text (WPF sees it only as `Segoe UI Variable Text`
  and `… Display`, with `Segoe UI` as the fallback; S1), with tabular figures
  (`Typography.NumeralAlignment="Tabular"`) wherever numbers update live, so
  they don't jitter. **Cascadia Mono** where monospaced text is wanted. **Segoe
  Fluent Icons** for button glyphs.
- **Layout is open:** e.g. a command bar for Start/Pause/Export/Import, the
  section switch as a tab or navigation control, tiles as stat cards, cards as
  layered surfaces. Every control and readout on the checklist must still exist
  and do the same thing.
- **Data colours keep their meaning** and may be retuned for contrast on Mica in
  both themes: a job always has its own hue, repeated jobs are shaded, the
  18-slot fallback palette stays distinguishable, and the owner keeps slot 0.

## Native APIs and WPF drawing

**Windows and the shell**

| Need | Native API |
|---|---|
| See-through floating panels | `AllowsTransparency` window, per-pixel alpha: background black at the slider's alpha, content opaque (proved in `poc/wpf`) |
| Always on top, off the taskbar and Alt+Tab | `Topmost`, `WS_EX_TOOLWINDOW`, `ShowInTaskbar=false` |
| Panels that don't take focus from the game | `WS_EX_NOACTIVATE`, and `MA_NOACTIVATE` for a click. Moving and resizing are done by hand (`SetWindowPos` with `SWP_NOACTIVATE` under a mouse capture): the system's own drag loop makes the window the foreground window whatever its style (N4) |
| Click-through panels | `WS_EX_TRANSPARENT \| WS_EX_LAYERED` via `SetWindowLongPtr` |
| Global hotkey | `RegisterHotKey`, `WM_HOTKEY` through an `HwndSource` hook |
| Mica, dark caption, corners | the Fluent theme; `DwmSetWindowAttribute` (`DWMWA_SYSTEMBACKDROP_TYPE`, `DWMWA_USE_IMMERSIVE_DARK_MODE`, `DWMWA_WINDOW_CORNER_PREFERENCE`) where the theme doesn't |
| Tray icon | `Shell_NotifyIconW` (P/Invoke; no WinForms `NotifyIcon`) |
| Single instance | named `Mutex` + a named pipe telling the first instance to come forward (`AllowSetForegroundWindow`) |
| Window placement across monitors and DPI | `GetWindowPlacement`/`SetWindowPlacement`; `MonitorFromRect` to reject off-screen positions |
| Per-monitor DPI (the second monitor scales differently) | PerMonitorV2 manifest; WPF's `DpiChanged` |
| Open / save dialogs | `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog` (the Common Item Dialog) |
| Crash and error dialogs | `TaskDialogIndirect` (comctl32 v6 via the manifest) |
| Noticing the event file grow | `FileSystemWatcher` (`ReadDirectoryChangesW`) to poll early; the 250 ms poll stays the source of truth |
| Opening links | `Process.Start` with `UseShellExecute`, http(s) only |

**Drawing**

| Piece | WPF approach |
|---|---|
| Charts (line, bars, histogram) | One `FrameworkElement` with layered `DrawingVisual`s: a series layer (frozen `StreamGeometry` + frozen pens, under a `BitmapCache`) and a furniture layer (axes, grid, labels, markers), redrawn when data changes and when the live edge steps; an overlay layer (crosshair, hover card) redrawn on mouse move. The live edge steps with the 4×/s clock tick, never per frame: S2 measured per-frame redraws at a whole core on these 120 Hz monitors (`poc/wpf/README.md`). Text through cached `FormattedText` |
| Tiles, status line, filter chips | data binding to view models |
| Bars moving between polls | `DoubleAnimation` on width, 250 ms (as `poc/wpf`) |
| Per-character table, actions, heals | `ItemsControl` + `Grid.IsSharedSizeScope`, Fluent-styled; not `DataGrid`, which is heavy and hard to style |
| Per-hit drill-down (can be long) | the same, virtualised: `VirtualizingStackPanel` in recycling mode |
| Theme switch at runtime | all brushes as `DynamicResource`; frozen where static |

## Decisions

**1. WPF on .NET 10, a new `src/Zerg` project producing `Zerg.exe`.** No
WinForms and no WebView2 in it (unless the chart contingency is used). WPF over
WinUI 3 because overlay windows need true per-pixel transparency
(`AllowsTransparency`), which WPF has and WinUI 3 makes awkward (`poc/`
bake-off). The old host moves to `src/Zerg.Web` (`ZergWeb.exe`), frozen.

**2. The Windows 11 look comes from the built-in Fluent theme**, not a library
(see "Look and feel").

**3. The counting logic is ported, not re-derived, and proved equal to the old
JS.** Each function in `source.js`, `stats.js` and `compare.js` gets a C# twin
in `Zerg.Core`. A parity test project runs the **real JS files** in .NET with
**Jint** (BSD-2, test-only) on the same inputs and compares results field by
field, as the existing tests do against `damage-meter.py`. Inputs:
- `../damage-meter/tools/gen-test-events.py` output
- the real 18-character alliance export `tools/exports/Paradox_Kirin_4.json`
- randomised sessions and filters (start/pause points, exclusions, skillchains
  on/off)

Both parity suites are scaffolding and are deleted in N9. Zerg's own unit tests
of its own code stay, as ordinary development tests.

**4. Charts port `chart.js`'s behaviour, not its look, and use no library.** The
behaviour that must survive:
- end labels nudged apart so they never collide
- "N others" grouping, drawn distinctly
- crosshair and hover card
- gaps in Compare's shorter run
- a live edge animated between polls
- job colours and the 18-slot palette

Bending a library (ScottPlot, LiveCharts2) to all of that is more work than
porting 680 lines of canvas drawing to `DrawingContext`.

**5. Light MVVM, no framework.** View models use `CommunityToolkit.Mvvm` (MIT,
Microsoft) source generators for `INotifyPropertyChanged` and commands.

**6. Panels use real per-pixel alpha** ("dark tint, solid text", as chosen on
2026-10-02): a borderless `AllowsTransparency` window, black at the slider's
alpha behind fully opaque content. No colour key and no tint window.

**7. Settings in `%LOCALAPPDATA%\VibeXI\zerg\settings.json`:** theme, exclusions,
skillchains, Hide names, line grouping, panel opacity, panel placement, section,
compare settings. They start fresh: nothing is imported from the old build's
browser profile.

**7a. Exported parses are `.zerg` files.** Zerg saves exports with the
`.zerg` extension (`<Owner>_parse_<date>_<time>.zerg`), not today's `.json`.
The contents keep today's JSON format and `PARSE_VERSION`. Import and the
Compare slots offer `*.zerg` first and also accept older `.json` exports, so
parses players already exported still load. Detection is by content, not by
extension.

**8. Polling stays at 250 ms, and the clock ticks at 250 ms**, both on the
dispatcher. An idle poll doesn't rebuild views.

**9. The look built in N2 to N5 is confirmed** (by the user, 2026-10-03):
everything under "How it looks" in those phases. That includes Start in green
and amber, the ✕ for Dock, the layout of the command bar and tiles, the chart
inks and hover card, the stand-in for a floating card, and the panels' opening
sizes. A later change to any of it is an ordinary change request, not an open
question. New visual choices from N6 on are still asked about.

**10. A heal panel's bar shows total healing**, not total damage (2026-10-03).
The four damage panels keep the damage total. This is a deliberate difference
from the reference, which showed the damage total on every panel. Built at
the start of N6.

**11. Above the Compare section, the bar is the reference's** (2026-10-03).
The character chips are hidden. The section switch, Start and Pause with the
clock's state, Hide names and the theme stay, and the session goes on running
underneath. Include Skillchains is on the Compare card, and is the same one
setting as the live sections'.

**12. Compare persists two settings: Compare by, and its Damage | Healing
mode.** The reference's notes say only the first is kept, but its code keeps
both (`ffxi_dps_cmpmode` beside `ffxi_dps_cmpby` in `compare.js`), and the code
is what is matched. The slots are not saved.

**13. The checks that need the game are one list, run once at N9** (2026-10-03):
the panels over the running game, the live clock past an hour, and a session
against the real addon with heals in it. They are under N9, step 1. No phase
before N9 waits on them.

**14. N8 is four extras** (picked by the user, 2026-10-03): single instance,
click-through panels with a global hotkey, reopening the panels that were open,
and a tray icon. Opening a `.zerg` file by double-click was not picked.

**15. How the four extras behave** (the user's answers, 2026-10-04):
- **The hot key is Ctrl+Alt+Z**, and it switches every open panel at once.
- **A click-through panel's bar slims to what is read**: Start and Pause, the
  opacity slider and Dock go; the title, the clock and the total stay, with a
  small lock. Nothing is shown that cannot be pressed.
- **The X still quits. Minimizing hides the main window in the tray**: off the
  taskbar, with the panels still up and the session still measured.
- **The tray icon**: a left click brings the main window forward. Its menu is
  Show Zerg, Start and Pause (worded and enabled as in the main window),
  Click-through panels (ticked while on, with the hot key beside it), a
  Panels submenu (each of the eight cards, ticked while floating), Dock all
  panels, and Exit.

## The feature checklist (the definition of "everything kept")

Each phase ticks off its rows. N9 needs every row ticked, checked against the
reference on the same file. Layout and styling may differ; the behaviour may
not.

**Live session**
- [x] Tail the newest `*.jsonl`; switch files on rollover; reset state on a new file
- [x] Start arms; the first counted event latches the zero; Pause/Resume; Cancel while armed; Start during a session re-arms
- [x] Elapsed clock (`MM:SS` / `H:MM:SS`), a distinct look for each state (running / paused / idle), ticking 4×/s; DPS decays between polls
- [x] Status: file, event and line counts, session state; armed indicator

**Filters and display**
- [x] Character chips: include/exclude (persisted, keyed by name), All/None, collapse row, `n/m` count and hint
- [x] Include Skillchains toggle (persisted)
- [x] Hide names: job aliases (`SAM/WAR 2`), owner kept, Job column dropped
- [x] Light / dark / follow Windows (persisted)
- [x] Job colours with shading for repeated jobs; 18-slot fallback palette; owner pinned to slot 0
- [x] Damage | Healing | Compare section switch (persisted)

**Damage section**
- [x] Tiles: Total damage, Elapsed, Party DPS, Top DPS, Biggest hit
- [x] Cumulative damage chart: every line coloured, Group under 5% (persisted), end labels, crosshair and hover card, data table, live edge
- [x] Damage by character: bars chart + the 14-column per-character table, nobody at zero, null as a dash
- [x] Actions table
- [x] Drill-down: tiles, histogram, per-hit table, Close; skillchain drill-down

**Healing section**
- [x] Tiles (Total, Elapsed, HPS, Top, Biggest heal); cumulative healing; healing by character; heals table; heal drill-down; pet healing column; HPS ticking

**Compare section**
- [x] Slots A/B: load from file, Use current; Damage | Healing mode; by Character | by Job
- [x] Tiles with deltas (B − A, % of A, good/bad colours); pace chart with the shorter run ending; tables (actors, kinds, targets, heal spells, heal targets)

**Import / export**
- [x] Export while paused; Save dialog; suggested name `<Owner>_parse_<date>_<time>.zerg` (Decision 7a)
- [x] Import; Back to live; an import locks the session buttons; `.zerg` files and older `.json` exports both import

**Floating panels**
- [x] Pop out any Damage card (line, bars, actions, drill); several at once
- [x] Pop out the Healing cards, the heal twins of those four
- [x] Always on top; dark tint at the slider value, solid text; per-panel opacity; a default opacity
- [x] Panel bar: title, Start/Pause, clock, total, opacity slider, Dock
- [x] Floating bars card is the compact strip (Damage / % / Acc); floating line card fills the window
- [x] Placement remembered per panel; closing a panel docks it

**Extras (N8; nothing the reference had, so nothing to check them against)**
- [x] Single instance: a second launch brings the first one's window forward and ends
- [x] Click-through panels, switched by a global hot key (Ctrl+Alt+Z), the main window and the tray menu
- [x] The panels that were floating when Zerg closed come back at the next start, where they were
- [x] Tray icon: click to bring the window forward; menu; minimizing hides the main window in the tray

The ticked rows were checked on the desktop, over other windows and with
another application in front. **Not yet over the game**: it was not running
when N4 or N5 was built. That check is part of the N9 sign-off (Decision 13).

## Phases

### S — spikes
Throwaway, in `poc/wpf/`, each answering a question that could change a phase:
1. **S1: Fluent theme and text.** `ThemeMode` on .NET 10 with Mica, in light,
   dark and System, switched at runtime; how the stock controls Zerg needs
   look (buttons, toggles, sliders, tabs, menus, scroll bars, tooltips); live
   tabular numbers in Segoe UI Variable on both monitors; `Display` vs `Ideal`
   text formatting at small sizes.
2. **S2: chart cost.** A `DrawingVisual` line chart with 18 series × 30 minutes
   of points, redrawn at 4×/s with a live edge and a moving crosshair. CPU must
   stay below the web build's on the same data.

**Done when** each has a written result in `poc/wpf/README.md`.
**Done 2026-10-02.** Both passed; S2 changed how the chart's live edge moves
(the Drawing table above).

### N0 — project split and the native shell
1. Move today's host to `src/Zerg.Web` (`AssemblyName` `ZergWeb`), unchanged
   otherwise; build it, run the tests, check it still runs.
2. New `src/Zerg` (WPF, `Zerg.exe`): `App.xaml` with the Fluent theme, Mica,
   per-monitor DPI manifest, the icon, version info; log, crash dialog
   (`TaskDialogIndirect`), options (`--events-dir`).
3. Settings file (Decision 7) and window placement.
4. The main window showing the live status from a tail (`EventFiles` +
   `FileSystemWatcher` + the 250 ms poll), as a proof of the loop.

**Done when** `Zerg.exe` opens a Windows 11-styled window that follows the event
file live in all three theme settings, and `ZergWeb.exe` still works.
**Done 2026-10-02.**

#### What N0 built, and what it taught (handoff to N1)

- **Layout.** `src/Zerg.Web` is the old host, unchanged except
  `AssemblyName` `ZergWeb`. `src/Zerg` is the WPF app: `App.xaml(.cs)` (theme,
  crash handling, options, startup), `MainWindow` + `MainViewModel` (the live
  readout and the theme setting), `EventFeed` (the poll loop), `Settings`,
  `Native/TaskDialog`, `Native/WindowPlacement`, `EqualsConverter`, `Log`,
  `Options`, `AppInfo`, `app.manifest`. `Zerg.Core/EventTail.cs` holds the
  follow-the-newest-file rules (file switch and shrink reset, offset carried),
  with 6 unit tests; the suite is 53 tests.
- **No file clashes with the web build.** The new log is
  `%LOCALAPPDATA%\VibeXI\zerg\logs\zerg.log` (the web build and the desktop
  snapshot keep `zerg.log` at the top). Placement lives in `settings.json`
  under `windows`, not `windows.json`. So ZergWeb needed no edit for this.
- **The main window's placement** round-trips exactly, normal and maximized,
  on both monitors, including the 150 % second one. Two things made that work:
  `SetWindowPlacement` is called twice from `SourceInitialized` (the first
  move onto a different-DPI monitor gets resized by WPF), and `WindowState =
  Maximized` is set **after** it. Set before the handle exists, WPF creates
  the window maximized on the default monitor and it stays there. A record
  that is off every screen (e.g. the −32000 parking spot) falls back to
  centring on the primary.
- **`TaskDialogIndirect` works** with the comctl32 v6 dependency in the
  manifest. `TASKDIALOGCONFIG` is byte-packed (160 bytes on x64). While a task
  dialog is open the dispatcher timers did not fire (a timer throwing every
  second logged once in ~10 s), so the feed pauses behind a crash dialog; and
  the guard keeps it to one dialog at a time. UI Automation's Invoke doesn't
  press a task dialog's buttons; Enter does.
- **The watcher does fire for appends** here: 5 of 5 appends from another
  process were picked up early. That test closed the file after each write;
  the addon keeps its handle open, which may report later. The 250 ms poll is
  still the source of truth either way.
- **Theme.** A row of `RadioButton`s styled as `ToggleButton`s, two-way bound
  through `EqualsConverter`, makes the three-way setting. The theme brushes
  `SystemFillColorSuccess/Caution/Critical/NeutralBrush` exist in WPF's
  Fluent dictionaries. A computed property on `Settings` needs `[JsonIgnore]`
  or it is saved too.
- **Toggles whose content is a panel** (icon + text) have no UI Automation
  name: give them `AutomationProperties.Name`.
- **`tools/drive.cs`** checks the native window from a script: screen grabs
  with Mica (per-monitor aware), and UI Automation listing and pressing.
- **The status readout is a stand-in.** File, lines read, size and last-line
  age prove the loop. The real status line (events, session state, armed) is
  N3's, on N1's logic.

### N1 — the counting logic in C#
1. Port `source.js`: line parsing, `OURS`, roster (kinds, jobs, manual), heals,
   meta/job lines, `parseFilename`, export/import parse format.
2. Port `stats.js` in full: the session model, `counted`/`filter`/`credit`/
   `collapse`/`connects`, `aggregate`, `cumulative`, `distribution`,
   `filterHeals`/`healing`, `quantile`, and the formatters.
3. Port `lib/compare.js`.
4. `tests/Zerg.Core.Tests/JsParity/`: the real JS under Jint, compared field by
   field (Decision 3). Names in the C# follow the naming rule from the start.

**Done when** every exported function matches the JS on all fixtures,
including randomised sessions and filters, with a mutation check proving the
tests can fail.
**Done 2026-10-02.**

#### What N1 built, and what it taught (handoff to N2)

- **Where each JS function went** (all in `Zerg.Core`, no UI):

  | JS | C# |
  |---|---|
  | `source.create`, `feed`, `feedRecord`, `reset`, `parseAll`, `parseFilename` | `EventReader` |
  | `createRoster` (`note`, `noteJob`, `jobLabel`, `jobTitle`, `isMob`, `setManual`…) | `Roster` |
  | `exportParse`, `stringifyParse`, `importParse` | `ParseFile.Export`, `Stringify`, `Import` (errors are `ParseImportException` with an `ImportError` code) |
  | `arm`, `idleSession`, `sessionStart/At/Elapsed/Running/Armed/Pause/Resume` | `Session` (instance methods) |
  | `counted`, `filter`, `firstCounted`, `credit`, `collapse`, `connects`, `aggregate`, `cumulative`, `distribution`, `quantile` | `Counting` (result types in `Totals.cs`) |
  | `filterHeals`, `healing` | `Healing.Filter`, `Healing.Totals` |
  | `fmtInt`, `fmtNum`, `fmtCompact`, `fmtClock`, `fmtElapsed`, `fmtStopwatch`, `fmtDuration` | `Format` |
  | `compare.measure`, `diff`, `actions`, `healActions`, `pace`, `delta`, `KINDS` | `Compare` |

  Rows are immutable records (`CombatEvent`, `HealEvent`), copied with `with`,
  so the list read from the file stays true to it. Numbers stay doubles, as
  the JSON's are. `Js.cs` holds the JavaScript rules the numbers depend on:
  `+v` coercion, truthiness, `Math.round`, stable sort, object key order,
  `Number::toString`, and a `JSON.stringify` twin (exports are byte-identical).
- **The parity suite** (`tests/Zerg.Core.Tests/JsParity/`) runs the real
  `source.js`, `stats.js` and `compare.js` under Jint 4.16.4 and compares
  every result field by field (`Canon` turns both sides into one plain shape).
  Inputs: the generator at seeds default/7/11, the fixture with extra edge
  rows (`Fixtures.EventsWithEdges`), the Kirin alliance export, three
  generated exports, 100 randomised sessions and filters (arm points, latches,
  pauses on and off event times, exclusions, skillchains, manual overrides,
  each followed by an export round trip), 50 malformed import documents,
  ~40 malformed lines, and odd file names. 67 tests without the mutation
  check, under a minute.
- **The mutation check** (`MutationTests`, trait `Category=Mutation`, about
  3½ min) breaks 47 rules in the JS one at a time and requires the suite to
  notice; all 47 are caught. Skip it day to day with
  `dotnet test apps/zerg/Zerg.slnx --filter Category!=Mutation`. The first
  run missed ten, which is why the edge rows exist (a 0-damage WS flagged a
  hit, a use whose later row is earlier or whose first row misses, a pet's
  heals, tied characters, a `NON` job, a zero-damage action, a name first
  seen as "other"). One candidate was an equivalent mutant: `roster.note`'s
  first line (`kind === 'other' && kinds[name]`) is redundant with the line
  after it.
- **Number formatting is Chrome's, not Jint's or .NET's.** A browser rounds
  the number as written (the shortest decimal form: 2.675 → 2.68, 0.15 → 0.2),
  halves away from zero, keeps "-0", and prints every digit of a big number.
  .NET's `N` format rounds the binary value and sends exact halves to even;
  Jint matches Chrome except past 15 significant digits and on
  0.49999999999999994. `Format` does it by hand on the shortest digits, and
  `FormatTests` pins it to strings captured from Chrome 152.
- **Faithful, odd but harmless:** `collapse` does not carry `by` onto a
  folded use (nothing downstream reads it there); a BOM-prefixed line or
  export is not JSON, as in the browser; a name like "constructor" would hit
  JavaScript's object prototype in the old code and is not imitated.
- **Import messages say "Zerg"** where the JS said "Damage Meter"; the suite
  matches errors by code, not wording. `ParseFile.Export`'s `keep` takes the
  row's wall-clock time (the only thing the UI's `keep` ever read).
- **A tooling trap:** a `\u` escape written into a file through the editor
  tools arrives as the raw character (a raw U+2028 broke the C# lexer). Write
  `(char)0x2028`, or `\p{Zl}` in a regex, instead.

### N2 — the chart control
`line`, `bars`, `histogram`, nice ticks, time ticks, the hover card, in S2's
design and the Fluent look. **Done when** each chart, drawn from the same data
as the reference, shows the same values, labels, grouping and hover readouts in
both themes.
**Done 2026-10-02.**

#### What N2 built, and what it taught (handoff to N3)

- **Two halves: layout in Core, paint in the app.**
  - `Zerg.Core/Charts` decides where everything goes. It takes numbers and a
    text-width function and returns positions, labels and hover readouts:
    `Ticks` (`Nice`, `Time`), `LineLayout`, `BarsLayout`, `HistogramLayout`,
    `HoverCard.Place`, `TextFit.Clip`, and `SmallLines` (the "Group under 5%"
    rule). No UI, so it is unit-tested and compared with `chart.js`.
  - `src/Zerg/Charts` paints what the layout says: `Chart` (the base: inks,
    drawn text, hairlines, the empty state, the hover card), `LineChart`,
    `BarChart`, `HistogramChart`, `Models.cs` (what the charts are given) and
    `DrawnText`.
- **What a chart is given.** Every input is a dependency property, so a view
  model can bind to it. The models are immutable: build a new one for each
  render. A chart never changes what it was given.

  | Element | Give it | Notes |
  |---|---|---|
  | `LineChart` | `Model`: a `LineModel` (`Times`, `Series` of `LineSeries(Name, Values, Color, Group)`, `Live`) | `EndLabels`, `Compact`, `Edge`, `EmptyText` |
  | `BarChart` | `Rows`: `BarRow(Label, Value, Color, Hover, Key)` | set `Height` to `BarsLayout.HeightFor(rows)`; `Hover` is the card's label/value rows; `Key` says which bar is which between updates (the label unless given: pass the real name when names are hidden) |
  | `HistogramChart` | `Model`: `HistogramModel.From(distribution)`, and `Fill` | |

  `Chart.HoverAt(point)` puts the pointer somewhere without the mouse, and
  `Chart.Readout` is what the hover card says, as data.
- **Feeding the cumulative chart** (`MainViewModel.DrawLine` is this, in code):
  1. take the aggregate's characters with a total above zero;
  2. `Counting.Cumulative(events, names, from: 0, now: session.Elapsed())`;
  3. one `LineSeries` each, with the colour and the drawn name;
  4. with grouping on, sum the series named by `SmallLines.Pick(agg, owner)`
     into one `Group` series called `SmallLines.Name(count)`;
  5. order by last value, smallest first, so the leader is drawn on top;
  6. `new LineModel(times, series, cumulative.Live == true)`.
- **The live edge is `LineChart.Edge`.** Set it to the session clock from the
  4×/s tick. The chart moves a live model's last grid time there and redraws.
  It copies the grid first, so one model can feed a docked chart and a floating
  one. A new `Model` makes the chart forget the edge it had, because new data
  states its own. While paused the clock is frozen, so setting it changes
  nothing.
- **Proved against `chart.js`.**
  - `JsParity/Suite.Charts.cs` runs the real `chart.js` under Jint with a
    canvas that records every call. The C# layout is turned into the calls it
    stands for and the two lists are compared: every label, gridline, marker,
    line, bar and column, then the hover card's text and position at 21 to 23
    pointer positions per chart. 177 charts: the edged fixture, generator seed
    7, the Kirin alliance (all lines and grouped), two pace charts, and made-up
    edge shapes, at eight sizes.
  - The mutation check has 27 `chart.js` rules; all are caught. One candidate
    was an equivalent mutant: `yTop = max(vmax, last tick)` is always `vmax`,
    because the tick picker never returns a tick past the maximum.
  - By hand, on the generated fixture: the reference page and the gallery agree
    on the total (151,387 in 24:01), the line chart's card at 14:52, the bars
    card for Hasaya, and the histogram's bin and mean for Parabellum. On the
    Kirin parse grouping turns 13 lines into 9 with "5 others", as the
    reference does.
- **One deliberate difference from the reference.** `chart.js` cuts a long name
  in an end label by its width in regular type and then draws it semibold, so
  the drawn label can overrun the margin sized for it. Zerg measures in the
  weight it draws. A name longer than 104 px can lose one more character than
  in the reference. The parity suite measures text with one width for every
  weight so this does not show there.
- **Tabular figures needed the text formatter.** `FormattedText` can't ask a
  font for them. `DrawnText` formats a line through `TextFormatter` with its
  own typography properties. Every number a chart draws is tabular.
- **How it looks** (confirmed by the user on 2026-10-03, Decision 9):
  - Inks are Fluent's: text `TextFillColorPrimary/Secondary/TertiaryBrush`,
    grid `DividerStrokeColorDefaultBrush`, axis and crosshair
    `SurfaceStrokeColorDefaultBrush`. Each is a brush property on `Chart`, set
    by resource reference, and a theme switch redraws the chart.
  - The marker ring is `ChartSurfaceBrush`, Zerg's own (`#2B2B2B` dark,
    `#FBFBFB` light): what a card on Mica comes out as, but solid. Zoomed
    grabs show it blending into the card in both themes.
  - The hover card is opaque (`SolidBackgroundFillColorQuarternaryBrush`), with
    round swatches. When it has more rows than its chart is tall, it runs them
    in two or three columns, so eighteen rows fit a 264 px floating chart. In
    `Compact` it uses 11 px type on 14 px rows.
  - A hairline is one device pixel (two from 200 %), on a pixel centre. At
    150 % that is crisp where a one-unit line is a blurred pixel and a half.
  - The group line's dashes have round ends, as the reference's do.
- **Theme.** `AppTheme.Apply(mode)` sets the Fluent theme and merges
  `Themes/Light.xaml` or `Themes/Dark.xaml`, which hold the 18 fallback colours
  (`Series1`…`Series18`) and `ChartSurfaceBrush`. `AppTheme.Series(slot)` reads
  one. Job colours belong in the same two files. A chart's inks restyle
  themselves, but series colours are the caller's: on `AppTheme.Changed`,
  build the models again. With the setting on System it follows the Windows
  setting `AppsUseLightTheme`.
- **Bars animate.** When `Rows` changes, each bar moves from its old width to
  its new one over 250 ms at 60 frames a second, matched by `Key`. The first
  rows appear without animation. Nothing animates when the chart isn't visible
  or Windows has animations off.
- **Cost** (Debug build, the 150 % monitor, the S2 file: 18 characters, 30
  minutes):

  | | CPU, % of one core |
  |---|---|
  | idle | 0.0 |
  | edge stepping at 4 Hz, two line charts (1,550×320 and 460×264), 18 lines each | 11.1 |
  | the same with a hover card held on both | 9.9 |
  | replay: every count and every chart rebuilt each tick, bars animating, two hover cards | 26.3 |

  That is about 5 % a chart, as S2 found. The replay figure is the gallery
  recounting 19,100 rows four times a second; how much of it is the bar
  animation was not separated. One 10-second reading with nothing switched on
  showed 48 %, and three readings straight after showed 0.0 %. My guess is the
  real mouse was sweeping an 18-line chart (S2 measured 50 % for that), but I
  did not confirm it. Watch for it in N3.
- **The gallery** (`Zerg.exe --charts <file>`) drew every chart from a file
  while there was no section to hold them. N3 deleted it.
- **`tools/drive.cs` grew**: `click` also toggles a check box, `set` gives a
  slider a value, `scroll` moves the first scrolling area, `zoom` enlarges
  part of a grab without smoothing.
- **Traps.**
  - UI Automation changes a check box or radio button without clicking it. A
    `Click` handler never runs; use `Checked`/`Unchecked` or a binding.
  - Straight after `front`, UI Automation sometimes sees only the frame of the
    window. Wait two seconds and ask again.
  - The layers of a chart are `DrawingVisual`s, hit only where they have ink.
    `Chart.OnRender` draws a transparent rectangle first so the whole chart
    sees the pointer.
  - A dependency property marked `AffectsRender` is enough to redraw a chart:
    WPF calls `OnRender`, which repaints the layers. Several changes in one
    tick make one redraw.

### N3 — the Damage section
Command bar, filter bar, tiles and the four Damage cards, on the N1 logic and
N2 charts. **Done when** the Damage rows pass against the reference on the
generated fixture and on a live session.

**Done 2026-10-03.**

#### What N3 built, and what it taught (handoff to N4)

- **The rules `app.js` kept in the page are in `Zerg.Core` now**, with unit
  tests (`tests/Zerg.Core.Tests/TrackerTests.cs`, 25 tests):

  | `app.js` | C# |
  |---|---|
  | `assignSlots`, `slotOf`, `assignJobVariants`, `assignAliases` | `Cast` (`Assign`, `SlotOf`, `VariantOf`, `AliasOf`, `Rewind`) |
  | `FFXITheme.step` | `Shades.Step` |
  | `sessionView` | `SessionView.Of` (labels, tips and looks as data) |
  | `emptyText`, the notes under the total and the clock, the status line's session part | `SessionText` |
  | `startSession`, `togglePause`, `cancelSession`, `secondary`, `latchStart`, `adoptSource`, the counting half of `render` | `Tracker` (`Follow`, `Feed`, `Start`, `Second`, `Cancel`, `TogglePause`, `Count`) |
  | the row thinning in `renderLineTable` | `Charts/Sampling.Rows` |

  `Tracker.Count(excluded, skillchains, now)` is the whole pipeline (slots,
  latch, filter, exclusions, aggregate) and returns a `Snapshot`: the counted
  rows, the totals, the characters to list chips for, the clock, and whether
  the session latched during it. It counts the party a second time only when
  an exclusion actually dropped rows.
- **The app side** (`src/Zerg`):
  - `MainViewModel` (two files) holds a `Tracker` and everything the window
    shows. `Recount()` runs on a poll that brought lines and on any filter or
    session change; `Tick()` runs 4×/s and rewrites only the clock, every DPS,
    and the chart's live edge. An idle poll only refreshes the status line.
  - `Rows.cs`: the lists (chips, legend, per-character table, actions) are
    `ObservableCollection`s of row objects that are **kept and updated in
    place** (`Rows.Sync`, matched by a key that is the real name), so a new
    total rewrites one cell and nothing is rebuilt under the pointer.
  - `Views/`: one `UserControl` per card (`LineCard`, `BarsCard`,
    `ActionsCard`, `DrillCard`), each bound to the same view model and holding
    no state. **N4 can put a second instance of a card in a panel window with
    the same `DataContext`.** `Cells` is the table-row panel (columns from a
    string like `1.6*,84,*`; a collapsed cell drops its column), `WheelChain`
    lets the wheel through a list that is at its end, `ActionRowTemplates`
    picks heading or action rows.
  - `Themes/*.xaml` hold the eighteen job colours (`JobWAR` …), read through
    `AppTheme.Job`. `MainViewModel.ColorOf` is the one place a character's
    colour is decided; `NameOf` the one place a name is drawn.
  - `Settings` gained `excluded`, `skillchains`, `hideNames`,
    `groupSmallLines`, `charactersOpen`. **No `section` yet**: there is one
    section, so the switch and its setting arrive with Healing in N5.
  - The chart gallery and the `--charts*` options are gone.
- **Checked against the reference** on the generated fixture, played into a
  live file (`tools/replay.py`, with `--extras`): Start, 812 lines appended,
  Pause; then the reference page was given the same file and the exact
  session times from Zerg's log. Every tile, chip, legend entry, data-table
  row, per-character cell, action row and drill-down figure agreed, in seven
  states: plain, Group under 5% off, data table open, a drill-down open,
  a character excluded, names hidden, skillchains off (62 to 97 rows each).
  Also by hand: Resume, Restart (re-arms, rows dropped, lines kept), Cancel,
  All / None, folding the character list, a newer file appearing (back to
  idle) and going away, light and dark, a 600-wide window, settings saved.
- **Checked afterwards, the same day** (the three things first left out):
  - **The hover cards and tooltips by the real mouse** (`drive.cs hover`): the
    line chart's card, a bar's, a histogram bin's, and the column-heading
    tooltips, in both themes. All correct but one: a tooltip on a column of
    figures came out right-aligned (the first trap below). Fixed in `App.xaml`.
  - **A session whose rows run past an hour**: the fixture stretched three
    times over (72 minutes of rows, written at once with their future times).
    The line chart's axis reads `1:00:00` and `1:10:00`, its hover card
    `1:05:27`, and the per-hit list `1:11:45`.
    **Still not seen: the clock itself past an hour** (the Elapsed tile and
    the DPS under it at `1:00:00` and after). The window left running for it
    was closed 31 minutes in. `Format` is unit-tested for the spelling; what
    is unseen is the live tile.
  - **The mutation check**: 74 of 74, on the code as N3 left it.
- **Cost.** A running session with nothing arriving: 4.1 % of one core (Debug,
  150 % monitor, 8 characters, one docked chart stepping its edge). N2's
  unexplained 48 % reading did not come back.
- **How it looks** (confirmed by the user on 2026-10-03, Decision 9):
  - One bar across the top: Start and Pause/Cancel, then Include Skillchains
    and Hide names as toggles, the theme on the right. Characters on their own
    line under it. Five tiles (they stack in threes, then twos, as the window
    narrows), then the four cards, then a status line at the bottom.
  - **Start is green** while it is the thing to press, amber while armed, and
    plain once the clock runs; Resume is amber. The accent colour was the
    first choice for Start, but the pressed toggles beside it are accent too
    and it did not stand out. The clock and the status dot use the same pair:
    green counting, amber held, grey idle; the dot's ring pulses while armed.
  - The total is the one large accent-coloured figure. The data table and the
    per-hit list sit in Fluent expanders. The selected action row has an
    accent mark at its left.
- **`tools/drive.cs` grew**: `text` prints everything on screen with a name,
  table cells included (`names` leaves those out); `click` presses rows inside
  lists and folds expanders; `scroll` picks the first area that scrolls up
  and down. `front` no longer taps Alt at a window that is already in front.
  `hover` moves the real mouse pointer to a point in a grab's own pixels: the
  only way to see a tooltip, or a hover card as the mouse brings it up.
- **Traps.**
  - **A tooltip takes its text alignment from the element it is on.** On a
    right-aligned column heading it wraps ragged-left. `App.xaml` has one
    `ToolTip` style that sets it back to left. That style is based on
    `DefaultToolTipStyle`, the Fluent theme's own key: based on
    `{x:Type ToolTip}` it loses the Fluent template and draws the old
    kind, one unwrapped line in a square box, with no error.
  - **The Alt tap in `front` broke UI Automation.** Sent to a Zerg window that
    was already foreground, it opened the system menu, and until that closed
    UI Automation saw only the window frame. This was N2's "wait two seconds".
  - UI Automation's Toggle flips a toggle without clicking it, so a
    `Command` on a `ToggleButton` never runs. The chips bind `IsChecked` both
    ways and act on the change (`ChipRow.Flipped`).
  - Pressing a row through UI Automation scrolls its list to it; a real click
    on a visible row does not. A grab taken after scripted presses can show a
    list scrolled.
  - An `ItemsControl` names each row for a screen reader by the item's
    `ToString()`. Rows return the name **as drawn**; the key is a real name
    even while names are hidden.
  - `AutomationProperties.Name` on a `TextBlock` replaces its text for UI
    Automation, so a value given a name can't be read by a script.
  - Collapsed elements stay in UI Automation's raw tree. A hidden card's last
    contents show up in `text`.
  - Fluent scroll bars are drawn over the content. Lists that scroll keep a
    14 px right margin, and sideways-scrolling tables a 12 px bottom one.
  - The reference page does not reset when its fixture is replaced by a file
    of similar length: it reads the tail as new rows on top of the old ones.
    Navigate to the page again before comparing.

### N4 — floating panels
Panel windows per Decision 6 for every Damage card: `WS_EX_NOACTIVATE`,
`WS_EX_TOOLWINDOW`, rounded corners, the panel bar, placement per panel.
**Done when** the floating-panel rows pass over the game.

#### Handoff to N4 (written at the end of N3, 2026-10-03)

**Where things stand.**
- `Zerg.exe` is the live Damage section: it follows the event file, measures
  a session, and draws the tiles and four cards. Healing, Compare, import /
  export and floating panels are not in it yet. Nothing is committed; the
  user handles git.
- The suite is 192 tests, 118 without the mutation check. All 192 pass (the
  mutation check was run after N3, on 2026-10-03).
- **Waiting on the user:** the look in "How it looks" above was my choice and
  has not been confirmed, the green Start button in particular. Ask before
  building more on it.
- **Left open on purpose:** the Damage | Healing | Compare switch and its
  `section` setting (N5, when there is a second section), and the line
  chart's end labels, which only the floating card uses (this phase).
- **Not checked in N3:** the live clock past an hour, and a session against
  the real addon in game. (The hover cards by real mouse and rows past an
  hour were checked afterwards: see "Checked afterwards" under N3.)
- The user's desktop shortcut still runs the old web snapshot in `dist/Zerg/`.
  That is intended until N9.

**Running it.**
- With the game: `dotnet run --project apps/zerg/src/Zerg`. It follows
  `%LOCALAPPDATA%\VibeXI\events`. Press Start; the clock begins on the first
  counted hit.
- Without the game: start it on a scratch folder holding an empty `.jsonl`,
  press Start, and play the fixture in with `tools/replay.py`. The steps, and
  how to compare the result with the reference page, are in `CLAUDE.md` under
  "Verifying a change", item 5.
- It reads and writes the real `settings.json`. Copy it aside before a check
  that toggles filters or moves the window, and put it back.

**Read first:**
- the checklist's Floating panels rows, Decision 6, and the "Windows and the
  shell" table above;
- `../damage-meter/CLAUDE.md`: "Floating, it is a strip", "Floating, the
  cumulative graph labels its lines", "The controls are in every pop-out
  window too", and what "Seeing through a pop-out window" says about the
  opacity setting (a default that resets every panel's own value);
- `../damage-meter/web/lib/popout.js` for the bar and `popSize` in `app.js`
  for the opening sizes (line 460×304; bars 460 × (64 + 21 per row, at least
  three rows));
- `poc/wpf` for the per-pixel alpha window that was proved.

**What exists already.**

| Need | Where |
|---|---|
| The cards, as controls that can be instantiated twice | `Views/LineCard`, `BarsCard`, `ActionsCard`, `DrillCard` |
| The floating look of the line chart | `LineChart.Compact` and `EndLabels` (N2), not used docked |
| Start / Pause as data, for a panel's own pair | `MainViewModel.View` (`SessionView`), `StartCommand`, `SecondCommand` |
| The clock and total for the panel bar | `ClockText`, `Light`, `TotalText` (already ticking) |
| Placement | `Native/WindowPlacement`, `Settings.Windows` by name |

**What is new in N4.** The panel window itself; a Pop out button on each card
and what the card's slot shows while it is out; the bars card's compact strip
(one row per character, the bar behind the name, Damage / % / Acc, nothing in
it ticking); the opacity default in the main window and each panel's slider;
`Settings` for the opacity values, with each panel's placement under
`windows` by name.

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer. Everything new follows the naming rule. When N4
is done, tick its checklist rows, mark it done here, and write a handoff for N5.

**Done 2026-10-03, except for one check: over the game.** The game was not
running when N4 was built. Everything else on the floating-panel rows was
checked in the running app (below). The first time the game is up, pop a panel
out over it and confirm the three things only the game can show: the panel
stays on top of it, the game shows through the tint, and the game keeps the
keyboard while the panel is clicked and dragged.

#### What N4 built, and what it taught (handoff to N5)

- **A panel is a second copy of a card, not the card moved.** The four cards
  are controls bound to one view model, so the panel makes its own instance
  (`new LineCard()` and so on) with the same `DataContext`. Both show the same
  figures at the same moment and nothing is pushed between windows. While a
  panel is out the main window collapses its own copy, which stops drawing, and
  shows a stand-in in its place.
- **One flag says "floating": `Views/Float.On`**, an inherited attached
  property that the panel sets on what holds its card. Every difference between
  a docked card and a floating one is a style trigger on it. The view model
  does not know which window a card is in.

  | Where | What `Float.On` does |
  |---|---|
  | `Card`, `CardTitle`, `CardNote`, `PopOut` (styles in `App.xaml`) | the card gives up its frame, padding, heading, note and Pop out button |
  | `LineCard` | legend and data table gone; the chart is `Compact` with `EndLabels`, has no fixed height, and fills the panel |
  | `BarsCard` | the bars chart and the 14-column table gone; the strip shown |
  | `ActionsCard` | the list's 340 cap lifted, so it is as tall as the panel |
  | `DrillCard` | nothing but colours; its note stays, because it is figures (who, hits, total), not an explanation |

  For one element, `Visibility="{Binding Path=(v:Float.On), RelativeSource={RelativeSource Self}, Converter={StaticResource Hidden}}"`
  is "docked only" (`Visible` for "floating only").
- **The pieces.**

  | File | What it is |
  |---|---|
  | `Zerg.Core/PanelOpacities.cs` | the opacity rules: 15 to 100, a default of 85, a panel's own value, and setting the default forgetting every own value. 19 unit tests |
  | `src/Zerg/Panels.cs` | `PanelSet` (on the view model as `Panels`): the cards that can float, the open windows, the opacity values and their saving. `PanelInfo`: one card's `IsOut`, `Opacity`, `PopOutCommand`, `DockCommand`. XAML reaches one as `Panels[line]` |
  | `src/Zerg/PanelWindow.xaml(.cs)` | the window: the tint, the bar, the card, opening sizes, placement, move and resize |
  | `src/Zerg/Native/Overlay.cs` | the window styles, and `Overlay.Drag`, a move or resize followed by hand |
  | `src/Zerg/Native/WindowPlacement.cs` | gained `Frame`, `CaptureFrame`, `ApplyFrame`, `MoveFrame`, `IsUsableFrame`: a panel's frame in screen pixels |
  | `src/Zerg/Views/Away.xaml` | the stand-in for a card that is out: its title and Bring back |
  | `src/Zerg/Views/Grow.cs` | `Grow.Share`: a bar's length as a share of its room, eased over 250 ms |

- **The window** (Decision 6, as proved in `poc/wpf`): `WindowStyle=None`,
  `AllowsTransparency`, `Background=Transparent`, `Topmost`,
  `ShowInTaskbar=False`, `ShowActivated=False`, and `WS_EX_TOOLWINDOW |
  WS_EX_NOACTIVATE` added in `SourceInitialized`. A rounded border is the
  backdrop: black at the slider's alpha. Everything drawn on it is solid.
  Not owned by the main window, so minimizing Zerg leaves the panels up.
- **A panel is dark whatever the theme**, because a black tint needs light
  ink: the window sets `ThemeMode="Dark"` for itself (it works on a
  transparent window, which stays see-through). Data colours follow:
  `AppTheme.Job` and `Series` take which set to read, and the view model has
  a panel twin of each coloured thing (`PanelLine`, `PanelHistogramFill`,
  `ActionRow.PanelSwatch`, and the strip's own fills) made with
  `PanelColorOf`. Under the dark theme the twins are the same objects. The
  marker ring (`ChartSurfaceBrush`) is overridden in the window's resources.
- **The bar**, left to right: the title (the drill-down's is the action it
  is open on), Start and Pause, the clock, the total, the opacity slider and
  its value, Dock. The session group never fades; the title and the tools
  rest at 45 % until the pointer is over the panel. The buttons use the same
  styles as the main window's (`StartButton`, `SecondButton`, `Clock`, moved
  into `App.xaml`), bound to the same commands.
- **The strip** (`MainViewModel.Strip`, drawn by `BarsCard`): one 20-unit row
  per character with damage, nobody at zero, the bar behind the name at 0.7
  opacity, Damage, % and Acc%, the owner marked at the left, a dark halo
  under the text. Filled by `DrawBars` from the same totals as the table, so
  nothing in it ticks.
- **Opening sizes** (`PanelWindow.OpeningSize`, in the window's own units):
  line 500×304, bars 500 wide and as tall as its rows (three at least),
  actions 660×420, drill-down 600×520. The reference opened at 460 wide; the
  bar's fixed parts need more here. The first time, a panel opens near the
  main window's top-left corner; after that, where it was left.
- **Settings**: `panelOpacity`, `panelOpacities` (key to value, only panels
  whose own slider was moved), and each panel's frame under `windows` as
  `panel:<key>`. A slider drag is saved once it settles (400 ms), not per
  step. Which panels were open is **not** remembered: Zerg starts with every
  card docked, as the reference did.
- **Closing docks.** Dock, Bring back, and closing the main window all go
  through the window's `Closed`, which saves the frame and puts the card back.
- **Checked in the running app** (fixture played into a live file, one
  2560×1600 display at 150 %):
  - Pop out all four; several at once; each is topmost, a tool window,
    no-activate and layered (`drive.cs windows`).
  - With another application in front: a real click on the panel's Pause
    paused the session, a real drag by the bar moved the panel, and the
    foreground window never changed (`drive.cs fg` before and after).
  - Resize by a corner and by an edge; stops at the minimum width.
  - The slider: backdrop from 85 % to 22 % with solid text, saved as that
    panel's own. The main window's slider at 60: all four panels at 60 %,
    own values gone.
  - The strip's Damage, % and Acc% equal the docked table's, row for row;
    Hide names carries over (owner kept).
  - The drill-down panel: title follows the action; Close from the panel
    leaves "Nothing to show yet"; picking an action from the floating
    actions list fills it.
  - Light theme: panels stay dark with the dark colour set; hover card and
    crosshair by the real mouse inside a panel.
  - Placement: reopened where left, size included; a frame at −32000 falls
    back to the opening place; a panel with no saved frame sizes to its rows
    with no scroll bar.
- **Cost** (Debug, 150 %, 8 characters, session running, nothing arriving):
  3.1 % of one core with everything docked, 6.3 % with the line, strip and
  actions panels floating.
- **Not checked**: over the game (above); a second monitor with another
  scale (there was one display); the resize cursors (a grab does not show
  the pointer); a full alliance in the strip.
- **How it looks** (confirmed by the user on 2026-10-03, Decision 9):
  - Dock is a small ✕ at the right of the bar, not the word.
  - The opacity value is printed beside the slider.
  - Start, Pause and the clock take the main window's colours (green,
    amber).
  - The stand-in is an outlined box with the card's title, "Showing in its
    own panel." and Bring back.
  - "Panel opacity" sits in the command bar after Hide names.
  - Panels open wider than the reference's (500, not 460) and the actions
    and drill-down panels at sizes of their own, not the docked card's.
- **`tools/drive.cs` grew**: `windows <pid>` lists a process's windows as
  `h<hwnd>` with title, frame and styles (a panel is not the main window, so
  this is how to get its target); `press` and `drag` use the real left
  button; `fg` prints the foreground window. `snap` now asks for layered
  windows (`CAPTUREBLT`).
- **Traps.**
  - **`DragMove`, and answering a hit test as the caption or an edge, both
    activate the window.** The system's drag loop made the panel the
    foreground window in spite of `WS_EX_NOACTIVATE`. Hence `Overlay.Drag`.
    It needs the mouse capture: with a button down, a captured window keeps
    getting the pointer even over another application's windows.
  - **If the capture fails, drop the drag**, or the panel follows the
    pointer for good: nothing else says the button came up.
  - **A row is rounded up to whole pixels.** At 150 % a 20-unit row with a
    1-unit gap is 32 pixels, not 31.5, so seven rows overflowed a panel
    sized for 21 each and it opened with a scroll bar. The opening height
    allows half a unit a row.
  - **The Fluent scroll bar is drawn over the content** (N3's trap again):
    a scrolling panel gives its card a 14-unit right margin only while the
    bar is there.
  - **The Fluent slider has a minimum width**; `MinWidth="0"` lets it be 64.
  - **Panels are topmost, so they cover each other.** A scripted press at a
    point lands on whichever panel is on top there. Two of my "failed" drags
    were presses on the other panel. Move them apart before testing one.
  - **Running a `.ps1` file is blocked on this machine** (execution policy).
    Run PowerShell inline, or pass `-ExecutionPolicy Bypass` for that one
    process as `measure-tint.ps1` is run. Don't change the policy itself.
  - A background `Zerg.exe` started inside a shell pipeline keeps the
    pipeline open: redirect its output (`>/dev/null 2>&1 &`).

### N5 — the Healing section
The Damage section's layout over the heals, the Damage | Healing switch, and a
panel for each heal card. **Done when** its checklist rows pass.

**Done 2026-10-03.**

#### Handoff to N5 (written at the end of N4, 2026-10-03)

**Where things stand.**
- `Zerg.exe` is the live Damage section with floating panels. Healing,
  Compare and import / export are not in it yet. Nothing is committed; the
  user handles git.
- The suite is 211 tests, 137 without the mutation check. All 211 pass (the
  mutation check, 74 of 74, was run on the code as N4 left it).
- **Waiting on the user:** the look, N3's and N4's ("How it looks" under
  each). Neither has been confirmed. Ask before building more on it.
- **Owed from N4:** the look over the game (see N4). Do it, or ask the user
  to, before trusting panels there.
- **Owed from N3:** the live clock past an hour (leave a session running 61
  minutes and read the Elapsed tile), and a session against the real addon
  in game.
- The user's desktop shortcut still runs the old web snapshot in
  `dist/Zerg/`. That is intended until N9.

**What N5 is.** The Damage section's layout over heal lines: five tiles
(Total, Elapsed, HPS, Top, Biggest heal), cumulative healing, healing by
character, the heals table, the heal drill-down; the Damage | Healing switch
(Compare joins it in N6) with its `section` setting; and a panel for each
heal card. Its checklist rows are "Healing section", the section switch
under "Filters and display", and the heal-twins row under "Floating panels".

**Read first:**
- `../damage-meter/CLAUDE.md`, "The Healing section": the chips follow the
  section on screen (healers in Healing, damage dealers in Damage, one
  exclusion list); healing leaves pet heals out of the hero tile, the line
  and the bars, with Pet Healing a column of its own; HPS ticks with DPS; **a
  heal does not start the clock**; the skillchain switch is not offered in
  this section.
- `renderHealView` and what it calls in `../damage-meter/web/app.js`
  (`renderHealTiles`, `renderHealLine`, `renderHealBars`, `renderHealMeter`,
  `renderHealActions`, `renderHealDrill`, `healEvents`), and the `#healView`
  markup in `index.html`.

**What exists already.**

| Need | Where |
|---|---|
| Heal rows, kept apart from damage | `Tracker.Reader.Heals` (`HealEvent`) |
| Scoping to the session, the party and the exclusions | `Healing.Filter` (takes the same `FilterOptions` as `Counting.Filter`) |
| Per character and per action | `Healing.Totals` (`HealTotals`, `HealerTotals`, `HealAction`), proved against the JS in N1 |
| The cumulative line and the histogram | `Counting.Cumulative` and `Counting.Distribution` on event-shaped copies of the heals (the reference's `healEvents`: `dmg` is HP healed, `hit` always true) |
| Charts | `LineChart`, `BarChart`, `HistogramChart`, unchanged |
| Rows that last across counts | `Rows.Sync` and the row classes in `Rows.cs` |
| A fixture with heals | the generated fixture has 79 heal lines |

**What is new in N5.**
- `Tracker.Count` returns damage only. The heals need the same treatment
  (filter with the session, roster and exclusions, then totals) in the same
  count, so both sections are measured over one window: extend `Snapshot`,
  with unit tests beside `TrackerTests`.
- The section switch and `Settings.Section`. Only the section on screen needs
  drawing, but a floating heal panel must keep updating while Damage is on
  screen, and the other way round.
- The heal cards. The four Damage cards bind to Damage-named properties
  (`Line`, `Bars`, `Actors`, `Actions`, `Drill…`). Decide first whether the
  heal cards are the same controls given a different data context (a small
  per-section view model exposing the same names) or four more controls. The
  first is less markup and makes panels free; it means moving the Damage
  properties off `MainViewModel` into a section object.
- Heal panels: add the four keys in `PanelSet` (the reference's are `hline`,
  `hbars`, `hactions`, `hdrill`), a case each in `PanelWindow`'s card switch
  and `OpeningSize`, and the Pop out button and `Away` stand-in per card.
  Everything else (bar, tint, opacity, placement, move and resize) is shared.
  The panel bar's total is damage (`TotalText`) today; the reference showed
  the damage total on every panel, heal ones included.

**Running it.** Unchanged from N4's handoff above: `dotnet run --project
apps/zerg/src/Zerg`, or a scratch folder and `tools/replay.py` without the
game. To check a panel, `drive.cs windows <pid>` gives its `h<hwnd>`.

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer. Everything new follows the naming rule. When N5
is done, tick its checklist rows, mark it done here, and write a handoff for
N6.

#### What N5 built, and what it taught (handoff to N6)

- **The heal cards are four more controls, not the damage cards given other
  data.** The handoff asked for that choice first. The damage cards stayed as
  they were, bound to the same names, because they had been checked against
  the reference cell by cell and nothing in N5 needed them to move. Two of
  the heal cards have different tables anyway (eight columns for fourteen,
  Casts for Hits and Miss, a pet's total in a heading), and the other two
  differ in every word. What it costs: the line card and the drill-down card
  exist twice as nearly the same markup, so a change to how either is laid
  out is made in two files (`LineCard` and `HealLineCard`, `DrillCard` and
  `HealDrillCard`).
- **One count measures both sections** (`Zerg.Core`):
  - `Tracker.Count` now filters the heals with the same session, roster and
    exclusions as the damage and totals them, in the same call. `Snapshot`
    gained `Heals` (the heals that count, on the session clock, a pet's
    credited to its owner), `Healing` (`HealTotals`), `Healers` (who a chip
    is shown for in the Healing section, excluded or not), `BestHeal` and
    `TopHealer`.
  - `Healing.AsEvents` turns heals into rows `Counting.Cumulative` and
    `Counting.Distribution` read unchanged: the amount is the HP healed and
    every one is a hit.
  - Seven unit tests beside the others in `TrackerTests.cs`, on lines made
    by `Lines.Heal`.
- **The app side** (`src/Zerg`):

  | File | What it is |
  |---|---|
  | `MainViewModel.Healing.cs` | everything the Healing section shows, as `Heal…`-named properties beside the damage ones: tiles, `HealLine` / `PanelHealLine`, `HealLegend`, the data table, `HealBars`, `Healers`, `HealStrip`, `Heals`, the heal drill-down and its two commands |
  | `MainViewModel.cs` | `Section` (and `IsDamage`, `IsHealing`), `DrawChips` listing whoever the section is about, `Tick` rewriting HPS with DPS |
  | `Rows.cs` | `HealerRow`, `HealStripRow`, `HealRow`; `IGroupedRow`, so one template selector serves the actions table and the heals table |
  | `Views/HealLineCard`, `HealBarsCard`, `HealActionsCard`, `HealDrillCard` | the four cards, each with a docked and a floating form on `Float.On`, as their damage twins |
  | `MainWindow.xaml` | the Damage \| Healing switch; the body is two stacks, one per section, each with its own five tiles |
  | `Panels.cs`, `PanelWindow` | four more keys (`hline`, `hbars`, `hactions`, `hdrill`), a card and an opening size each |
  | `Settings.cs` | `section` |

- **Both sections are drawn on every count**, whichever is on screen, as the
  reference did. That is what keeps a floating heal panel right while Damage
  is on screen, and the other way round, with nothing to track about which
  panels are out. A collapsed card does not paint, so the cost is the
  counting and the row updates, not the drawing.
- **The rules, as the reference has them:**
  - The chips list whoever the section is about: who dealt damage, or who
    healed. One exclusion list serves both, by name.
  - Healing leaves a pet's heals out of the hero tile, the line and the
    bars. They are the owner's Pet Healing, and in the heals table under the
    pet's name, where the Share column is out of healing and pet healing
    together.
  - Someone whose pet did all their healing is listed (a row, a chip, "n
    healers") with 0 healing; nobody is top healer until someone has healed
    more than 0.
  - A heal does not start the clock. A cure before the first counted hit
    falls before the zero.
  - Biggest heal is what one target got from one action, a pet's included.
    The drill-down is per cast, summed over its targets, and has Casts where
    the damage one has Accuracy.
  - The cumulative healing chart has no "Group under 5%".
  - Include Skillchains is not offered in the Healing section.
  - Start, Cancel and a new event file close both drill-downs.
  - Every heal panel's bar shows the damage total, as the reference's did.
    (As N5 built it. Decision 10 changes it to total healing.)
- **One deliberate difference.** None, pressed in the Healing section, also
  closes the heal drill-down. The reference closed only the damage one and
  left the heal one open on "No casts in range".
- **Checked against the reference** on the generated fixture played into a
  live file with `--extras` (Start, 815 lines, Pause; the reference page given
  the same file and the session times from Zerg's log). Every figure agreed
  in two states: plain with a drill-down open on a Curaga (tiles, chips,
  legend, the 16 data-table rows, the per-character table, the heals table
  with its pet headings, the drill-down's tiles, caption and 11 casts), and
  with a healer excluded, names hidden and the drill-down on a pet's heal
  (the Job column gone, "Unknown job" for a character with no job on record).
- **Checked in the running app** (one 2560×1600 display at 150 %):
  - The switch: chips change with the section (8 damage dealers, 3 healers);
    Include Skillchains gone in Healing; the section saved and restored.
  - Live: after Resume, two heals appended to the file arrived in the tiles,
    the table, the chips ("4 healers") and a floating heals panel; HPS, the
    party's and each row's, fell between polls.
  - Panels: all four heal cards out at once, each topmost, a tool window,
    no-activate and layered; dragged apart by the real mouse with the
    foreground window unchanged; the heal strip following an exclusion made
    from the Damage section; the heal drill-down panel's title following the
    heal and saying why it is empty after Close; dark under the light theme;
    placement saved under `panel:hline` and the rest, and a panel reopened
    where it was left after a restart.
  - A 588-unit-wide window: the heal tiles in twos, the command bar wrapped.
  - Before Start: every heal card and the strip panel say "Press Start to
    begin measuring".
- **Tests.** 218 in all, 144 without the mutation check; all pass
  (the mutation check caught 74 of 74, on the code as N5 left it).
- **Cost** (Debug, 150 %, session running, nothing arriving, 8 damage
  dealers and 3 healers; % of one core over 10 seconds, read while the test
  suite was running beside it): 3.1 with the Damage section on screen and
  everything docked (as N4 measured), 4.5 with the Healing section, 4.7 with
  the heal line, strip and heals panels floating over the Healing section,
  6.1 with those three floating and the Damage section on screen.
- **Not checked:** the heal panels over the game (it was not running; see
  N4); a session with real heals from the addon, a pet's among them; the
  heal line's hover card by the real mouse (the bars' one was seen); a
  second monitor.
- **How it looks** (confirmed by the user on 2026-10-03, Decision 9):
  - The Damage | Healing switch is the first thing on the command bar, as
    two segment buttons like the theme's. Start and Pause now wrap as one
    piece.
  - The Healing section is the Damage section's layout exactly: the same
    five tiles, the total in the accent colour, the same four cards.
  - A heading in the heals table says the pet's part after the total, in
    the quiet ink: "3,354  + 344 pet".
  - The Min, Max and Share headings of the heals table have tooltips the
    reference did not.
  - A heal panel's bar shows the damage total (the reference's behaviour).
    **Not confirmed: the user chose total healing instead** (Decision 10).
- **`tools/replay.py --extras`** also writes three pet heals: two from the pet
  of a character who heals too, one from the pet of a character who does not.
  The fixture has none.
- **Traps.**
  - **After a theme switch, `drive.cs text` reads old values from list
    rows.** (Fixed at the start of N6: see "What N6 built". Kept here for
    what it looked like.) Rows that existed when the theme changed go on reading, through
    UI Automation, as they were at that moment: a DPS cell stood at 690.8
    while the screen and the party's tile went on falling. Rows made later
    read true, and so does everything outside a list. The screen is right
    throughout. It is not new in N5 (the damage table does it too), and it
    means a screen reader is told stale figures after a theme switch, which
    is a defect to fix, not only a testing trap. When comparing with the
    reference, do not switch theme first, or restart Zerg after doing so;
    settle any doubt with `snap`.
  - **A line appended by hand in the second Resume was pressed can fall in
    the pause.** The file's times are whole seconds; a row stamped with the
    second that began before the press is before the resume, and is dropped
    as it should be. Wait a second after Resume before writing rows stamped
    "now". (Start is different on purpose: arming counts from the top of its
    second.)
  - `ItemsControl` rows of two kinds are picked by `IGroupedRow`, not by the
    row's class: a third grouped table only has to implement it.
  - `Process.CloseMainWindow` sent from a PowerShell one-liner inside Git
    Bash did nothing once; the same call from PowerShell itself closed the
    app. Check the log for `exit` before rebuilding.

### N6 — the Compare section
Two exported parses side by side, A the baseline: slots, tiles with their
changes, the cumulative chart, and the tables, for damage and for healing.
**Done when** its checklist rows pass.

**Done 2026-10-03.**

#### Handoff to N6 (written at the end of N5, 2026-10-03)

**Where things stand.**
- `Zerg.exe` is the live Damage and Healing sections, with a floating panel
  for each of their eight cards. Compare and import / export are not in it
  yet. Nothing is committed; the user handles git.
- The suite is 218 tests, 144 without the mutation check. All pass
  (the mutation check, 74 of 74, was run on the code as N5 left it).
- **Nothing is waiting on the user.** The open questions were settled on
  2026-10-03 (Decisions 9 to 14): the look of N2 to N5 is confirmed, and the
  checks that need the game moved to N9, step 1.
- The user's desktop shortcut still runs the old web snapshot in
  `dist/Zerg/`. That is intended until N9.

**Two things to do before N6's own work** (the user's choice, 2026-10-03):
1. **Fix the stale rows after a theme switch** (the first trap under N5):
   list rows that existed at the switch go on reading, through UI Automation,
   as they were then. A screen reader is told old figures. Fix it while two
   sections have lists; Compare adds five more tables that would inherit it.
   Check with `drive.cs text` after a switch, on a running session: a DPS
   cell must keep falling.
2. **A heal panel's bar shows total healing** (Decision 10). The bar's total
   is `TotalText` for every panel today; the four heal panels (`hline`,
   `hbars`, `hactions`, `hdrill`) take the healing total instead.

**What N6 is.** The third section: two exported parses side by side, A the
baseline, every difference B − A and every percentage that over A. Its
checklist rows are the two under "Compare section" and the rest of the
section-switch row under "Filters and display". The reference has more on the
page than those rows name, and all of it is kept: **Swap A ↔ B**, dropping a
file onto a slot, a slot's own error note, the Include Skillchains toggle
repeated on the Compare card, and Hide names hiding every name (a comparison
has no owner). Compare cards have no panels.

**Read first:**
- `../damage-meter/CLAUDE.md`, "Compare": a run is an import measured by the
  same pipeline; by Job re-actors before the count and never borrows a job
  from the other run; the shorter run's line ends, it is not carried flat;
  run colours mean only "which run"; delta colours and Length being neutral;
  no owner; Use current is a snapshot; what is persisted.
- `../damage-meter/web/compare.js` (754 lines, all drawing): `render`,
  `renderSlot`, `renderTiles` / `renderHealTiles`, `renderPace`, `renderRows`,
  `actionTable`, `renderHealing`, `healActionTable`, `renderKinds`,
  `renderTotals`, `renderTargets`, `deltaCell`, `identities`, `useCurrent`.
  And `#compareView` in `index.html`.

**What exists already.**

| Need | Where |
|---|---|
| Reading an exported parse, with its errors as codes | `ParseFile.Import` (`ImportedParse`, `ParseImportException`) |
| Use current: the session on screen as a parse | `ParseFile.Export` and `Stringify`, then `Import` (the round trip is the point: it measures as the file would) |
| A run's figures, by character or by job, skillchains on or off | `Compare.Measure` (`Measurement`) |
| Rows paired across the two runs, kinds, targets, heal spells and targets | `Compare.Diff` (`CompareRows`), `Compare.Actions`, `Compare.HealActions`, `Compare.KindLabels` |
| B − A and its percentage of A | `Compare.Delta` |
| The two cumulative lines on one grid, the shorter one ending | `Compare.Pace`; `LineChart` lifts the pen on a value that is not finite (N2, drawn in the gallery then, never yet in a section) |
| All of it proved against the JS | N1's parity suite, pace charts in N2's |
| A third section | `MainViewModel.Section` is a string; `IsDamage` is "not Healing" today and must become "is Damage". The body in `MainWindow.xaml` is a `Grid` of one stack per section |
| Skillchains and Hide names | `MainViewModel.Skillchains`, `HideNames` (one setting each, shared with the live sections) |
| Parses to load | `../damage-meter/tools/exports/Paradox_Kirin_4.json` (18 characters); more are made by exporting from the reference page, or by `ParseFile.Export` on a played fixture |

**What is new in N6.**
- The Open dialog (`Microsoft.Win32.OpenFileDialog`): `*.zerg` first, older
  `.json` exports accepted, told apart by content (Decision 7a). N7's Import
  uses the same dialog, so write it once where both can reach it. Dropping a
  file on a slot is WPF's `AllowDrop` and `Drop`.
- A view model for the section. It shares nothing with the live sections but
  the two settings above and the theme, so give it its own class (exposed
  from `MainViewModel`, as `Panels` is) and do not grow `MainViewModel`
  further. It needs the tracker only for Use current.
- The views: the runs card with its two slots and its switches (Damage |
  Healing, Character | Job, Include Skillchains, Swap), the tiles with
  deltas, the pace chart, and the tables. The delta cell (value, B − A, the
  percentage, good or bad colour with the sign always in the text) is used
  in every table: make it one small control.
- The bar above the section, per Decision 11: the chips are hidden; the
  section switch, Start and Pause, Hide names and the theme stay; polling and
  the session go on underneath. Include Skillchains leaves the command bar
  here and is on the Compare card, bound to the same setting.
- `Settings`: Compare by and the Damage | Healing mode, both saved
  (Decision 12). The slots are not saved: a parse is a megabyte or two.
- The checklist's section-switch row is ticked when Compare is on the switch.

**Running it.** `dotnet run --project apps/zerg/src/Zerg` with the game, or a
scratch folder and `tools/replay.py` without it (`CLAUDE.md`, "Verifying a
change", item 5). The switch is `click <pid> "Healing section"` or
`"Damage section"`. To compare with the reference's Compare page, load the
same two files into it with `DPS.compareView.load('a', name, text)`;
`DPS.compareView.state.last` holds what it drew.

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer. Everything new follows the naming rule. When N6
is done, tick its checklist rows, mark it done here, and write a handoff for
N7.

#### What N6 built, and what it taught (handoff to N7)

**The two items done first.**

- **Rows read stale after a theme switch: fixed** (`AppTheme.RereadRows`).
  - What happened: a theme switch gives every list a new template, and the
    list makes its rows again. What UI Automation is told about a row is
    remembered per item, not per row drawn, so an item that was there before
    the switch went on answering with the text blocks of the row that had
    been thrown away, frozen at their last values.
  - The fix: once the windows have been laid out in the new theme
    (`ApplicationIdle`), every window that a reader has looked at has its
    remembered children dropped, top to bottom (`ResetChildrenCache`). A
    window nothing has asked about is left alone.
  - Checked on a running session: 13 figures differ between two `text` reads
    three seconds apart, before a switch, after a switch to light, and after
    the switch back. Before the fix only 4 moved after a switch (the tiles).
  - The cause is my reading of how WPF behaves, from the symptoms; I did not
    step through WPF's source. The fix is what was verified.
- **A heal panel's bar shows total healing** (Decision 10).
  `PanelSet.IsHealing(key)`; `PanelWindow` rebinds its bar's total to
  `HealTotalText` for those four. Checked with both kinds out at once: the
  Heals panel read 21,341 and the Actions panel 153,687, as the two sections'
  tiles did.

**The Compare section.**

- **Everything it prints is worked out in `Zerg.Core`, as text**
  (`CompareSheet.cs`). `CompareSheet.Of(a, b, byJob, skillchains, hideNames)`
  measures both runs (`Compare.Measure`, `Diff`, `Pace`, unchanged from N1)
  and returns the tiles, both cumulative lines, and every table's rows for
  damage and for healing, as records of strings and bar lengths. The app
  adds colours and which rows are open, and nothing else.

  | `compare.js` | C# |
  |---|---|
  | `deltaCell` | `Change.Of` (`Change`: `Main`, `Sub`, `Tone`) |
  | `ab` | `AB` |
  | `bars` | `BarPair` (lengths 0 to 1) |
  | `renderTiles`, `renderHealTiles` | `CompareSheet.DamageTiles`, `HealTiles` (`CompareTile`) |
  | `renderPace` | `DamagePace`, `HealPace`, `CompareSheet.Table` (`PaceLine`) |
  | `renderRows`, `actionTable` | `Actors` (`ActorLine`, `ActionLine`) |
  | `renderHealing`, `healActionTable` | `Party`, `Healers`, `HealNote` (`HealerLine`, `HealLine`) |
  | `renderKinds`, `renderTargets`, `renderTotals` | `Kinds`, `Targets`, `HealSpells`, `HealTargets` (`KindLine`, `TotalLine`) |
  | `identities`, `nameFor` | `Ids` (`Identity`), and the names drawn in `HealTargets` |
  | the slot's Started / Length / Party | `CompareSheet.Describe` (`RunSummary`) |
  | `useCurrent` | `CompareSheet.Snapshot` |

- **The app side** (`src/Zerg`):

  | File | What it is |
  |---|---|
  | `CompareViewModel.cs` | `CompareViewModel` (on the main view model as `Compare`): the two slots, the switches, and what is drawn. `RunSlot`: one slot, its buttons and its error. `CompareActorRow`, `CompareHealerRow`: a table row with its swatch and whether it is open |
  | `ParseDialog.cs` | the Open dialog (`*.zerg` and `*.json` together, then all files), `Read` for a dropped file, `Title` (a name without its extension). `ParseText` is a file's name and text |
  | `Views/CompareSection.xaml(.cs)` | the whole section: the runs card and its slot template, the tiles, the chart card, the damage cards and the healing cards. The code-behind sets how many tiles sit in a row and takes a dropped file |
  | `Views/RunPair` | a figure for each run in one cell, A over B, each behind a tick in its run's colour |
  | `Views/ChangeText` | a change: the percentage (or points, or time) in the tone's colour, the plain difference under it, or beside it on a tile |
  | `Views/RunBars` | two bars in a cell, each followed by its figure |
  | `Views/PresentConverter` | visible only for a value that is there (not null, 0 or "") |
  | `MainViewModel.cs` | `Section` takes `Compare`; `IsCompare`, `IsLive`; `Current()` (the session as a parse, for Use current); tells `Compare` when a shared setting, the theme or the session changes |
  | `MainWindow.xaml` | Compare on the switch; the character row hidden over Compare; the section as the third child of the body |
  | `Settings.cs` | `compareBy`, `compareMode` |
  | `Themes/*.xaml` | `RunABrush`, `RunBBrush` (the first two fallback slots) and `RunInkBrush` (the letter on a run's badge) |

- **The rules, as the reference has them:**
  - A is the baseline. Every change is B − A, every percentage that over A.
    Over a baseline of nothing a figure reads "new"; with a side missing, a
    dash.
  - A run is measured exactly as it would be if imported. Nothing is counted
    a second way.
  - By job re-keys every row onto its character's main job before the count.
    A job is never borrowed from the other run.
  - The shorter run's line ends where the run did. The chart's hover card
    names only the runs that have a figure there.
  - A parse with no heal lines at all is unmeasured in Healing mode: dashes,
    no line, and a note saying which side. A healer missing from a run that
    does have heal lines healed 0 there, with no average.
  - Length is neutral (shorter is not better), and so is Casts.
  - There is no owner. Hide names hides every name; a job that stands in
    for a name is numbered from the second character on it.
  - Include Skillchains is the live sections' own setting, shown on the
    Compare card in Damage mode and hidden in Healing mode.
  - Open rows stay open through Swap, a mode change, Hide names and
    Include Skillchains; they are forgotten when a slot changes or when
    Character | Job changes.
  - An error stays on the slot it happened in, and the slot keeps what it
    held. Swap moves the errors with their runs.
  - Use current takes a copy through the export format. A running clock is
    frozen in the copy alone; the session goes on.
  - Compare by and the mode are saved (Decision 12). The slots are not.
  - Only the mode on screen has rows, and a closed row has nothing under it:
    what opens under a row is made when it opens.
- **Deliberate differences from the reference.**
  - **Hide names also hides who a heal landed on when they have no row.**
    The reference drew a heal target through the table's own rows, so a
    target with no row kept their name: everyone, when the rows are jobs,
    and anyone who only received heals (a pet among them) when the rows are
    characters. Zerg draws those as their job too, numbered on from the rows
    (`CompareSheet.Naming.Draw`).
  - The slot says "1 character" for a party of one; the reference said
    "1 characters".
  - Every bar in a table is to one scale. The reference let a bar grow to
    its share of the whole cell and then capped it, so the longest bars
    were shorter than their share.
  - A slot's buttons are always at its top right; the reference moved them
    under the drop hint while the slot was empty.
  - Start and Pause stay above Compare (Decision 11); the reference hid its
    whole filter bar.
  - The Open dialog lists `.zerg` beside `.json` (Decision 7a).
- **Checked against the reference**, both pages given the same files: the
  two generated parses (`gen-test-events.py --export`, seeds 7 and 11) and
  the 18-character alliance parse. Zerg's `drive.cs text` was compared with
  the reference page's own text word by word, section by section (tiles,
  legend, the main table with rows opened, the two cards under it), in
  fourteen states. All agreed, except the heal targets while names are
  hidden, which is the first difference above.
  - Damage, by character, two rows open; the same with skillchains off.
  - Healing, by character, a healer open; healing by job.
  - Damage by job with a row open; the same with names hidden; healing by
    job and by character with names hidden; damage by character with names
    hidden.
  - After Swap.
  - The alliance parse against a generated one: damage and healing, by
    character and by job. The alliance parse has no heal lines, so its side
    of Healing is dashes and the note says so.
  - The chart's data table, all 17 rows, with the shorter run's dashes.
- **Checked in the running app** (one 2560×1600 display at 150 %):
  - The Open dialog, driven for real: a `.json` into A, a `.zerg` into B.
  - A `.zerg` file dragged from an Explorer window with the real mouse and
    dropped on B: loaded. An event file dropped on a full A: the slot kept
    its run and said why the file would not load.
  - An event file and a junk file through the dialog: each slot's own
    message, the runs kept. Clear.
  - Use current: disabled before Start; taken while the
    session ran (Length 00:12, 77,213) and the session went on to 153,687;
    taken again while paused, when the tiles equalled the Damage section's
    exactly (153,687, 00:36, 4,171.6 DPS).
  - The chart: the shorter run's line stopping at 4:58 of 20:54, and the
    hover card past it, by the real mouse, naming run A alone.
  - Light and dark. A 600-unit-wide window: slots stacked, tiles in twos,
    the cards that come in pairs stacked, the main table scrolling sideways.
  - The section, Compare by and the mode restored after a restart.
- **Tests.** 238 in all, 164 without the mutation check: 20 new ones in
  `CompareSheetTests.cs`, on runs made the way Use current makes one.
  All 238 pass: the mutation check was run on the code as N6 left it and
  caught 74 of 74 (9 minutes this time, with nothing else running).
- **Cost** (Debug, 150 %): 0.2 % of one core with two parses compared on
  screen and nothing arriving. Compare redraws only on a change.
- **Not checked:** two real parses from two different evenings of play (the
  generated pair share a roster); a parse of a megabyte or more for how long
  a redraw takes (the alliance parse is five minutes long); a second monitor.
- **How it looks. Not yet confirmed by the user** (Decision 9 asks for
  that). The choices that had more than one reasonable answer:
  - Compare is the third segment of the section switch.
  - The switches (Show, Compare by, Include Skillchains, Swap) sit on a line
    of their own under the card's heading, not at its right.
  - A slot is an outlined box with its run's colour down the left edge:
    dashed and empty-looking until it holds a parse, accent-outlined while
    a file is held over it.
  - A run's badge is its letter on its colour (blue A, orange B).
  - Good news is the theme's green and bad news its red; no change, and a
    change that is neither, are the quiet ink.
  - Tiles are six cards, each A over B with the change under a rule; the
    total's two figures are in the accent colour.
  - In a table, a cell of two figures is two lines, each behind a small
    tick in its run's colour; a change is the percentage with the plain
    difference under it in small print.
  - A row opens onto its actions on a quieter, indented surface.
- **`tools/drive.cs` grew**: `type <target> <name> <text>` fills a text
  field and `enter <target>` presses Enter in a window, which together work
  an Open dialog; `where <target> <name>` prints the middle of an element,
  for `hover`, `press` and `drag`. Its output is UTF-8 now, so a minus
  sign, a dash and Δ come out as themselves.
- **Traps.**
  - **A long command is cut short.** A Bash command of more than about
    8,000 characters arrived truncated ("unexpected EOF"). Write a long
    script to a file and run the file.
  - **A tooltip can become the process's main window.** With the pointer
    resting on something that has a tooltip, `drive.cs <pid>` addressed the
    tooltip (`snap` gave a 222×49 picture, `text` nothing). Use the window's
    `h<hwnd>` from `windows <pid>`, or move the pointer off first.
  - **A window moved from PowerShell lands scaled.** `MoveWindow` called
    from PowerShell (which is not DPI-aware) put a window half as far again
    as asked, at 150 %. Give it the numbers divided by the scale.
  - **Three expanders are called "Data table"**, one per section, and a
    collapsed section's is still found first. Compare's is named
    "Data table of both runs".
  - **`Include Skillchains` is on the command bar and on the Compare
    card.** `click` finds the bar's first, hidden or not; either flips the
    one setting.
  - **The reference capitalises its headings in the stylesheet** and runs a
    tile's two change figures together; compare without case, and split
    them.
  - **An element that is only hidden is still read out.** Every hidden
    copy of a table shows in `text`. Compare makes what is under a row only
    when the row opens, and gives rows only to the mode on screen, which is
    why its text can be compared with the reference at all.
  - **The settings file is the user's.** They tried the app between two of
    my runs and left Include Skillchains off; a comparison with the
    reference then differed until the reference was set the same way. Read
    `settings.json` before blaming the code.

### N7 — import / export
Export a paused parse to a `.zerg` file; import one in place of the live
session, and go back. **Done when** its checklist rows pass.

**Done 2026-10-03.**

#### Handoff to N7 (written at the end of N6, 2026-10-03)

**Where things stand.**
- `Zerg.exe` is the live Damage and Healing sections with their floating
  panels, and the Compare section. Import / export is the last checklist
  group. Nothing is committed; the user handles git.
- The suite is 238 tests, 164 without the mutation check. All pass (the
  mutation check, 74 of 74, was run on the code as N6 left it).
- **Waiting on the user:** the look of the Compare section ("How it looks"
  under N6) has not been confirmed. Ask before building more on it.
- The user's desktop shortcut still runs the old web snapshot in
  `dist/Zerg/`. That is intended until N9.

**What N7 is.** The two rows under "Import / export" in the checklist:
- **Export**, offered only while paused. The document is built before the
  dialog opens. Suggested name `<Owner>_parse_<YYYY.MM.DD>_<HHMM>.zerg`
  (Decision 7a), from the session's zero in local time, the owner stripped
  to letters, digits, `_` and `-`.
- **Import**, always offered. It sets the live state aside whole and shows
  the parse in its place; **Back to live** (there only during an import)
  puts it back. A second import replaces the first.
- **An import locks both session buttons**, on the main window and on
  every panel.
- Both `.zerg` and older `.json` files import, told apart by content.
- The reference also says what the last Export or Import did, in a note
  beside the buttons that clears itself after eight seconds, and names the
  source "imported · <name>" on the status line.

**Read first:**
- `../damage-meter/CLAUDE.md`, "Export and import": why export waits for a
  pause; only the rows the session can count are written; filters are not
  exported; an import is locked and can be exported again; import takes
  nothing away, and a running session keeps running behind it; the owner
  is the exporter's.
- `../damage-meter/web/app.js`: `exportParse`, `exportName`, `saveFile`,
  `importParse`, `loadParse`, `stashLive`, `backToLive`, `adoptSource`,
  `applyParse`, `parseNote`, `srcName`, and every `app.imported` test.

**What exists already.**

| Need | Where |
|---|---|
| The export document and its text | `ParseFile.Export`, `Stringify` (byte-identical to the reference's, N1) |
| "Only what the session covers" | the `keep` in `CompareSheet.Snapshot` is the same rule; Export wants it without the frozen clock, since the session is already paused |
| Reading a parse, with errors as messages | `ParseFile.Import` (`ImportedParse`: a reader, a paused session, `Skipped`, `File`) |
| The Open dialog, and a name without its extension | `ParseDialog.Open`, `Read`, `Title`. It has a `ClientGuid`, so Windows remembers its folder: give the Save dialog the same one and Import opens where exports went |
| Locked buttons, and the wording of an import | `SessionView.Of(session, imported: true)`; `SessionText.Empty` and `TotalNote` take `imported` too. None of the three is passed `true` anywhere yet |
| A dialog asked for by a view model | `CompareViewModel.Picker`, set in `MainWindow`: do the same for Import and for Save |
| Parses to import | `../damage-meter/tools/gen-test-events.py --export <file>.json --seed N --date YYYY-MM-DD`; `../damage-meter/tools/exports/Paradox_Kirin_4.json` |

**What is new in N7.**
- **Where the buttons go.** Export, Import and Back to live need a place on
  the command bar, and the note a place beside them. That is a visual
  choice: ask.
- **The Save dialog** (`Microsoft.Win32.SaveFileDialog`), beside
  `ParseDialog.Open`: default extension `.zerg`, the suggested name, the
  same `ClientGuid`.
- **Showing a parse instead of the session.** `Tracker` holds the reader,
  the session and the colour slots (`Cast`) and counts them. An import
  needs the same count over another reader and session, with the live
  tracker left as it is and still fed by the poll. One way: a second
  `Tracker` made from an `ImportedParse` (it needs a way to be given a
  reader and a session), and `MainViewModel` counting whichever is on
  screen. Start, Pause and Cancel must refuse during an import as well as
  look disabled.
- **`MainViewModel.Current()`** (Use current in Compare) reads the live
  tracker and calls the copy "Current session <time>". During an import
  the reference used the import: its name, and its file. Make `Current()`
  follow whichever is on screen.
- **The status line** during an import: "imported · <name>", no line
  count, and the date before the start time.
- **A new event file arriving during an import** belongs to the live
  state set aside, not to what is on screen.

**Running it.** `dotnet run --project apps/zerg/src/Zerg`, or a scratch
folder and `tools/replay.py` without the game (`CLAUDE.md`, "Verifying a
change", item 5). Compare is `click <pid> "Compare section"`; a slot is
filled with `click <pid> "Open a parse into A"`, then `windows <pid>` for
the dialog's `h<hwnd>`, `type h<hwnd> "File name:" <path>` and
`enter h<hwnd>`. The Save dialog can be worked the same way. To check an
export, import it into the reference page and compare the figures; a
re-export of an import should equal the original apart from `exported`.

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer. Everything new follows the naming rule. When N7
is done, tick its checklist rows, mark it done here, and write a handoff for
N8.

#### What N7 built, and what it taught (handoff to N8)

- **An import is a second `Tracker`, and the session is never touched.**
  `Tracker.Of(parse)` wraps an imported parse's reader and session, so it is
  counted, coloured and drawn by exactly the code that draws the session,
  panels included. It is marked `Imported`, and refuses `Start`, `Second`,
  `TogglePause`, `Cancel`, `Follow` and `Feed`.
  - `MainViewModel` holds `live` (the session, fed by every poll) and
    `import` (null, or the parse on screen). `Shown` is `import ?? live`, and
    everything that puts a figure, a name or a colour on screen reads `Shown`.
  - Nothing is set aside and put back, as the reference did. The session
    simply goes on being fed while it is off screen, and Back to live drops
    the import.
  - While an import is on screen a poll feeds `live` and counts nothing. An
    armed session therefore finds its zero on the way back, at the same hit
    it would have (the zero is the hit's own time, not the time it was
    noticed).
- **The pieces.**

  | File | What changed |
  |---|---|
  | `Zerg.Core/Tracker.cs` | `Of`, `Imported`, `CanExport`, `Export(now)` |
  | `Zerg.Core/ParseFile.cs` | `Extension` (`.zerg`), `SuggestedName(owner, startedAt)`, `CanSaveAs(path)` |
  | `Zerg.Core/SessionView.cs` | `SessionText.Status(session, imported)`: an import's start carries its date |
  | `src/Zerg/MainViewModel.Parse.cs` | `import`, `Shown`, `IsImported`, `CanExport`, the tips, the note and its timer, `ExportCommand`, `ImportCommand`, `BackToLiveCommand`, `Picker`, `Saver` |
  | `src/Zerg/MainViewModel.cs` | `live` in place of `tracker`; `Start` and `Second` refuse during an import; the status line; `Current()` follows what is on screen |
  | `src/Zerg/ParseDialog.cs` | `Save(owner, name)` |
  | `src/Zerg/MainWindow.xaml(.cs)` | Export…, Import…, Back to live and the note on the command bar; the two dialogs handed to the view model |

- **The rules, as the reference has them:**
  - Export is offered only while the clock is stopped; its tooltip says
    which of "Pause first" or "Start, then Pause" applies.
  - The text is made when the button is pressed, before the dialog opens.
  - Only rows the session can count are written (after the zero, outside
    every pause). Filters are not exported.
  - The name offered is `<Owner>_parse_<YYYY.MM.DD>_<HHMM>.zerg`, from the
    session's zero in local time.
  - Import is always offered. A second import replaces the first, and Back
    to live still lands on the session.
  - An import locks Start and Pause in the main window and on every panel,
    and the commands refuse as well.
  - An import can be exported again, and comes out as the file it came
    from, apart from `exported`.
  - The owner is the exporter's: slot 0, and the name Hide names keeps.
  - The viewer's own exclusions, Include Skillchains and Hide names apply
    to an import.
  - The status line reads "imported · <file name>", then the events and
    the start with its date, and no line count.
  - A note beside the buttons says what the last Export or Import did, in
    the bad-news colour for a failure, and clears itself after 8 seconds.
  - Import, Back to live, and Start close both drill-downs.
  - A new event file arriving during an import belongs to the session: the
    import stays, and Back to live lands on the new file.
  - Compare's Use current takes whatever is on screen: an import goes in
    under its own file's name.
- **Deliberate differences from the reference.**
  - Exports are `.zerg` (Decision 7a). Import lists `.zerg` and `.json`
    together.
  - The session is read while an import is on screen, not parked and read
    on the way back. The figures come out the same.
  - Back to live is shown over the Compare section too while an import is
    on screen. Start and Pause are there (Decision 11), locked, and their
    tooltip sends the reader to that button. Export and Import are not
    shown over Compare.
  - `ParseFile.CanSaveAs` refuses a name ending `.jsonl` with a note. The
    Save dialog already turns `x.jsonl` into `x.jsonl.zerg`, so this only
    catches a machine where `.jsonl` is a registered type.
- **Checked against the reference**, three ways:
  - A generated parse (`Hasaya_run1.json`) imported into both. The same
    tiles, status line, locked buttons and note; the per-character table,
    the actions table, the healing table and the heals table agreed word
    for word (93, 350, 28 and 46 words).
  - A session measured in Zerg (the fixture played into a live file),
    exported, and the `.zerg` file imported into the reference page: its
    tiles and all four tables equalled Zerg's live view of that session.
  - That import exported again from Zerg: byte for byte the original
    (221,977 bytes), apart from `exported`.
- **Checked in the running app** (one 2560×1600 display at 150 %):
  - Export disabled while running, enabled once paused. The Save dialog
    offered `Hasaya_parse_2026.10.03_2103`, type "Zerg parse (*.zerg)".
  - The export imported back into Zerg: every line of `drive.cs text`
    (932 of them) equal to the paused live view, apart from the buttons,
    the notes and the status line. Back to live: equal to before.
  - A `.json` export and a `.zerg` one both import.
  - Start and Pause disabled in the main window and on a floating panel
    during an import; a real mouse press on the panel's Start armed nothing.
  - An armed session with an import opened over it and the fixture then
    played into the file: the import did not move and the session did not
    start. Back to live 40 seconds later: the session started at the first
    hit's own time, its clock at 00:40, total 153,687, and Pause worked.
  - A junk file and an event file: "Import failed: …" in red, the screen
    unchanged.
  - A newer event file created during an import: logged as followed, the
    import and its open drill-down unchanged; Back to live showed the new
    file, idle.
  - Over Compare during an import: only Back to live of the three; Use
    current put the import in a slot as "Hasaya_run2".
  - The note gone after ten seconds.
  - Both dialogs reopening in the folder a parse was last opened from.
    Windows does not remember a folder under `%TEMP%`: test this with an
    ordinary one.
  - Light and dark.
- **Tests.** 247 in all, 173 without the mutation check: 9 new ones in
  `ImportExportTests.cs`. All 247 pass: the mutation check was run on the
  code as N7 left it and caught 74 of 74.
- **Not checked:** an export of a real evening's session from the addon
  (N9, step 1); a write that fails (a read-only folder, a full disk) for
  the wording of "Export failed"; the note's wording read by a screen
  reader (it is marked as a polite live region).
- **How it looks. Not yet confirmed by the user**:
  - Export… and Import… are two plain buttons after Start and Pause.
  - Back to live is an accent-coloured button beside them.
  - The note is small quiet text after the buttons (red for a failure),
    cut with an ellipsis past 460 units, with the whole text on hover.
  - With these on it, the command bar no longer fits one line at the
    default width: Panel opacity wraps to a second line (and Hide names
    with it during an import). The bar was built to wrap; whether it should
    be rearranged instead is the user's call.
  - The status dot is amber during an import, as for a held session.
- **`tools/drive.cs` grew**: `keys <target> <text>` types as a keyboard
  would, into whatever has the focus. `type` now skips a field that is
  read-only.
- **Traps.**
  - **A Save dialog ignores `type`.** UI Automation's SetValue changes the
    text in the name box and the dialog still saves under the name it had.
    Twice that put a test export in the user's Documents folder (moved out
    and deleted). Use `keys`, which goes into the name box a new Save
    dialog opens with focused and selected, then `enter`.
  - **The Save dialog hides the extension** in the name it offers
    (`Hasaya_parse_…_2103`, not `….zerg`) and adds it on saving.
  - **A tooltip is a window** of the process, topmost, no-activate and
    layered like a panel: `windows <pid> | grep noactivate` can return it
    with the panel. Pick a panel by its title.
  - **`names` lists only what is shown**, which makes it the way to ask
    whether a button is on screen; `text` lists hidden ones too.
  - The reference page's Open dialog is stubbed with
    `Object.defineProperty(window, 'showOpenFilePicker', …)` returning a
    handle whose `getFile()` gives a `File`; then click `#importBtn`.

### N8 — extras that are cheap natively
Built once the checklist is complete. The user picked these four on
2026-10-03 (Decision 14):
- single instance, so a second launch brings the first window forward
- click-through panels with a global hotkey to take them back
- reopening the panels that were open when Zerg was last closed
- a tray icon

Not picked: opening a `.zerg` file by double-clicking it (a per-user file
association under `HKCU\Software\Classes`, handed to the running instance). It
needs single instance first, so it stays cheap to add if the user asks.

**Done when** each of the four works in the running app and nothing on the
checklist has changed.
**Done 2026-10-04.** What it built is under "What N8 built", after the
handoff it started from.

#### Handoff to N8 (written at the end of N7, 2026-10-03)

**Where things stand.**
- **The feature checklist is complete.** `Zerg.exe` does everything the
  reference did: the live Damage and Healing sections, floating panels,
  Compare, import and export. What is left is N8 (these extras) and N9
  (sign-off with the game, publishing, and deleting the old code).
- Nothing is committed; the user handles git.
- The suite is 247 tests, 173 without the mutation check. All pass (the
  mutation check, 74 of 74, was run on the code as N7 left it).
- **Waiting on the user:** the look of the Compare section (under N6) and
  of the import / export controls (under N7). Neither is confirmed.
- The user's desktop shortcut still runs the old web snapshot in
  `dist/Zerg/`. That is intended until N9.

**Ask first.** Each extra has a choice in it that is the user's:
- **The hotkey**: which keys. The Phase 0 spike used Ctrl+Alt+Z
  (`PLAN.md`, the spike's results table).
- **Click-through**: whether the hotkey switches every panel at once or
  one; and how a panel shows that clicks pass through it, since it can no
  longer be clicked to say so.
- **The tray icon**: what its menu holds; whether closing the main window
  now hides Zerg to the tray or still quits (today `ShutdownMode` is
  `OnMainWindowClose`, and closing docks every panel); what a click on the
  icon does.
- **Reopening panels**: Zerg starts with every card docked, as the
  reference did (N4). Confirm that the panels open at close should now
  come back at the next start, at their saved places.

**What exists already.**

| Need | Where |
|---|---|
| Panel windows, their styles, and their placement | `PanelWindow`, `Native/Overlay.cs` (`Attach` adds the tool-window and no-activate styles), `Native/WindowPlacement` (`CaptureFrame`, `ApplyFrame`, `IsUsableFrame`) |
| Which panels are open | `PanelSet` (`open`, `Open`, `Close`, `CloseAll`); `MainWindow.OnClosing` calls `CloseAll` before saving its own placement |
| Settings | `Settings` (add the open panels, the hotkey); each panel's frame is already under `windows` as `panel:<key>` |
| Startup | `App.OnStartup`: options, settings, theme, feed, window. Single instance goes first here, before anything is read or written |
| The APIs chosen | the "Windows and the shell" table near the top: `RegisterHotKey` and `WM_HOTKEY` through an `HwndSource` hook; `WS_EX_TRANSPARENT \| WS_EX_LAYERED`; `Shell_NotifyIconW` by P/Invoke, no WinForms; a named `Mutex` and a named pipe, with `AllowSetForegroundWindow` |

**What to watch for.**
- A panel is already layered (`AllowsTransparency`). Click-through is
  adding `WS_EX_TRANSPARENT` to its extended style and taking it away.
  While it is on, the panel's own bar cannot be reached: the hotkey, and
  the tray menu if there is one, are the only ways back.
- The panel opens for a card whose data may not be there yet at startup
  (no session). A reopened panel shows "Press Start to begin measuring",
  or for a drill-down "Nothing to show yet", which is what it shows today
  after Start.
- A second instance must do nothing to the settings file or the log before
  it knows it is second.
- `drive.cs` cannot press a global hotkey through UI Automation; `keys`
  types text. A hotkey needs `keybd_event` with the modifiers held, which
  `enter` shows the pattern for.
- These checks belong over the game where they can be had (a click
  reaching the game through a panel; the hotkey while the game has the
  keyboard). With no game running, another application's window stands in,
  as it did for N4, and the game itself is N9, step 1.

**Running it.** `dotnet run --project apps/zerg/src/Zerg`, or a scratch
folder and `tools/replay.py` without the game (`CLAUDE.md`, "Verifying a
change", item 5).

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer. Everything new follows the naming rule. When N8
is done, mark it done here and write a handoff for N9.

#### What N8 built, and what it taught (handoff to N9)

The four choices the N7 handoff left to the user were asked first, and the
answers are Decision 15.

- **The pieces.**

  | File | What it is |
  |---|---|
  | `Zerg.Core/KeyChord.cs` | a key with its modifiers, read from text ("Ctrl+Alt+Z") and written back; `Parse`, `OrDefault`, `Default`. 31 unit tests in `KeyChordTests.cs` |
  | `src/Zerg/Native/SingleInstance.cs` | `Claim` (a named mutex), `Signal` (second launch: pass on the right to the foreground, then one byte down a named pipe), `Listen` (first launch: a background thread on the pipe) |
  | `src/Zerg/Native/HotKey.cs` | `RegisterHotKey` on the main window's handle, `WM_HOTKEY` through a hook; null when Windows refuses the chord |
  | `src/Zerg/Native/TrayIcon.cs` | `Shell_NotifyIconW`, version 4; events `Picked` and `MenuAsked`; `Shown`; put back when Explorer restarts (`TaskbarCreated`); `Front` |
  | `src/Zerg/Native/Overlay.cs` | gained `ClickThrough(window, on)`: `WS_EX_TRANSPARENT` added or taken away |
  | `src/Zerg/TrayMenu.xaml(.cs)` | the tray icon's menu, a `ContextMenu` bound to the main view model |
  | `src/Zerg/Panels.cs` | `PanelSet`: `ClickThrough`, `ToggleClickThroughCommand`, `HotKey`, `ClickThroughTip`, `AnyOut`, `DockAllCommand`, `Reopen`, `Leave` (in place of `CloseAll`), `All`. `PanelInfo`: `ToggleCommand` |
  | `src/Zerg/PanelWindow.xaml(.cs)` | `Through()`: the window style, and the bar's two forms (`Pair`, `Tools`, `Locked`) |
  | `src/Zerg/MainWindow.xaml(.cs)` | `Attach` (tray icon and hot key, at `SourceInitialized`), `ComeForward`, `ShowMenu`, `OnStateChanged` (minimize hides), `Reopen`; the Click-through toggle on the command bar |
  | `src/Zerg/App.xaml.cs` | single instance first in `OnStartup`; `Reopen` after the window is shown; the listener |
  | `src/Zerg/Settings.cs` | `openPanels`, `clickThroughKey`; strings written unescaped |

- **Single instance.**
  - One Zerg per Windows session: the mutex is `Local\VibeXI.Zerg`, the pipe
    `VibeXI.Zerg.<session id>`, open to the current user only.
  - `Claim` is the first thing `OnStartup` does. A second launch reads no
    options, opens no log and loads no settings; it signals and exits 0.
    `OnExit` skips its log line too.
  - The second launch calls `AllowSetForegroundWindow` before it writes to the
    pipe. Windows gives the right to the foreground to the program just
    started, and without passing it on the first one's `Activate` only
    flashes its taskbar button.
  - The first logs `launched again: brought forward` and calls
    `MainWindow.ComeForward`, which is also what a click on the tray icon and
    the menu's Show Zerg do: shown if hidden, restored if minimized (to
    maximized if that is what it was), activated.
  - A second launch's own command line is ignored. `--events-dir` on it does
    nothing.
- **Click-through.**
  - One switch for every panel: `PanelSet.ClickThrough`. Each `PanelWindow`
    listens for it and calls `Through()`.
  - Switched from four places: the hot key, the Click-through toggle on the
    main window's command bar, the tray menu's line, and nowhere on a panel
    (a click-through panel hears nothing from the mouse).
  - **Not remembered between runs.** Zerg always starts with panels that can
    be clicked, so nobody is met by a panel they cannot move and a key they
    have forgotten. My choice; the user has not been asked.
  - **A panel popped out while it is on comes up click-through**, with the
    slim bar. It can still be docked from the main window's stand-in or the
    tray menu.
  - Switching it on drops a drag in progress and puts the bar's faded parts
    back to resting: no mouse-leave will come to do it.
  - The bar while on: title, clock, total, lock (Decision 15). The card under
    the bar is unchanged, so what it has to press is still drawn and cannot
    be pressed: Group under 5%, the rows of an actions list, a drill-down's
    Close. The user answered for the bar; the card was not asked about.
- **The hot key.**
  - `settings.clickThroughKey`, "Ctrl+Alt+Z" by default. There is no control
    for it: it is changed in the file, and read at start.
  - A chord is modifiers and one key (A to Z, 0 to 9, F1 to F24) joined by
    "+", any order, any case. It must include Ctrl, Alt or Win: a bare key
    or Shift alone would take a typing key, or one the game uses, from every
    application. Anything else is no chord, the default is used, and the log
    says so. The file keeps what the player wrote.
  - If another program holds the chord, there is no hot key. The log says
    `taken by another program`, the tooltip on the main window's toggle says
    the tray menu has it instead of naming keys, and the menu line shows no
    keys beside it. Nothing tries a second chord.
  - `MOD_NOREPEAT`: holding the keys switches once.
- **Reopening panels.**
  - `settings.openPanels`: the keys of the panels that are out, in listing
    order. Written each time one goes out or is docked, so it is right after
    a crash too.
  - Closing Zerg calls `PanelSet.Leave`: the windows close, each saving its
    frame, and the list is left alone. Dock all panels (the tray menu) docks
    them and empties the list.
  - `App.OnStartup` calls `Reopen` after the main window is shown: a panel
    with no saved frame opens beside it. A key the build does not know is
    skipped.
  - The checklist row "closing a panel docks it" still holds. Closing Zerg
    is not closing a panel.
- **The tray icon.**
  - Owned by the main window's handle, which exists whether the window is
    shown or hidden. The icon is the exe's own (`ExtractIconExW`); the tip is
    "Zerg".
  - Left click or Enter: `ComeForward`. Right click or the menu key:
    `ShowMenu`.
  - Minimizing hides the window (`OnStateChanged`), but only while the icon
    is really there (`TrayIcon.Shown`): with Explorer gone, a minimize stays
    an ordinary one. The state before minimizing is kept in `restoreTo`.
  - Exit from the menu is `MainWindow.Close`, so it saves everything the X
    does. A window closed while hidden saves its restored frame, not the
    place Windows parks a minimized one.
  - **Windows 11 puts a new icon in the overflow** (the ^ beside the clock)
    until the player drags it out or switches it on under Settings >
    Personalization > Taskbar > Other system tray icons. So a minimized Zerg
    is behind the ^ at first. Launching Zerg again brings it back: single
    instance makes that work.
- **Checked in the running app** (one 2560×1600 display at 150 %, another
  application's window standing in for the game):
  - A second launch with another window in front: exit code 0, the first
    window in front (`drive.cs fg`), one process, no second "starting" line
    in the log. The same with the first hidden in the tray: shown again at
    its frame.
  - The icon is there (`drive.cs tray <h> rect`), and gone after exit.
  - A real left click on the icon in the overflow: the hidden window is
    back, in front.
  - A real right click: the menu, in front, with the main window active and
    with another application active. A click elsewhere closes it.
  - Every line of the menu: Show Zerg; Resume and Pause (the log has
    `session resumed`, `session paused`); both greyed during an import;
    Click-through panels ticked and unticked; the submenu's eight cards with
    a tick on the floating one, popping one out and docking one; Dock all
    panels (greyed with none out); Exit, while hidden in the tray.
  - The hot key with another application holding the keyboard: every panel
    gains the click-through style (`drive.cs windows`), the foreground window
    does not change, the bar is title, clock, total and lock.
  - **A real click on a click-through panel lands on what is under it**: the
    panel lay over a character chip in the main window, and a press on the
    panel excluded the character. With click-through off the same press did
    nothing to the chip.
  - The chart's hover card and crosshair, up when click-through goes on,
    are gone at once.
  - The main window's toggle and the tray line switch it too, and all three
    agree. A panel popped out while it is on comes up click-through.
  - The hot key while the main window is hidden in the tray.
  - Another program holding Ctrl+Alt+Z; a chord of the player's own
    (`ctrl+shift+f12`: it works and Ctrl+Alt+Z does nothing); a setting that
    is no chord (`Banana`: the default, and a log line).
  - Two panels out, one moved, Zerg closed with the X: both back at the next
    start at their frames, neither click-through. One docked, closed,
    started: one back. Dock all, closed, started: none.
  - A reopened panel before Start shows "Press Start to begin measuring".
  - The menu in light and in dark; the toggle, off and on, in dark.
- **Tests.** 278 in all, 204 without the mutation check: 31 new ones in
  `KeyChordTests.cs`. All 278 pass: the mutation check was run on the code as
  N8 left it and caught 74 of 74. Nothing in the counting code changed.
- **Not checked:**
  - Over the game: all of it. It is on N9's list (step 1).
  - Explorer restarting (the icon coming back). Restarting it on the user's
    desktop was not mine to do.
  - The hot key pressed in the middle of a drag (the code releases the
    capture; no script presses a key mid-drag).
  - A second monitor; two people signed in at once.
  - The menu opened from the keyboard (Win+B, then the menu key): it opens
    at the pointer, which may be nowhere near the icon.
- **How it looks. Not yet confirmed by the user:**
  - The lock on a click-through panel's bar: Segoe Fluent Icons `E72E`, 12
    units, at the right where the tools were, in the quiet text colour.
  - The Click-through toggle on the command bar, after Panel opacity. At the
    default width it is on the bar's second line with it.
  - The tray menu in the Fluent theme, light or dark with the app, Show Zerg
    in semibold.
- **`tools/drive.cs` grew**: `chord <keys>` presses a key with its modifiers
  held, to whatever has the keyboard (a global hot key); `at <x> <y> [right]`
  clicks a point of the screen; `tray <target> rect|pick|menu` asks the shell
  where a window's icon is, or tells the window what the shell would for a
  click or a right click; `windows <pid> all` lists hidden windows too, and
  every listing now marks `clickthrough` and `hidden`.
- **Traps.**
  - **A dev run no longer starts beside a running Zerg.** It brings the
    running one forward and exits with code 0, silently. If a change seems
    to have no effect, look for an older `Zerg.exe` first. After N9 the
    user's own copy is that `Zerg.exe`: ask them to close it, and never stop
    a process you did not start.
  - **A hidden main window is not the process's main window.** While Zerg is
    in the tray `drive.cs <pid>` finds nothing or a panel. Take the window's
    `h<hwnd>` from `windows <pid> all` before hiding it.
  - **The Fluent theme's styles are found by a control's exact type.**
    `TrayMenu` is a class of its own, so it names its style
    (`Style="{DynamicResource {x:Type ContextMenu}}"`). Without that it was
    drawn in Windows' old menu colours with the theme's white ink on top.
  - **The Fluent menu's own tick is not used.** One tickable line indents
    every plain line beside it and leaves a line with a submenu where it was;
    and `IsChecked` without `IsCheckable` draws no tick at all. `TrayMenu`
    gives every line a header template with a tick column of its own.
  - **A menu opened from the tray must be made the foreground window**
    (`TrayIcon.Front` on the popup's handle), or it stays up when the player
    clicks elsewhere. Windows allows that only in answer to a real click on
    the icon. `drive.cs tray <h> menu` posts the message without the click,
    so that menu cannot take the foreground, and with Zerg's own window in
    front it closes at once. It is good for reading the menu, not for
    proving it behaves: use the real mouse for that.
  - **`tray rect` gives the ^ button's place while the icon is in the
    overflow and the overflow is closed** (48×72 here). Click that, wait a
    second, and ask again: 60×60 is the icon itself.
  - **`System.Text.Json` writes "+" as `+`.** The settings are now
    written with the relaxed encoder so the hot key reads as it is typed.
  - **UI Automation's Invoke gives a button the keyboard focus, and WPF
    shows a focused control's tooltip.** A tooltip in a grab after
    `drive.cs click` is that, not a fault.

### N9 — sign-off, switch-over and removal
1. The user signs off the whole checklist in `Zerg.exe`. That includes the
   checks that need the game, which no earlier phase could run (Decision 13):
   - **Panels over the running game**, damage and heal ones: a panel stays on
     top of the game, the game shows through the tint, and the game keeps the
     keyboard while the panel is clicked and dragged.
   - **The live clock past an hour**: leave a session running 61 minutes and
     read the Elapsed tile and the DPS under it at `1:00:00` and after.
   - **A session against the real addon**, with heals in it, a pet's among
     them.
   - **Click-through over the game** (N8): with a panel over the game and
     click-through on, a click on the panel reaches the game; Ctrl+Alt+Z
     switches it while the game has the keyboard, and the game keeps it.
   - **The tray with the game in front** (N8): Zerg minimized to the tray,
     panels still up; the icon's menu opens over the game and its Start and
     Pause work.
   - Also unseen so far, and seen in passing here: a second monitor at
     another scale, a full alliance in the strip, the resize cursors, and the
     heal line's hover card by the real mouse.
2. Publish `Zerg.exe` to `dist/Zerg/`, replacing the old snapshot, so the
   desktop shortcut runs the new app.
3. **(Done, 2026-10-04.)** Move into `apps/zerg/`, rewritten under the naming rule: the event contract
   and the reasons behind each counting and session rule (from
   `../damage-meter/CLAUDE.md`), the fixture generator (`gen-test-events.py`)
   and the export fixtures the unit tests use.
4. Delete: `src/Zerg.Web`, `EventsApi.cs`, `Query.cs`, both parity suites,
   `spike/`, `poc/`, `PLAN.md`'s web-host history, and then
   `apps/damage-meter/`.
5. Update references elsewhere in the repo that point at the deleted app. Today
   they are: `README.md`, `.gitignore`, `.claude/launch.json`,
   `addon-dev/PLAN.md`, comments in `addons/VibeXI/` (`vibexi.lua`,
   `vx_emit.lua`, `vx_enums.lua`), `apps/ws-calculator/CLAUDE.md`,
   `shared-calc/README.md`, `shared-ui/README.md`,
   `shared-ui/css/ffxi-theme.css`. `shared-ui` itself stays; the calculators
   use it.
6. Run the naming-rule gate.
7. Rewrite `apps/zerg/CLAUDE.md` for the native app, and update the memory
   notes.

**Done when** Zerg builds, tests and runs with no WinForms, no WebView2, no
reference to `apps/damage-meter`, and a clean naming-rule gate.

**Packaging comes after N9** (today's PLAN.md Phase 4: single-file `Zerg.exe`,
zip, version resource; Phase 5: clean-machine test, SmartScreen, player README).
With no WebView2, the runtime check and its dialog go away. Single instance,
which Phase 4 also listed, was built in N8.

#### Handoff to N9 (written at the end of N8, 2026-10-04)

**Where things stand.**
- **Everything is built.** `Zerg.exe` does all the reference did (the
  checklist), plus the four extras. N9 builds nothing new: it is the user's
  sign-off, the switch-over, and the removal of the old code.
- Nothing is committed; the user handles git.
- The suite is 278 tests, 204 without the mutation check.
- The user's desktop shortcut still runs the old web snapshot in
  `dist/Zerg/`. Step 2 replaces it.

**N9 is two halves, and the first is the user's.** Steps 1 and 2 need the
user at the keyboard with the game running. Steps 3 to 7 are yours, and
**step 4 deletes things that cannot be had back except from git**: do not
start it until the user has said, in so many words, that the sign-off is
done and the old code can go. "Resume plan item N9" is the go-ahead to
begin N9, not to delete. `apps/zerg/` has never been committed (it shows as
untracked), so say so to the user before anything is deleted: a commit
first is what makes the deletion reversible.

**Ask first.**
- **The look not yet confirmed**, three lots: the Compare section (listed
  under N6), the import and export controls (under N7), and N8's (under
  "What N8 built": the lock on a click-through bar, the Click-through toggle
  on the command bar, the tray menu). The command bar now wraps to a second
  line at the default width; whether to rearrange it is still the user's
  call (raised under N7).
- **Three things N8 settled without asking**, each cheap to change:
  - Click-through is not remembered between runs.
  - A click-through panel's card keeps drawing its own buttons (Group under
    5%, the rows of an actions list, Close), though they cannot be pressed.
    Only the bar was slimmed.
  - The hot key is changed in `settings.json` (`clickThroughKey`), with no
    control for it in the window.
- **How the user wants to run the sign-off**: together in one sitting, or
  by themselves with a list. Either way, write the list out for them from
  the feature checklist and step 1, as things to do and what to see.
- **Whether the game runs windowed or borderless.** A panel can sit over a
  window; nothing can sit over a game in exclusive full screen.

**Step 1, what to prepare.**
- The checks that need the game are five now: the three of Decision 13
  and N8's two. All are listed under step 1.
- If the game reads the keyboard below the level Windows' hot keys work at,
  it may also see Z when Ctrl+Alt+Z is pressed, though Zerg gets the hot
  key. If that types or triggers something in game, another chord goes in
  `clickThroughKey`; Decision 15 then wants updating.
- For "the live clock past an hour" nothing has to be watched for an hour:
  start a session, leave it, read the tile at `1:00:00`.
- A fault found in sign-off is fixed in `src/Zerg` and checked again; the
  parity suites are still there until step 4, so a counting fix can still
  be proved against the reference.

**Step 2, publishing.** The command is the one in `CLAUDE.md` for the web
build with the project changed, and `-o apps/zerg/dist/Zerg`:
```bash
dotnet publish apps/zerg/src/Zerg -c Release -r win-x64 --self-contained false -o apps/zerg/dist/Zerg
```
- Empty `dist/Zerg` first: the old snapshot's files (WebView2's among them)
  would otherwise sit beside the new exe. Look at what is in the folder
  before deleting it, and check that the user's old Zerg is closed.
- The shortcut points at `dist/Zerg/Zerg.exe`, and the name does not change.
- **From then on a dev build will not start while the user's Zerg runs**
  (single instance; see N8's traps).
- This is not packaging. Single-file, the zip and the version resource wait
  until after N9.

**Steps 3 to 7.** As listed above. Things to know:
- Step 3 before step 4: the fixture generator and the export fixtures the
  unit tests read live under `apps/damage-meter/tools/`. Search the tests
  for `damage-meter` to find every path, move what they read into
  `apps/zerg/`, and run the suite before deleting anything.
- `tools/replay.py` and the recipes in `CLAUDE.md` read the fixture from
  `apps/damage-meter/tools/events/`; they move with it.
- Deleting both parity suites takes Jint and the Python reference with
  them. What stays is Zerg's own unit tests: run them, and expect the count
  to drop by the parity and mutation tests.
- `tools/cdp.cs` and `tools/measure-tint.ps1` drive the web build; they go
  with it. `tools/drive.cs` and `tools/replay.py` stay.
- Step 5's list of files was made on 2026-10-02. Search the repository
  again; do not trust the list to be complete.
- Step 6, the gate: a case-insensitive search of `apps/zerg/` for
  `damage.?meter` and `metrics`. This plan and `PLAN.md` name the old app
  on nearly every page: step 4 removes `PLAN.md`'s web-host history, and
  this file is either rewritten as a short record or deleted, which is the
  user's choice. Ask.
- Step 7: `CLAUDE.md` is still mostly about the frozen web host. Its
  sections on the native app (Commands, Layout, "Verifying a change" item
  5, the last traps) are the part to keep and grow.
- The memory notes to update: `zerg-native-host` (the migration is over),
  `zerg-naming-rule` (the gate has been run), and `colorkey-inert-on-chrome`
  (about code that no longer exists).

**Running it.** `dotnet run --project apps/zerg/src/Zerg`, or a scratch
folder and `tools/replay.py` without the game (`CLAUDE.md`, "Verifying a
change", item 5).

**House rules.** The user handles git. Ask before visual choices with more
than one reasonable answer, and before deleting. Everything new follows the
naming rule. Copy `settings.json` aside before a check and put it back.

#### N9, first half: what was done (2026-10-04)

**What the user decided at the start of N9** (asked, 2026-10-04):
- **They run the sign-off alone**, from a list. The list is the next section.
- **They commit before anything is deleted, themselves.** Do not commit for
  them, and do not start step 4 until they have said the commit is made.
- **This file ends as a short record**: what was built and decided, in Zerg's
  words, plus the packaging handoff. `PLAN.md`'s web-host history is removed.
- **The unconfirmed looks and N8's three choices are judged during the
  sign-off.** They are part D of the list. Nothing was changed.

**Step 3 is done.** Nothing was deleted and nothing in `apps/damage-meter` or
`src/Zerg.Web` was touched.
- **`RULES.md`** (new): the event contract and the reason behind each counting
  and session rule, in Zerg's words, each pointing at the function in
  `Zerg.Core` that holds it. It is the rewrite of the old app's working notes;
  the parts about the web page (its DOM, its pop-out windows, its stylesheet
  traps, Chrome's colour key) were left behind on purpose. It calls the
  in-game parser the columns are modelled on "the reference parser", and
  points at `addons/VibeXI/vx_enums.lua` for its name: that file is outside
  `apps/zerg`, so the gate does not reach it.
- **`tools/gen-test-events.py`** (copied and reworded): the same generator,
  with its comments under the naming rule. Two differences from the original:
  - It finds the addon's version properly. The original looked for
    `vibexi.lua` one folder too low and always wrote `"v":"0.0.0"` on the
    probe line; this one writes the real version (`0.3.0` today). That line is
    `kind:"meta"`, which the reader skips, and it is the only line that
    differs: checked by generating with both and comparing.
  - `--export` takes a `.zerg` name (Decision 7a), and still takes `.json`.
    An export of seed 7 from both generators is identical but for its
    `exported` stamp.
- **`tools/exports/Paradox_Kirin_4.json`** (copied): the real 18-character
  alliance parse. It stays a `.json` on purpose: it is the proof that an
  older export still imports.
- **`tools/events/Hasaya_2026.07.30.jsonl`**: the generated fixture, made by
  the new generator. `.gitignore` has `apps/zerg/tools/events/` beside the
  old line.
- **No unit test read anything from the old app.** The N9 handoff expected
  some to. Only the two parity suites do (`Parity/PythonReference.cs`,
  `JsParity/JsReference.cs`, whose `Fixtures` class runs the generator), and
  they still point at the originals, which is right until step 4 removes
  both. The plain unit tests make their own lines (`Lines` in
  `TrackerTests.cs`).
- **`CLAUDE.md`** points at `RULES.md` and at the new generator and fixture
  paths in the native recipes. It is otherwise unchanged: its rewrite is step
  7, and until step 4 it has to go on describing the web host and the suites.

**Checked.**
- The suite: 204 passed without the mutation check (the test project alone:
  `dotnet test apps/zerg/tests/Zerg.Core.Tests --filter Category!=Mutation`;
  the user's `Zerg.exe` was running, which locks `src/Zerg`'s output, so the
  whole solution could not be built). The mutation check was not run; no C#
  changed.
- **Step 2's command was rehearsed** into a scratch folder: it builds, and
  gives eight files (`Zerg.exe`, `Zerg.dll`, `Zerg.Core.dll`,
  `CommunityToolkit.Mvvm.dll`, two `.pdb`, `Zerg.deps.json`,
  `Zerg.runtimeconfig.json`), with nothing of WebView2. `dist/Zerg` itself
  was not touched: it still holds the old web snapshot (13 entries, WebView2's
  among them, last built 2026-10-02).

**Found on the way.**
- **`spike/` and `poc/` are already gone.** Step 4 lists them, and `CLAUDE.md`
  still describes `spike/OverlaySpike`; neither folder exists, under
  `apps/zerg` or at the root.
- **Step 5's list is still right.** A fresh search of the repository outside
  `apps/zerg` and `apps/damage-meter` finds the old app named in the same
  eleven files: `.claude/launch.json` (its three `damage-meter*` entries),
  `.gitignore`, `README.md`, `addon-dev/PLAN.md`, `addons/VibeXI/vibexi.lua`,
  `vx_emit.lua`, `vx_enums.lua`, `apps/ws-calculator/CLAUDE.md`,
  `shared-calc/README.md`, `shared-ui/README.md`,
  `shared-ui/css/ffxi-theme.css`. `TMP/` and `nobd.png` at the root are the
  user's and untracked; leave them.

#### The sign-off list (for the user)

Run Zerg as you would use it:

```bash
dotnet run --project apps/zerg/src/Zerg
```

It follows `%LOCALAPPDATA%\VibeXI\events`, the folder the addon writes to.
The game must be **windowed or borderless**: nothing can sit over a game in
exclusive full screen. Tick each line, or note what was wrong; a fault is
fixed in `src/Zerg` and that line is run again.

**A. With the game running (never checked before)**
- [ ] **A real session.** Load the addon, press Start, fight. The status line
      shows the day's file and its counts rising; the tiles, both charts, the
      per-character table and the Actions table fill. Pause, then Resume.
- [ ] **Heals, a pet's among them.** In the Healing section the healers are
      listed, a pet's healing is in the Pet Healing column and not in the
      owner's Healing, and HPS moves with the clock.
- [ ] **A panel over the game.** Pop out Cumulative damage and Damage by
      character, and a Healing card. Each stays on top of the game, the game
      shows through the tint, and the text stays solid.
- [ ] **The game keeps the keyboard.** Click a panel's Start or Pause, drag
      it, resize it by an edge: you can still move and type in game without
      clicking the game first.
- [ ] **Click-through.** Press Ctrl+Alt+Z with the game in front: the panels'
      bars slim down and show a lock, and a click on a panel now reaches the
      game. Press it again: the panels take clicks again. The game never loses
      the keyboard, and the chord types or triggers nothing in game. (If it
      does, another chord goes in `clickThroughKey` in `settings.json`.)
- [ ] **The tray.** Minimize Zerg: it leaves the taskbar, the panels stay up
      and the session goes on. Right-click the tray icon (it may be under the
      ^ by the clock): the menu opens over the game, and its Start and Pause
      work. A left click brings the window back.
- [ ] **Past an hour.** Leave a session running. At an hour the Elapsed tile
      reads `1:00:00` and goes on, the digits beside it do not shift, and the
      DPS under it keeps falling smoothly. Nothing has to be watched for the
      hour.

**B. Seen in passing during A**
- [ ] A panel dragged to the second monitor (another scale) is sharp, the
      right size, and comes back there after a restart.
- [ ] A full alliance: the chips, the floating strip and the cumulative chart
      with Group under 5% hold eighteen names.
- [ ] The resize cursors show at a panel's edges and corners.
- [ ] Resting the mouse on the cumulative healing line shows its hover card.

**C. The rest of the checklist (no game needed)**
- [ ] Start during a session re-arms. Cancel calls off an armed Start.
- [ ] Character chips: exclude one, All, None, collapse the row. The choice
      is still there after a restart.
- [ ] Include Skillchains, Hide names, the theme (light, dark, Windows) each
      do what they say and are remembered.
- [ ] A row of the Actions table opens the drill-down (tiles, histogram, each
      hit); Close shuts it. The same for a skillchain row and for a heal.
- [ ] Export while paused offers `<Owner>_parse_<date>_<time>.zerg`. Import
      opens it, locks Start and Pause, and Back to live returns. An older
      `.json` export imports too
      (`apps/zerg/tools/exports/Paradox_Kirin_4.json`).
- [ ] Compare: load a parse into A and into B, Use current, Swap, Damage and
      Healing, by Character and by Job. The changes read B − A.
- [ ] Panels: opacity by a panel's own slider and by the default; Dock and
      the ✕ put the card back; a panel reopens where it was.
- [ ] Starting Zerg a second time brings the first window forward. Panels
      that were floating when Zerg closed come back at the next start.

**D. To accept, or to say what should change**

The look (each was my choice, never confirmed):
- [ ] **The Compare section**: the two slots as outlined boxes with the run's
      colour down the edge; the A and B badges (blue, orange); six tiles, A
      over B with the change under a rule; table cells of two lines with a
      tick in each run's colour; a row opening onto a quieter, indented
      surface; the switches on their own line under the heading.
- [ ] **Import and export**: Export… and Import… as two plain buttons after
      Start and Pause; Back to live in the accent colour; the small note
      after them; the status dot amber during an import.
- [ ] **The command bar wraps to a second line** at the default width (Panel
      opacity and Click-through go down). Keep it, or have it rearranged?
- [ ] **N8's**: the small lock on a click-through panel's bar; the
      Click-through toggle on the command bar; the tray menu.

Three behaviours I chose without asking, each cheap to change:
- [ ] Click-through is **not remembered** between runs: Zerg always starts
      with panels that take clicks.
- [ ] A click-through panel's card **still draws its own buttons** (Group
      under 5%, the rows of an actions list, Close), though they cannot be
      pressed. Only the bar was slimmed.
- [ ] The hot key is changed **only in `settings.json`** (`clickThroughKey`);
      there is no control for it in the window.

**E. When it has all passed**
1. Commit the tree as it is (`apps/zerg/` has never been committed, and the
   next step deletes `apps/damage-meter/` and the web host; the commit is what
   makes that reversible).
2. Close Zerg, old and new.
3. Say: **"N9 sign-off is done and committed; the old code can go."** With
   anything from D you want changed.

#### Handoff to the rest of N9 (written 2026-10-04)

**Where things stand.** Step 3 is done. Steps 1, 2 and 4 to 7 are not.
Nothing is committed. `dist/Zerg` is still the old web snapshot, and the
desktop shortcut still runs it.

**Do not start until the user has said, in so many words, that the sign-off
is done, that they have committed, and that the old code can go.** "Resume
plan item N9" is not that. If they come back with faults or with changes from
part D of the list, those come first: fix in `src/Zerg`, verify
(`CLAUDE.md`, "Verifying a change", item 5), and have them look again. The
parity suites are still there for a counting fix. A change to a look the user
asks for is not a new question; a new visual choice of yours still is.

**Then, in this order.**
1. **Step 2, publish.** Check no `Zerg.exe` is running (if one is, it is the
   user's: ask). List `apps/zerg/dist/Zerg` and empty it (it is ignored by
   git, so it cannot be had back; it is only a build of code that is
   committed). Then:
   ```bash
   dotnet publish apps/zerg/src/Zerg -c Release -r win-x64 --self-contained false -o apps/zerg/dist/Zerg
   ```
   Expect the eight files listed above. Start `dist/Zerg/Zerg.exe` once and
   read `logs\zerg.log` for a clean start, then close it. The shortcut needs
   no change.
2. **Step 4, delete**, and build and test after each lot:
   - `src/Zerg.Web/` and its line in `Zerg.slnx`.
   - `src/Zerg.Core/EventsApi.cs` and `Query.cs`, with
     `tests/.../EventsApiTests.cs` and `QueryTests.cs`. Nothing in
     `src/Zerg` uses either (searched 2026-10-04).
   - `tests/.../Parity/` and `tests/.../JsParity/`, and the `Jint` package
     from `Zerg.Core.Tests.csproj`. `Fixtures` and `Canon` go with them;
     search the remaining tests for both names first.
   - `tools/cdp.cs` and `tools/measure-tint.ps1`.
   - `PLAN.md`.
   - `apps/damage-meter/`, last. It has uncommitted edits of the user's
     (eleven files show as modified): that is what their commit is for.
   - Expect the suite to drop from 278 by the parity and mutation tests and
     the tests of the two deleted files. Record the new count.
3. **Step 5**: the eleven files listed under "Found on the way". In
   `.claude/launch.json`, the three `damage-meter*` entries go; ask whether
   the user wants a Zerg entry in their place (it is a desktop app, so
   probably not). In `.gitignore`, drop the `apps/damage-meter/tools/events/`
   line. In the addon's three Lua files only comments change: run
   `python addon-dev/check-apis.py` and
   `python addon-dev/check-lua.py addons/VibeXI/*.lua` afterwards. Search
   again when done; the list is as of 2026-10-04.
4. **Comments in the kept code** that still name the old app or its files:
   `src/Zerg.Core/EventFiles.cs` (line 11), `tests/.../EventFilesTests.cs`
   (line 178), and `src/Zerg/App.xaml.cs` (line 81, the word alone). The
   plan's "What carries over" table promised `EventFiles`' comments would be
   reworded.
5. **Step 7 before step 6**, since the gate searches the docs:
   - **`CLAUDE.md`**: rewrite for the native app. Keep and grow Commands,
     Layout, State on disk (the native rows), "Verifying a change" items 1
     and 5, the traps that are about WPF or the tools. Everything about the
     web host, CDP, the tint measurement and the reference page goes.
   - **This file**: rewrite as the short record the user chose. Keep, in
     Zerg's words: the decisions that still bind (the look and its
     confirmations, 7 and 7a, 10 to 12, 14, 15), the feature checklist as
     what Zerg does, each phase's traps that are about code that still
     exists, and the packaging handoff below. Drop the migration mechanics
     and every pointer at the old app.
   - **Memory**: `zerg-native-host` (the migration is over; say what is
     next), `zerg-naming-rule` (the gate has been run), and
     `colorkey-inert-on-chrome` (about code that no longer exists: delete it,
     or cut it to the one fact that still helps).
6. **Step 6, the gate**: a case-insensitive search of `apps/zerg/` for
   `damage.?meter` and `metrics`, with `bin/`, `obj/` and `dist/` left out,
   finds nothing. Any hit is fixed, not excused. Run it over file names too.
7. **Done when**: `dotnet build apps/zerg/Zerg.slnx` and `dotnet test` are
   green, `Zerg.exe` runs from `dist/Zerg`, there is no WinForms, WebView2 or
   Jint reference in any project file, and the gate is clean. Then tell the
   user how to run it, and that packaging is what is left.

**After N9: packaging** (the old `PLAN.md`'s Phases 4 and 5, which go with
that file, so they are recorded here): a single-file `Zerg.exe`, a zip, a
version resource; then a clean-machine test, SmartScreen, and a README for
players. No WebView2 runtime check is needed any more, and single instance is
built. Ask the user before starting it; they wanted to iterate first.

**Traps for this half.**
- **One `Zerg.exe` at a time.** A dev run beside the user's brings theirs
  forward and exits 0 without a word. Look first (`CLAUDE.md`, "Verifying a
  change", item 2 has the command), and never stop a process you did not
  start.
- **A running `Zerg.exe` locks `src/Zerg`'s build output**, so the solution
  will not build while the user's dev run is up. The test project alone
  still builds.
- **Run `dotnet build-server shutdown`** after scripted builds, and before
  deleting a project folder: a lingering build node holds directory locks.
- **OneDrive holds this repository.** A folder delete can fail on a file it
  is syncing; try again rather than forcing it.
- **The checks write the real `settings.json`.** Copy it aside and put it
  back; the user had the Compare section open and Include Skillchains off
  on 2026-10-04.

---

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| A counting rule drifts in the port, and a number is quietly wrong | **high**: the numbers are the point of the app | Jint parity on every exported function, randomised inputs, mutation check (N1) |
| The redesign quietly drops a control or behaviour | medium | the checklist is the contract; each phase is checked against the reference, interaction by interaction |
| The Fluent theme, still experimental in .NET 10, has gaps or bugs | medium | S1 before any view; restyle individual controls where it falls short |
| Charts take longer than estimated (hover, labels, live edge) | medium | port `chart.js`'s logic rather than invent; S2 first; the chart contingency if a phase is blocked |
| The naming rule is missed somewhere small (a log line, a tooltip) | low | names follow the rule from N0 on; the N9 gate searches everything |
| Players lose their settings at the switch | low, accepted | settings start fresh (Decision 7); old `.json` exports still import (Decision 7a) |
