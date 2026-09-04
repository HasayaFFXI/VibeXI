# damage_meter — working notes

Live HorizonXI damage meter. **No Node, no build step, nothing to pip install**
— the server is a stdlib-only Python script and the front end is classic
scripts. `README.md` is the human-facing overview; this file is the operational
detail. `PACKAGING.md` covers turning the server into an `.exe`.

## The one source is the addon

`../addons/VibeXI/` reads the game's own action packets and appends one JSON
object per line to `%LOCALAPPDATA%\VibeXI\events\<Character>_<date>.jsonl`.
That file is the only input this app has.

**There is no chat-log reader and there must not be a second one.** The log
parser was deleted on 2026-09-04, once the addon was approved; `web/lib/parser.js`
and the CP932 tailer went with it, along with the article heuristic, the
who-fights-whom fixed point, the five-second AoE echo, the `lastDamager` guess
and every `guess: true` event. All of those existed to recover facts the packet
states outright, and keeping a second source that disagrees with the first is
worse than having one that is right. If historical chat logs ever need reading
again, that is a separate tool, not a fallback branch in here.

What the packet gives that the text could not:

| | The log said | The packet says |
|---|---|---|
| who is a player | nothing — inferred from "the" plus a fixed point | `spawn_flags`, per entity |
| AoE grouping | one line per victim, grouped by a 5 s window | the target list, in the packet |
| multi-attack | one line per swing, indistinguishable from separate rounds | results nested under a target |
| crits | a turn of phrase, three of them for ranged | a message id |
| miss vs parry vs shadow | mostly indistinguishable | distinct message ids |
| pets | an ordinary name | `pet_index`, so the owner is named |
| WS vs job ability | undecidable at the announcement | the packet category |
| skillchains | credited by guessing who swung last | attached to the closing weaponskill |

## Shape

```
damage-meter.py     http.server on 127.0.0.1 + newest-event-file tailer
winalpha.py         ctypes user32: the layered-window alpha behind /api/alpha
Damage-Meter.cmd    double-click launcher (python damage-meter.py)
../addons/VibeXI/   the addon that produces every event this app draws
../shared-ui/       THE design system, shared with ../ws_calculator; mounted at /shared/
web/index.html      page shell; theme -> source -> stats -> chart -> popout -> app
web/style.css       app-only rules: filter bar, source indicator, diagnostics, pop-outs
web/app.js          polling, filter state, all DOM writing
web/lib/source.js   JSONL lines -> events   (DOM-free)
web/lib/stats.js    events -> aggregates    (DOM-free)
web/lib/chart.js    canvas line / bars / histogram
web/lib/popout.js   moves a card into its own OS window
tools/gen-test-events.py   synthetic event file, for working with no game running
```

## Hard rules

- **The server is dumb on purpose.** `/api/events?file=&offset=` returns raw
  lines and nothing else. All interpretation and aggregation happen in the
  browser, so a change there is an F5, not a restart. Do not move any of it into
  the server.
- **`lib/source.js` and `lib/stats.js` stay DOM-free**, same contract as
  `../ws_calculator`. They're what gets validated from the console; if a function
  needs a value it takes it as an argument.
- **Classic scripts on `window.DPS`, never ES modules.** Load order is
  `/shared/js/theme.js -> source -> stats -> chart -> popout -> app`; `chart.js`
  reads `DPS.stats` at load time for the formatters, `app.js` takes its `$` from
  `DPS.popout`, and `app.js` needs `FFXITheme` for the light/dark toggle.
- **`$` is `DPS.popout.byId`, not `document.getElementById`.** A popped-out
  card's nodes live in another document, where `getElementById` cannot see them.
  Anything that reaches for an element by id must go through `$`.
