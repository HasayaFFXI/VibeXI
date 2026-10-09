# zerg — working notes

Zerg shows a HorizonXI party's damage and healing, live, as a **native
Windows 11 app**: C# / .NET 10 / WPF, `Zerg.exe`. It reads the event files the
VibeXI addon writes from inside the game. A player runs it with no console
and no browser, and its cards can float over the game as see-through panels
that never take the keyboard.

- `RULES.md` — the event contract, and the reason behind each counting and
  session rule. Read it before changing any number Zerg prints.
- `CHANGELOG.md` — what each version was. "Versions", below, says how the
  number is kept.
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
  screens before assuming. Windows reports 174 Hz for the primary and
  240 Hz for the second (2026-10-05). On the morning of 2026-10-06 there
  was one screen only, 2560×1600 at 150 %: a window 1440×900 pixels then
  holds 960×600 units, and a grab of it does not compare with one taken at
  100 %. That evening there were two again: the primary at 100 %, and the
  2560×1600 one at 150 % at (3440,−158). On 2026-10-07, one again:
  2560×1600 at 150 %, its work area 1528 pixels tall; and by that
  evening the two, as the evening before. A scratch program that draws
  without a window draws at the primary's scale, so its pictures and
  its figures changed with it. On 2026-10-08, the one again, at 150 %,
  all day: the redesign's last phase was checked on it and on nothing else.
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
# all tests (709, under a second once built). Naming the solution builds the
# app too; beside a running Zerg, name the test project instead
dotnet test apps/zerg/Zerg.slnx
dotnet test apps/zerg/tests/Zerg.Core.Tests

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
output, so close it before rebuilding. Beside one that cannot be closed,
the test project alone still builds (`dotnet test` of the project, not of
the solution, which builds the app as well), and the app builds into
another folder (`dotnet build apps/zerg/src/Zerg/Zerg.csproj -o <folder>`),
though it cannot be started from there either while the other runs.

## Versions

Zerg's version is written in one place, and not here:
`<Version>` in `apps/zerg/Directory.Build.props`, which MSBuild applies to
every project under `apps/zerg`: `Zerg.exe`, `Zerg.dll` and `Zerg.Core.dll`
all carry it as their file and product version (Explorer's Details tab
shows it). `AppInfo.Version` reads it back out of the running assembly;
the first line of each run in the log says it (`---- Zerg 0.2.0 starting`),
and the status line ends with it, in the window's lower right-hand corner
(`v0.2.0`, `MainViewModel.StatusVersion`; `drive.cs`'s `text` prints it).
Nothing else may hold a copy: a second place to update is a place that
goes stale.

It is `major.minor.patch`, and while the major is 0:

- **minor** for anything a player would notice as new or different: a
  feature, a changed layout, a counting rule, a settings key, a newer
  `.zerg` format;
- **patch** for a fix that makes Zerg do what that version already claimed.

Refactors, tests, tools and these notes move nothing. 1.0.0 is the owner's
to call.

Three other numbers in the repository are not this one and move on their
own: the addon's `addon.version` in `addons/VibeXI/vibexi.lua` (written to
each event file's `meta` line as `v`), `ParseFile.Version` (the `.zerg`
format, a whole number, raised only when an older Zerg could not read the
file), and `assemblyIdentity version` in `app.manifest` (Windows' side by
side identity; leave it at 1.0.0.0).

