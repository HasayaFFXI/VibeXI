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

**Nothing is counted until you press Start.** The meter is a stopwatch: every
figure on the page — the totals, the DPS, the chart's x axis — runs on elapsed
time from the start of the pull, not on the time of day.

The two session buttons are the biggest and the only coloured controls on the
bar, because they are the only ones that change what is being *measured* rather
than what is being *shown*. Green means the clock is counting; brass means it is
not — the status dot beside the filename uses the same two colours.

- **Start** — arms the meter. **The clock does not start on the press; it starts
  on the first hit that counts**, and that hit sits at 0:00. So you can press it
  early — on the run in, while the last buff goes up — and still measure the
  pull and nothing but the pull. While it is waiting the status dot is a pulsing
  ring and the panels say *armed*. Pressing Start again re-arms, so it is also
  how you clear the meter between pulls; there is no separate Reset.

  A swing that *misses* starts the clock too — it is still the fight beginning,
  and it belongs in your accuracy. What does not start it is anything the meter
  would not have counted anyway: a monster's attack, or a character you have
  switched off. (Switch every character off and the clock will never start,
  which is the one way this can look stuck — press Cancel and start again.)
- **Cancel** — appears in place of Pause while the meter is armed, and calls the
  arming off. The pull went wrong, the party reset, you armed the wrong moment:
  press it and the meter goes back to counting nothing, ready to be armed again.
  It is only there before the clock starts — once a session is running the
  button is Pause again, and a session that has damage in it is ended by Start.

- **Pause** — stops counting damage *and* stops the clock. Damage dealt while
  paused is not counted, and the time you were paused is not divided into your
  DPS, so a break for buffs or a run back to camp does not drag the numbers
  down. Resume picks the timeline up exactly where it left off — the chart shows
  no gap, because as far as the meter is concerned that time did not happen.

  Only the paused stretch itself is cut. The quiet before you press Pause, and
  the quiet after you press Resume, are ordinary parts of the pull and are
  counted: the clock runs on real time from the moment the first hit lands, not
  from swing to swing. Waiting for an event happens once, at the very start.

**DPS falls while nothing is happening**, and that is the point of a fixed
start: the damage stays put while the clock keeps running, so the number you are
reading is real output over the whole pull rather than over whichever moments
you happened to be swinging. Every character's DPS is divided by the same
clock, so the character column adds up to the party figure.

**The filter row scopes everything below it.**

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

Select any row in **Actions** to open its drill-down; select it again, or use
Close, to dismiss it.

**Everyone is painted in their job's colour** — the same mapping Metrics uses,
so the meter agrees with the parser next to it and the tank is the colour you
already expect. Two people on the same job get the same hue at different
lightnesses. Several jobs are shades of red, so the job palette is not one you
can read under colourblindness — but nothing here depends on the hue: every
panel prints the name and the job in text beside the colour. Anyone with no job
colour on record — a trust, a pet's owner seen only through the pet, DNC, SCH,
GEO or RUN, which Metrics never finished — gets a distinct colour of their own
instead.

### Hide names

**Hide names**, in the top bar beside the theme toggle, draws every character
but you as their job — `SAM/WAR` where their name was — so a screenshot or a
stream can show the numbers without showing the party. Your own name stays,
because a meter you cannot find yourself on is not much use; the meter knows
which character is yours from the event file's name.

Nothing is filtered and no number moves: the chips still exclude the same
people, the colours are unchanged and every total is identical. Only the text
goes — on the chips, the tiles, the legend, both tables and the drill-down. The
character table's Job column steps aside, because the name is now carrying
it. Two people on the same job come out as `SAM/WAR` and `SAM/WAR 2`,
numbered in the order they took their colours, so nobody's label moves mid-
session. Someone whose job the addon has never reported reads `Unknown job`.

The setting is remembered across reloads. Two things it does not hide: a pet
still goes by its own name, because its name is part of the action
(`Fluffikins: Big Scissors`) rather than a character's, and the source line at
the top still shows the event file, which is named after you.

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

Two things happen to a floating window:

- **Its background is dropped out entirely**, so what is left over the game is
  the lines, the numbers and the 20px bar. This is what makes the game readable
  rather than merely dimmer, and it is always on. Its side effect is that clicks
  land on whatever is underneath, so drag the window by its own title bar, not by
  the empty space inside it.
- **The opacity slider in the bar** drags what remains — chrome, panel and all —
  down to 15%. It is per-panel and remembered.

**Panels open at 85% opacity**, and **Pop-out opacity** in the top bar of the
main page changes that — it is the one opacity control you can reach without a
window already open. Moving it sets every panel back to it; a window's own
slider then adjusts that one window, until you touch the main-page control again.

A web page cannot make its own window see-through — CSS opacity fades the
contents against the *browser*, not onto your desktop. So the meter's own server
does it (`/api/alpha`), which means:

- **Windows only.** Elsewhere the slider falls back to fading just the panel's
  contents, which cannot show the game. When that happens the bar says **"fade
  only"** and its tooltip explains why.
- **The server has to be the one this page was loaded from.** An older instance
  still running from before this feature has no `/api/alpha`, and the fallback is
  all you will get — stop it and start it again.
- **The whole window fades together — the bar cannot stay solid while the panel
  goes see-through.** Windows gives a window one opacity, and the per-pixel
  alternative is not available to a browser window. If you want a solid header
  over a transparent panel, that needs an in-game overlay, not this.