- **The design system lives in `../shared-ui/`, not in `web/`.** `damage-meter.py`
  mounts that directory at the `/shared/` URL prefix so nothing is copied in —
  `resolve_static_path` picks a root from the prefix and applies the same
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
- **The event file is ASCII, and that is the addon's job.** `vx_emit.lua` escapes
  every byte >= 0x7F as `\uXXXX`, so the bytes decode identically as ASCII,
  UTF-8 or CP932. The server decodes UTF-8. Do not add encoding negotiation to
  this path; fix the emitter if a name ever arrives mangled.
- **Byte offsets, not line counts.** `read_tail` trims back to the last newline
  and reports `nextOffset`, so a half-written line is held for the next poll
  instead of being parsed as truncated. This is what makes the addon's
  flush-per-event safe — half a JSON object is not parseable. The client must
  carry `nextOffset` forward; never seek to EOF.
- **The file must be opened sharing `Write` and `Delete`.** The addon writes it
  from inside the game process, on the game's thread, and must never be blocked
  by this tool. Python's `open()` shares read and write but *not* delete, so
  `_open_shared` goes through `CreateFileW` with all three and falls back to
  `open()` only off Windows.

## The event contract

One line, one `(action, target, result)` row:

```json
{"t":1785000000,"seq":3,"use":41207,"kind":"ws","actor":"Hasaya","actorKind":"player",
 "action":"Tachi: Jinpu","actionId":32,"target":"Goblin Pathfinder","targetKind":"mob",
 "dmg":723,"hit":true,"crit":false,"burst":false,"msg":185}
```

`kind` is one of `melee ranged ws magic ability mobtp pet skillchain addl`.
`msg` is the raw game message id, carried on every event so a later phase can
add outcomes without changing the wire format. `owner` and `pet` appear only on
a pet's own rows.

**`t` is SECONDS, and `source.js` scales it to milliseconds exactly once.** The
addon does not link LuaSocket purely to get a finer clock (`addon-dev/PLAN.md`,
Decision 1), so the wire clock is `os.time()` with `seq` ordering events inside
one second. Nothing in the UI resolves finer than a second. Everything
downstream of `feed()` is in milliseconds; do not scale it twice.

**A `kind:"meta"` line is not an event.** Two kinds arrive: the addon's startup
environment probe, once per session, and one notice per game message id it saw
and did not recognise. `source.js` routes both into `meta`, which is what the
Diagnostics panel prints. An unrecognised id is damage nobody is being credited
with, so that panel is the first place to look when a total seems low; the fix
is a new entry in `../addons/VibeXI/vx_enums.lua`, not here.

## `use` is per swing, not per action

This is the one thing about the contract that is easy to get wrong, and it was
wrong once. The packet has two dimensions and they do not mean the same thing:

- **Several targets, one result each** — an AoE. One use of the action. The
  packet carries the target list, so this is stated rather than inferred.
- **One target, several results** — a multi-attack round. Two or three genuine
  swings, each with its own hit-or-miss outcome.

`record()` in `vibexi.lua` keys the id on the result's *position*
(`use_for(slot)`), so result 1 across every target shares one id and result 2
takes the next. Minting a single id for the whole action folds the second case
into the first, and since `stats.collapse` treats `hit` as "any", a round that
landed once and whiffed once then reports **one hit and no miss**. Measured on
the test fixture that read as 93.7% accuracy against a true 89.7% — the swing
count is the denominator, so this shows up as a party that never misses.

`stats.collapse` folds the rows sharing a use back together: damage sums,
`hit`/`crit`/`burst` are "any", and a multi-target use reports its target as
`"3 targets"`. `aggregate` and `distribution` both collapse first; `cumulative`
deliberately does not, because it only sums into time bins and the total is
identical either way.

**The invariant to re-check after any change either side of the bridge:
collapsing must never move damage, only counts.**

## Skillchains and additional effects ride on the proc trailer

Neither is a packet or a message of its own. Both arrive in a result's optional
proc trailer — `proc_message` plus `proc_value` — on the weaponskill or swing
that caused them, and `vibexi.lua` reads that trailer **outside** the branch that
handles the main message, so a chain is never lost because its weaponskill's own
message happened to be unrecognised.

