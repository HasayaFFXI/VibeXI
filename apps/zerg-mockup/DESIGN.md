# Zerg: a denser, designed look (mockup)

A design exploration to react to, not an implementation. Nothing here is wired
to Zerg; nothing under `apps/zerg` was changed.

**Status: revision 3, 2026-10-05, with the palette revision of the same day
applied on top.** Every proposal has been decided by the owner and the sheets
match the decisions (section 5, "Decisions"). What is still open is listed at
the end of that section.

| File | What it shows |
|---|---|
| `01-damage-live.svg` | Damage section, session running, an action picked (the primary screen) |
| `02-healing-held.svg` | Healing section, session held, an export just made, one card floating |
| `03-compare.svg` | Compare: two runs by character, a row opened onto its actions, one action opened onto its distribution in both runs |
| `04-pop-outs.svg` | The pop-outs over the game: strip, cumulative chart, Actions with its drill-down under the row, click-through, a restart being asked about |
| `05-tray-menu.svg` | The tray menu, its Panels submenu, and the menu while a restart waits for its answer |
| `06-settings.svg` | The Settings page, with the two Row shading switches, the Low accuracy mark and Compare colours |
| `07-states.svg` | Idle with no addon, armed, restart asked, trouble, View with and without a parse, Compare with an empty slot, Hide names on with Include Skillchains off, click-through on; and the same rows under each of the four pairs of the shading switches |
| `08-kit.svg` | Tokens, type, and every stock control restyled |
| `09-damage-live-light.svg` | Screen 01 in the light tokens |
| `10-layout.svg` | The layout is the user's: a pane being moved with its drop places, the arrangement that results with a divider being dragged, and the rules |
| `png/` | The same ten, rendered at 100% |

Two other files sit in this folder and are **a record, not part of this
design**: `03-compare-palette-proposal.svg` and `08-kit-palette-proposal.svg`.
They are the colour proposals the owner chose from (decisions 16 to 19), and
they show options that were not taken as well as those that were; they have no
PNG. The colours decided are the ones in section 2, and where a proposal sheet
differs from section 2, section 2 is right.

The SVGs name fonts that ship with Windows 11 (Segoe UI Variable, Cascadia
Mono, Segoe Fluent Icons). On a machine without them they fall back and the
spacing drifts; the PNGs are the reference.

**The data is made up, and it adds up.** A twelve-character alliance fights
Kirin for 14:26. The fight was simulated swing by swing and then counted by
Zerg's own rules (per-use hits and misses, pets credited to the owner, Accuracy
over own melee and ranged swings only, WS Avg over weaponskills that dealt
damage, casts once per use), so every tile, table row, chart, distribution and
hit list on every screen is drawn from one list of events. Magnitudes were
checked against the real Kirin parse in `apps/zerg/tools/exports`; no name from
that file is used. Hasaya, Parabellum, Xatsh, Gillette and Sylviane come from
Zerg's own test fixture; the other names are invented.

---

## 1. The direction

**An instrument, not a set of cards.** The current app is a column of Fluent
cards on Mica: rounded, padded 16, one under another, so 900 pixels of height
show the tiles, one chart and the top of a table. The mockup is one ink-dark
surface cut into panes by one-pixel rules, the way a trading terminal or a DAW
is. There are no cards, no gaps between panes, no shadows inside the window and
no translucency. Colour is spent only where it means something:

