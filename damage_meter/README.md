# Damage Meter

Live damage meter for HorizonXI. The **VibeXI Ashita addon** reads the game's own
action packets and writes them to a file; this reads that file and draws every
damaging action **your side dealt** — damage monsters deal is not counted or
shown anywhere:

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

Nothing is sent anywhere. The addon only ever writes a local file — it has no
network capability at all, by construction — and a small Python process reads
that file and serves the page on `localhost`.

## Setting it up

**1. Install the addon.** Copy the `addons/VibeXI/` folder from this repo into
your Ashita addons directory, so it lands at
`…\HorizonXI\Game\addons\VibeXI\` with `vibexi.lua` inside it. Then, in game:

```
/addon load VibeXI
```

It starts writing to `%LOCALAPPDATA%\VibeXI\events\<Character>_<date>.jsonl`.
Add the load line to your startup script if you want it every session.

**2. Start the meter.** Double-click **`Damage-Meter.cmd`**, or:

```bash
python damage-meter.py
```

It opens <http://localhost:8731/> and follows the newest `*.jsonl` in
`%LOCALAPPDATA%\VibeXI\events`. Leave it running while you play; the page
updates four times a second. Ctrl-C in the console window stops it.

Options:

```bash
python damage-meter.py --port 9000 --no-browser --events-dir "D:\some\other\events"
```

| Flag | Default | Meaning |
|---|---|---|
| `--events-dir` | `%LOCALAPPDATA%\VibeXI\events` | directory to watch |
| `--port` | `8731` | local port |
| `--no-browser` | off | don't open a browser on start |

It always follows whichever `.jsonl` in that directory was modified most
recently, so a day rollover or a character switch is picked up automatically —
the page resets and replays the new file from the top.

If the status line says **waiting for the addon**, nothing has been written yet:
either the addon is not loaded, or nothing has happened in game since it was.

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
  event files, so a character you never want counted stays excluded. `Hide`
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

The panel is still live out there: it keeps updating, it stays in
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
collected events and keeps following the file from the point it had reached. It
deliberately **keeps** your filters, each character's colour, and which names
are known to be monsters — so the next pull looks the same as the last one, just
counted from zero. The status line then reads *since reset at hh:mm:ss*.

It does **not** re-read the file. If you want the whole session counted again
from the top, reload the page — that is what a fresh page load already does.

## Reading the numbers

Everything here comes from the game's own action packets, so there is no
guesswork left in any of it.

- **Average** is damage per *connecting* hit. Misses are counted in their own
  column and in the accuracy figure, never folded into the average.
- **DPS** for a character uses that character's own active window — first to
  last action — not the whole encounter, so someone who joined halfway through
  is not divided by time they weren't there.
- **Accuracy** counts swings, not attack rounds. A double-attack round that
  landed once and missed once is one hit and one miss.
- **Weaponskills** are one row each. A weaponskill that gets evaded is recorded
  as a miss for that weaponskill, because the packet says which of the two it
  was rather than leaving it to be inferred.
- **Skillchains** are their own row (`Skillchain: Fusion`), credited to whoever
  closed the chain. The chain arrives attached to the closing weaponskill, so
  this is stated by the game and not worked out from who swung last, and its
  damage is counted separately from the weaponskill's own. Magic bursts are
  flagged on the row that burst. **Additional Effect** — an enspell, a Sneak
  Attack proc, an HP drain — is its own row, credited to the attack it rode
  on.
- **Area-of-effect** damage is one action however many targets it reached. The
  packet carries the whole target list, so `Firaga III` on three mobs is a
  single cast of the summed damage, with the target column reading `3 targets`.
- **Pets** are counted as their owner's damage. The packet names each pet's
  owner outright, so this is attribution and not a guess: the pet has no row of
  its own, and its damage shows up inside the owner's breakdown under the pet's
  name, like `Fluffikins: Big Scissors`.
- **Damage dealt *to* the party is not recorded.** This meter measures what the
  party dealt to monsters; a monster's own swings are dropped by the addon
  before they are ever written.
- **Counters, spikes and Retaliation are counted**, under those names, for
  whoever reacted. They are damage your side dealt to the monster even though
  the monster is the one who swung, so the addon files them the right way round
  before writing them. Reactions that dealt no damage are not recorded, so those
  rows always show 100% accuracy.

## When something looks wrong

Open **Diagnostics** at the bottom.

- **Name classification.** Every name is classified from the game's own spawn
  flags — player, pet, mob, npc — so this should always be right; there is no
  heuristic left to get it wrong. It is still the table to check when someone is
  missing from the meter entirely, and you can override any row by hand.
- **Addon notices.** The addon's startup probe, plus one line for each kind of
  game message it saw and did not recognise. Anything listed there is damage
  nobody is being credited with, and the fix is a new message id in
  [`../addons/VibeXI/vx_enums.lua`](../addons/VibeXI/vx_enums.lua).

## Layout

```
damage-meter.py     tails the newest addon event file, serves web/ on localhost
winalpha.py         ctypes Win32 call that makes a pop-out window see-through
Damage-Meter.cmd    double-click launcher
../addons/VibeXI/   the Ashita addon that produces the data
../shared-ui/       design system shared with ../ws_calculator, served at /shared/
web/index.html      the page
web/style.css       app-only styling: filter bar, source indicator, diagnostics
web/app.js          polling, state, rendering
web/lib/source.js   addon JSONL lines -> damage events
web/lib/stats.js    events -> totals, time series, distributions
web/lib/chart.js    canvas line / bar / histogram
web/lib/popout.js   panels in their own windows
tools/gen-test-events.py   writes a synthetic event file, for working without the game
```

The palette and the shared widgets come from
[`../shared-ui`](../shared-ui) — the same stylesheet the weaponskill calculator
uses, so the two apps look like one toolset. The server mounts that directory at
`/shared/` rather than keeping a copy here, which means it must stay a sibling of
this project. The **Light / Dark** button in the top right switches themes and
remembers the choice.

`CLAUDE.md` has the operational detail.