- **Metrics is the source of truth for the skillchain ids.** `E.Skillchains` in
  `vx_enums.lua` is `Res.WS.Skillchains` from Metrics'
  `resources/weapon_skills_curated.lua`, copied verbatim, and `E.skillchain()`
  is its `Res.WS.Get_Skillchain` — a lookup, nothing more.

  **Do not derive the ids arithmetically.** An earlier cut computed them as
  `287 + effect` / `384 + effect` from the LandSandBoat server's
  `action_result_t::recordSkillchain`. That formula disagrees with Metrics twice
  — Radiance and Umbra at 302/303 rather than 767/768, and the 385/386 pair read
  as "absorbed" rather than as Light and Darkness — and Metrics is the parser
  with a track record against this server. Reconciling them is a measurement
  against a live client, not a re-reading of either source.
- **The table is consulted only when `kind == 'ws'`.** That is the one context
  Metrics consults it in: `H.TP.Skillchain_Parse` is called from `H.TP.Action`
  and nowhere else (`handlers/tp_action.lua:41`).

  This is load-bearing, not tidiness. **229 is in two tables at once**: on a
  weaponskill Metrics calls it 'DRG Jump Effect', and everywhere else it reads
  the same trailer as `Message.ENSPELL` and files it as an additional effect.
  Look the table up globally and every enspell proc in the game becomes a
  skillchain row.
- **Do not read `proc_kind` on its own to identify a chain.** It is a variant
  (add-effect OR skillchain), so only the message says which.
- **The chain's damage is `proc_value`, never `res.value`** — it is its own
  damage and is not part of the weaponskill's. Metrics'
  `H.TP.Skillchain_Damage` reads `add_effect_param` for the same reason.
- **Both get their own `use`, minted once per action.** One weaponskill closes
  one chain however many targets or swings it involved. Sharing the
  weaponskill's id would let `collapse` fold the chain's damage into the
  weaponskill and lose it as a row.
- The action name is `'Skillchain: Fusion'` — the same string the chat parser
  produced, and what `app.js`'s `/^Skillchain:/` drill-down test matches.

## Party damage only

This meter counts what your side dealt. Damage a monster dealt is never shown,
never totalled, and never charted. There is no Party/Monsters/Both switch.

**The monsters' events are still parsed and kept.** They are dropped in
`stats.filter`, on the actor side, and only when a `roster` is passed (the second
`filter` call in `render()` deliberately passes none — it is re-filtering an
already-scoped list by the character chips). They stay in the event list because
the Diagnostics roster is built from it, and because a manual override has to be
able to bring a name back without a re-read.

`roster.isMob` is now a lookup: `actorKind`/`targetKind` in
(`player`, `pet`) is ours, anything else is not. **Anything not positively ours
is treated as a monster**, so an entity the addon could not resolve — it arrives
as `Unknown`/`other` — is left out of the totals rather than silently added to
them. Under-counting a stranger beats crediting one.

Consequences worth knowing:

- **A name misfiled vanishes from the meter completely** rather than showing up
  under a different tab. The Diagnostics roster table is the fix and its
  `card-sub` says so. With spawn flags this should never happen; the override
  survives because a classification the user disagrees with should still be
  theirs to fix.
- **Monsters still appear as `target`s** — in the drill-down's per-hit table and
  in the Diagnostics roster list. That is party damage *to* them, which is the
  whole point; only the actor side is filtered.
- **Pets count as ours** and get their own row under their own name. `owner`
  names the master but nothing folds a pet into it yet.
- **`windowOf` still resolves `Latest fight` over the unfiltered event list.** A
  fight's boundaries are a property of the combat, not of the display filter, and
  a pull where the monsters got the last word still ended when they did.

## Gotchas