- **a character** (their job's colour, unchanged from the game and from Zerg),
- **the session's state** (green is counting, amber is not, red is a
  measurement about to be dropped: Zerg's own two-colour rule, kept),
- **which run** in Compare (A blue, B orange, unchanged),
- **better or worse** in Compare (always beside a sign).

Everything else is five greys of blue-black and four of ink. The one brand
colour is the amber of Zerg's icon. It marks what is selected, the active
section, the focus ring and the slider, and it is the "not counting" colour of
the session, which reads as one idea: amber is where to look.

Two things were added after the first review, and both are shown on every
screen: **a share is drawn as the row itself**, shaded in the character's
colour to the share's length, the way the pop-out strip does it; and **the
panes are the user's to arrange**: moved by their headings, sized by the
dividers between them (sheet 10).

At 1440 x 900 the Damage section now shows, without scrolling: the five
figures, the character filter, the whole fourteen-column per-character table
with a party line, the Actions table, the cumulative chart, and the picked
action's drill-down with its histogram and hit list. The current layout needs
roughly three screens of scrolling for the same content.

---

## 2. Tokens

### Colour, dark (the default)

| Token | Value | Use |
|---|---|---|
| `bg0` | `#0A0C10` | window, title bar, status line, text box well |
| `bg1` | `#0F1217` | pane surface |
| `bg2` | `#151921` | raised: a character's heading row, the party line, a hovered row, menus |
| `bg3` | `#1C222C` | selected row, a button at rest, a toggle that is on |
| `bg4` | `#262D3A` | hover on a control, the picked segment, tooltip and hover card |
| `line` | `#1B2029` | rule between rows |
| `line2` | `#2A313D` | rule between panes, a control's edge |
| `line3` | `#3D4656` | strong edge: window frame, menu, scroll thumb |
| `t1` | `#E9ECF2` | figures, names (15.9:1 on `bg1`) |
| `t2` | `#A6AFBF` | secondary figures, button text (8.5:1) |
| `t3` | `#838DA5` | labels, column headings, notes, captions (5.6:1; 4.8:1 on `bg3`) |
| `t4` | `#5E677A` | a dash, a disabled control (3.3:1; never carries information alone) |
| `accent` | `#F2A33A` | brand, selected, focus, armed and held (9.0:1); text on it `#191004` |
| `live` | `#22E05A` | counting, better (10.6:1); text on it `#051306` (10.8:1) |
| `crit` | `#FF5063` | dropped, error, worse (5.9:1; 5.0:1 on `bg3`); text on it `#190405` (6.2:1) |
| `runA` / `runB` | `#3987E5` / `#E8792B` as installed | Compare's two runs: blue for the baseline, orange for the compared run. **Each is a setting** (Compare colours, sheet 06); badge ink `#101010` |
| job colours | as `Themes/Dark.xaml` | unchanged; a second character on a job is a shade of it, as now |
| close hover | `#C42B1C` | the caption's close button under the pointer |

Panels over the game keep a black backdrop at the panel's opacity. On it,
surfaces are white at 7% (heading row), 13% (button, selected) and 20% (hover);
rules are white at 8%, 16% and 28%; `t2` to `t4` are one step lighter
(`#B9C1CF`, `#98A2B6`, `#6F798C`); and every glyph carries a black halo
(2.4 px at 60%) so it holds over a bright sky at 30% backdrop.

Tints follow from the tokens and are not chosen separately. A state tag is
its colour at 12% over `bg1` with an edge at 45%: LIVE `#112B1F` / `#186F35`,
IDLE (from `t3`) `#1D2128` / `#434957`, ARMED and HELD `#2A231B` / `#755327`.

### The row shade

A share is the row's own background, from the row's left edge to the share's
length. Its strength is set per colour, by one rule: **as strong as leaves
`t2` text at 4.5:1 on it, and never over 30%** (light theme: 5.5:1, 22%).

| | Dark | Light |
|---|---|---|
| Most job colours | 30% of the colour over the row | 22% (WAR 21%) |
| Held back for legibility | BST 29%, BRD 28%, MNK 27%, SMN 23%, PLD 22%, WHM 20% | none |
| An action or heal under a character, when that switch is on (it is off as installed) | `t1` at 10%, no colour | `t1` at 8% |
| Compare, a run's band | set from the run's colour (the rule is below): 20% for run A and 19% for run B as installed | 16% (not drawn; from the contrast figures, and not reworked for the new colours) |
| Pop-out strip | same rule, up to 34%, over the panel's backdrop | same |
| Hovered row | `t1` at 5% over the whole row, a `line3` rule above and below | same |
| Selected row | `bg3` under the shade, a 2 px amber edge | same |
| A dash on a shaded row | `#949EB2` (halfway from `t3` to `t2`) | `#58606F` |

Measured on the worst colour of each theme: `t1` 8.4:1, `t2` 4.5:1, a dash
3.7:1 (dark); `t1` 11.9:1, `t2` 5.6:1, a dash 4.2:1 (light). Job names beside a
character move from `t3` to `t2` in shaded tables, because `t3` falls to about
3.0:1 on the pale shades. Nothing marks the shade's end: a line there cut
through whichever figure it fell on.

**A run's band has its own rule, because its colour is the user's.** The
band is **as strong as leaves `crit` text at 4.5:1 on it over `bg1`, and never
over 26%**. Red is the darkest text that sits on a band, so everything else
follows: on the installed pair `live` is 8.1:1 or better, `t1` 12.1:1, `t2`
6.5:1 and a dash 5.3:1. The rule gives 20% for the installed blue and 19% for
the installed orange; a paler pick gets a fainter band (white 10%, a yellow
13%) and a dark one stops at the 26% cap. On the opened row (`bg2`) red is
4.2:1 on either band, as it was before this revision.

The shade's edge is exact: it falls where the share ends and crosses a figure
if one is there, as the real pop-out's does. It is not snapped to a column and
does not fade, and no line marks it (a line cut through the figure it fell
on).

**Two switches, and one rule.** Settings has "Shade characters in their
colour" (on as installed) and "Shade actions and heals" (off as installed).
The first governs every top-level row: a character's row in Damage by
character and Healing by character, their heading in Actions and Heals, the
runs' bands in Compare, and the pop-out strip. The second governs the rows
under a character. The rule for both: **a share is drawn exactly once. Where
the row is shaded, the row is the bar and the cell is the figure alone; where
it is not, a small bar sits in the row's own share cell (Damage %, Healing %,
Share) beside the figure, to the same scale.** In Compare with the first
switch off, the Damage cell gets its two small run bars back. Sheet 07 shows
the same rows under all four pairs. The pop-outs follow both switches: a
strip with shading off is still a strip (its 3 px colour edge stays), with
the small bar in its % cell.

**The low accuracy mark.** Accuracy, WS Acc and Pet Acc under a threshold are
marked in red, more strongly the lower they fall. The threshold is a setting,
**90% as installed**; at 60% and under the mark is at its strongest (the 60%
is fixed and is not a setting; confirmed by the owner, see section 5). The
mark is drawn in the live tables only, not in Compare, and not on the Party
line as the sheets stand (see "Still open" in section 5). With 90%, thirteen of the
seventeen rates in the example table are under the line, so the mark is
graded in kind as well as strength, to stay calm:

- **90% down to about 85%:** a 2 px red rule under the figure, nothing else.
- **About 85% down to 75%:** the cell is progressively cut out of the row:
  filled with the pane's own surface, washed red, outlined red. A cut-out and
  not a wash, because a wash of red over a row already shaded red, magenta or
  orange disappears; cut out, it reads the same on a plain row and on any
  shade.
- **75% down to 60%:** the full cut-out, its wash and outline deepening.
- **60% and under:** the strongest (wash 34%, outline 72%). Figures on it
  stay at 9.6:1 or better.

### Colour, light (cheap, so included)

`bg0 #E9EBEF`, `bg1 #FFFFFF`, `bg2 #F4F5F7`, `bg3 #E7EAEF`, `bg4 #DBDFE6`;
`line #ECEEF2`, `line2 #D5D9E0`, `line3 #B4BBC7`; `t1 #14171D`, `t2 #474E5C`,
`t3 #687182`, `t4 #A0A7B4`; `accent #A35F00`, `live #0B7A45`, `crit #C42B3A`;
runs and job colours as `Themes/Light.xaml`. Panels stay dark, as now.

### Type

