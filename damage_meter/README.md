# Damage Meter

Live damage meter for HorizonXI. It tails the most recently modified chat log,
parses out every damaging action **the party dealt** — damage monsters deal is
not counted or shown anywhere — and draws:

- **Cumulative damage over time**, one line per character, with a crosshair that
  reads every character at the same instant.
- **Damage by character** — total, share, DPS, action count, average per action,
  best hit, accuracy.
- **Action breakdown** — every regular attack, weaponskill (`Tachi: Jinpu`),
  spell, skillchain and additional effect, per character, with count / total /
  average / min / max.
- **Drill-downs** — select any action to get its damage distribution: histogram,
  min, average, max, median, standard deviation, IQR, 90th percentile, crit
  rate, and the full list of individual hits.

Nothing is installed and nothing is sent anywhere. A small PowerShell process
reads the log and serves the page on `localhost`.

## Running it

Double-click **`Damage-Meter.cmd`**, or:

```bash
powershell -ExecutionPolicy Bypass -File damage-meter.ps1
```

It opens <http://localhost:8731/> and starts following the newest `*.log` in
`%APPDATA%\HorizonXI-Launcher\HorizonXI\Game\chatlogs`. Leave it running while
you play; the page updates once a second. Ctrl-C in the console window stops it.

Options:

```bash
powershell -ExecutionPolicy Bypass -File damage-meter.ps1 -Port 9000 -NoBrowser -LogDir "D:\some\other\chatlogs"
```

| Flag | Default | Meaning |
|---|---|---|
| `-LogDir` | the HorizonXI launcher path | directory to watch |
| `-Port` | `8731` | local port |
| `-NoBrowser` | off | don't open a browser on start |

It always follows whichever `.log` in that directory was modified most recently,
so a day rollover or a character switch is picked up automatically — the page
resets and replays the new file from the top.

## Using it

**The filter row scopes everything below it.**

- **Range** — `All`, `Latest fight` (everything since the last gap of 90 s or
  more with no combat), or a rolling 5 / 15 / 60 minutes measured back from the
  newest event.
- **Skillchains** — `On` credits skillchain damage to whoever closed the chain;
  `Off` leaves it out of every total, chart and table. Off is the setting to use
  when you want to compare raw weaponskill and melee output, since a chain's
  damage depends as much on who opened it. Remembered across reloads.
- **Characters** — on its own line under the other filters, so a full 18-person
  alliance gets the width it needs. Click a name to include or exclude it;
  `All` / `None` do the whole list at once. The header shows how many of how
  many are included. Excluded characters are dropped from every total, chart and
  table, not just hidden. The selection is remembered across reloads and across
  log files, so a character you never want counted stays excluded. `Hide`
  collapses the list to a single line — the count and a summary of who is
  excluded stay visible — and that too is remembered.
- **Pause** — freezes ingestion so you can read a table mid-fight.
- **Reset** — drops every event collected so far and starts counting from here.

Select any row in **Actions** to open its drill-down; select it again, or use
Close, to dismiss it.

### Keep in focus

Every chart and table panel has a **Keep in focus** button in its top-right
corner. It moves that panel into a window that floats above other applications,
so it stays visible while you play full-screen-windowed — drag it into a corner
beside the game, or onto a second monitor.

The panel is still live out there: it keeps updating with the log, it stays in
step with the filter row back on the main page, and clicking a row in a floating
**Actions** window still drives the drill-down. Charts get more room and grow
with the window.

It uses the browser's document picture-in-picture, which means:

- **Chrome or Edge only.** The button is disabled in other browsers and says why.
- **One panel at a time.** Turning it on for a second panel returns the first one
  to the page — the browser only allows one floating window.

A dashed placeholder holds the panel's place on the main page. **Bring back**
there, **Dock** in the window itself, or pressing **Keep in focus** again all
return it; so does simply closing the window.

Floating panels are not remembered across a page reload.

### Seeing the game through a floating panel

Each floating window's bar carries two controls:

- **The opacity slider.** Drags the whole window — chrome, panel and background
  alike — down to 15%. Everything gets ghosted evenly.
- **BG**, on by default. Drops the panel's background out *completely*, so what
  is left over the game is the lines, the numbers and the 20px bar. This is the
  one that makes the game readable rather than merely dimmer. Its side effect is
  that clicks land on whatever is underneath, so drag the window by its own title
  bar, not by the empty space inside it.

Both are per-panel and remembered.

