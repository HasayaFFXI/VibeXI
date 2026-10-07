# Zerg UI redesign: implementation plan

This is a step-by-step plan for rebuilding Zerg's user interface to the design
in `apps/zerg-mockup/`. It is written for an engineer or coding agent who has
not seen the conversation that produced it. Paths are relative to the
repository root. Phases 0 and 1 are done; what was built, checked and found
is in section 10, and a step whose wording turned out wrong is corrected in
place with "As built". Phases 2 to 12 are still a plan only.

- **The design (source of truth):** `apps/zerg-mockup/DESIGN.md` and the ten
  sheets `01-damage-live.svg` to `10-layout.svg` beside it (PNG renders in
  `apps/zerg-mockup/png/`). The two `*-palette-proposal.svg` files are a record
  of options and are **not** part of the design.
- **The app being changed:** `apps/zerg` (C# / .NET 10 / WPF). Read
  `apps/zerg/CLAUDE.md` (working notes, build and verify recipe, traps) and
  `apps/zerg/RULES.md` (counting rules) before starting.
- **Verified while writing this plan (2026-10-05):** the solution builds and
  `dotnet test apps/zerg/Zerg.slnx` passes 227 of 227 tests with .NET SDK
  10.0.401. The app was **not run**; everything said about its current look
  comes from reading the XAML and code.

---

## 1. Summary and strategy

### What the redesign is

Today Zerg is a single scrolling column of rounded Fluent "cards" on a Mica
window: a command bar, a row of section radio buttons, five tiles, then cards
for the cumulative chart, a bar chart with a table, the Actions table and a
drill-down. The redesign turns that into a dense "instrument":

- One dark surface divided into **panes** by one-pixel rules. No cards, no
  gaps, no Mica, no shadows inside the window.
- A **custom 36 px title bar** that holds the sections as tabs (Damage,
  Healing, Compare, and View while a parse is open) and the Settings gear.
- The five tiles become one **64 px band of figures**, each with a small mark.
- The bar chart is removed. Each table row is **shaded in the character's
  colour to the length of its share** (the idiom the floating strip already
  uses), governed by two new settings.
- Denser tables (24 px rows, 12 px text), grouped column headings, a graded
  red **low accuracy mark**, a **Party line**, and a "Min, avg, max" mark.
- The drill-down becomes a pane beside the tables.
- Panes can be **resized, folded and moved** by the user, per section, and the
  arrangement is remembered.
- Compare, Settings, the floating panels ("pop-outs") and the tray menu are
  restyled in the same language. Settings gains three rows (Row shading, Low
  accuracy mark, Compare colours with a colour picker).
- Every stock control gets Zerg's own template; the Fluent theme is switched
  off.

### Terms used in this plan

| Term | Meaning |
|---|---|
| Token | A named design value (a colour, a size). Section 4 lists them with the WPF resource key proposed for each. |
| Pane | A region of a section's body with a 30 px heading: the redesign's replacement for a card. |
| Card | One of today's `UserControl`s in `apps/zerg/src/Zerg/Views/` (`LineCard`, `BarsCard`, ...). Each has a docked form and a floating form switched by the inherited attached property `Views/Float.On`. Cards are kept and re-skinned as pane content. |
| Panel / pop-out | A card floating over the game in a `PanelWindow` (borderless, topmost, never takes the keyboard). |
| Stand-in | What the main window shows where a floating card was (`Views/Away.xaml`). |
| Band | A fixed-height strip above the panes: title bar, command bar, figures, characters. Bands are not movable. |
| Back-shade | A row's background drawn in a colour from the row's left edge to the length of its share. |
| Split tree | The data model of a section's pane arrangement: nested horizontal and vertical splits with a ratio each. |
| Count / beat | The two things that redraw the app today: `MainViewModel.Recount()` (an event arrived or a filter changed) and `MainViewModel.Tick()` (the draw frequency). See `apps/zerg/CLAUDE.md`, "How it works". |

### Strategy and ordering

The app is finished and signed off, so the plan is a **re-skin in place**, never
a rewrite. Every step leaves the app building, starting and measuring. The
order is chosen so that risk is taken early where it is cheap and late where it
is isolated:

1. **Tokens first, invisibly** (phase 1). Add Zerg's own colour, type and
   measure resources beside the Fluent ones. Then alias the Fluent brush keys
   Zerg uses onto the tokens, so the whole app changes colour in one small
   step and every later step works in final colours.
2. **Own control templates, then leave Fluent** (phase 2). Fluent supplies both
   brushes and templates. Templates are replaced control type by control type
   while Fluent is still loaded; only when nothing depends on it is
   `ThemeMode` switched to `None`. This is the one step that can break
   startup, so it is small and comes with a checklist.
3. **Shell, then bands** (phases 3 and 4). The custom title bar is the riskiest
   piece of chrome (window placement, maximize, snap). It goes in before any
   screen work so it is exercised by every later verification run.
4. **Panes in a fixed arrangement** (phase 5). The pane control and the layout
   host are built once, showing the design's default arrangement with no
   interaction. Screens are then redesigned inside real panes, and the
   interactive layout work (phase 11) only adds behaviour to a host that
   already exists.
5. **Screens**: live tables (phase 6), charts (phase 7), Compare (phase 8),
   Settings (phase 9), pop-outs and tray menu (phase 10). New settings are
   wired into the view model with their installed defaults when the tables
   need them (phase 6); their controls arrive with the Settings page
   (phase 9).
6. **Adjustable layout last among features** (phase 11). It is the largest item
   and the least coupled to how anything looks.
7. **Light theme, states, accessibility, performance, clean-up, docs**
   (phase 12).

Why not layout first: its value depends on the panes being worth arranging,
and its risk (focus, drag, persistence) is independent of the re-skin. Why not
screens before controls: every screen uses buttons, toggles, segments and
scroll bars; restyling them once avoids touching each screen twice.

### Ground rules for whoever implements this

- **The naming rule.** Two terms are banned everywhere under `apps/zerg/`
  (UI text, identifiers, file names, comments, this plan). See "The naming
  rule" in `apps/zerg/CLAUDE.md` for the terms and the exact check command;
  run it at the end of every phase. It currently finds nothing in
  `apps/zerg` or in `apps/zerg-mockup`. New code about fonts and sizes is the
  likeliest place to trip it: say "measurements" or "figures".
- **UI layer only.** Do not change what is counted. The one place this plan
  changes what `Zerg.Core` counts is called out as such (the Party line,
  step 6.9). New pure helpers (shade strength, layout tree, colour
  maths) go in `Zerg.Core` so they can be tested without a window.
- **Keep automation names.** `apps/zerg/tools/drive.cs` finds controls by
  `AutomationProperties.Name` and `AutomationId` (`Start`, `Second`,
  `"Damage section"`, `"Pop out Actions"`, `"Actions of <character>"`, ...).
  Every name listed in `apps/zerg/CLAUDE.md` under "Verifying a change" must
  survive, on the new control that replaces the old one.
- **Toggles act on `IsChecked`, never on `Click` or `Command`** (UI Automation
  flips them without clicking; see the traps in `apps/zerg/CLAUDE.md`).
- **Every token reference is a `DynamicResource`.** `AppTheme` merges the theme
  dictionary at run time, after `App.xaml` is parsed; a `StaticResource` to a
  token fails at startup.
- **The user's environment.** Do not commit (the user handles git). Do not
  stop a `Zerg.exe` you did not start; a dev run beside a running one exits
  silently. Copy `%LOCALAPPDATA%\VibeXI\zerg\settings.json` aside before a
  run and restore it afterwards. Do not publish over `apps/zerg/dist/Zerg`.
- **Out of scope:** packaging, installer, signing, file association; anything
  in `apps/zerg/UX_Feedback.md` that the design did not adopt (for example
  global hot keys for Start and Pause, auto-pause, sortable columns).

### Commands

```bash
# build everything (set the opt-out in the same command; see CLAUDE.md "Environment")
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build apps/zerg/Zerg.slnx

# all tests (227 at the start of this work; Zerg.Core only)
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test apps/zerg/Zerg.slnx

# after scripted builds
dotnet build-server shutdown

# run against a scratch events folder
dotnet run --project apps/zerg/src/Zerg -- --events-dir <dir>
```

The full recipe for playing a fixture into a running app and reading the
window with `tools/drive.cs` is in `apps/zerg/CLAUDE.md`, "Verifying a
change". This plan calls that recipe **"the replay recipe"**.

---

## 2. How the current UI is built (what you will be changing)

| Area | Files | Notes |
|---|---|---|
| Application resources | `apps/zerg/src/Zerg/App.xaml` | `ThemeMode="System"` (Fluent). Fonts `TextFont`, `NumberFont`, `IconFont`. Shared styles: `Card`, `Figure`, `Label`, `Segment`, `CardTitle`, `CardNote`, `Badge`, `Swatch`, `Cell`, `CellText`, `Head`, `HeadText`, `RowLine`, `SmallButton`, `SmallToggle`, `SmallSegment`, `PopOut`, `SessionButton`, `StartButton`, `SecondButton`, `Clock`, `Chip`, `RowButton`. Converters `Equals`, `Visible`, `Hidden`, `Present`. |
| Theme | `apps/zerg/src/Zerg/AppTheme.cs`, `Themes/Dark.xaml`, `Themes/Light.xaml` | `AppTheme.Apply` sets `Application.ThemeMode`, then merges one of the two dictionaries (18 `Series*` colours, 18 `Job*` colours, `ChartSurfaceBrush`, `RunABrush`, `RunBBrush`, `RunInkBrush`). `AppTheme.Changed` makes the view models hand colours over again. |
| Main window | `MainWindow.xaml`, `MainWindow.xaml.cs` | Stock title bar. Rows: command bar (`DockPanel` with the gear, `WrapPanel` with the session pair, Export, Import, note); a "Section" row of `RadioButton`s plus "Toggles"; the character chips; a page `ScrollViewer` named `Body` holding `ViewCard`, the Damage `StackPanel` (`UniformGrid` `Tiles` and the cards), the Healing `StackPanel`, `CompareSection`, `SettingsPage`; the status line. Code-behind owns the draw beat (`Native/Metronome`), tray icon, hot key, window placement, `FitTiles`. |
| View model | `MainViewModel.cs` and its partials `.Damage.cs`, `.Healing.cs`, `.Parse.cs`, `.Settings.cs`; rows in `Rows.cs` | CommunityToolkit.Mvvm. `Section` is `Damage`, `Healing`, `View`, `Compare` or `Settings`. Row objects are kept across counts (`Rows.Sync`). `ColorOf` / `PanelColorOf` decide a character's colour; `NameOf` a drawn name. |
| Cards | `Views/LineCard`, `BarsCard`, `ActionsCard`, `DrillCard` and the `Heal*` twins | Each is a `Border Style="{StaticResource Card}"` with its own heading and a `PopOut` button. `BarsCard` holds a `BarChart` plus a 14-column table when docked, and a four-column strip when floating. |
| Table plumbing | `Views/Cells.cs` (row panel driven by a widths string), `Views/Grow.cs` (eased bar length as a `ScaleTransform`), `Views/ActionRowTemplates.cs`, `Views/WheelChain.cs` | |
| Charts | `Charts/Chart.cs` (base: inks, hover card, hairlines), `LineChart`, `BarChart`, `HistogramChart`, `PairedHistogramChart`, `Models.cs`, `DrawnText.cs`; geometry in `apps/zerg/src/Zerg.Core/Charts/` | Charts only paint. `Chart` takes its inks from Fluent brush keys through `SetResourceReference`. |
| Compare | `Views/CompareSection.xaml(.cs)`, `Views/RunPair`, `RunBars`, `ChangeText`; `CompareViewModel.cs`; data from `Zerg.Core/CompareSheet.cs` | One long `StackPanel` of cards. |
| Settings page | `Views/SettingsPage.xaml(.cs)`; `MainViewModel.Settings.cs`; `Settings.cs` | Five cards. |
| Floating panels | `PanelWindow.xaml(.cs)`, `Panels.cs` (`PanelSet`, `PanelInfo`), `Native/Overlay.cs`, `Native/WindowPlacement.cs`, `Views/Away.xaml`, `Views/Float.cs` | Six panels: `line`, `bars`, `actions`, `hline`, `hbars`, `hactions`. A drill-down has no panel of its own. |
| Tray | `TrayMenu.xaml(.cs)`, `Native/TrayIcon.cs` | A `ContextMenu` subclass. |
| Tests | `apps/zerg/tests/Zerg.Core.Tests/` | xUnit, `Zerg.Core` only. There are **no UI or view-model tests**; UI is verified in the running app with `tools/drive.cs`. |

---

## 3. Gap analysis: current UI against the design

"Restyle" means the structure stays and resources change. "Restructure" means
XAML or code is rearranged. "New" did not exist. "Remove" goes away.

### 3.1 Foundations and controls

| Design element (DESIGN.md section) | Today | Change | Kind |
|---|---|---|---|
| Colour tokens, dark and light (2) | Fluent brushes: 22 distinct `*Brush` keys (and 2 Fluent style keys) used across `App.xaml`, `MainWindow.xaml`, `PanelWindow.xaml`, 13 view files and `Charts/Chart.cs` | Own token brushes in `Themes/Dark.xaml` and `Themes/Light.xaml`; stop using Fluent keys | New + restyle |
| Type scale (2) | `Figure` 28 to 36, `Cell` 13, `Head` 12 SemiBold, `Label` 12 | Sizes 22 / 18 / 16 / 15 / 14 / 12.5 / 12 / 11.5 / 11 / 10.5 / 9.5; new `SmallFont`, `MonoFont` | Restyle + new |
| Measures (2) | Rows 32 / 30 / 44, padding 16, radius 8 | Rows 24 / 22 / 20 / 36, bars 36 / 40 / 64 / 30 / 24, radius 4 / 3 / 2 / 8 | Restyle |
| Own control templates (3, sheet 08) | Fluent templates; own templates only for `SessionButton`, `Chip`, `RowButton` | Button, ToggleButton, RadioButton (segment and tab), Slider, ScrollBar, ToolTip, TextBox, Expander, ContextMenu, MenuItem, Separator; `ThemeMode="None"` | New |
| Keycap, state tag, tick-box toggle, ghost button, caps label | None | New styles and small elements | New |

### 3.2 Main window

| Design element | Today | Change | Kind |
|---|---|---|---|
| 36 px title bar with mark, tabs, gear, caption buttons (3, 01) | Stock title bar; "Section" radio row in `MainWindow.xaml`; gear `ToggleButton` on the command bar | `WindowChrome`; tabs replace the Section row; gear moves up | Restructure |
| View is a tab while a parse is open (07) | No segment; `ImportCommand` is the only way in | Tab bound to `HasParse`, with file name and close mark | New |
| Command bar 40 px: pair with glyphs, Export, Import, toggles as tick boxes, keycap on Click-through, layout button (3, 01) | Two rows: bar, then "Section" + "Toggles" | One row; "Section" and "Toggles" captions go; layout button is new | Restructure + new |
| Figures band 64 px with marks and state tag (4, 01) | `UniformGrid` `Tiles` / `HealTiles` of five `Tile` cards (about 110 px) | One ruled band; three small marks; LIVE / ARMED / HELD / IDLE / SAVED tag | Restructure + new |
| Characters: one 30 px line, name-only rectangular chips, "+N" (4) | Wrapping pill chips with job, Hide / Show button, capped at 92 px | One line; overflow chip | Restructure |
| View head band (07) | `Views/ViewCard.xaml` card | 38 px band (parse open) or a drop area (nothing open) | Restyle |
| Status line 24 px with "N panels out · Dock all" (4, 02, 07) | Dot with pulsing ring, file, detail | Mono file name; right-hand panels note and Dock all link | Restyle + new |
| Two-column flush panes, each scrolling (1, 5) | Page `ScrollViewer` with a `StackPanel` | Pane host with a default arrangement | Restructure |

### 3.3 Live sections (Damage and Healing)

| Design element | Today | Change | Kind |
|---|---|---|---|
| Pane heading: grip, title, note, fold chevron, Pop out icon (3, 08) | `CardTitle` + `CardNote` + text `PopOut` button inside each card | One `Pane` control wraps each card | New + restructure |
| Bar chart removed; rows are the bars (4, decision 2) | `c:BarChart` above the table in `BarsCard.xaml` / `HealBarsCard.xaml`; `Bars`, `BarsHeight`, `HealBars`, `HealBarsHeight` in the view model | Delete the chart; add a back-shade rectangle to each row; `Fraction` on `ActorRow` / `HealerRow` | Remove + new |
| Grouped headings WEAPONSKILL / SKILLCHAIN / PET (4) | 14 flat headings ("WS Damage", "WS Avg", ...) | Second heading row; one-word headings | Restructure |
| Rows 24 / 22 / 20, text 12 | `RowLine` MinHeight 32, `RowButton` 30, hit rows 30 | Density | Restyle |
| Low accuracy mark (2, decisions 11, 13, 14) | Nothing | Rule then cut-out, graded; new setting | New |
| Party line (decision 7) | Nothing | New row; **needs a small `Zerg.Core` addition** | New (core + UI) |
| Owner's row marker | 2 px accent rectangle in the strip only | Amber triangle at the left of the owner's row in the docked table too | New |
| Actions: heading shaded, action rows with a small share bar, "Min, avg, max" mark (4) | Heading is a `StackPanel` of name and total; Share is text | Columns on the heading; two new marks | Restructure + new |
| Two shading switches (2, decision 12) | Nothing | `shadeCharacters` (on), `shadeActions` (off) | New |
| Drill-down as a pane with six ruled figures, histogram band and median tick, "Every hit" open with a bar per hit and a crit spark (4) | `DrillCard` under the Actions card; six rounded tiles; `Expander` closed; " ✦" appended to the damage text | Pane of its own; restyle; new marks | Restructure + new |
| Cumulative chart: end labels, no legend, restyled hover card (4) | Legend `ItemsControl` in `LineCard.xaml`; `EndLabels` only when floating | `EndLabels` always; legend removed | Restyle + remove |
| Stand-in: 46 px dashed strip (02) | `Views/Away.xaml` rounded bordered box | Restyle; pane shrinks to it | Restyle |

### 3.4 Compare, Settings, pop-outs, tray

| Design element | Today | Change | Kind |
|---|---|---|---|
| Compare: switches on the title line, slots side by side on one line each, six tiles in a 72 px band (03) | Card with wrap panel, two tall slots, six tile cards | Bands | Restructure |
| Compare rows shaded twice, run marked once per row, rows 36 (03) | `RunBars` cell with two bars; a tick before every figure in `RunPair` | Two half-height bands behind the row; `RunPair` loses its ticks | Restructure |
| Action well darker and indented; spread lines 23 px with change split in two columns (03) | `Under` style on `SubtleFillColorSecondaryBrush`; `ChangeText` stacks percentage over difference | Restyle; new two-column form of `ChangeText` | Restyle |
| Run colours blue and orange, each a setting (decision 19) | `RunABrush` / `RunBBrush` alias `Series1` / `Series2` in the theme files; `CompareViewModel.Show` reads `AppTheme.Series(0/1)` | Own colours, set at start and on change | New |
| Settings: eight ruled rows, label left, control right (06) | Five stacked cards | Restructure; keycaps; opacity preview | Restructure |
| Settings rows: Row shading, Low accuracy mark, Compare colours with picker (06) | Nothing | New rows; a colour picker popup (WPF has none) | New |
| Panel bar 30 px: pair, clock, total, then title; tools on hover (04, decision 4) | 32 px: title, pair, clock, total, slider, percent, Dock, always visible at 45% | Reorder; tools hidden at rest | Restructure |
| Strip shade strength per colour, 3 px colour edge, halo on all panel text (04) | Flat `Opacity="0.7"`; `DropShadowEffect` on the strip's cells only | Per-colour brush; halo on the panel body | Restyle |
| Tray menu heading line with state, clock, total (05, decision 5) | No heading | Non-interactive first item, shown only while armed or running (Q21) | New |

### 3.5 Layout

| Design element | Today | Change | Kind |
|---|---|---|---|
| Panes resized by dividers, folded, moved by their headings; per section; remembered; lock and reset; keyboard (10, decision 1) | Fixed order of cards | Split-tree model, pane host, drag, persistence | New |
| Single column under 700 px wide | Always single column | Fallback mode of the host | Restructure |

### 3.6 Removed outright

- `apps/zerg/src/Zerg/Charts/BarChart.cs`, the `BarRow` record in
  `Charts/Models.cs`, and the view-model properties that feed them.
  `apps/zerg/src/Zerg.Core/Charts/BarsLayout.cs` and its tests in
  `apps/zerg/tests/Zerg.Core.Tests/ChartLayoutTests.cs` become dead and are
  removed in phase 12.
- `LegendRow` and the `Legend` / `HealLegend` collections (docked legend).
- The "Section" and "Toggles" captions, the Hide / Show characters button, the
  `Card` style, `Tile`, `ArmedRing`'s pulse storyboard (the state tag's ring
  replaces it; decided, Q17).
