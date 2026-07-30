# damage_meter — working notes

Live HorizonXI damage meter. **No Node, no Python, no build step** on this
machine — the server is PowerShell 5.1 and the front end is classic scripts.
`README.md` is the human-facing overview; this file is the operational detail.

## Shape

```
damage-meter.ps1    HttpListener on localhost + newest-log tailer
Damage-Meter.cmd    double-click launcher (powershell -ExecutionPolicy Bypass)
../shared-ui/       THE design system, shared with ../ws_calculator; mounted at /shared/
web/index.html      page shell; theme -> parser -> stats -> chart -> popout -> app
web/style.css       app-only rules: filter bar, source indicator, diagnostics, pop-outs
web/app.js          polling, filter state, all DOM writing
web/lib/parser.js   lines -> events        (DOM-free)
web/lib/stats.js    events -> aggregates   (DOM-free)
web/lib/chart.js    canvas line / bars / histogram
web/lib/popout.js   moves a card into its own OS window
```

## Hard rules

- **The server is dumb on purpose.** `/api/log?file=&offset=` returns raw log
  lines and nothing else. All parsing and aggregation happen in the browser, so
  a parse-rule change is an F5, not a restart. Do not move parsing into
  PowerShell.
- **`lib/parser.js` and `lib/stats.js` stay DOM-free**, same contract as
  `../ws_calculator`. They're what gets validated from the console; if a function
  needs a value it takes it as an argument.
- **Classic scripts on `window.DPS`, never ES modules.** Load order is
  `/shared/js/theme.js -> parser -> stats -> chart -> popout -> app`; `chart.js`
  reads `DPS.stats` at load time for the formatters, `app.js` takes its `$` from
  `DPS.popout`, and `app.js` needs `FFXITheme` for the light/dark toggle.
- **`$` is `DPS.popout.byId`, not `document.getElementById`.** A popped-out
  card's nodes live in another document, where `getElementById` cannot see them.
  Anything that reaches for an element by id must go through `$`.
- **The design system lives in `../shared-ui/`, not in `web/`.** `damage-meter.ps1`
  mounts that directory at the `/shared/` URL prefix so nothing is copied in —
  `Resolve-StaticPath` picks a root from the prefix and applies the same
  containment check to each. `web/style.css` loads *after* the shared sheet and
  holds only what `../ws_calculator` would never want. Before adding a rule, check
  whether the shared sheet already has the primitive (`.card`, `.tile`,
  `table.data`, `.chart-wrap`, `.segmented`, `.chip`, `button.ghost`), and if a
  rule in `style.css` starts looking generally useful, move it up rather than
  letting the other app grow a copy. `../shared-ui/README.md` is the vocabulary
  list.
- **No palette in the JS.** `chart.js`'s `theme()` and `seriesColor()` are
  one-line delegations to `FFXITheme`, which reads the live custom properties off
  `:root`. That is what lets one stylesheet drive both apps and both themes. The
  toggle, its label and its persistence are `FFXITheme.bind()`; the only
  app-specific part is that `onChange` must call `render()`, because canvas
  cannot restyle itself the way the DOM does.
- **Chat logs are Shift-JIS (CP932)**, not UTF-8. The auto-translate brackets
  are two-byte `0x81xx` sequences. Decoding as UTF-8 mangles every one of them.
- **Byte offsets, not line counts.** `Read-LogTail` trims back to the last
  newline and reports `nextOffset`, so a half-written line the game is still
  flushing is held for the next poll instead of being parsed as truncated. The
  client must carry `nextOffset` forward; never seek to EOF.
- **`FileShare` must include `Write` and `Delete`** or the running game gets
  blocked from writing its own log.

## The two facts that drive the parser

1. **A multi-sentence game message is written across multiple lines, and only
   the first carries a `[HH:MM:SS]` stamp.** So a weaponskill is
   `Hasaya uses Tachi: Jinpu.` on one line and
   `The Goblin Pathfinder takes 723 points of damage.` on the next, with the
   skill name never repeated. `state.pending` holds the announced action until
   its damage line lands; it survives ignored lines, is consumed or dropped by
   the next combat line, and expires after 8 s so a Meditate can never adopt an
   unrelated number.

   Consequence: `X uses Y.` alone is not enough to tell a weaponskill from a job
   ability — only what follows is. That is why nothing is emitted at the
   announcement, and why job abilities never appear at all.

2. **Nothing in the log states who is a player.** The article does: monsters are
   "the Goblin Pathfinder", characters never are. `roster.rebuild` seeds from
   that hard signal plus the log-filename owner, then runs a fixed point over
   who-fights-whom (attacking a monster makes you an ally, and vice versa) so
   article-less NMs like "Leaping Lizzy" still land correctly. It reruns over
   the *whole* event list after every poll, so a name classified late
   retroactively fixes earlier events. Manual overrides from the Diagnostics
   panel beat everything.

## Gotchas

- **`Skillchain: Fusion.` is eaten by the `Name: text` chat filter** unless it
  is exempted. `COMBAT_HINT` exists for exactly this; add to it before adding to
  `IGNORE`.