| Role | Face | Size / weight | Notes |
|---|---|---|---|
| Figure (the five tiles) | Segoe UI Variable Display | 22 / 600 | tabular |
| Page title | Segoe UI Variable Display | 18 / 600 | |
| Compare tile figure | Segoe UI Variable Display | 16 / 600 | tabular |
| Drill-down figure | Segoe UI Variable Display | 15 / 600 | tabular |
| Drill-down title | Segoe UI Variable Text | 14 / 600 | |
| Pane title, section tab | Segoe UI Variable Text | 12.5 / 600 | |
| Cell, button, menu line | Segoe UI Variable Text | 12 / 400; totals and the picked row 600 | tabular |
| Paired cell (A over B) | Segoe UI Variable Text | 11.5 / 400 | tabular |
| Column heading | Segoe UI Variable Text | 11 / 500, `t3` | sentence case |
| Note, job beside a name | Segoe UI Variable Text | 10.5 to 11 / 400, `t3` | |
| Label (tile, column group) | Segoe UI Variable Small | 9.5 / 600, caps, +0.7 tracking, `t3` | |
| File name, time of a hit, key | Cascadia Mono (Consolas behind it) | 10 to 11.5 / 400 | |

Every number is tabular (`Typography.NumeralAlignment="Tabular"`, as now), and
right-aligned in its column. Figures stay in Segoe UI Variable, not a monospace
face: its tabular digits are about 10% narrower than Cascadia's, which is what
lets fourteen columns fit 940 pixels, and it has no slashed zero to add noise
to a column of thousands. The monospace face is kept for things that are read
as strings.

### Measure

- **Grid 4.** Cell padding 8 left and right; pane text starts at 12.
- **Rows:** 24 in a table; 22 under a heading in Actions and Heals; 20 in a
  list of hits and in the pop-out strip; 36 where a cell holds A over B.
  (Today: 32, 30, 30 and 44.)
- **Bars of the window:** title 36, command 40, figures 64, characters 30,
  pane heading 30, column heading 24 (38 where columns are grouped), status 24.
- **Radius:** 4 on controls, 3 on chips, 2 on bars and swatches,
  8 on the window, menus and panels. Nothing is a pill except the state
  tag.
- **Panes:** smallest 320 wide by 132 tall. A divider is a one-pixel rule
  with a 7 px hit area and a 3 by 22 grip at its middle; a heading's grip is
  six dots, 5 by 9, at 8 from the pane's edge.
- **Rules:** one device pixel on a pixel centre. No shadow or gap between
  panes. Shadows only under things that float: menus, tooltips, hover cards.

### Icons

Segoe Fluent Icons, as now, at 10 to 14: caption buttons, the gear, pop out,
dock, lock, play / pause / restart / cancel / tick on the session pair, folder,
warning. Carets and the sort mark are drawn as two-segment strokes so they stay
crisp at 8 px. The app's mark in the title bar is the icon reduced: an amber Z
on an ink tile.

---

## 3. Window chrome and controls

See `08-kit.svg` for each of these at rest, hovered, pressed, focused and
disabled.

- **Title bar (36).** Custom, in `bg0`: the mark, "Zerg", then the sections as
  tabs (Damage, Healing, Compare; the active one in `t1` with a 2 px amber
  underline), the gear, and drawn caption buttons 46 wide. The rest drags the
  window. This takes over the "Section" row of radio buttons, saving a line.
- **Buttons.** `bg3` face, `line2` edge, radius 4, 26 tall. Hover lifts to
  `bg4`; pressed sinks to `bg2`; focus is a 1.5 px amber ring outside the
  edge. A ghost button (Export, Import, Close, Replace) has no face until
  hovered. One filled button per place at most.
- **The session pair** keeps its rule: the only fills that say a state. Start
  is green when idle, amber while armed, plain once running, red as Confirm;
  the second is amber while it holds the clock. Each also carries a glyph, so
  the pair reads in peripheral vision.
- **Toggles** (Include Skillchains, Hide names, Click-through, Group under 5%)
  are a tick box and label in one bordered piece: on is `bg3` with an amber
  tick, off is an empty box. Click-through shows its hot key as a keycap on
  the button instead of only in its tooltip.
- **Segments** share one frame; the picked one is a `bg4` slab inside it.
- **Chips** are rectangles (radius 3): swatch and name. Excluded: dashed edge,
  hollow swatch, name struck through. More than fit become "+5".
- **Combo box.** Not used by Zerg today; drawn for completeness in the same
  language (a button face with a caret; the list is a menu).
- **Slider.** 4 px track, amber fill, a ring thumb.
- **Text box.** A `bg0` well with a `line2` edge; amber edge when focused;
  paths in the monospace face.
- **Scroll bars.** A 6 px `line3` thumb, no track, no arrows; 10 px under the
  pointer. Still drawn over the content, so the existing right margins stay.
- **Tooltip and hover card.** `bg4`, `line3` edge, radius 5, a soft shadow.
  The hover card's heading (the instant) is ruled off from its rows.
- **Menus.** `bg2`, radius 8, 28 px lines, ticks in amber in a fixed column
  (as `TrayMenu` does now), hot keys as keycaps, a hovered line as a `bg4`
  slab inset 4.
- **Pane heading.** A six-dot grip at its left in `t4` (it brightens under
  the pointer, and the cursor becomes the move cursor): the heading is the
  handle the pane is moved by. At its right, a small chevron that folds the
  pane to its heading, then Pop out where the card can float.
- **Divider.** The rule between two panes, with a short `line3` grip at its
  middle. Under the pointer the rule is 3 px of amber at 55% and the cursor is
  the resize cursor; dragged, it is solid amber with both sizes beside it.
- **Layout button.** A small split-rectangle glyph at the end of the command
  bar: Lock layout, Reset this section's layout, Reset all layouts.
- **State tag.** LIVE / ARMED / HELD / IDLE / SAVED, a small outlined pill in
  the state's colour beside the clock. Armed has a ring round its dot.

---

## 4. Screen by screen

### 01 Damage, live

- **Command bar.** The session pair first, Export and Import, then the
  toggles. What the last export did is said at the right end.
- **The five figures** become one 64 px band of ruled cells. Each keeps its
  label, figure and note, and gains a small mark from data Zerg already has:
  the party's damage per 20 seconds beside the total, the running DPS beside
  Party DPS, and everyone's share of the party end to end beside Top DPS. The
  clock is the only coloured figure, with the state tag beside it.
