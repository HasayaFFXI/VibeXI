# Zerg — UX audit

Audit date: 2026-10-05. Scope: every user-facing part of the native WPF app in
`apps/zerg`. Audited from source only; the app was not run (see
[Method and limits](#method-and-limits)).

How to read this document:

- File references are relative to `apps/zerg/` and given as `path:line`.
- **Verified** means the behaviour follows directly from the XAML or C# cited.
  **Inferred** means it is my reading of how that code will look or feel at
  runtime (sizes, contrast, what fits on screen) and should be checked in the
  running app before acting on it.
- Severity: Critical / High / Medium / Low. Effort: S (hours), M (a day or
  two), L (several days or a design change).
- Several behaviours flagged here are deliberate and documented in the code
  comments or `RULES.md`. Where that is so I say it, and recommend the smallest
  change that keeps the rule while removing the cost to the player.

---

## 1. Executive summary

Zerg is a careful, coherent app. Its states are named, coloured and explained
consistently; almost every control has a tooltip that says what it does *and*
what state it is in; errors stay beside the thing that failed; destructive
folder changes ask first; and the floating panels are properly engineered for
play (they never take the keyboard, they are see-through per pixel, and
click-through is one switch with a global hot key). For a first release it is
unusually finished.

The weaknesses are not in the screens themselves but at the edges of the
workflow, where the player's attention is on the game and not on Zerg:

**Strengths**

- One session model (idle / armed / live / held) drawn the same way on the
  main window, every panel bar and the tray menu (`src/Zerg.Core/SessionView.cs:28-70`).
- "Start arms, the first hit starts the clock" removes the need to time a
  click, and the empty states say so (`src/Zerg.Core/SessionView.cs:86-91`).
- Figures are tabular and rows are updated in place, so nothing jitters or
  loses scroll position while it updates (`src/Zerg/App.xaml:39-44`, `src/Zerg/Rows.cs:224-250`).
- Explanations live where the question arises: every column heading of the
  per-character table says how it is counted (`src/Zerg/Views/BarsCard.xaml:108-130`).
- Panels: no-activate, hand-rolled drag so the game keeps the keyboard, places
  and opacity remembered, reopened at the next start, click-through never
  restored at start so nobody is locked out (`src/Zerg/Panels.cs:54-57`).
- Good recovery paths: a second launch brings the hidden window forward
  (`src/Zerg/App.xaml.cs:62-66`); a hot key Windows will not give is never
  saved (`src/Zerg/MainViewModel.Settings.cs:234-246`); a bad settings file is
  kept aside (`src/Zerg/Settings.cs:120-126`).
- Screen-reader names are set on nearly every control, and status notes use
  polite live regions.

**The changes that would help most**

1. Let the session be driven from the keyboard inside the game (global hot
   keys for Start/Restart and Pause/Resume). Today the only global key is
   click-through, and the panel's Start and Pause buttons disappear while
   click-through is on.
2. Stop losing sessions silently: Restart, closing the window, and a new event
   file each discard an unsaved session with no confirmation, no undo and (for
   the file switch) no message.
3. Give first launch a real empty state. With no addon connected the body
   says "Press Start", and the only guidance is one 12-px line at the bottom.
4. Say on a floating panel when it is showing a saved parse and not the live
   session.
5. Show whether data is actually arriving (how old the event file is), so
   "armed and waiting" can be told from "the addon is not running".

No finding is rated Critical: nothing blocks the core task or corrupts data on
disk. The High findings are all about a session being lost or misread without
the player being told.

---

## 2. Top recommendations, prioritised

| # | Recommendation | Severity | Effort | Rationale |
|---|---|---|---|---|
| 1 | Global hot keys for Start/Restart and Pause/Resume, set on the Settings page with the existing chord recorder | High | M | The session cannot be controlled from inside the game without the mouse, and not at all from a click-through panel |
| 2 | Protect an unsaved session: undo or confirm on Restart, confirm on window close, and auto-keep the outgoing session | High | S to M | One click on the largest button (which sits beside Pause) or on the window's X discards the fight |
| 3 | First-run / no-addon empty state in the body, with the load command and the folder being watched | High | S | The body tells a new user to press Start when the real first step is loading the addon |
| 4 | Keep an armed session armed across an event-file switch, and tell the player whenever a file switch ends a session | High | S | Arming before logging in on a new day is silently undone; a running session is dropped with only a log line |
| 5 | Mark floating panels while the View section is on screen (or keep panels on the live session) | High | S (badge) / L (decouple) | The overlay over the game silently shows an old parse's numbers |
| 6 | Show data freshness on the status line ("last event 3 s ago" / "file last written yesterday") | Medium | S | Nothing distinguishes a live addon from a stale file |
| 7 | Offer "pause at last hit" or an optional auto-pause after N quiet seconds | Medium | M | DPS is only right if Pause is clicked the moment the fight ends, while the player is busy |
| 8 | Let Export work on a running session by snapshotting it (as Compare's "Use current" already does) | Medium | S | Removes a forced Pause-then-Export two-step and a disabled button |
| 9 | Make View a visible section and rename Import; reduce the three different Damage / Healing switches to one mental model | Medium | S to M | View is a section with no tab, entered through a button whose label does not describe what it does the second time |
| 10 | Tell the player the first time Zerg hides in the tray; add a "Minimize to tray" setting | Medium | S | On Windows 11 the icon starts in the overflow, so the app appears to vanish |
| 11 | Surface a failed hot key outside the Settings page | Medium | S | A player can switch click-through on and find the key that switches it off does nothing |
| 12 | Panel legibility: a text halo on every floating form and a panel text-size setting | Medium | M | Panel text is 10 to 13 px, fixed, over arbitrary game imagery at as little as 15% backdrop |
| 13 | One vocabulary for panels (panel / pop out / dock) and for paused (not "held") | Low | S | Six words are in use for two ideas |
| 14 | Draw frequency: rename, add presets, state the cost, reconsider the default | Low | S | The setting is described in engine terms and defaults to a costly value |
| 15 | In-app keyboard shortcuts and tab order; Esc closes Settings and the drill-down | Low | S | There are no key bindings or access keys anywhere in the app |
| 16 | Click-to-sort on the per-character tables | Low | M | The table can only be read in total order |
| 17 | Failure notes should not dismiss themselves; success notes should offer "Show in folder" | Low | S | An export error disappears after 8 seconds |
| 18 | About line (version, log folder link) on the Settings page | Low | S | The version is shown nowhere in the UI |

---

## 3. Section by section

### 3.1 Main window shell: command bar, section switch, toggles

**What it is.** The frame every section sits in: Start and the second session
button, Export…, Import…, a transient note, the Settings gear
(`src/Zerg/MainWindow.xaml:94-149`); a "Section" row with Damage | Healing |
Compare and a "Toggles" group (Include Skillchains, Hide names, Click-through)
(`:157-192`); the body; the status line (`:384-393`).

**Works well**

- The two session buttons are the largest controls and the only ones whose
  fill carries state (`src/Zerg/App.xaml:183-241`).
- Disabled buttons explain themselves (`ToolTipService.ShowOnDisabled`, e.g.
  `src/Zerg/MainWindow.xaml:122`; `src/Zerg/MainViewModel.Parse.cs:184-186`).
- The bar wraps in pieces so a narrow window never separates Start from Pause
  (`src/Zerg/MainWindow.xaml:105-107`); tiles reflow 5 / 3 / 2
  (`src/Zerg/MainWindow.xaml.cs:77-78`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **Restart discards the session with one click.** Start becomes "Restart" once armed or running; pressing it drops every row read so far. No confirmation, no undo. Verified. | `src/Zerg.Core/SessionView.cs:48-51`, `src/Zerg/MainViewModel.cs:247-257`, `src/Zerg.Core/Tracker.cs:156-162` | Restart is the biggest button and sits 6 px from Pause (4 px on a panel bar, `src/Zerg/PanelWindow.xaml:86-91`). Reaching for Pause at the end of a fight and hitting Restart loses the fight. Export is only possible *after* Pause, so there is no earlier chance to save. |
| Medium | **Export is disabled while the clock runs.** The tooltip explains ("Pause first — a parse is exported once its clock has stopped"). Verified. | `src/Zerg/MainViewModel.Parse.cs:180-187`, `src/Zerg.Core/Tracker.cs:205` | A two-step the app could do itself. `MainViewModel.Current()` already freezes a running session into export text for Compare (`src/Zerg/MainViewModel.cs:489-495`). |
| Medium | **Import… changes meaning but not its label.** With a parse left open in View, Import… opens no dialog; it navigates back to that parse. Only the tooltip says so. Verified. | `src/Zerg/MainViewModel.Parse.cs:96-105`, `src/Zerg/MainWindow.xaml:127-129` | An ellipsis promises a dialog. To open a *different* file the user must click Import…, land in View, then find Replace…. |
| Medium | **View is a section with no segment.** In View (and in Settings) none of Damage / Healing / Compare is selected, yet the row is still on screen. Verified. | `src/Zerg/MainWindow.xaml:159-170`, `src/Zerg/EqualsConverter.cs:13-17`, `src/Zerg/MainViewModel.cs:63-72` | "Where am I, and how do I get back?" has no visible answer; clicking Damage both leaves View and changes what the numbers mean (saved parse to live session). |
| Low | **Export… is hidden, not disabled, outside Damage/Healing/View**, so Import… shifts left when entering Compare or Settings. Verified. | `src/Zerg/MainWindow.xaml:121-124` | A fixed target moves. |
| Low | **"Section" and "Toggles" are implementation words** used as visible labels. Verified. | `src/Zerg/MainWindow.xaml:158`, `:177` | "Toggles" names the control type, not what the group is for. |
| Low | **Click-through sits among data filters** though it acts only on floating panels, and its hot key is only in its tooltip. Verified. | `src/Zerg/MainWindow.xaml:188-190`, `src/Zerg/Panels.cs:216-223` | With no panel out it does nothing visible; the one fact a player needs before going back to the game (which keys undo this) is hidden behind a hover. |
| Low | **The gear's tooltip is out of date**: it lists the folder, hot key and theme, not opacity or draw frequency. Verified. | `src/Zerg/MainWindow.xaml:100` | Opacity is the setting most likely to be looked for. |
| Low | **Tab order starts at the gear** (declared first so it can dock right), then jumps left to Start. Verified. | `src/Zerg/MainWindow.xaml:98-103` before `:108` | Keyboard users begin at the least-used control. |

**Recommendations**

- Restart: after a Restart that discarded a started session, show a note in
  the `ParseNote` slot: `Restarted. Undo` (a link/button, 15 s), keeping the
  previous `Reader` and `Session` until the new session latches. A modal
  dialog is wrong here because Restart is also pressed from a panel over the
  game. If undo is too much, at least auto-save the outgoing session (see 5.6).
- Export: enable whenever `Session.StartedAt != null`. If running, export the
  frozen copy `Current()` produces; tooltip "Save this parse as it stands
  now. The session keeps running."
- Section row: add a fourth segment, **View**, always enabled; with no parse
  open it shows the existing drop card (`src/Zerg/Views/ViewCard.xaml:32-48`).
  Rename the command-bar button to **Open parse…** and make it always open
  the dialog. Keep the gear as it is.
- Keep Export… in place and disabled outside parse sections, with tooltip
  "Export is for the Damage, Healing and View sections".
- Labels: "Section" to **Show**; drop the "Toggles" caption or use
  **Options**. Label the toggle **Click-through (Ctrl+Alt+Z)** from
  `Panels.HotKey`, and move it next to a panels group (see 3.9), or disable it
  with a tooltip while `Panels.AnyOut` is false.
- Gear tooltip: "Settings: events folder, hot keys, panel opacity, refresh
  rate, theme".
- Set `TabIndex` (or `KeyboardNavigation.TabIndex`) so Start is first and the
  gear last on the bar.

### 3.2 Character filter (chips)

**What it is.** "Characters 5/6", a Hide/Show button, All, None, a hint that
names who is excluded, and a wrapping list of toggle chips with colour, name
and job (`src/Zerg/MainWindow.xaml:197-238`, `src/Zerg/MainViewModel.cs:558-585`).

**Works well.** The count and the "1 excluded: Name" hint stay visible when
the list is folded, so an exclusion is never invisible. Chips keep their
colour identity; each has a tooltip saying what a click will do. The list is
capped at 92 px and scrolls. Exclusions persist on purpose and say so in code
(`src/Zerg/Settings.cs:36-38`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Low | **"Hide" / "Show" next to "All" / "None"** reads as a third filter action ("hide all?"). It folds the chip list. Verified. | `src/Zerg/MainWindow.xaml:201-207`, `src/Zerg/MainViewModel.cs:112-113` | Ambiguous beside a toggle literally called "Hide names" one row up. |
| Low | **An excluded chip is shown only by fading to 45%.** Verified; contrast inferred. | `src/Zerg/App.xaml:273-276` | A faded pill is easy to miss in a list of eighteen; there is no strike-through or icon. The text hint compensates. |
| Low | **A long-forgotten exclusion comes back silently** when that character next appears; All and None act only on who is listed now. Verified, by design. | `src/Zerg/MainViewModel.cs:327-334` | A character excluded weeks ago is excluded today. The hint does show it, in secondary 12-px text. |

**Recommendations**

- Replace the Hide/Show button with a chevron on the label itself:
  `▾ Characters 5/6` (a `ToggleButton`, automation name unchanged).
- Excluded chip: keep the fade and add a strike-through on the name, or an
  "off" glyph, so state is not carried by opacity alone.
- When the hint is non-empty, draw it in the caution colour, not secondary
  grey: an active exclusion changes every total on screen.

### 3.3 Status line and session state

**What it is.** A dot (grey idle, green live, amber armed/held/waiting, red
error; armed also pulses), the file name, then counts and the session state
(`src/Zerg/MainWindow.xaml:25-79`, `:384-393`; `src/Zerg/MainViewModel.cs:438-480`).

**Works well.** State is always in words as well as colour. The armed pulse
is a good answer to "is it stuck?". The no-file and unreadable-file cases have
their own text (`src/Zerg/MainViewModel.cs:452-467`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **A new event file ends the session without telling anyone.** When a newer `.jsonl` appears (a new day after a relog or addon reload, another character) the tracker is reset to idle. A running session's rows are gone; an *armed* session is disarmed. The only trace is the file name changing on the status line and a log line. Verified in Zerg; exactly when the addon starts a new file I could not fully establish (its comment says the file "rolls over at midnight", `addons/VibeXI/vibexi.lua:695-700`; the emitter picks the date when it opens the file, `addons/VibeXI/vx_emit.lua:214`). | `src/Zerg/MainViewModel.cs:218-228`, `src/Zerg.Core/Tracker.cs:125-133`, `src/Zerg.Core/EventTail.cs:45-49` | The likeliest case is the ordinary evening routine: launch Zerg, press Start ("pressing early costs nothing"), then log in. Yesterday's file was being followed; today's appears; the arming is dropped; the fight is not recorded and the charts say "Press Start to begin measuring". The same happens if Start is pressed before any file exists. |
| Medium | **Nothing shows whether data is arriving.** The line prints events and lines read, but not when the file was last written, although every poll carries it (`TailUpdate.Modified`). A file from yesterday and a file being written now both show a grey dot. Verified. | `src/Zerg.Core/EventTail.cs:11-14`, `src/Zerg/MainViewModel.cs:469-479` | "Armed, waiting for the first hit" cannot be told from "VibeXI is not loaded". |
| Low | **"events" and "lines" are parser internals.** Verified. | `src/Zerg/MainViewModel.cs:478-479` | Useful to a developer, noise to a player. |
| Low | **"held" and "paused" are both used** for the same state. Verified. | `src/Zerg.Core/SessionView.cs:90` ("Paused — nothing is being counted"), `:103` ("held — nothing counting"), `:113` ("· held"); button says Pause / Resume | Two words invite the question whether they differ. |
| Low | **The armed pulse ignores the Windows "animation effects" setting** that the bars respect. Verified. | `src/Zerg/MainWindow.xaml:64-76` vs `src/Zerg/Views/Grow.cs:33` | Inconsistent with the app's own reduced-motion handling. |

**Recommendations**

- In `OnUpdated`, when `fresh` is true and the session was only armed, re-arm
  after `live.Follow` (nothing was measured, so nothing can be mis-dated).
  When a started session is dropped, keep a persistent note until dismissed:
  `New event file (Hasaya_2026.10.05.jsonl). The previous session ended.`
  and, with 5.6, `It was saved to Recent.`
- Status line wording: `Hasaya · receiving · last event 2 s ago · armed` and,
  when the file has not been written for more than a minute or two,
  `Hasaya_2026.10.04.jsonl · last written yesterday 23:10 · is VibeXI loaded?`
  with the dot amber. Keep the event and line counts in the tooltip.
- Use "paused" everywhere the player reads it; keep `Held` as the enum name.
- Skip the pulse storyboard when `SystemParameters.ClientAreaAnimation` is
  false (a steady ring is enough).

### 3.4 Damage section

**What it is.** Five tiles (Total damage, Elapsed, Party DPS, Top DPS, Biggest
hit; `src/Zerg/MainWindow.xaml:251-294`), then four cards: Cumulative damage
(`src/Zerg/Views/LineCard.xaml`), Damage by character (bar chart plus a
14-column table, `src/Zerg/Views/BarsCard.xaml`), Actions (folded per
character, `src/Zerg/Views/ActionsCard.xaml`) and the Drill-down that appears
when an action is picked (`src/Zerg/Views/DrillCard.xaml`).

**Works well**

- The clock is a hero figure and takes the state colour; the note under the
  total says the state in words (`src/Zerg.Core/SessionView.cs:98-104`).
- Dashes for "nothing to measure" instead of misleading zeroes
  (`src/Zerg/MainViewModel.Damage.cs:216-219`).
- Picking an action scrolls the drill-down into view and marks the row
  (`src/Zerg/MainWindow.xaml.cs:66`, `src/Zerg/Views/ActionsCard.xaml:57-59`).
- "Group under 5%" lives on the chart it changes, and its tooltip says who is
  in the group (`src/Zerg/Views/LineCard.xaml:26-28`, `:47`).
- Inner scroll areas hand the wheel to the page at their ends
  (`src/Zerg/Views/WheelChain.cs`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Medium | **The elapsed clock, and so every DPS, depends on Pause being clicked at the right moment.** There is no auto-pause and no way to trim a session back to its last hit. Verified (no such code exists in `src`). | `src/Zerg.Core/Session.cs:111-117`, `src/Zerg/MainViewModel.cs:406-436` | The moment a fight ends is when the player is least able to click. Every second of delay dilutes DPS, and it cannot be corrected afterwards. |
| Low | **The per-character table is likely below the fold at the default window size.** Tiles, then a 320-px chart card, come first. Inferred from the fixed heights. | `src/Zerg/MainWindow.xaml:10` (900 high), `src/Zerg/Views/LineCard.xaml:65` | The ranking table is what most players open a parser for; the first screen is a chart that needs minutes of data to say anything. |
| Low | **Tables cannot be sorted.** Headings are plain text; rows come largest total first. Verified. | `src/Zerg/Views/BarsCard.xaml:104-131` | "Who has the best accuracy / WS average" means reading a column by eye. |
| Low | **The Actions table has no "expand all / collapse all"**, opens fully folded on every run, and each open row is a tab stop. Verified. | `src/Zerg/MainViewModel.Damage.cs:225-239`, `src/Zerg/Views/ActionsCard.xaml:13-16` | A solo or small-party player always has to open their own heading first. |
| Low | **The crit mark in the hit list (`✦`) has no legend.** Verified. | `src/Zerg/MainViewModel.Damage.cs:374` | A symbol the reader must guess. |
| Low | **With the drill-down floating and no action picked, the main window shows no stand-in for it** (the whole block is collapsed), so nothing in the window says that panel is out. Verified. | `src/Zerg/MainWindow.xaml:306-309` | Minor: the tray menu is then the only place that lists it. |

**Recommendations**

- Add **Pause at last hit** to the second button's behaviour: when Pause is
  pressed more than about 5 s after the last counted event, set `PausedAt`
  to that event's time and note `Paused at the last hit, 0:14 ago`.
  Optionally a Settings switch: "Pause automatically after [20] seconds with
  no damage" (off by default, so the documented "nothing happens unasked"
  rule holds).
- Let cards be reordered or collapsed (a chevron in each card heading, state
  saved), or simply offer "Table first" as a layout option. At minimum check
  the default-size first screen in the running app.
- Make numeric headings of the per-character table click-to-sort with a
  small arrow; remember the choice per table.
- Open the owner's heading by default in Actions; add `Expand all` /
  `Collapse all` small buttons in the card heading.
- Add "✦ critical hit" to the drill-down caption when `d.Crits != 0`.

### 3.5 Healing section

**What it is.** The Damage section's layout over heals: five tiles
(`src/Zerg/MainWindow.xaml:317-358`), Cumulative healing, Healing by
character, Heals, Heal drill-down.

**Works well.** It is a true twin: same positions, same interactions, same
clock. The rules that differ are stated where they apply (pet heals excluded
from Healing and shown apart: `src/Zerg/Views/HealBarsCard.xaml:102-111`,
`src/Zerg/Views/HealActionsCard.xaml:116-120`). Include Skillchains is hidden
here because it changes nothing (`src/Zerg/MainWindow.xaml:180-182`).

**Issues.** Everything in 3.4 applies equally (sorting, expand all, pause
timing). One addition:

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Low | **The Start/Pause tooltip talks only about damage in the Healing section** ("Stop counting damage and stop the clock…"). Verified. | `src/Zerg.Core/SessionView.cs:61-62` | Small wording slip on the section where it is most visible. |

**Recommendation.** "Stop counting and stop the clock. What happens while
paused is not counted, and paused time is not divided into DPS or HPS."

### 3.6 View section, Export and Import

**What it is.** A saved parse opened read-only and drawn with the Damage and
Healing cards. Its head card is a drop target when empty and, once a parse is
open, shows the name, Replace…, Close, a "Show Damage | Healing" switch and
the parse's start, length and party (`src/Zerg/Views/ViewCard.xaml`). Export
writes the paused parse on screen to a `.zerg` file
(`src/Zerg/MainViewModel.Parse.cs:194-216`, `src/Zerg/ParseDialog.cs`).

**Works well**

- Drag and drop with a visible accent border; one Open dialog and one
  remembered folder for opening and saving (`src/Zerg/ParseDialog.cs:22-23`).
- A file that will not open leaves what was open in place and says why on
  the card (`src/Zerg/MainViewModel.Parse.cs:130-137`).
- The export name is sensible and unique per pull
  (`src/Zerg.Core/ParseFile.cs:70-76`); saving as `.jsonl` is refused with a
  reason (`src/Zerg/MainViewModel.Parse.cs:202-207`).
- The live session keeps being measured underneath, and the UI says so.

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **Floating panels switch to the saved parse while View is on screen, and say nothing.** Panels share the main view model, which draws whatever `Shown` is; while viewing, new live data is read but not drawn anywhere. On the panel the Start/Pause pair is disabled, the clock shows the parse's length in amber, and the total's tooltip still reads "Total damage this session". Verified. | `src/Zerg/MainViewModel.Parse.cs:32-41`, `:151`, `src/Zerg/MainViewModel.cs:231-235`, `src/Zerg/PanelWindow.xaml:100-103` | The overlay exists to be glanced at during a fight. Opening an old parse on the second monitor makes the overlay over the game show an old fight's numbers with no label. |
| Medium | **Three different Damage / Healing switches**: the shell's Section row (live session), View's "Show" (this parse), Compare's "Show" (both runs). In View, two of them are on screen at once and the shell's one means "leave View". Verified. | `src/Zerg/MainWindow.xaml:159-166`, `src/Zerg/Views/ViewCard.xaml:68-74`, `src/Zerg/Views/CompareSection.xaml:441-447` | The same two words on the same screen do different things. |
| Low | **Export failures clear themselves after 8 seconds**, like successes. Verified. | `src/Zerg/MainViewModel.Parse.cs:22`, `:169-177` | An error can vanish before it is read; the red text is trimmed to 460 px with the rest only in a tooltip (`src/Zerg/MainWindow.xaml:131-133`). |
| Low | **A successful export offers no way to reach the file.** Verified. | `src/Zerg/MainViewModel.Parse.cs:210` | "Exported Hasaya_parse_….zerg" with no folder and no link. |
| Low | **No recent files and no file association**; every open goes through the dialog. Verified (association is listed as an open idea in `CLAUDE.md`). | `src/Zerg/ParseDialog.cs:28-38` | Reviewing last night's pulls means browsing each time. |
| Low | **Vocabulary**: Export… / Import… on the bar, Open… / Replace… / Close on the card, "saved parse", "exported parse", "View a parse". Verified. | `src/Zerg/MainWindow.xaml:121-129`, `src/Zerg/Views/ViewCard.xaml:33-58`, `src/Zerg.Core/SessionView.cs:41`, `:99` | "Import" suggests the file is merged into something; it is only viewed. |

**Recommendations**

- Panels: quick fix, when `Viewing` is true set the panel bar title to
  `Viewing: <name>` in the caution colour and change the total's tooltip to
  "Total damage in this saved parse". Proper fix: panels always draw the live
  session (a second, live-only set of card state), so the overlay never
  changes meaning because of what the main window is showing.
- With View promoted to a segment (3.1), the shell row becomes
  Damage | Healing | View | Compare and always means "which page". Inside
  View and Compare keep the inner switch but label it **Side** (or
  "Damage / Healing of this parse") so it does not echo the outer one.
- Notes: keep a failure until the next export or until dismissed (add a
  small "x"); on success make the file name a link that opens Explorer with
  the file selected.
- Use one verb family: **Save parse…** (was Export…) and **Open parse…**
  (was Import…), matching the card's Open… / Replace… / Close.

### 3.7 Compare section

**What it is.** Two slots, A (baseline) and B, each filled by Open…, drop, or
"Use current"; switches for Damage | Healing, Character | Job, Include
Skillchains, Swap; then, once both are filled, six A-over-B tiles, a
cumulative chart of both runs with an optional data table, the by-character
(or by-job) table whose rows open onto paired actions, whose rows in turn open
onto a paired distribution chart with a statistics column, and by-type /
by-target tables (`src/Zerg/Views/CompareSection.xaml`,
`src/Zerg/CompareViewModel.cs`).

**Works well**

- The A/B identity is carried consistently: a lettered badge, a colour edge on
  the slot, a tick before each figure, the same two colours in the charts.
- Change is always B − A with the sign in text and colour only repeating it
  (`src/Zerg/Views/ChangeText.xaml:7-10`).
- "Use current" removes the export-then-open round trip and explains itself
  when disabled (`src/Zerg/CompareViewModel.cs:366-368`).
- Errors stay on the slot they belong to and travel with it on Swap.
- Opened rows survive a redraw; detail is only built when opened, which also
  keeps a screen reader from reading closed rows
  (`src/Zerg/Views/CompareSection.xaml:182-186`).
- The cumulative chart has a "Data table" alternative
  (`src/Zerg/Views/CompareSection.xaml:561-591`).
- The distribution charts share bins and show shares, not counts, so runs of
  different length compare honestly; the caption and hover carry the counts.

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Medium | **Two players cannot be compared with each other.** Rows are matched by name across the two runs (or pooled by job). There is no way to put character X of a run beside character Y of the same run. Verified. | `src/Zerg/CompareViewModel.cs:321-323`, `src/Zerg/Views/CompareSection.xaml:451-458` | "How does my Tachi: Gekko spread compare with the other samurai's?" is a common question and the new distribution charts are the right tool for it, but there is no route to them. |
| Low | **The distribution chart is three levels deep** (row, then action, then chart) and each level is a full-width row that looks like the one above it. Verified; the feel is inferred. | `src/Zerg/Views/CompareSection.xaml:280-302`, `:623-655` | The newest feature is the hardest to find. The card note does mention it. |
| Low | **Δ columns have no text heading**, only the symbol with a tooltip. Verified. | `src/Zerg/Views/CompareSection.xaml:608-616` | Three identical "Δ" headings in one table; which figure each belongs to is by position only. |
| Low | **The A/B marker in table cells is a 5 × 2 px tick.** Verified; legibility inferred. | `src/Zerg/Views/RunPair.xaml:13-20` | Very small for the only per-cell cue; the fixed A-over-B order carries it. |
| Low | **With one slot filled nothing says what is missing.** The lower part is simply absent. Verified. | `src/Zerg/Views/CompareSection.xaml:479`, `src/Zerg/CompareViewModel.cs:468` | A hint would close the loop. |
| Low | **Dropping two files fills only one slot** (the first file is taken). Verified. | `src/Zerg/Views/CompareSection.xaml.cs:41-43` | A natural gesture half works. |
| Low | **Include Skillchains is a full-size toggle here and a small one on the shell.** Verified. | `src/Zerg/Views/CompareSection.xaml:463-465` vs `src/Zerg/MainWindow.xaml:180` | Same setting, two looks. |
| Low | **Slots are emptied when Zerg closes.** Verified, deliberate. | `src/Zerg/CompareViewModel.cs:258-259`, `src/Zerg/Settings.cs:32-34` | A comparison in progress has to be rebuilt from the file dialog. |

**Recommendations**

- Player-versus-player: add **Character** as a third "Compare by" mode when
  both slots hold the same file (or add a per-slot "Only: [character ▾]"
  picker). Then A = Hasaya, B = the other samurai, and every table and
  distribution chart below already works.
- Give the action rows under a character a hint on first open: a secondary
  line "Select an action to compare how its hits are spread", or a small
  chart glyph at the right of each action row.
- Headings: "Δ Damage", "Δ DPS", "Δ Accuracy", "Δ WS Avg" (the columns are
  wide enough at the table's 1080 minimum), or a two-row heading.
- Widen the RunPair tick to a 3 × 10 px bar, or prefix the figures with a
  small "A" / "B" in the run colour.
- When exactly one slot is filled show, under the slots: "Add a second parse
  to compare. Tip: Use current takes the session being measured."
- Accept two dropped files: first into the slot dropped on, second into the
  other.
- Remember the two slots' file paths and reopen them at the next start if
  the files still exist (paths, not contents, so the settings file stays
  small).

### 3.8 Settings page

**What it is.** A page that replaces the section on screen, opened by the gear
or the tray menu, closed by Done, the gear, or any section segment. Five
cards: Events folder, Click-through hot key, Default pop-out opacity, Draw
frequency, Theme (`src/Zerg/Views/SettingsPage.xaml`,
`src/Zerg/MainViewModel.Settings.cs`). Everything applies at once.

**Works well**

- No Apply / Cancel to forget: each change is live and saved (sliders are
  debounced, `src/Zerg/MainViewModel.Settings.cs:280-287`).
- Events folder: the path can be selected and copied, the note says how many
  event files are there and which is newest, changing it mid-session asks
  first with a plain-language warning (`:77-103`, `:116-134`).
- Hot-key recorder: the button shows the keys and takes new ones, Esc or
  leaving cancels, a rejected chord says what is allowed, a chord Windows
  will not give is never saved (`:189-246`,
  `src/Zerg/Views/SettingsPage.xaml.cs:41-62`).
- The page is capped at 860 px so controls stay near their labels.

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **Click-through is the only global hot key.** Start/Restart and Pause/Resume have none. Verified (one `RegisterHotKey` call site; no key or input bindings anywhere in `src`). | `src/Zerg/MainWindow.xaml.cs:102-108`, `src/Zerg/Native/HotKey.cs` | See workflow 5.3: with click-through on, the session cannot be started or paused without leaving the game or toggling click-through off and on around a mouse click. |
| Medium | **A hot key that failed to register at start is reported only on this page** (and in the log). The Click-through tooltip quietly drops the key name. Verified. | `src/Zerg/MainViewModel.Settings.cs:177-182`, `src/Zerg/Panels.cs:222-223`, `src/Zerg/Views/SettingsPage.xaml:85` | The player switches click-through on, returns to the game, presses the remembered keys, and nothing happens. The way out (tray menu or the main window) is not shown anywhere they are looking. |
| Medium | **Once a panel's own slider has been moved, the default-opacity slider no longer affects it, and the page does not show which panels those are.** The card's text explains the rule. Verified; the developer notes record that a reset was offered and declined (`CLAUDE.md`, "Open ideas"). | `src/Zerg/Panels.cs:227-248`, `src/Zerg/Views/SettingsPage.xaml:103-113`, `src/Zerg.Core/PanelOpacities.cs:11-14` | After a while the Settings slider appears to do nothing, with no clue why. |
| Low | **"Draw frequency" is described in engine terms and gives no sense of cost.** 1 to 60 "a second", default 30. The project's own measurements put 30 a second at 23 to 25% of one core with the Damage section on screen, against 2 to 3% at 4 a second (`CLAUDE.md`, "What the draw frequency costs"). The clock itself only changes once a second. Verified (the cost figures are the project's, not mine). | `src/Zerg/Views/SettingsPage.xaml:120-130`, `src/Zerg.Core/DrawRate.cs:16-19` | FFXI leans on one core. A player cannot tell from "Higher is smoother and uses more processor time" that the default is the expensive end, or what a sensible low value is. |
| Low | **The default-opacity slider has no preview** unless a panel happens to be out and un-overridden. Verified. | `src/Zerg/Views/SettingsPage.xaml:106-113` | Setting a see-through value blind. |
| Low | **Esc does not close the page**; Done sits at the right end of an 860-px column while the gear that also closes it is at the far right of the window. Verified. | `src/Zerg/Views/SettingsPage.xaml:22-27` | Two close controls, neither where a dialog's would be, no keyboard exit. |
| Low | **No About information**: version, where settings and logs are, a way to open the log folder. The version is only written to the log. Verified. | `src/Zerg/Log.cs:27`, `src/Zerg/Zerg.csproj:18-21` | A bug report needs both. The crash dialog names the log path as plain text (`src/Zerg/App.xaml.cs:104`). |
| Low | **The events folder cannot be opened in Explorer or pasted in.** Verified. | `src/Zerg/Views/SettingsPage.xaml:35-47` | Checking what the addon wrote means copying the path by hand. |
| Low | **Behaviours with no setting**: minimize-to-tray (always on), click-through at start (always off), which section opens first. Verified. | `src/Zerg/MainWindow.xaml.cs:156-167`, `src/Zerg/Panels.cs:54-57` | The defaults are defensible; some players will want the others. |

**Recommendations**

- Rename the hot-key card **Hot keys** and list three rows with the existing
  recorder button on each: Click-through panels (Ctrl+Alt+Z), Start / Restart
  (suggest Ctrl+Alt+S), Pause / Resume (suggest Ctrl+Alt+P). Optionally a
  fourth, Show / hide panels (Ctrl+Alt+H). `KeyChord`, `HotKey.Register` and
  the "taken by another program" handling already cover everything needed;
  the settings file needs three more keys.
- When `BindHotKey` fails at start, put a persistent line on the status row:
  `Ctrl+Alt+Z is in use by another program, so click-through has no hot key.
  Change it` (link to Settings), and show a small warning dot on the gear.
- Opacity card: under the slider list the overridden panels, e.g.
  `Own setting: Damage by character 60%, Actions 40%`. This keeps the
  "own value outlasts the default" rule and makes it visible. Add a sample
  swatch (a small dark rectangle over a checkerboard) driven by the slider.
- Rename **Draw frequency** to **Refresh rate**, and replace the bare slider
  with three presets plus the slider: `Light (4/s)`, `Balanced (10/s)`,
  `Smooth (30/s)`. Card text: "How often the DPS and HPS figures and the
  moving edge of the cumulative charts are redrawn. New hits always appear
  at once. Smooth uses roughly a quarter of one processor core while the
  Damage section is open." Consider Balanced as the default.
- Handle Esc on the page (when not recording a chord) as Done.
- Add a last card, **About**: `Zerg 0.1.0`, buttons `Open log folder`,
  `Open settings folder`, `Copy version`. Add `Open folder` beside Browse….
- Add a **Window** card: `[x] Minimize to the notification area`,
  `[ ] Start with click-through on` (off by default, with the existing
  warning text).

### 3.9 Floating panels (pop-outs) and click-through

**What it is.** Any of the eight cards can be popped out into a borderless,
topmost, see-through window. Its 32-px bar holds the title, Start and the
second button, the clock, the section total, an opacity slider and Dock
(`src/Zerg/PanelWindow.xaml`). The card inside takes a compact form: end
labels instead of a legend, a four-column strip instead of the 14-column
table. Click-through passes the mouse to the game for all panels at once and
strips the bar down to title, clock, total and a lock glyph
(`src/Zerg/PanelWindow.xaml.cs:272-287`).

**Works well**

- Clicks never take the keyboard from the game, including drags and resizes
  (`src/Zerg/Native/Overlay.cs:27-40`, `:63-102`).
- The floating forms are redesigned, not shrunk: the strip keeps only what a
  glance is for and nothing in it moves with the clock
  (`src/Zerg/Views/BarsCard.xaml:49-91`).
- The owner's row has an accent edge so it is found without reading
  (`src/Zerg/Views/BarsCard.xaml:69-71`).
- Start and Pause are never faded and stay a fixed target because the clock
  and total sit to their right (`src/Zerg/PanelWindow.xaml:73-82`).
- An empty drill-down panel says how to fill it
  (`src/Zerg/PanelWindow.xaml.cs:259-262`).
- The main window leaves a labelled stand-in with "Bring back" where a card
  was (`src/Zerg/Views/Away.xaml`).
- Click-through is never restored at start, so a player is never met by a
  panel they cannot move (`src/Zerg/Panels.cs:54-57`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **Click-through removes Start and Pause from the panel, and there is no key for them.** Verified. | `src/Zerg/PanelWindow.xaml.cs:285`, `src/Zerg/PanelWindow.xaml:84-92` | The mode a player fights in is the mode with no session control. |
| High | **Panels show a saved parse while the View section is on screen**, unmarked. See 3.6. | `src/Zerg/MainViewModel.Parse.cs:41` | Mode error on the surface that is only ever glanced at. |
| Medium | **Legibility over the game.** Panel text is fixed at 10 to 13 px: strip headings 10, strip cells 12, bar buttons 12, opacity read-out 11, chart labels 11. Only the strip has a dark halo behind its text; the Actions and Heals tables, the drill-down and the chart labels are plain text on a backdrop that can be set as low as 15%. Sizes verified; the legibility consequence is inferred. | `src/Zerg/Views/BarsCard.xaml:15-28`, `:73-76`, `src/Zerg/PanelWindow.xaml:31`, `:63`, `src/Zerg/Charts/LineChart.cs:126`, `:143`, `src/Zerg.Core/PanelOpacities.cs:18` | On a 1440p or 4K display, over bright scenery, small secondary-colour text at a low backdrop will wash out. There is no way to make a panel's text larger. |
| Medium | **There is no "hide all panels for a moment".** Dock all closes every panel; bringing them back is one menu trip per panel. Verified. | `src/Zerg/Panels.cs:150-155`, `src/Zerg/TrayMenu.xaml:56-67` | Cutscenes, screenshots, menus: the player wants the overlay gone and back, not rebuilt. |
| Low | **A new panel opens on top of the main window**, not over the game. Verified. | `src/Zerg/PanelWindow.xaml.cs:91-97` | Each panel has to be dragged to the game once. Places are remembered afterwards. |
| Low | **The bar does not fit a narrow panel.** Its fixed content (pair, clock, total, slider, read-out, Dock) adds up to roughly 430 px; the minimum panel width is 280. Inferred from the declared sizes and paddings. | `src/Zerg/PanelWindow.xaml:8`, `:58-104` | Below about 430 px the title goes and then something on the bar is cut off. Default widths (500 to 660) are fine. |
| Low | **The drill-down's six figures share one row** whatever the panel's width. Inferred. | `src/Zerg/Views/DrillCard.xaml:31-36` | In a narrow panel each tile is too small for a 20-px figure. |
| Low | **Dock is drawn as a close "X".** The tooltip explains. Verified. | `src/Zerg/PanelWindow.xaml:66-70` | Reasonable, since closing is docking, but it reads as "discard". |
| Low | **Six words for two ideas**: "Pop out", "pop-out", "panel", "floating panel", "Dock", "Bring back". Verified. | `src/Zerg/App.xaml:168-171`, `src/Zerg/Views/SettingsPage.xaml:103`, `src/Zerg/TrayMenu.xaml:56-67`, `src/Zerg/Views/Away.xaml:14-19` | Settings says "pop-out", the tray says "Panels", the stand-in says "Bring back", the bar says "Dock". |

**Recommendations**

- Hot keys for Start/Restart and Pause/Resume (3.8) are the fix for the first
  issue. Keep the pair hidden under click-through; a control that cannot be
  clicked should not be drawn.
- Apply the strip's `DropShadowEffect` halo (or a 1-px dark outline) to all
  text in floating forms via a `Float.On` trigger, and add a Settings slider
  **Panel text size** (90% / 100% / 115% / 130%) applied as a
  `LayoutTransform` scale on `PanelWindow`'s `Body` and bar.
- Add **Hide panels / Show panels** to the tray menu and as a hot key: hide
  the windows without docking, leaving `openPanels` untouched.
- Open a first-time panel on the monitor the game is on if that can be
  detected, otherwise beside (not over) the main window.
- Bar: below about 440 px collapse the slider and read-out into a small
  "opacity" button with a flyout; give the bar's fixed group priority over
  the title (already so) and let the total trim last.
- Drill tiles: use a `WrapPanel` with a 96-px minimum tile width when
  `Float.On` is true.
- Vocabulary: noun **panel**, verbs **Pop out** and **Dock**. So: "Default
  panel opacity", stand-in button "Dock here" (or keep "Bring back" as the
  one exception and say "Dock" in its tooltip), and on the bar either a dock
  or pin glyph from the icon font in place of the X, or the X with the word
  "Dock" beside it while the pointer is over the panel.

### 3.10 Tray icon, minimize, close, second launch

**What it is.** A notification-area icon. Left click shows Zerg; right click
opens a menu: Show Zerg, Start / Pause (same wording and enabling as the
buttons), Click-through panels (with the hot key as gesture text), a Panels
submenu with ticks, Dock all panels, Settings…, Exit
(`src/Zerg/TrayMenu.xaml`). Minimizing hides the window to the tray with the
panels still up (`src/Zerg/MainWindow.xaml.cs:156-167`). Closing the main
window exits (`src/Zerg/App.xaml:7`). A second launch brings the first
forward (`src/Zerg/App.xaml.cs:19-25`, `:62-66`).

**Works well.** The menu is the same commands with the same words; it is the
documented way back from click-through; the icon survives an Explorer
restart; if the icon could not be created, minimize does not hide
(`src/Zerg/MainWindow.xaml.cs:160`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| High | **Closing the window ends an unsaved session without asking.** Verified. | `src/Zerg/MainWindow.xaml.cs:180-192` | Sessions are never saved by design (`src/Zerg.Core/Tracker.cs:92-93`), so the X on the title bar discards a running or paused, un-exported fight, and closes every panel with it. |
| Medium | **The first minimize makes Zerg disappear with no explanation.** The taskbar button goes; on Windows 11 a new tray icon starts in the overflow (noted in `CLAUDE.md`). Verified that no notification is shown; the overflow behaviour is Windows'. | `src/Zerg/MainWindow.xaml.cs:160-164`, `src/Zerg/Native/TrayIcon.cs:47-56` | "Did it crash?" Relaunching does bring it back, which is a good safety net but not an explanation. |
| Low | **"Restart" in the tray menu is as unguarded as the button.** Verified. | `src/Zerg/TrayMenu.xaml:47-48` | A menu is an easy place to slip by one line. |
| Low | **The tray tooltip is always just "Zerg".** Verified. | `src/Zerg/MainWindow.xaml.cs:89` | The one place state could be read with the window hidden says nothing. |
| Low | **No Export on the tray menu.** Verified. | `src/Zerg/TrayMenu.xaml` | A player living in the tray must show the window to save. |

**Recommendations**

- On close with `StartedAt != null` and nothing exported since the last
  counted event: `TaskDialog.Ask("Close Zerg and lose this session?",
  "The session being measured has not been exported. Closing ends it.")`,
  or better, auto-save it (5.6) and close without asking.
- First hide to tray: a one-time balloon/toast, "Zerg is still running. Its
  icon is in the notification area; panels stay on screen." Remember that it
  was shown. Add the "Minimize to the notification area" setting (3.8).
- Tray tooltip: `Zerg · live 12:34 · 1,234,567` / `Zerg · paused` /
  `Zerg · waiting for VibeXI` (update on session change and once a second
  at most).
- Add `Export…` to the tray menu, enabled as on the bar.

### 3.11 Dialogs and error states

**What it is.** The Windows Open, Save and folder dialogs
(`src/Zerg/ParseDialog.cs`, `src/Zerg/MainWindow.xaml.cs:112-117`); a task
dialog that asks before a folder change ends a session; a task dialog for an
unhandled error with details and the log path; a task dialog for bad
command-line options (`src/Zerg/Native/TaskDialog.cs`, `src/Zerg/App.xaml.cs`).

**Works well.** Native dialogs, owned by the main window, with a headline
that is a question and a body that states the consequence
(`src/Zerg/MainViewModel.Settings.cs:82-85`). A non-fatal error does not kill
the app mid-fight, and repeated faults do not stack dialogs
(`src/Zerg/App.xaml.cs:84-111`). Import failures have player-facing messages
(`src/Zerg.Core/ParseFile.cs:22-26`).

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Low | **The confirm dialog's buttons are OK / Cancel.** Verified. | `src/Zerg/Native/TaskDialog.cs:34` | "OK" does not say what will happen; verb buttons are safer for a destructive choice. |
| Low | **The folder dialog's title is a description, not an action**: "The folder VibeXI writes its event files to". Verified. | `src/Zerg/MainWindow.xaml.cs:114` | Reads oddly as a window title. |
| Low | **The log path in the crash dialog is plain text.** Verified. | `src/Zerg/App.xaml.cs:104` | Cannot be clicked or copied easily. |
| Low | **An unreadable event file is reported only on the status line.** Verified. | `src/Zerg/MainViewModel.cs:452-458` | A red 10-px dot and a 12-px line for "nothing is being recorded". |

**Recommendations**

- Add custom button text to `TaskDialog.Ask` (the config already has
  `cButtons` / `pButtons`): "Change folder and end session" / "Keep
  session".
- Folder dialog title: "Choose the VibeXI events folder".
- Crash dialog: enable hyperlinks in the footer, or add an "Open log folder"
  button.
- For the Error status, also show the message as a persistent note beside
  Export/Import (the red `ParseNote` style already exists).

### 3.12 Charts (shared behaviour)

**What it is.** Four custom-drawn charts (line, bar, histogram, paired
histogram) with a hover card that reads the thing under the pointer
(`src/Zerg/Charts/Chart.cs`).

**Works well.** Hover covers the whole chart, not just the ink; the card is
opaque, placed to stay inside the chart, and splits into columns for an
alliance; empty charts say why they are empty in the session's own words;
inks follow the theme; no per-frame redraw.

**Issues**

| Sev | Issue | Evidence | Why it hurts |
|---|---|---|---|
| Low | **Charts are mouse-only and invisible to assistive technology.** `Chart` derives from `FrameworkElement`, is not focusable and creates no automation peer, so the `AutomationProperties.Name` set on each chart is not exposed. Verified (no `OnCreateAutomationPeer` in `src`). | `src/Zerg/Charts/Chart.cs:32`, `:271-272`, e.g. `src/Zerg/Views/LineCard.xaml:61-62` | The bar chart and histograms have tables or tiles beside them; the live cumulative chart has no non-visual equivalent (Compare's has a data table). |
| Low | **Job colours are admittedly not colour-blind safe** (four reds) and rely on the name being beside every mark. Verified, documented. | `src/Zerg/Themes/Dark.xaml:31-37` | In the cumulative chart with the legend above and many lines, matching a line to a name by colour is the only cue in the docked form (the floating form has end labels). |

**Recommendations**

- Give `Chart` an `OnCreateAutomationPeer` returning a simple
  `FrameworkElementAutomationPeer` with the name and a one-line summary as
  help text ("6 characters; leader Hasaya 412,330").
- Offer end labels on the docked cumulative chart too (the `EndLabels`
  property exists, `src/Zerg/Views/LineCard.xaml:77`), or highlight the line
  whose legend entry is hovered.

---

## 4. Cross-cutting themes

### 4.1 Terminology

- **session / parse / run / pull**: "session" is live, "parse" is a saved
  file, "run" is a parse in a Compare slot, "pull" appears in tooltips. The
  first three are a defensible system; say it once (the Compare card could
  read "Open or drop two saved parses (runs)") and drop "pull" from UI text
  or use it only in Start's tooltip.
- **Export / Import / Open / Replace / View**: see 3.6. Prefer Save parse…
  and Open parse….
- **panel / pop-out / floating panel; Dock / Bring back**: see 3.9.
- **held / paused**: see 3.3.
- **Draw frequency**: see 3.8.

### 4.2 Navigation model

Five destinations (Damage, Healing, View, Compare, Settings) are reached
three different ways: three by segment, one by a command button, one by a
toggle. Inside View and Compare a second Damage | Healing switch appears.
Recommendation: four segments (Damage, Healing, View, Compare) plus the gear;
inner switches labelled differently from the outer ones; Esc leaves Settings.

### 4.3 Keyboard access and shortcuts

Verified: there are no `KeyBinding`s, access keys or `IsDefault` /
`IsCancel` buttons anywhere in `src`; the only keyboard handling is the chord
recorder and the one global hot key. Everything is reachable by Tab because
rows are real buttons, which is good, but nothing is fast. Suggested in-app
bindings on `MainWindow`:

| Keys | Action |
|---|---|
| F5 or Ctrl+R | Start / Restart |
| Space (when focus is not in a control) or Ctrl+P | Pause / Resume |
| Ctrl+S | Save parse (Export) |
| Ctrl+O | Open parse (Import) |
| Ctrl+1 … Ctrl+4 | Damage, Healing, View, Compare |
| Ctrl+, | Settings |
| Esc | Close Settings; else close the drill-down |
| Ctrl+H | Hide names |

Show each in its control's tooltip. Add global hot keys as in 3.8.

### 4.4 Accessibility

- Names and live regions are in good shape. Charts are the gap (3.12).
- Small text: about twenty places set 9.5 to 11 px
  (e.g. `src/Zerg/Views/CompareSection.xaml:46`,
  `src/Zerg/Views/ChangeText.xaml:46`, `src/Zerg/Views/RunBars.xaml:7`,
  `src/Zerg/App.xaml:89`). Raise the floor to 11 px in the main window.
- Tertiary-colour text carries information in several places: job badges
  (`src/Zerg/App.xaml:92`), strip headings, the idle clock
  (`src/Zerg/App.xaml:246`), chart axis labels. Inferred: in the light theme
  the Fluent tertiary brush on a white card is likely below 4.5:1 at these
  sizes. Check with a contrast tool; use the secondary brush for anything
  that is data.
- Much explanation is tooltip-only on plain `TextBlock`s (column headings),
  which the keyboard cannot reach. A small "?" button per table opening a
  legend, or an `Expander` "What the columns mean", would cover it.
- High-contrast mode and screen readers were not tested (see limits).

### 4.5 Visual hierarchy and density

The main window is dense but ordered: session controls, what is shown,
who is counted, figures, detail. Two notes. First, the prime position under
the tiles goes to the cumulative chart rather than the ranking (3.4).
Second, Compare tables put five ideas in one cell row (A, B, tick colours,
Δ %, Δ absolute); this is as dense as a table can be and still read, and it
does read, but only because of the strict A-over-B order. Do not add to it.

### 4.6 Feedback, status and errors

Strong inside a screen; weak for events that happen *to* the session:

| Event | What the player is told today |
|---|---|
| Restart pressed during a session | Nothing (figures go to zero) |
| New event file ended the session | File name changes on the status line |
| Arming dropped by a file switch | Start turns green again |
| Hot key could not be registered | A line on the Settings page |
| Panels now show a saved parse | Nothing on the panel |
| Window hidden to the tray | Nothing |
| Export failed | Red note for 8 seconds |

One persistent, dismissible notification strip (reusing the `ParseNote`
style, placed under the command bar) would cover all of these.

### 4.7 Onboarding and empty states

In-session empty states are good ("Press Start…", "Armed — …", "Paused —
…"). The before-anything state is not: see 5.1. There is no first-run tip for
the three ideas a new user must learn (Start arms; cards pop out; Ctrl+Alt+Z
makes panels click-through). A dismissible three-line "Getting started" card
on first launch, saved as `seenIntro` in settings, would do it.

### 4.8 Settings discoverability and defaults

The gear is conventional and the page is short. Defaults are mostly safe
(click-through off at start, panels at 85%, names shown, skillchains
counted). Questionable defaults: refresh at 30 a second (3.8); minimize to
tray with no notice (3.10). Per-panel opacity is discoverable only by
hovering a panel (the tools rest at 45%); the Settings card does mention it.

### 4.9 Overlay-while-gaming ergonomics

- Staying out of the way: excellent (no activation, click-through, tool
  windows off Alt+Tab).
- Glanceability: the strip is right; the clock colour (green live, amber
  paused) works at a glance. Idle and armed look the same on a click-through
  bar (grey 00:00), with the card's empty text as the only cue
  (`src/Zerg/App.xaml:245-255`). Consider amber for armed as on the status
  dot.
- Legibility and size: see 3.9.
- Control: the gap is keyboard control of the session and a quick
  hide/show (3.8, 3.9).
- Trust: the overlay must always mean "now". Today it does not while View is
  open (3.6).

---

## 5. Workflow walks

### 5.1 First launch, no data

1. Window opens centred at 1280 × 900 on Damage. Tiles read 0 and
   "not started — press Start"; every chart says "Press Start to begin
   measuring"; Start is green and enabled
   (`src/Zerg.Core/SessionView.cs:89`, `:101`).
2. The only sign that Zerg has no data source is the status line: an amber
   dot and "waiting for the addon · …\VibeXI\events doesn't exist yet. It
   appears when VibeXI first runs." (`src/Zerg/MainViewModel.cs:459-467`).
3. The user presses Start. The dot stays amber "waiting"; the buttons become
   Restart / Cancel; nothing else can happen.

Friction: the largest call to action is the wrong first step; the right one
(load the addon) is never stated as an instruction and the command is not
given; the message is in the smallest text on the screen. If the user then
loads the addon, the arming from step 3 is dropped when the first file
appears (3.3).

Recommendation (High, S): while `hasFile` is false replace the tiles and
cards with one card:

> **Waiting for VibeXI**
> Zerg shows what the VibeXI addon records while you play.
> 1. Start the game and load the addon: `/addon load vibexi`
> 2. Zerg connects by itself when the addon writes its first event.
>
> Looking in `C:\Users\…\AppData\Local\VibeXI\events` — `Change folder…`

(Confirm the exact load command for the Ashita setup players will use.) Make
`SessionText.Empty` aware of the no-file case so panels say "Waiting for
VibeXI" too, and keep an early Start armed across the first file.

### 5.2 Connecting and receiving data

Once a file exists the dot goes grey and the line shows the file name and
counts. Friction: no freshness (3.3); "0 events · 1,204 lines · not started"
does not say "connected". After Start, "armed, waiting for the first hit" is
clear. Recommendation: the freshness wording in 3.3, and a brief green flash
of the dot when new lines arrive while idle.

### 5.3 Watching a live fight

Intended routine: pop out Damage by character, drag it over the game, press
Ctrl+Alt+Z, fight.

- Starting: Start is on the panel bar and does not take the keyboard. Good.
  But with click-through already on (the state the player wants to stay in),
  the pair is hidden: Ctrl+Alt+Z, click Start, Ctrl+Alt+Z. Three actions,
  two of them a mode switch.
- During: the strip updates in place; the clock is green. Good.
- Ending: Pause needs the same three actions, at the moment the player is
  looting or being attacked by the next mob. Every second late lowers every
  DPS, permanently (3.4).
- Next pull: Restart, same three actions, and the previous pull is gone
  unless it was paused and exported from the main window first (four more
  steps: show window, Export…, name, Save).

Recommendations: session hot keys (High); pause at last hit (Medium);
auto-keep the outgoing session (5.6).

### 5.4 Reviewing a finished fight

Pause, bring the main window forward (tray click or second launch), read the
tiles, scroll to the table, open a character in Actions, pick an action, the
drill-down scrolls into view. This flow is smooth. Friction: the table may
need a scroll (3.4); no sorting; Hide names is one click for a screenshot
(good) but there is no "copy summary" for pasting numbers into chat, which is
what many players do next.

Recommendation (Low to Medium, S to M): a **Copy summary** button on the
Damage by character card that puts a few plain-text lines on the clipboard
(name, damage, share, DPS, accuracy), honouring Hide names.

### 5.5 Saving and reopening

Pause, Export…, Save: three steps, sensible name offered, note confirms.
Reopen: Import…, pick the file, View shows it. Friction: Pause is required
(3.1); the note does not link to the file; Import… does not always open a
dialog; leaving View is by clicking Damage or Healing, which nothing on the
View card says (its tooltip on the disabled Start does:
`src/Zerg.Core/SessionView.cs:41`).

Recommendation: View segment (3.1); a "Back to live session" link on the
View card.

### 5.6 Comparing fights, and comparing players

- Fight against fight: Compare, Open… into A, Open… into B (or Use current),
  read tiles, chart, tables; open a row, then an action, for the
  distribution. Works well. Friction: both runs must have been exported by
  hand at the time; slots are emptied on exit; three levels to the chart.
- Player against player: dead end (3.7).
- Underlying cause of most friction in 5.3 to 5.6: a session exists only
  until the next Restart, file switch or exit, and saving it is a manual,
  multi-step act done at a busy moment.

Recommendation (larger investment, M to L): **Recent sessions.** Whenever a
started session ends (Restart, file switch, exit) write it with the existing
export code to `%LOCALAPPDATA%\VibeXI\zerg\recent\`, keep the last 10 or 20,
and list them in View's empty card and in each Compare slot's Open menu
("Recent ▾"). This removes the Restart and close data-loss findings, makes
"compare with my last pull" two clicks, and needs no new file format.

### 5.7 Changing settings

Gear, change, Done: immediate and safe. Opacity: the per-panel slider is
found by hovering the panel; the default slider is on the page; the
relationship between them is explained in text but not shown (3.8). Refresh
rate: a number without a frame of reference (3.8). Hot key: the best control
on the page. Folder: clear, guarded. Theme: instant.

### 5.8 Putting Zerg away, bringing it back, exiting

Minimize hides to the tray silently (3.10). A tray click or a second launch
brings it back. Closing exits and discards the session silently (3.10).
Panels and their places come back at the next start; click-through does not,
by design.

---

## 6. Quick wins (small effort, noticeable benefit)

1. First-run "Waiting for VibeXI" card with the load command (5.1).
2. Keep an armed session armed across an event-file switch; show a note when
   a started session is ended by one (3.3).
3. Badge panels "Viewing: <file>" while the View section is on screen (3.6).
4. Confirm on window close when an un-exported session exists (3.10).
5. File freshness on the status line (3.3).
6. Export a running session by snapshot (3.1).
7. Status-row warning when the click-through hot key could not be taken
   (3.8).
8. One-time "still running in the notification area" message (3.10).
9. Put the hot key on the Click-through button's face (3.1).
10. Rename: "paused" for "held"; "panel" for "pop-out"; fix the gear tooltip
    (3.3, 3.9, 3.1).
11. Failure notes stay until dismissed; success note links to the file (3.6).
12. Esc closes Settings and the drill-down; sensible tab order (3.1, 3.8).
13. Verb buttons on the confirm dialog (3.11).
14. Refresh-rate presets and plain wording (3.8).
15. About card with version and "Open log folder" (3.8).

## 7. Larger investments

1. Global hot keys for Start/Restart, Pause/Resume and Hide/Show panels
   (3.8). The plumbing exists; this is mostly Settings UI and three more
   registrations.
2. Recent sessions: automatic keep of ended sessions, surfaced in View and
   Compare (5.6). Also the cleanest fix for Restart and close.
3. Panels always on the live session, independent of what the main window is
   viewing (3.6).
4. Pause at last hit and optional auto-pause (3.4).
5. Panel text size and a text halo on every floating form (3.9).
6. Player-versus-player comparison (3.7).
7. Sortable tables; reorderable or collapsible cards (3.4).
8. In-app keyboard shortcuts and a chart automation peer (4.3, 3.12).
9. View as a first-class section with one vocabulary for opening and saving
   parses (3.1, 3.6).

---

## Method and limits

**What I examined.** All XAML and code-behind under `src/Zerg` (the two
windows, the tray menu, every view under `Views/`, the app resources and both
theme dictionaries), all four parts of `MainViewModel`, `CompareViewModel`,
`Panels`, `Rows`, `Settings`, `ParseDialog`, `EventFeed`, `AppTheme`, the
chart elements, and the `Native` helpers that shape behaviour (tray icon,
overlay, hot key, task dialog, single instance). In `src/Zerg.Core` I read
what words or gates the UI: `SessionView`, `Session`, `Tracker`, `EventTail`,
`KeyChord`, `PanelOpacities`, `DrawRate`, and parts of `Format`, `ParseFile`,
`CompareSheet` and the chart layouts. I also read `CLAUDE.md` and `RULES.md`
for intent, and the addon's file-opening code to understand when a new event
file appears.

**Whether I ran the app.** No. This is a source-only audit. A published
`Zerg.exe` was already running on the machine, and the app is single-instance:
a second launch only brings the running one forward and exits. I did not want
to take the foreground from, drive, or screen-capture a session that was in
use, so nothing was launched, built or captured, and no file other than this
one was created or changed.

**What I could not assess, or am unsure of**

- Anything about actual appearance: real contrast ratios, whether the
  per-character table is above the fold, how panel text reads over the game
  at low opacity, how the panel bar behaves when narrow. These are marked
  Inferred and should be checked on screen.
- Screen-reader and high-contrast behaviour in practice (only the automation
  properties in the XAML were reviewed).
- Performance feel at different refresh rates; the cost figures quoted are
  the project's own notes.
- When exactly the addon starts a new event file during play. Zerg's reaction
  to a new file is verified; whether that can happen mid-fight at midnight
  (as an addon comment suggests) or only on a reload or relog (as the
  emitter's code suggests) I could not settle from the source.
- The exact in-game command to load the addon; the text suggested in 5.1
  needs confirming.
- The suggested default chords (Ctrl+Alt+S, Ctrl+Alt+P, Ctrl+Alt+H) were not
  checked against the game's or other tools' bindings; the existing
  "taken by another program" handling would catch a clash.
- Line numbers are as of commit `9e3d56a`.
