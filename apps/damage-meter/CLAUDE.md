# damage-meter — working notes

Live HorizonXI damage meter. **No Node, no build step, nothing to pip install**
— the server is a stdlib-only Python script and the front end is classic
scripts. `README.md` is the human-facing overview; this file is the operational
detail. `PACKAGING.md` covers turning the server into an `.exe`.

## The one source is the addon

`../../addons/VibeXI/` reads the game's own action packets and appends one JSON
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
../../addons/VibeXI/  the addon that produces every event this app draws
../../shared-ui/    THE design system, shared with ../ws-calculator; mounted at /shared/
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
  `../ws-calculator`. They're what gets validated from the console; if a function
  needs a value it takes it as an argument.
- **Classic scripts on `window.DPS`, never ES modules.** Load order is
  `/shared/js/theme.js -> source -> stats -> chart -> popout -> app`; `chart.js`
  reads `DPS.stats` at load time for the formatters, `app.js` takes its `$` from
  `DPS.popout`, and `app.js` needs `FFXITheme` for the light/dark toggle.
- **`$` is `DPS.popout.byId`, not `document.getElementById`.** A popped-out
  card's nodes live in another document, where `getElementById` cannot see them.
  Anything that reaches for an element by id must go through `$`.
- **The design system lives in `../../shared-ui/`, not in `web/`.** `damage-meter.py`
  mounts that directory at the `/shared/` URL prefix so nothing is copied in —
  `resolve_static_path` picks a root from the prefix and applies the same
  containment check to each. `web/style.css` loads *after* the shared sheet and
  holds only what `../ws-calculator` would never want. Before adding a rule, check
  whether the shared sheet already has the primitive (`.card`, `.tile`,
  `table.data`, `.chart-wrap`, `.segmented`, `.chip`, `button.ghost`), and if a
  rule in `style.css` starts looking generally useful, move it up rather than
  letting the other app grow a copy. `../../shared-ui/README.md` is the vocabulary
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

`kind` is one of `melee ranged ws magic ability mobtp pet skillchain addl
reaction`.
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
is a new entry in `../../addons/VibeXI/vx_enums.lua`, not here.

**Neither is a `kind:"job"` line.** It states one party member's jobs:

```json
{"kind":"job","t":1785000000,"actor":"Hasaya","main":"SAM","mainId":12,"mainLvl":75,
 "sub":"WAR","subId":1,"subLvl":37}
```

No `seq` and no `use`, because it takes part in no ordering; the `sub` trio is
omitted entirely when there is no sub-job, the same rule `owner` and `pet`
follow. `source.js` files it on the **roster**, never in `events` — a job carries
no damage, and a zero-damage row would land in every count in the app.

Four things about it are load-bearing:

- **Written on change, not on a timer.** `vx_entity.note_job` returns a record
  only when something actually differs, so the file gets a handful of these per
  session rather than one every three seconds. The consequence is that
  `source.reset()` must **keep** the job map — a cleared one would stay empty
  until somebody changed job — and it does, because the map lives on the roster
  and reset only drops events.
- **Last write wins**, unlike `roster.note`'s first-answer-wins for spawn kinds.
  A second job line for a character is a real change, so the newest one is true.
  A spawn classification cannot change, which is why the other rule is the
  opposite.
- **A main job of 0 never overwrites a real one.** The party table reports 0 for
  a member who is zoning or out of range, and taking that at face value would
  blank a character's job — and their colour — every time they crossed a zone
  line. Guarded in Lua, in `note_job`; Metrics guards the same way.
- **It also classifies the name as ours.** A job line comes off a party slot, so
  `source.js` calls `roster.note(name, 'player')` on it. For a member who never
  acts — the white mage — that is the *only* evidence there is, and without it
  they read as an unclassified stranger in Diagnostics.

A party member who never deals damage therefore reaches the app through this
line and nothing else. They appear in the Diagnostics roster, with their job,
and in no chart, chip or total — which is correct: the meter lists the party,
but it charts damage.

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
- **A monster's own swings are never recorded.** The addon drops any action one
  of ours is not the actor of (`is_ours` in `vibexi.lua`), so damage *taken* is
  not in the file at all. It used to be written and then dropped in the browser;
  on a long pull that was a large fraction of every line for a view that does
  not exist. Files captured before that change still parse — `filter` drops
  monster actors exactly as it always did.
