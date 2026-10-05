# zerg — working notes

Zerg shows a HorizonXI party's damage and healing, live, as a **native
Windows 11 app**: C# / .NET 10 / WPF, `Zerg.exe`. It reads the event files the
VibeXI addon writes from inside the game. A player runs it with no console
and no browser, and its cards can float over the game as see-through panels
that never take the keyboard.

- `RULES.md` — the event contract, and the reason behind each counting and
  session rule. Read it before changing any number Zerg prints.
- This file — everything needed to work on the code day to day.

## Where it sits

```
FFXI ─▶ addons/VibeXI (Lua, in-game) ─writes─▶ %LOCALAPPDATA%\VibeXI\events\<Char>-<YYYYMMDDHHMMSS>.jsonl
                                                            │ tails
                                                      apps/zerg (this)
```

The addon is the only data source, and the bridge is one-way: the addon
appends to a file, Zerg reads it. Zerg uses nothing else in the repository
(not `shared-ui`, not `shared-calc`).

## Working with this user

- **The user handles git.** Don't commit, branch or push unless asked.
- **Verify in the running app**, not just by building. The recipe is under
  "Verifying a change". The user often has FFXI running behind the windows;
  screen captures will include the game.
- **Ask before visual or behavioural choices** that have more than one
  reasonable answer. Mechanical fixes don't need asking. A change the user
  asks for is not a new question.
- **The naming rule.** Zerg is its own product: nothing under `apps/zerg/`
  names the browser app it replaced or the in-game parser its columns are
  modelled on. The check is
  `grep -rniE "damage.?meter|metric[s]" apps/zerg` outside `bin`, `obj` and
  `dist`, which must find nothing (the pattern is spelled so it does not
  match itself).
- **The settings file is theirs.** A check writes the real
  `%LOCALAPPDATA%\VibeXI\zerg\settings.json`: copy it aside first and put it
  back. The user may run Zerg between two of your runs and change a filter;
  read the file before blaming a mismatch on the code.
- **Their Zerg is `dist/Zerg/Zerg.exe`**, run from a desktop shortcut. Only
  one `Zerg.exe` runs at a time, so a dev run beside theirs brings theirs
  forward and exits. Never stop a process you didn't start: ask them to
  close it. Don't publish over `dist/Zerg` unless asked.

## Environment

- **.NET SDK 10.0.401, x64**, in `C:\Program Files\dotnet`. An x86 SDK may
  also be present under `Program Files (x86)`; it is not on PATH and must not
  be used. `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set at user level at the user's
  request. A shell started before that may not have it, so set it in the
  command when running `dotnet`.
- **Python 3.14** on PATH: needed only for `tools/gen-test-events.py` and
  `tools/replay.py`.
- **Windows 11 Home.** **Usually two monitors**: the primary is 3440×1440 at
  (0,0); the second is at x=3440 with a different scale. Not always: list the
  screens before assuming. Both refresh at about 120 Hz.
- The shell tools are Windows PowerShell 5.1 and Git Bash. **Never edit
  source with PS 5.1 `Get-Content`/`Set-Content`**: it reads UTF-8 without a
  BOM as ANSI and mangles non-ASCII. Use the editor tools. PowerShell scripts
  in this repo stay ASCII-only for the same reason.
- **Running a `.ps1` file is blocked** by the execution policy. Run
  PowerShell inline, or pass `-ExecutionPolicy Bypass` for that one process.
  Don't change the policy itself.
- **OneDrive holds this repository.** A folder delete can fail on a file it
  is syncing; try again rather than forcing it.

## Commands

```bash
# all tests (227, under a second once built)
dotnet test apps/zerg/Zerg.slnx

# run it: follows %LOCALAPPDATA%\VibeXI\events, or the folder set on the Settings page
dotnet run --project apps/zerg/src/Zerg
dotnet run --project apps/zerg/src/Zerg -- --events-dir <dir>

# the build the user's desktop shortcut runs (close their Zerg first; empty dist/Zerg first)
dotnet publish apps/zerg/src/Zerg -c Release -r win-x64 --self-contained false -o apps/zerg/dist/Zerg

# play a fixture into the file Zerg is following (press Start first)
python apps/zerg/tools/replay.py <fixture.jsonl> <dir>\<live>.jsonl [--squeeze 60] [--extras]