**Cutting a version** (the git steps are the user's):

1. Change `<Version>` in `Directory.Build.props`.
2. In `CHANGELOG.md`, turn "Unreleased" into `## <version> — <date>` and
   leave an empty "Unreleased" above it. A change worth a line goes under
   "Unreleased" when it is made, not at the cut.
3. `dotnet test apps/zerg/Zerg.slnx`.
4. Commit, and tag that commit `zerg-v<version>` (`zerg-v0.2.0`). The
   prefix is there because the repository holds the addon too, which has
   its own versions.
5. If the user asks for it, publish over `dist/Zerg` (the command is
   above).

0.1.0 was never tagged. `dist/Zerg` is 0.2.1, published on 2026-10-08: the
Target and Type filters. A patch number by the owner's call, though they
are a feature and the rule above would have made them a minor.

## Layout

```
Directory.Build.props      the version, for every project here
Zerg.slnx
src/Zerg.Core/             net10.0, no UI, so it is testable without a window
  EventFiles.cs            newest *.jsonl; open shared R|W|Delete; read whole lines from a byte offset
  EventTail.cs             follows the newest file: offset carried, reset on a file switch or shrink
  EventReader.cs, Roster.cs, Events.cs   lines → damage rows, heals, roster
  Session.cs               the session clock: arm, latch, pause, At/Elapsed, and where a
                           paused clock ends (Snap)
  Counting.cs, Totals.cs   filter, credit, collapse, connects, aggregate, cumulative, distribution;
                           the party's own split columns for the Party line (Aggregate.Party,
                           by Split.Figures, which a character's row uses too)
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
  Charts/                  where a chart puts things, no drawing: Ticks, LineLayout (the
                           cumulative chart: the plot, both axes, the markers, and the
                           names at the lines' ends, with a total where there is room,
                           the leader, a line back to the marker for a name that was
                           moved; Retime, for an edge that stepped),
                           HistogramLayout (columns, the mean's rule, the band of the
                           middle half, the median's tick), PairedHistogramLayout (two
                           runs, a column each per bin), Shapes (a box, a tick, a hover
                           card's place and, in HoverCard.Arrange, everything in one),
                           SmallLines, Sampling; and BandMarks, the
                           same for the main window's bands: the marks beside three of the
                           figures (the amount per 20 seconds, doubled in a long session;
                           the running rate; the party's shares as one cut bar), how many
                           figures stand in a row (and how many of Compare's six), how
                           many chips a line holds
  Layout/SplitTree.cs      how a section's panes are arranged, no drawing: a tree of splits
                           (SplitNode: PaneSplit, a room cut in two by a ratio, or PaneLeaf,
                           a pane by its key), and SplitTree.Arrange, which turns a tree and
                           a room into a rectangle per pane and a rule per split, keeping
                           every pane to a least size, giving a folded pane or a
                           stand-in its own height, and folding a pane that stands alone
                           in its column sideways, to a rail; Least, Keys, At; PaneLayouts,
                           the arrangement each section is installed with (Damage, Healing,
                           Compare)
  Layout/SplitTree.Edits.cs  a tree changed, each change a function that returns another
                           tree: a rule moved (SetRatio; Drag, which stops at the panes'
                           least sizes), a pane folded, moved beside another, put on an
                           edge of the section, swapped; the neighbour on a side; a saved
                           tree made fit for this build (Repair)
  Layout/SplitTree.Json.cs   a tree as settings.json has it, and read back from whatever
                           is there (PaneLayouts.Read, Write)
  Layout/PaneDrops.cs      where a pane carried by its heading can be put down: the five
                           places on another pane, the section's edges; which one a point
                           is in, and the tree that goes with it
  Layout/Columns.cs        a table's columns, no drawing: written as text ("278*150,50*40"),
                           a number is units, a star a share of what is left, a second
                           number the least that share may be; Resolve gives each column
                           its width in a row, Least the narrowest the row can be, Sheds
                           whether a table in a room has to drop columns
  Layout/PanelFit.cs       a floating panel's measures, no drawing: its bar, a strip's
                           rows, the least width; how tall a strip's panel opens for so
                           many rows on a screen of a given scale, worked out in whole
                           pixels (StripHeight); from what width the bar keeps its title
                           beside the tools (TitleStays)
  RowShade.cs              how strong a row's shade may be: the largest percentage, up to
                           a cap, that leaves the quieter ink legible on the colour; the
                           same in a floating panel, against the panel's own backdrop and
                           ink (Strip, for a strip's line; PanelHeading, for a heading in
                           a table's panel); and a run's band in Compare by the same rule
                           with two inks, the red of a worse change and the green of a
                           better one: as strong as both allow
  RunColours.cs            the colours of Compare's two runs, as rules: how one is written
                           ("#3987E5") and read back, how strong its band is behind a row,
                           and which ink the letter on its badge takes
  Hsv.cs                   a colour as the colour picker holds it: hue, saturation and
                           brightness, to red, green and blue and back; Keep, so a marker
                           dragged to black or gray and back comes back to its hue
  LowMark.cs               the low accuracy mark: a rate under the threshold as four
                           strengths (a rule, the cut-out's surface, wash and outline)
  RowMarks.cs              the lengths of the marks in a row: a share out of the largest
                           of its siblings, an action's least, average and greatest hit
                           out of the character's biggest, a hit's distance from the average
  Tracker.cs               the live session: file followed, rows, clock, Start / Pause / Cancel
                           (First and Second are the two buttons: over a started session the
                           first press of Start only asks, and the pair is Confirm / Cancel),
                           and Count(), the whole pipeline in one call, damage and healing.
                           Also an opened parse, read only (Tracker.Of), and Export()
  Targets.cs               isolating targets: what each target took (Totals), the order they
                           are picked from (Sorted: alphabetical), and how what is picked is
                           said ("Kirin +2", "Damage to Kirin"). The filter itself is a line
                           of Counting.Counted; Snapshot.Rate is why no DPS is given meanwhile
  DamageTypes.cs           isolating damage types, the same along another cut: the type a
                           row is counted under (Of: its kind, a pet's rows all "pet"),
                           what each is called, their totals and order, and the label over
                           a total whatever is isolated (Heading: "Melee damage to Kirin")
  Cast.cs, Shades.cs       colour slots (owner in slot 0), shades for a shared job, hidden-name labels
  SessionView.cs           the two session buttons and the session's wording, as data; the
                           word in the state tag (SessionTag: the state, or Saved over an
                           opened parse)
  PanelOpacities.cs        how see-through each floating panel is: a default, and a panel's own value,
                           which outranks the default once its slider has been moved
  DrawRate.cs              the draw frequency: how often whatever the clock moves is redrawn
                           (1 to 60 a second, 30 unless set), and the interval that makes
  KeyChord.cs              a key with its modifiers, as text ("Ctrl+Alt+Z"), as Windows wants it,
                           and as pressed on the Settings page (Of)
src/Zerg/                  net10.0-windows WPF exe, Zerg.exe
  App.xaml(.cs)            ThemeMode="None": no theme of Windows' is loaded. The fonts, the
                           measures (heights, radii), the type styles, the session pair
                           (StartButton, SecondButton and what is on them) and the other
                           shared styles; merges Themes/Controls.xaml; startup (single
                           instance first), options, crash dialog
  MainWindow               a title bar of Zerg's own, 36 units (the mark, the sections as
                           tabs: Damage, Healing, Compare, and View while a parse is open
                           or that section is on screen; the gear for the Settings page;
                           minimize, maximize or restore, close); the command bar, one
                           line (Start and Pause, Export… and Import…, the toggles Include
                           Skillchains, Hide names and Click-through, what the last Export
                           did, the layout button, whose menu locks the panes' arrangement
                           or sets it back); over the View
                           section, its band (the parse open there), which never scrolls;
                           then the page (Body): the band of five figures (three with a
                           small mark, the clock with the state tag), the characters as
                           chips, and the section: the Damage or Healing section's four
                           panes in two columns, each scrolling what it holds (a pane whose
                           card is floating is the short stand-in for it); or the Compare
                           section's own bands and its four panes; or the Settings page.
                           Over the panes, in a window 700
                           units wide or more, the page is as tall as the window leaves it
                           and does not scroll; narrower, the panes stand in one column
                           and the figures and the characters scroll with them; status
                           line (the dot, the file, the detail; with a card floating, how
                           many are out and Dock all). The code makes the WindowChrome
                           (TakeTitleBar), keeps a maximized window's content on the screen
                           (Fit), registers the hot key (Bind), opens the folder dialog
                           (PickFolder), owns the tray icon, and hands the foreground on
                           if a panel is left holding it after the tray menu closes or
                           after a start (HandOn), opens the layout button's menu
                           (OnLayout), and hands what a section's panel asks for (another
                           arrangement, a sentence for the status line) to the view model
  MainViewModel            holds a Tracker; Recount() on new lines or a filter change, Tick() at
                           the draw frequency.
    (.Damage, .Healing)    One file per section; both sections are drawn on every count
    (.Layout)              each section's tree of panes (DamageLayout, HealingLayout,
                           CompareLayout), read from the settings and saved to them;
                           Rearrange, which a section's panel reaches through the window
                           when a player has made another arrangement; the lock
                           (LayoutLocked); what the layout button's menu does
    (.Parse)               Export, Import (the way into the View section: `ImportCommand`), and
                           the View section's parse. `live` is the session and is
                           always fed; `viewed` is the parse open in View; `Shown` is `viewed`
                           while View is on screen with one, else `live`, and is what gets drawn
    (.Settings)            the Settings page: `IsSettings` (the page is a `Section` value that is
                           never saved), the events folder (`Watch`: asks first when a session
                           would be lost, then `EventFeed.Watch` + `Tracker.Unfollow`), the
                           hot key (`BeginHotKey` / `TakeHotKey` / `CancelHotKey`, `Use`),
                           the draw frequency (`DrawFrequency`), how rows are shaded
                           (`ShadeCharacters`, `ShadeActions`), the low accuracy threshold
                           (`LowAccuracy`), the colours of Compare's runs (`RunA`, `RunB`;
                           what a well reads: `RunACode`, `RunABand`, ...); `Settle`, which
                           puts a save off until a slider or the picker has stood still;
                           and what the page's pictures are drawn in (`SampleShade1`, ...)
  CompareViewModel.cs      the Compare section (MainViewModel.Compare): two slots (RunSlot), the
                           switches, and the rows drawn from a CompareSheet. Redraws only on a
                           change, and only while it is on screen. A row opens onto its
                           actions or heals, and each of those (CompareSpreadRow) onto how
                           its hits or casts are spread in each run. Also what its panes
                           are called, and Recolour: the chart of both runs made again
                           when a run's colour changes, and nothing else
  ParseDialog.cs           the Open dialog for an exported parse (*.zerg and older *.json), and
                           the Save dialog for an export
  Rows.cs                  the list rows, kept and updated in place (Rows.Sync), each with
                           its shade and how long it is; the two Party lines
  PickFilter.cs            what damage is isolated to along one cut, and the list it is
                           picked from (PickRow): what a pick button and its menu are
                           drawn from. Two kinds, PickFilter.OfTargets (TargetFilter)
                           and PickFilter.OfTypes (TypeFilter, damage types), and one
                           of each on MainViewModel (the session's, or the viewed
                           parse's) and on CompareViewModel (Compare's own). Never
                           saved; its owner empties it (Reset)
  Panels.cs                PanelSet / PanelInfo: which cards float, their opacity, the open
                           windows; which were out last time (Reopen, Leave), click-through,
                           Dock all
  PanelWindow              a floating panel: one card in a borderless, always-on-top, see-through
                           window that never takes the keyboard; its bar (the pair, the
                           clock, the total, the card's name; the opacity slider and Dock
                           only while the pointer is over the panel: Chrome), tint, halo,
                           the card's margins, sizes and placement
  TrayMenu.xaml            the tray icon's menu: while a session is armed or counting, a
                           heading (the mark, the clock and the total, the state tag) that
                           is not a line to choose; then the lines
  Views/                   one UserControl per card (LineCard, BarsCard, ActionsCard, DrillCard, and
                           the Healing section's HealLineCard, HealBarsCard, HealActionsCard,
                           HealDrillCard), each a Pane with a docked and a floating form
                           switched by Float.On; Pane (a region of a section with a
                           30-unit heading: grip, title, note, tools, fold mark, Pop out;
                           folded, it is its heading; to UI Automation, a group named
                           by its title; the grip and the fold mark are controls, which
                           it names: "Move Actions", "Fold Actions"); SplitPanel (a
                           section's panes, each child put in the rectangle the
                           section's tree gives the pane whose key it carries, the rules
                           painted between; never takes a child out and puts it back,
                           and says each pane's place in the order they stand in as its
                           Panel.ZIndex, which is the order the Tab key and a screen
                           reader then meet them in;
                           ComparePanes is one, of a class of its own. Four files:
                           SplitPanel.cs arranges; SplitPanel.Dividers.cs is the thumb
                           over each rule, DividerThumb, dragged, set back by a
                           double-click or by Enter, moved by the arrow keys, with a
                           value to UI Automation; PaneDrag.cs is a pane carried by its heading, and
                           PaneGrip, the grip, a button that lets a drag through;
                           SplitPanel.Menu.cs is a heading's menu), LayoutOverlay (what
                           is drawn over the panes while one is carried or a rule is in
                           the hand: the places, the card, the readout); PageStack (two
                           classes:
                           PageView, the page's scroll viewer, which tells its content how
                           tall the window leaves it, and PageStack, the content, which
                           makes the section fill that or lets the page scroll, and says
                           when panes are to stand in one column); Away (the stand-in for
                           a floating card: a 46-unit dashed strip), Grow (a bar's eased
                           length; in a floating panel, its length at once), Cells (table-row panel, on Zerg.Core's Columns),
                           Shading (two classes: Shading, how a card's rows are drawn,
                           said once on the card and inherited: characters shaded,
                           actions shaded, the low accuracy threshold; and Shed, which
                           says a table is in a room too narrow for all its columns),
                           RowMarks (the marks in a row, each one element that paints
                           itself: LowMark behind a rate, SmallBar beside a share, Spread
                           for an action's least, average and greatest hit, Deviation
                           for one hit against the average, RunMarks for which run each
                           line of a Compare row is), Caps (a label in capitals with
                           room between the letters: over each figure, and the
                           characters' count), StateTag (the pill beside the clock: LIVE,
                           ARMED, HELD, IDLE, SAVED; paints itself), FigureBand (the five
                           figures' panel: equal cells, the rules between them, three in a
                           row or two in a narrow window; with Six, Compare's band of six),
                           ChipLine (two panels: ChipLine,
                           the characters' line, and ChipFit inside the chips' list, which
                           shows as many as fit on one line or wraps them all), BarWrap (the
                           command bar's panel: wraps group by group, drops a rule that
                           would begin or end a line, sets the note against the right-hand
                           end; the View band's line too), Room (the width of a word in
                           another weight, unseen: keeps
                           a tab as wide picked as not), WheelChain,
                           ActionRowTemplates,
                           HiddenConverter, PresentConverter; ViewCard (the View section's
                           band: one line about the parse open there, with Damage |
                           Healing; or the place to open or drop one); CompareSection (the
                           Compare section's bands: what to compare, the two slots, six
                           figures), ComparePanes (its four panes: a SplitPanel with them
                           written into it) and their cells: RunPair (A over B), ChangeText
                           (B - A), RunBars (a small bar per run, while rows are not
                           shaded); SettingsPage (one ruled row to a setting: events
                           folder, hot key, the pop-outs' default opacity, the draw
                           frequency, row shading, the low accuracy mark, theme, Compare
                           colours; its code reads a key chord off the keyboard and opens
                           and closes the colour picker; SettingRow, in the same file, is
                           a row's panel: the words and the control side by side, or one
                           under the other in a narrow window), ColourPicker (a square, a
                           strip, a swatch, the colour's code: part of the page, not a
                           window) and ColourField (two classes: ColourField, the square,
                           and HueStrip; each paints itself, follows the pointer, takes
                           the keyboard and has an automation peer), Sample (a picture on
                           the Settings page of what a setting does: one named image to
                           UI Automation, with nothing inside it), ChordKeysConverter
                           (and PercentConverter); PickButton (a pick button, "Target"
                           or "Type" as its PickFilter says: one bordered piece, the
                           word, what is picked and a chevron, which open its menu, and
                           while anything is isolated a mark that clears; the menu is a
                           context menu of its own style, made of the filter's rows
                           each time it opens, which stays open while lines are chosen)
  EventFeed.cs             250 ms poll on the dispatcher + FileSystemWatcher to poll early;
                           Watch(dir) follows another folder from then on
  Settings.cs              %LOCALAPPDATA%\VibeXI\zerg\settings.json
  AppTheme.cs              light or dark (ThemeChoice: System, Light, Dark): merges
                           Themes/Light|Dark.xaml and nothing else; Job, Series and Token hand
                           code a colour by its key; and makes the six brushes Compare's
                           two runs are drawn with, from the colours set or installed
                           (Runs, Run, RunBand), replaced in one step
  Themes/                  Zerg's own colours and Zerg's own controls. Dark.xaml and
                           Light.xaml, one merged at a time: the tokens (Bg0 to Bg4, Line to
                           Line3, Text1 to Text4, Accent, Live, Crit and what is written on
                           them, tag tints, overlays), the 18 fallback slots, the job colours,
                           the chart surface, the installed colours of a comparison's two
                           runs (colours only: AppTheme makes their brushes). Panel.xaml: the
                           tokens as a floating panel has them, merged into PanelWindow's own
                           resources. Controls.xaml: the look of every stock control (button,
                           ghost, filled and icon buttons, tick-box toggle, segment and its
                           frame, section tab and the mark that closes one, the caption
                           buttons, a link, slider, scroll bar and viewer, tooltip, text
                           box, expander, the expander over a drill-down's list
                           (HitsExpander), menu, keycap, the focus rings), of a pane
                           (ZPane, with its tall heading and its rail) and of what a pane
                           is arranged with (PaneGrip, PaneFold, Divider), each under a
                           key and, at the
                           end, by control type; merged in App.xaml
  Charts/                  the chart elements, which only paint: Chart (base: the inks,
                           the face, drawn text, hairlines, the hover card and its
                           shadow; and what a chart is to UI Automation, one picture
                           with a name and a line about what it shows), LineChart (five
                           layers, two of which stand between counts),
                           HistogramChart, PairedHistogramChart (Compare's, in the runs'
                           colours), Models (their inputs), DrawnText (tabular figures; the
                           same line with room between its letters, for Caps and StateTag),
                           Spark (the marks in the band of figures: Spark, thin bars or a
                           line, and ShareBar; not charts, and painted only when given
                           something new)
  Native/                  TaskDialog (TaskDialogIndirect), WindowPlacement (Get/SetWindowPlacement,
                           and a panel's frame in screen pixels), Overlay (tool-window and
                           no-activate styles; a move or resize followed by hand; click-through),
                           SingleInstance (mutex + pipe), HotKey (RegisterHotKey), TrayIcon
                           (Shell_NotifyIcon), Metronome (the draw beat: a high-resolution
                           waitable timer on its own thread, played on the dispatcher),
                           WindowFrame (the edge Windows 11 draws round the main window, in
                           Zerg's colour; how far a maximized window hangs over its screen),
                           InFront (which window has the foreground, and giving it to
                           another: for the main window to take it off a panel)
  Log.cs, Options.cs, AppInfo.cs, EqualsConverter.cs
  app.manifest             PerMonitorV2 + common controls v6
  zerg.ico                 built by tools/make-icon.ps1 from assets/icon-source.webp
tests/Zerg.Core.Tests/     xUnit v2, 709 tests of Zerg.Core: the tail, the tracker (sessions,
                           counting, healing, the Party line), chart layout (ChartLayoutTests:
                           ticks, the names at a line's end, a stepped edge, the hover
                           card, the histograms), Compare's sheet, export / import,
                           number formatting, key chords, panel opacities, the draw rate, the
                           bands' marks (BandMarksTests: the bucket, the bars, the share bar,
                           how many chips fit a line, how many figures stand in a row), the
                           panes' arrangement (SplitTreeTests: the installed trees against
                           the design's sheets, least sizes, a folded pane and a stand-in,
                           rooms too small, whole pixels, the rail; SplitEditsTests: a rule
                           moved, a pane folded, moved, swapped and put on an edge, the
                           least sizes after any of it, a saved tree repaired, written and
                           read back, where a carried pane lands), a table's columns
                           (ColumnsTests:
                           the sheets' widths, leasts, a column gone, when each table
                           drops which), a floating panel's measures (PanelFitTests: a
                           strip's panel never a pixel short of its rows at any scale),
                           the row shade (RowShadeTests: every job colour
                           of both themes, and as a panel has them), the low accuracy mark (LowMarkTests: the
                           marks the sheets draw), the marks' lengths (RowMarksTests),
                           the runs' colours (RunColoursTests: the band in both themes,
                           held to the red and to the green; the badge's ink; a colour
                           as written), a colour as hue,
                           saturation and brightness (HsvTests: there and back, the
                           sheet's markers, what a gray and black keep), isolating
                           targets (TargetsTests: what is counted, an area attack, the
                           clock left alone, no rate, the list and its order, the words,
                           a compared run and its sheet), isolating damage types
                           (DamageTypesTests: a row's type, what is counted in a session
                           and in a compared run, the rate kept, each list of what the
                           other filter leaves, the sheet)
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
  moves. The rates are rewritten only where they can be seen: the Damage
  section's while that section is on screen, the Healing section's while
  that one is, and neither while the window is in the tray or shows
  Compare or the Settings page (WPF lays out text it is not showing). A
  count rewrites both, and changing section is a count. Whatever an event
  moves is redrawn by a count, when the event arrives. `Native/Metronome` keeps the beat; the 250 ms poll is the one
  periodic timer left, and it is how events arrive, not how anything is
  drawn. A cumulative chart that is not on screen skips the step and is
  drawn when it comes back; one that is on screen draws its lines and its
  time labels again and leaves the rest standing (see "Charts"). Charts
  never redraw per frame: that cost a whole core on these monitors. The beat also sets `Tag`, the word in the state
  tag, which is a change only when the session's state changes.
- **The bands.** Under the command bar, the window's whole width: the
  View section's band (`Views/ViewCard`), which never scrolls, and, at
  the top of the page, the five figures (`Views/FigureBand`) and the
  characters' line (`Views/ChipLine`), which stand still in a window wide
  enough for two columns of panes and scroll with the page in a narrower
  one (see "The page"). In the band of figures only the clock and the rate
  are rewritten on the beat. The state tag (`Views/StateTag`) is painted
  when the state changes. The three marks (`Charts/Spark`: the party's
  damage per 20 seconds, the running DPS, everyone's share) are given new
  numbers by a count (`DrawTiles`, `DrawHealTiles`, from
  `Zerg.Core/Charts/BandMarks`) and painted only then; between two events
  they stand still while the rate beside them falls. A count that leaves a
  mark's numbers as they were hands it the list it already has, and it is
  not painted. The characters' line is one line of as many chips as fit,
  with a chip saying how many do not ("+5"), or every chip on as many
  lines as that takes: `charactersOpen`, which that chip flips (one line,
  as installed).
- **The page.** `Body`, between the command bar (or the View band) and the
  status line, is a `Views/PageView`: a scroll viewer that tells what it
  holds, a `Views/PageStack`, how tall the window has left it. The stack's
  children are the figures, the characters and the section, one under
  another. Over the Damage and Healing sections (`Fill`, bound to
  `ShowsParse`), in a window at least `NarrowWidth` (700 units) wide, the
  section is given exactly what the others leave: the page is as tall as
  the window has room for and does not scroll, so the two bands stand
  still. Under that width (or under what the section's arrangement needs
  side by side, where a player has made one wider than the installed two
  columns: `PageStack.NeedsWidth`, which the panes' panel says) the stack
  says so to everything in the page (`SplitPanel.Stacked`, inherited),
  every child is as tall as it asks to be, and the page scrolls, bands and
  all. The Compare section fills
  the window in the same way (`Fill` is bound to `FillsPage`: a parse's
  panes, or Compare's): its own bands are a child of the stack, where
  the figures and the characters are over the live sections. The
  Settings page is always as tall as it asks to be. One instance of each
  band either way: nothing is moved between two places. A section whose
  least height changes while the page stands (a drill-down opening, a
  card floated or brought back) tells the page, which measures again
  (`PageStack.OnNeedsChanged`).
- **Panes.** A section's cards are panes (`Views/Pane`: a heading and what
  it holds), flush against each other in a `Views/SplitPanel`, which puts
  each where the section's tree of splits says (`MainViewModel.DamageLayout`
  and `HealingLayout`; the arithmetic is `Zerg.Core/Layout/SplitTree`).
  A child of the panel is a `Grid` carrying a pane's key
  (`v:SplitPanel.Key="bars"`) and holding the card and its stand-in. The
  panel only changes the rectangles its children are arranged in: no
  child is ever taken out and put back, so a new arrangement builds no
  chart again and loses no list's rows. A pane that has a height of its
  own just now says so on its `Grid` (by triggers in `MainWindow.xaml`:
  `SplitPanel.Fixed`, 46, while its card floats; `SplitPanel.SelfFolded`
  while a drill-down has nothing picked), and its neighbour in the column
  takes the rest. No pane is made smaller than 320 by 132; in a window too
  short for that the panel says what it needs (`PageStack.Needs`) and the
  page scrolls.
- **The arrangement is the player's**, per section, and remembered
  (`settings.layouts`). Three things change it, and each is a function in
  `Zerg.Core/Layout` that makes a new tree from the old one. **A rule is
  dragged**: over each lies a `Views/DividerThumb`; while it is in the
  hand the panel arranges by a tree of its own, and says the new tree once
  when it is let go (Esc drops it); a double-click sets that split back to
  its installed share; with the keyboard on it the arrow keys move it 8
  units and Enter sets it back. **A pane is folded** by the mark in its heading: it is its
  heading, and its neighbour in the column takes the room; alone in its
  column it folds sideways, to a rail 30 units wide with its title reading
  down. **A pane is carried by its heading** (a stand-in by its whole
  strip) and put down on another pane (above, below, left of or right of
  it, which halves that pane's room, or its middle, which swaps the two)
  or on an edge of the section (it takes that whole side); nothing moves
  until it is put down, and Esc puts it back. `Views/LayoutOverlay` draws
  what is in the hand. The panel does not hold the tree: it raises
  `SplitPanel.RearrangedEvent`, the main window hands that to
  `MainViewModel.Rearrange`, and the panel follows its binding. The save
  comes 400 ms after the last change. **Nothing is ever taken out of the
  panel**: the thumbs and the overlay are extra visual children of it, and
  a move changes rectangles only. The layout button on the command bar
  has Lock layout (`layoutLocked`: no thumbs, no grips, no move; a pane
  still folds), Reset for the section on screen and Reset all. The grip in
  a heading opens the heading's own menu (Fold, Pop out, Move left, right,
  up or down by one place, Reset), which a right-click on the heading
  opens too, and Shift+F10 or the menu key with the keyboard on one of
  the heading's controls. In one column nothing is dragged; a pane still
  folds.
- **The keyboard.** The Tab key goes through the window as it reads: the
  title bar (the tabs, the View tab and the mark that closes it, the
  gear), the command bar (the pair, Export, Import, the toggles, the
  layout button), the chips, All and None, then the panes **in the order
  they stand in**, not the order they are written in, then the rules of
  the section, then Dock all on the status line while a card floats. In a
  heading: the grip, the pane's own tools, the fold mark, Pop out; then
  whatever in the pane can be pressed (a character's heading, an action).
  Two things make that so. What is docked to the right is written first
  in XAML, so the title bar and the command bar are each a Tab group of
  their own (`KeyboardNavigation.TabNavigation="Local"`) that says its
  order with `TabIndex`. And `SplitPanel` gives each pane its place in the
  tree's reading order (down the first column, then the next) as its
  `Panel.ZIndex`: that is the order a panel hands its children out in to
  the Tab key, to UI Automation and to the painter, so `names` and a
  screen reader follow a move too, and no child is moved for it. The
  caption buttons are not Tab stops, as Windows' own are not. Whatever
  takes the keyboard shows the accent ring (`ZFocus`, `ZFocusInset`); a
  rule is lit instead. **A list whose rows cannot be pressed** (the two
  per-character tables, a drill-down's hits, Compare's small tables)
  **cannot be scrolled with the keyboard alone**: its scroll viewer does
  not take the keyboard, on purpose (the same cards float, and nothing in
  a panel may take it).
- **Lists.** Rows are objects kept across counts and updated in place
  (`Rows.Sync`, keyed by the character's real name even while names are
  hidden), so a new total rewrites one cell.
- **The tables.** There is no bar chart: **the row is the bar.** A
  character's row in the two per-character tables, and their heading in
  the Actions and Heals tables, is shaded in their colour from its left
  edge to their total out of the leader's (`Fraction`; a `Rectangle`
  under `Views/Grow`, so nothing is laid out as it moves), at the
  strength `Zerg.Core/RowShade` allows that colour (`ShadeOf`,
  `HeadingShadeOf` on the raised surface; in a panel `PanelShadeOf` for a
  strip's line and `PanelHeadingShadeOf` for a heading). An
  action under a character has its share as a small bar beside the
  figure, to its total out of that character's largest action. Two
  settings turn that round (`shadeCharacters` off: plain rows and a
  small bar beside the share; `shadeActions` on: the rows under a
  character shaded, with no colour), and a third is the threshold under
  which Accuracy, WS Acc and Pet Acc are marked red (`lowAccuracy`). A
  card binds the three once (`v:Shading.Characters="{Binding
  ShadeCharacters}"`) and its rows inherit them: a flip is a trigger in
  each row and a repaint of the marks that read it, **never a count and
  never a new row**. The marks (`Views/RowMarks.cs`) are painted on a
  count, not on the beat. Under the rows of each per-character table the
  **Party line** stands still: the party under the columns it sums
  (`MainViewModel.Party`, `HealParty`; `RULES.md` says how it is
  counted), its rate rewritten by the beat with the rows'. Columns are
  shares with a least width (`Zerg.Core/Layout/Columns`); in a pane or a
  panel too narrow for them all the Actions table drops its "least,
  average, greatest" mark, then Miss and Min, and Heals drops Min
  (`Views/Shed`), and under what is left the table scrolls sideways, as
  the per-character tables do under 900 units (720 in Healing).