- **Do not write literal control characters anywhere in the source** — not in a
  regex character class, not in a string literal. Use `\xNN` / `\0` escapes. A
  raw byte makes the file read as *binary*: `grep` answers "Binary file matches"
  instead of showing the line, and the Edit tool cannot match around it. The
  place that invites it is `renderChips`'s `names.join('\0')` in `app.js`, which
  wants NUL as a separator no character name can contain. It is an escape now;
  keep it that way.
- **`setup()` detaches both hover handlers, and every draw must go through it.**
  A handler installed by `line()` closes over that draw's data — `onmouseleave`
  repaints a saved `ImageData` snapshot of it. Leaving one attached across a
  draw with different data lets the old chart come back: reset cleared the
  canvas correctly, then the first mouse-out repainted the pre-reset series over
  the empty state. Clearing only `onmousemove` in the empty branches is not
  enough; it is the *leave* handler that redraws.
- **Colour slots are assigned once and only to names the meter counts**
  (`assignSlots`). Monsters are skipped: they can never be an actor, so slotting
  one would push a real party member further down the palette for nothing.
  `app.seen` still records every name, so a name flipped into the party by hand
  picks up the next free slot on the following render — and a manual flip runs
  `resetColors()` anyway.
- **The file's owner is pinned to slot 0, before the first-seen loop runs.**
  Otherwise their hue is decided by whether they or the tank swung first, which
  is luck — and the owner is the one character on every chart of every session,
  so theirs is the hue that must not drift. `roster.owner` comes from the
  filename, so it is known before any line is read.
- **Idle polls skip `render()`.** Rebuilding the tables with no new data resets
  scroll position and kills text selection.
- **`POLL_MS` is 250, but the browser clamps it to 1000 whenever the tab is
  hidden.** That is a Chrome timer policy, not a bug here, and it cannot be
  worked around from the page. Consequences: a minimised or background-tab meter
  silently reverts to a 1 s cadence, and any attempt to measure the poll rate
  from a non-visible tab reads ~1000 ms no matter what `POLL_MS` says
  (`document.visibilityState` is the thing to check before believing a
  measurement). A "Keep in focus" Document PiP panel is always visible, so it
  runs unclamped — which is the mode that actually wants the low latency.
- **The poll path is O(events) and runs at poll rate** — `filter` and `aggregate`
  twice each, plus `cumulative`. `roster.rebuild` is gone, which was the most
  expensive thing in it (32 ms on a 200k-event session) and the reason
  `REBUILD_MS` existed; classification is now a per-line lookup done during
  `feed()`. Polls cannot pile up (`schedule()` is called from the final
  `.then()`, so the next one is queued only after the current finishes), but a
  very long grinding session will still get warm. If that bites, the fix is
  incremental aggregation rather than a slower poll — and the cheapest single win
  is that `render()` computes `S.aggregate` twice, once only to get the chip
  names.
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
- **A regenerated fixture does not reset a page that is already polling.** The
  server only signals `reset` when the file got *shorter* than the client's
  offset; rewrite it to a similar length and the client happily reads the few
  trailing bytes as new events on top of the old ones. Reload the page after
  running the generator, and do not trust a number measured without doing so.

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
| the whole roster (`kinds`, `manual`) | monsters must stay monsters across the reset |
| every filter (`range`, `chains`, `actorsOff`) | user intent, not collected data |

`app.scanned` **must** be rewound to 0 alongside `source.reset()`. It is a cursor
into the event list; leaving it past the now-empty list makes `scanActors` skip
every name until the list grows back past the old length.

## Charts

House style from the `dataviz` skill; the palette is its documented reference
instance (blue, orange, aqua, yellow, magenta, green, violet, red) with each
mode's own steps, already validated — **don't re-step it**. It now lives in
`../shared-ui/css/ffxi-theme.css` as `--series-1..18`, deliberately kept apart
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

### Eighteen slots, one per character

**Every character gets their own colour — there is no muted tail and no "Other"
line.** An alliance is 18 characters, so the palette is 18 slots
(`FFXITheme.SLOTS`), and `colorOf` is a straight `FFXITheme.series(slotOf(name))`.