- **Characters** is one 30 px line: count, chips, All, None, and who is out.
- **Damage by character.** The bar chart and the table said the same thing
  twice; they are one table now, and **the bar is the row**: each row is
  shaded in the character's job colour from its left edge to a length that is
  its damage out of the leader's, with all fourteen figures on top. Damage %
  is the number alone; the share is not drawn twice. The eleven narrow
  columns are grouped under WEAPONSKILL, SKILLCHAIN and PET so their headings
  can be one word. A rate under 90% is marked in red, deepening to 60%, so
  the low Accuracy is found without reading (Xatsh's 57.1% WS Acc). The
  owner's row has an amber marker. A **Party** line closes the table under
  the columns it sums, unshaded.
  - *Scaled to the leader, not to 100% of the party.* It is what the pop-out
    does (`Fraction = total / largest total`), and with ten to eighteen
    characters nobody is over about 20%: scaled to the party every bar would
    end inside the first two columns and the differences between rows, which
    are what the bar is for, would be a few pixels. The percentage beside it
    carries the absolute share.
- **Actions.** Same tree, 22 px rows. A character's heading is shaded the
  same way (their damage out of the leader's, in their colour), and its Share
  is the figure alone. An action is not shaded as installed: its share is a
  small grey bar in the Share cell beside the figure, its total out of that
  character's largest action. One scale holds everywhere, shaded or not: *a
  row's share is drawn to its value over the largest of its siblings.* Added:
  a "Min, avg, max" mark per action (a line from least to greatest with a tick
  at the average, scaled to the character's biggest hit).
- **Every pane** has a grip in its heading and every divider a grip at its
  middle: the layout is adjustable (sheet 10). The layout button ends the
  command bar.
- **Cumulative damage** names each line at its end (the chart can already do
  this for panels), so the legend row is gone. The hover card is shown.
- **The drill-down** is a pane to the right of the tables instead of a card
  below them, so picking an action never scrolls the page. Six figures in one
  ruled row; the histogram gains a band for the middle half of the hits and a
  tick at the median; "Every hit" is open by default with a small bar per hit
  for how far it fell from the average, and an amber spark for a crit.
- **Status line.** Dot, file (monospace), events, lines, start.

### 02 Healing, held

The same grid over the heals, with the same back-shade: Healing by character
to the leading healer, a healer's heading in Heals; a heal under a healer has the small bar
in its Share cell, as an action does. Held: the clock, the tag and Resume are amber,
and Export wakes. Cumulative healing is floating, so its pane shrinks to a
46 px dashed stand-in with "Bring back" and the drill-down takes the height.
The status line says how many panels are out and offers Dock all. The Heals
table has no "Min, avg, max" mark, on purpose: there Avg is per cast and Min
and Max are per target, so one line would mix two scales.

### 03 Compare

- The switches (Show, Compare by, Include Skillchains, Swap) share one line
  with the card's title; the two slots sit side by side, each one line of
  facts with its three buttons.
- **Six tiles** in one 72 px band: A over B at the left with the run's badge,
  the change at the right in its tone, the plain difference under it.
- **By character.** Rows are 36 tall. The run is marked once per row, in the
  Job column, instead of a tick ahead of every figure. **The row is shaded
  twice**: A's band across its top half, B's across its bottom half, each to
  that run's damage out of the largest in the table, in the runs' colours (a
  length here says which run, so it is not the job's colour; the swatch keeps
  that). The two bars that sat in the Damage cell are gone; the cell is the
  two figures. By damage type and By target are shaded the same way. Green
  and red changes stay at 4.5:1 or better on either band, whatever colours
  the runs are set to (section 2, "The row shade").
- **The runs are blue and orange as installed** (`#3987E5`, `#E8792B`), the
  usual pair for two series, in place of blue and a vermilion that read as
  the red of a worse change. Both are the user's to set (sheet 06).
- **A row's actions** open in a darker well, indented, with the same columns
  as now. **An action's distribution** opens under it: the paired histogram
  on shared bins with each run's average ruled, and the figures beside it
  with the change split into two columns (the percentage, toned; the plain
  difference, quiet) so each line is 23 px instead of 34.
- **Right column:** both runs on one clock, By damage type, By target.

### 04 Pop-outs

- **Bar, 30 px.** The pair moves to the left edge, then the clock and the
  total; the title is right-aligned and dim. The pair is still a fixed target
  (nothing to its left can grow), and the bar now works down to about 250
  wide. Opacity and Dock appear only while the pointer is over the panel, in
  the title's place, so a resting panel is all figures.
- **Strip.** Unchanged in idea, and now the same idiom as the main window's
  tables: the row is shaded to its share of the leader's. The shade follows
  the same per-colour rule (up to 34% here, over the panel's backdrop) in
  place of today's flat 70%, so pale job colours (WHM white, PLD yellow) no
  longer sit bright behind white text; a solid 3 px edge at the left stands
  in for the swatch. Character headings in the Actions pop-out are shaded
  too.
- **Actions.** The drill-down opens under the picked row as now: six
  figures, the histogram, and Every hit folded.
- **Click-through.** No pair, no tools, a lock; the clock and total stay.
- **Restart asked.** Confirm in red and Cancel, on every bar at once, as
  today. Nothing counts the five seconds down.

### 05 Tray menu

The same lines in the same order, restyled. One addition: a heading line with
the session's state, clock and total. While a restart is being asked about the
menu stays open, as now, with Confirm in red; nothing counts the five seconds
down.

### 06 Settings

Each setting is one ruled row: name and explanation at the left, the control
at the right, in place of five stacked cards. The hot key is drawn as keycaps;
the listening state is shown beside it. The opacity slider gets a small
preview of a strip at that opacity.

Two rows are new, between Draw frequency and Theme:

- **Row shading**, two toggles. "Shade characters in their colour" (ticked;
  "on unless switched off"), with the line "A character's row in Damage by
  character and Healing by character, their heading in Actions and Heals, and
  the two runs in Compare." And "Shade actions and heals" (unticked; "off
  unless switched on"), with "The rows under a character, shaded without a
  colour." The row's explanation says what an unshaded row shows instead and
  that the pop-outs follow both. A four-row preview beside them shows the
  table as currently set.
- **Low accuracy mark**, a slider from 50% to 100%, at 90% ("90% unless
  changed"), with six sample rates under it drawn as they would be marked.

One more is new, after Theme:

- **Compare colours**, a well for each run: the run's badge in its colour,
  the colour's code, a chevron. Opening a well shows a picker under it (a
  square for saturation and brightness, a strip for hue, a field for the
  code) and the band strength that colour comes out at. Any colour can be
  picked. "Use default" puts both back and is off while both are as
  installed. A one-row preview beside the wells shows a Compare row as set,
  with the two strengths under it. The sheet shows A at rest and B open.

### 07 States

Each is a 960-wide piece of the window. Wording is Zerg's own
(`SessionText`, `SessionView`, the tooltips). **View is a tab** that appears
while a parse is open, with the file's name and a close mark, so the section
has a visible place and a way back. A refused export says why in red in the
command bar's note and clears after 8 seconds, as today; in a window too
narrow for the note beside the buttons it wraps to a line of its own, also as
today. Restart asked is Confirm and Cancel in the pair, with no countdown.

The last row is **Row shading, the four settings**: three characters and one
of them opened onto three actions, drawn under each pair of the two switches
(characters shaded and actions not, which is how Zerg is installed; both;
neither; actions only), so the choices can be compared on the same rows.

### 08 Kit

The reference sheet for sections 2 and 3: the colour tokens, the type scale,
the measures, the job colours, and the controls: buttons at rest, hovered,
pressed, focused and disabled; the session pair in each of its six looks;
toggles, segments, chips and state tags; text box, combo box and slider;
scroll bars, tooltip and keycaps; the hover card and a menu; the window
chrome; and the marks used inside a cell. For the panes it shows a divider
at rest, under the pointer and dragged, a heading at rest and under the
pointer, the place a dragged pane will land, and a pane folded to its
heading. The layout button's menu is on sheet 10, not here. Its table rows
(shaded, hovered, selected, and a Compare row shaded twice) show the shading
as installed, characters shaded and actions not, and point to sheet 07 for
the other three settings.

### 09 Light

Screen 01 with the light tokens, to show the system holds, the back-shade and
the cut-outs included. Not tuned further.

### 10 The layout is the user's

**A, a pane being moved.** Cumulative damage has been picked up by its
heading. Its old place stays, dimmed and outlined, until it is put down (Esc
puts it back). The pane under the pointer dims and shows five places: above,
below, left of and right of it, which split that pane's room between the
two, and its middle, which swaps the two panes. The place under the pointer
is amber and says in words what will happen. A thin amber band along each
edge of the section is a sixth kind of place: dropped there, the pane takes
that whole side, full width or full height. What is carried is a small card
with the pane's heading and a sketch of its contents. The status line says
what is happening.

**B, put down, and a divider in the hand.** The same Damage section after
that swap: the chart wide under the table, Actions as a narrow tree (in a
narrow pane it sheds Miss, Min and the "Min, avg, max" mark and keeps Share
with its small bar, as its pop-out already sheds columns), the drill-down
under it. The divider between the columns is being
dragged: solid amber, with both widths in a small readout that follows the
pointer. Sizes change live; there is no ghost line.

**The model, decided.**

- **A tree of splits, nothing else.** A section's body is rows and columns of
  panes. No tabs and no stacking: with four panes, a tab hides a quarter of
  the screen, which is the opposite of the point. No floating inside the
  window either.
- **One way to move each thing.** Inside the window a pane is moved by
  dragging its heading, and only that. Past the window's edge nothing lands:
  a drag does not tear a pane off. Floating over the game is Pop out's job
  and stays a button, because a pop-out is a different form of the card (a
  strip, a compact chart), always on top and never taking the keyboard, and
  not something to fall into by overshooting. A floating panel is moved by
  its own bar, as now, and cannot be dropped back into the window by
  dragging; it returns with Dock, Bring back or Dock all, into the place its
  stand-in has kept. The stand-in is a pane like any other and can be moved,
  which is how the place a card will come back to is chosen.
- **Fold, not hide.** The chevron in a heading folds the pane to its 30 px
  heading where it stands; the panes sharing its column take the room, and
  the same mark opens it. A pane alone in its column folds sideways to a
  30 px rail with its title reading down. There is no hidden state and so no
  list of hidden panes to look for: a pane is always on screen as itself, as
  its heading, or as its stand-in.
- **The drill-down** keeps its rule ("it goes where its table goes"). It is a
  pane with a place of its own; with no action picked it is folded to its
  heading, and while Actions (or Heals) floats it is folded and says so,
  because the panel has it.
- **Smallest pane** 320 by 132: a heading, column headings and three rows. A
  divider stops there. Under 700 wide the window cannot hold two such
  columns, so the panes fall into one column in reading order and the page
  scrolls, as today; the arrangement is kept and returns with the width.
- **Per section, remembered.** Damage, Healing and Compare each keep their
  own arrangement (the Healing table has eight columns to Damage's fourteen,
  so they will want different splits). View borrows Damage's or Healing's,
  whichever side it shows. Compare's four panes (By character or By healer,
  the cumulative chart, By damage type or By heal, By target) share one
  arrangement across its Damage and Healing modes. The bands above the panes
  (figures, characters, Compare's slots and tiles) are fixed. Saved in
  `settings.json` as shares of the room, not pixels.
- **Set back.** Double-click a divider to reset that one split. The layout
  button has Reset for this section and for all, and **Lock layout**, which
  removes the grips and stops every drag once the arrangement is settled.
- **Without a pointer.** A heading's own menu has Fold, Pop out and Move
  (left, right, up, down); a focused divider takes the arrow keys.

---

## 5. What changed, and why

| Now | Mockup | Why |
|---|---|---|
| Fluent cards on Mica, one column, the page scrolls | Flush panes in two columns, each pane scrolls | The stock look is the cards; panes show about three times as much per screen |
| Stock title bar; "Section" row of radio buttons | Custom 36 px bar with sections as tabs | No grey chrome, one line saved, the section is where a title is expected |
| Five tiles, about 110 tall | One 64 px band, each cell with a small mark | Same figures at 60% of the height, with a trend beside them |
| Bar chart, then a table of the same people | One table whose rows are the bars: each row shaded to its share of the leader's | The chart repeated the Damage column; the pop-out strip already drew it this way |
| A fixed order of cards | Panes moved by their headings and sized by their dividers, per section, remembered | The layout that suits a six-person party does not suit an alliance, or a second monitor |
| Fourteen ungrouped headings ("WS Damage", "WS Avg", ...) | Three column groups with one-word headings | Fits 940 px without sideways scrolling; easier to find a group |
| Rows 32 and 30, text 13 | Rows 24 and 22, text 12 | A full alliance (18) fits where 13 rows did |
| Nothing marks a poor rate | A red mark under 90% (a setting): a rule, then a cut-out as it falls | The eye finds the outlier, on any row colour, without thirteen boxes |
| No choice about how a share is drawn | Two switches: characters shaded in their colour (on), actions and heals shaded (off) | Some will want the plain table; the share is drawn once either way |
| Min, Avg, Max as three numbers | The numbers, plus one mark | Spread is a shape before it is a figure |
| Legend above the docked chart | Lines named at their ends | Already how the panel does it; one row saved |
| Drill-down is a card under Actions | A pane beside the tables | Picking an action no longer moves the page |
| Chips with name and job, as pills | Name-only rectangles | Twelve fit one line; the job is in the row below and in the tooltip |
| Click-through's hot key only in a tooltip | On the toggle, as a keycap | The one fact needed before going back to the game |
| Panel bar: title, pair, clock, total, slider, %, Dock | Pair, clock, total, title; tools on hover | Less chrome over the game; works narrower |
| Strip bar at a flat 70% behind the text, with a text shadow | The same bar at a strength set per colour (20% to 34%); halo on all panel text | Legible on every job colour; one idiom with the main window |
| Tray menu has no status | A heading line with state, clock, total | Answers "is it running?" |
| Compare: a tick before every figure, two bars in the Damage cell | The run marked once per row; the row shaded twice, A over B | Hundreds fewer marks; the same share idiom as the live tables |
| Compare rows 44, spread lines 34 | 36 and 23 | Density |
| Settings: five cards | Eight ruled rows, label left, control right: the five settings there are now, plus Row shading, Low accuracy mark and Compare colours | Shorter; the control is beside what explains it |

### Decisions

The owner has been through every proposal. This is where each stands.

**Accepted, as drawn**

1. **Movable, resizable panes** (sheet 10): splits only, no tabs or stacking;
   fold, not hide; per section, remembered, with lock and reset. The whole
   model stands.
2. **Back-shaded rows** replace the bar chart, with the shade's edge exact
   (it crosses a figure where it falls, as the pop-out's does; no snapping, no
   fade).
3. **View is a tab** while a parse is open.
4. **Panel bar:** the pair at the left edge; opacity and Dock only under the
   pointer.
5. **A heading line in the tray menu** with state, clock and total.
6. **Name-only chips**, the job in the tooltip.
7. **A Party line** under the per-character tables. It needs a small core
   addition (party-wide Accuracy, WS and pet figures in the live count).
8. **The amber accent** `#F2A33A`, unchanged.

**Rejected, and removed from the sheets**

9. **A failed export that stays until dismissed.** The note clears after 8
   seconds, as today.
10. **A visible five-second line under Confirm.** Confirm and Cancel are
    restyled and nothing more.

**Changed**

11. **The low accuracy mark starts at 90%** (it was 85%) **and the threshold
    is a setting.** The mark was reweighted for it (a rule first, a cut-out
    as the rate falls); the threshold itself was not touched.
12. **Shading is governed by two settings:** characters in their colour (on
    as installed) and actions and heals without a colour (off as installed).
    An unshaded row carries its share as a small bar in its own cell. The
    pop-outs follow both.

**Confirmed afterwards** (first drawn as the designer's assumptions, then
accepted by the owner)

13. **60% stays the fixed point at which the mark is strongest**, and is not
    a setting. If the threshold is set below about 65% the grading has little
    room; an implementation should keep the strongest point at least 15
    points under the threshold.
14. **The mark applies to Accuracy, WS Acc and Pet Acc** in the live tables,
    and not to Compare's accuracy cells, which already carry a toned change.
15. **With "Shade characters in their colour" off, a pop-out strip keeps its
    3 px colour edge** and shows the small bar in its % cell.

**Palette revision** (decided 2026-10-05 from the two proposal sheets; dark
theme only, the light theme is left as it was by the owner's choice)

16. **`crit` is `#FF5063`** (it was `#F26D78`, which read as pink and sat on
    RDM's colour). It is darker, so it is 5.9:1 on `bg1` where the old red
    was 6.5:1, and a run's band is fainter to keep it readable.
17. **`live` is `#22E05A`**, a signal green (it was `#3DD68C`). The owner
    chose it over the emerald the review recommended, knowing the cost: it is
    nearer THF's and RNG's greens, and nearer the amber of HELD for a viewer
    with deuteranopia, so LIVE against HELD rests on the tag's word and mark.
18. **`t3` is `#838DA5` and `t4` is `#5E677A`** (they were `#78829A` and
    `#515A6B`), and the 9.5 px captions that were drawn in `t4` ("per 20 s",
    "running", "party share") are `t3`.
19. **The runs are blue `#3987E5` and orange `#E8792B` as installed, and each
    is a setting with a free colour picker.** The band's strength is worked
    out from the colour (section 2).

**Assumed by the palette revision, for the owner to confirm**

- **A run's band is never over 26%**, the strength it had before the
  revision. Only "keep red readable" was decided; the cap is an assumption,
  so that a dark pick does not flood the row.
- **Nothing stops the two runs being set alike, or to a colour that reads as
  better or worse** (a green, a red). A free picker was the choice; the
  preview in Settings is the only guard.
- **A badge's letter is `#101010` or `t1`, whichever is clearer on the picked
  colour.** On the installed pair it is `#101010` (5.2:1 and 6.5:1).
- **One pair of run colours serves both themes once it is set**, with the
  strength worked out per theme; until then each theme keeps its installed
  pair. The light pair was not reworked.
- **The colours stay with the slot** when Swap A ↔ B is used.

**What the decisions add to what Zerg remembers** (all in `settings.json`,
beside what is there now):

- the two run colours (blue and orange as installed);
- the low accuracy threshold (90% as installed);
- "Shade characters in their colour" (on as installed);
- "Shade actions and heals" (off as installed);
- each section's arrangement of panes, as shares of the room, with which
  panes are folded (Damage, Healing and Compare each have their own).

**Described only, by the owner's choice** (not drawn, and not to be):

- **Light Compare.** The run bands there work out at 16%; at that strength
  green "better" text is about 4.4:1, just under the 4.5:1 the rest of the
  design holds to, so it needs tuning when it is built.
- **Compare with "Shade characters in their colour" off** (the two small run
  bars back in the Damage cell).
- A rearranged Healing or Compare section.
- The locked layout.
- The sideways rail a pane folds to when alone in its column.

The script that generated these sheets is not in the repository, also by the
owner's choice.

**Still open**

- **The Party line and the low accuracy mark.** The sheets draw the Party
  line's rates without the mark (85.2%, 86.0% and 82.4% in the example are
  all under 90%). Whether the party's own rates should be marked was not
  asked and not decided.
- **Whether Lock layout is remembered** between runs of Zerg is not stated
  anywhere above.
- Everything in section 7, which is what could not be learned from the
  source.

**Kept on purpose:** every word of Zerg's vocabulary and every tooltip quoted;
job colours and the shade for a second character on a job; a name always
beside a colour; green counting / amber not; the pair's meanings and order;
nobody at zero in the table; a dash for "nothing to measure"; B minus A with
the sign in the text; no overcure; panels always dark; the status line.

---

## 6. Feasibility in WPF

S is a style, template or binding (hours). M is a day or two. L is more.

| Element | How | Size |
|---|---|---|
| Tokens, dark and light | Two brush dictionaries swapped by `AppTheme`, as `Themes/Dark.xaml` and `Light.xaml` are now. Stop using the Fluent `*FillColor*Brush` keys | S |
| Own control templates | Button, ToggleButton, RadioButton (segment), Slider, ScrollBar, ToolTip, TextBox, Expander, ContextMenu, MenuItem. Cleanest with `ThemeMode="None"` so nothing Fluent shows through; Mica goes, and solid surfaces are cheaper to draw. `TrayMenu` is a subclass and must still name its style | M to L |
| Custom title bar | `WindowChrome` (caption height 36, `IsHitTestVisibleInChrome` on the tabs and buttons). Keep `SingleBorderWindow` for the Windows 11 corners and shadow. Needs the usual inset when maximized, and a check that `WindowPlacement` still restores correctly. The Snap Layouts flyout on the maximize button needs an `HTMAXBUTTON` hit-test hook; without it snapping by drag and Win+arrows still work | M |
| Two-column pane layout | The arrangement as installed: a `Grid` in place of the `StackPanel`; each pane its own `ScrollViewer`. Once panes can be moved this is the split-tree host below, showing its default tree. The single-column fallback is at 700 wide (see "Narrow-window fallback"); an earlier figure of 1100 belonged to the fixed layout and no longer applies | M |
| Denser rows, grouped headings | `v:Cells` width strings and row heights; a second `Cells` row for the groups | S |
| Back-shaded rows | Exactly what `BarsCard.xaml`'s strip does today: a `Rectangle` stretched across the row behind its `v:Cells`, `v:Grow.Share="{Binding Fraction}"`, `Fraction` already being total over the largest total. Move that into the table's row template; add `Fraction` to `ActorRow`, `HealerRow` and the heading rows. No `DropShadowEffect` is needed at these strengths | S |
| Shade strength per colour | One function beside `Shades` (lower the alpha until `t2` clears 4.5:1 over the surface), worked out once per colour and theme and handed over as the brush, as `PanelColorOf` is | S |
| Compare colours | Two colours in `settings.json`. `RunABrush` and `RunBBrush` stop being aliases of `Series1` and `Series2` (which are also the fallback job slots) and are set at start and on change. The band strength is the same kind of function as the shade's, with `crit` in place of `t2` and a 26% cap. The picker is a small `Popup` of Zerg's own (a square, a strip, a text box): WPF has no stock colour picker | M |
| Resizing panes | `Grid` + `GridSplitter` with a restyled template (the rule, the grip, a 7 px hit area); star sizes written to `settings.json` on `DragCompleted`; `MinWidth`/`MinHeight` on the panes; arrow keys come free; a double-click handler resets | S to M |
| Moving panes (docking) | Real work. Recommended: **a small split-tree control of Zerg's own**, not a docking library. A layout model (`Split { Orientation, Ratio, A, B }` or `Leaf { PaneKey, Folded }`, in `Zerg.Core` so it is testable: insert beside, swap, remove-and-heal, clamp to minimums, serialise) and a host that builds nested `Grid`s with `GridSplitter`s from it and re-parents the existing card instances. The drag is a mouse capture on the heading, an adorner for the carried card, and a hit test of the pointer against each pane's five zones and the section's edges. AvalonDock (the usual library) would also do it, but it is built around tabs, auto-hide and its own floating windows: all three were decided against, its floating windows activate and would have to be kept away from Zerg's no-activate panels, and its chrome would need retemplating end to end to match. Suppressing most of a library costs more than the two or three hundred lines this needs. Watch for: a re-parented card rebuilds its visuals (charts repaint, lists remake rows, UI Automation peers go stale: the `AppTheme.RereadRows` trap) | L |
| Fold, lock, reset, the layout menu | State on the leaf; a button and a menu; the drill-down's automatic fold is a binding to `DrillOpen` | S to M |
| Narrow-window fallback | Below 700 wide swap the tree host for today's `StackPanel` order | S |
| Low accuracy mark | A `Border` behind the figure (surface brush, red brush, red stroke) and a 2 px rule under it, their opacities from one converter given the rate and the threshold. The threshold is a new `lowAccuracy` in `settings.json` with a slider on the Settings page, as `drawFrequency` has | S |
| The two shading switches | Two booleans in `settings.json` and on the view model; the row templates show the back `Rectangle` or the cell bar by a trigger; panels inherit them through the shared `DataContext`. Compare's `RunBars` stays in the tree for when the first is off | S |
| "Min, avg, max" mark | A small element with `OnRender`, three rectangles. Rows update in place, so it redraws only on a count | S to M |
| Party line | Needs the party's Accuracy, WS and pet figures from the live count; Compare's `Measurement` already works them out. A small core change with its own test and a line in `RULES.md` | S to M |
| Marks in the figures band | A tiny `Chart` subclass fed from `Counting.Cumulative`; redraw on a count, never on the beat | M |
| Share-of-party bar | An `ItemsControl` of star-sized columns | S |
| End labels on the docked chart | `LineChart.EndLabels` exists; set it and drop the legend | S |
| Hover card, histogram band and median tick | Paint code in `Chart`, `HistogramChart`; Q1, Q3 and the median are already in `Distribution` | S |
| Drill-down as a pane | Layout only; `DrillCard` is reused. The "vs avg" bar is two rectangles per (virtualized) row | S |
| Sections as tabs; View as a tab | Restyle the radio buttons; bind the View tab to `HasParse`. An information-architecture decision more than work | S |
| Name-only chips, "+N" | Template; the overflow needs a small panel or a wrap as now | S |
| Status line: panels out, Dock all | A count on `PanelSet` beside `AnyOut` | S |
| Panel bar reorder, tools on hover | `PanelWindow.xaml`; tools at opacity 0 and not hit-testable at rest | S |
| Halo on panel text | Today's `DropShadowEffect` on the strip, applied to the panel's body. It costs a bitmap pass per panel: measure it at the draw frequency before keeping it | S to M |
| Tray menu heading | A non-interactive first item bound to the same view model | S |
| Compare restyle | `RunPair`, `ChangeText` and the row templates; `RunBars` becomes two half-height rectangles behind the row, sized from the `BarPair` lengths `CompareSheet` already gives; no change to `CompareSheet` except, optionally, exposing a spread line's two parts separately (it already has `Main` and `Sub`) | M |
| Settings rows, keycaps, preview | XAML only | S |
| Cascadia Mono | Installed with Windows 11's Terminal, but it is a variable font and WPF sees variable faces unreliably (the Segoe UI Variable note in `App.xaml`). Bundle the static Regular (SIL OFL) or use Consolas | S |

Nothing needs a blur, acrylic, a shader or per-frame animation. The only
effects are shadows under menus and tooltips (popups already have them) and
the panel halo.

---

## 6a. What the real pop-out does (read from source)

`Views/BarsCard.xaml`, the floating form: each character is a 20 px `Grid`
row with a 1 px gap. Behind its cells is one `Rectangle`, stretched across
the whole row, filled with the character's panel colour at `Opacity="0.7"`,
corners 2, and scaled along X by `v:Grow.Share="{Binding Fraction}"` (an
eased `ScaleTransform`, 250 ms, so no layout runs). `Fraction` is set in
`MainViewModel.Damage.cs` as `a.Total / top`, where `top` is the largest
total on the strip: the leader's row is full width. The cells (name, job,
Damage, %, Acc%) sit on top inside one `DropShadowEffect` (depth 0, blur 3,
black at 75%) that keeps pale text legible on bright fills, and the file
owner's row has a 2 px accent rectangle at its left. `HealBarsCard.xaml` is
the same with Healing, % and Casts.

The mockup takes that idiom as it is (a fill behind the whole row, scaled to
the leader, figures on top) and changes one thing: the strength. At 70% a
white or yellow fill leaves `t2` figures near 1.5:1, which the strip survives
with three bold figures and a shadow, and a fourteen-column table would not.
A shadow on every row of the main tables would also be a bitmap pass per row
each time DPS is rewritten.

## 7. Not determined from source

- **How the current app looks on screen.** It was read, not run. "Stock
  Fluent cards" is from the XAML and theme keys.
- **Chart details** (axis styling, the hover card's exact form) are from the
  paint code as read; the mockup's charts are a restyle in that spirit.
- **Event file name.** `CLAUDE.md` says `<Char>-<YYYYMMDDHHMMSS>.jsonl`,
  `RULES.md` says `<Character>_<date>.jsonl`. The mockups use the first.
- **Whether the live count has party-wide Accuracy, WS and pet figures.**
  Compare's `Measurement` does; the Party line assumes the live one can.
- **The line count on the status line** is invented; the event count is the
  simulation's.
- **One error message is invented:** "this file is not a parse exported from
  Zerg" on a Compare slot. The real `ParseImportException` texts were not
  read. "Could not open: ..." is the app's own prefix.
- **Pet moves** are named ("Fluffikins: Big Scissors", "Garuda: Predator
  Claws"). `RULES.md` lists `#<id>` names for these as a known gap.
- **Whether characters are listed before a session starts.** The idle and
  armed pieces show chips; the app may show none.
- **Whether the tray icon has a tooltip.** None is drawn.

---

## 8. How these were made

Static drawings only; no application code is in this folder. The SVGs were
written by a throwaway script kept outside the repository, so that the layout
grid is exact and the figures on every sheet come from one simulated
fight. Each was rendered in a browser engine (headless Edge) and checked at
100% for overlap, clipping, alignment and contrast, with closer crops of the
shaded tables, the Settings rows and the layout sheet (the contrast figures
in section 2 are computed, not judged by eye); the PNGs are those renders.

Three revisions: the first design; then back-shaded rows and movable panes;
then the owner's decisions (two proposals removed, the 90% threshold, the two
shading switches).

The palette revision (decisions 16 to 19) was not made by that script. The
new values were written straight into the nine dark SVGs, the Compare colours
row was drawn into sheet 06 by hand, and the nine PNGs were rendered again.
Sheet 09 and its PNG were not touched. If the script is run again it will
put the old colours back unless its tokens are changed to those in section 2.