- **Compare.** Two things on screen, both with the `CompareViewModel`
  as their data context. **The bands** (`Views/CompareSection`): what to
  compare, the two slots, and six figures, each A over B with its
  change. **The panes** (`Views/ComparePanes`, which is a `SplitPanel`
  with its four panes written into it, arranged by
  `MainViewModel.CompareLayout`): who (by character, by job; comparing
  healing, by healer), both runs on one clock, what kind, at whom. One
  pane holds a mode's table or the other's; only the mode on screen has
  rows. They are there once both slots hold a parse (`ShowsPanes`).
  **A row is shaded twice**: run A's band across its top half and run
  B's across its bottom half, each to that run's amount out of the
  largest in the table (two `Rectangle`s under `Views/Grow`); which line
  is which run is marked once per row, before the job
  (`Views/RunMarks`), and nowhere else. `shadeCharacters` off, the bands
  go and the amount's cell has two small bars (`Views/RunBars`): a
  trigger, never a redraw. A row opens onto its actions in a darker
  well, and an action onto how its hits are spread in each run, each
  worked out only when it is opened; what is open stays open through a
  redraw. **Each run's colour is a setting** (`runA`, `runB`; blue and
  orange as installed): `AppTheme` makes six frozen brushes from the two
  colours (`RunABrush`, `RunABandBrush`, `RunAInkBrush` and B's three;
  the band's strength and the badge's ink are worked out in
  `Zerg.Core/RunColours`: a band is as strong as leaves both the red of a
  worse change and the green of a better one at 4.5 to one on it, which
  is 21% and 19% for the installed pair in the dark theme and 14% and 13%
  in the light one), and everything of a run asks for them with
  `DynamicResource`. A colour belongs to its slot: Swap exchanges the
  parses. Under the cumulative chart, "Data table" puts the same
  figures in the chart's place.
- **Isolating targets.** The Target button (a `Views/PickButton` whose
  data context is a `PickFilter`) stands after Include Skillchains on the
  command bar, over the Damage section only, and again on the Compare
  section's title line while damage is compared. Its menu lists every target
  alphabetically with what it took (over Compare, in each run); choosing a
  line isolates that target or lets it go, the menu stays open so several
  can be chosen, and "All targets" clears. What is picked goes into the
  count (`Tracker.Count(..., targets)`, `CompareSheet.Of(..., targets)`),
  and `RULES.md`, "Isolating targets", says what that changes. On screen
  while one is isolated: the button is lit in the accent, names what is
  picked and has a mark that clears; the first figure's label reads "Damage
  to Kirin" (`TotalLabel`) with its share of everything in the note; **every
  DPS is a dash** (`MainViewModel.Rate`, which the draw beat goes through
  too, so a beat cannot write a number back) and the mark beside Party DPS
  is gone (`RateMark`); a damage panel's bar names the targets after the
  card (`PickFilter.Scope`). In Compare the By target table's rows are
  buttons (`ComparePickRow`, the `TargetPick` template) that pick as the
  menu does; the table lists every target whatever is picked, the isolated
  ones edged in the accent and the rest dimmed, with Clear among the pane's
  tools. Nothing of it is saved: `MainViewModel` empties its filter on
  Start, on a new event file and when the session and a viewed parse change
  places (with the drill-down, in `Recount`), `CompareViewModel` when a slot
  changes.
- **Isolating damage types** is the same thing along another cut: a second
  pick button, Type, after Target, in both places (`TypeFilter` on each
  view model, made by `PickFilter.OfTypes`), and in Compare the By damage
  type table's rows as buttons (`KindPick`; the two templates share the
  `SidePick`, `PickEdge`, `PickCells` and `PickName` styles in
  `Views/ComparePanes.xaml`). The Damage section has no such table: there
  the menu is the whole of it. A type's key is the row's kind ("ws"); it is
  drawn, sorted and logged by what its line of the table is called
  ("Weaponskills"). **The two filters cut together and each list is of what
  the other leaves** (`Tracker.Count`, `Compare.Measure`), so picking Melee
  narrows the targets to what melee dealt, and picking a target narrows the
  types to what that target took. A type takes no DPS away. The first
  figure's label says both ("Melee damage to Kirin", `DamageTypes.Heading`),
  a damage panel's bar names both after the card, and the two filters are
  emptied together. `RULES.md`, "Isolating damage types".