- Mica and every Fluent resource key.

---

## 4. Design tokens

Values were read from the SVG sources (not from the PNGs) and checked against
`DESIGN.md` section 2. Where the two differ, the decisions in section 8
say so. Resource keys are proposals; they follow the existing convention in
`Themes/Dark.xaml` (a `Color` named `X`, a `SolidColorBrush` named `XBrush`).

### 4.1 Colour: surfaces, rules, ink

Define each as a `Color` and a brush in **both** `Themes/Dark.xaml` and
`Themes/Light.xaml`.

| Key (`Color` / brush) | Dark | Light | Use |
|---|---|---|---|
| `Bg0` / `Bg0Brush` | `#0A0C10` | `#E9EBEF` | Window, title bar, status line, text box well, Compare's action well |
| `Bg1` / `Bg1Brush` | `#0F1217` | `#FFFFFF` | Pane surface, command bar, bands |
| `Bg2` / `Bg2Brush` | `#151921` | `#F4F5F7` | Heading row of a character, Party line, hovered heading, menus, slot cards, drill figure row, disabled button face |
| `Bg3` / `Bg3Brush` | `#1C222C` | `#E7EAEF` | Selected row, button at rest, toggle that is on, small-bar track |
| `Bg4` / `Bg4Brush` | `#262D3A` | `#DBDFE6` | Control hover, picked segment, tooltip, hover card, hovered menu line |
| `Line` / `LineBrush` | `#1B2029` | `#ECEEF2` | Rule between rows; edge of a disabled control |
| `Line2` / `Line2Brush` | `#2A313D` | `#D5D9E0` | Rule between panes and bands; control edge; column-group rules |
| `Line3` / `Line3Brush` | `#3D4656` | `#B4BBC7` | Window frame, menu and tooltip edge, scroll thumb, divider grip, keycap edge |
| `Text1` / `Text1Brush` | `#E9ECF2` | `#14171D` | Figures, names |
| `Text2` / `Text2Brush` | `#A6AFBF` | `#474E5C` | Secondary figures, button text, job beside a name on a shaded row |
| `Text3` / `Text3Brush` | `#838DA5` | `#687182` | Labels, column headings, notes, captions |
| `Text4` / `Text4Brush` | `#5E677A` | `#A0A7B4` | A dash on a plain row, disabled text, grip dots at rest |
| `ShadedDash` / `ShadedDashBrush` | `#949EB2` | `#58606F` | A dash on a shaded row |
| `MarkTile` / `MarkTileBrush` | `#07080C` | `#07080C` | The ink tile behind the amber Z (sheet 09 keeps it dark) |

### 4.2 Colour: meaning

