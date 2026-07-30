# Damage Meter

Live damage meter for HorizonXI. It tails the most recently modified chat log,
parses out every damaging action, and draws:

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
- **Show** — `Party` (damage your side dealt), `Monsters` (damage taken), or
  `Both`.
- **Characters** — click a name to include or exclude it; `All` / `None` do the
  whole list at once. The header shows how many of how many are included.
  Excluded characters are dropped from every total, chart and table, not just
  hidden. The selection is remembered across reloads and across log files, so a
  character you never want counted stays excluded.
- **Pause** — freezes ingestion so you can read a table mid-fight.
- **Reset** — drops every event collected so far and starts counting from here.

Select any row in **Actions** to open its drill-down; select it again, or use
Close, to dismiss it.

### Pop-out windows

Every chart and table panel has a **Pop out** button in its top-right corner.
It moves that panel into its own window you can drag anywhere and size however
you like — onto a second monitor, or into a corner beside the game. The panel is
still live: it keeps updating with the log, it stays in step with the filter row
back on the main page, and clicking a row in a popped-out **Actions** window
still drives the drill-down. Charts get more room in their own window and grow
with it.

A dashed placeholder holds the panel's place on the main page. **Bring back**
there, or **Dock back** in the window itself, returns it; so does simply closing
the window.

**Keep in focus** is the same thing in a window that floats above other
applications, so it stays visible while you play full-screen-windowed. It uses
the browser's document picture-in-picture, which means:

- **Chrome or Edge only.** The button is disabled in other browsers and says why.
- **One panel at a time.** Turning it on for a second panel returns the first one
  to the page — the browser only allows one floating window.

Pop-outs are not remembered across a page reload.

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
- **Skillchains and magic bursts** are separate rows, credited to the character
  who closed them. **Additional Effect** is credited to the attack it rode on.
- **`Unattributed`** means damage appeared with no announcement in front of it —
  a damage-over-time tick, spikes, an enspell. It is credited to the last
  character who dealt damage, which is a guess; the row is named so you know.

## When something looks wrong

Open **Diagnostics** at the bottom.

- **Name classification.** Monsters are detected from the article: the log says
  "the Goblin Pathfinder" but never "the Hasaya". Named notorious monsters have
  no article, so they're caught by a second pass over who-fights-whom. If one is
  still misfiled, flip it here and every chart re-sorts.
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