- **The Settings page.** One ruled row to a setting
  (`Views/SettingsPage`, each row a `v:SettingRow`): what it is called
  and what it does at the left, its control at the right, and beside
  some a small picture of what is set (`Views/Sample`: a strip at the
  opacity, four rows shaded as set, six rates as they would be marked, a
  Compare row in the two runs' colours), made of the tables' own parts
  and so never different from them. Under 850 units wide a control goes
  under its words. Every control is bound to the view model, and what it
  sets is on screen at once; a slider's value and a picked colour are
  **saved 400 ms after they stop moving** (`MainViewModel.Settle`; the
  pop-outs' opacity has its own timer in `PanelSet`), and are in the
  settings object meanwhile, so closing inside that time loses nothing.
  **The colour picker** (`Views/ColourPicker`) is part of the page, not
  a window: it opens under whichever of the two wells was pressed (one
  picker, moved), takes the keyboard, and closes on Esc, on a press
  anywhere outside the wells and itself, when the keyboard goes
  elsewhere in the window, and when the page goes. It holds its colour
  as hue, saturation and brightness (`Zerg.Core/Hsv`). Every colour
  picked is the run's setting there and then: the six brushes are
  swapped (`AppTheme.Runs`), the page's own picture and the Compare
  section follow, and Compare's chart of both runs is made again once
  the colour has stood still (`RunsSettled`).
- **The Actions and Heals tables start folded**: a heading per character,
  and a character's actions or heals only once their heading is picked
  (`MainViewModel.openActions` and `openHeals`, by real name, kept for the
  run and not saved). A docked card and its pop-out share it; the two
  tables do not.
- **A drill-down goes where its table goes**, and has no panel of its own.
  In the main window it is a pane that is always there, `DrillCard`
  (`HealDrillCard`), beside the tables: folded to its heading until an
  action is picked ("Select an action to drill down"), then open, with
  nothing else moving, under a tall heading (`Pane.IsTall`: the action,
  and under it whose it is and how much). With the table floating, the pane is folded again
  and says where the drill-down is ("Showing in the Actions panel"), and
  the panel makes one under the picked action's row (`Under` in
  `ActionsCard.xaml`: only while `Selected` and `Float.On`), without its
  heading, with a shorter chart and with "Every hit" folded (it is open in
  the pane). The card folds itself (`Pane.IsFolded`,
  by triggers on `DrillOpen` and `Panels[actions].IsOut`); a fold means
  nothing under `Float.On`.
- **Charts.** `Zerg.Core/Charts` decides where every mark goes from numbers
  and a text-width function; `src/Zerg/Charts` paints it. A chart is given an
  immutable model: build a new one for each draw. Series colours are the
  caller's, so models are built again on `AppTheme.Changed`. Type is 10
  along an axis and 10.5 elsewhere, in the application's `TextFont`.
  **The cumulative chart has no legend: each line is named at its end**,
  in the margin right of the plot (`EndLabels`, on in both cards, docked
  and floating; Compare's chart of two runs has it off and prints each
  run's total beside its marker). Names that would collide are moved
  apart and joined to their markers by a thin line; the line that leads
  is drawn stronger and its name too; a chart wide enough to keep a plot
  of 400 units prints each total after its name; a chart too short for
  every name keeps the largest lines'. The line that stands for several
  ("2 others") cannot say who they are: the tooltip of "Group under 5%"
  does (`GroupTip`, from `GroupMembers`). While the session is counting
  the chart's right-hand edge is ruled in green (`LineModel.Running`; a
  held session's chart is live and has no rule). The chart is five
  layers: under the lines the value axis, over them what is at their
  ends (the rule, markers, names), and those two **stand between counts**;
  the lines and the time axis's labels are drawn again when the edge
  steps (`LineLayout.Retime`); the crosshair is the fifth. The hover
  card (a heading ruled off from a row per line; `HoverCard.Arrange`
  says where everything in it goes) is drawn when the pointer reaches
  another grid time, and with a live chart under it on every beat; its
  shadow is rings of plain black, not an effect, drawn when the card's
  size changes. A histogram has a faint band behind its columns from the
  lower quartile to the upper and a tick across its baseline at the
  median, beside the rule at the mean. **To UI Automation a chart is one
  picture**: the name it is given in XAML ("Cumulative damage chart") and,
  as its help text, one line about what it shows now (`Chart.Summary`:
  so many lines over the clock at the edge and which ends highest, at
  how much; so many values from the least to the greatest, their average
  and median; each run's count and average). The line is made when a
  reader asks
  for it and at no other time; nothing in the chart is listed and nothing
  is said when it changes. What a chart draws is in the tables beside it.
- **Tokens.** Every surface, rule and piece of ink is a named value from
  `Themes/Dark.xaml` or `Themes/Light.xaml` (`Bg1Brush`, `Line2Brush`,
  `Text3Brush`, `AccentBrush`, ...; `UI-REDESIGN-PLAN.md`, section 4, lists
  them and what each is for). XAML asks for one with `DynamicResource`, never
  `StaticResource`: `AppTheme` merges the file at run time, after `App.xaml`
  is read. A floating panel has its own values under the same keys
  (`Themes/Panel.xaml`, merged into `PanelWindow`), so a card is dark in a
  panel with no trigger per brush. Sizes of type are the styles in `App.xaml`
  (`Figure`, `Cell`, `Head`, `Label`, `Badge`, ...), and the measures there
  (the bars' heights, a pane's heading and least size, the radii) are
  asked for by key. No colour is written outside `Themes/` but three: the
  made-up backdrop of the Settings page's picture of a pop-out.
- **Controls.** No theme of Windows' draws anything: `App.xaml` says
  `ThemeMode="None"`, and every stock control takes its look from
  `Themes/Controls.xaml`. A `Button` with no style is a `ZButton`, a
  `ToggleButton` a `TickToggle` (a tick box and its label; its `Tag`, if
  given, is drawn as a keycap), and so on for `Slider`, `ScrollBar`,
  `ScrollViewer`, `ToolTip`, `TextBox`, `Expander`, `ContextMenu`,
  `MenuItem` and `Separator`. A `RadioButton` has no look by type: each
  names `Segment` (inside a `Border` of the `SegmentGroup` style, which is
  the frame) or `SectionTab`. The title bar's three buttons name
  `CaptionButton` or `CaptionClose`, and words to press in a line of small
  print name `LinkButton`. A control type with no style there is drawn
  as WPF draws it unasked, pale and square whatever Zerg's theme. A state
  (under the pointer, pressed, disabled, on) is a trigger on the style, not
  in the template: a style based on another changes one state without a
  template of its own. Both windows say their own `FontSize` (12), and the
  main window says how its text is smoothed (grays).
- **Colours and names.** `MainViewModel.ColorOf` is the one place a
  character's colour is decided, and `NameOf` the one place a name is drawn.
  Panels are always dark, so each coloured thing has a panel twin
  (`PanelColorOf`).
- **Panels.** A panel is a second instance of a card with the same
  `DataContext`. `Views/Float.On`, an inherited attached property the panel
  sets, is the only thing that says "floating"; every difference is a style
  trigger on it. For one element,
  `Visibility="{Binding Path=(v:Float.On), RelativeSource={RelativeSource Self}, Converter={StaticResource Hidden}}"`
  is "docked only" (`Visible` for "floating only"). A pane has no heading
  there (the panel's bar names the card), but its tools stay, on a line of
  their own: "Group under 5%" in the Cumulative damage panel. The window is borderless
  and transparent, topmost, a no-activate tool window, moved and resized by
  hand (`Overlay.Drag`).
- **A panel's bar** is 30 units to under its rule and at rest is all
  figures: the pair at the left edge (a fixed target: nothing before it
  can grow), the clock in the session's colour, the total, and the
  card's name at the right, quiet. **The opacity slider and Dock are
  there only while the pointer is over the panel** (`PanelWindow.Chrome`):
  at rest they are at no opacity and not hit-testable, so a press where
  they are moves the panel; they are not collapsed, so UI Automation
  still has them. Under the pointer the name moves left of them in a
  panel 360 units wide or more and goes in a narrower one; in a panel
  too narrow for the tools beside the clock and the total, those two
  give the tools their place for as long as the pointer is there. A
  panel is 250 units wide at least. While clicks pass through, the pair
  and the tools are gone and a lock stands at the bar's end.
- **A strip** (the floating form of the two per-character cards) is a
  row of 20 units and a gap of 1 per character: shaded in their colour to
  their share of the leader's, as the table's row is, at the strength a
  panel allows that colour (`Zerg.Core/RowShade.Strip`); a solid 3-unit
  edge in the same colour; the owner's triangle in the gutter; three
  figures. It follows `shadeCharacters` (off: no shade, a small bar
  beside the %), and under 304 units wide drops its last column and the
  jobs. Its panel opens exactly as tall as its rows on the screen it
  opens on (`Zerg.Core/Layout/PanelFit`).
- **The halo.** A panel's text has a dark halo, so it holds over a bright
  part of the game with the backdrop turned down. It is one
  `DropShadowEffect` (`PanelHalo` in `App.xaml`) on the bar's line and on
  the whole card of the four panels in which nothing moves with the
  clock (`PanelWindow.Halo`). **Never on a row, and never over a
  cumulative chart**, whose lines are drawn at the draw frequency: an
  effect is worked out again whenever anything in it or under it is
  drawn. That chart outlines its own labels in paint (`Chart.Halo`,
  `Chart.Label`) on the two layers that stand between counts; its time
  labels, which move on every beat, have none. For the same reason **a
  bar in a panel does not ease** (`Views/Grow`): it goes to its new
  length at once.
- **The tray menu** has a heading while a session is armed or counting
  (`SessionView.Underway`): the mark, "Zerg", the clock and the total,
  and the state tag. It is a `MenuItem` with a look of its own that
  cannot be pointed at or reached with the keys. Idle, held, or with a
  saved parse on screen, the menu begins at Show Zerg.
- **Sections.** `MainViewModel.Section` is Damage, Healing, View, Compare, or
  Settings (never saved). `IsDamage` and `IsHealing` mean "these tiles and
  cards are on screen": the section of that name, or View showing that side.
  Each section but Settings is a tab in the title bar; over the Settings
  page no tab is picked and the gear is lit.
- **The title bar.** The main window's top 36 units are the first row of
  `MainWindow.xaml`. WPF's `WindowChrome` (made in `TakeTitleBar`) makes
  the whole window Zerg's to draw and tells Windows what is under the
  pointer: the bar's empty parts are the window's caption, so Windows itself
  drags the window by them, maximizes it on a double-click, snaps it, and
  opens the window's menu on a right-click or Alt+Space; a strip 6 units
  wide inside each edge sizes the window. Placement is as it was
  (`WindowPlacement`), and minimizing still goes through `OnStateChanged`
  into the tray. Maximized, Windows hangs the window's edges over the
  screen; `Fit` measures by how much on every move and gives the root that
  margin.
- **An opened parse** is a second, read-only `Tracker` (`Tracker.Of`). The
  session goes on being fed while a parse is on screen.

## State on disk

All under `%LOCALAPPDATA%\VibeXI\zerg\`:

| | |
|---|---|
| `logs\zerg.log` | startup, settings and theme changes, file switches, session changes with their exact times, crashes. The first thing to read when something is wrong |
| `settings.json` | filters (`excluded`, `skillchains`, `hideNames`, `groupSmallLines`), `charactersOpen` (false, as installed: one line of as many chips as fit and a "+N" chip; true: every character's chip, on as many lines as they take. Until 2026-10-07 the installed value was true; a file that has the key keeps what it says), how the tables' rows are drawn (`shadeCharacters`, true as installed: a character's row is shaded in their colour to the length of their share; `shadeActions`, false as installed: the rows under a character are shaded; `lowAccuracy`, 90 as installed, 50 to 100: the percentage under which a rate is marked red; the Settings page sets all three, and a file without them gets the installed look), the colours of Compare's two runs (`runA`, `runB`: each as it is written, "#3987E5", or null, as installed: the blue and the orange of the theme in use; once set, the one colour serves both themes; set with the Settings page's picker, and back to null with its "Use default"), `theme`, `section`, `viewMode`, `compareBy`, `compareMode`, panel opacity (`panelOpacity`, the default, set on the Settings page; `panelOpacities`, each panel whose own slider has been moved), `drawFrequency` (how many times a second the clock, every DPS and HPS and a live chart's edge are redrawn, 1 to 60), placement under `windows` (`main`, and `panel:<key>` for each floating panel), `openPanels` (the panels to bring back at the next start), `layouts` (how each section's panes are arranged, where that is not as installed: an object with a tree under "Damage", "Healing" or "Compare"; a split is `{"split": "columns", "share": 0.653, "first": ..., "second": ...}`, "rows" for one over the other, and a pane `{"pane": "bars"}` with `"folded": true` while it is; the key is absent while all three are as installed, as in a file from before 2026-10-08; whatever is under it that is not a tree is the installed arrangement, and a tree is repaired against the panes the build has), `layoutLocked` (false as installed: the arrangement cannot be dragged), `clickThroughKey` (the hot key, "Ctrl+Alt+Z") and `eventsDir` (the folder the event files are looked for in; absent or null is the addon's own) |

A `zerg.log` at the top of that folder, a `windows.json` and a `WebView2\`
folder may also be there on this machine. They are left over from the build
Zerg replaced; nothing reads them.

## Verifying a change

1. **Tests:** `dotnet test apps/zerg/Zerg.slnx`, 709 tests. They cover
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
   # replay.py prints three numbers: the first row's time, the last row's
   # time (both Unix seconds) and the row count. Wait until 3 s past the
   # second number (about 27 s in all). A Pause before then cuts the session
   # short, silently: a whole one with --extras reads 153,687 damage in 00:24.
   # Then one more line, which counts nothing, so that a count happens after
   # the last row. Until one does, the cumulative chart is not live and its
   # right-hand edge stands still while the session runs:
   printf '{"kind":"meta","t":%s}\n' "$(date +%s)" >> <dir>/Hasaya_2026.10.03.jsonl
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
   - **The bands.** The state tag is in `text` and `names` as the state in
     words: `Idle`, `Armed`, `Live`, `Held`, `Saved` (`text` has it twice,
     once for the band of the section that is not on screen). A chip is
     pressed by the character's name (`click <pid> Xatsh`), `Include all`
     and `Include none` as before. `Show or hide the character list` is
     the chip after the chips ("+5", or a chevron), which is there only
     while it has something to do: more chips than one line holds, or an
     opened list of more than one line. A chip that is not on the line is
     not in `names` and cannot be pressed. The marks have no text; what is
     written over them has (`per 20 s`, `running`, `party share`).
     The fixture played in is 24 seconds long: two bars, the second short.
   - **The panes.** Each pane's heading is in `text` as its title and
     its note (`Actions`, then the sentence after it); in `names` the
     pane is `Group '<title>'` (`Group 'Actions'`, `Group 'Drill-down'`)
     and the note is not listed. Pop out is a glyph
     in the heading with the name it always had (`Pop out Actions`); a
     drill-down has none, and its `Close drill-down` (`Close heal
     drill-down`) is there only while it is open. Folded, a drill-down
     reads `Drill-down` and `Select an action to drill down`, or
     `Showing in the Actions panel`. **`scroll` moves the first area that
     scrolls up and down**: in a window wide enough for two columns the
     page does not, so that is a pane's list (the first pane's that has
     more than fits); in a narrow window it is the page. The stand-in for
     a floating card is the card's title, `Showing in its own panel.` and
     `Bring back <title>`.
   - **The layout.** Each pane's heading has `Move <pane>` (the grip: a
     button, and `click` on it opens the heading's menu) and `Fold <pane>`
     (`Unfold <pane>` while folded); a drill-down's are `Move Drill-down`
     and `Fold Drill-down` (`Heal drill-down` in Healing) whatever its
     heading reads; a stand-in has `Move <title>` too. Each rule that can
     be moved is `Thumb 'Divider between <pane> and <pane>'`, the first
     pane in reading order of each side: as installed, `Divider between
     Damage by character and Cumulative damage` (upright), `... Damage by
     character and Actions`, `... Cumulative damage and Drill-down`; a
     rule beside a folded pane or a stand-in has none, and none has while
     the layout is locked or the window is one column. **A rule is moved
     without the mouse by `set <pid> "<its name>" <units>`**: its value
     is the size of the first of its two panes across it. `where <pid>
     "<its name>"` gives its middle for a real `drag`; a real double-click
     sets it back, and so does Enter with the keyboard on it. **A pane is
     moved without the mouse** by its menu:
     `click <pid> "Move Actions"`, then the menu is a window of the
     process with no title (`windows <pid>`; a tooltip is one too, with
     no `MenuItem` in its `names`), `click h<menu> Move`, and `Left`,
     `Right`, `Up` or `Down` in the window that opens. With the mouse:
     `drag` from the grip, or anywhere on the heading that is not a
     button, to a point in another pane (its middle swaps; near its top,
     bottom, left or right puts the pane there) or within 14 units of the
     section's edge. `click <pid> Layout` opens the layout menu (`Lock
     layout`, `Reset Damage layout`, `Reset all layouts`). `zerg.log` has
     a line for each change once it has stood still 400 ms (`layout
     Damage: Cumulative damage put above Actions`, `a divider dragged`,
     `layout locked`) and, at a start from a file that has the key,
     `layouts read: Damage rearranged, Healing as installed, ...`.
     `phase11\scripts\offscreen.sh` (under
     `%LOCALAPPDATA%\VibeXI\zerg-redesign\`) draws and drives all of it
     without a window and says whether the rows and charts are the same
     objects afterwards; `walk1.sh` to `walk4.sh` beside it are the four
     walks on screen (each says in its heading what real input it makes),
     and `mouse.cs` is the real mouse for them: a double-click, a drag
     held at its end for a grab, one key by its code, and nothing at all
     unless the window in front is the Zerg it was told.
   - **The keyboard.** `drive.cs chord` cannot send Tab.
     `phase12\scripts\tab.cs <pid> tab 60` presses the real Tab key sixty
     times and says after each press what has the keyboard, as UI
     Automation has it (`until "<name>" <n>` stops at a control; `key 0D`
     is Enter, `1B` Esc, `5D` the menu key; `shift 79` is Shift+F10); it
     presses nothing unless the window in front is that Zerg's. One turn
     of the window is about 56 presses with the fixture played in and a
     character's actions open. `phase12\scripts\offscreen.sh` walks the
     same order without a window (it asks WPF's own `KeyboardNavigation`),
     the page and the two bars, installed and after a move, and says what
     ring each stop shows. `phase12\scripts\walk1.sh` to `walk4.sh` are
     the closing pass's four walks on screen: every state of the design's
     sheet 07 in the dark theme, the light theme and the keyboard, a
     narrow window; each says in its heading what real input it makes.
   - **The tables.** In `text` a per-character table reads its group
     names as written (`Weaponskill`, `Skillchain`, `Pet`: they are drawn
     in capitals), then one word per column (`Damage` four times, `Avg`,
     `%` and `Acc` twice each), then the rows, then `Party` and its
     figures. A character's heading in the Actions table has their total
     and their share and no dash or caret; the caret is drawn. The marks
     have no text. The hit list is open, so its rows are in `text`, the
     spark of a critical hit as a line of its own before the figure; in
     `names` a hit is `0:23, Goblin Pathfinder, 176, −45 against the
     average` and one of the six figures `Min 142`. `Every hit` (`Every
     cast`) opens and shuts the list. The three settings are on the
     Settings page (below).
     `phase6\scripts\tables.cs` (run by `offscreen.sh` beside it) draws
     the page with rows that have everything, under each pair of the
     settings, and says for each table whether it scrolls sideways or
     has dropped columns, what a flipped setting paints and what a beat
     paints.
   - **The page without a window:** `phase5\scripts\panes.cs` draws the
     page (bands and panes) from a built `Zerg.dll` and the XAML cut out
     of `MainWindow.xaml`, at several sizes and states, says where every
     pane was put and whether the page scrolls, lists the buttons'
     automation names, and says what a beat paints. It makes no `Zerg.App`
     (it reads `App.xaml`'s resources as text into a plain `Application`),
     so it can run beside a running Zerg, from a build in another folder.
     (It was written for the tree as Phase 5 left it and stops on the
     first hit it makes: a `HitRow` takes six values since Phase 6.
     `phase6\scripts\tables.cs` is the same program brought up to date,
     and `phase7\scripts\page.cs` again: it gives the charts models and
     steps the cumulative chart's edge on every beat.)
   - **The charts.** A chart is in `names` as a picture under the name
     its XAML gives it, while it can be seen: `Image 'Cumulative damage
     chart'`, `'Damage distribution chart'`, `'Cumulative healing chart'`,
     `'Healing distribution chart'`, `'Cumulative chart of both runs'`,
     `'Distribution chart of both runs'`. Its help text is one line about
     what it shows (`phase7\scripts\help.cs h<hwnd> "Cumulative damage
     chart"` prints a control's help text; the line itself was read off
     the peer without a window, not through `drive.cs`). Nothing painted
     on a chart is listed (the names at the lines' ends): for those, look
     at a grab. The hover card needs the real pointer: `hover <target> <x>
     <y>` at a point in the plot, read off a grab of that window, and a
     second later `snap`; then `hover <pid> -40 -40` to take the pointer
     off. The rule at a counting session's edge shows only between the
     `meta` line of the recipe above and Pause. The fixture has two
     characters under 5%, so with `groupSmallLines` on there is a
     "2 others" line; who they are is in the tooltip of `Group under 5%`,
     which UI Automation has as that button's help text
     (`phase7\scripts\help.cs h<hwnd> "Group under 5%"` prints it, with
     no pointer). `phase7\scripts\charts.cs` (run by `offscreen.sh`
     beside it) draws each chart alone without a window, in both themes
     and as a panel has it, with and without its card
     (`chart.HoverAt(point)` puts the pointer somewhere), and says how
     many times each layer of the cumulative chart is drawn while its
     edge steps.
   - **The controls' looks without a window:** `phase12\scripts\make-pressed.py`
     makes `pressed.cs` from `phase2\scripts\kit.cs`: every control at
     rest, under the pointer and pressed, side by side, in both themes
     (`drive.cs` cannot hold a button down while it takes a grab).
   - **Without a window:** `phase4\scripts\bands.cs` under
     `%LOCALAPPDATA%\VibeXI\zerg-redesign\` draws the bands to pictures
     from the built `Zerg.dll` and the band's XAML cut out of
     `MainWindow.xaml`, with made-up values, at several widths and states,
     and says which elements a beat paints again (`offscreen.sh` builds
     and runs it).
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
     The rectangle under `windows.main` is the whole window as seen, and is
     what `rect` and `snap` report: with a title bar of its own the window
     has no unseen sizing frame outside it. (A placement saved before the
     title bar changed was 7 pixels wider on the left, right and bottom at
     100% than what was seen: the redesign's phase 0 asked for 93,100
     1454x907 and its grabs are 1440x900 at 100,100. Such a file should
     come back as the same rectangle with all of it now seen; that was
     reasoned, not run.) Maximized, `rect` says the screen's left, top and right and
     a bottom below the work area by the overhang (`0,0 2560x1539` on a
     2560 by 1600 screen at 150%, whose work area is 1528 tall).
   - **The window's own buttons** are `click <pid> Minimize`, `Maximize`
     (`Restore` while maximized) and `Close`: Zerg's, in its title bar.
     Whether a real press reaches them, and whether the bar drags the
     window, needs the real mouse (`at`, `press`, `drag`): UI Automation
     presses without one. **`front` restores a maximized or snapped window**
     (it sends `SW_RESTORE`): between maximizing and the next real click, do
     not call it; look with `fg`.
   - **The section** is switched with `click <pid> "Healing section"`,
     `"Damage section"` or `"Compare section"`: the tabs in the title bar.
     `"View section"` is a fourth tab, there only while a parse is open (or
     while the View section is on screen with none); `Import` is still the
     way to open a parse into it (below), and `"Close the View tab"` closes
     the parse from the tab.
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
     chart's table is `"Data table of both runs"` (it takes the chart's
     place while it is open). `Include Skillchains` is on its title line
     too. Its four panes are groups in `names` (`Group 'By character'`,
     `'Cumulative damage'`, `'By damage type'`, `'By target'`; by job and
     for healing they are called otherwise), there only once both slots
     hold a parse; **`scroll` moves the first of their lists that has
     more than fits**, since the page itself does not scroll. `Use
     current for A` wants a session whose clock has started. A whole
     window's `text` is mostly other sections' (hidden things are read
     out): to compare Compare's figures with an older capture, cut its
     lines out of the older file ("Compare runs" to the Settings page)
     and ask whether each is in the newer one. The runs' colours and the
     shading are set on the Settings page (below).
     `phase8\scripts\compare.cs` (run by `offscreen.sh` beside it)
     draws the section without a window, the real view model over the
     two test parses, in two dozen states, sizes and colours, and says
     where everything was put and what a script would press;
     `compare-walk.sh` walks it on screen.
   - **The Target button, and Type.** `click <pid> Target` opens its menu
     (over Compare the same name; only the one on screen is found), and
     `click <pid> Type` the other's: `MenuItem 'All types'`, then the
     types by what they are called, each with its damage and share (over
     Compare, a column for each run); `"Clear the type filter"` is its
     mark, and `zerg.log` has `types Melee` (`compare types Melee,
     Weaponskills` over Compare). Over Compare `click <pid> Melee` is a
     row of By damage type and `"Clear the isolated types"` the link in
     that pane's heading. With both isolated the first figure reads
     `Melee damage to Leaping Lizzy`, or `Damage of 2 types to Goblin
     Pathfind…`, and a panel's bar `Damage by character · Leaping Lizzy ·
     Melee`. The fixture's whole run reads 62,555 of Melee (Party DPS
     2,606.5, which stays), 17,157 of it to Leaping Lizzy, and 51,549 of
     Melee and Weaponskills to Goblin Pathfinder. **With a menu open, find
     it by its styles** (`windows <pid>`: `tool noactivate topmost
     layered`, not `clickthrough`): its size changes with its lines. The menu
     is a window of the process with no title, 400 units wide
     (`windows <pid>`; `400x…` at 100%, `600x…` at 150%): `names h<menu>` lists `MenuItem
     'All targets'` and one per target, alphabetically, each followed by
     its figures; `click h<menu> "<target>"` isolates it or lets it go and
     the menu stays open; `chord $'\x1b'` (Esc, with Zerg in front: `fg`
     first) shuts it. **While the menu is open `click <pid> Target` finds
     the word "Target" in the menu's own heading**, not the button: shut
     it with Esc. `click <pid> "Clear the target filter"` is the mark on
     the button, there only while a target is isolated. In `text` the
     button reads `Target` twice and then what is picked (`All`, `Kirin`,
     `Genbu +2`), and the first figure's label reads `Damage to <target>`
     or `Damage to 2 targets`, its note `74.9% of 153,687 · …`; every DPS
     is `—` and Party DPS's note is `no rate for a target`. In Compare a
     row of By target is pressed by the target's name (`click <pid>
     "Leaping Lizzy"`), and `"Clear the isolated targets"` is the link in
     that pane's heading. `zerg.log` has `targets Leaping Lizzy, Rock
     Crab`, `targets all`, and `compare targets …`. The fixture has eight
     targets; a whole one with `--extras` reads 75,788 to Goblin
     Pathfinder and 39,355 to Leaping Lizzy of 153,687. A floating damage
     panel's bar reads `<card> · <what is picked>` in `text`.
   - **The View section.** `click <pid> Import` (on the command bar) opens
     the Open dialog, as above, and shows the section once a file is
     chosen; with a parse left open under another section it goes back to
     that parse and opens no dialog, as `click <pid> "View section"` does
     (the tab). A start never opens on this section.
     On its band, `"Open a parse to view"` (`"Open another parse to view"`
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
     `set <pid> "Draw frequency" <value>`, and the low accuracy mark
     `set <pid> "Low accuracy mark" <value>` (50 to 100). The shading
     switches are `click <pid> "Shade characters in their colour"` and
     `"Shade actions and heals"`. `settings.json` and
     `logs\zerg.log` (`events dir …`, `hot key …`, `panel opacity …`,
     `draw frequency …`, `low accuracy under …`, `shade characters …`,
     `run colours A …, B …`) say what was taken; a slider's line and a
     colour's come 400 ms after the last change. The hot key's keycaps
     are in `text` and `names` as one thing, the chord (`Ctrl+Alt+Z`);
     the four pictures are one `Image` each (`A picture of …`) and
     nothing in them is read out.
   - **The colour picker** is on the Settings page and in the main
     window's own tree. `click <pid> "Colour of run A"` (`B`) opens it
     under that well, and again shuts it; a well's code is its help
     text, and the two codes are in `text` as written (`#3987E5`). Open,
     it is `Custom 'Colour picker for run A'` in `names`, with three
     parts: `set <pid> Hue <0 to 360>`; `type <pid> "Saturation and
     brightness" "<percent> <percent>"` (its value reads the same way);
     and `type <pid> "Colour code" "#22E05A"`, which is taken at once
     (a text set whole is; typed with the real keyboard it waits for
     Enter); a code that is not a colour's leaves `not a code` in
     `text`. `"Use the default run colours"` puts both
     back. `where <pid> "Saturation and brightness"` and `where <pid>
     Hue` give the points for a real `drag`. Esc for `chord` is the
     character itself (`chord $'\x1b'` in Bash), and Tab cannot be sent
     (`chord` trims it away). `phase9\scripts\settings.cs` (run by
     `offscreen.sh` beside it) draws the page without a window at seven
     widths, in both themes and under each pair of the switches, lists
     what UI Automation is told, and drives the picker by its keys, its
     automation peers and its code field; `settings-walk.sh` walks the
     page on screen (139 seconds; it says in its heading which real keys
     and presses it makes), and `settings-restart.sh` starts again from
     what that left, types a code into the picker, and in a short window
     opens a drill-down beside a floating card to see the page scroll
     (with `second-monitor` as a fourth argument, a saved placement on
     the second monitor in that half's place: never run).
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
     closed with `click h<hwnd> Dock`, or all at once with
     `click <pid> "Dock all"` (the status line, which also says how many
     are out: `text` has "1 panel out", "3 panels out, click-through"). A drill-down has no panel of its
     own: in the Actions or Heals panel, `click h<hwnd> "<character>, <action>"`
     opens it under that row.
   - **A panel's bar.** In `text` it reads the pair (`Restart`, `Pause`,
     each twice), the clock, the total, the card's name, the lock's
     sentence (read out though hidden), `Panel opacity`, the percentage,
     `Dock`. **Dock and the slider are not seen until the pointer is over
     the panel, and a script reaches them all the same**: `click h<hwnd>
     Dock` and `set h<hwnd> "Panel opacity" <value>` work at rest, and
     `names` lists `Button 'Dock'` and `Slider 'Panel opacity'` at rest
     (seen). To see them, the real pointer has to be on the panel: `hover
     h<hwnd> <x> <y>`, then `snap`. With clicks passing through they are
     not there at all. A strip's headings read `CHARACTER`, `DAMAGE`, `%`,
     `ACC%` (`HEALING`, `CASTS`), as written.
     `phase10\scripts\panels.cs` (run by `offscreen.sh` beside it) makes
     the real `PanelWindow` and the real tray menu without showing
     either, draws them in some forty states and sizes, and says where
     the bar's parts are, what a press on the unseen tools reaches, what
     UI Automation is told, and what a beat and a count paint again.
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
     before and after: it must not become the panel. **And read the
     pointer back before pressing** (`phase10\scripts\pointer.cs where`):
     a game can keep the pointer to its own monitor, `press` is not told,
     and the press then lands wherever the pointer is.
     `phase10\scripts\owed-walk.sh` does it that way (`onto`). (Settled
     on 2026-10-08, after the bar changed: a press on the bar, a drag by
     it, a resize from two edges and a drag of a scroll bar's thumb, the
     foreground Zerg's main window before and after each.)
   - **The tray icon.** `tray <h> rect` says where the icon is in screen
     pixels. On Windows 11 it starts in the overflow: with that closed the
     answer is the ^ button (48x72 here); `at <x> <y>` on it, wait a second,
     and `tray <h> rect` again gives the icon itself (60x60). Then
     `at <x> <y>` in its middle is a real click, and `at <x> <y> right`
     opens the menu, which is a window of the process with no title
     (`windows <pid>`): `names`, `snap` and `click h<menu> <line>` work on
     it, and the submenu is another such window. `tray <h> menu` opens the
     menu without the mouse, for reading only. The heading is in `names`
     as `MenuItem 'Session'` with its text after it (`Zerg`, the clock
     and the total as one line, the state as a word), there only while
     the session is armed or counting; the lines are as they were. **A
     borderless game covers the taskbar**: the icon's place is then the
     game's, and a click there is a click in the game
     (`phase10\scripts\pointer.cs whose <x> <y>` says whose window is at
     a point). **The menu's window is larger than the menu** by the room
     its shadow needs: for a click at 2141,1353 it stood at 2141,848,
     418 by 506 (seen at 150%), its foot and its left-hand edge at the
     pointer, so the menu itself is 26 units above the click and 14 right
     of it. After a real click the menu is the foreground window, and
     once a line is chosen the foreground goes back to the shell's
     overflow window (seen).
   - **Minimized, Zerg is hidden in the tray**, and `<pid>` no longer finds
     its window: take its `h<hwnd>` first, or from `windows <pid> all`.
3. **`logs\zerg.log`** for what the app thought happened.

## Traps already hit (each cost a debugging round)

**WPF, with no theme of Windows' loaded**

Until 2026-10-06 Windows' Fluent theme drew every stock control, and several
traps here were about it. It is gone (`ThemeMode="None"`; `Themes/Controls.xaml`
draws the controls). Each trap below that the change touched says how it was
checked afterwards: "seen" in the running app, "off screen" in a picture
drawn without a window, or "not checked".

- **Window placement.** `SetWindowPlacement` is called twice from
  `SourceInitialized` (the first move onto a monitor of another scale gets
  resized by WPF), and `WindowState = Maximized` is set **after** it. Set
  before the handle exists, WPF creates the window maximized on the default
  monitor and it stays there. A record that is off every screen (Windows
  parks a minimized window at about −32000) falls back to a default place.
  The title bar of Zerg's own did not change any of this (seen on one
  screen at 150%, 2026-10-07: the same pixels back, maximized back
  maximized over the same restored rectangle, −32000 to the middle of the
  screen; **not seen on a second monitor of another scale**, which was not
  attached). The `WindowChrome` is set before the placement is asked for,
  so the window gets its bar first and its place second.
- **A maximized window is larger than its screen**, by the sizing frame it
  would have had (11 pixels a side at 150%), and with a title bar of its
  own all of that is content. `MainWindow.Fit` gives the root a margin of
  what `Native/WindowFrame.Fit` measures, on every `WM_WINDOWPOSCHANGED`.
  Not on `StateChanged`: WPF raises that when the property is set, which at
  a start from a saved maximized placement is before the window has been
  maximized, and the measurement would be of the restored window. (Seen:
  maximized by a double-click, by the button, by a drag to the top edge and
  at a start, the bar and the status line are whole and at the screen's
  edges.)
- **`WindowChrome.CaptionHeight` is counted from under the top sizing
  strip**, not from the window's top: 6 + 30 is the 36-unit bar. And from
  the window's top edge, which on a maximized window is above the screen:
  `Fit` adds that, or the bar's lowest units would not drag the window.
  Setting any property of a `WindowChrome` makes WPF set the window's frame
  again there and then (`SetWindowPos`), so `Fit` does it after Windows'
  message about the move, not during it.
- **Whatever is pressed in the title bar says
  `WindowChrome.IsHitTestVisibleInChrome="True"`**, or the press drags the
  window (the property is inherited, so a control's template parts have
  it). The same mark outranks the sizing strip, which is **inside** the
  window now, 6 units along each edge: a control within 6 units of an edge
  loses that much to sizing unless it is marked, and a marked one takes the
  sizing away where it lies. The scroll bars' thumbs are marked (only the
  thumbs), so a bar at the window's right-hand edge can be grabbed. (Seen:
  the window sizes from all four edges, the right one beside a scroll bar.
  Not seen: a thumb grabbed inside the strip.)
- **A control type with no style in `Themes/Controls.xaml` is drawn as
  Windows draws it**: pale, square, in Windows' colours whatever Zerg's
  theme. A `Button` with no style is a `ZButton` (26 tall, a face and an
  edge) and a `ToggleButton` a tick box, which is wrong for a caption
  button, a tab or anything else that is not one: give such a thing a style
  of its own. Nothing covers `CheckBox`, `ComboBox`, `ListBox`,
  `TabControl`, `ProgressBar`, `GridSplitter`, a bare `RepeatButton` or
  `Thumb`. (Seen: every control now in Zerg is covered. Not checked: what
  an uncovered one looks like.)
- **A style is found by a control's exact type.** A subclass (`TrayMenu`)
  must name its style (`Style="{DynamicResource {x:Type ContextMenu}}"`).
  (Seen: it names it and is drawn right. Not checked: the look without.)
  An element of a class of Zerg's own has no style unless it names one:
  a `v:StateTag` without `Style="{StaticResource StateTag}"` has no
  colours and draws a gray word on nothing. (Or unless the application's
  resources have a style under its exact type: `v:Pane` gets `ZPane` that
  way, from the last lines of `Themes/Controls.xaml`. Off screen.)
- **An element cannot ask for more than it was offered.** WPF cuts the
  size an element returns from measuring down to the size it was given,
  and arranges and clips it in that. A panel that needs more than its
  parent offered has to say so some other way: `SplitPanel` sets
  `PageStack.Needs` on itself while it measures, and the page measures it
  again with that much. (Off screen: before this, a page said it was 300
  tall while holding panes 423 tall.)
- **A `StaticResource` that is missing is found when the window is made,
  not when the project is built.** A style taken out of `App.xaml` while
  one card still asked for it gave a build that compiled and could not
  start (seen, in the log). Take a style out after its last use.
- **An element that paints itself beside something the beat rewrites**
  (`StateTag`, `Spark`, `ShareBar`) must not be `AffectsRender` on
  anything the beat sets, and is not painted again because a neighbour's
  text changed: WPF calls `OnRender` only for an element that asked or
  whose size changed. To see whether one was painted, off screen:
  `UIElement`'s private `_drawingContent` is a new object after every
  `OnRender` (`phase4\scripts\bands.cs`, `Beats`).
- **A panel may set a property on a child, or on itself, while it
  measures**, if it measures whatever that changes afterwards in the same
  pass and nothing it set feeds back into what it already measured.
  `FigureBand` tells each cell whether it has room for a mark before it
  measures the cell; `ChipLine` says what the "+N" chip reads and whether
  it is there after it has measured the chips, with a fixed width kept
  back for that chip, so the answer cannot change the question.
- **A chip that is not on the line is `Hidden`, not `Collapsed`**: a
  collapsed element has no size to measure, and the panel needs every
  chip's width to decide which fit.
- **A text block is trimmed to the width it was measured in.** `ChipLine`
  measures the hint a second time, in the room left for it. (Whether one
  measured without limit and arranged in less would trim was not tried.)
- **A context menu says Closed late, and sometimes never.** It fades out,
  and raises `Closed` when it has; opened again before that, it does not
  raise it at all. The press that shuts a menu lands on the button that
  opens it, 60 ms later in `drive.cs press`, so a guard set in `Closed` saw
  nothing and every second press opened the menu again (seen, with a log
  line in each handler). `PickButton` asks the `IsOpen` property instead
  (`DependencyPropertyDescriptor.AddValueChanged`), which changes there and
  then, and notes on the button's way down whether that press shut the menu.
- **A separator in a menu does not find its style by its type.** The menu
  tells it to look under the key `MenuItem.SeparatorStyleKey`, so
  `Themes/Controls.xaml` has a style under that key as well as one for
  `Separator`. Without it a menu's separators are Windows' own pale lines
  on the dark menu, and nothing says so (seen; fixed; seen right).
- **A tooltip takes its font, its weight and its text alignment from the
  element it is on**, and a column of figures is right-aligned and a total
  SemiBold. `ZToolTip` sets all three back. A tooltip is a window of its
  own, see-through at its edges: its shadow is cast by a plain shape behind
  the box, because an effect on the box itself would draw its text through
  a bitmap. (Seen: left-aligned, Regular, on a right-aligned heading.)
- **Where a dictionary stands decides who answers.** The application's
  merged list is `Themes/Controls.xaml`, then the colour set `AppTheme`
  adds at run time; the last answers first. `AppTheme` puts the new set in
  before it takes the old one out. A window's own resources are nearer
  than the application's: a floating panel finds `Themes/Panel.xaml`
  first, and so does a tooltip opened from one (measured: its edge is the
  panel's `Line3`, not the application's). The tray menu belongs to no
  window and gets the application's (reasoned from that; it was seen in
  the dark theme only).
- **A token a panel's file lacks is the application's there**, light under
  the light theme. `Themes/Panel.xaml` gained `Bg1Brush` (transparent) for
  the inside of a slider's ring. A new control used in a panel: list the
  keys its style asks for against that file.
- **A user control is not measured again because something inside it
  was**, if its root comes to the same size. Whatever it worked out in
  its own `MeasureOverride` from its children is then stale. The Compare
  section's panes are not inside its bands' control for that reason: the
  page has to see their least size, and looks for it one level down
  (`PageStack.Needed`). (Off screen: a window 300 tall that did not
  scroll.)
- **An application tells its `Window`s that a resource dictionary was
  swapped, and nothing else.** In Zerg that is everything. In a scratch
  program whose stage is a bare `HwndSource`, brushes asked for with
  `DynamicResource` seem not to follow a new theme or a new run colour:
  stand the stage in a `Window` that is never shown
  (`phase8\scripts\compare.cs`, `Recolour`).
- **`CompareViewModel` saves `settings.json` when `Mode` or `By` is set**
  (and `MainViewModel` when almost anything is). A scratch program that
  makes one writes the user's real file unless it gives it the mode in
  the `Settings` object it is constructed with and never sets either.
  **Some of `MainViewModel`'s saves come 400 ms later, from a timer**
  (`Settle`: the low accuracy threshold, a run's colour; the draw
  frequency and the pop-outs' opacity have their own). A scratch program
  that sets one of those must stop the timer before it lets the
  dispatcher run (`phase9\scripts\settings.cs`, `Calm`), and must give
  every slider's property a value inside the slider's range before the
  page is made: a slider that has to bring a value into range writes it
  back, and the write starts the timer.
- **A `DynamicResource` to a key that does not exist is not an error.** WPF
  leaves the property unset and the app starts: a mistyped key in a
  template draws nothing and says nothing. A key renamed by plain text
  replacement is another way to get one (one key's name inside another's).
  List the keys asked for against the keys defined after any such edit;
  `phase8\scripts\keys.py` under `%LOCALAPPDATA%\VibeXI\zerg-redesign\`
  does it (the one in `phase2` does not know the six run brushes, which
  `AppTheme` makes in code, and lists them as defined nowhere).
- **Text with no size of its own takes the window's, and both windows say
  12.** While Fluent was loaded it was the theme's 14 (taking
  `FontSize="13"` off a name made it larger). A tooltip and a menu are
  windows of their own: their styles say family, size and weight. (Seen:
  the same sizes before and after the theme went.)
- **How text is smoothed depends on the window.** A see-through window (a
  panel, a tooltip, a menu) smooths in grays; an ordinary one uses
  ClearType, with coloured fringes. The Fluent theme made the main window
  gray too. `MainWindow` says `TextOptions.TextRenderingMode="Grayscale"`
  to stay as it was and match the rest (seen: grays again; a few pixels at
  the edge of each letter still differ from before, none by eye).
- **Windows 11 draws an edge round a window that has no title bar of
  Windows'**, in a colour of its own. `Native/WindowFrame.Edge` asks for
  the theme's `Line3` and for round corners (seen under the dark theme: the
  edge reads `#3D4656`, two pixels at 150%, and the corners are round; not
  seen under the light theme or on Windows 10, which has neither
  attribute). Until the title bar became Zerg's own, Windows drew a
  program's title bar light unless asked for the dark one, and
  `Native/TitleBar` asked; that file is gone.
- **Leaving a theme changes more than templates** (text smoothing above,
  and the title bar's colour while Windows drew it). To call such a step
  neutral, compare a grab from before with one from after, pixel for pixel.
- **There is no Medium weight.** Segoe UI Variable (under each of its three
  names), Segoe UI and Cascadia Mono have Regular and SemiBold faces and
  nothing between that WPF can pick: `FontWeight="Medium"` is drawn SemiBold
  (asked of WPF off screen, 2026-10-06).
- **A menu line is ticked by `IsChecked` alone.** `ZMenuItem` gives every
  line the same column at its left: an accent tick while it is checked, or
  the line's `Icon`. No line is `IsCheckable`: choosing one runs its
  command, and the tick follows what the command did. (Off screen: a ticked
  line beside plain ones and a submenu line, all in line. Not seen in the
  running app: no line was ticked in the states visited.)
- **Scroll bars are drawn over the content** (`ZScrollViewer`, as Fluent's
  were). Lists that scroll keep a 14-unit right margin, and
  sideways-scrolling tables a 12-unit bottom one. The bar is 12 units
  wide. (Seen.)
- **A slider has no minimum width.** Fluent's had one; `ZSlider` has
  none, and nothing here needs `MinWidth="0"` to make a slider narrow.
- **A text box applies its `Padding` to its text itself.** A template that
  also gives the content host a margin from `Padding` counts it twice
  (off screen).
- **A style's triggers outrank its setters, and the triggers of a style
  outrank those of the style it is based on.** That is how a ghost or a
  filled button changes one state of the plain button with no template of
  its own (off screen: the filled button stays amber under the pointer).
  A value set on the control itself outranks them all.
- **Every style names its focus ring** (`ZFocus`, `ZFocusInset`); one that
  does not gets Windows' dotted rectangle. WPF shows a ring only after the
  keyboard was used, and `drive.cs front` taps Alt: whatever UI Automation
  pressed last shows its ring in the next grab (seen).
- **An expander's heading is a toggle named `HeaderSite`**, and it is given
  the expander's own automation name, so `click <pid> "Data table of both
  runs"` finds something to press under the name the expander is known by
  (seen in `names`).
- **After a theme switch a list used to make its rows again**, and what
  UI Automation had been told about the old rows went stale: every
  figure a script read stood still at what it was at the switch. That
  was while the Fluent theme was loaded and a switch replaced its whole
  dictionary, templates and all; `AppTheme.RereadRows` dropped the
  remembered children afterwards. A switch now replaces colours only, no
  row is made again, and the function is gone (2026-10-08; seen with it
  taken out: `text` the same before and after a switch but for the
  Settings page's own theme-dependent lines, and, after a switch, a
  character excluded and names hidden both changed what `text` and
  `names` read of rows that were there before it). If a switch ever
  replaces templates again, it will be wanted again: `phase12\before\tree`
  has it.
- **What is docked to the right is written first, and the Tab key goes
  by what is written.** The gear came before the tabs and the layout
  button before Start (found by reading; fixed, then walked with the
  real key). A bar whose reading order is not its written order is a Tab
  group of its own (`KeyboardNavigation.TabNavigation="Local"`) with a
  `TabIndex` on what is out of place; a panel inside it that is to go
  first as a whole is a `Local` group too, with a `TabIndex` of its own
  (`KeyboardNavigation.TabIndex` works on anything that is a group).
- **A panel's children are met in `Panel.ZIndex` order** by the Tab key,
  by UI Automation and by the painter, not in the order they were added.
  That is how the panes are tabbed in the order they stand in without one
  of them being moved. It is also why two off-screen comparisons of "the
  same rows in the same order" stopped agreeing after a move: the rows
  are the same objects, met in another order.
- **A context menu that is open when the menu key comes up shuts
  itself.** A heading's menu opened as that key went down was gone before
  it was seen (found with the real key; Shift+F10 worked). It is opened
  as the key comes up (`SplitPanel.OnKeyUp`).
- **A reason cut short is no reason.** The note of a refused export was
  one line with an ellipsis; in a window 680 units wide it lost its last
  sentence, which is what to do about it. It wraps (seen).
- **An attached property is not told a value that equals its default.**
  `Views/Grow.Share` defaulted to 0, and a bar whose first share was
  exactly 0 was never scaled: it lay the whole length of its row (a
  healer whose pet did all the healing; seen). Its default is "not a
  number" now. Anything that acts in a property's changed callback has
  the same hole.
- **A figure wider than its cell is cut short with an ellipsis, and
  nothing says so.** The `Cell` style pads 8 units both sides; a
  right-aligned figure in a tight column is given its left padding back
  (`Padding="2,0,8,0"`). Try a table with long figures: a party's rate
  in the thousands, a total past a million.
- **An inherited attached property set on one table reaches a table
  inside it** (a drill-down under a row of the floating Actions table).
  `Views/Shed` says its own answer outright every time, so the inner one
  never takes the outer one's.
- **A record shown in a list is named by its `ToString`** to UI
  Automation, members and all. `HitRow` and `StatTile` say what they are.
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
  Release). The chart's own `Paint` is 0.1 to 0.2 ms of that. (Measured
  when every redraw drew the whole chart. Since 2026-10-07 a step of the
  edge draws two of its five layers, the lines and the time labels; what
  that costs has not been measured.)
- **A layer left standing must know what it was drawn for.** The
  cumulative chart's value axis and line ends are drawn again only when
  the model (by reference), the size, `Compact`, `EndLabels`, the empty
  text or `Chart.Epoch` (the inks, the face, the DPI) has changed.
  Anything new that those two layers depend on has to join that list in
  `LineChart.Paint`, or it shows up a count late. And nothing on them may
  depend on the time at the edge: `LineLayout.Retime` moves the time
  axis only. To see which layers a redraw drew, off screen:
  `DrawingVisual`'s private `_content` is a new object after each
  (`phase7\scripts\charts.cs`, `Beats`).
- **A held session's cumulative chart is "live"**: `Counting.Cumulative`
  calls a chart live when the clock is past the last event, and a held
  clock is. `Live` means "the last grid time is the clock, and may be
  moved". Whatever should show only while the session counts asks
  `LineModel.Running`.
- **An effect on a drawn layer is worked out again on every frame that
  draws it** (reasoned from how WPF composes a frame; not measured). A
  hover card over a live chart is drawn at the draw frequency, so its
  shadow is plain shapes on a layer of their own, redrawn when the
  card's size changes (`Chart.Shade`). The other reason is the tooltip's:
  an effect draws the text under it through a bitmap.
- **A layer opened and closed with nothing in it is still a layer
  drawn.** `Chart.UpdateHover` takes the card and the crosshair down only
  when one is up; it used to clear both on every paint, at the draw
  frequency.
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
  with the Damage section on screen. Take these as rough: measured again on
  2026-10-05, the same build at 30 a second with the Damage section on
  screen (1440 by 900 on the primary monitor, no panels) read 14.6 to 15.7%,
  and 18.4 to 20.7% forty minutes later. The spread was not explained.
- **A UI Automation client makes every layout dearer** from then on (WPF
  keeps its peers up to date), so a cost measured after `drive.cs` has read
  the window is too high. `front`, `snap` and `press` do not use it.
- **A hairline is one device pixel on a pixel centre.** At 150 % a one-unit
  line is a blurred pixel and a half.
- **A row is rounded up to whole pixels.** At 150 % a 20-unit row with a
  1-unit gap is 32 pixels, not 31.5. A strip's panel opens at a height
  worked out in pixels, part by part, for the scale of the main window's
  screen (`Zerg.Core/Layout/PanelFit`; seen at 150%: three rows in 183
  pixels and no scroll bar).
- **An effect is worked out again whenever anything in what it is set
  on, or under it, is drawn again** (as read; not measured). One on a
  panel's whole card is paid for on every count, and would be on every
  frame of an eased bar and on every beat over a cumulative chart: hence
  where `PanelHalo` is and is not, and no ease in a panel. Before putting
  anything that moves with the clock into a strip or a table's panel,
  remember the card is under an effect.
- **A menu makes a line of whatever is put in it.** Something that is
  not a `MenuItem` or a `Separator` is wrapped in a `MenuItem`, with the
  menu's look and its slab under the pointer (as read in WPF; not
  tried). The tray menu's heading is a `MenuItem` with a style of its
  own for that reason.