# the synthetic fixture (tools/events/, ignored by git), or a parse ready to open
python apps/zerg/tools/gen-test-events.py
python apps/zerg/tools/gen-test-events.py --out <dir>\Hasaya_2026.07.30.jsonl
python apps/zerg/tools/gen-test-events.py --export <file>.zerg --seed 7 --date 2026-07-31
```

`Zerg.exe` takes one option, `--events-dir <dir>`: followed for that run
only, over the folder chosen on the Settings page (`settings.eventsDir`) and
without changing it.

The publish gives eight files (`Zerg.exe`, `Zerg.dll`, `Zerg.Core.dll`,
`CommunityToolkit.Mvvm.dll`, two `.pdb`, `Zerg.deps.json`,
`Zerg.runtimeconfig.json`) and needs the .NET 10 Desktop Runtime. It is not
packaging; that is still to do.

After scripted builds run `dotnet build-server shutdown`: a lingering build
node has held directory locks here. A running `Zerg.exe` locks its build
output, so close it before rebuilding; the test project alone still builds.

## Layout

```
Zerg.slnx
src/Zerg.Core/             net10.0, no UI, so it is testable without a window
  EventFiles.cs            newest *.jsonl; open shared R|W|Delete; read whole lines from a byte offset
  EventTail.cs             follows the newest file: offset carried, reset on a file switch or shrink
  EventReader.cs, Roster.cs, Events.cs   lines → damage rows, heals, roster
  Session.cs               the session clock: arm, latch, pause, At/Elapsed, and where a
                           paused clock ends (Snap)
  Counting.cs, Totals.cs   filter, credit, collapse, connects, aggregate, cumulative, distribution
  Healing.cs, Compare.cs   heal totals; A/B measure, diff, pace, and one action's hits
                           (Spread) or one heal's casts (HealSpread) in both runs on shared bins
  CompareSheet.cs          everything the Compare section prints for two runs, as text and bar
                           lengths: tiles, both cumulative lines, every table, who each row is,
                           a change (B - A) with its sign and tone; the session as a parse;
                           one action's or heal's spread (Spread, HealSpread), worked out
                           only when it is opened
  ParseFile.cs             export / import of a paused parse; the name an export is offered under
  Format.cs                numbers and clocks, spelled as a browser spells them for en-US
  Js.cs                    the conventions the numbers and the export text depend on
                           (coercion, stable sort, key order, JSON text)
  Charts/                  where a chart puts things, no drawing: Ticks, LineLayout, BarsLayout,
                           HistogramLayout, PairedHistogramLayout (two runs, a column
                           each per bin), Shapes, SmallLines, Sampling
  Tracker.cs               the live session: file followed, rows, clock, Start / Pause / Cancel
                           (First and Second are the two buttons: over a started session the
                           first press of Start only asks, and the pair is Confirm / Cancel),
                           and Count(), the whole pipeline in one call, damage and healing.
                           Also an opened parse, read only (Tracker.Of), and Export()
  Cast.cs, Shades.cs       colour slots (owner in slot 0), shades for a shared job, hidden-name labels
  SessionView.cs           the two session buttons and the session's wording, as data
  PanelOpacities.cs        how see-through each floating panel is: a default, and a panel's own value,
                           which outranks the default once its slider has been moved
  DrawRate.cs              the draw frequency: how often whatever the clock moves is redrawn
                           (1 to 60 a second, 30 unless set), and the interval that makes
  KeyChord.cs              a key with its modifiers, as text ("Ctrl+Alt+Z"), as Windows wants it,
                           and as pressed on the Settings page (Of)