| Key | Dark | Light | Use |
|---|---|---|---|
| `Accent` / `AccentBrush` | `#F2A33A` | `#A35F00` | Brand, selected, active tab underline, focus ring, slider, armed and held |
| `OnAccent` / `OnAccentBrush` | `#191004` | `#FFFFFF` (from sheet 09's tick) | Text and glyphs on an accent fill |
| `Live` / `LiveBrush` | `#22E05A` | `#0B7A45` | Counting; a better change |
| `OnLive` / `OnLiveBrush` | `#051306` | `#FFFFFF` (not in the design; Q11) | Text on a live fill |
| `Crit` / `CritBrush` | `#FF5063` | `#C42B3A` | Dropped, error, a worse change, low accuracy mark |
| `OnCrit` / `OnCritBrush` | `#190405` | `#FFFFFF` (not in the design; Q11) | Text on a crit fill |
| `CloseHoverBrush` | `#C42B1C` | `#C42B1C` | Caption close button under the pointer (glyph `#FFFFFF`) |
| `RunA` / `RunABrush` | `#3987E5` | `#2A78D6` (today's `Series1`) | Compare's baseline run. A user setting; these are the installed values |
| `RunB` / `RunBBrush` | `#E8792B` | `#C94F18` (today's `Series2`) | Compare's compared run. A user setting. **New value in dark** (was `#D95926`) |
| `RunInkBrush` | `#101010` | `#101010` or `Text1`, whichever is clearer on the colour | Letter on a run's badge |
| `Job*`, `Series1` to `Series18` | unchanged | unchanged | Leave both files' job and fallback colours exactly as they are |

State-tag tints follow one rule: fill is the colour at 12% over `Bg1`, edge is
the colour at 45% over `Bg1` (recomputed while writing this plan; it
reproduces the sheet's values exactly).

| Key | Dark fill / edge | Light fill / edge |
|---|---|---|
| `TagLiveFillBrush` / `TagLiveEdgeBrush` | `#112B1F` / `#186F35` | `#E2EFE9` / `#91C3AB` (sheet 09) |
| `TagIdleFillBrush` / `TagIdleEdgeBrush` (from `Text3`; also SAVED) | `#1D2128` / `#434957` | compute by the rule |
| `TagHeldFillBrush` / `TagHeldEdgeBrush` (ARMED and HELD) | `#2A231B` / `#755327` | compute by the rule |

Overlays (alpha over whatever is beneath):

| Key | Value | Use |
|---|---|---|
| `RowHoverBrush` | `Text1` at 5% | Hovered row, over the whole row, with a `Line3` rule above and below |
| `ActionShadeBrush` | `Text1` at 10% (light 8%) | Back-shade of an action or heal row when "Shade actions and heals" is on |
| `DropZoneFillBrush` | `Accent` at 15% | Where a dragged pane will land (kit draws 14%, sheet 10 16%); edge is `Accent` 1.5 px |
| `EdgeZoneBrush` | `Accent` at 28% | 5 px band along each edge of the section during a drag |
| `DividerHoverBrush` | `Accent` at 55% | 3 px divider under the pointer; solid `Accent` while dragged |
| `HistogramBandBrush` | `Text1` at 4% | The middle-half band on a histogram |

### 4.3 Colour: floating panels (always dark)

Proposed as a third dictionary, `Themes/Panel.xaml` (new), merged into
`PanelWindow.Resources`. It **redefines the same keys** as section 4.1, so a
card inside a panel picks up panel values through its ordinary
`DynamicResource` lookups with no trigger per brush.

| Key redefined in `Themes/Panel.xaml` | Value |
|---|---|
| Backdrop (set in code by `PanelWindow.Tint`) | Black at the panel's opacity, as today (the sheets draw `#06080C`; Q8) |
| `Bg2Brush` | White at 7% (`#12FFFFFF`) |
| `Bg3Brush` | White at 13% (`#21FFFFFF`) |
| `Bg4Brush` | White at 20% (`#33FFFFFF`) |
| `LineBrush` | White at 8% (`#14FFFFFF`) |
| `Line2Brush` | White at 16% (`#29FFFFFF`) |
| `Line3Brush` | White at 28% (`#47FFFFFF`) |
| `Text1Brush` | `#E9ECF2` |
| `Text2Brush` | `#B9C1CF` |
| `Text3Brush` | `#98A2B6` |
| `Text4Brush` | `#6F798C` |
| `Accent`, `Live`, `Crit` and their `On*` | The dark theme's values, whatever the app theme |
| `HoverCardBrush` (added in Phase 1; `Bg4` in the two theme files) | `#262D3A`, solid. A chart's hover card must not be see-through (`Charts/Chart.cs` says why), and `Bg4Brush` is see-through here. Confirmed by the owner on 2026-10-06 (section 10, Phase 1) |
| `ChartSurfaceBrush` | `#101010` as today (already overridden in `PanelWindow.xaml`) |
| Halo | Black at 60%, 2.4 px, around every glyph (see risk R7) |

### 4.4 Row shade strengths

The strength (alpha) of a back-shade is computed, not listed. The rule was
re-run while writing this plan and reproduces `DESIGN.md`'s table exactly for
the live tables:

- **Rule:** the largest whole percentage, no greater than the cap, at which
  `Text2` drawn on (colour at that alpha over `Bg1`) still has a WCAG contrast
  ratio of at least the target.
- **Dark:** target 4.5, cap 30%. Results: 30% for most jobs; BST 29, BRD 28,
  MNK 27, SMN 23, PLD 22, WHM 20.
- **Light:** target 5.5, cap 22%. Results: 22% for all but WAR 21.
- **Pop-out strip:** cap 34%. The sheet draws 34% for most, SMN 31, WHM 27.
  The inputs that give exactly those two numbers could not be recovered
  (panel `Text2` at 4.5 over `#06080C` gives SMN 32, WHM 28). See Q13.
- **Compare run band:** the largest percentage, no greater than 26%, at which
  `Crit` text on (run colour at that alpha over `Bg1`) keeps 4.5. This gives
  19% for `#E8792B` and 10% for white as `DESIGN.md` says, and 21% for
  `#3987E5` where `DESIGN.md` says 20% (Q12).

### 4.5 Type

Font resources in `App.xaml` (existing keys kept, two added):

| Key | Value | Status |
|---|---|---|
| `TextFont` | `Segoe UI Variable Text, Segoe UI` | Existing |
| `NumberFont` | `Segoe UI Variable Display, Segoe UI` | Existing |
| `SmallFont` | `Segoe UI Variable Small, Segoe UI` | New (verify WPF resolves the name; R4) |
| `MonoFont` | `Cascadia Mono, Consolas` | New (see R4 and Q19) |
| `IconFont` | `Segoe Fluent Icons, Segoe MDL2 Assets` | Existing |

Type styles (`TextBlock` styles in `App.xaml`). Existing keys are re-valued so
their many usages follow; new keys are marked.

| Style key | Font | Size / weight | Colour | Used for |
|---|---|---|---|---|
| `Figure` (existing) | `NumberFont` | 22 / SemiBold, tabular | `Text1` | The five figures |
| `PageTitle` (new) | `NumberFont` | 18 / SemiBold | `Text1` | "Settings" |
| `TileFigure` (new) | `NumberFont` | 16 / SemiBold, tabular | `Text1` | Compare tile figures |
| `DrillFigure` (new) | `NumberFont` | 15 / SemiBold, tabular | `Text1` | Drill-down's six figures |
| `DrillTitle` (new) | `TextFont` | 14 / SemiBold | `Text1` | Drill-down title |
| `SlotTitle` (new) | `TextFont` | 13 / SemiBold | `Text1` | A parse's name in a slot or the View band |
| `PaneTitle` (replaces `CardTitle`) | `TextFont` | 12.5 / SemiBold | `Text1` | Pane title, section tab, menu line (menu lines are Regular) |
| `Cell`, `CellText` (existing) | `TextFont` | 12 / Regular, tabular; totals and the picked row SemiBold | `Text1`, or `Text2` for secondary figures | Table cells |
| `PairCell` (new) | `TextFont` | 11.5 / Regular, tabular | `Text1` | A over B cells in Compare; toggle and chip labels |
| `Head`, `HeadText` (existing) | `TextFont` | 11 / Regular, sentence case. The design asks for Medium, which WPF draws as SemiBold here (R4), so Q19's Regular applies | `Text3` | Column headings |
| `Label` (existing key, now the "note" role) | `TextFont` | 11 / Regular | `Text3` | Notes, pane note, status detail |
| `Badge` (existing) | `TextFont` | 10.5 / Regular (11 in the per-character table's Job column) | `Text2` on shaded rows, `Text3` elsewhere | Job beside a name |
| `Caps` (new; a style for the `Views/Caps` element, not for `TextBlock`) | `SmallFont` | 9.5 / SemiBold, upper case, +0.7 tracking | `Text3` | Figure labels, "CHARACTERS 12/12", "BASELINE" |
| `GroupCaps` (new; for `Views/Caps`) | `SmallFont` | 9 / SemiBold, upper case, +0.8 (column groups, the style's value) or +0.6 (drill labels, strip headings: set `Tracking` where used) | `Text3` | WEAPONSKILL / SKILLCHAIN / PET; MIN / AVERAGE / ... |
| `Mono` (new) | `MonoFont` | 11 (file name, hit time: the style's size), 11.5 (paths, colour codes), 10 (keycap) | `Text2` (the style's) or `Text3` | Strings read as strings |
| `TagText` (new; for `Views/Caps`) | `TextFont` | 9.5 / Bold, +0.6 | The state's colour | LIVE / ARMED / HELD / IDLE / SAVED |

Hover-card text is 10.5 (heading SemiBold `Text1`, names `Text2`, values
SemiBold `Text1`). Chart axis labels are 10 `Text3`; end labels 10.5.

### 4.6 Measures

Proposed as `sys:Double` (or `Thickness`, `CornerRadius`, `GridLength`)
resources in `App.xaml`.

| Key | Value | Notes |
|---|---|---|
| `TitleBarHeight` | 36 | Caption buttons 46 wide (the sheets draw the last one 45 to leave the frame) |
| `CommandBarHeight` | 40 | Buttons 26 tall at y 7; toggles 24 tall at y 8; group separators 1 by 20, `Line2` |
| `FiguresBandHeight` | 64 | Five equal cells; text starts 14 in; cell rules inset 10 top and bottom |
| `ViewBandHeight` | 38 | `Bg2` with a 2 px `Accent` edge at the left |
| `CharactersBandHeight` | 30 | Chips 20 tall, gap 4 |
| `PaneHeadingHeight` | 30 | Grip dots at x 8 to 14; title at x 22; rule under it is `Line` |
| `ColumnHeadHeight` / `GroupedHeadHeight` | 24 / 38 | Rule under headings is `Line2` |
| `RowHeight` | 24 | 23 px of row and a 1 px `Line` rule |
| `SubRowHeight` | 22 | Heading and action rows in Actions and Heals |
| `HitRowHeight` | 20 | Hit list, pop-out strip (plus a 1 px gap in the strip) |
| `PairRowHeight` | 36 | Compare, By character (Q9 for 32 and 28) |
| `PartyRowHeight` | 26 | `Bg2`, `Line2` rule above |
| `StatusBarHeight` | 24 | Dot radius 4 at x 16; file name at x 30 |
| `CellPadding` | 8 left and right | Pane text starts at 12 |
| `ControlRadius` / `ChipRadius` / `BarRadius` / `WindowRadius` | 4 / 3 / 2 / 8 | Tooltip and hover card 5; combo list and picker 6; state tag 9 (a pill); small bars 1.5 |
| `PaneMinWidth` / `PaneMinHeight` | 320 / 132 | |
| `NarrowWidth` | 700 | Below it, one column |
| Divider | 1 px rule, 7 px hit area, grip 3 by 22 (`Line3`, radius 1.5) | Hover: 3 px `Accent` at 55%, grip 5 by 26 solid |
| Heading grip | Six dots, radius 1, in a 5 by 9 block | `Text4`; `Text1` under the pointer |
| Scroll bar | Thumb 6 wide, radius 3, `Line3`, 3 px from the edge; 10 wide `Text3` under the pointer; no track or arrows | Drawn over content |
| Focus ring | 1.5 px `Accent`, 2 px outside the control edge, radius 5.5 | Text box and combo show a 1 px `Accent` edge instead |
| Small share bar | 42 by 6 (Actions), 48 by 6 (character cells), radius 1.5; track `Bg3`; fill `Text3` (actions) or the character's colour | |
| Low accuracy cut-out | 46 by 18, radius 2, centred on the figure, inset 3 from the row's top and bottom; rule 36 by 2, radius 1, 3 px under the figure | |
| Swatch | 8 by 8, radius 2 (10 by 10 today) | 7 by 7 radius 1.5 in hover cards |
| Shadows | Menu: black 60%, blur about 22, offset 8 down. Tooltip, hover card, carried pane: black 55%, blur about 12, offset 4. Combo list, picker: black 50%, blur about 14, offset 5 | The SVG's `stdDeviation` values are 11, 6 and 7; a WPF `BlurRadius` is roughly twice that |

### 4.7 Icons (Segoe Fluent Icons code points read from the SVGs)

| Use | Code point | | Use | Code point |
|---|---|---|---|---|
| Minimize | `E921` | | Start / Resume (play) | `E768` |
| Maximize | `E922` (restore `E923`, not drawn) | | Pause | `E769` |
| Close; tab close mark (8 px) | `E8BB` | | Restart | `E72C` |
| Settings gear (14 px) | `E713` | | Cancel | `E711` |
| Pop out (12 px) | `E8A7` | | Confirm; tick; export done | `E73E` |
| Dock (panel bar) | `E73F` | | Lock (click-through) | `E72E` |
| Folder | `E8B7` | | Warning (error notes) | `E7BA` |
| Theme light / dark / system | `E706` / `E708` / `E7F4` | | | |

Drawn as paths, not glyphs: carets and chevrons (two segments, stroke 1.3,
round joins; a 7 by 3.5 or 8 by 4 box), the toggle tick (stroke 1.6), the
layout button (a 13 by 11 rectangle
split by one vertical and one horizontal line), the owner marker (a 4 by 7
filled `Accent` triangle), and the app mark. The mark is an 18 by 18 tile
(radius 4, `MarkTile`, `Line3` edge) with this path in `Accent`, in the tile's
own coordinates:
`M5,5 L13,5 L13,7.2 L8.3,11 L13,11 L13,13.2 L5,13.2 L5,11 L9.7,7.2 L5,7.2 Z`.

### 4.8 Table columns measured from the sheets

Right edges of right-aligned figures at a pane width of 940. Express them as
`Cells` share weights (a number followed by `*`) so they scale with the pane;
the numbers below are the pixel widths at 930 plus a 10 px margin for the
scroll bar.

| Table | Column widths |
|---|---|
| Damage by character (14 columns) | Character 138, Job 70, Damage 68, Damage % 64, DPS 54, Accuracy 62, then WEAPONSKILL: Damage 68, Avg 52, % 54, Acc 58; SKILLCHAIN: Damage 64, % 54; PET: Damage 66, Acc 58. Group rules at x 457.5, 689.5 and 807.5 |
| Actions | Action 278 (caret at 12, swatch at 28, text at 42), Hits 50, Miss 50, Total 76, Avg 60, Min 56, Max 60, "Min · avg · max" 160 (mark drawn 132 wide), Share 104 (bar 42 wide then the figure), trailing 36 |
| Healing by character (8 columns) | Character 190, Job 100, Healing 104, Healing % 105, HPS 96, Casts 96, Avg / cast 115, Pet Healing 124 |
| Heals | Heal 360, Casts 56, Total 110, Avg 96, Min 96, Max 96, Share 104, trailing 12 (right edges at 408, 518, 614, 710, 806, 910) |
| Compare, By character | Name 160 (caret 12, swatch 28, text 42), Job 96 (run mark at 168, text at 182), Damage right edge 334, change 412, DPS 478, change 550, Accuracy 622, change 692, WS Avg 758, change 830, Share 898 |
| Hit list | Time at 12 (mono), Target at 72, Damage right edge 341 from the pane's left, "vs avg" bar centred on a tick at 393, figure right edge 477 (pane 499 wide) |

---

## 5. Phases

Each phase lists numbered steps, then acceptance criteria and how to verify.
"Build" means the build command in section 1 succeeds with no new warnings.
"Tests" means the test command passes. "Naming check" means the command in
`apps/zerg/CLAUDE.md`, "The naming rule", prints nothing.

### Phase 0: baseline and guard rails

No source changes.

0.1. Read `apps/zerg/CLAUDE.md` and `apps/zerg/RULES.md` in full, and
`apps/zerg-mockup/DESIGN.md` sections 1 to 5.

0.2. Generate the synthetic fixture (`python apps/zerg/tools/gen-test-events.py`)
and run the replay recipe once on the unmodified app. Save `tools/drive.cs`
`snap` captures of: Damage, Healing, View with a parse, Compare with both
slots filled and one row opened onto an action, Settings, each of the six
panels, the tray menu, and the light theme. Keep them outside the repository
(a scratch folder). These are the "before" pictures for regression checks.

0.3. Record the output of `drive.cs text <pid>` and `names <pid>` for the same
states. The names list is the contract phase by phase: any name that
disappears later is a regression unless this plan says it is removed.

0.4. Note CPU use at the default draw frequency with the Damage section on
screen and a session running (Release build; `apps/zerg/CLAUDE.md` gives
23 to 25% of one core as the measured baseline). Phase 12 compares against it.
**Dropped by the owner on 2026-10-05** (section 10): no CPU baseline is kept
and no later step compares against one.

**Acceptance:** build, tests (227), naming check all clean; captures and
names saved.

### Phase 1: tokens, fonts and type

Goal: Zerg's own colours and type exist as resources, and the running app
takes its colours from them, while every Fluent template is still in place.

1.1. **Add the tokens.** In `apps/zerg/src/Zerg/Themes/Dark.xaml` and
`Themes/Light.xaml` add the `Color` and brush resources of sections 4.1 and
4.2 and the overlays. Do not touch `Series*` or `Job*`. Change `RunBBrush` in
`Dark.xaml` only in step 8.1. No visible change. (DESIGN.md 2; `08-kit.svg`.)
*As built (2026-10-06):* every key of 4.1 and 4.2, the six tag brushes and
the six overlays, in both files, plus `HoverCardBrush` (section 4.3). `RunA`
and `RunB` were **not** added as `Color` keys and the three run brushes are
exactly as they were: step 8.1 owns them.

1.2. **Give code access to tokens.** In `apps/zerg/src/Zerg/AppTheme.cs` add a
`public static Color Token(string key, bool dark)` beside `Job` and `Series`,
reading from the same `Set(dark)` dictionary. The view models need token
colours (surface, `Text2`, `Crit`) to compute shade strengths. *As built:* it
reads `Dark.xaml` or `Light.xaml` only, so `Token("Text2", dark: true)` is
the application's `#A6AFBF`, not a panel's `#B9C1CF`; the strip's shade
strength (Q13) needs the panel value from somewhere else.

1.3. **Add fonts and measures.** In `apps/zerg/src/Zerg/App.xaml` add
`SmallFont`, `MonoFont` and the measure resources of section 4.6. Convert
`<Application.Resources>` to an explicit `<ResourceDictionary>` with a
`MergedDictionaries` list (empty for now) so later steps can merge files.
*As built:* the keyed measures of 4.6 only (heights, `CellPadding` as a
`Thickness`, `ControlRadius` / `ChipRadius` / `WindowRadius` as
`CornerRadius`, `BarRadius` as a number because a `Rectangle`'s radius is
one, the pane minimums, `NarrowWidth`). The rows of 4.6 with no key
(divider, grip, scroll bar, focus ring, share bar, cut-out, shadows) were
not made into resources.

1.4. **Alias the Fluent keys onto tokens.** In both theme files, define a
brush under each Fluent key Zerg uses, pointing at the token colour. Because
`AppTheme.Refresh` adds Zerg's dictionary after WPF has added the Fluent one,
Zerg's definition wins for every `DynamicResource` lookup that reaches the
application's resources. **Done and verified 2026-10-06 (section 10): the
aliases hold**, at a start under System, through System to Dark on a dark
Windows, and through Dark to Light and back. WPF keeps its dictionary first
in the list and replaces it where it stands, so it never ends up after
Zerg's. This step said the aliases would also reach "inside Fluent's own
templates"; that is **not** what was seen: a checked toggle and the picked
section stayed Fluent's blue with `AccentFillColorDefaultBrush` aliased to
amber. The theme settles what its templates draw inside its own dictionary,
or most of it. So the aliases recoloured Zerg's own uses of those keys, and
step 1.5 has since removed every such use: they are kept only because step
2.5 is where they are removed, and whether any Fluent template still takes
one was not determined. The aliases never reach a floating panel, whose
window carries its own Fluent set; that is expected. The mapping:

| Fluent key in use | Token |
|---|---|
| `TextFillColorPrimaryBrush` | `Text1` |
| `TextFillColorSecondaryBrush` | `Text2` |
| `TextFillColorTertiaryBrush` | `Text3` |
| `AccentFillColorDefaultBrush`, `AccentTextFillColorPrimaryBrush` | `Accent` |
| `TextOnAccentFillColorPrimaryBrush` | `OnAccent` |
| `SystemFillColorSuccessBrush` | `Live` |
| `SystemFillColorCautionBrush` | `Accent` |
| `SystemFillColorCriticalBrush` | `Crit` |
| `SystemFillColorNeutralBrush` | `Text3` |
| `CardBackgroundFillColorDefaultBrush` | `Bg1` |
| `CardStrokeColorDefaultBrush`, `ControlStrokeColorDefaultBrush`, `ControlElevationBorderBrush`, `SurfaceStrokeColorDefaultBrush` | `Line2` |
| `ControlStrongStrokeColorDefaultBrush` | `Line3` |
| `DividerStrokeColorDefaultBrush` | `Line` |
| `SubtleFillColorSecondaryBrush` | `Bg2` |
| `SubtleFillColorTertiaryBrush`, `ControlFillColorDefaultBrush` | `Bg3` |
| `ControlFillColorSecondaryBrush`, `SolidBackgroundFillColorQuarternaryBrush` | `Bg4` |

Also set `MainWindow`'s `Background` to `{DynamicResource Bg0Brush}` and
`ChartSurfaceBrush` to `Bg1` in both theme files. The window stops being
Mica-backed here. *As built:* `MainWindow` also has
`Foreground="{DynamicResource Text1Brush}"`, so text with no brush of its own
is `Text1` and not Fluent's white. Its title bar is still Windows' own until
phase 3.

1.5. **Panel tokens first** (moved forward from step 2.5 by the owner,
2026-10-05; section 10). Before any card is migrated, create
`Themes/Panel.xaml` (section 4.3) and merge it in `PanelWindow.xaml`'s
`Window.Resources`, leaving `ThemeMode="Dark"` on the window until step 2.5.
Without it, a migrated card inside a panel would take the application's
token values, which under the light theme is dark ink on a black panel.
Check a panel under the light theme after the first card is migrated.

**Then migrate usages, one file per step, mechanically.** Replace each Fluent
key with its token key in: `App.xaml`, `MainWindow.xaml`, `PanelWindow.xaml`,
`Views/ActionsCard.xaml`, `Away.xaml`, `BarsCard.xaml`, `ChangeText.xaml`,
`CompareSection.xaml`, `DrillCard.xaml`, `HealActionsCard.xaml`,
`HealBarsCard.xaml`, `HealDrillCard.xaml`, `LineCard.xaml`, `RunBars.xaml`,
`SettingsPage.xaml`, `ViewCard.xaml`, and the eight `SetResourceReference`
calls in the constructor of `Charts/Chart.cs` (`Ink` to `Text1Brush`, `Ink2`
to `Text2Brush`, `Muted` to `Text3Brush`, `GridInk` to `LineBrush`, `AxisInk`
to `Line2Brush`, `CardFill` to `HoverCardBrush`, `CardStroke` to
`Line3Brush`; `Surface` stays `ChartSurfaceBrush`). Where the same Fluent key
served two roles (for example `TextFillColorTertiaryBrush` for both labels
and dashes), pick the token by role using section 4.1. Build after each
file; batch the on-screen checks (the owner's rule, section 10).

*As built (2026-10-06), and where it departs from the lines above:*

- `CardFill` goes to `HoverCardBrush`, not `Bg4Brush` as this step first
  said. In a panel `Bg4Brush` is white at 20%, and the hover card is meant
  to be solid. `HoverCardBrush` is `Bg4` in the main window, so nothing
  differs there.
- By role: the dash between a character's job and total in the Actions and
  Heals headings is `Text4`; the text on Start is `OnLive` while idle and
  `OnCrit` as Confirm, `OnAccent` while armed, and on the second button
  while held. Everything else took the token the table in 1.4 gives its old
  key.
- **Replace the longer key first.** `AccentTextFillColorPrimaryBrush`
  contains `TextFillColorPrimaryBrush`; a plain text replacement made
  `AccentText1Brush` of it in four files. A `DynamicResource` to a key that
  does not exist is not an error, so this would have shown only as missing
  colour. Found by listing every key in use against the keys the theme
  files define; do that after any such edit.
- `PanelWindow.xaml` still has `ThemeMode="Dark"` and its two literal
  brushes (`#33FFFFFF`, `#22FFFFFF`): step 2.5 owns those.
- `{DynamicResource AccentButtonStyle}` in `Views/ViewCard.xaml` and
  `DefaultToolTipStyle` in `App.xaml` are styles, not brushes, and are still
  there: step 2.2 owns them.

1.6. **Re-value the type styles** in `App.xaml` to section 4.5 (`Figure`,
`Label`, `Badge`, `Cell`, `CellText`, `Head`, `HeadText`, `Swatch`) and add
the new ones. Remove per-element `FontSize` overrides that now disagree (for
example `FontSize="36"` on the hero tiles in `MainWindow.xaml`, `FontSize="13"`
on names in the row templates). Rows will look loose until phase 6 tightens
their heights; that is expected.

*As built (2026-10-06):*

- Re-valued: `Figure` 22; `Label` 11 in `Text3`; `Badge` 10.5; `Cell` and
  `CellText` 12 (padding from `CellPadding`); `Head` and `HeadText` 11
  Regular in `Text3`; `Swatch` 8 by 8.
- Added and **used nowhere yet**: `TileFigure`, `DrillFigure`, `PageTitle`,
  `DrillTitle`, `SlotTitle`, `PaneTitle`, `PairCell`, `Mono`, and for
  `Views/Caps` the styles `Caps`, `GroupCaps`, `TagText`. The phase that
  rebuilds each screen puts them on. So the "Settings" title is still 22,
  a card's title (`CardTitle`) still 16 in `NumberFont`, the drill-down's
  six figures still 20, Compare's tile figures still 20, and no label is
  in capitals. Whether Phase 1 should have put them on is a question for
  the owner (section 10).
- Overrides removed or changed: the two hero figures and the clock lose
  `FontSize="36"` (and the other three figures the 8-unit top margin that
  lined them up with a 36-unit figure); names, the dash and the total in
  the Actions and Heals headings, and names in the per-character and
  Compare rows, lose `FontSize="13"` and are 12; an empty table's line and
  the "Drop an exported parse here" line go from 13 to 12 (sheet 07 draws
  them at 12); the pet figure in a Heals heading 13 to 12; `ChangeText`'s
  main figure 13 to 12; `RunPair`'s figures 13 to 11.5 (the `PairCell`
  size); two overrides that had become the style's own size are gone.
- Left alone on purpose, for the phase that rebuilds the thing: icon sizes,
  the session buttons (2.3), chips and the "Section" / "Toggles" /
  characters labels (3, 4), the status line (3.5), `RowLine`'s 32-unit
  minimum height and the other row heights (6), the strip's heading size
  (10), every size drawn by a chart (7).

1.7. **Tracked caps.** Add `apps/zerg/src/Zerg/Views/Caps.cs` (new): a small
`FrameworkElement` that draws upper-case text with extra advance between
glyphs. WPF's `TextBlock` has no letter-spacing property. Give it an
automation peer that reports the text, so `drive.cs text` still reads labels.
See R4.

*As built (2026-10-06), which is not how this step first described it:* the
line is **not** built from the font's glyph typeface. It is shaped by the
text formatter through `Charts/DrawnText.cs` (which gained `WidthSpaced` and
`DrawSpaced`), and each `GlyphRun` the formatter made is drawn again with
every advance lengthened by the tracking. A glyph typeface alone gives
neither tabular figures (R4 asks for them wherever digits are drawn, and
"CHARACTERS 12/12" has digits) nor a fallback font for a character the face
lacks; the formatter gives both. Properties: `Text`, `Tracking` (in units,
after every glyph but the last), and the inherited `FontFamily`, `FontSize`,
`FontWeight` and `Foreground`, so `TagText` (Bold, `TextFont`) is the same
element. The text is taken as written and drawn in capitals; **the peer
reports it as written** ("Total damage", control type Text), which is what
the label reported as a `TextBlock`. Checked off screen only: nothing in the
app uses it yet (section 10).

**Acceptance:** the app runs in the new palette in both themes (dark surfaces
are blue-black, accent is amber, the clock is signal green while live). No
Fluent brush key remains in `apps/zerg/src/Zerg` except the aliases in the two
theme files (search for `FillColor` and `StrokeColor`). All automation names
from step 0.3 are still present. Build, tests, naming check.

**Verify visually against:** the colour and type blocks of `08-kit.svg`. Only
colours and font sizes are expected to match at this point, not layout.

### Phase 2: control kit, and leaving Fluent

Goal: every stock control is drawn by Zerg's own template; `ThemeMode` is
`None`.

2.1. Create `apps/zerg/src/Zerg/Themes/Controls.xaml` (new) and merge it in
`App.xaml`. Add styles **with keys first** so nothing changes until a control
opts in. One step per control, each compared with its drawing on `08-kit.svg`
at rest, hovered, pressed, focused and disabled:

| Step | Style keys (proposed) | Specification |
|---|---|---|
| 2.1a | `ZButton`, `GhostButton`, `FilledButton`, `IconButton` | 26 tall, radius 4, 12 px text. Rest `Bg3` / `Line2`; hover `Bg4` / `Line3`; pressed `Bg2`; disabled `Bg2` / `Line` with `Text4`; focus ring. Ghost: no face, `Line2` edge, `Text2` text, until hovered. Filled ("the one to press"): `Accent` with `OnAccent` SemiBold text |
| 2.1b | `TickToggle` | 24 tall. On: `Bg3`, `Line3` edge, a 12 by 12 `Accent` box (radius 2.5) with an `OnAccent` tick, label `Text1` 11.5 Regular (the design's Medium, which WPF would draw SemiBold: R4, Q19). Off: no face, `Line2` edge, an empty 11 by 11 box edged `Text3`, label `Text2`. A `Tag`-like slot at the right for a keycap |
| 2.1c | `Segment` (re-template the existing key), `SmallSegment` | A shared 24 tall frame (`Bg1`, `Line2`, radius 4); the picked segment is a 20 tall `Bg4` slab (radius 3) inset 2, label `Text1` SemiBold; others `Text2`. The frame belongs to the container, so add a `SegmentGroup` `Border` style to wrap a row of segments |
| 2.1d | `SectionTab` | `RadioButton`. Label 12.5; active `Text1` SemiBold with a 2 px `Accent` underline (radius 1) the width of the label at the bottom of the title bar; others `Text2` |
| 2.1e | `ZSlider` | 4 px track `Bg4` (radius 2), `Accent` fill, thumb a 7 px ring (`Bg1` fill, 2 px `Accent` stroke) with a 2.5 px `Accent` centre. Keep `MinWidth="0"` usable |
| 2.1f | `ZScrollBar`, `ZScrollViewer` | Thumb only, per section 4.6. The viewer's template overlays the bar on the content (as Fluent does) so the existing 14 px right margins and 12 px bottom margins stay correct |
| 2.1g | `ZToolTip` | `Bg4`, `Line3`, radius 5, 11.5 `Text1`, padding 10 by 8, shadow, left-aligned text (keep the reason given in `App.xaml` for the alignment setter) |
| 2.1h | `ZTextBox` | 28 tall, `Bg0` well, `Line2` edge, `Accent` edge when focused, `MonoFont` 11.5 |
| 2.1i | `ZExpander` | A heading row with a drawn caret; used by "Data table" in Compare |
| 2.1j | `ZContextMenu`, `ZMenuItem`, `ZSeparator` | Menu `Bg2`, `Line3` edge, radius 8, shadow. Lines 28 tall, 12.5 text, a fixed 24 px tick column at x 12 with an `Accent` tick or a `Text2` glyph, hovered line a `Bg4` slab inset 4 (radius 4), hot key as a keycap at the right, submenu chevron, disabled `Text4`. Separator `Line2`. This absorbs the hand-made tick column in `TrayMenu.xaml` |
| 2.1k | `Keycap` | 16 or 18 tall, `Bg1`, `Line3` edge, radius 3, a second `Line3` rule along the bottom edge, `MonoFont` 10 `Text2` |
| 2.1l | `ZComboBox` | Drawn on the kit "for completeness"; Zerg has no combo box. **Skip** unless one is introduced |

2.2. Re-point Zerg's own keyed styles in `App.xaml` at the new ones instead of
Fluent's type-keyed styles: `SmallButton` and `SmallToggle` (today
`BasedOn="{StaticResource {x:Type Button}}"` and `{x:Type ToggleButton}`),
`Segment`, `SmallSegment`, `PopOut`, and the local styles in
`TrayMenu.xaml` and `Views/SettingsPage.xaml` that are `BasedOn` a type key.
Replace `{DynamicResource AccentButtonStyle}` in `Views/ViewCard.xaml` with
`FilledButton`, and the `DefaultToolTipStyle` base in `App.xaml` with
`ZToolTip`.

2.3. Re-style the session pair in `App.xaml` (`SessionButton`, `StartButton`,
`SecondButton`) to the kit's six looks, keyed on the existing
`View.StartLook` and `View.SecondLook` values from
`apps/zerg/src/Zerg.Core/SessionView.cs` (no core change):

| Look | Face | Text | Glyph |
|---|---|---|---|
| Start, `Idle` | `Live` | `OnLive` | `E768` |
| Start, `Armed` | `Accent` | `OnAccent` | `E72C` |
| Start, `Running` | `Bg3` / `Line2` | `Text1` | `E72C` |
| Start, `Confirm` | `Crit` | `OnCrit` | `E73E` |
| Start, `Locked` | `Bg2` / `Line` | `Text4` | `E768` |
| Second, `Off` | `Bg2` / `Line` | `Text4` | `E769` |
| Second, `Cancel` | `Bg3` / `Line2` | `Text1` | `E711` |
| Second, `Live` | `Bg3` / `Line2` | `Text1` | `E769` |
| Second, `Held` | `Accent` | `OnAccent` | `E768` |

Main-window size is 92 by 26 with a 12 px SemiBold label; the panel-bar size
(`BarStart`, `BarSecond` in `PanelWindow.xaml`) is 20 tall with an 11 px
label and no glyph. Keep the template's reason for existing: a stock button
repaints its fill under the pointer.

2.4. Make the new styles implicit: in `Themes/Controls.xaml` add type-keyed
styles (`Button`, `ToggleButton`, `Slider`, `ScrollBar`, `ScrollViewer`,
`ToolTip`, `TextBox`, `Expander`, `ContextMenu`, `MenuItem`, `Separator`)
based on the keyed ones. `RadioButton` gets no implicit style; every radio
button in Zerg names `Segment`, `SmallSegment` or `SectionTab`. `TrayMenu`
keeps naming its style (`Style="{DynamicResource {x:Type ContextMenu}}"`),
since a style is still found by exact type.

2.5. **Leave Fluent.** In one step:

- `App.xaml`: `ThemeMode="None"`.
- `AppTheme.Apply`: stop assigning `Application.Current.ThemeMode`; keep
  everything else (the Windows light / dark watch, the dictionary swap,
  `Changed`, `RereadRows`).
- `PanelWindow.xaml`: remove `ThemeMode="Dark"` (`Themes/Panel.xaml` has
  been merged in `Window.Resources` since step 1.5); `Foreground` becomes
  `{DynamicResource Text1Brush}`; replace the literal `#33FFFFFF` and
  `#22FFFFFF` with `Line2Brush` and `LineBrush`.
- `Settings.ThemeMode` (a computed property returning WPF's `ThemeMode`) is
  now only a three-way choice. Replace it with an enum of Zerg's own in
  `AppTheme.cs` and remove `WPF0001` from `NoWarn` in `Zerg.csproj` if
  nothing else needs it.
- Remove the aliases added in step 1.4.

Startup checklist for this step, because a missing resource is a crash at
launch and not a compile error: main window opens; every section; Settings;
each panel; the tray menu and its submenu; a tooltip; the drill-down's
`Expander`; a task dialog (native, unaffected); theme switch both ways.

**Acceptance:** no control in any window shows a Fluent or classic Windows
look ("Nothing here should read as a default WPF control", `08-kit.svg`).
Searching `apps/zerg/src/Zerg` for `FillColor`, `StrokeColor`,
`AccentButtonStyle`, `DefaultToolTipStyle` and `ThemeMode="` finds only
`ThemeMode="None"`. Theme switching still re-colours everything live. Build,
tests, naming check, names list.

**Verify visually against:** `08-kit.svg` (buttons, session pair, toggles,
segments, fields, slider, scroll bars, tooltip, keycaps, menu).

### Phase 3: window shell

3.1. **Custom title bar.** In `MainWindow.xaml` add a
`WindowChrome.WindowChrome` (`CaptionHeight` 36, `UseAeroCaptionButtons`
false, a resize border, glass frame chosen so Windows 11 keeps the rounded
corners and shadow; keep `WindowStyle="SingleBorderWindow"`). Add a first
grid row of `TitleBarHeight` on `Bg0` with a `Line2` rule under it: the mark
(section 4.7), "Zerg" (12.5 SemiBold), a 1 by 16 `Line2` separator at x 82,
then the tabs, then at the right the gear and three caption buttons
(minimize, maximize or restore, close; 46 wide; hover `Bg3`, close hover
`CloseHoverBrush`). Mark every interactive element
`WindowChrome.IsHitTestVisibleInChrome="True"`. In `MainWindow.xaml.cs`:
handlers for the three buttons (use `SystemCommands`), a root margin that
compensates for the frame overhang when maximized, and automation names
"Minimize", "Maximize", "Restore", "Close". Minimizing must still hide to the
tray through the existing `OnStateChanged`. See R1.

3.2. **Tabs replace the Section row.** Move the three `RadioButton`s from the
"Section" `WrapPanel` in `MainWindow.xaml` into the title bar with
`Style="{StaticResource SectionTab}"`, keeping `GroupName`, the `Equals`
converter bindings, tooltips and the automation names "Damage section",
"Healing section", "Compare section". Delete the "Section" caption.

3.3. **View tab.** Add a fourth tab bound to the existing
`MainViewModel.Section` (parameter `View`), visible while
`HasParse || IsView` (decided, Q2); with nothing open it shows "View" alone,
with no file name and no close mark. Its content is
"View", then `ViewedName` in `MonoFont` 11 `Text3`, then an 8 px close mark
that runs the existing `CloseParseCommand` (give it its own automation name,
"Close the View tab"; the Close button in the View band keeps "Close the
viewed parse").
Selecting the tab sets `Section` to `View`, which is what `ImportCommand`
already does when a parse is open. (`07-states.svg`, "View, a parse open".)

3.4. **Gear.** Move the Settings `ToggleButton` (bound to `IsSettings`,
automation name "Settings") from the command bar to the title bar, as a
14 px glyph that is `Text1` while the page is open and `Text2` otherwise.
Update its tooltip to list what the page now holds.

3.5. **Command bar.** Rebuild the first content row as one 40 px bar on `Bg1`
with a `Line2` rule under it, still a `WrapPanel` so a narrow window wraps
whole groups: the session pair; a separator; Export (default button; hidden
outside `ShowsParse` as today) and Import (ghost); a separator; the three
toggles as `TickToggle`s (`Include Skillchains` only while `IsDamage`, as
today), with a `Keycap` on Click-through bound to `Panels.HotKey` (collapsed
when it is null); the `ParseNote` text right-aligned (11.5, `Text2`, or
`Crit` with the `E7BA` glyph when `ParseNoteBad`; a green `E73E` tick before
a success); and at the far right a placeholder `IconButton` for the layout
menu, disabled until phase 11. Delete the "Toggles" caption. Keep every
binding, tooltip, `ShowOnDisabled` and automation name.

3.6. **Status line.** Rebuild the last row as a 24 px bar on `Bg0` with a
`Line2` rule above: the dot (radius 4; colours from `StatusLight` exactly as
the `StatusDot` style maps them today, using `LiveBrush`, `AccentBrush`,
`CritBrush`, `Text3Brush`); `StatusFile` in `MonoFont` 11 `Text2`;
`StatusDetail` 11 `Text3`; and right-aligned, a new `StatusPanels` text
("1 panel out", "3 panels out, click-through") followed by a "Dock all" link
button bound to the existing `Panels.DockAllCommand`. Add to `PanelSet` in
`Panels.cs` an observable `OutCount` beside `AnyOut` (set in `Remember()`),
and build the text in `MainViewModel`. For the armed pulse see Q17.

**Acceptance:** the window has no system title bar; it drags by the bar,
maximizes and restores by double-click and by the button, snaps with
Win+arrows and by dragging to a screen edge, resizes from every edge, and
restores its saved placement on the next start, including maximized and on a
second monitor of another scale (edit `settings.json` as
`apps/zerg/CLAUDE.md` describes). Tabs switch sections; the gear opens and
closes Settings; minimizing still hides to the tray and the tray icon brings
it back. Build, tests, naming check, names list.

**Verify visually against:** the top 76 px and the bottom 24 px of
`01-damage-live.svg`; "Window chrome" on `08-kit.svg`; the status lines on
`02-healing-held.svg` and `07-states.svg`.

### Phase 4: bands

4.1. **State tag.** Add `apps/zerg/src/Zerg/Views/StateTag.xaml(.cs)` (new): a
pill 18 tall (radius 9) with a 3 px dot and the word, in the fills of section
4.2. ARMED draws a ring (radius 6.5, `Accent` at 35%) round its dot; HELD
draws two 2 by 6 bars instead of a dot. It binds to a new
`MainViewModel.Tag` property (an enum `Idle`, `Armed`, `Live`, `Held`,
`Saved`), set in `Tick` beside `Light`: `Saved` while a parse is on screen
in View, otherwise the value of `View.Light`.

4.2. **Figures band.** Replace the two `UniformGrid`s (`Tiles`, `HealTiles`)
in `MainWindow.xaml` with a 64 px band of five equal cells separated by
`Line2` rules inset 10 px. Each cell: a `Caps` label (baseline 17 px down),
the figure (`Figure`, 22), the note (`Label` 10.5). Bind the same properties
as today (`TotalText`, `TotalNote`, `ClockText`, `ClockNote`, `DpsText`,
`DpsNote`, `TopName`, `TopNote`, `TopSwatch`, `TopTip`, `BigText`, `BigNote`
and the healing twins). The clock keeps the `Clock` style's colour rule
(`Live`, `Accent` when held, `Text3` otherwise) and gains the state tag to
its right. The Top DPS swatch is a 10 px circle before the name. Keep
`FitTiles`' breakpoints as the rule for a narrow window (Q22).

4.3. **Marks in the band.** Add `apps/zerg/src/Zerg/Charts/Spark.cs` (new): a
plain `FrameworkElement` with `OnRender` (not a `Chart`; it needs no hover
card) that draws either thin bars or a line with a faint area under it from
an array of numbers, and a second small element or mode for a stacked
share bar. Feed three new view-model properties, built in `DrawTiles` (on a
count, never on the beat):

- beside the total, "per 20 s": the party's damage per bucket, from the same
  `Counting.Cumulative` call `DrawLine` already makes (difference of the
  summed series between grid times). Bars 1.2 px wide at a 2.2 px pitch,
  `Text3` at 75%, up to 26 px tall, in a 96 px wide area ending 14 px before
  the cell's right rule; caption `Caps`-sized "per 20 s" above it.
- beside Party DPS, "running": the running DPS over time (cumulative party
  total divided by elapsed at each grid time), as a 1.3 px `Text2` line with
  a 16% area.
- beside Top DPS, "party share": one 8 px tall bar of segments, each
  character's share in their colour, largest first, 1 px gaps, radius 1.

The Healing band gets the same three from healing data. Idle and armed bands
draw no marks. See Q10 for the bucket rule on long fights.

4.4. **Characters line.** Rebuild the chips block in `MainWindow.xaml` as one
30 px band: a `Caps` label bound to `CharactersLabel` (upper-cased:
"CHARACTERS 12/12"), the chips, a 1 by 14 `Line2` separator, All and None as
text buttons (`Text2`, 11.5), then `CharactersHint` (11, `Text3`). Re-template
the `Chip` style in `App.xaml`: a 20 tall rectangle (radius 3, `Bg2`, `Line2`
edge) with an 8 px swatch and the name only (11.5). Excluded: no face, a
dashed edge (3 2), a hollow swatch edged `Text4`, the name in `Text4` struck
through. Drop the job `TextBlock` from the chip's template in
`MainWindow.xaml` (the job is already in `ChipRow.Tip`). For overflow and the
fate of the Hide / Show button and the `charactersOpen` setting see Q18.

4.5. **View band.** Re-skin `Views/ViewCard.xaml`. With a parse open: a 38 px
`Bg2` band with a 2 px `Accent` left edge holding `ViewedName` (`SlotTitle`),
"Show" and the Damage | Healing segment (`ViewMode`), Started, Length, Party,
and at the right Replace… and Close as ghost buttons. With nothing open: the
title "View a parse", the explanatory note, and a dashed drop area with the
`FilledButton` "Open…"; while a file is held over it (`ViewOver`) the area
gets an `Accent` edge. `ViewError` shows in `Crit` with the warning glyph.
Keep all drag-and-drop handlers and automation names.

**Acceptance:** all figures, notes and chips show the same text as before
(compare `drive.cs text` output with step 0.3, allowing for the upper-cased
labels). The tag reads IDLE, ARMED, LIVE, HELD in step with the session pair
and SAVED in View. Marks appear only once a session has started and do not
redraw between counts (check with a paused session: no repaint).
Build, tests, naming check, names list.

**Verify visually against:** y 76 to 170 of `01-damage-live.svg` and
`02-healing-held.svg`; the idle, armed, held and View pieces of
`07-states.svg`; "Toggles, choices, chips" on `08-kit.svg`.

### Phase 5: panes in the default arrangement

Goal: the Damage and Healing sections are flush panes in two columns, each
scrolling by itself, in the arrangement the sheets draw. Nothing is draggable
yet. Compare and Settings keep scrolling as a page until phases 8 and 9.

5.1. **The pane control.** Add `apps/zerg/src/Zerg/Views/Pane.cs` (new), a
`HeaderedContentControl` subclass with dependency properties `Title`, `Note`,
`Tools` (extra heading content, such as "Group under 5%"), `PopOutCommand`,
`IsFolded`, and its template in `Themes/Controls.xaml`: a 30 px heading on
`Bg1` (grip dots, title in `PaneTitle`, note in `Label`, then right-aligned
`Tools`, the fold chevron, the Pop out icon button `E8A7`) with a `Line` rule
under it, then the content. Under `Float.On` the heading and any frame
collapse, exactly as the `Card`, `CardTitle` and `CardNote` triggers in
`App.xaml` do today, because the panel's bar already names the card. The Pop
out button keeps each card's automation name ("Pop out Cumulative damage",
...) and the tooltip from the `PopOut` style. The grip and chevron are drawn
but inert until phase 11. (DESIGN.md 3, "Pane heading"; `08-kit.svg`,
"Window chrome" and "Panes".)

5.2. **The split-tree model.** Add `apps/zerg/src/Zerg.Core/Layout/SplitTree.cs`
(new): an immutable tree whose nodes are `Split(Orientation, Ratio, First,
Second)` or `Leaf(PaneKey, Folded)`, plus one pure function
`Arrange(tree, width, height, minimums, fixedHeights)` that returns a
rectangle per pane key and a line segment per split, clamping every ratio so
no pane is smaller than 320 by 132 and giving a folded or stand-in leaf its
fixed height. Add `apps/zerg/tests/Zerg.Core.Tests/SplitTreeTests.cs` (new).
Only arranging is needed now; editing operations come in phase 11. The
default trees, measured from the sheets (body 1440 by 706):

| Section | Default tree |
|---|---|
| Damage | Columns at 0.653 (940 of 1440). Left column: `bars` over `actions` at 0.473. Right column: `line` over `drill` at 0.419 |
| Healing | Columns at 0.653. Left: `hbars` over `hactions` at 0.32. Right: `hline` over `hdrill` at 0.419 (the sheet shows the chart floating; this ratio is borrowed from Damage) |
| Compare (phase 8) | Columns at 0.653. Left: `cactors` alone. Right: `cpace` over (`ckinds` over `ctargets` at 0.614) at 0.313 |

`drill`, `hdrill` and the four Compare keys are new pane keys; they are not
panel keys in `PanelSet` and cannot float.

5.3. **The host.** Add `apps/zerg/src/Zerg/Views/SplitPanel.cs` (new): a
`Panel` with an attached property `Key` for its children and a `Tree`
property. It arranges its direct children in the rectangles
`SplitTree.Arrange` returns and draws the split lines as one-device-pixel
`Line2` rules. Children are never re-parented, so a chart is not rebuilt and
UI Automation peers stay valid when the arrangement changes (see R6 for why
this is recommended over the nested `Grid` and `GridSplitter` approach that
`DESIGN.md` section 6 sketches). Below `NarrowWidth` it stacks the children
in reading order at their natural heights and reports the total height, so
the existing page `ScrollViewer` (`Body`) scrolls them as today.

5.4. **Damage section body.** In `MainWindow.xaml` replace the Damage
`StackPanel`'s card list with a `v:SplitPanel` whose four children are
`Grid`s keyed `bars`, `actions`, `line`, `drill`; each holds the card and its
`v:Away` stand-in with the same `Panels[...]` bindings as today. `Body`
scrolls vertically only for Compare, Settings and the narrow fallback (bind
its `VerticalScrollBarVisibility`). Remove the `Drill` wrapper `StackPanel`.

5.5. **Cards become panes.** For each of `Views/LineCard.xaml`,
`BarsCard.xaml`, `ActionsCard.xaml`, `DrillCard.xaml`: replace the root
`<Border Style="{StaticResource Card}">` and the heading `DockPanel` with a
`v:Pane`, moving the title, note and Pop out command onto it. Move "Group
under 5%" into `LineCard`'s `Tools`. Give each table a vertical
`ScrollViewer` around its rows so the column headings stay put while rows
scroll (today the page scrolled); keep `v:WheelChain.On` and the 14 px right
margin. Remove fixed heights that assumed a page (`Height="320"` on the
docked `LineChart`, `MaxHeight="340"` on the Actions list) for the docked
form; keep them for the narrow fallback through a trigger on a new inherited
attached property the host sets (for example `SplitPanel.Stacked`).

5.6. **The drill-down pane.** `DrillCard`'s root binding
`Visibility="{Binding DrillOpen, ...}"` goes: the pane is always in the
tree. Its leaf is folded to the heading while `DrillOpen` is false, and
while `Panels[actions].IsOut` is true (the panel shows the drill-down under
the picked row, as today); in the second case the heading says the Actions
panel has it. In `MainWindow.xaml.cs` the `DrillOpened` and
`HealDrillOpened` handlers keep their `BringIntoView` only for the stacked
fallback. The floating form in `ActionsCard.xaml` (`Detail` template under
the row) is untouched.

5.7. **Stand-in.** Re-skin `Views/Away.xaml` as a 46 px strip with a dashed
`Line3` outline: the `E8A7` glyph, the title in `Text2` SemiBold, "Showing in
its own panel.", and "Bring back" as a ghost button (automation name
unchanged). While a card is out its leaf takes 46 px and its column
neighbour takes the rest (`02-healing-held.svg`).

5.8. **Healing section.** Repeat 5.4 to 5.7 for `HealLineCard`,
`HealBarsCard`, `HealActionsCard`, `HealDrillCard` with keys `hline`, `hbars`,
`hactions`, `hdrill`.

**Acceptance:** at 1440 by 900 the Damage section shows all four panes with
no page scroll bar; each table scrolls inside its pane; popping a card out
leaves a 46 px stand-in and "Bring back" restores it; picking an action
unfolds the drill-down without moving anything else; below 700 px wide the
panes stack and the page scrolls. Panels still work (they use the same card
controls). New tests pass. Build, tests, naming check, names list.

**Verify visually against:** the pane boundaries of `01-damage-live.svg`
(rules at x 940.5, y 504.5 and y 466.5) and `02-healing-held.svg`; "A pane"
on `08-kit.svg`. Table contents are still the old density; that is phase 6.

### Phase 6: the live tables

6.1. **Settings plumbing (no controls yet).** In `apps/zerg/src/Zerg/Settings.cs`
add `ShadeCharacters` (bool, default true), `ShadeActions` (bool, default
false) and `LowAccuracy` (int, default 90; the name `lowAccuracy` is
`DESIGN.md`'s). In `MainViewModel.Settings.cs` add matching observable
properties that save and call `Recount()` on change, clamping `LowAccuracy`
to 50 to 100. Test by editing `settings.json` between runs.

6.2. **Shade strength.** Add `apps/zerg/src/Zerg.Core/RowShade.cs` (new) with
the pure functions of section 4.4 (`Strength(colour, surface, text, target,
cap)` and `RunBand(colour, surface, crit)`), and
`tests/Zerg.Core.Tests/RowShadeTests.cs` (new) asserting the dark and light
tables in section 4.4 for all 18 job colours. In `MainViewModel.cs` add
`ShadeOf(name)` and `PanelShadeOf(name)` beside `SwatchOf`, returning frozen,
cached `SolidColorBrush`es whose alpha is the strength (cache by colour and
theme, like `Solids`).

6.3. **Row data.** In `Rows.cs` add to `ActorRow` and `HealerRow`: `Shade`
(brush), `Fraction` (total over the largest total, as `StripRow.Fraction`
is computed in `DrawBars` today), `IsOwner`, `Tip`, and the three rates as
numbers (`AccuracyRate`, `WsAccuracyRate`, `PetAccuracyRate`, nullable) for
the mark. Add to `ActionRow` and `HealRow`: on a heading `Shade`, `Fraction`
and the character's `Share` text; on an action `ShareFraction` (its total
over the largest of its siblings) and, for `ActionRow` only, `SpreadMin`,
`SpreadAvg`, `SpreadMax` as fractions of the character's biggest hit. Fill
them in `DrawBars`, `DrawActions`, `DrawHealBars`, `DrawHeals`.

6.4. **Damage by character, docked form** (`Views/BarsCard.xaml`). Delete the
`c:BarChart`. Replace the `Columns` string with the widths of section 4.8.
Headings: a 38 px block with a first row of `GroupCaps` labels (WEAPONSKILL,
SKILLCHAIN, PET) each flanked by `Line2` hairlines, 1 px `Line` rules between
the groups, and a second row of one-word headings ("Damage", "Avg", "%",
"Acc" under each group). Keep every heading's existing tooltip. Row
template, 24 px: a `Grid` holding, back to front, the shade `Rectangle`
(`Fill="{Binding Shade}"`, `v:Grow.Share="{Binding Fraction}"`,
`RenderTransformOrigin="0,0.5"`, no rounding, no opacity), a hover overlay,
the owner marker, and the `v:Cells`. Name SemiBold on the owner's row; Damage
SemiBold; WS Avg, WS % and SC % in `Text2`; a dash in `ShadedDash`. The
shade's end is exact and unmarked. (DESIGN.md 2 "The row shade", 4, 6a.)

6.5. **Unshaded form.** When `ShadeCharacters` is false the shade rectangle
is collapsed and a 48 by 6 bar in the character's colour (track `Bg3`)
appears in the Damage % cell before the figure, to the same scale
(`Fraction`). Dashes return to `Text4` and the job to `Text3`. Use a
`DataTrigger` on the view-model flag reached through the `ItemsControl`'s
`DataContext`. (`07-states.svg`, "Row shading, the four settings".)

6.6. **Hover.** A hovered row gets `RowHoverBrush` over its whole width and
`Line3` rules above and below (`08-kit.svg`, "Table rows").

6.7. **Low accuracy mark.** Add `apps/zerg/src/Zerg.Core/LowMark.cs` (new): a
pure function from a rate and the threshold to four opacities (rule, surface,
wash, outline), with tests. The design gives the bands in words only
(DESIGN.md 2, "The low accuracy mark"); the function below is fitted to the
nine marks sampled from `01-damage-live.svg` and `08-kit.svg` and reproduces
each within 0.03, with one exception: the sheet draws no cut-out at 84.4%,
where the fit gives a faint one, so draw the cut-out only once `k` reaches
0.1 (see Q14):

- `T` is the threshold as a fraction (0.90 as installed);
  `S = min(0.60, T - 0.15)` is where the mark is strongest. No mark at or
  above `T`.
- `t = clamp((T - rate) / (T - S), 0, 1)`; `k = clamp((t - 1/6) / (1/3), 0, 1)`
  (with the installed threshold `k` runs from 0 at 85% to 1 at 75%).
- Rule opacity: `0.55 + 0.30 * t` while `k = 0`; otherwise
  `max(0, 0.60 - 0.55 * k)`; none once `k = 1`.
- Surface (the pane's own `Bg1`, which cuts the cell out of the row): `k`.
- Wash (`Crit`): `0.195 * k` up to `k = 1`, then rising linearly to 0.34 at
  `t = 1`.
- Outline (`Crit`, 1 px): 2.12 times the wash, at most 0.72.

Sampled values for the test: 89.6% rule 0.56; 88.1% rule 0.58; 84.4% rule
0.57; 80.7% surface 0.46, wash 0.08, outline 0.17, rule 0.37; 80.4% 0.49 /
0.09 / 0.19 / 0.35; 79.1% 0.61 / 0.11 / 0.24 / 0.28; 78.4% 0.67 / 0.13 /
0.28 / 0.24; 57.1% 1.00 / 0.34 / 0.72 / none.

Add `apps/zerg/src/Zerg/Views/LowMark.cs` (new), an `OnRender` element placed
behind the Accuracy, WS Acc and Pet Acc figures in the row template, with
`Rate` and `Threshold` properties and the geometry of section 4.6. It
redraws only when a count changes its rate. Not on the Party line (Q5), not
in Compare, not on a dash.

6.8. **Sort mark: not drawn.** The sheets draw a small filled triangle beside
"Damage" (and "Total" in Actions, "Healing" in the healing table). The owner
decided against it (Q3): nothing can be sorted, so no mark is drawn and the
headings stay non-interactive. The sheets will differ from the app here.

6.9. **Party line (the one counting change).** The live count has no
party-wide Accuracy, weaponskill, skillchain or pet figures:
`Counting.Aggregate` in `apps/zerg/src/Zerg.Core/Counting.cs` fills them per
character only (`ActorTotals.FinishSplit` in `Totals.cs`).
`Compare.Measure` in `Compare.cs` already sums the per-character `Split`
counts into party figures for Accuracy and weaponskills; do the same for the
live count: add a party `Split` (and the derived nullable figures, pet
included) to `Aggregate`, filled where the actors are finished. Add tests
beside the existing ones in `tests/Zerg.Core.Tests/TrackerTests.cs`
(a party of two with known swings; a party with no weaponskills prints
dashes), and one sentence to `apps/zerg/RULES.md` under "The per-character
table". Then in `MainViewModel.Damage.cs` build a `Party` row object (count
of characters under Job, total, "100.0%", party DPS rewritten by `Tick` with
the others, and the sums), and in `BarsCard.xaml` draw it as a fixed 26 px
row on `Bg2` under the scrolling rows, with `Line2` above and the group
rules continued through it, unshaded. Healing's Party line needs no core
change (`HealTotals` already has `Total`, `Casts`, `Avg`, `PetTotal`).

6.10. **Actions** (`Views/ActionsCard.xaml`). Columns per section 4.8. The
`Heading` template becomes a 22 px row on `Bg2` with the shade behind it:
caret (drawn chevron, right when closed, down when open, replacing the
`Caret` text glyph), swatch, name SemiBold, job (`Text2`), and the total and
share in the Total and Share columns (today the total follows the name after
a dash). The `Action` template is a 22 px row: name indented to 42, figures,
Miss as a `Text4` dash when zero (as today), the "Min, avg, max" mark, a 42
by 6 `Text3` bar on a `Bg3` track then the Share figure. With `ShadeActions`
on, the row is shaded with `ActionShadeBrush` to `ShareFraction` and the
small bar is collapsed. Selected: `Bg3` under the shade, a 2 px `Accent`
edge at the left (replacing the 3 px rounded bar), name SemiBold. Keep the
commands, `CommandParameter`s, automation names (`Label`) and the `Under`
content control for the floating form.

6.11. **The "Min, avg, max" mark.** Add `apps/zerg/src/Zerg/Views/Spread.cs`
(new): an `OnRender` element 132 wide drawing a `Line2` baseline, a 4 px
`Text3` bar (radius 2) from the least to the greatest hit, and a 2 by 10
`Text1` tick at the average, all scaled to the character's biggest hit. Its
heading is "Min · avg · max". Not in the Heals table (DESIGN.md 4, 02).

6.12. **Shedding columns in a narrow pane.** When the Actions pane is under
about 560 wide, collapse Miss, Min and the mark (collapsing a cell in the
heading and in every row drops the column; `Views/Cells.cs` already supports
this). Drive it from the card's `SizeChanged` through an inherited attached
property. The floating Actions panel sheds the same columns in place of
today's sideways scrolling under 620 px (Q25). The Heals table sheds Min
under about 480 wide by the same means; the per-character tables keep
scrolling sideways (Q16). (Sheet 10 B.)

6.13. **Drill-down** (`Views/DrillCard.xaml`). Heading: `DrillTitle` (14
SemiBold), under it an 8 px swatch and `DrillNote`; at the right the fold
chevron and "Close" as a ghost button (`CloseDrillCommand`, name "Close
drill-down"); a 2 px `Accent` edge down the heading's left. The six figures
(`DrillTiles`) become one 42 px `Bg2` row of equal cells ruled with `Line2`:
`GroupCaps` label over a `DrillFigure`. Then the histogram, the caption
(`Label`, centred), and "Every hit": a 24 px heading row with a caret, the
title (11.5 SemiBold), the count (`Label`) and the two column headings; it is
open by default (`IsExpanded="True"`, not saved). Hit rows are 20 px: time in
`MonoFont` 11 `Text3`, target `Text2`, damage SemiBold, then the deviation
mark and the signed difference in `Text2`. Add
`apps/zerg/src/Zerg/Views/Deviation.cs` (new): a 12 px `Line3` tick with a
5 px tall bar to its right in `Live` (over the average) or to its left in
`Crit` (under), at 85% opacity, its length the hit's distance from the
average over the largest distance in the list. Extend the `HitRow` record in
`Rows.cs` with `Offset` (minus one to one) and `Crit`, filled in `DrawDrill`;
draw the crit spark as a separate 9 px `Accent` "✦" after the damage instead
of appending " ✦" to the text. Keep the list virtualized.

6.14. **Healing twins.** Apply 6.4 to 6.6 and 6.10 to
`Views/HealBarsCard.xaml` and `HealActionsCard.xaml` (no low accuracy mark:
healing has no rates; no "Min, avg, max" mark; the heading keeps its
"+ N pet" note after the job), and 6.13 to `HealDrillCard.xaml` ("Every
cast", "Healed").

6.15. **Floating forms still work.** The strip in `BarsCard.xaml` and
`HealBarsCard.xaml` is restyled in phase 10; in this phase only check that
each of the six panels still opens and reads correctly.

**Acceptance:** with the synthetic fixture replayed, every figure in the two
per-character tables, Actions, Heals and both drill-downs equals the value
the unmodified app printed (compare `drive.cs text`; the Party line and the
separated crit spark are the only additions). An alliance of 18 fits the
per-character pane without sideways scrolling at 940 px. Shades animate on a
count and do not move between counts. Toggling the two settings in
`settings.json` gives the four combinations of `07-states.svg`. New tests
pass; the test count rises. Build, tests, naming check, names list.

**Verify visually against:** `01-damage-live.svg` (tables, drill-down),
`02-healing-held.svg`, "Table rows" and "Marks inside a cell" on
`08-kit.svg`, the last row of `07-states.svg`.

### Phase 7: charts

7.1. **Shared chart look** (`Charts/Chart.cs`). Axis labels 10 px `Text3`;
grid `Line`; baseline `Line2`. Hover card: `Bg4` fill, `Line3` edge, radius
5, a soft shadow (a `DropShadowEffect` on the `card` `DrawingVisual` only),
10.5 px text, 15 px rows, 7 px square swatches (radius 1.5) in place of
circles, and a `Line3` rule under the heading. Keep the column-splitting for
an alliance and the `Compact` variant. The empty-state text becomes 12 px
`Text3`. Make the typeface follow `TextFont` rather than the literal family
string in `Chart.cs`.

7.2. **Cumulative chart, docked** (`Views/LineCard.xaml`,
`HealLineCard.xaml`, `Charts/LineChart.cs`,
`Zerg.Core/Charts/LineLayout.cs`). Set `EndLabels` in the docked style as
well and delete the legend `ItemsControl`. Lines 1.5 px (the leader's 2),
end markers radius 3 with a 1.5 px ring in the surface colour
(`LineLayout.MarkerRadius` is 4.5 today), end labels 10.5 px with the leader
in `Text1` SemiBold and the rest `Text2`, thin `Line3` leaders from a marker
to a nudged label, crosshair a dashed `Text3` line. While the model is live,
draw the right edge as a 1 px `Live` rule at 55% (present on the sheets, not
mentioned in `DESIGN.md`; Q23). Changes to label geometry belong in
`LineLayout` with its tests in `ChartLayoutTests.cs`.

7.3. **What the legend used to say.** The legend's tooltip was the only place
that named who is inside the grouped "others" line (`LegendRow.Tip`). Append
the member names to `GroupTip` in `MainViewModel.cs` when the group exists,
then remove `Legend`, `HealLegend` and `LegendRow` (Q20).

7.4. **Histogram** (`Charts/HistogramChart.cs`, `Charts/Models.cs`,
`Zerg.Core/Charts/HistogramLayout.cs`). Add `Q1`, `Q3` and `Median` to
`HistogramModel` (they are already on `Distribution` in `Totals.cs`) and to
`HistogramLayout.Compute`, which returns the band's x range and the median's
x; test in `ChartLayoutTests.cs`. Paint the band (`HistogramBandBrush`, full
plot height), the columns (2 px rounded tops, 1 px gaps, as now), the average
rule in `Text2` with its "avg N" label (10 px SemiBold), and the median as a
1.5 px `Text1` tick straddling the baseline (5 px above, 4 below).

7.5. **Paired histogram** (`Charts/PairedHistogramChart.cs`): same axis and
label styling; fills already follow `RunABrush` and `RunBBrush`.

**Acceptance:** charts redraw only on a count or at the draw frequency, never
per frame (the existing rule). Hover reads the
same values as before. Layout tests pass. Build, tests, naming check.

**Verify visually against:** the chart and histogram in
`01-damage-live.svg`; "Hover card and menu" on `08-kit.svg`; panel 2 on
`04-pop-outs.svg`.

### Phase 8: Compare

8.1. **Run colours.** In `Settings.cs` add `RunA` and `RunB` (nullable hex
strings; null means "as installed"). Remove `RunABrush` and `RunBBrush` from
the two theme files (they alias `Series1` and `Series2`, which must stay as
the first two fallback slots) and have `AppTheme` put `RunABrush`,
`RunBBrush`, `RunABandBrush`, `RunBBandBrush` and `RunInkBrush` into the
application resources at start, on a theme change and when a setting
changes: the colour is the setting or the installed value of section 4.2,
the band's alpha comes from `RowShade.RunBand`, the ink is `#101010` or
`Text1`, whichever contrasts more. In `CompareViewModel.Show` replace
`AppTheme.Series(0, ...)` and `Series(1, ...)` with these colours. The
colours belong to the slot and do not move on Swap (assumed by the design;
Q11).

8.2. **Fixed bands** (`Views/CompareSection.xaml`). (a) A 34 px line: title
"Compare runs" and its note at the left; at the right "Show" with the
Damage | Healing segment, "Compare by" with Character | Job, Include
Skillchains as a `TickToggle` (damage mode only), "Swap A ↔ B" as a ghost
button. (b) The two slots side by side, each a 54 px `Bg2` card (radius 5,
`Line2` edge, a 3 px edge in the run's colour): a 16 px badge, the role in
`Caps`, the name in `SlotTitle`, and Replace…, Use current, Clear as 22 px
ghost buttons at the right; a second line with Started, Length, Party. An
empty slot is dashed with "Drop an exported parse here, or open one" and
Open…; an error shows in `Crit` on its slot. (c) A 72 px band of six cells
(`Tiles`): `Caps` label; A and B each with a 13 px badge and a `TileFigure`
and an optional 10 px `Text3` note (`SubA`, `SubB`); at the cell's right the
change in its tone (13 SemiBold) with the plain difference under it. Keep the
slot drag-and-drop handlers and all automation names.

8.3. **Panes.** Put the four tables and the chart into a `v:SplitPanel` with
the Compare tree of step 5.2: `cactors` (By character or By job; in healing
mode By healer), `cpace` (the cumulative chart and its "Data table"
expander), `ckinds` (By damage type; in healing mode By heal), `ctargets`
(By target). One arrangement serves both modes. `Body` stops scrolling for
Compare.

8.4. **By character.** Rows 36 px. Behind each row two bands: run A across
the top half and run B across the bottom half, each a `Rectangle` filled
with the run's band brush and scaled by `v:Grow.Share` to
`Line.Damage.A` and `Line.Damage.B` (the bar lengths `BarPair` already
carries; no change to `Zerg.Core/CompareSheet.cs`). The Damage cell becomes
the two figures (`Line.Damage.TextA` over `TextB`, 11.5 SemiBold). Remove the
tick from `Views/RunPair.xaml` and mark the run once per row in the Job
column (a 6 by 2 mark in each run's colour before each job line). With
`ShadeCharacters` off, collapse the bands and show `Views/RunBars` in the
Damage cell as today (keep the control). `ChangeText` keeps sign, tone
(`Live`, `Crit`, `Text2` for none) and the plain difference under it (10 px
`Text2`). An opened row sits on `Bg2`.

8.5. **The action well.** `ActorDetail` and `HealerDetail`: a `Bg0` well,
indented, with a 24 px heading row and 32 px action rows; the picked action
has a 2 px `Accent` edge. `SpreadDetail`: the paired histogram at the left
and at the right a figure table with 23 px rows whose change is split into
two columns, "Δ %" (toned) and "Δ" (quiet). Give `Views/ChangeText` a second
layout for this, or bind `Change.Main` and `Change.Sub` to two cells.

8.6. **Side tables.** By damage type and By target (and their healing
twins): 28 px rows with the two bands at 14 and 13 px, name, A over B
figures, change, and (By damage type) A over B share (Q9).

8.7. **Chart.** `PaceLegend` becomes a two-line legend inside the plot's top
left (swatch, "A · name"), since the chart's right edge belongs to the end
of each run.

**Acceptance:** with two exports of the fixture loaded (make a second with
`gen-test-events.py --export ... --seed 7`), every Compare figure equals the
unmodified app's. Swap, Clear, Use current, By job, healing mode, a row
opened onto an action and an action onto its distribution all still work and
survive a redraw (`CompareViewModel` keeps what is open). The existing
`CompareSheetTests` pass unchanged. Build, tests, naming check, names list.

**Verify visually against:** `03-compare.svg`; "Compare, a slot empty" on
`07-states.svg`; the Compare row on `08-kit.svg`.

### Phase 9: Settings page

9.1. **Page frame** (`Views/SettingsPage.xaml`). A header (title in
`PageTitle`, the note "Set once and left alone. The session goes on being
measured underneath.", and "Done" as a default button at the right, name
"Close settings") with a `Line2` rule under it; then eight rows separated by
`Line` rules inset 24 px. Each row is a three-column grid: name
(`PaneTitle`) and explanation (`Label`, wrapping) in a column about 430
wide; the control starting at x 470; an optional preview at about x 830.
Raise the page's `MaxWidth` from 860 to 1100. The existing cards' texts are
kept word for word. Row order: Events folder, Click-through hot key, Default
pop-out opacity, Draw frequency, Row shading, Low accuracy mark, Theme,
Compare colours. (`06-settings.svg`.)

9.2. **Events folder.** The read-only path in a `ZTextBox` (410 wide, folder
glyph `E8B7` at its left, `MonoFont`), "Browse…", "Use default", then
`EventsFolderNote` and `EventsFolderSource`. Bindings and names unchanged.

9.3. **Hot key.** The `Keys` button keeps its click, key and focus handlers
in `SettingsPage.xaml.cs` and its automation name. At rest its content is
the chord as separate keycaps (split `HotKeyText` on "+" with a small
converter, `apps/zerg/src/Zerg/Views/ChordKeysConverter.cs`, new). While
`Recording` it shows "Press the new keys…" in `Accent` with an `Accent`
edge, and `HotKeyNote` under it.

9.4. **Default pop-out opacity.** `ZSlider` (240 wide) and the percentage,
plus a preview: a 196 by 44 swatch of a made-up bright backdrop (a gradient)
under a black layer at `Panels.DefaultOpacity`, with two strip rows on it.

9.5. **Draw frequency.** `ZSlider` and "30 a second" (12 SemiBold).

9.6. **Row shading (new).** Two `TickToggle`s bound to `ShadeCharacters` and
`ShadeActions`, each with its quiet note ("on unless switched off", "off
unless switched on") and the explanatory sentences from `DESIGN.md` section
4, "06 Settings"; and a four-row preview (two characters, two actions) drawn
with the same row templates' shade rules as currently set.

9.7. **Low accuracy mark (new).** A `ZSlider` from 50 to 100 bound to
`LowAccuracy`, "90% unless changed", and six sample rates drawn with
`Views/LowMark` at the current threshold. Save through a debounce timer as
`DrawFrequency` does in `MainViewModel.Settings.cs`.

9.8. **Theme.** The existing three radio buttons as one segment group with
their glyphs.

9.9. **Compare colours (new).** Two wells (a 118 by 28 button holding the
run's badge, the colour's code in `MonoFont`, a chevron), "Use default"
(disabled while both are as installed), and a one-row preview of a Compare
row with "the bands come out at N% and M%". Opening a well shows a `Popup`
under it: add `apps/zerg/src/Zerg/Views/ColourPicker.xaml(.cs)` (new) with a
150 by 100 saturation and brightness square, a 12 by 100 hue strip, a 24 px
swatch, a code field, and "band N%"; put the colour conversions and code
parsing in `apps/zerg/src/Zerg.Core/Hsv.cs` (new) with tests. Changes apply
live through step 8.1 and are saved on a debounce. The popup closes on a
click outside or Esc. WPF has no stock colour picker, so this control is the
largest new piece of the page.

**Acceptance:** every existing setting behaves as before (folder change asks
first during a session; a rejected chord is never saved; opacity and draw
frequency are debounced; theme switches live). The three new rows change the
tables, the mark and Compare immediately and persist across a restart;
`settings.json` gains `shadeCharacters`, `shadeActions`, `lowAccuracy`,
`runA`, `runB` and loses nothing (unknown keys are still preserved by
`Settings.Unknown`). Build, tests, naming check, names list.

**Verify visually against:** `06-settings.svg`.

### Phase 10: pop-outs and the tray menu

10.1. **Panel bar** (`PanelWindow.xaml`, `PanelWindow.xaml.cs`). Height 30
(update the `Chrome` constant, which `StripHeight` and the opening sizes
depend on). Order from the left: the pair (`BarStart`, `BarSecond`; 20 tall,
4 px apart, 8 px from the edge), the clock (12.5 SemiBold `NumberFont`,
coloured by `Light`), the total, then the title right-aligned (11 SemiBold
`Text3`). `Tools` (a 56 px slider, the percentage, a Dock icon button with
`E73F`) sits at the right at opacity 0 and not hit-testable; `Frame`'s
`MouseEnter` and `MouseLeave` handlers switch it on and off in place of
today's fade to 45% (Q7 for where the title goes). Keep: the pair is never
faded; nothing to the left of the pair can grow; `Through()` hides the pair
and tools and shows the lock under click-through; the bar's drag
(`Overlay.Drag`), the edge grips, the saved frame, the tint. Frame edge
`Line2Brush`, bar rule `LineBrush` (panel values), radius 8.

10.2. **Strip** (floating form in `Views/BarsCard.xaml` and
`HealBarsCard.xaml`). Row 20 px plus a 1 px gap. The bar behind the row
takes its brush from `StripRow.Fill` computed with the strip strength
(section 4.4) and loses `Opacity="0.7"`; add a 3 px solid edge in the
character's colour at the row's left (radius 1.5) and the `Accent` owner
triangle in place of the 2 px rectangle. Headings in `GroupCaps`. With
`ShadeCharacters` off: no bar, the small bar in the % cell, the 3 px edge
stays (decision 15). Remove the per-row `DropShadowEffect`.

10.3. **Halo.** Put one `DropShadowEffect` (depth 0, black, blur about 3) on
`PanelWindow`'s `Body` so every glyph in a panel has a dark halo. Measure its
cost at the draw frequency with the cumulative panel up before keeping it
(R7).

10.4. **Actions and Heals panels.** Heading rows shaded with
`PanelShadeOf`; selected action `Bg3Brush` (white at 13%) with the `Accent`
edge; the drill-down under the row as now, with "Every hit" folded when
`Float.On` (a style trigger on `IsExpanded`).

10.5. **Tray menu** (`TrayMenu.xaml`). Add a first, non-interactive item (not
focusable, not hit-testable): the mark, "Zerg" (12.5 SemiBold), a line
"`ClockText` · `TotalText` damage" (11 `Text3`), and the `StateTag` at the
right; then a separator and the existing lines in the existing order. The
heading and its separator are shown only while the session is armed or
running (the owner's decision, Q21); in every other state, and while View
shows a saved parse, they are collapsed and the menu starts at its first
existing line. Session
lines get their glyph in the tick column by look (as step 2.3); Click-through
shows an `Accent` tick when on and the hot key as a keycap
(`Panels.HotKey`); the Panels submenu keeps its ticks. Keep
`StaysOpenOnClick="{Binding View.StartAsks}"`, the `AutomationId`s, and
`TrayIcon.Front` in `MainWindow.ShowMenu`. Nothing counts the five seconds
down.

**Acceptance:** run the panel checks in `apps/zerg/CLAUDE.md` ("Whether a
panel takes the keyboard", click-through, the tray icon): a real press on a
panel never changes the foreground window; a panel drags and resizes without
activating; click-through passes clicks; places and opacities are restored;
a restart asked about shows Confirm and Cancel on every bar and in the open
tray menu. A panel 250 px wide still shows the pair, clock and total. Build,
tests, naming check, names list.

**Verify visually against:** `04-pop-outs.svg` (all five panels),
`05-tray-menu.svg`.

### Phase 11: the adjustable layout

11.1. **Tree operations** (`Zerg.Core/Layout/SplitTree.cs`, tests in
`SplitTreeTests.cs`). Pure functions, each returning a new tree: set a
split's ratio (clamped to the pane minimums for a given size); toggle a
leaf's fold; move a pane beside another (`Left`, `Right`, `Above`, `Below`:
remove it, heal the hole by promoting its sibling, then split the target's
room in two); swap two panes; move a pane to an edge of the section (it
takes that whole side); reset to the default. Plus a tolerant round trip to
JSON as shares of the room: a saved tree that names an unknown pane, misses
one, or repeats one is repaired (unknown dropped, missing appended) and never
throws.

11.2. **Persistence** (`Settings.cs`). Add `Layouts` (section name to tree)
and `LayoutLocked` (Q6). `MainViewModel` exposes the tree for the section on
screen and saves on change through a debounce. View uses Damage's or
Healing's tree according to `ViewMode`.

11.3. **Dividers** (`Views/SplitPanel.cs`, a `Divider` style in
`Themes/Controls.xaml`). For each split the panel owns a `Thumb` laid over
the rule: 7 px hit area, a 3 by 22 `Line3` grip at its middle, resize cursor;
under the pointer a 3 px `DividerHoverBrush` line and a 5 by 26 `Accent`
grip; while dragged solid `Accent` and a readout of both sizes ("940 | 499",
`MonoFont` 11.5 SemiBold on `Bg4` with a `Line3` edge) following the
pointer. Sizes change live; the ratio is saved on `DragCompleted`. A
double-click resets that split to its default ratio. The thumb is focusable
and takes the arrow keys (8 px a press). The status line shows
"Double-click a divider to set it back" while one is held.

11.4. **Fold.** The chevron in a pane heading toggles the leaf's fold: the
pane becomes its 30 px heading (title in `Text2`, a short note such as
"10 characters") and its column neighbours take the room. A pane alone in
its column folds sideways to a 30 px rail with the title reading down
(described in `DESIGN.md` 4, sheet 10, not drawn). There is no hidden state.

11.5. **Moving a pane.** Add `apps/zerg/src/Zerg/Views/PaneDrag.cs` (new). A
press on a heading (not on its buttons) that travels more than 4 px captures
the mouse and starts a move; the cursor is the move cursor. An overlay on the
`SplitPanel` (an adorner) draws: the origin pane dimmed (`Bg0` at about 78%)
with a dashed `Line3` outline and "(title) is being moved / Esc puts it
back"; the pane under the pointer dimmed with five zones (above, below, left
of, right of as dashed `Line3` outlines with small labels; the middle as
"Swap with (title) / the two panes change places"); the zone under the
pointer filled `DropZoneFillBrush` with a 1.5 px `Accent` edge and its words
in `Accent`; a 5 px `EdgeZoneBrush` band along each edge of the section; and
the carried card (250 by 92, `Bg2`, `Accent` edge, shadow: the heading and a
`VisualBrush` sketch of the pane). The status line says "Moving (title) ·
Esc cancels". Releasing over a zone applies the matching tree operation;
Esc, a lost capture, or a release anywhere else puts it back. A drag never
leaves the window and never floats a pane: floating stays Pop out's job. A
stand-in is a pane like any other and can be moved.

11.6. **Layout button and heading menu.** Enable the command bar's layout
button: a menu with "Lock layout" (ticked when on), "Reset (section)
layout", "Reset all layouts". Locked: grips are not drawn, dividers do not
drag, headings do not start a move. Give each pane heading a context menu:
Fold, Pop out (where the card can float), Move with Left, Right, Up, Down
(swap with the neighbour on that side), so no pointer is needed.

11.7. **Per section and fallback.** Damage, Healing and Compare each keep a
tree. Below 700 px the stacked mode of step 5.3 applies and the saved tree
is untouched; it returns with the width.

**Acceptance:** the tree tests cover every operation, the minimum-size clamp
and the repair of bad saved trees. In the app: each divider drags, clamps at
320 by 132, resets on double-click, moves with the arrow keys; each pane
folds and unfolds; a pane can be dropped on all five zones of another pane
and on all four edges, and Esc cancels; arrangements persist per section
across a restart; Reset restores the default; Lock stops every drag. Charts
are not rebuilt by a move (the cumulative chart does not flash empty), list
scroll positions survive, and `drive.cs text` still reads every table after
a move. Build, tests, naming check.

**Verify visually against:** `10-layout.svg` (A, B and the four rule
boxes); "Panes: move, resize, fold" on `08-kit.svg`.

### Phase 12: light theme, states, accessibility, performance, clean-up

12.1. **Light theme.** Switch to Light and compare with
`09-damage-live-light.svg`: surfaces, rules, ink, the light shade strengths
(22%, WAR 21%), the cut-out on a white pane, tag tints, `OnAccent` white.
Panels stay dark. Light Compare is not drawn: check that changes stay
readable on the bands and tune per Q11.

12.2. **States sweep.** Reproduce each piece of `07-states.svg` in the app
and compare: idle with no addon; armed; restart asked; a refused export (red
note with the warning glyph, clears after 8 s, wraps to its own line in a
narrow window) and an unreadable file (red status line); View with a parse;
View with nothing open, a file held over it, and an open error; Compare with
an empty slot and a slot error; Hide names on with Include Skillchains off
(the Job column drops out, SC cells are dashes); click-through on with
panels out; the four shading combinations.

12.3. **Accessibility.** Automation names for everything new: the tabs,
caption buttons, layout button, each pane (its title), each fold button
("Fold Actions"), each divider (the two panes it separates), the colour
wells and picker parts. `Views/Caps` and the marks expose their text or a
one-line summary through an automation peer. Tab order runs title bar,
command bar, bands, panes in reading order, status line. Every template
shows the focus ring. Column-heading tooltips survive the move to grouped
headings. Respect `SystemParameters.ClientAreaAnimation` wherever
`Views/Grow.cs` is reused (it already does).

12.4. **Performance.** The CPU comparison against step 0.4 was **dropped by
the owner on 2026-10-05** (section 10): there is no baseline and no
measurement to repeat. What remains is a review by reading: the things most
likely to cost are the panel halo (R7), text laid out in collapsed panes
(R8), and any element that redraws on the beat that should redraw on a
count.

12.5. **Clean-up.** Delete what nothing references any more:
`Charts/BarChart.cs`, `BarRow` in `Charts/Models.cs`,
`Zerg.Core/Charts/BarsLayout.cs` and its tests in `ChartLayoutTests.cs`,
`Bars`, `BarsHeight`, `HealBars`, `HealBarsHeight`, the legend rows, the
styles `Card`, `CardTitle`, `CardNote`, `PopOut`, `Tile`, `TileNote`,
`ArmedRing` and any others left unused, and `FitTiles` if Q22 retires it.
Search for leftover literal colours in XAML and code
(`#` followed by six or eight hex digits outside `Themes/`).

12.6. **Documents.** Update `apps/zerg/CLAUDE.md`: the "Layout" tree (new
files), "How it works" (panes, tokens, the split tree), "State on disk"
(the new `settings.json` keys), and "Traps already hit" (the Fluent traps no
longer apply; add what this work learned). `apps/zerg/RULES.md` already got
its Party line sentence in step 6.9. Leave `apps/zerg/UX_Feedback.md` alone.

**Acceptance:** all ten sheets have been compared with the running app in
both themes where a sheet exists; the names list from step 0.3 is intact
apart from documented removals (the Hide / Show characters button if Q18
removes it); no dead code from the old look. Build,
tests, naming check.

---

## 6. Phase summary

| Phase | Delivers | Size (DESIGN.md's S / M / L) |
|---|---|---|
| 0 | Baseline captures and numbers | S |
| 1 | Tokens, fonts, type; the app in its new colours on Fluent templates | S to M |
| 2 | Own control templates; Fluent switched off | M to L |
| 3 | Title bar with tabs, command bar, status line | M |
| 4 | Figures band with marks and state tag, characters line, View band | M |
| 5 | Pane control, split-tree model and host, default arrangement, drill-down as a pane | M |
| 6 | Dense tables, back-shaded rows, low accuracy mark, Party line, Actions marks, hit list | M to L |
| 7 | Chart restyle, end labels, histogram band and median | S to M |
| 8 | Compare restyle and run colours | M |
| 9 | Settings page with three new rows and the colour picker | M |
| 10 | Panel bar, strip, halo, tray menu heading | M |
| 11 | Resize, fold, move, lock, reset, persistence, keyboard | L |
| 12 | Light theme, states, accessibility, performance, clean-up, documents | M |

---

## 7. Risks and tricky areas

**R1. The custom title bar.** `MainWindow.xaml.cs` restores placement with
`WindowPlacement.Apply` from `SourceInitialized` and sets
`WindowState = Maximized` afterwards; `apps/zerg/CLAUDE.md` records that this
order is load-bearing. `WindowChrome` changes the non-client area, so:
(a) a maximized window overhangs the screen by the resize border and needs an
inset on the root element; (b) the saved rectangle must still restore to the
same pixels on a monitor of another scale (test with a saved placement on the
second monitor, maximized, and the off-screen record at about -32000);
(c) the Windows 11 Snap Layouts flyout does not appear over a custom
maximize button without answering `WM_NCHITTEST` with `HTMAXBUTTON`, which
then breaks WPF's own hover and click on that button. *Recommended:* ship
without the flyout first (drag-to-snap and Win+arrows still work, as
`DESIGN.md` section 6 notes), and add the hook only if asked. Also test the
system menu (Alt+Space), double-click on the bar, and that hiding to the
tray on minimize is unchanged.

**R2. Leaving Fluent is a startup risk, not a compile risk.** A
`StaticResource` that no longer resolves throws when the XAML loads. The
type-keyed bases (`{StaticResource {x:Type Button}}` in `App.xaml`,
`TrayMenu.xaml`, `Views/SettingsPage.xaml`), `AccentButtonStyle` and
`DefaultToolTipStyle` are the known ones; step 2.2 removes them before step
2.5 flips the switch. *Recommended:* do 2.5 alone, and walk its checklist.
Some lazily created templates (tooltips, the tray menu, popups) only fail
when first opened, so open each one.

**R3. Resource lookup order between Fluent and Zerg's dictionaries** (step
1.4) was reasoned, not run. If Fluent's brushes win, skip the alias step and
go straight to step 1.5; nothing else depends on it. **Settled 2026-10-06:
Zerg's win, and R3's route was not needed** (section 10, Phase 1).

**R4. Type that WPF handles awkwardly.** (The three checks below were made
on 2026-10-06, off screen: WPF was asked which face it picks, and a test
string of each was drawn to a picture. Section 10, Phase 1.)
- *Letter spacing:* `TextBlock` has none. Step 1.7's `Caps` element draws
  tracked text itself. If it proves fiddly (fallback fonts, high DPI), plain
  upper-case SemiBold text without tracking is an acceptable interim.
  **Found:** not fiddly once the text formatter does the shaping; the
  interim was not needed.
- *Weight 500:* Segoe UI Variable is seen by WPF only under its optical-size
  family names (the note in `App.xaml`), and a Medium face may not be among
  the named instances. Verify what `FontWeight="Medium"` renders; if it is
  not distinct from Regular, use Regular for column headings and toggle
  labels (Q19). **Found:** there is no Medium face (the named weights are
  300, 350, 400, 600, 700), and asked for Medium WPF picks **SemiBold, not
  Regular**. So "Medium" in XAML would silently be SemiBold. Column headings
  were made Regular, as Q19 decided; step 2.1b's toggle label should be
  Regular too, written as such.
- *Segoe UI Variable Small* and *Cascadia Mono* (a variable font installed
  with Windows Terminal) may not resolve by those names; each has a fallback
  in its font resource. Check with a visible test string before relying on
  them. **Found:** both resolve by those names on this machine
  (`SEGUIVAR.TTF`, `CASCADIAMONO.TTF`), and Small is a different cut from
  Text (wider, more open).
- *Tabular figures* stay on every figure (`Typography.NumeralAlignment`), and
  drawn text goes through `Charts/DrawnText.cs`, which is the only way to get
  them in `OnRender` code. New `OnRender` elements that draw digits must use
  it too.

**R5. One-device-pixel rules.** The design wants every rule to be one device
pixel on a pixel centre. A `Border` one unit thick is 1.5 px at 150% and is
rounded to 2 px by layout rounding. Charts already solve this (`Chart.Hair`,
`SnapLine`). *Recommended:* publish a per-window `Hair` thickness (one
divided by the window's DPI scale) as a resource from `MainWindow` and
`PanelWindow` on `SourceInitialized` and `DpiChanged`, and use it through
`DynamicResource` for row and pane rules; a panel can sit on a monitor of
another scale, which is why it is per window. Remember that a row is rounded
up to whole pixels (the 150% trap in `apps/zerg/CLAUDE.md`), so a panel's
opening height must be recomputed for the 30 px bar and 20 px rows.

**R6. Pane host: custom panel or nested grids.** `DESIGN.md` section 6
suggests nested `Grid`s with `GridSplitter`s and re-parenting the cards, and
itself warns that a re-parented card rebuilds its visuals (charts repaint,
lists remake rows, UI Automation peers go stale: the `AppTheme.RereadRows`
trap). *Recommended:* the custom `Panel` of step 5.3, which only changes the
rectangles its children are arranged in. The cost is writing divider thumbs,
arrow keys and double-click by hand (step 11.3), which is small. If the
nested-grid route is taken instead, call the equivalent of
`AppTheme.RereadRows` after every move and expect list scroll positions to
reset.

**R7. The panel halo costs a bitmap pass per panel.** A `DropShadowEffect`
on a panel's body is re-rendered whenever anything inside changes; the
cumulative panel changes at the draw frequency. `apps/zerg/CLAUDE.md`
measures about half a percent of a core per chart draw a second already.
*Recommended:* measure at 30 a second with the cumulative panel up. If the
halo adds more than a few percent, apply it to text containers only (strip
rows, table rows, the bar) and draw the chart's labels with their own dark
outline in `LineChart.Paint`, or keep the halo off the chart panel.

**R8. Hidden things still cost.** WPF lays out text it is not showing, and
`AffectsRender` redraws charts nobody can see (both in
`apps/zerg/CLAUDE.md`). Folded panes, the stacked fallback and stand-ins
create more ways to be "not on screen". *Recommended:* a folded or floated
pane's card is `Collapsed`, and the per-beat DPS and HPS rewrite
(`MainViewModel.Tick`, gated by `Seen && ShowsParse`) stays gated; do not add
any per-beat work for the new marks (they are per count).

**R9. Row templates run hundreds of times.** A full alliance with every
heading open is a few hundred action rows; each new element in a row
template multiplies. *Recommended:* keep marks as single `OnRender` elements
rather than nested panels, freeze and cache every brush (as `Solid` does),
reuse `Views/Grow.cs` for every shade (a transform, so no layout per frame),
and do not put an effect on any row.

**R10. `Rows.Sync` identity and animation.** Rows are kept across counts so a
new total rewrites one cell. A shade bound to `Fraction` animates from its
old length on a kept row; a row that is re-created starts at its length
(`Grow` does not animate what is not visible). Do not rebuild row lists when
a shading setting flips: flip the trigger.

**R11. Panels must still never take the keyboard.** Everything in
`Native/Overlay.cs` and the move and resize code in `PanelWindow.xaml.cs`
stays. New panel chrome must not introduce a focusable popup or a
`ContextMenu` on a panel, and tools hidden at rest must also be not
hit-testable so an invisible slider cannot be dragged.

**R12. Automation names are the test harness.** `tools/drive.cs` is how every
phase is verified, and it finds things by name. Replacing a `TextBlock` with
a custom element removes its text from `drive.cs text` unless the element
has an automation peer. Budget for a peer on `Caps`, `StateTag` and the
figures band.

**R13. The colour picker is a small control with many edges:** dragging in
the square and strip, typing a code, invalid input, closing, two wells
sharing one popup, live preview while dragging (each change recolours the
Compare tables and charts). *Recommended:* apply live to the brushes but
debounce the save and the Compare redraw.

**R14. Settings compatibility.** Old `settings.json` files have none of the
new keys (defaults must give the installed look) and newer keys must be
ignored safely by an older build (`Settings.Unknown` already round-trips
them). A computed property on `Settings` needs `[JsonIgnore]` (existing
trap).

**R15. Test coverage is `Zerg.Core` only.** There is no automated check of
any view. This plan moves every new rule that can be stated without a window
into `Zerg.Core` (shade strength, run band, low mark, split tree, colour
maths, histogram and label geometry) so it is tested; everything else is
verified by the replay recipe and the sheets.

---

## 8. Open questions and design ambiguities

**All of these were settled with the owner on 2026-10-05.** The right-hand
column is the decision, not a suggestion. The owner took the plan's
recommended default for every question except Q3 (no sort mark) and Q21 (the
tray heading shows only while armed or running). "Sheet" means the SVG of
that number.

| # | Question | Decision |
|---|---|---|
| Q1 | **The naming rule against the design.** Do `DESIGN.md` or the sheets use either banned term (for the parse view or anything else)? | No. The project's check finds nothing in `apps/zerg-mockup`, so there is nothing to replace. Keep running the check; if a later revision of the design introduces the banned term for the parse view, use "parse" or "session" as the app does today |
| Q2 | **The View tab with nothing open.** The tab "appears while a parse is open", but the View section can be on screen with nothing open (after Close, or after a file that would not open); sheet 07 draws that state with no tab selected | Show the View tab while `HasParse` or `IsView`, without a file name or close mark when nothing is open, so the section on screen always has a selected tab |
| Q3 | **The sort mark.** Sheets 01, 04 and 08 draw a small triangle beside "Damage" and "Total"; `DESIGN.md` lists "the sort mark" among the icons but describes no sorting, and the app has none | **No mark.** Nothing can be sorted, so the triangle is left out (step 6.8). No sorting is added |
| Q4 | **"Every hit" lists misses.** Sheet 01 says "Every hit 227" for 200 hits and 27 misses, and sheet 10 B draws a "miss" row. Today the list is the uses that hit (`Distribution.Events`) | Keep hits only and show the hit count. Listing misses needs a `Zerg.Core` change and is new scope |
| Q5 | **The Party line and the low accuracy mark** (left open in `DESIGN.md` 5) | Not marked, as the sheets draw it |
| Q6 | **Is Lock layout remembered between runs?** (left open in `DESIGN.md` 5) | Yes: save `layoutLocked`. An arrangement locked because it is settled should stay locked |
| Q7 | **Panel tools and the title.** `DESIGN.md` says opacity and Dock appear "in the title's place"; sheet 04 panel 3 shows the title still there, moved left of the tools and brightened | Follow the sheet while there is room; under about 360 px the tools replace the title |
| Q8 | **Panel backdrop and rules.** `DESIGN.md` says a black backdrop and rules at 8, 16 and 28%; the sheets draw `#06080C`, a 13% frame and a 10% bar rule | Black as today (the difference cannot be seen at any opacity) and the three documented strengths: frame 16%, bar rule 8% |
| Q9 | **Compare row heights.** `DESIGN.md` gives 36 "where a cell holds A over B" and 23 for spread lines; sheet 03 also draws 32 px action rows in the well and 28 px rows in By damage type and By target, all with A over B | Follow the sheet: 36, 32, 28, 23 |
| Q10 | **The figures-band marks.** "Per 20 s" is drawn for a 14 minute fight (44 bars). Nothing says what happens in a two-hour session, what "running" plots exactly, or what Healing shows | Bucket 20 s until more than 44 bars would be needed, then double the bucket and the caption ("per 40 s"); "running" is the cumulative party total over elapsed time at each grid time; Healing shows the same three from healing data; nothing before the clock starts |
| Q11 | **Light theme gaps.** No text-on-colour inks for `Live` and `Crit`; light Compare is "described only" and its 16% band leaves green at about 4.4:1; the light run pair "was not reworked"; "one pair of run colours serves both themes once set" is an assumption awaiting the owner | White on all three light fills (about 5:1 each); compute the light band by the same rule as dark, with the light `Crit`, and let it come out fainter than 16% if it must; keep `Light.xaml`'s current pair as the light defaults; a colour the user picks applies to both themes. The design's other stated assumptions stand until the owner says otherwise: the band never exceeds 26%, nothing stops the two runs being set alike, and a colour stays with its slot on Swap |
| Q12 | **Run band for the installed blue.** The stated rule computes 21% for `#3987E5`; `DESIGN.md` says 20% | Compute it. One point is not visible, and a hard-coded exception would defeat the rule for picked colours |
| Q13 | **Strip shade strengths.** The stated rule (cap 34%) does not reproduce the sheet's SMN 31% and WHM 27% with any inputs tried | Use panel `Text2` (`#B9C1CF`) at 4.5:1 over `#06080C`, cap 34%: SMN 32, WHM 28 |
| Q14 | **The low accuracy grading.** Bands are given in words and by example; the generating script is not in the repository | The fitted function in step 6.7 |
| Q15 | **The folded drill-down.** With no action picked it is "folded to its heading", but no sheet shows that heading; and the open pane has both Close and a fold chevron | Folded title "Drill-down" with the note "Select an action to drill down" (or "Showing in the Actions panel" while that floats). Close clears the pick (the pane then folds by itself); the chevron folds without clearing |
| Q16 | **Tables in a pane narrower than their columns.** Sheet 10 B shows Actions shedding Miss, Min and the mark. Nothing is said for the 14-column table or for Heals | Per-character tables scroll sideways under about 900 px, as they do today under 1120; Actions sheds as drawn; Heals sheds Min under about 480 |
| Q17 | **The armed pulse.** Today a ring spreads from the status dot while armed (`ArmedRing` in `MainWindow.xaml`). The sheets draw a plain dot and a static ring on the ARMED tag, and `DESIGN.md` says nothing needs per-frame animation, but also that armed "is loud" | Drop the pulse; the amber pair, clock, tag and dot carry it. The owner confirmed the removal |
| Q18 | **Chips that do not fit, and Hide / Show.** "More than fit become '+5'", with no word on what pressing it does. The Hide / Show button and its `charactersOpen` setting are not drawn | "+N" is a toggle that opens the line into today's wrapping list (capped at 92 px, scrolling) and back; it reuses `charactersOpen`. The Hide / Show button goes. The hint text still names who is excluded |
| Q19 | **Fonts.** Weight 500 may not exist as a face WPF can pick; Cascadia Mono is a variable font WPF may not see; bundling a static face adds a licence file to what ships | Regular where Medium is not distinct; `Cascadia Mono, Consolas` with no bundling (packaging is scheduled separately) |
| Q20 | **Information that lived on removed things.** The bar chart's hover card had "Avg / action"; the legend's tooltip named the members of the grouped line | Put "avg per action" in each table row's tooltip (the strip already does); append the members to `GroupTip` |
| Q21 | **Tray heading in other states.** Drawn only live, with damage | **Live only.** The heading (state tag, `ClockText` and "`TotalText` damage") and its separator are shown only while the session is armed or running; otherwise the menu has no heading (step 10.5) |
| Q22 | **The figures band in a narrow window.** Five cells do not fit the 520 px minimum width | Keep today's `FitTiles` breakpoints (five across from 1040, three from 640, else two) as rows of 64 px cells; hide a cell's mark when the cell is under 240 px wide |
| Q23 | **Chart details not in `DESIGN.md`:** the green live-edge rule; end labels showing names only (today's floating labels print name and total); marker and line weights | Follow the sheets for weights and the live edge; keep name and total in end labels where there is room, name only otherwise (`DESIGN.md` 7 calls the charts "a restyle in that spirit") |
| Q24 | **Wording slips on the sheets:** the status line omits the line count on sheet 10; "1 misses" on sheet 10 B; the event file name pattern | The app's existing wording wins everywhere (`DESIGN.md` 5, "Kept on purpose: every word of Zerg's vocabulary") |
| Q25 | **"As its pop-out already sheds columns."** `DESIGN.md` 4 says the Actions pop-out sheds columns; in the code it scrolls sideways under 620 px and sheds nothing | Treat shedding as new behaviour (step 6.12) and apply it to the pop-out too |
| Q26 | **Windows 10.** The manifest still declares it; rounded window corners, Segoe UI Variable and Segoe Fluent Icons are Windows 11 | Accept square corners and the declared font fallbacks; do not draw corners by hand |
| Q27 | **Heading menu "Move left / right / up / down".** Not defined beyond its name | Swap with the neighbouring pane on that side; do nothing at an edge |
| Q28 | **Second character on a job.** The sheets' second BLM (`#C4AED3`) is slightly lighter than `Shades.Step` produces | The code wins ("a shade of it, as now") |

---

## 9. What could not be verified or mapped

- **The current app's on-screen look.** It was read, not run. Phase 0 exists
  to capture it. (Captured since: section 10, Phase 0.)
- **Resource precedence in step 1.4** (Zerg's dictionary over Fluent's): see
  R3. (Settled in Phase 1: Zerg's wins.)
- **What WPF renders for** `Segoe UI Variable Small`, weight 500 and
  `Cascadia Mono` on this machine: see R4. (Checked in Phase 1: both names
  resolve; weight 500 is drawn as SemiBold.)
- **`WindowChrome` with the existing placement code** on a mixed-DPI pair of
  monitors: see R1.
- **Exact generator rules** behind the strip strengths, the blue run band and
  the low accuracy grading: fitted or approximated (Q12 to Q14). The script
  that drew the sheets is not in the repository.
- **Sheets that do not exist** and therefore have no reference to compare
  with: light Compare, Compare with character shading off, a rearranged
  Healing or Compare section, the locked layout, the sideways rail of a pane
  folded alone in its column, the folded drill-down heading, the layout in a
  narrow window, the figures band below five cells, and a panel narrower
  than 440 px.
- **Design elements with no counterpart in the app** (new scope, each called
  out above): the Party line's party-wide rates (step 6.9), listing misses
  in the hit list (Q4, decided against), column sorting (Q3, decided against,
  and its mark is not drawn), column shedding (Q16, Q25, adopted).
- **Not adopted from `apps/zerg/UX_Feedback.md`:** that audit recommends
  further changes (session hot keys, confirm on close, data freshness on the
  status line, and others). The design did not take them up and this plan
  does not include them.

---

## 10. Hand-over notes

### Phase 0, 2026-10-05: stopped early at the owner's request; closed 2026-10-06

**Closed.** Phase 0 was stopped before it was written up in full, because it
was running more on-screen checks than the owner wanted. No source file was
changed. Nothing was committed. It was closed on 2026-10-06 without another
on-screen run:

- The owner accepted the baseline as taken (the decision table below): 1440
  by 900 on the primary monitor at 100%, dark, installed defaults, panels
  over the main window, the two Settings grabs cut off at the top. No
  retakes.
- The CPU baseline (0.4) is dropped, not owed.
- Chart automation peers are deferred to step 12.3, not owed here.
- The index of the saved files, the one thing still missing, is written:
  `INDEX.txt` at the top of the baseline folder, from what is on disk. It
  lists each state, what was pressed to reach it, which of the three files it
  has, and what is not in the baseline.
- The acceptance gates were run again on the tree as Phase 0 left it, before
  any Phase 1 edit: build 0 warnings and 0 errors, tests 227 of 227, naming
  check prints nothing.

Nothing else in these notes was found open. What follows is the record as
the previous engineer left it, with the index line brought up to date.

**Done**

- 0.1: `CLAUDE.md`, `RULES.md` and `DESIGN.md` sections 1 to 5 read, and this
  plan in full.
- 0.2 and 0.3: the fixture generated (809 lines, the same bytes as the copy
  already there); the replay recipe run on the unmodified Debug build at
  1440 by 900 on the primary monitor, dark theme, installed defaults. Saved
  for each state: a `drive.cs snap` grab, `text` and `names`. States: Damage
  (idle, armed, running, held, an action opened onto its drill-down and hit
  list), Healing (and a heal opened), all six panels, a stand-in, Settings,
  the light theme (Settings, Damage, Healing, one panel), View (both sides,
  and with nothing open), Compare (empty, one slot, both, a row opened onto
  an action and its distribution, by job, healing), the tray menu and its
  Panels submenu, restart asked, names hidden with skillchains off, a panel
  in click-through, two hover cards and a column-heading tooltip.
- Build 0 warnings, 0 errors; tests 227 of 227; naming check prints nothing
  (all three run at the start, on the unmodified tree).

**Not done, or only partly**

- 0.4, the CPU baseline, was **not settled, and the owner then dropped it**.
  The same Release build in the same state (Damage on screen, session
  running, 30 a second, no panels, no UI Automation client) read 14.6, 15.1
  and 15.7 percent of one core in two runs, and 18.4 to 20.7 in two runs
  forty minutes later. `CLAUDE.md` said 23 to 25. The cause of the spread
  was not found. The readings and the Release build they were taken from
  have been deleted; `CLAUDE.md` now carries the spread as a caution.
- The Settings grabs (`16-settings.png`, `17-light-settings.png`) were taken
  with the page scrolled a little: the title row and its Close button are cut
  off in the picture. Their `text` and `names` files are whole.
- No index of the saved files was written at the time. It was written on
  2026-10-06 (`INDEX.txt`, in the folder below). What in this plan and in
  `CLAUDE.md` disagrees with the code is under "Findings" below.

**Where the baseline is** (outside the repository)

`%LOCALAPPDATA%\VibeXI\zerg-redesign\phase0\`

| Folder | Holds |
|---|---|
| `INDEX.txt` | every state by number: what is on screen, how it was reached, which files it has; the other files; what the baseline does not hold (written 2026-10-06) |
| `captures\` | 56 grabs, `01-...` to `37-...`; a `-scrollNN` suffix is the same state with the page scrolled that far |
| `text\`, `names\` | `drive.cs text` and `names` for the same state numbers; `names\_all-named-controls.txt` lists every named control seen and the states it was seen in |
| `fixtures\` | the fixture, and `Test_RunA.zerg` / `Test_RunB.zerg` (the second from `--seed 7 --date 2026-07-31`) used for View and Compare |
| `settings-backup\` | the owner's `settings.json` as it was, and the variants the runs used (`settings.baseline-dark.json` is the one the grabs were taken under) |
| `scripts\` | `capture.sh <out dir>` replays the whole sequence above, with its helpers in `lib.sh`. `cpu.sh` is left over: the Release build it ran has been deleted. `capture.sh` was run once in full and gave `text` and `names` identical to the saved ones in every state but the running one, apart from the "started HH:MM:SS" lines |
| `events\` | the scratch events folder the scripts point Zerg at; one empty file |
| `recheck-run.log` | what that one full repeat run printed. The folder it wrote to has been deleted; `names\_repeat-run-04-damage-paused.txt` and its `text\` twin are what is left of it |

**Three things to know before using it**

- Press Pause only after the time `replay.py` prints second has passed. A
  press before it cuts the session short, silently. A whole session reads
  153,687 damage in 00:24.
- `drive.cs text` also prints sections that are not on screen, as they last
  were. Two `text` files compare only if the same presses led to both.
- Charts are not in `names` or `text` at all: they carry an automation name
  in XAML but have no automation peer.

**Decisions by the owner after Phase 0** (2026-10-05; each is already written
into the step it changes)

| Subject | Decision |
|---|---|
| Panels under the light theme | `Themes/Panel.xaml` is created and merged in Phase 1, at the start of step 1.5, not in step 2.5 |
| Step 1.4, aliasing Fluent keys | Try it, and test a theme switch that keeps dark or light the same before relying on it; if it fails, skip 1.4 (R3) |
| CPU baseline | Dropped. Steps 0.4 and 12.4 and the acceptance lines of phases 7 and 12 no longer ask for a CPU comparison |
| The baseline as taken | Accepted as it stands: 1440 by 900 on the primary monitor, installed defaults, dark theme, panels captured over the main window, Settings grabs cut off at the top. No retakes |
| Chart automation peers | Open. Decide at step 12.3 |
| Real-mouse work (`press`, `hover`, `drag`) | On the primary monitor only. `drive.cs` is not to be changed for this |
| On-screen checking in general | Keep it short. Phase 0 was stopped for running too many checks: verify what a step's acceptance asks for, once, and do not repeat runs to confirm a result already in hand |

**Findings** (from reading the code unless it says "seen")

- **Step 1.5 and panels.** `PanelWindow.xaml` sets `ThemeMode="Dark"` on the
  window, so today a card inside a panel gets dark Fluent brushes whatever
  the application's theme. A card moved to token keys resolves them from the
  application's dictionary. Hence the decision above. Not run.
- **Step 1.4 and a theme switch.** `AppTheme.Apply` assigns
  `Application.ThemeMode` every time, and `Refresh` returns early when dark
  or light has not changed. Whether WPF then puts its dictionary after
  Zerg's was not run.
- **The replay recipe and the live edge** (seen). Every row arrives at once,
  dated ahead of the clock, so the one count they cause builds a cumulative
  chart that is not live (`Counting.Cumulative`: `live` is `now > t1`), and
  no later count comes. The chart's right-hand edge stands still while the
  session runs. One more line after the last row's time has passed, a `meta`
  line that counts nothing, causes the count that makes it live. The recipe
  in `CLAUDE.md` now has that line and the wait before Pause. Step 7.2's
  live-edge rule cannot be seen without it.
- **`drive.cs` on the second monitor** (seen, partly diagnosed). At 150%, a
  point read off a grab missed three times with `press` and `hover`; the
  same point divided by 1.5 hit. Grabs and window frames were right.
- **Event file names.** The addon in this repository writes
  `<Character>-<YYYYMMDDHHMMSS>.jsonl` (`addons/VibeXI/vx_emit.lua`, changed
  on 2026-10-05), as `CLAUDE.md` says. The files in the owner's events
  folder, the fixture and the recipe's examples still have the older shape,
  `<Character>_<YYYY.MM.DD>.jsonl`. Zerg follows the newest `*.jsonl`
  whatever its name, so both work.
- **Two scripted runs misbehaved** (a Zerg minimized to the tray seconds
  after starting; a real press on Start that did not register). The owner
  was using the machine at the time and takes both to be that.
- **Corrected in the documents on 2026-10-05:** the monitors' refresh rates
  and the caution on the CPU figures in `CLAUDE.md`; in `RULES.md`, that DPS
  is rewritten at the draw frequency, not four times a second.

**What Phase 1 should check first**

1. That no `Zerg.exe` of the owner's is running, and copy `settings.json`
   aside.
2. Step 1.4's aliases through a System to Dark switch on a dark Windows,
   before migrating anything.
3. A panel under the light theme, straight after `Themes/Panel.xaml` is
   merged and the first card is migrated.

All three were done first in Phase 1; the outcomes are below.

### Phase 1, 2026-10-06: tokens, fonts and type

Steps 1.1 to 1.7 are built. Nothing was committed. The app was launched
three times, about eight minutes on screen in all, and the real mouse was
not used. "Seen" below means in a grab of the running app; "off screen"
means a scratch program asked WPF or drew to a picture without showing a
window; "built" means it compiles and was not looked at.

**The screen was not the baseline's.** On 2026-10-06 the machine had one
screen, 2560 by 1600 at 150%. Phase 0 was taken on a 3440 by 1440 screen at
100%. The baseline's settings file gives a window of 1440 by 900 pixels,
which at 150% holds 960 by 600 units and lays the tiles out three across. So
the full pass below ran in a window of 2160 by 1350 pixels, the same 1440 by
900 units, from a settings file of its own. `names` and `text` compare with
the baseline; no grab compares pixel for pixel.

**Done, step by step**

- **1.1** Tokens in `Themes/Dark.xaml` and `Themes/Light.xaml`: every key of
  sections 4.1 and 4.2, the tag brushes, the overlays, and `HoverCardBrush`.
  The light tag tints for Idle and Held were worked out by the stated rule
  (`#EDEEF0` / `#BBBFC7` and `#F4ECE0` / `#D6B78C`); the rule gives the
  sheet's own values for the four pairs the sheets draw. `Series*`, `Job*`
  and the three run brushes are untouched.
- **1.2** `AppTheme.Token(key, dark)`. Nothing calls it yet. Built.
- **1.3** `SmallFont`, `MonoFont`, the keyed measures, and
  `Application.Resources` as an explicit dictionary with an empty merged
  list. Nothing uses the measures yet except `CellPadding` (in `Cell`) and
  `BarRadius` (in `Swatch`).
- **1.4** The aliases, `MainWindow`'s background and ink, and
  `ChartSurfaceBrush` on `Bg1`. **The aliases hold**; R3's route was not
  taken.
- **1.5** `Themes/Panel.xaml`, merged in `PanelWindow.xaml` before anything
  else was moved; then every Fluent brush key in `src/Zerg` replaced by a
  token, in the sixteen XAML files and `Charts/Chart.cs`.
- **1.6** Type styles re-valued, new ones added, old-scale overrides
  removed. The step lists exactly what.
- **1.7** `Views/Caps.cs`, on a letter-spaced draw added to
  `Charts/DrawnText.cs`.

**Not done, or only partly**

- The new type styles and `Caps` are **used nowhere**. So only part of the
  kit's type block is on screen: the figure (22), a cell (12), a column
  heading (11), a note (11), a job (10.5). The page title, pane title,
  drill-down title, capital labels and the mono file name are as they were.
  The step asks to add those styles, not to put them on; the phases that
  rebuild each screen do that. A question for the owner.
- `PanelWindow.xaml`'s `ThemeMode="Dark"` and two literal brushes, the
  `AccentButtonStyle` in `Views/ViewCard.xaml`, the type-keyed style bases:
  all phase 2's, as planned.
- The aliases are still in the two theme files, as planned (2.5 removes
  them).

**Verified, and how**

- *Step 1.4, off screen:* WPF keeps the Fluent dictionary first in
  `Application.Resources.MergedDictionaries` and replaces it in place on
  every `ThemeMode` change (System, Dark, Light, System again); a dictionary
  added later stays last and answers first. Replacing
  `Application.Resources` with an explicit dictionary after `ThemeMode` is
  set, which is the order `App.xaml` now has, keeps the Fluent dictionary.
  A window with `ThemeMode="Dark"` and an explicit dictionary of its own
  keeps its Fluent set first and the merged file second.
- *Step 1.4, seen (launch A, before anything but one card was moved):*
  started under System on a dark Windows, then Dark, then a session, then
  Light, then Dark again. In each grab the window is `Bg0` and a card `Bg1`
  with a `Line2` edge, to the exact colour values, in both themes, while
  `App.xaml`'s `Card` still asked for the Fluent keys.
- *A panel under the light theme, seen (launch A, and again in launch B
  with everything moved):* light ink on the dark panel; job names in the
  panel's `Text2`; in launch B the pair, the clock and the total in amber.
- *The palette in both themes, seen (launch B, the baseline's whole
  sequence, 53 grabs):* dark surfaces blue-black, accent amber, the clock
  signal green while live and amber while held, the status dot the same;
  light as section 4.1. Every section, the six panels, a stand-in, Settings,
  View, Compare with a row opened onto an action, the tray menu and its
  submenu, a tooltip: all opened without an error, and `zerg.log` has
  nothing but the usual lines.
- *Automation names (launch B):* compared file by file with Phase 0, start
  times apart. No line of the baseline's is missing from `names` or from
  `text` in any of the 33 states that hold still. The running state (03)
  differs only in what the clock moves, as it did between Phase 0's own two
  runs. Nine states have three lines more: the tooltip of the button the
  script had just pressed (the focus tooltip in `CLAUDE.md`'s traps). Why it
  was still up in these runs and not in Phase 0's was not looked into; no
  tooltip code changed.
- *Fonts (R4), off screen:* each family name asked of WPF's own typeface
  matching, and a test string of each drawn to a picture. `Segoe UI Variable
  Small` and `Cascadia Mono` resolve. No family has a Medium face; Medium is
  drawn SemiBold.
- *`Caps`, off screen only:* drawn from the built `Zerg.dll` at 100%, 150%
  and 300%. With no tracking it is the same width as a `TextBlock` of the
  same text in capitals, and looks the same beside it; with 0.7 it is wider by 0.7 per
  gap; "12/12" and "11/11" come out the same width; a character the face
  lacks is drawn from the fallback font; a family that is not installed
  still draws. Its peer answers control type Text, class `Caps`, and the
  text as written.
- Build 0 warnings, 0 errors; tests 227 of 227; the naming check prints
  nothing; `FillColor`, `StrokeColor` and `ElevationBorder` are found only
  among the aliases in the two theme files; no `StaticResource` names a
  token outside them.

**Not verified**

- **The final build's Damage and Healing tables.** Launch B showed the names
  in table rows at 14 units, larger than before (see "Traps"). They were
  given an explicit 12 and one short launch (C) was made to look. Its
  Compare grab shows the names at 12. Its Damage and Healing grabs were lost
  to a slip in the script (all three were written under one name) and were
  not retaken, to keep to the owner's rule. Those rows carry the same edit;
  it is built, not seen. Launch B's pictures are therefore one fix behind
  the final build in exactly that respect.
- `Caps` in the running app, and whether `drive.cs text` reads it there.
- A chart's hover card inside a panel (it needs the real pointer). The
  brush it now asks for is solid there by construction.
- The 100% screen, the second monitor, any other window size, Windows 10.
- `AppTheme.Token`: nothing calls it.

**Where the plan or `CLAUDE.md` disagreed with what was found** (each is
corrected in place)

- Step 1.4 said the aliases would recolour Fluent's own templates. They do
  not, visibly: a checked toggle stayed Fluent's blue with
  `AccentFillColorDefaultBrush` aliased to amber.
- Step 1.5 sent the hover card's fill to `Bg4Brush`, and section 4.3 makes
  `Bg4Brush` white at 20% in a panel. `Charts/Chart.cs` says the card is
  opaque on purpose. `HoverCardBrush` was added to keep it so. The owner
  confirmed it (decisions below).
- Step 1.7 described a `GlyphRun` built from the glyph typeface. That gives
  neither tabular figures nor font fallback; the element was built on the
  text formatter instead.
- R4 and Q19 expected Medium to fall back to Regular. It falls to SemiBold.
- Section 4.5 lists `Caps`, `GroupCaps` and `TagText` among `TextBlock`
  styles. They are styles for `Views/Caps`.
- `CLAUDE.md` described `App.xaml`, `AppTheme.cs` and `Themes/` as they were
  before the tokens, and had the machine's screens as a pair. It now has the
  tokens, `Themes/Panel.xaml`, `Caps`, the single 150% screen of this day,
  and six new traps.

**Traps hit**

- **Text with no size of its own is 14 units, not 12.** The Fluent window
  style sets the window's font size to 14, and a `TextBlock` with no
  `FontSize` and no style inherits it. Removing `FontSize="13"` from a name
  made it larger. Give it 12 explicitly. This will bite again at step 2.5:
  when the Fluent theme goes, the inherited size drops to WPF's own 12, and
  everything that leans on it shrinks (the chips' names, the labels in the
  Settings page's theme choice, any stock button's text that has no size
  set).
- **One key's name inside another's** (step 1.5).
- **A one-unit border is two device pixels at 150%** (R5, seen in every
  grab). Nothing was done about it; it is R5's.
- **A script written with a here-document through the Bash tool lost a
  backslash** (`\\` arrived as `\`), which is how launch C's grabs were
  written over each other. Write scripts with the editor tools. `CLAUDE.md`
  has it.
- **The Phase 0 capture script at 150%** needs the larger window to mean the
  same thing. `phase1\scripts\capture.sh` takes `VARIANT=<settings file>`.

**Where Phase 1's files are** (outside the repository)

`%LOCALAPPDATA%\VibeXI\zerg-redesign\phase1\`, with an `INDEX.txt`: `a\`
(launch A's seven grabs), `captures\`, `text\`, `names\` and
`capture-run.log` (launch B), `c\` (launch C's one grab), `settings\` (the
150% settings file), `scripts\` (the capture script with `VARIANT`).

**Decisions taken without the owner, each easy to turn back** (the owner
was asked about each afterwards and let every one stand; see the next
table)

| Subject | What was done | To turn it back |
|---|---|---|
| A chart's hover card in a panel | Solid, through a new key `HoverCardBrush` | Point `CardFill` in `Charts/Chart.cs` at `Bg4Brush` and delete the key from the three theme files |
| Column headings' weight | Regular (Q19), where the design's Medium would be drawn SemiBold | One setter in `Head` in `App.xaml` |
| New type styles | Defined, put on nothing | Nothing to undo; putting them on is each screen's phase |
| `CardTitle` | Left at 16 in `NumberFont`: step 1.6 does not list it | Base it on `PaneTitle` |
| The three smaller figures' top margin | Removed with the hero figures' size, so the five figures stand level | Put `Margin="0,8,0,0"` back in `MainWindow.xaml` |
| Sizes the plan does not give | Empty-table and drop lines 12 (sheet 07); `ChangeText` 12; `RunPair` 11.5 | The attributes in those files |
| What `Caps` tells UI Automation | The text as written, not in capitals | `Peer.GetNameCore` in `Views/Caps.cs` |
| The aliases after 1.5 | Kept until 2.5, as planned, though they do little | Delete the block at the end of each theme file |
| Running the full capture once | One pass of the baseline's sequence, as the acceptance asks for the names | Nothing to undo |

**Decisions by the owner after Phase 1** (2026-10-06; these are settled, not
suggestions)

| Subject | Decision |
|---|---|
| A chart's hover card in a panel | Solid. `HoverCardBrush` stays |
| Which screen a phase is verified on | Whatever is attached. Compare in units, and say in the phase's notes which screen and scale were used. No second baseline. Which scale counts for the pixel-level rule checks of phases 5 and 6 is still open |
| New type styles defined in Phase 1 | Left off until the phase that rebuilds each screen puts them on |
| Column headings' weight | Regular. The same holds for the toggle label in step 2.1b and anywhere else the design says Medium |
| The window's font size before step 2.5 | Set `FontSize="12"` explicitly on `MainWindow` and `PanelWindow` before the Fluent theme is switched off, so that step 2.5 itself moves no text. Text that leaned on Fluent's 14 becomes 12 in that earlier step, on purpose |
| The sizes and the margin Phase 1 chose | Accepted as they are: empty-table and drop lines 12, `ChangeText` 12, `RunPair` 11.5, the pet figure in a Heals heading 12, no top margin on the three smaller figures, `CardTitle` left at 16 |
| What `Caps` tells UI Automation | The text as written ("Total damage"), not the capitals drawn |
| The aliases of step 1.4 | Kept until step 2.5, as planned |
| The names check at the end of a phase | **Only the screens that phase touched.** Not the whole baseline sequence. This replaces "all automation names from step 0.3 are still present" in every later phase's acceptance: check the names on what the phase changed, once |

**What Phase 2 should check first**

1. That no `Zerg.exe` of the owner's is running; copy `settings.json`
   aside; and **list the screens and their scale** before sizing a window
   or comparing a grab with anything saved.
2. The Damage and Healing tables in the first launch: names in the rows and
   in the Actions and Heals headings should be 12 units, the same as the
   figures beside them.
3. Before step 2.5, set `FontSize="12"` on `MainWindow` and `PanelWindow`
   (decided by the owner, above). Today the size is Fluent's 14 by
   inheritance; setting it explicitly before the theme is switched off
   keeps 2.5 from moving text.
4. At step 2.5, when `ThemeMode="Dark"` leaves `PanelWindow`, the panel's
   stock controls (its slider, the Dock button, a tooltip) start taking the
   application's styles, and their brushes then come from
   `Themes/Panel.xaml`. Check a panel under the light theme again there,
   and a tooltip opened from a panel.
5. `Themes/Panel.xaml` has only the keys of section 4.3. Anything in a panel
   that asks for `Bg0Brush`, `Bg1Brush`, `ShadedDashBrush`, a tag brush or
   an overlay gets the application's value, which under the light theme is
   the light one. Add the key there when a panel first needs it.
6. A new control template that asks for a token by a mistyped key draws
   nothing and says nothing. After each file, list the keys asked for
   against the keys defined.
7. Write `Regular` where the design says Medium (step 2.1b).