- **Reaction damage is counted, and it arrives backwards.** A counter, a spikes
  proc and a Retaliation all ride on a packet the MONSTER is the actor of, and
  `record_reactions` in `vibexi.lua` writes them with the two ends swapped: the
  actor is whoever reacted, the target is whoever swung into them. Nothing in
  the browser knows this happened — the rows arrive already inverted, with
  `kind:'reaction'` and an action of `Counter`, `Retaliation` or `Spikes`. The
  same `is_ours` test is applied to the entity that reacted, so a monster's own
  spikes firing on our swing invert to monster damage and are dropped like any
  other.
- **Pets count as ours and are credited to their owner.** `stats.credit` re-actors
  every row carrying `owner` onto the master, so a pet has no row, no chip and no
  colour slot of its own; its damage is part of the owner's total and DPS. The
  action keeps the pet's name (`Fluffikins: Big Scissors`) so the owner's
  breakdown still separates pet from master — both swing an "Attack", and one
  average over the two would describe neither. It copies rather than mutates, so
  `app.source.events` stays true to the file for Diagnostics and the roster.
- **The monsters' events still bound nothing.** `windowOf` used to resolve
  `Latest fight` over the unfiltered list, on the grounds that a pull where the
  monsters got the last word still ended when they did. There is no automatic
  window any more -- the user says when the pull started -- so the only thing
  the monsters' rows are still read for is the Diagnostics roster.

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
| `LWA_COLORKEY` | *intended* to drop pixels of exactly `#010203`. **Inert — see below.** | always on; `DPS.popout.keyBg(key, false)` |

**The bar carries Start, Pause, the elapsed clock and the total**, at full
opacity while the rest of it fades; see "The controls are in every pop-out
window too" above for why they are there and how they stay in step with the
page.

**A panel opens at 85%, and the page's top bar sets that number.** Opaque is the
wrong thing for a window whose whole job is to be laid over the game: at 100% it has to be discovered that the slider exists at all, and the
slider is inside the window, which is the one place a user who has not opened one
yet cannot look.
The **Pop-out opacity** config (`#popAlpha`, wired by `bindConfig()` in
`popout.js`, not by `app.js`) is that control, and `DEFAULT_ALPHA = 85` is only
what it reads before anyone has moved it.

Setting it **clears every panel's own slider value** (`alphas = {}`). That is the
part to leave alone: a per-panel override would otherwise outrank the config
silently — set once, sessions ago, on a window that is not on screen to show what
it is doing — and the config would look broken on exactly the panel being watched.
One control, one meaning; a window's own slider then adjusts that window from
there on, and lasts until the config is next touched.

#### The punch-out does not work, and never has

`.key-bg .pop-doc` paints the background exactly `#010203` so the window manager
will drop those pixels. **It doesn't.** Chrome presents its windows through
DirectComposition rather than the legacy redirection surface `LWA_COLORKEY`
operates on, so keyed pixels are never dropped. `SetLayeredWindowAttributes`
still returns success, so `applied > 0` is true, `key-bg` goes on, and nothing
ever reported a problem.

Measured 2026-09-05, keying a real Chrome window painted entirely `#010203` and
sampling the screen underneath:

| | sample A | sample B |
|---|---|---|
| no layering | `#010203` | `#010203` |
| `alpha=255`, no key | `#010203` | `#010203` |
| `alpha=255`, **with key** | `#010203` | `#010203` |
| `alpha=128`, no key | `#070708` | `#070709` |
| `alpha=128`, **with key** | `#070708` | `#070709` |

Alpha moves the pixel; the key changes nothing at either alpha. `--disable-gpu`
does not help. A synthetic non-Chrome layered window keyed correctly at every
alpha including 255, so this is Chrome specifically, not the alpha value.

So **the transparency you see is uniform alpha alone**, and painting the
background `#010203` is decorative — a near-black backdrop that alpha then makes
translucent, which is what an overlay wants anyway. That is precisely why this
has always looked like it works.

Two things follow, both tried and both dead ends:

- **A solid bar over a translucent panel is not possible in this window.** One
  alpha byte for the whole `HWND`, no per-region form, and the per-pixel escape
  hatch (`UpdateLayeredWindow`) has to own the window's pixels — Chrome's, not
  ours. Pinning the alpha to 100 to keep the bar crisp makes the window opaque
  and the panel a solid near-black slab. Wanting a genuinely solid bar means
  drawing the overlay in the Ashita addon instead.
- **Nothing is click-through.** That was a claimed side effect of the punch-out;
  since the punch-out is inert, the window takes clicks everywhere.

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
- **Never paint `#010203` unless the server call landed.** `osAlpha()` toggles
  `key-bg` off on failure. Since the key is inert anyway this is always "simply a
  near-black window" — but the gate still earns its keep, because on failure
  there is no OS alpha either, and near-black with no alpha is an opaque slab.
