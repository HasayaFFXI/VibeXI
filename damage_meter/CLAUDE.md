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

   This is now the *only* thing that decides whether damage is counted at all —
   see "Party damage only" below — so a misclassification is no longer a
   cosmetic sorting problem.

## Party damage only

This meter counts what the party dealt. Damage a monster dealt is never shown,
never totalled, and never charted. There is no Party/Monsters/Both switch.

**The monsters' events are still parsed, and must stay that way.** They are
dropped in `stats.filter`, on the actor side, and only when a `roster` is passed
(the second `filter` call in `render()` deliberately passes none — it is
re-filtering an already-scoped list by the character chips). Moving the drop into
`parser.js` looks tempting and breaks the roster: `rebuild`'s fixed point derives
"is a monster" from who fights whom, so an article-less NM is only identified
*because* its attacks on the party were parsed. Kill those events and Leaping
Lizzy joins the party.

Consequences worth knowing:

- **A character misfiled as a monster vanishes from the meter completely**,
  rather than showing up under a different tab. The Diagnostics roster table is
  the fix and its `card-sub` says so.
- **Monsters still appear as `target`s** — in the drill-down's per-hit table, and
  in the Diagnostics roster list. That is party damage *to* them, which is the
  whole point; only the actor side is filtered.
- **`windowOf` still resolves `Latest fight` over the unfiltered event list.** A
  fight's boundaries are a property of the combat, not of the display filter, and
  a pull where the monsters got the last word still ended when they did.

## Gotchas

- **`Skillchain: Fusion.` is eaten by the `Name: text` chat filter** unless it
  is exempted. `COMBAT_HINT` exists for exactly this; add to it before adding to
  `IGNORE`.
- **A skillchain is credited from `state.lastWS`, not `state.lastDamager`.**
  Those are the same name only in a solo log: in a party the other members' `X
  hits Y for N` lines land between the weaponskill and the `Skillchain:` line,
  so `lastDamager` hands the chain to whoever swung last. `push()` maintains
  `lastWS` alongside `lastDamager` and only `kind === 'ws'` events that actually
  landed damage update it — a weaponskill the mob evaded opened nothing.
  `lastDamager` is still the fallback past `CHAIN_MS` (10 s), because a
  weaponskill that old did not open this chain either.
- **The skillchain toggle is a `stats.filter` option, not a parse rule.** By the
  time the user flips it the lines are long gone and only the events remain, so
  turning it off has to be a re-render (drop `kind === 'skillchain'`), never a
  re-parse. Same reason the parser always emits the events regardless.
- **Chat is filtered before any damage rule runs.** A player typing "hit the
  crab for 9999 points of damage" must not become an event — there is a line
  like this in the synthetic test log specifically to catch a regression.
- **Test ranged before melee.** `(.+?) hits (.+?) for` happily matches
  `Hasaya's ranged attack hits …` with actor `Hasaya's ranged attack`.
- **Ranged crits have three separate phrasings and none reach `RE.ranged`.**
  `hits X squarely for N` and `strikes true, pummeling X for N` both end in `!`
  rather than `.`, and the second has no "hits" at all — hence `RE.rngCrit`.
  They are counted as plain ranged damage, *not* flagged `crit`, because the
  wording is the only tell. The third, `Dags's ranged attack scores a critical
  hit!`, does flag the crit but names the *attack*; `unranged()` pulls the
  attacker back out of it, or the damage on the next line lands under an actor
  literally called "Dags's ranged attack".
- **`Additional effect:` has a named-target form too** — `Additional effect: X
  takes N additional points of damage.` The word "additional" in front of
  "points" is what keeps it out of `RE.takes`. Both forms credit
  `state.lastDamager` and share the one `Additional Effect` action row.
- **An AoE outlives its own `pending` slot.** Only the first victim is written as
  a continuation of `X casts Meteor.`; the other victims arrive seconds later as
  their own stamped lines, and any melee swing in between clears `pending`. So a
  resolved announcement is copied into `state.aoe` and stays eligible for
  further `takes` lines for `AOE_MS` (5 s). `aoe.hit` records who it already
  covered, because one cast never hits the same target twice and a repeated line
  is a DoT tick, not splash.
- **The AoE echo needs `couldStrike`, or it is worse than the guess it replaces.**
  Unguarded, a party nuke that just resolved adopts the *monster's* AoE damage on
  the party and credits it to the nuker. `state.foes` is a parse-time sketch of
  who fights whom, fed only by attributions actually read off a line; two names
  that ever fought the same third party are on one side and cannot damage each
  other. It exists because `roster` answers this far better but only after
  `rebuild` runs at the end of a poll — on first load every line is fed before
  the first rebuild, which is exactly when the echo needs an answer. `foes`
  survives a meter reset for the same reason `roster.articled` does.
- **The lastDamager fallback sets `guess: true`, and `roster.rebuild` skips those
  events.** The actor on an `Unattributed` row was never read off a line, and the
  propagation pass cannot tell that. One mis-credited "ally hits ally" marks the
  victim a monster, everyone the victim fights becomes an ally, and the boss ends
  up in the party list — this is not hypothetical, it is what a stray Meteor
  splash line did to a Promathia log. Articles are still honoured on guessed
  events: those come from the text, not the guess.
- **`X's attack is countered by Y.` is the one message this client sometimes puts
  on a single line with its damage sentence.** So `RE.counter` peels like a
  prefix rather than matching to end-of-line, and both the combined and the split
  form fall through to `RE.takes`. Without it the whole line matches `RE.takes`
  and the target becomes the literal string `"Promathia's attack is countered by
  Hasaya. Promathia"` — a fake entity that then enters the roster. Note the
  counterer is named *second*; the damage is theirs.