- **Naming a part of a window `Left`, `Top`, `Width` or `Height` hides
  the window's own** (the compiler warns, CS0108, and the code still
  builds).
  So does a method: `Drop` in a panel hides `UIElement.Drop`, `Name`
  hides `FrameworkElement.Name`.
- **An arrow key a focused control does not use takes the keyboard to
  whatever control lies that way.** On a divider, which uses the two
  arrows across it, Up and then Left three times moved the rule once: Up
  had taken the keyboard off it (seen). A control whose arrows mean
  something keeps all four (`SplitPanel.OnThumbKey`).
- **A `Thumb` takes the keyboard when it is pressed**, so a look for
  "has the keyboard" stays on after every drag. `DividerThumb` is lit
  with the keyboard only when the keyboard came by a key.
- **A `Thumb`'s own drag numbers are measured from the thumb**, which
  moves when what it drags is laid out as it goes. The panes' panel asks
  the mouse where it is instead (`SplitPanel.OnThumbMoved`).
- **A bitmap draws an element at the element's own place in its parent**,
  not at the bitmap's corner; and a `VisualBrush` with a viewbox in
  absolute units drew nothing into one (off screen). For a picture of a
  pane: draw it where it stands and cut the picture out
  (`SplitPanel.Sketch`).
- **A `JsonElement?` is the type for a setting whose shape may be wrong.**
  A typed property makes `Settings.Load` fail on one bad value under its
  key, and every other setting is lost with it (`Settings.Layouts`).