- State lives in `ffxi_dps_alpha` (`key -> 15..100`), `ffxi_dps_keybg`
  (`key -> bool`) and `ffxi_dps_alpha_default` (one number, what the page config
  is set to); `DPS.popout.alpha(key[, v])`, `DPS.popout.keyBg(key[, v])` and
  `DPS.popout.defaultAlpha([v])` are the console handles, and the response
  carries `method` and `window` so a wrong match is diagnosable.
- **`clampAlpha`'s fallback is the configured default, not 100**, so “no value
  of its own” means “whatever the page says” everywhere at once. The one place
  that cannot use it is `readDefaultAlpha`, which is reading that fallback.
- **Verified: the server end applies and reads back** (`alpha`, `flags=2`,
  ex-style gains `0x80000`) against a real Chrome window, and every client branch
  is verified against a stubbed endpoint. **Also now verified: how Chrome
  composites a layered window** — alpha yes, colour key no; see the punch-out
  section above for the measurements. The Browser pane cannot produce an
  always-on-top window, so that test drives a real Chrome launched with `--app`
  and samples the desktop with `GetPixel`.
- **The punch-out has no button any more.** The bar carried a **BG** toggle next
  to the slider; it was removed on 2026-09-05 as one control too many over a game
  screen, and it is simply always on. `keys`/`ffxi_dps_keybg` and the `key=`
  query parameter all stay: the flag costs nothing, it is the escape hatch for a
  window that renders wrong, and a host that *did* honour the key (a
  non-Chromium browser, or an overlay this app drew itself) would want it.

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

## The session clock

**Every number in this app is measured from the session's zero, and there is no
other clock.** `app.session` is `{ armedAt, startedAt, spans, pausedAt }` and
the whole model is a handful of functions in `stats.js` over those four fields:
`sessionAt` (wall clock -> ms since zero, or `null` for an instant the session
does not cover), `sessionElapsed` (the clock right now), `sessionRunning` and
`sessionArmed`.

**The Start press is not the zero.** Start only *arms* the session; the clock
starts on the first event that would actually be counted, and **that event's own
timestamp becomes the zero**. Pressing early therefore costs nothing, which is
the entire point — a stopwatch you must press on the frame of the first swing is
one that is always a second or two wrong, and every DPS figure in the app is
divided by that error. Three states, each real:

| `armedAt` | `startedAt` | |
|---|---|---|
| null | null | idle. Nothing counted, nothing drawn. |
| set | null | **armed**. Still nothing counted — there is no zero to measure from — but the next counted event will set one. |
| set | set | running, or paused. |

`at()` and `elapsed()` key on `startedAt` alone, so an armed session behaves
exactly like an idle one until it latches. That is what keeps arming out of
every panel: one extra field and no new branch downstream.

The wire clock is still wall-clock seconds, because that is what the addon can
cheaply know. **It is converted exactly once, in `stats.filter`**, in the same
place and the same style `credit` re-actors a pet: events outside the session
are dropped, survivors are *copied* with `t` rewritten to elapsed milliseconds
and the original kept as `wall`. Downstream of that one call there is no wall
clock left — `aggregate`, `cumulative`, `distribution`, every table and the
chart axis all see a timeline that starts at zero.

Consequences worth knowing:

- **`startedAt: null` is a real state and it draws nothing** — whether idle or
  armed. The meter reads the file, fills the Diagnostics roster and counts not
  one point of damage. `filter` short-circuits to `[]` when there is no zero, so
  this is one branch rather than a special case in every panel.
- **`counted()` is the single predicate, and it has to be.** Every non-time drop
  rule — combat, the skillchain switch, the monster filter, `credit`, the actor
  exclusions — lives there, and both `filter` and `firstCounted` go through it.
  Two copies would let the meter start its clock on an event it then refuses to
  draw, which is a zero nothing on screen can account for.
- **What starts the clock is exactly what would be counted.** A monster's swing
  does not, nor an unresolved `Unknown` actor's, nor a skillchain with chains
  switched off, nor an excluded character's attack. The consequence: with every
  character excluded nothing qualifies and the clock never starts. That is
  consistent — the meter would draw nothing anyway — but it is the one way
  arming can look stuck, which is why the armed state is loud (a pulsing ring on
  the status dot, an outlined button, and a line in every empty chart).
- **A MISS starts the clock, like any other swing.** Waiting for damage to land
  would put every miss before it outside the session and silently drop them from
  the accuracy figure — exactly the number a run of whiffs at the start of a
  pull ought to be moving.
- **`armedAt` is floored to the second.** The wire clock is `os.time()`, so a
  swing 300 ms before the press and one 300 ms after carry the *same* `t` and
  cannot be told apart. Rounding down counts that second rather than discarding
  it: catching the first swing matters more than excluding one that beat the
  button by a moment, and the alternative drops it from accuracy too.