- **Spikes must not consume `state.pending`.** `X's spikes deal N points of
  damage to Y` fires in reaction to someone else's swing, so unlike every other
  direct-damage form it leaves the pending announcement alone — the weaponskill
  it interrupts is still waiting for its own damage line.
- **A ranged miss is indistinguishable from a melee miss** — the log only says
  "Xatsh misses the Goblin". Those land in the `Attack` bucket, which is why a
  pure ranged attacker can show an `Attack` row with 0 hits and some misses.
  That is honest, not a bug.
- **Do not write literal control characters anywhere in the source** — not in a
  regex character class, not in a string literal. Use `\xNN` / `\0` escapes. A
  raw byte makes the file read as *binary*: `grep` answers "Binary file matches"
  instead of showing the line, and the Edit tool cannot match around it. Two
  places invite it: the strip-control-bytes regex in `parser.js`'s `feed()`, and
  `renderChips`'s `names.join('\0')` in `app.js`, which wants NUL as a separator
  no character name can contain. Both are escapes now; keep them that way.
- **`setup()` detaches both hover handlers, and every draw must go through it.**
  A handler installed by `line()` closes over that draw's data — `onmouseleave`
  repaints a saved `ImageData` snapshot of it. Leaving one attached across a
  draw with different data lets the old chart come back: reset cleared the
  canvas correctly, then the first mouse-out repainted the pre-reset series over
  the empty state. Clearing only `onmousemove` in the empty branches is not
  enough; it is the *leave* handler that redraws.
- **Colour slots are assigned once and only to characters** (`assignSlots`).
  Monsters are skipped: they can never be an actor, so slotting one would push a
  real party member into the muted tail for nothing. `app.seen` still records
  every name, because classification arrives late — a name reclassified into the
  party (manually, or once its relationships resolve) picks up the next free slot
  on the following render, and a manual flip runs `resetColors()` anyway.
- **Idle polls skip `render()`.** Rebuilding the tables once a second with no
  new data resets scroll position and kills text selection.
- **The filter bar is two stacked rows, and the character list owns the second
  one.** An 18-name alliance cannot share a line with the segmented controls —
  given a shared row the chips get a narrow column and grow downwards with every
  extra character. The chips box is capped at 92px and scrolls past that, so a
  narrow window can never push the (sticky) bar over the page, and `Hide`
  collapses the row to one line; the label keeps its `n/m` count and
  `#charHint` names who is excluded so the state is never invisible.
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

Any card carrying `data-popout="key"` gets a **Keep in focus** button in its
head, and that is the only one — there is no "Pop out". `lib/popout.js`
**moves the card's real DOM** into a child window with `adoptNode` — it is never
cloned, so `render()` keeps writing to the same nodes and no part of the render
path knows a panel is elsewhere. A placeholder holds the card's slot in the page
(and its grid cell) so docking back lands in exactly the original position.

**`'window'` mode still exists; nothing in the UI reaches it.** A plain
`window.open` panel cannot be raised above other applications, which is the only
reason to detach a panel here, so the button went. The mode stays because two
things need it: `place()` degrades a `'focus'` request to it where PiP is
missing, and it is the only way to exercise the move machinery in an embedded
webview (the iframe recipe below). If you re-add a button for it, note that the
focus toggle currently goes straight back to `'docked'` when switched off —
with two modes in play that has to become a three-way again.

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
- **Only the module's own button hides while a panel is away**, never the whole
  `.card-tools` — the drill-down's Close is in that group and has to stay usable
  in the child window.