A web page cannot make its own window see-through — CSS opacity fades the
contents against the *browser*, not onto your desktop. So the meter's own server
does it (`/api/alpha`), which means:

- **Windows only.** Elsewhere the slider falls back to fading just the panel's
  contents, which cannot show the game. When that happens the bar says **"fade
  only"** and its tooltip explains why.
- **The server has to be the one this page was loaded from.** An older instance
  still running from before this feature has no `/api/alpha`, and the fallback is
  all you will get — stop it and start it again.
- If a floating window ever renders **black** instead of transparent, that is the
  graphics driver refusing to composite it. Turn **BG** off; the slider at 100%
  undoes the rest.

The window's chrome is otherwise as small as a browser allows — a 20px bar that
fades until you point at it, no card frame, no headings, no sub-headings.

### What Reset does

Reset is the "clear the meter between pulls" button: it throws away the
collected events and keeps following the log from the point it had reached. It
deliberately **keeps** your filters, each character's colour, and which names
are known to be monsters — so the next pull looks the same as the last one, just
counted from zero. The status line then reads *since reset at hh:mm:ss*.

It does **not** re-read the file. If you want the whole log parsed again from
the top, reload the page — that is what a fresh page load already does.

## Reading the numbers

- **Average** is damage per *connecting* hit. Misses are counted in their own
  column and in the accuracy figure, never folded into the average.
- **DPS** for a character uses that character's own active window — first to
  last action — not the whole encounter, so someone who joined halfway through
  is not divided by time they weren't there.
- **Weaponskills** are read from two lines. The log announces
  `Hasaya uses Tachi: Jinpu.` and puts the damage on the *next* line
  (`The Goblin Pathfinder takes 723 points of damage.`), so the parser holds the
  announced action until its damage arrives. A weaponskill that gets evaded is
  recorded as a miss for that weaponskill.
- **Skillchains** are their own row (`Skillchain: Fusion`), credited to the last
  character to land a *weaponskill* — the one who closed the chain. The rest of
  the party keeps swinging in between, so "the last character to deal damage" is
  usually the wrong answer. Magic bursts are likewise their own row.
  **Additional Effect** is credited to the attack it rode on.
- **Area-of-effect** damage is split across lines: only the first victim rides
  on the announcement, and the rest arrive seconds later on their own lines with
  other people's swings in between. The announced action stays open for five
  seconds so every victim lands under it — `Meteor` on four people is four rows
  under `Meteor`, not one plus three guesses.
- **Counters** (`Promathia's attack is countered by Hasaya.`) are their own row,
  credited to the character who countered.
- **`Unattributed`** means damage appeared with no announcement in front of it —
  a damage-over-time tick, spikes, an enspell, or an area attack whose
  announcement never made it into the log. It is credited to the last character
  who dealt damage, which is a guess; the row is named so you know. Because it
  is a guess it is kept out of the party-vs-monster classification, so a wrong
  one can misplace damage but can never move a name to the wrong side.

## When something looks wrong

Open **Diagnostics** at the bottom.

- **Name classification.** Monsters are detected from the article: the log says
  "the Goblin Pathfinder" but never "the Hasaya". Named notorious monsters have
  no article, so they're caught by a second pass over who-fights-whom. If one is
  still misfiled, flip it here and every chart re-sorts. This is the table to
  check when a character is missing from the meter entirely: only characters are
  counted, so a name filed as a monster contributes nothing.
- **Unrecognised damage lines.** Any line containing a damage number that no
  parse rule matched is listed here. If this list isn't empty, the meter is
  under-counting and the pattern needs adding to
  [`web/lib/parser.js`](web/lib/parser.js).

## Layout

```
damage-meter.ps1    tails the newest log, serves web/ on localhost
Damage-Meter.cmd    double-click launcher
../shared-ui/       design system shared with ../ws_calculator, served at /shared/
web/index.html      the page
web/style.css       app-only styling: filter bar, source indicator, diagnostics
web/app.js          polling, state, rendering
web/lib/parser.js   log lines -> damage events
web/lib/stats.js    events -> totals, time series, distributions
web/lib/chart.js    canvas line / bar / histogram
web/lib/popout.js   panels in their own windows
```

The palette and the shared widgets come from
[`../shared-ui`](../shared-ui) — the same stylesheet the weaponskill calculator
uses, so the two apps look like one toolset. The server mounts that directory at
`/shared/` rather than keeping a copy here, which means it must stay a sibling of
this project. The **Light / Dark** button in the top right switches themes and
remembers the choice.

`CLAUDE.md` has the operational detail.