- **The latch is sticky.** `sessionStart` sets `startedAt` once and never moves
  it. A later filter change cannot re-date a running session and re-scale every
  number in it.
- **A filter change CAN latch an armed session**, and should. `latchStart` runs
  at the top of `render()`, which a filter change also calls, so flipping a name
  back to `player` in Diagnostics — or switching skillchains on — can make an
  already-read event the first counted one, and it then becomes the zero. It is
  the first thing being counted, so it is the right zero.
- **The second button is Pause OR Cancel, never both.** While armed there is no
  clock to hold but there is an arming to call off; once the clock runs there is
  no arming left to cancel. One slot, one meaning at a time, dispatched by
  `secondary()`. Arming is a statement about a pull that has not happened yet,
  and a statement made early can turn out to be wrong — the puller pulls
  something else, the party resets, someone drops. Without Cancel the only way
  out of an armed meter was to let it catch a hit and throw that session away:
  "measure the thing you did not want to measure, then discard it".
- **Cancel is only reachable while armed**, and deliberately. A session with
  damage in it is ended by Start, not by Cancel — "cancel" would then mean
  discarding a measurement, which is a different and far more destructive act
  than calling one off before it began.
- **Cancelling is exactly the idle transition a new event file makes**, and
  nothing more. The events read while armed are NOT dropped: an idle session
  counts nothing, so they are invisible either way, and the next Start clears
  them with everything else. Colour slots are kept for the same reason they
  survive a Start — a character changing hue because somebody called off a
  countdown is worse than a stale entry.
- **`aria-pressed` comes off the button when it is Cancel.** Pause and Resume
  are two faces of a toggle; Cancel is a plain action, and announcing a
  two-state control that is not there is worse than announcing nothing. The
  attribute is removed rather than reported false (`pauseToggle` in the view).
- **Pause stops the damage and the clock together, and it has to.** Freeze one
  without the other and the meter lies: a running clock over a frozen numerator
  reads as a wipe, a frozen clock over a running numerator reads as a parse.
- **A pause is subtracted, not skipped over.** Two swings either side of a three
  minute pause come out three minutes closer together than their timestamps are.
  That is what keeps the axis and the DPS denominator describing the same span
  — a gap on the chart that no denominator accounted for would be unreadable.
- **Polling does not stop while paused.** The addon keeps writing whatever this
  page does, so stopping the reader only moves the same bytes into a burst on
  resume. Events during a pause are read, kept for Diagnostics, and dropped in
  the view by their own timestamps — the same shape as the monster filter.
  Because the drop is by timestamp and not by arrival, poll latency cannot let
  an event sneak past the boundary it landed on the wrong side of.
- **One denominator, everywhere.** `aggregate(events, {duration})` divides every
  DPS by the session clock — the party's and each character's alike — so the
  character column sums to the party figure. Each actor's own first-to-last span
  is still measured and still returned, as `window`; it is simply no longer what
  anything is divided by. Without `opts.duration` the old per-actor behaviour is
  what you get, which is what keeps `aggregate` usable on a bare event list.
- **DPS decays on its own, so two things tick.** The numerator holds and the
  denominator grows, which `render()` cannot express — it runs only when a poll
  brings new data, and an idle poll deliberately skips it to keep scroll
  position and text selection. `tickClock` rewrites the Elapsed tile, the party
  DPS, every `[data-dps]` cell in the character table and every floating panel's
  clock-and-total readout, as text, four times a second. **The party tile and the character cells must move together or not at
  all**: a tile decaying past a frozen column is two correct numbers at two
  different instants, and it reads as a bug. That is also why the bars chart's
  hover card no longer carries a DPS row — it is built once per render and
  cannot be ticked, and the same character's live cell is in the same card a few
  pixels below.
- **The chart's left edge is pinned to zero** (`cumulative`'s `opts.from`), not
  to the first event. A pull whose first swing landed twenty seconds in would
  otherwise draw an axis twenty seconds shorter than the span DPS is divided by.
- **There is no idle cutoff on the live edge.** It used to stop advancing after
  90 s of silence, and `FIGHT_GAP_MS` is gone with it. Under a session clock a
  flat line out to the present is the most informative thing on screen: it is a
  falling DPS, drawn.