- **The placeholder carries only "Bring back".** It used to hold a second "Keep
  in focus" toggle, for the case of promoting an already-popped-out window to
  always-on-top; with `'window'` mode off the menu that case cannot arise, and
  the button would have been a differently-worded duplicate of "Bring back".

### Seeing through a pop-out window

The panels are an overlay for the game, so a floating window has to be seen
through — and **a page cannot do that to the window it lives in.** CSS `opacity`
fades the document against the *browser's* backdrop, never onto the desktop; the
game is not behind the document, it is behind the window. Worse, `opacity` on
`body` does not even fade the page background, because a body background
propagates to the window canvas and is painted outside the faded layer. That
combination — contents ghosted, background solid — is what the first cut of this
shipped, and it reads exactly like a broken slider.

Only the OS can do it, so `GET /api/alpha` does, with two effects:

| | what it does | control |
|---|---|---|
| `LWA_ALPHA` | whole window translucent, chrome and background included | the slider, 15–100% |
| `LWA_COLORKEY` | pixels of exactly `#010203` dropped entirely | the **BG** button, on by default |

The punch-out is the one that answers "I want to see the game, not a dimmer
panel": `.key-bg .pop-doc` paints the background that exact colour, the window
manager drops those pixels, and since `chart.js` draws on a *cleared* canvas what
survives over the game is lines, numbers and the 20px bar. Its side effect is
that the punched-out area is **click-through** — good for an overlay, but the
window can then only be dragged by its own title bar.

Things that will bite:

- **The window is matched by where it is, not by its title.** A Document PiP
  window's caption belongs to Chrome, not to the page, so `document.title` is not
  reliably on it. The client sends the centre of its own window plus its size and
  `devicePixelRatio`; the server does `WindowFromPoint` → `GetAncestor(GA_ROOT)`.
  Title and "the topmost browser window" remain as second and third tries.
- **The size check on that point is load-bearing.** This machine runs a 150%
  desktop and the server process is DPI-*unaware*, so it sees virtualised
  coordinates while the client measures CSS pixels — the two candidate points
  (scaled and raw) disagree, and on a scaled desktop the wrong one still lands on
  *something*, quite possibly the game. Only the size comparison tells them
  apart. Never take a point hit without it.
- **`Apply` refuses the shell** (`Progman`, `WorkerW`, `Shell_TrayWnd`, …). A
  point is always over *some* window.
- **The em dash makes the query non-ASCII.** The handler parses it with
  `HttpUtility::ParseQueryString(..., $Utf8)` rather than `$req.QueryString`,
  which does not reliably decode it.
- **An older server instance still running has no `/api/alpha`.** It 404s and the
  client concedes to the fade — which is why the fade is now labelled **"fade
  only"** in the window bar with the reason in its tooltip, and why it drops the
  background rather than leaving the un-fadeable canvas behind it. A silent
  half-fade is indistinguishable from a bug.
- **Never paint `#010203` unless the server confirmed the key.** `osAlpha()`
  toggles `key-bg` off on failure; left on without the punch-out it is simply a
  near-black window.
- State lives in `ffxi_dps_alpha` (`key -> 15..100`) and `ffxi_dps_keybg`
  (`key -> bool`); `DPS.popout.alpha(key[, v])` is the console handle, and the
  response carries `method` and `window` so a wrong match is diagnosable.
- **Verified: the server end applies and reads back** (`alpha`, `flags=2`,
  ex-style gains `0x80000`) against a real Chrome window, and every client branch
  is verified against a stubbed endpoint. **Not verified: how Chrome composites a
  layered PiP window** — that needs a real always-on-top window, which the
  Browser pane cannot produce. If one renders black, that is the GPU compositor:
  turn **BG** off, and 100% on the slider undoes the rest.

**"Keep in focus" is Document Picture-in-Picture**, the only web API that yields
an always-on-top window; a plain `window.open` cannot be raised above other
applications from script. So it is Chromium-only (the button is disabled
elsewhere, with the reason in its `title`) and the browser allows exactly **one**
at a time — asking for a second closes the first, which arrives here as an
ordinary close and docks that panel back into the page. That is deliberate: the
alternative, reopening it as a normal window from a `pagehide` handler, is
outside a user gesture and gets blocked.

Neither window kind can be opened from an embedded webview — the Browser pane
blocks `window.open` and answers `requestWindow` with "no window" — so a real
floating window can only be seen in a real browser. Everything downstream of the
move *is* exercisable there by standing an iframe in for both constructors; see
recipe 2 in the validation section, and read its two warnings before assuming a
silent no-op means the code is broken.

## Reset semantics