src/Zerg/                  net10.0-windows WPF exe, Zerg.exe
  App.xaml(.cs)            Fluent theme, shared styles, startup (single instance first), options,
                           crash dialog
  MainWindow               command bar (Start and Pause first, Export… and Import… after them,
                           the gear for the Settings page last), the Damage | Healing |
                           Compare switch on the line under it with the Toggles beside it
                           (Include Skillchains, Hide names, Click-through), character chips
                           (not over Compare), then the section on screen: five tiles and its cards (or
                           the stand-in for one that is floating), the View card over the same
                           tiles and cards, the Compare section, or the Settings page; status
                           line. Registers the hot key (Bind), opens the folder dialog
                           (PickFolder), owns the tray icon
  MainViewModel            holds a Tracker; Recount() on new lines or a filter change, Tick() at
                           the draw frequency.
    (.Damage, .Healing)    One file per section; both sections are drawn on every count
    (.Parse)               Export, Import (the way into the View section: `ImportCommand`), and
                           the View section's parse. `live` is the session and is
                           always fed; `viewed` is the parse open in View; `Shown` is `viewed`
                           while View is on screen with one, else `live`, and is what gets drawn
    (.Settings)            the Settings page: `IsSettings` (the page is a `Section` value that is
                           never saved), the events folder (`Watch`: asks first when a session
                           would be lost, then `EventFeed.Watch` + `Tracker.Unfollow`), the
                           hot key (`BeginHotKey` / `TakeHotKey` / `CancelHotKey`, `Use`), and
                           the draw frequency (`DrawFrequency`)
  CompareViewModel.cs      the Compare section (MainViewModel.Compare): two slots (RunSlot), the
                           switches, and the rows drawn from a CompareSheet. Redraws only on a
                           change, and only while it is on screen. A row opens onto its
                           actions or heals, and each of those (CompareSpreadRow) onto how
                           its hits or casts are spread in each run
  ParseDialog.cs           the Open dialog for an exported parse (*.zerg and older *.json), and
                           the Save dialog for an export
  Rows.cs                  the list rows, kept and updated in place (Rows.Sync)
  Panels.cs                PanelSet / PanelInfo: which cards float, their opacity, the open
                           windows; which were out last time (Reopen, Leave), click-through,
                           Dock all
  PanelWindow              a floating panel: one card in a borderless, always-on-top, see-through
                           window that never takes the keyboard; its bar, tint, sizes and placement
  TrayMenu.xaml            the tray icon's menu
  Views/                   one UserControl per card (LineCard, BarsCard, ActionsCard, DrillCard, and
                           the Healing section's HealLineCard, HealBarsCard, HealActionsCard,
                           HealDrillCard), each with a docked and a floating form switched by
                           Float.On; Away (the stand-in for a floating card), Grow (a bar's eased
                           length), Cells (table-row panel), WheelChain, ActionRowTemplates,
                           HiddenConverter, PresentConverter; ViewCard (the View section's head:
                           open or drop one parse, then Damage | Healing); CompareSection (the
                           whole Compare section) and its cells: RunPair (A over B), ChangeText
                           (B - A), RunBars (a bar per run); SettingsPage (events folder, hot
                           key, the pop-outs' default opacity, the draw frequency, theme; its
                           code reads a key chord off the keyboard)
  EventFeed.cs             250 ms poll on the dispatcher + FileSystemWatcher to poll early;
                           Watch(dir) follows another folder from then on
  Settings.cs              %LOCALAPPDATA%\VibeXI\zerg\settings.json
  AppTheme.cs              light or dark: sets the Fluent theme, merges Themes/Light|Dark.xaml;
                           after a switch, makes a screen reader look at every list again
  Themes/                  Zerg's own colours per theme: the 18 fallback slots, the job colours,
                           the chart surface, the two runs of a comparison
  Charts/                  the chart elements, which only paint: Chart (base), LineChart, BarChart,
                           HistogramChart, PairedHistogramChart (Compare's, in the runs'
                           colours), Models (their inputs), DrawnText (tabular figures)
  Native/                  TaskDialog (TaskDialogIndirect), WindowPlacement (Get/SetWindowPlacement,
                           and a panel's frame in screen pixels), Overlay (tool-window and
                           no-activate styles; a move or resize followed by hand; click-through),
                           SingleInstance (mutex + pipe), HotKey (RegisterHotKey), TrayIcon
                           (Shell_NotifyIcon), Metronome (the draw beat: a high-resolution
                           waitable timer on its own thread, played on the dispatcher)
  Log.cs, Options.cs, AppInfo.cs, EqualsConverter.cs
  app.manifest             PerMonitorV2 + common controls v6
  zerg.ico                 built by tools/make-icon.ps1 from assets/icon-source.webp
tests/Zerg.Core.Tests/     xUnit v2, 227 tests of Zerg.Core: the tail, the tracker (sessions,
                           counting, healing), chart layout, Compare's sheet, export / import,
                           number formatting, key chords, panel opacities, the draw rate
tools/drive.cs             drive the windows: screen grabs, UI Automation, the real mouse (move,
                           click, drag), text fields, Enter and typed keys (for a file dialog), a
                           process's window list, zoom into a grab, a key chord (a global hot key),
                           the tray icon (a file-based app: dotnet run --file)