This is knowingly past where colour alone works, and the `dataviz` skill's own
rule is that a 9th series folds into "Other" rather than getting a hue. What
makes it defensible here is that **the meter never identifies a character by
colour alone**: the legend names all of them, the line table gives each a
column, the bars chart and the actions table are labelled rows, and hover names
the series. Colour is the cross-panel *link* between those, not the label.

Slots 9–18 were solved, not picked — a max-min search over OKLCH maximising the
worst OKLab ΔE across normal, protan and deutan vision, per mode, against its
surface. The bar they had to clear, and do: **no pair involving a new slot is
weaker than the weakest pair that was already inside slots 1–8** (dark 7.4 vs
5.8 for the shipped blue/violet; light 6.4 vs 3.9 for the shipped orange/red).
Slot *order* is solved too, so consecutive slots — the order characters are
handed colours in — stay far apart: worst adjacent pair 14.1 dark, 12.0 light,
both of which are shipped pairs, not new ones. All 18 sit inside each mode's
lightness band, above the chroma floor, at ≥3:1 against the card.

Two consequences:

- **`--text-muted` never existed.** `colorOf` used to return it for the tail, so
  every unslotted name got the empty string — invisible only because the tail
  collapsed into one line that had its colour overwritten anyway. If a slot
  lookup ever goes wrong again, an empty `background` is the symptom to look for.
- **Past 18 the palette wraps** and two characters share a hue. That needs an
  alliance plus pets, and the legend still tells them apart.

The validator ships with the `dataviz` skill. There is no Node on this machine
but there is Python, so it can be run directly; the maths is also portable enough
to paste the conversions (`lin`/`oklab`/Machado simulate/ΔE) into a
`javascript_tool` call against a page in the Browser pane and score the palette
there. Note that a `javascript_tool` call reloads the page first, so globals do
not survive between calls — each call has to be self-contained.

## Validating a change

`tools/gen-test-events.py` writes a synthetic event file — seeded, so it is
reproducible — carrying every case worth regression-testing: multi-attack rounds,
AoE nukes sharing one `use`, skillchains across both of Metrics' id ranges, additional
effects, a pet with an owner, an NPC, an article-less NM, an unresolved
`Unknown` target, monster damage on the party, both kinds of meta line, and gaps
between fights so `Latest fight` has something to find. Its docstring says what
each case catches; read that before changing it.

**It must stay field-for-field identical to `vx_emit.encode`.** If the fixture
and the emitter disagree the fixture is worthless, so that function is the thing
to diff against when either changes.

```bash
python damage_meter/tools/gen-test-events.py
python damage_meter/damage-meter.py --port 8732 --no-browser --events-dir damage_meter/tools/events
```

That is the `damage-meter-fixture` entry in `../.claude/launch.json`, port 8732.
**Reload the page after regenerating** — see the last gotcha above.

Browser-pane screenshots work against `http://localhost`; it is `file://` pages
that come back as static top-of-page snapshots. From the page console:

```js
// 1. the source contract
DPS.app.source.meta                       // probe + unrecognised message ids
DPS.app.source.state.bad                  // lines that were not JSON at all
DPS.app.source.roster.kinds               // name -> player | pet | mob | npc | other
DPS.source.parseAll(['{"t":1,"use":1,"kind":"ws","actor":"A","actorKind":"player",' +
  '"action":"Tachi: Jinpu","target":"B","targetKind":"mob","dmg":700,"hit":true}']).events

// 2. monster filtering. No roster == no filtering, so this is the check that the
//    drop works: every name the first call is missing must be a non-player kind.
var R = DPS.app.source.roster, E = DPS.app.source.events;
DPS.stats.aggregate(DPS.stats.filter(E, {roster: R})).actors.map(a => a.name)
DPS.stats.aggregate(DPS.stats.filter(E, {})).actors.map(a => a.name)

// 3. THE invariant: collapsing moves counts, never damage.
var s = DPS.stats.filter(E, {roster: R}), sum = a => a.reduce((n,e) => n+(e.hit?e.dmg:0), 0);
sum(DPS.stats.collapse(s)) === sum(s)

// 4. the two dimensions stay apart: AoE folds, multi-attack does not.
var col = DPS.stats.collapse(s);
col.filter(e => e.action === 'Firaga III' && e.parts > 1).length   // > 0
col.filter(e => e.action === 'Attack' && e.parts > 1).length       // must be 0

// 5. a skillchain never shares a use with the weaponskill that closed it
var sc = col.filter(e => e.kind === 'skillchain');
sc.some(e => col.some(o => o !== e && o.use === e.use))            // must be false
[...new Set(sc.map(e => e.actionId))].sort((a,b) => a-b)           // all in E.Skillchains
```