- **`liveEdge` is the clock whenever a session has STARTED, paused included**,
  and null only before Start. Making paused an exception was a bug: returning
  null ends `cumulative`'s grid at the newest EVENT, so pressing Pause snapped
  the chart back to the last swing and discarded the quiet stretch between that
  swing and the button — while `sessionElapsed`, frozen at the press, went on
  counting exactly that stretch into the DPS denominator. The axis and the
  number beside it were describing different windows, which is the one thing
  this chart may never do. `sessionElapsed` is already frozen while paused, so
  handing it over unconditionally holds the edge still at the press.
- **The two silences either side of a pause are both real elapsed time**, and
  both are drawn: last-event-to-Pause, and Resume-to-next-event. Only the pause
  span itself is spliced out. The one place the meter waits for an event before
  its clock moves is the very first arming — after that the clock runs on wall
  time whether anything is happening or not, because that is what DPS is divided
  by. `tickLine`'s `paused()` guard is what stops the frozen edge from being
  animated forward while held.
- **A new event file resets the session to idle**, armed or not. A clock still
  running from the previous character or the previous day is measuring a session
  that is not this one.

### The two controls, and where they live

Start and Pause (Cancel, while armed) are the only controls in this app that
change what is being *measured*; everything else on the filter bar changes what
is being *shown*. So they are the only ones that are larger than default and the
only ones that carry a fill — `.segmented.session` in `web/style.css`, sized 13px/8x20 against the
filters' 12px/5x12.

**Two colours, one meaning each, and the status dot uses the same pair: green is
counting, brass is not.** Start is green while it is the thing to press, turns
brass the moment it is armed and waiting, and steps back to a `--blade` outline
once a session is running — "Restart" is a re-do, not the main action, and
filling it would put two loud buttons side by side with the destructive one the
louder. Pause is brass whenever it is holding the clock. Cancel takes the same
blade outline Restart wears, because both are the step-back beside a filled
button and one shape for "undo" is easier to learn than two; it is never filled,
since a cancel that shouts as loudly as Start is a cancel that gets pressed. A user who has learnt
the dot has learnt the buttons; that is why `.dot.armed` was moved off green.

`--on-good` and `--on-brass` were added to the shared sheet for this, mirroring
`--on-blade`: a fill needs a text colour graded against it, and both accents
invert between the tiers (bright on dark, dark on light).

Three traps, all of which bit:

- **`.segmented.session button` must not set `color`.** It outweighs both
  `.segmented button:disabled` and every `.session-*.is-*` state rule, so a
  colour there silently wins them all — it put bone text on the brass fill and
  cancelled the disabled greying at the same time. That rule may own size and
  weight; colour belongs to the states.
- **No `transition` on background or colour.** A pop-out's pair is created bare
  and dressed a tick later, so a transition opens every new window with a flash
  of unstyled button. None of the other segmented controls animate their fill
  either.
- **A hidden tab freezes transitions**, so while one was in place the buttons
  read as their *pre*-transition value forever from `getComputedStyle` — which
  looks exactly like a rule that is not applying. Screenshot before believing a
  colour measured from the Browser pane.

### The clock is a figure, not a caption

It is the denominator under every DPS on the page and the one number in the app
that keeps moving with nothing happening, so it is the **second tile**, beside
the total it divides, and it is drawn at 34px rather than buried in the hero
tile's sub-line. Every floating panel carries **both** figures beside its own
buttons, in that same order — a panel is over the game so the pull can be run
without leaving it, and how long it has run and how much it has done are exactly
what is being asked from there. Together they are the whole meter in one 20px
line, which is what a panel showing a single chart could not otherwise say.

- **`fmtStopwatch`, not `fmtElapsed`.** Same clock, zero-padded to a fixed
  width: `MM:SS`, and `H:MM:SS` past the hour. `fmtElapsed` drops the leading
  zero so a figure inside a sentence or under an axis tick reads as short as it
  is; a stopwatch is *watched*, and one that changes width at `0:59 -> 1:00`
  shifts every digit beside it. `tabular-nums` is on for the same reason, and it
  is why the tile and every floating bar line up.
- **The state rides as a class, in the same two colours as the buttons and the
  status dot** — green counting, brass held, `--faint` idle. A stopped clock and
  a slow one are otherwise indistinguishable by watching, which is the one
  question being asked of it.
- **The elapsed reading came OUT of `#tTotalSub` and is not repeated there.**
  Two adjacent cards printing the same clock in two formats is the thing the
  second card was added to fix; the hero's sub-line now carries only the state of
  the measurement, which the total needs and the clock does not.
- **`#tClockSub` is the one tile that still prints a time of DAY.** It says when
  the pull began in the world, which is the one fact an elapsed clock cannot
  state — the same reason `fmtClock` survives in the status line.

### The controls are in every pop-out window too