tools/replay.py            play a fixture into a live event file, re-timed to now
tools/gen-test-events.py   the synthetic session: an event file, or an exported parse
tools/exports/             Paradox_Kirin_4.json: a real 18-character alliance parse, kept as
                           .json on purpose (the proof an older export still opens)
tools/make-icon.ps1        the icon, from assets/
```

## How it works

- **The loop.** `EventFeed` polls every 250 ms (a `FileSystemWatcher` only
  makes a poll come early). New lines go to the `Tracker`, which reads them
  into rows. `MainViewModel.Recount()` then calls `Tracker.Count(excluded,
  skillchains, now)`: one call that filters, credits and totals damage and
  healing and returns a `Snapshot`, from which every tile, chart and table of
  both sections is redrawn. A poll that brought nothing only refreshes the
  status line.
- **The draw beat.** `Tick()` runs at the draw frequency (`DrawRate`: 30 a
  second unless the Settings page says otherwise, 1 to 60) and rewrites
  everything the clock moves and nothing else: the clock, every DPS and HPS,
  and the live edge of the two cumulative charts, the only charts the clock
  moves. Whatever an event moves is redrawn by a count, when the event
  arrives. `Native/Metronome` keeps the beat; the 250 ms poll is the one
  periodic timer left, and it is how events arrive, not how anything is
  drawn. A cumulative chart that is not on screen skips the step and is
  drawn when it comes back. Charts never redraw per frame: that cost a whole
  core on these monitors.
- **Lists.** Rows are objects kept across counts and updated in place
  (`Rows.Sync`, keyed by the character's real name even while names are
  hidden), so a new total rewrites one cell.
- **The Actions and Heals tables start folded**: a heading per character,
  and a character's actions or heals only once their heading is picked
  (`MainViewModel.openActions` and `openHeals`, by real name, kept for the
  run and not saved). A docked card and its pop-out share it; the two
  tables do not.
- **A drill-down goes where its table goes**, and has no panel of its own.
  With the Actions (or Heals) card docked, it is the card under it,
  `DrillCard` (`HealDrillCard`). With the table floating, the main window
  shows no drill-down, and the panel makes one under the picked action's
  row (`Under` in `ActionsCard.xaml`: only while `Selected` and `Float.On`),
  without its heading and with a shorter chart.
- **Charts.** `Zerg.Core/Charts` decides where every mark goes from numbers
  and a text-width function; `src/Zerg/Charts` paints it. A chart is given an
  immutable model: build a new one for each draw. Series colours are the
  caller's, so models are built again on `AppTheme.Changed`.
- **Colours and names.** `MainViewModel.ColorOf` is the one place a
  character's colour is decided, and `NameOf` the one place a name is drawn.
  Panels are always dark, so each coloured thing has a panel twin
  (`PanelColorOf`).
- **Panels.** A panel is a second instance of a card with the same
  `DataContext`. `Views/Float.On`, an inherited attached property the panel
  sets, is the only thing that says "floating"; every difference is a style
  trigger on it. For one element,
  `Visibility="{Binding Path=(v:Float.On), RelativeSource={RelativeSource Self}, Converter={StaticResource Hidden}}"`
  is "docked only" (`Visible` for "floating only"). The window is borderless
  and transparent, topmost, a no-activate tool window, moved and resized by
  hand (`Overlay.Drag`).
- **Sections.** `MainViewModel.Section` is Damage, Healing, View, Compare, or
  Settings (never saved). `IsDamage` and `IsHealing` mean "these tiles and
  cards are on screen": the section of that name, or View showing that side.
- **An opened parse** is a second, read-only `Tracker` (`Tracker.Of`). The
  session goes on being fed while a parse is on screen.

## State on disk

All under `%LOCALAPPDATA%\VibeXI\zerg\`:

| | |
|---|---|
| `logs\zerg.log` | startup, settings and theme changes, file switches, session changes with their exact times, crashes. The first thing to read when something is wrong |
| `settings.json` | filters (`excluded`, `skillchains`, `hideNames`, `groupSmallLines`, `charactersOpen`), `theme`, `section`, `viewMode`, `compareBy`, `compareMode`, panel opacity (`panelOpacity`, the default, set on the Settings page; `panelOpacities`, each panel whose own slider has been moved), `drawFrequency` (how many times a second the clock, every DPS and HPS and a live chart's edge are redrawn, 1 to 60), placement under `windows` (`main`, and `panel:<key>` for each floating panel), `openPanels` (the panels to bring back at the next start), `clickThroughKey` (the hot key, "Ctrl+Alt+Z") and `eventsDir` (the folder the event files are looked for in; absent or null is the addon's own) |

A `zerg.log` at the top of that folder, a `windows.json` and a `WebView2\`
folder may also be there on this machine. They are left over from the build
Zerg replaced; nothing reads them.

## Verifying a change

1. **Tests:** `dotnet test apps/zerg/Zerg.slnx`, 227 tests. They cover
   `Zerg.Core` only. There is no second implementation to compare the
   counting with any more: a change to a counting rule needs its own test,
   and its reason in `RULES.md`.
2. **The running app.** Check first whether a Zerg is already running:
   ```powershell
   Get-CimInstance Win32_Process -Filter "Name like 'Zerg%'" | Select ProcessId, CommandLine
   ```
   If it is the user's, ask them to close it. Zerg only counts what arrives
   after Start, so a fixture in the past has to be played in:
   ```bash
   : > <dir>/Hasaya_2026.10.03.jsonl
   apps/zerg/src/Zerg/bin/Debug/net10.0-windows/Zerg.exe --events-dir <dir> >/dev/null 2>&1 &
   dotnet run --file apps/zerg/tools/drive.cs -- click <pid> Start
   python apps/zerg/tools/replay.py apps/zerg/tools/events/Hasaya_2026.07.30.jsonl <dir>/Hasaya_2026.10.03.jsonl --extras
   # wait ~27 s for the squeezed session to pass, then:
   dotnet run --file apps/zerg/tools/drive.cs -- click <pid> Pause
   ```
   `tools/drive.cs` then reads and drives the window: `front <pid>`,
   `snap <pid> <png>`, `text <pid>` (every piece of text on screen, table
   cells included, one per line), `names <pid>` (controls only, and only
   what is shown), `click <pid> <name>` (buttons, toggles, chips, a
   character's heading in the Actions table as `Actions of <character>`
   and in the Heals table as `Heals of <character>`, which shows or hides
   the rows under it, those rows as `<character>, <action>`, expanders),
   `set <pid> <slider> <value>`,
   `scroll <pid> <percent>`, `hover <pid> <x> <y>` (moves the real mouse
   pointer to a point in a grab's own pixels, for hover cards and tooltips;
   it takes the pointer from the user), and
   `zoom <png> <x> <y> <w> <h> <times> <out>`.
   - The exact session times are in `logs\zerg.log` (`session paused: armed
     …, zero …, paused …, ended …`: the press, and the end of the last
     swing's second, which is where the paused clock is read).
   - **Restart asks first** once the clock has started: `click <pid> Restart`
     turns the pair into `Confirm` and `Cancel` (on every panel bar and in
     the tray menu too, which stays open for the answer), and it goes back
     by itself after 5 seconds. The log says `restart asked`, then `armed`,
     `restart called off` or `restart not confirmed`.
   - Placement and theme live in `settings.json`: edit it between runs to
     test restore (a narrow window, the second monitor, maximized, -32000).
   - **The section** is switched with `click <pid> "Healing section"`,
     `"Damage section"` or `"Compare section"`. The View section has no
     segment: `Import` is its way in (below).
     `replay.py --extras` also writes three pet heals, so the Pet Healing
     column and a healer with no healing of their own have something in
     them.
   - **The Compare section.** A slot is filled through the real Open dialog:
     `click <pid> "Open a parse into A"`, `windows <pid>` for the dialog's
     `h<hwnd>`, then `type h<hwnd> "File name:" <path>` and `enter h<hwnd>`.
     The switches are `"Compare damage"`, `"Compare healing"`,
     `"Compare by character"`, `"Compare by job"`, `"Swap A and B"`,
     `"Use current for A"`, `"Clear A"`; a row opens by its name, and
     an action or heal under it as `<row>, <action>`, onto its distribution; the
     chart's table is `"Data table of both runs"`.
   - **The View section.** `click <pid> Import` (on the command bar) opens
     the Open dialog, as above, and shows the section once a file is
     chosen; with a parse left open under another section it goes back to
     that parse and opens no dialog. A start never opens on this section.
     On its card, `"Open a parse to view"` (`"Open another parse to view"`
     once one is open) opens the same dialog. Its sides are
     `"View damage"` and `"View healing"`; `"Close the viewed parse"`
     empties it.
   - **The Settings page** is `click <pid> Settings` (the gear; again, or
     `"Close settings"`, or any section, to leave). The theme is
     `"Light theme"`, `"Dark theme"`, `"System theme"`. The folder is
     `"Choose the events folder"` (a folder dialog, by its `h<hwnd>`) and
     `"Use the default events folder"`; with a session armed or started a
     task dialog asks first. The hot key is
     `"Change the click-through hot key"`, then `front <pid>` and
     `chord <keys>` while Zerg has the keyboard, and
     `"Use the default hot key"`. The pop-outs' default opacity is
     `set <pid> "Default pop-out opacity" <value>`; a pop-out's own is
     `set h<hwnd> "Panel opacity" <value>`, and from then on the default
     leaves that pop-out alone. The draw frequency is
     `set <pid> "Draw frequency" <value>`. `settings.json` and
     `logs\zerg.log` (`events dir …`, `hot key …`, `panel opacity …`,
     `draw frequency …`) say what was taken.
   - **Export** is `click <pid> Export` (the parse on screen must be
     paused). **A Save dialog ignores `type`** and saves under the name it
     offered, in whatever folder it opened in: use
     `keys h<hwnd> <full path>` and then `enter`, and check where the file
     went. It hides the extension in the name it offers and adds it on
     saving.
   - **`where <target> <name>`** prints the middle of an element in a grab's
     own pixels, for `hover`, `press` and `drag`. A drag may end outside the
     window it began in: that is how a file is dropped from an Explorer
     window onto a slot.
   - **A floating panel is not the process's main window.** `windows <pid>`
     lists every window as `h<hwnd>` with its title, frame and styles
     (`tool noactivate topmost layered` for a panel); use that `h<hwnd>` as
     the target of any other command. Panels are opened with
     `click <pid> "Pop out Cumulative damage"` (or `Damage by character`,
     `Actions`, `Cumulative healing`, `Healing by character`, `Heals`) and
     closed with `click h<hwnd> Dock`. A drill-down has no panel of its
     own: in the Actions or Heals panel, `click h<hwnd> "<character>, <action>"`
     opens it under that row.
   - **Click-through** is `chord Ctrl+Alt+Z` (the real keys, to whatever has
     the keyboard: run `fg` first so they never land in the game),
     `click <pid> "Click-through panels"` in the main window, or the tray
     menu. `windows <pid>` marks a click-through panel `clickthrough`. To
     prove a click passes through, lay the panel over a control of the main
     window (a character chip), `press` the panel there, and read
     `settings.json` for what changed.
   - **Whether a panel takes the keyboard.** UI Automation presses a button
     without a click, so it cannot show this. `press <target> <x> <y>` and
     `drag <target> <x1> <y1> <x2> <y2>` use the real left button, at points
     in a grab's own pixels; `fg` prints the foreground window. Run `fg`
     before and after: it must not become the panel.
   - **The tray icon.** `tray <h> rect` says where the icon is in screen
     pixels. On Windows 11 it starts in the overflow: with that closed the
     answer is the ^ button (48x72 here); `at <x> <y>` on it, wait a second,
     and `tray <h> rect` again gives the icon itself (60x60). Then
     `at <x> <y>` in its middle is a real click, and `at <x> <y> right`
     opens the menu, which is a window of the process with no title
     (`windows <pid>`): `names`, `snap` and `click h<menu> <line>` work on
     it, and the submenu is another such window. `tray <h> menu` opens the
     menu without the mouse, for reading only.
   - **Minimized, Zerg is hidden in the tray**, and `<pid>` no longer finds
     its window: take its `h<hwnd>` first, or from `windows <pid> all`.
3. **`logs\zerg.log`** for what the app thought happened.

## Traps already hit (each cost a debugging round)

**WPF and the Fluent theme**
- **Window placement.** `SetWindowPlacement` is called twice from
  `SourceInitialized` (the first move onto a monitor of another scale gets
  resized by WPF), and `WindowState = Maximized` is set **after** it. Set
  before the handle exists, WPF creates the window maximized on the default
  monitor and it stays there. A record that is off every screen (Windows
  parks a minimized window at about −32000) falls back to a default place.
- **The Fluent theme's styles are found by a control's exact type.** A
  subclass (`TrayMenu`) must name its style
  (`Style="{DynamicResource {x:Type ContextMenu}}"`) or it is drawn in the
  old colours with the theme's ink on top.
- **A style based on `{x:Type ToolTip}` loses the Fluent template** and draws
  the old square box, with no error. Base it on `DefaultToolTipStyle`. A
  tooltip also takes its text alignment from the element it is on, which is
  why `App.xaml` sets it back to left.
- **The Fluent menu's own tick is not used.** One tickable line indents every
  plain line beside it, and `IsChecked` without `IsCheckable` draws no tick.
  `TrayMenu` gives every line a header template with a tick column.
- **Fluent scroll bars are drawn over the content.** Lists that scroll keep a
  14-unit right margin, and sideways-scrolling tables a 12-unit bottom one.
- **The Fluent slider has a minimum width**; `MinWidth="0"` lets it be
  narrow.
- **After a theme switch a list makes its rows again**, and what UI
  Automation was told about the old rows goes stale. `AppTheme.RereadRows`
  drops the remembered children once the windows are laid out again. The
  cause is a reading of WPF's behaviour from its symptoms, not from its
  source; the fix is what was verified.
- **Tabular figures in drawn text need `TextFormatter`.** `FormattedText`
  cannot ask a font for them; `DrawnText` can.
- **A chart's layers are `DrawingVisual`s, hit only where they have ink.**
  `Chart.OnRender` draws a transparent rectangle first so the whole chart
  sees the pointer. A dependency property marked `AffectsRender` is enough
  to redraw a chart.
- **`AffectsRender` redraws a chart nobody can see**, once it has been on
  screen: its section collapsed, or the window in the tray. WPF's render
  thread pays for that redraw as it does for a visible one. `LineChart.Edge`
  is therefore not `AffectsRender`: it invalidates only while `IsVisible`,
  and `Chart` redraws on `IsVisibleChanged`.
- **A redraw of a cumulative chart costs about half a percent of one core
  per draw a second**, three quarters of it on WPF's render thread, whatever
  the chart's size, and with or without the lines' `BitmapCache` (measured,
  Release). The chart's own `Paint` is 0.1 to 0.2 ms of that.
- **WPF lays out text it is not showing.** A DPS cell in a collapsed
  section, or in a window hidden in the tray, costs the UI thread the same
  as one on screen when its text changes. Hence `MainViewModel.Seen`, which
  the main window sets: off screen, the beat leaves DPS and HPS alone, and
  a count still rewrites them.
- **What the draw frequency costs** (measured, Release, one core, a session
  running, no UI Automation client): at 30 a second, 23 to 25% with the
  Damage section on screen (the chart about 15, the figures about 10); 10%
  with only the cumulative pop-out up; 2% on the Settings page, or with the
  bars pop-out up and the main window in the tray. At 4 a second, 2 to 3%
  with the Damage section on screen.
- **A UI Automation client makes every layout dearer** from then on (WPF
  keeps its peers up to date), so a cost measured after `drive.cs` has read
  the window is too high. `front`, `snap` and `press` do not use it.
- **A hairline is one device pixel on a pixel centre.** At 150 % a one-unit
  line is a blurred pixel and a half.
- **A row is rounded up to whole pixels.** At 150 % a 20-unit row with a
  1-unit gap is 32 pixels, not 31.5. A panel's opening height allows for it.
- **A computed property on `Settings` needs `[JsonIgnore]`** or it is saved.
  `System.Text.Json` escapes "+" unless the relaxed encoder is used, which
  is why the settings use it.
- **A `DispatcherTimer` cannot keep a rate faster than about 16 a second.**
  Windows rounds its waits up to the 15.6 ms system clock: asked for 30 a
  second it gave 21.3, and for 60, 39.9 (measured). Four a second is exact.
  Anything faster goes through `Native/Metronome`.
- **`TASKDIALOGCONFIG` is byte-packed** (160 bytes on x64). Dispatcher timers
  do not fire while a task dialog is open, so the feed pauses behind one.

**Panels and the shell**
- **`DragMove`, and answering a hit test as the caption or an edge, both
  activate the window**, in spite of `WS_EX_NOACTIVATE`. Hence
  `Overlay.Drag`, which needs the mouse capture. **If the capture fails,
  drop the drag**, or the panel follows the pointer for good.
- **A menu opened from the tray must be made the foreground window**
  (`TrayIcon.Front`), or it stays up when the player clicks elsewhere.
  Windows allows that only in answer to a real click on the icon.
- **A second launch must pass on the right to the foreground**
  (`AllowSetForegroundWindow`) before it signals the first, or the first
  only flashes its taskbar button.
- **A hidden main window is not the process's main window.**

**UI Automation and `drive.cs`**
- **UI Automation changes a toggle, check box or radio button without
  clicking it.** A `Click` handler or a `Command` on a `ToggleButton` never
  runs: bind `IsChecked` and act on the change.
- **Invoke gives a button the keyboard focus, and WPF shows a focused
  control's tooltip.** A tooltip in a grab after `click` is that. With the
  pointer resting on something that has a tooltip, `<pid>` can address the
  tooltip itself (a tiny `snap`, an empty `text`): use the window's
  `h<hwnd>`. A tooltip is topmost, no-activate and layered like a panel, so
  pick a panel by its title.
- **Pressing a row through UI Automation scrolls its list to it**; a real
  click does not.
- **An `ItemsControl` names each row by the item's `ToString()`.** Rows
  return the name as drawn. `AutomationProperties.Name` on a `TextBlock`
  replaces its text for UI Automation, so a value given a name cannot be
  read by a script. A toggle whose content is a panel needs a name given.
- **`front` does not bring Zerg back from the tray properly.** It shows the
  window behind WPF's back, which goes on thinking it is hidden: nothing is
  "on screen" to UI Automation. Come back as a player does, with
  `tray h<hwnd> pick`.
- **Hidden and collapsed elements are still read out** by `text` (a hidden
  section's last contents, a hidden copy of a table). `names` lists only
  what is shown.
- **The one "Data table" expander is Compare's**, and `click` knows it as
  "Data table of both runs". **`Include Skillchains` is among the Toggles
  and on the Compare card**; `click` finds the Toggles' first, and either
  flips the one setting.
- **Panels are topmost and cover each other.** A scripted press lands on
  whichever is on top: move them apart first.
- **`tray <h> menu` is for reading only**: that menu cannot take the
  foreground, and with Zerg's own window in front it closes at once.
- **Windows does not remember a folder under `%TEMP%`** for a file dialog.

**This machine**
- **A dev run does not start beside a running Zerg.** It brings the running
  one forward and exits 0, silently. If a change seems to have no effect,
  look for an older `Zerg.exe` first.
- **When the user is playing, the game keeps taking the foreground back**:
  `front` fails, grabs show the game, and the tray menu will not stay open.
  Do the on-screen checks in few, short runs.
- **Screen captures and window moves from PowerShell are DPI-unaware.** On a
  scaled monitor a grab takes the wrong region, and `MoveWindow` lands half
  as far again at 150 %. `drive.cs` is per-monitor aware; use it.
- **A background `Zerg.exe` started inside a shell pipeline keeps the
  pipeline open**: redirect its output. `Process.CloseMainWindow` from a
  PowerShell one-liner inside Git Bash did nothing once: check the log for
  `exit` before rebuilding.
- **A Bash command of more than about 8,000 characters arrives cut short.**
  Write a long script to a file and run the file.
- **A `\u` escape written through the editor tools arrives as the raw
  character** (a raw U+2028 broke the C# lexer). Write `(char)0x2028`, or
  `\p{Zl}` in a regex.
- **.NET 10 file-based apps** (`dotnet run --file x.cs`) have reflection-based
  JSON off. Use `JsonObject`, not anonymous types.

## Status and what's next

**The native build is finished and signed off** (2026-10-05). The browser
build it replaced, its host and the tests that compared the two are deleted.
`dist/Zerg/Zerg.exe` is the native app, and the user's desktop shortcut runs
it.

**Next is packaging**, which has not been started and waits for the user's
word: a single-file self-contained `Zerg.exe`, a zip, a version resource;
then a clean-machine test, SmartScreen, and a README for players.

Open ideas, none started:
- **Opening a `.zerg` file by double-click** (a per-user file association,
  handed to the running instance).
- **Signing** the exe.
- **A reset** for a pop-out's own opacity. The user was offered one and
  chose not to have it.