- **Chat is filtered before any damage rule runs.** A player typing "hit the
  crab for 9999 points of damage" must not become an event — there is a line
  like this in the synthetic test log specifically to catch a regression.
- **Test ranged before melee.** `(.+?) hits (.+?) for` happily matches
  `Hasaya's ranged attack hits …` with actor `Hasaya's ranged attack`.
- **A ranged miss is indistinguishable from a melee miss** — the log only says
  "Xatsh misses the Goblin". Those land in the `Attack` bucket, which is why a
  pure ranged attacker can show an `Attack` row with 0 hits and some misses.
  That is honest, not a bug.
- **Do not write literal control characters into a regex character class.**
  The strip-control-bytes regex in `feed()` must be written with `\xNN` escapes;
  writing the raw bytes makes the file binary and breaks the Edit tool.
- **`setup()` detaches both hover handlers, and every draw must go through it.**
  A handler installed by `line()` closes over that draw's data — `onmouseleave`
  repaints a saved `ImageData` snapshot of it. Leaving one attached across a
  draw with different data lets the old chart come back: reset cleared the
  canvas correctly, then the first mouse-out repainted the pre-reset series over
  the empty state. Clearing only `onmousemove` in the empty branches is not
  enough; it is the *leave* handler that redraws.
- **Colour slots are assigned once, from one shared pool**, allies first then
  monsters (`assignSlots`). Separate per-side pools were tried and are wrong —
  they hand a monster the same hue as a party member in "Both" mode.
- **Idle polls skip `render()`.** Rebuilding the tables once a second with no
  new data resets scroll position and kills text selection.
- **`renderChips` updates in place when the cast is unchanged.** Rebuilding the
  list on a mere toggle drops focus from the chip the user just activated. Only
  a changed set of names rebuilds the HTML. The same trap bites test code:
  after a rebuild a captured chip node is detached, so a second `.click()` on it
  goes nowhere and its computed style reports stale values — re-query between
  clicks.
- **`roster.articled` is sticky and never cleared.** `rebuild` re-derives
  everything else from the current event list, so without it a meter reset would
  briefly file every monster as a party member until each one was seen with its
  article again.

## Pop-out windows

Any card carrying `data-popout="key"` gets a **Pop out** and a **Keep in focus**
button in its head. `lib/popout.js` **moves the card's real DOM** into a child
window with `adoptNode` — it is never cloned, so `render()` keeps writing to the
same nodes and no part of the render path knows a panel is elsewhere. A
placeholder holds the card's slot in the page (and its grid cell) so docking back
lands in exactly the original position.

The consequences worth knowing:

- **`getElementById` only searches its own document.** `byId()` keeps an index of
  the ids currently outside the page and `app.js` uses it as `$`. Adding an id to
  markup inside a poppable card is fine; *creating* one at render time is not,
  because the index is built once on the way out.
- **A canvas drawn before the child's stylesheet lands measures zero**, same trap
  as `display:none`. `place()` renders once immediately and again after the
  copied `<link>`s fire `load`.
- **`setup()` reads `canvas.ownerDocument.defaultView.devicePixelRatio`**, not the
  main window's. A panel dragged to a second monitor has its own ratio.
- **Stylesheets are re-linked by resolved `href`.** The child is `about:blank`, so
  a relative `style.css` would resolve against nothing. The child gets the same
  two sheets and its own `data-theme`, which the theme toggle has to push out to
  it (`DPS.popout.theme`) — colours themselves still resolve off the main
  document's `:root`, so both windows always agree.
- **Closing a child window is docking.** `pagehide` handles it, and a 1 s poll of
  `win.closed` backs that up because the event is not guaranteed for a window
  closed from the OS chrome. Both paths check `p.win === win` first, so the old
  window's death during a mode change can't dock a panel that just moved.
- **The drill-down card hides itself.** A `MutationObserver` on `hidden` mirrors
  that onto the placeholder (no ghost box) and onto the child window (which shows
  an empty-state line instead of a blank page).
- **Only the module's own two buttons hide while a panel is away**, never the
  whole `.card-tools` — the drill-down's Close is in that group and has to stay
  usable in the child window.

**"Keep in focus" is Document Picture-in-Picture**, the only web API that yields
an always-on-top window; a plain `window.open` cannot be raised above other
applications from script. So it is Chromium-only (the button is disabled
elsewhere, with the reason in its `title`) and the browser allows exactly **one**
at a time — asking for a second closes the first, which arrives here as an
ordinary close and docks that panel back into the page. That is deliberate: the
alternative, reopening it as a normal window from a `pagehide` handler, is
outside a user gesture and gets blocked.

Neither window kind can be opened from an embedded webview — the Browser pane
blocks `window.open` and answers `requestWindow` with "no window" — so the
pop-out itself can only be exercised in a real browser. What *is* testable there
is everything downstream of the move; see the validation recipe below.

## Reset semantics