- **A computed property on `Settings` needs `[JsonIgnore]`** or it is saved.
  `System.Text.Json` escapes "+" unless the relaxed encoder is used, which
  is why the settings use it.
- **Content that is stretched and has a `MaxWidth` is centred**, not set
  to the left, once its room is wider than that. For "as wide as the
  room up to so much, at the left" give it a `Grid` column with a
  `MaxWidth` (the Settings page's header and rows). With
  `HorizontalAlignment="Left"` instead it is only as wide as what it
  holds asks to be. (Off screen.)
- **A stock `Slider` with `IsMoveToPointEnabled` goes to a press and then
  does not follow the pointer** (read in WPF's source, not tried), which
  is half of what a colour picker's strip has to do. The picker's two
  parts are elements of Zerg's own for that reason.
- **Giving something the keyboard scrolls it into view, and of two such
  requests waiting a scroll viewer keeps the later.** The colour picker
  was asked into view and then given the keyboard: only its square came
  into view. The keyboard first, then `UpdateLayout`, then
  `BringIntoView` (`SettingsPage.Pick`). (Off screen; then seen: the
  picker wholly in view as it opens, in a window 900 units tall and in
  one 440.)
- **A press that brings the keyboard to a text box also puts the caret
  where the pointer is**, which undoes a `SelectAll` made as the keyboard
  arrived. The picker's code field keeps that first press to itself
  (`OnCodePress`). (Seen: a press, three characters and Enter replaced
  the whole code.)
- **Being told that a resource dictionary changed is a walk of everything
  in the window**, hidden sections and all: about 1.5 microseconds an
  element here (off screen, the Settings page alone). `AppTheme.PutRuns`
  replaces the runs' six brushes in one step for that reason; putting
  the new set in and taking the old one out told every window twice, at
  every point of a drag in the picker.
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
- **The strip that sizes a panel is inside it**: 6 units along each edge
  (5 along the top, where the pair begins 5 down). A table lies edge to
  edge in its panel, so its scroll bar is in that strip: a scroll bar's
  thumb outranks it (`PanelWindow.EdgeUnder`). Anything else put within
  6 units of a panel's edge loses that much to sizing.
- **When the window of Zerg's that has the foreground goes away, Windows
  may hand the foreground to a panel**, no-activate or not. Seen twice
  on 2026-10-07, both times with the game up: straight after a start
  that reopens panels, and after a tray menu asked for with Zerg in
  front closed, `fg` named a panel. The next morning, with no game,
  neither happened, with this build or with the one in `dist`: whether
  it is older than the redesign cannot be told. `MainWindow.HandOn`
  looks for it at those two moments, once the dispatcher is idle, and
  if a panel has the foreground gives it to what had it before (or to
  the main window) and says so in the log (`a panel had the foreground
  after …`). **It has not been seen to act**; a log with that line in
  it is the evidence it lacks. It is outside a panel's own paths on
  purpose: nothing in `Overlay.cs` or `PanelWindow` knows of it.
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
- **UI Automation gives a text box the keyboard before it sets its
  text**, and a toggle before it toggles it, as it does a button before
  it presses it. So "the field does not have the keyboard" cannot mean
  "this text was not typed": the colour picker's code field took a
  script's code for typing and waited for Enter (seen). It asks instead
  whether a key or a character has just come to it
  (`ColourPicker.OnCodeTyping`). And a script's next `click` anywhere
  takes the keyboard from whatever had it, which is itself an event
  (the field takes a valid code as the keyboard leaves).
- **Invoke gives a button the keyboard focus, and WPF shows a focused
  control's tooltip.** A tooltip in a grab after `click` is that. With the
  pointer resting on something that has a tooltip, `<pid>` can address the
  tooltip itself (a tiny `snap`, an empty `text`, "not found" from
  `click`): use the window's `h<hwnd>`. A tooltip is topmost, no-activate
  and layered like a panel, so pick a panel by its title. A tooltip is not
  always up 1.8 seconds after `hover`: wait 2.5 before a grab that must
  show one.
- **`chord` takes a single key by its character's code**, so an apostrophe
  is the Right arrow (0x27), and by the same rule `%` the Left, `&` Up and
  `(` Down (only the apostrophe was tried). One arrow key moves the
  keyboard focus as a person does, which is the way to see a focus ring on
  a control of one's choosing: press its neighbour through UI Automation,
  then the arrow. `fg` first: the key goes to whatever is in front.
- **The scroll bars have no arrow buttons**, so `names` no longer lists
  `Scroll Up` and `Scroll Down`, which every earlier file of names has.
- **`names` lists the panes in the order they stand in**, down the first
  column and then the next, and the Tab key goes the same way (seen, in a
  section with a pane moved). Two `names` files of one section compare
  line for line only if its panes stood the same way in both: sort them
  first. A line of `names` is `ControlType.Button 'Start'`: a pattern
  that begins at the line's start has to say `ControlType.`.
- **A `Window` that is never shown lays nothing out**, handle or no
  handle (`WindowInteropHelper.EnsureHandle`): it is collapsed until it
  is shown. A scratch program can swap a dictionary under what it holds
  and see brushes follow, but cannot see whether a list would make its
  rows again; that needs the running app.
- **The Tab order can be asked without a window**: WPF's own
  `KeyboardNavigation` has the two functions the key calls (`GetNextTab`,
  `GetGroupParent`; not public), once its `_navigationProperty` field is
  given `KeyboardNavigation.TabNavigationProperty`, which is what the key
  does first (`phase12\scripts\sweep-tail.cs`, `Stops`).
- **A menu and a tooltip are both windows of the process with no title**,
  and pressing a button through UI Automation brings up its tooltip. To
  find the menu, look for the one whose `names` has a `MenuItem`.
- **A script that presses where something should be misses when an
  earlier step left it elsewhere.** Ask `where` just before a real press.
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
- **`front` un-maximizes and un-snaps** (`SW_RESTORE`). A real click aimed
  at where a maximized window's button was then lands on whatever is
  behind. Check with `fg` instead once the window is maximized or snapped.
- **Windows' own title bar is gone from UI Automation**: `names` no longer
  has `TitleBar 'Zerg'`, `System Menu Bar` or `System`, and `Minimize`,
  `Maximize` and `Close` are Zerg's buttons, once each.
- **Text in a control's template is not a control to UI Automation.** WPF's
  peer for a `TextBlock` says so when the block is part of a template: it
  is taken for part of the control it draws. `text` still reads it;
  `names` does not list it, and a screen reader moving from control to
  control does not stop at it. When the cards' titles moved into the
  pane's template they went missing from `names` (seen), so `Views/Pane`
  has a peer of its own, a group named by the title. A button in a
  template that is collapsed is still read by `text` under its name: the
  Pop out button of a pane that cannot float has its name taken off.
- **An automation peer of one's own must not say "I am a control" whatever
  happens.** `Views/Caps` did (`IsControlElementCore` returning true), and
  once labels were `Caps`, `names` listed those of the section that was
  not on screen and of bands that were collapsed. Left to WPF, the answer
  is yes only while the element can be seen. `Caps` and `StateTag` do not
  override it now (seen: `names` of the idle Damage section has one
  `Idle` and no healing label).
- **`text` reads what is hidden, a second copy of a label included.** The
  tabs keep the width of their label in SemiBold with an unseen twin of it
  (`Views/Room`); the twin has no automation peer, so `text` reads each
  label once. `View section` and `Close the View tab` are in `text` even
  while that tab is not shown; `names` has them only while it is.
- **Restored from maximized by dragging its bar, a window can be left
  hanging a few pixels off the screen's left** (Windows' doing: seen at
  x = −11). The pointer cannot go there, so a scripted drag of that edge
  does nothing. Start from a placement that is wholly on the screen.
- **Hidden and collapsed elements are still read out** by `text` (a hidden
  section's last contents, a hidden copy of a table). `names` lists only
  what is shown.
- **The one "Data table" expander is Compare's**, and `click` knows it as
  "Data table of both runs". **`Include Skillchains` is on the command
  bar and on Compare's title line**; `click` finds the command bar's
  first, and either flips the one setting.
- **A harness written for an earlier tree can draw a later one wrong and
  say nothing.** `phase7\scripts\page.cs` gives the page a made-up view
  model; since Phase 8 the window binds `Fill` to `FillsPage` and hides
  Compare's panes by a binding to a view model that one has not got, so
  under it the page does not fill the window and Compare's panes stand
  in the live section's place. `phase9\scripts\make-needs.py` makes a
  copy that sets the one and hides the other.
- **A here-document inside a Bash command loses backslashes** (`\\n`
  arrives as a line break): the trap under "This machine", met twice
  more in Phase 9 and four times in Phase 12 (a Python edit sent through
  one lost `\s`, `\r` and `\n` and left a script that still passed
  `bash -n`). Scripts are written with the editor tools, and so are
  scripts that edit scripts.
- **`enter` presses the real Enter key**, at whatever is in front if the
  dialog could not be brought forward. Ask `fg` first, and do not press
  if the foreground is not Zerg's (`phase8\scripts\compare-walk.sh`,
  `fill_safe`).
- **Panels are topmost and cover each other.** A scripted press lands on
  whichever is on top: move them apart first.
- **A game can keep the real pointer.** With the owner's game up
  (2026-10-07: borderless over the whole primary monitor), `hover` said
  it had moved the pointer onto a panel on the other monitor and half a
  second later the pointer was at the corner of the game's monitor; with
  the game in front, a script's move was refused outright. Nothing a
  script does with the real mouse is to be believed then: read the
  pointer back, and do not press if it is not where it was put.
- **Starting Zerg takes the foreground**, from a game too. A walk that
  must not disturb whoever is at the machine cannot start one.
- **`tray <h> menu` is for reading only**: that menu cannot take the
  foreground, and with Zerg's own window in front it closes at once.
- **Windows does not remember a folder under `%TEMP%`** for a file dialog.

**This machine**
- **A dev run does not start beside a running Zerg.** It brings the running
  one forward and exits 0, silently. If a change seems to have no effect,
  look for an older `Zerg.exe` first.
- **A Zerg whose start failed used to stay alive with no window.** An
  exception in `OnStartup` (a missing resource, say) was logged, shown in a
  dialog and "carried on" from; with no main window ever shown, nothing
  ended the process, and it kept the single-instance lock and its build
  output. Every later start, the desktop shortcut's included, then did
  nothing and said nothing (seen 2026-10-07). Since that day a fault before
  the main window is up ends Zerg after its dialog (`started` in
  `App.xaml.cs`, exit code 1; built, not yet seen failing). A build from
  before the fix can still be found this way: it is not on the taskbar or
  in the tray, so look in the process list.
- **Running the dev build while someone is editing the source can catch the
  tree half-changed.** That is how the start above failed: a style was
  removed a minute before its last user was.
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
- **Python prints to this console as Windows-1252** and stops at the first
  icon-font character in a `names` or `text` file ("charmap codec can't
  encode"). Set `PYTHONIOENCODING=utf-8` for a script that prints them.
- **A Bash command of more than about 8,000 characters arrives cut short.**
  Write a long script to a file and run the file. Write it with the editor
  tools: a script made with a here-document inside a Bash command lost a
  backslash (`\\` arrived as `\`), and three grabs were saved over each
  other under one wrong name.
- **A `\u` escape written through the editor tools arrives as the raw
  character** (a raw U+2028 broke the C# lexer). Write `(char)0x2028`, or
  `\p{Zl}` in a regex.
- **.NET 10 file-based apps** (`dotnet run --file x.cs`) have reflection-based
  JSON off. Use `JsonObject`, not anonymous types.
- **An `Application` object starts itself.** Its constructor queues the
  call to `OnStartup`, and the first thing that lets the dispatcher run
  carries it out. A scratch program that made a `Zerg.App` to get at its
  styles (to draw them to a picture without a window) started the real
  Zerg inside itself: Zerg's options dialog sat on screen for minutes, and
  `zerg.log` got a "starting" line. Take what the constructor queues off
  the dispatcher again (`Dispatcher.Hooks.OperationPosted`, then `Abort`),
  hold the `Local\VibeXI.Zerg` mutex so a start that slipped through would
  take itself for a second launch, and give the program a time limit.
  `phase2\scripts\kit.cs` under `%LOCALAPPDATA%\VibeXI\zerg-redesign\` does
  all three, and draws every control style in every state off screen.
  The other way is to make no `Zerg.App` at all: `phase5\scripts\panes.cs`
  parses what is between `App.xaml`'s `Application.Resources` tags as
  text (with `Themes/Controls.xaml` named in full, and the one converter
  that is not public, `EqualsConverter`, made by reflection) and gives it
  to a plain `Application`.

## Status and what's next

**The native build is finished and signed off** (2026-10-05). The browser
build it replaced, its host and the tests that compared the two are deleted.
`dist/Zerg/Zerg.exe` is the native app, and the user's desktop shortcut runs
it.

**Next is packaging**, which has not been started and waits for the user's
word: a single-file self-contained `Zerg.exe`, a zip, a version resource;
then a clean-machine test, SmartScreen, and a README for players.

**The redesign of the UI is done** (2026-10-08): a re-skin in place to
the design in `apps/zerg-mockup/` (`DESIGN.md` and ten sheets), in thirteen
phases, 0 to 12. `UI-REDESIGN-PLAN.md` is the plan and the record: its
section 5 has each step with an "As built" under it, its section 10 an
entry per phase (what was done, how it was checked, on which screen, the
traps, the details decided and how to turn each back), and its section 11,
"Where the redesign stands", is the place to start: what is finished, what
was never verified and what would verify it, and what is left for the
owner.

What it made, in one paragraph: no theme of Windows' draws anything, and
every colour and control is Zerg's own (`Themes/`); a title bar of Zerg's
own with the sections as tabs; one command bar; a band of five figures
with small marks and the state tag; the characters as one line of chips;
each section four flush panes that scroll what they hold and that the
player resizes, folds and moves, each section's arrangement remembered;
dense tables in which the row is the bar, with grouped headings, the low
accuracy mark and a Party line; charts that name their lines at their
ends; Compare as bands over panes, its rows shaded twice in two colours
that are settings; a Settings page of ruled rows with a colour picker; and
panels whose bars are all figures until the pointer is over them. Six
settings keys came with it (`shadeCharacters`, `shadeActions`,
`lowAccuracy`, `runA`, `runB`, `layouts`, and `layoutLocked` makes seven),
one changed its installed value (`charactersOpen`), and one counting rule
was added (the Party line; `RULES.md`). 227 tests became 673.

Its controls (Phase 2) were seen at 100%; everything built after that,
from the title bar on, was seen at 150% only (one screen of 2560 by 1600
on most days, the second monitor of the pair on one evening) and never at
100%. **One check its plan asks for was never made: a saved main-window
placement on a second monitor of another scale, with the title bar now
Zerg's own.** That monitor was not attached on any day the check could be
run. The placement code is untouched and the inset of a maximized window
is measured each time, so it should hold; the owner's first start with the
window on that monitor is the check
(`%LOCALAPPDATA%\VibeXI\zerg-redesign\phase9\scripts\settings-restart.sh`
in its `second-monitor` mode runs it by script, and has never been run).

**`dist/Zerg` is the redesigned build since 2026-10-08**, published at the
owner's word, and the desktop shortcut runs it. It was started once after
the publish (the Release build's first start): it read the owner's settings
and their saved layouts, followed the newest event file and exited clean.
Nothing else of the Release build has been looked at; every check in the
redesign ran on a Debug build. The build from before the redesign (2026-10-05)
is kept outside the repository, in
`%LOCALAPPDATA%\VibeXI\zerg-redesign\dist-before-redesign\`: copy its eight
files back over `dist/Zerg` to return to it. That old build reads a
`settings.json` the new one has written and keeps the keys it does not know
(seen for Phase 8's keys; not tried for `layouts` and `layoutLocked`).

Open ideas, none started:
- **Opening a `.zerg` file by double-click** (a per-user file association,
  handed to the running instance).
- **Signing** the exe.
- **A reset** for a pop-out's own opacity. The user was offered one and
  chose not to have it.
- What the redesign left as ideas (`UI-REDESIGN-PLAN.md`, section 11): a
  hair-line of one device pixel per window (every rule is one unit on
  whole pixels now, two pixels at 150%); lists that scroll with the
  keyboard; a folded pane's own short note; the Snap Layouts flyout over
  the maximize button.