```js
// 6. the pop-out path, without a real window. An iframe's contentWindow is a
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
// 7. eyeball the charts -- composite the canvases and export
const cs = ['lineChart','barsChart','histChart'].map(i=>document.getElementById(i));
// draw them onto one canvas, toDataURL('image/png'), then decode the base64 to
// a .png with Python and open it.
```

On the addon side, with no Lua runtime on this machine:

```bash
python addon-dev/check-apis.py                # allowlist / denylist / event names
python addon-dev/check-lua.py addons/VibeXI/*.lua   # structural balance only
```

Both must be green before a commit; the first is wired to the pre-commit hook.
Neither can catch a misspelled identifier or a bad expression — that surfaces on
`/addon load VibeXI` and nowhere earlier.

Checks that have caught real problems: append to a running file mid-line and
confirm the partial line is held back; drop a newer `.jsonl` into the directory
and confirm the client resets to it; confirm no monster name reaches the
character chips, the bars chart or the actions table; flip a name to
"leave out" in Diagnostics and confirm it leaves every total, then flip back and
confirm no surviving character's colour changed.

## Known gaps

Most of what used to be listed here was a property of the chat log and went with
it. What is left:

- Only the newest event file is followed. Events are not persisted by this app —
  closing the page loses them, and reopening replays the current file from the
  top. That replay is the persistence: the addon's file survives an FFXI crash,
  which is why it is written under `%LOCALAPPDATA%` rather than `%TEMP%`. Only
  the theme, the character exclusion list, the skillchain toggle and whether the
  character row is collapsed are stored (`ffxi_dps_theme`, `ffxi_dps_excluded`,
  `ffxi_dps_chains`, `ffxi_dps_charrow` in localStorage).
- The exclusion list is keyed by bare name, so it is shared across event files.
  That is intentional: a character you never want counted stays excluded.
- Pets are ordinary allies with their own row, not folded into their master —
  even though `owner` now says who that is. Folding them is a UI decision nobody
  has made yet, not a missing fact.
- Monster TP moves and pet abilities emit `#<id>` rather than a name. The name
  tables are ~300 KB and every event carries `actionId`, so naming can be added
  without touching the event contract.
- Reaction damage is not recorded at all: counters, spikes and retaliation are
  real damage belonging to the *other* entity, and emitting them as-is would
  credit a victim with their attacker's damage. Attribution has to be inverted
  first. See the "deliberately in NEITHER table" note in `vx_enums.lua`.
- MP drain, cures, enfeebles and TP are not parsed; this is a damage meter. Every
  event carries its raw `msg`, so adding them is an enums change, not a format
  change.
- A character with a single event has a zero-length active window, so their DPS
  shows as 0.
- Absorbed and "no effect" outcomes are treated as misses. Skillchains are the
  exception and deliberately so: Metrics has no absorbed-chain concept — it maps
  385/386 to Light and Darkness like any other id — so every chain that fires is
  recorded as damage.
- Multi-attack swings are visible individually, but nothing reports the round
  shape (double/triple/quad rates). That is Phase 4 in `addon-dev/PLAN.md`.