`resetMeter` drops the events and **keeps the read offset** — it is "clear the
meter between pulls", not a re-read; a page reload is what replays the file from
the top, and that is documented in the README rather than given its own button.

What survives a reset, and why:

| Kept | Reason |
|---|---|
| `app.slots` / `app.seen` | a character changing hue mid-session is worse than a stale entry |
| the whole roster (`articled`, `manual`, derived sets) | monsters must stay monsters across the reset |
| every filter (`range`, `side`, `actorsOff`) | user intent, not collected data |

`app.scanned` **must** be rewound to 0 alongside `parser.reset()`. It is a cursor
into the event list; leaving it past the now-empty list makes `scanActors` skip
every name until the list grows back past the old length.

## Charts

House style from the `dataviz` skill; the palette is its documented reference
instance (blue, orange, aqua, yellow, magenta, green, violet, red) with each
mode's own steps, already validated — **don't re-step it**. It now lives in
`../shared-ui/css/ffxi-theme.css` as `--series-1..8`, deliberately kept apart
from the blade/brass chrome tokens: series colours encode *data*, so they are not
folded into the app's identity even though everything around them was. Fixed
specs, not options: 2px lines, ≥8px markers with a 2px surface ring, hairline
solid gridlines, 2px surface gap between fills, categorical bars capped at 24px.

Chart height comes from the shared `.chart-wrap` rules, not from the drawing
code. The one exception is the bars chart, whose height is data-driven — `app.js`
sets `#barsWrap`'s inline height from the row count, which is meant to win over
the CSS.

One deliberate exception: **histogram columns are not capped at 24px.** A
histogram column's width is the bin interval — it is data, not a mark style — so
it fills its slot less the 2px gap. Capping it makes a distribution read as a
sparse categorical chart.

Past eight entities no ninth hue is generated: the tail goes muted and folds
into a single "Other" line.

## Validating a change

Browser-pane screenshots work against `http://localhost` (an earlier note here
said they didn't); it is `file://` pages that come back as static top-of-page
snapshots. Two more things that work:

```js
// 1. parse correctness, from the page console
DPS.app.parser.unparsed                       // must stay empty
Object.keys(DPS.app.parser.roster.mobs)
DPS.stats.aggregate(DPS.stats.filter(DPS.app.parser.events, {side:'ally', roster:DPS.app.parser.roster}))
DPS.parser.parseAll(['[10:00:00] A uses Tachi: Jinpu.', 'The B takes 700 points of damage.'])
```

```js
// 2. the pop-out path, without a real window. An iframe's contentWindow is a
//    genuine second Window/Document, so swapping it in for window.open exercises
//    adoptNode, the id index, the copied stylesheets and the canvas redraw --
//    everything except the OS window itself.
var f = document.createElement('iframe');
f.style.cssText = 'position:fixed;left:0;bottom:0;width:900px;height:620px;z-index:9999';
document.body.appendChild(f);
window.open = function () { return f.contentWindow; };
DPS.popout.place('line', 'window').then(function () {
  var d = DPS.popout.panels.line.win.document;
  console.log(document.getElementById('lineChart'),      // null -- it moved
              DPS.popout.byId('lineChart'),              // found via the index
              d.getElementById('lineChart').getBoundingClientRect());
});
```

```js
// 3. eyeball the charts -- composite the canvases and export
const cs = ['lineChart','barsChart','histChart'].map(i=>document.getElementById(i));
// draw them onto one canvas, toDataURL('image/png'), then decode the base64 to
// a .png with PowerShell and open it. This is the only way to actually look at
// the output here.
```

`tools/gen-test-log.ps1` writes a synthetic CP932 log (seeded, so it is
reproducible) — five characters, weaponskills, crits, skillchains, magic bursts,
ranged attacks, additional effects, an article-less NM, chat noise, and gaps
between fights so `Latest fight` has something to find. Point a second instance
at it:

```bash
powershell -ExecutionPolicy Bypass -File damage-meter.ps1 -Port 8732 -NoBrowser -LogDir <dir>
```

Checks that have caught real problems: append to a running log mid-line and
confirm the partial line is held back; drop a newer `.log` into the directory
and confirm the client resets to it; switch Party/Monsters/Both and confirm no
two entities share a colour slot and no entity's slot changes.

## Known gaps

- Only the newest log file is followed. Parsed events are not persisted —
  closing the page loses them, and reopening replays the current file from the
  top. Only the theme and the character exclusion list are stored
  (`ffxi_dps_theme`, `ffxi_dps_excluded` in localStorage).
- The exclusion list is keyed by bare name, so it is shared across log files and
  across both sides. That is intentional (a character you never want counted
  stays excluded), but it means excluding a monster named like a character would
  hide both.
- Pets are treated as ordinary allies with their own row, not folded into their
  master.
- Cure/heal, enfeeble and TP lines are not parsed; this is a damage meter only.
- A character with a single event has a zero-length active window, so their DPS
  shows as 0.
- Absorbed and "takes no damage" outcomes are treated as misses.