`resetMeter` drops the events and **keeps the read offset** — it is "clear the
meter between pulls", not a re-read; a page reload is what replays the file from
the top, and that is documented in the README rather than given its own button.

What survives a reset, and why:

| Kept | Reason |
|---|---|
| `app.slots` / `app.seen` | a character changing hue mid-session is worse than a stale entry |
| the whole roster (`articled`, `manual`, derived sets) | monsters must stay monsters across the reset |
| every filter (`range`, `chains`, `actorsOff`) | user intent, not collected data |

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
DPS.stats.aggregate(DPS.stats.filter(DPS.app.parser.events, {roster:DPS.app.parser.roster}))
// no roster == no monster filtering, so this is the check that the drop works:
// every name the first call is missing must be in roster.mobs, and nothing else.
DPS.stats.aggregate(DPS.stats.filter(DPS.app.parser.events, {})).actors.map(a=>a.name)
DPS.parser.parseAll(['[10:00:00] A uses Tachi: Jinpu.', 'The B takes 700 points of damage.'])
```

```js
// 2. the pop-out path, without a real window. An iframe's contentWindow is a
//    genuine second Window/Document, so standing it in for the two window
//    constructors exercises adoptNode, the id index, the copied stylesheets and
//    the canvas redraw -- everything except the OS window itself.
//
//    Two things this recipe used to get wrong, both learned the hard way:
//      - `window.open = fn` does NOT stick in the Browser pane even though the
//        property reports writable. Object.defineProperty does.
//      - stub, click and read must be ONE javascript_tool call. The pane
//        reloads the page between calls, which restores the native constructor
//        and drops the iframe -- the symptom is a click that silently does
//        nothing and leaves mode at 'docked'.
//    Stub documentPictureInPicture too, or only 'window' mode is reachable and
//    the button no longer opens that.
var f = document.createElement('iframe');
f.style.cssText = 'position:fixed;left:0;bottom:0;width:900px;height:620px;z-index:9999';
document.body.appendChild(f);
Object.defineProperty(window, 'open', {
  configurable: true, writable: true, value: function () { return f.contentWindow; } });
Object.defineProperty(window, 'documentPictureInPicture', {
  configurable: true, writable: true,
  value: { requestWindow: function () { return Promise.resolve(f.contentWindow); } } });

var p = DPS.popout.panels.line;
p.focusBtn.click();                       // out: docked -> focus
setTimeout(function () {
  var d = p.win.document;
  console.log(p.mode,                                    // 'focus'
              document.getElementById('lineChart'),      // null -- it moved
              DPS.popout.byId('lineChart'),              // found via the index
              d.getElementById('lineChart').getBoundingClientRect());  // non-zero
  p.focusBtn.click();                     // back: focus -> docked, NOT 'window'
  setTimeout(function () { console.log(p.mode, !!document.getElementById('lineChart')); }, 400);
}, 500);
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
between fights so `Latest fight` has something to find. Most of its skillchains
have another character's melee hit deliberately spliced in between the
weaponskill and the `Skillchain:` line — that is the case that catches
`lastDamager` attribution, and it is worth keeping when the generator changes.
Point a second instance at it (this is the `damage-meter-test` entry in
`../.claude/launch.json`, port 8732):

```bash
powershell -ExecutionPolicy Bypass -File damage-meter.ps1 -Port 8732 -NoBrowser -LogDir <dir>
```

Checks that have caught real problems: append to a running log mid-line and
confirm the partial line is held back; drop a newer `.log` into the directory
and confirm the client resets to it; confirm no monster name reaches the
character chips, the bars chart or the actions table (its NM, `Leaping Lizzy`,
is the one that tests the relationship pass rather than the article); flip a
character to `Monster` in Diagnostics and confirm they leave every total, then
flip back and confirm no surviving character's colour changed.

## Known gaps

- Only the newest log file is followed. Parsed events are not persisted —
  closing the page loses them, and reopening replays the current file from the
  top. Only the theme, the character exclusion list, the skillchain toggle and
  whether the character row is collapsed are stored (`ffxi_dps_theme`,
  `ffxi_dps_excluded`, `ffxi_dps_chains`, `ffxi_dps_charrow` in localStorage).
- The exclusion list is keyed by bare name, so it is shared across log files.
  That is intentional: a character you never want counted stays excluded.
- Pets are treated as ordinary allies with their own row, not folded into their
  master.
- Cure/heal, enfeeble and TP lines are not parsed; this is a damage meter only.
- A character with a single event has a zero-length active window, so their DPS
  shows as 0.
- Absorbed and "takes no damage" outcomes are treated as misses.