A panel is floated over the game so a pull can be run without leaving it, and
reaching back to the page to press Start is the one thing that cannot be done
from there. A meter you have to alt-tab to start is a meter that gets started
late — and late is an error divided into every DPS figure it then reports. So
`dress()` builds the same pair into every floating bar.

- **app.js owns the copy, popout.js owns the nodes.** `sessionView()` returns the
  labels, titles and state class as plain data; `paintSession(view, start, pause)`
  writes them onto a pair. `DPS.popout.session(view)` pushes that to every open
  window, exactly as `DPS.popout.theme(name)` pushes a theme. popout.js is loaded
  first and knows nothing about sessions — it is handed finished text and applies
  it. The alternative is two copies of this copy, drifting apart, in two windows
  side by side on one screen.
- **The view is remembered, not just forwarded.** A window opened later missed
  every push that came before it existed, so `sessionControl` paints from the
  stored view at birth.
- **`paint` overwrites `className` outright**, so nothing may be hung on the
  buttons themselves. The floating bar's compact sizing is selected through the
  `.pop-session` wrapper instead.
- **`.pop-session` is the WRAPPER, not the segmented pair.** It holds the pair
  *and* the elapsed readout, and it is what the fade below exempts. It used to
  be the `.segmented.session` element itself; if a selector assumes that, it is
  reading the old shape.
- **The live figures are pushed separately from the view**, through
  `DPS.popout.readout({ clock, state, total })`, and for a plain reason: the view
  changes when a button is pressed, these change four times a second, and pushing
  them together would rewrite two buttons' labels, titles and classes on every
  tick. Same contract otherwise — both strings arrive finished, popout.js does no
  formatting and knows of neither a clock nor a damage total, and the last value
  is REMEMBERED so a window opened mid-pull is born showing the right numbers
  instead of zeroes.
- **The total is pushed from `tickClock`, not from `renderTiles`.** It only
  *changes* on a render — `tickClock` reads the same `app.tileAgg.total` the hero
  tile was written from, so the two can never disagree — but pushing it on the
  tick is what gets it into a window opened between two polls. On a static file
  that is the difference between a floating bar reading `155,195` and one reading
  `0` until the next event lands.
- **They sit to the RIGHT of the buttons, and must**, in the page's own order:
  clock, then total. The pair is a fixed hit target aimed at without looking; a
  readout ahead of it would shove both buttons sideways when the clock rolls past
  an hour. `.pop-clock`'s `min-width` is sized for `H:MM:SS` for the same reason,
  and `.pop-total` is deliberately given none — it is last in the group, so a
  seventh digit pushes nothing but the panel title, which is the one thing on
  this bar meant to give way.
- **The total is `--blade`, the clock is its state colour.** Two adjacent
  monospace figures in one colour read as a single string; the accent is also
  what the hero tile paints the total, so the bar and the page agree. The total
  carries no state class — it is the same number running or held.
- **The bar's fade moved from the bar to its children.** Opacity on a parent
  cannot be undone by a child, and the session group is the one thing there that
  must stay readable and hittable without hunting for it: a Start button at 45%
  over a battle scene is not a Start button. `.pop-bar > *:not(.pop-session)`
  carries the fade now; the group never does — and the clock is inside the group
  precisely so it is inside that exemption.
- **The controls survive the module being used without them.** `sessionControl`
  returns null when `init` was given no `session`, so a page with poppable cards
  and no session still works — and still tests.

### Start is also the reset

There is no separate Reset button; pressing Start during a session is how the
next pull gets measured — it re-arms, and the next counted hit is the new zero. It drops the events collected so far and **keeps the
read offset** — not a re-read; a page reload is still what replays the file
from the top, and that is documented in the README. Dropping them buys nothing
on screen, since the session window would have hidden them anyway; what it buys
is that the poll path stays O(events since Start) rather than growing all
session.

What survives, and why:

| Kept | Reason |
|---|---|
| `app.slots` / `app.seen` | a character changing hue mid-session is worse than a stale entry |
| the whole roster (`kinds`, `manual`) | monsters must stay monsters across the reset |
| `chains`, `actorsOff` | user intent, not collected data |

`app.scanned` **must** be rewound to 0 alongside `source.reset()`. It is a cursor
into the event list; leaving it past the now-empty list makes `scanActors` skip
every name until the list grows back past the old length.

**The session is not persisted.** Nothing in `localStorage` holds it and a reload
returns the meter to "not started" — a clock restored from a previous page load
would be measuring wall-clock time the user was not in a fight for. An armed
session is not restored either, for the same reason: arming is a statement about
the pull that is about to happen.

## Charts