- If a floating window ever renders **black** instead of transparent, open the
  browser console and run `DPS.popout.keyBg('line', false)` (or `bars`,
  `actions`, `drill`) to stop painting that panel's background
  near-black; the slider at 100% undoes the rest.

**Every floating panel carries its own Start and Pause**, in the same colours as
the page's, and they stay lit while the rest of the bar fades. The whole reason
to float a panel is to run a pull without leaving the game, and reaching back to
the browser to press Start is exactly what that is meant to avoid. Press either
one anywhere — page or panel — and every window follows.

The window's chrome is otherwise as small as a browser allows — a 20px bar that
fades until you point at it, no card frame, no headings, no sub-headings.

### Starting a second pull

Press **Start** again. It throws away the collected events, re-arms the clock —
so the next counted hit becomes the new 0:00 — and keeps following the file from
the point it had reached. It deliberately
**keeps** your filters, each character's colour, and which names are known to be
monsters — so the next pull looks the same as the last one, just counted from
zero. The status line reads *armed, waiting for the first hit* until one lands,
then *started hh:mm:ss* — the time of the hit itself, and the one place the
meter still prints a time of day.

It does **not** re-read the file. Reloading the page rewinds to the top of the
file, but it also puts the meter back to *not started* — the clock never
survives a reload, because a stopwatch restored from a previous page load would
be timing something you were not doing.

### Saving a parse, and opening someone else's

Pause the session and **Export…**, next to the session buttons, lights up. It
opens the browser's Save dialog: choose a folder, name the file, save. The
suggested name is yours plus when the pull began —
`Hasaya_parse_2026.07.30_1402.json`. The file holds the pull and nothing else:
every hit inside the session, the party's jobs, and the clock with its pauses.
Your filters are not in it, so whoever opens it sees it through their own.

**Import…** opens one. The whole page switches to that parse — totals, charts,
drill-downs — with its clock stopped where it was exported, and the
status line reads `imported · <file>`. You can filter it, hide names and float
panels as usual, but Start and Pause are switched off: it is a recording, not a
fight. **Back to live** returns you to your own meter exactly as you left it.

Importing takes nothing away. If a pull was running when you imported, it is
still running when you come back, and whatever the addon wrote in the meantime
is read in then.

- **Chrome or Edge** give you the folder picker and remember the folder, and
  Import opens in the same place. Other browsers save to your downloads folder.
- **Only Export needs a pause.** A running clock would be out of date before the
  file was written.
- **Keep the `.json` ending.** The meter follows the newest `.jsonl` in the
  events folder, so an export renamed to `.jsonl` would be read as today's file.

## Reading the numbers

Everything here comes from the game's own action packets, so there is no
guesswork left in any of it.

- **Average** is damage per *connecting* hit. Misses are counted in their own
  column and in the accuracy figure, never folded into the average.
- **DPS** is damage divided by the session clock — the time since the first
  counted hit, less any time you were paused. Every character uses that same clock, so
  the character column adds up to the party figure, and a character who joined
  late or died early reads lower because they contributed to less of the pull.
  It falls while nothing is happening, which is what makes it a measure of the
  pull rather than of your best moments in it.
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
- **Jobs** are the game's own, read from the party window rather than guessed
  from what someone swings: main and sub, as `SAM/WAR`, next to every character
  in the chips, the legend, the actions list and the character table (hover for
  levels). The addon reports a job when it first sees the party and again if
  someone changes, so a job change mid-session is picked up. Characters who are
  not in your party — a trust, a passing stranger's pet — have no job to show and
  read as `—`.
- **Counters, spikes and Retaliation are counted**, under those names, for
  whoever reacted. They are damage your side dealt to the monster even though
  the monster is the one who swung, so the addon files them the right way round
  before writing them. Reactions that dealt no damage are not recorded, so those
  rows always show 100% accuracy.

## When something looks wrong

The addon writes its own notices into the event file alongside the damage — its
startup probe, and one `"kind":"meta"` line for each kind of game message it saw
and did not recognise. The meter skips them, so when a total seems low, search
the file (under `%LOCALAPPDATA%\VibeXI\events\`) for `"meta"`. Anything listed
there is damage nobody is being credited with, and the fix is a new message id
in [`../../addons/VibeXI/vx_enums.lua`](../../addons/VibeXI/vx_enums.lua).

## Layout

```
damage-meter.py     tails the newest addon event file, serves web/ on localhost
winalpha.py         ctypes Win32 call that makes a pop-out window see-through
Damage-Meter.cmd    double-click launcher
../../addons/VibeXI/  the Ashita addon that produces the data
../../shared-ui/    design system shared with ../ws-calculator, served at /shared/
web/index.html      the page
web/style.css       app-only styling: filter bar, source indicator, pop-outs
web/app.js          polling, state, rendering
web/lib/source.js   addon JSONL lines -> damage events
web/lib/stats.js    events -> totals, time series, distributions
web/lib/chart.js    canvas line / bar / histogram
web/lib/popout.js   panels in their own windows
tools/gen-test-events.py   writes a synthetic event file, for working without the game
```

The palette and the shared widgets come from
[`../../shared-ui`](../../shared-ui) — the same stylesheet the weaponskill calculator
uses, so the two apps look like one toolset. The server mounts that directory at
`/shared/` rather than keeping a copy here, which means it must stay at the repo
root, two levels above this app. The **Light / Dark** button in the top right switches themes and
remembers the choice.

`CLAUDE.md` has the operational detail.