House style from the `dataviz` skill; the palette is its documented reference
instance (blue, orange, aqua, yellow, magenta, green, violet, red) with each
mode's own steps, already validated — **don't re-step it**. It now lives in
`../../shared-ui/css/ffxi-theme.css` as `--series-1..18`, deliberately kept apart
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

### Job colour, always — and the ramp underneath it

`colorOf(name)` paints **every character in their job's colour**. There is no
alternative palette and no control to pick one: the Colour segmented control
that used to sit in the filter bar is gone, and so is `ffxi_dps_colour`. One
mapping means a hue means the same thing in every screenshot, every session and
every panel. **Do not reintroduce the toggle** — its removal was asked for
explicitly, not lost in a refactor.

Metrics' own job colours, `--job-war` … `--job-pup` in the shared sheet, read
through `FFXITheme.job(abbrev)`. They are in the design system because the party
already reads them at a glance in game: the warrior is red, the samurai orange.
Agreeing with the parser sitting next to it is worth more here than inventing a
second mapping for the same six people.

What that costs is said out loud in the stylesheet and repeated here because it
is the kind of thing that gets "fixed" later by someone who did not know: **the
job palette is not colourblind-separable.** WAR, NIN, RDM and SAM are four reds.
That is acceptable *only* because the meter never identifies a character by
colour alone — every panel that paints one also prints the name and the job as
text.

Three edges, the third of which is why the series ramp still exists:

- **Two characters can share a job.** `assignJobVariants` numbers them and
  `FFXITheme.step(color, k)` turns the number into a lightness move — away from
  the page first (lighter in dark mode, darker in light), so a variant is never
  the harder one to see. Ordered by **colour slot**, not by damage or table
  position, so the same warrior keeps the same shade all session; ordering it by
  anything the user can reorder would swap shades under them. Slots are still
  assigned for every counted character even though most never spend theirs on a
  hue — this ordering, and the fallback below, are what they are for.
- **Four jobs have no colour.** Metrics leaves DNC, SCH, GEO and RUN at 0,0,0 —
  unfinished, not black on purpose — so they have no token, and neither does
  `NON`. `FFXITheme.job()` returns `''` for those *deliberately*: the caller has
  something better to fall back to than a made-up hue.
- **A character with no job on record falls through to their slot.** A trust, a
  pet's owner seen only through the pet, anyone the party table has not reported
  yet. The fallback is what makes the empty string safe.

The light tier is **not** Metrics' verbatim and cannot be: Metrics paints onto
the game's 3D scene, and PLD's yellow-green and WHM's white vanish on a `#f7f4ec`
card. Hue is what carries the recognition, so hue is what is preserved and
lightness is what moves. WHM is the one that cannot be preserved — white has no
hue — and becomes a warm dark neutral.

Job colours are for **marks only** — dots, swatches, chart lines. The job *text*
beside them stays in `--mist` / `--faint`, exactly as character names stay in ink
next to their series swatch, and for the same reason: neither palette is graded
for body text.

### Hiding the names

`#anonBtn` in the top bar draws every character but the file's owner as their
job. It is a **display** switch and nothing else: `nameOf()` is the only thing it
changes, every key stays the real name — the exclusion list, `data-actor`, the
roster override, `colorOf`, `S.filter`, the aggregate itself — and the handler is
a `render()`. If a total ever moves when it is toggled, something has started
keying off a drawn name.

- **`assignAliases` mirrors `assignJobVariants`, and for the same reason.**
  Ordered by colour slot, so a label is stable for the session; ordering it by
  damage or table position would renumber the party under the user on every lead
  change. Two people on one job come out `SAM/WAR` and `SAM/WAR 2`, the first
  unnumbered exactly as the first also takes the base colour.
- **Slots are not the whole party.** A member who never deals damage is never an
  actor and so is never slotted, but they do reach the Diagnostics roster off
  their job line alone — so the map also takes every name `roster.kinds` calls a
  `player`, trailing the slotted ones in name order. Leaving them out put
  Sylviane's name back on screen the moment that panel was opened, which is how
  this was found. The residual: two characters on one job, neither slotted yet,
  and the one sorting second is the one that acts first — that pair swaps numbers
  on that first swing and never again.
- **The Job column is dropped, not blanked**, in both tables that carry one. The
  name cell is already the job, a second copy of it reads as a bug, and a `—`
  there would say "job unknown", which is a different fact. `jobBadge()` does the
  same for the inline badges on a chip, the legend and an actions group row.
- **The roster table sorts on the drawn name.** Sorting a hidden name by the name
  it is hiding leaves it sitting in its own alphabetical slot — a sort that looks
  broken and also narrows down who it is.
- **A pet keeps its name.** It is not a character, its damage is already the
  owner's, and its name lives inside the action string. Hiding it means changing
  the action label, not this map.

### The fallback: eighteen slots, one per character

`FFXITheme.series(slotOf(name))` is what a character gets when the job palette
has nothing for them. It is no longer reachable as a mode, but it is not dead
code and must not be deleted: an alliance of six with a SCH, a GEO and two
trusts is an ordinary party, and every one of those falls here.

**Every character gets their own colour — there is no muted tail and no "Other"
line.** An alliance is 18 characters, so the palette is 18 slots
(`FFXITheme.SLOTS`).

That is knowingly past where colour alone works, and the `dataviz` skill's own
rule is that a 9th series folds into "Other" rather than getting a hue. What
makes it defensible here is that **the meter never identifies a character by
colour alone**: the legend names all of them, the line table gives each a
column, the bars chart and the actions table are labelled rows, and hover names
the series. Colour is the cross-panel *link* between those, not the label.

The ramp is still sized and solved for all 18 rather than for the handful that
now reach it, because which characters fall through is a property of the party,
not of the app — a full alliance of trusts would use every slot.

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
between fights, which is what a Start pressed part-way through a session has
to land in the middle of. Its docstring says what
each case catches; read that before changing it.

The job lines carry four of their own: a member who **never acts** (Sylviane,
WHM) and so must be listed with her job and charted nowhere; a member with **no
sub-job** (Vermillion), whose line omits the sub trio; a **mid-session job
change** (Rhyllis, PLD/WAR → SAM/WAR in the downtime after the second fight),
which must take, proving last-write-wins; and the **two-characters-one-job** case
that change creates, which is what forces the shared job colour to be shaded
rather than drawn twice.

**It must stay field-for-field identical to `vx_emit.encode`.** If the fixture
and the emitter disagree the fixture is worthless, so that function is the thing
to diff against when either changes.

```bash
python apps/damage-meter/tools/gen-test-events.py
python apps/damage-meter/damage-meter.py --port 8732 --no-browser --events-dir apps/damage-meter/tools/events
```

That is the `damage-meter-fixture` entry in `../../.claude/launch.json`, port 8732.
**Reload the page after regenerating** — see the last gotcha above.

Browser-pane screenshots work against `http://localhost`; it is `file://` pages
that come back as static top-of-page snapshots. From the page console:

```js
// 1. the source contract
DPS.app.source.meta                       // probe + unrecognised message ids
DPS.app.source.state.bad                  // lines that were not JSON at all
DPS.app.source.roster.kinds               // name -> player | pet | mob | npc | other
DPS.app.source.roster.jobs                // name -> { main, mainId, mainLevel, sub, ... }
DPS.app.source.roster.jobLabel('Hasaya')  // 'SAM/WAR', or '' if never reported
DPS.app.jobVariant                        // name -> 0,1,2... among characters sharing a job
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
  the theme, the character exclusion list, the skillchain toggle, whether the
  character row is collapsed, whether the names are hidden and the pop-out
  opacity settings are stored — the session clock deliberately is not, so a
  reload returns the meter to "not started" — (`ffxi_dps_theme`, `ffxi_dps_excluded`,
  `ffxi_dps_chains`, `ffxi_dps_charrow`, `ffxi_dps_anon`, `ffxi_dps_alpha`,
  `ffxi_dps_keybg`, `ffxi_dps_alpha_default` in localStorage).
- The exclusion list is keyed by bare name, so it is shared across event files.
  That is intentional: a character you never want counted stays excluded.
- Reaction ATTEMPTS are not recorded — 535 RetaliateShadowAbsorbs, 592
  PerfectCounterMiss, 14 CounterAbsByShadow. Reaction damage itself is counted,
  but the reactions that dealt nothing are not, so a Counter or Retaliation row
  always reads 100% accuracy. Which side of the packet each of those ids belongs
  to has not been measured against a live client, and guessing would inflate a
  party member's swing count with reactions that were never theirs.
- Monster TP moves and pet abilities emit `#<id>` rather than a name. The name
  tables are ~300 KB and every event carries `actionId`, so naming can be added
  without touching the event contract.
- MP drain, cures, enfeebles and TP are not parsed; this is a damage meter. Every
  event carries its raw `msg`, so adding them is an enums change, not a format
  change.
- Absorbed and "no effect" outcomes are treated as misses. Skillchains are the
  exception and deliberately so: Metrics has no absorbed-chain concept — it maps
  385/386 to Light and Darkness like any other id — so every chain that fires is
  recorded as damage.
- Multi-attack swings are visible individually, but nothing reports the round
  shape (double/triple/quad rates). That is Phase 4 in `addon-dev/PLAN.md`.
