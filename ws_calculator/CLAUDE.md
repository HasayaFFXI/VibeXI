# ws_calculator — working notes

Browser FFXI toolset. **No build step, no bundler, no Node, no Python available.**
Pages are opened directly from disk. `README.md` has the human-facing overview;
this file is the operational detail.

## Paths

```
../shared-ui/css/ffxi-theme.css   THE design system, shared with damage_meter — tokens, .card,
                              .field, .grid, buttons, table.data, .chart-wrap, .chart-tip
../shared-ui/js/theme.js      window.FFXITheme — the same tokens resolved for <canvas>
pages/ws-calculator.html      weaponskill Monte-Carlo sim (currently the only page)
shared/css/theme.css          app-only rules — mob grid, buff table, presets, run bar, results
shared/data/mob-data.js       MOB_DATA: zones, mobsByZone, species. ~390KB, ONE LINE.
shared/data/job-data.js       JOB_NAMES, JOB_VIT_RANK (LandSandBoat grades.cpp), rankToLetter,
                              populateJobSelect
shared/data/weapon-data.js    WEAPON_TYPES (pDIF cap + hit-rate cap), populateWeaponSelect
shared/lib/core.js            namespace bootstrap + clamp/randomInt/escapeHtml/resolve/resolveAll/
                              num/checked/scrollWithin/minMax
shared/lib/damage.js          THE ENGINE — fTPInterp, calcFSTR_PC, wRatioCapPC, spikeRatioPC,
                              calculateMeleePDIF, calcAlpha, calcMainBase, pDifBoundsForATT, simulate
shared/lib/mob-stats.js       getBaseToRank, getBaseDefEva, derive  (mobutils.cpp port)
shared/lib/buffs.js           food model + job-ability uptime model — ABILITIES, derivePotency,
                              rotationStates, resolveStates, effectiveAtt
shared/lib/chart.js           setupCanvas, plotBox, drawAxes, scale, label, inPlot, attachTooltip, COLORS/MONO/SANS
shared/lib/presets.js         10-slot localStorage presets: store + UI + dirty tracking
shared/components/mob-selector.js  fstr-panel.js  attack-panel.js  histogram.js
```

Server source of truth for every formula:
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\` (LandSandBoat
derivative). It lives outside this repo — it is a separate upstream checkout and
is deliberately not vendored here. Formula reference lives in the
`ffxi-ws-damage` skill.

## Hard rules

- **Classic scripts on `window.FFXI`. Never ES modules.** Modules are CORS-blocked
  on `file://`, which would break double-click-to-open. This is deliberate, not
  an oversight — don't "modernize" it.
- **Load order: `../../shared-ui/js/theme.js` → `core.js` → `data/` → `lib/` →
  `components/`**, all before the page's inline script. `core.js` creates the
  namespace everything else assigns into; `theme.js` comes first because
  `lib/chart.js` builds its colour view from `FFXITheme` at load time.
  Within `lib/`, `buffs.js` loads before `damage.js` — the engine consumes buff
  states that `buffs.js` builds, and although it only needs them at call time, the
  dependency direction is one-way and the script order should say so.
- **`lib/damage.js`, `lib/mob-stats.js`, `lib/buffs.js` must stay DOM-free.** No
  `document`, no element ids. They are what gets validated against the server Lua,
  so they have to run from a bare console call or a harness. If a formula needs a
  value, it takes it as an argument.
- **Components never reference another component's ids or call another module's
  render function by name.** Each exposes `create(config)` taking a `mount` map of
  elements-or-ids plus callbacks (`getInputs`, `onDerive`, `onApply`). The host
  page does the wiring. This is what makes them reusable on a second page.
- **The calculator is live — there are no recalculate buttons.** A single
  document-level `input`/`change` listener sets `pendingDerive` and calls
  `scheduleRecalc()`; `recalcAll()` then re-derives the target (only if a mob-panel
  field moved), re-renders the food summary and both panels, and reruns the sim.
  Don't reintroduce a component-owned button — components just expose `render()` /
  `derive()` for the host to call. Anything that writes a field programmatically
  must either fire a bubbling event or call `scheduleRecalc()` itself.
- **The design system lives in `../shared-ui/`, not here.** `shared/css/theme.css`
  loads *after* it and holds only what damage_meter would never want. Before adding a
  rule, check whether the shared sheet already has the primitive — `.card`,
  `.field`, `table.data`, `.chart-wrap`, `.chart-tip`, `.tile`, `.stat`,
  `button.primary` — and if a rule here starts looking generally useful, move it
  up rather than letting the other app grow a copy. `../shared-ui/README.md` is
  the vocabulary list.
- **Canvas colours come from `FFXITheme`, not from a table in the JS.**
  `FFXI.chart.COLORS` is a live view over the CSS custom properties; every key is
  a getter, so `C.COLORS.blade` re-reads on access and a theme swap needs no work
  at the call sites. Don't reintroduce a hardcoded palette — that is exactly what
  drifted before.
- **Chart height lives in CSS, not JS.** `setupCanvas(canvas)` falls back to the
  canvas's laid-out height, which the shared `.chart-wrap` sizing rules set
  (`.short` 170 / `.mid` 220 / `.tall` 320). Passing an explicit height is an
  override, not the normal path. A canvas inside a `display:none` container
  measures zero, so draw *after* revealing it — `.results-panel` gets `.show`
  before `histogram.draw`, and that ordering matters now.
- Styling uses classes, not ids (`button.primary`, `.results-panel`,
  `.chart-wrap canvas`). Keep page-specific styling in the page.

## Gotchas

- **Never read `shared/data/mob-data.js` with the Read tool** — one 390KB line,
  ~250k tokens. Move/inspect it with shell (`sed -n '1p'`, `head -c`). Same for the
  legacy `../jinpu_calculator/ws_damage_calculator.html`, whose line 748 is that blob.
- **The preview pane caches `file://` snapshots hard.** After editing a shared
  `.js` you can keep getting the old file; `force: true` and `?v=N` query-busting
  both fail. Opening a new tab is unreliable too. Verify semantics by evaluating
  the changed logic in-page, or accept the disk grep as proof.
- **Chrome restores form state across reloads**, so a "fresh" page can still hold
  values from earlier poking. Reset explicitly — and note `vit`/`def`/`tLvl` are
  `type=hidden`, so a `input[type=number]` sweep misses them.
- Synthetic `new Event('input')` **does not bubble** by default. The presets dirty
  tracker listens on `document`, so tests must use
  `new Event('input', { bubbles: true })` or they'll silently see a clean state.
- Presets store the recorded number as `metric`; pre-refactor saves used
  `avgDamage` and are migrated on read in `loadRaw`. Storage key is unchanged:
  `ws_damage_calc_presets_v1`.

## The buff / uptime model

The Actor card's **Buffs** section models Hasso, Berserk and Warcry by *uptime* —
the share of weaponskills that land with the buff active — rather than as on/off
toggles. Server-sourced numbers, all verified against the repo:

| Ability | Duration | Recast | Default uptime | Effect that reaches a WS |
|---|---|---|---|---|
| Hasso   | 300s | 60s  | 100% (capped) | `TWOHAND_STR` +floor(SAMlvl/7), `TWOHAND_ACC` +10 |
| Berserk | 180s | 300s | 60%           | `ATTP` +25, +2 per 10 levels over 40 **as WAR main only**, max +10 |
| Warcry  | 30s  | 300s | 10%           | `ATTP` +floor(floor(warLvl/4 + 4.75) / 256 × 100) |

Durations come from `scripts/globals/job_utils/{samurai,warrior}.lua`, recasts from
the `recastTime` column of `sql/abilities.sql`, mods from `scripts/effects/*.lua`.
Potency is cast through `uint16` by `addStatusEffect` (`lua_base_entity.cpp`), so
fractional powers **truncate** — that is why Warcry at WAR37 is 5% and not 5.47%.
Berserk's `DEFP -25` and Hasso's haste are deliberately not modeled: neither
touches outgoing weaponskill damage.

**Uptime becomes damage through a correlated rotation, not independent coin
flips.** Each buff is treated as re-pressed the instant its recast is up, so within
its own recast-long cycle it covers the leading `uptime%`. A weaponskill lands at a
uniformly random phase of the shared cycle (the LCM of the recasts). Consequences
worth knowing before "fixing" anything here:

- Buffs sharing a recast **correlate**. Berserk and Warcry are both on 300s, so at
  their natural uptimes every Warcry window sits wholly inside a Berserk window —
  you never see Warcry alone. That is the point of the model.
- Buffs on different timers are still correlated, just weakly, because everything
  is pressed from a common phase 0. At Hasso 33% / Berserk 47% the joint
  probability is 0.198, not the 0.155 independence would give. This is a modeling
  assumption (you press things off cooldown together), not a bug.
- Each buff's **marginal** probability comes out exactly equal to its uptime field,
  for any combination of uptimes. That is the invariant to test after any change to
  `rotationStates`.
- The state distribution is **enumerated exactly**, not sampled — the phase space is
  piecewise-constant, so `rotationStates` returns segments with closed-form
  probabilities. The rotation therefore adds no sampling noise of its own, and the
  states can be listed in the UI.

`FFXI.buffs.resolveStates` turns that into `p.buffStates`: `{prob, label, active,
str, att, hitRate}` per state, with the stats already resolved. `simulate` draws one
state per trial (one roll, not per hit) and precomputes `mainBase` and both
hit-rate clamps per state, since Hasso moves STR. **`damage.js` knows nothing about
abilities** — it just consumes a distribution over stat sets, which is what keeps
the ability formulas testable on their own. Omitting `p.buffStates` runs a single
unbuffed state from `p.str`/`p.att`/`p.hitRate`, exactly as before buffs existed.

Two composition rules that are easy to get wrong:

- `battle_entity.cpp` `ATT()` is
  `max(1, ATT + ATT*ATTP/100 + min(ATT*FOOD_ATTP/100, FOOD_ATT_CAP))`. Ability ATT%
  and food ATT% are each taken off the **same pre-percentage base and added** —
  they do not compound — and **only food is capped**. `ATTP` is one shared modifier,
  so Berserk and Warcry sum into it first.
- `physical_hit_rate.lua` uses `hitdiff = (acc - eva) / 2`, so Hasso's +10 ACC is
  worth **+5 points of hit rate** — which the great katana's 95% cap usually eats
  entirely at the page's default 95.

Potency defaults derive from the job setup (main job + `#aLvl`, sub job + `#subLvl`).
A **blank** potency field means "derived"; typing a number overrides it. Blank-as-auto
is deliberate — it needs no dirty flag, and presets and Chrome's form-state restore
round-trip an empty string for free. Don't replace it with a hidden `data-auto`
attribute. An ability whose job requirement isn't met is disabled, annotated, and
contributes nothing regardless of what its fields hold.

## Validating a change to the engine

```js
FFXI.damage.simulate(readParams(), 200000)   // full run
FFXI.damage.calcMainBase(readParams())       // deterministic part only
buffStates()                                 // the exact rotation distribution
```

`readParams`, `runSimulation`, `calcFSTR_PC`, `fTPInterp`, `calculateMeleePDIF`,
`buffStates`, `buffPotency` and `jobSetup` are all page globals, for the existing
console workflow.

For a buff change specifically, the checks that caught real problems:

```js
// 1. marginals must equal the uptime fields exactly, for any uptimes
const st = buffStates();
['hasso','berserk','warcry'].map(id =>
  st.reduce((t,s) => t + (s.active.includes(id) ? s.prob : 0), 0));

// 2. the mixed run must equal the probability-weighted mix of single-state runs
const p = readParams();
const mix = p.buffStates.reduce((t, s) => t + s.prob *
  FFXI.damage.simulate({...p, buffStates:[{...s, prob:1}]}, 200000).avgAll, 0);
// vs FFXI.damage.simulate(p, 200000).avgAll  -- inside ~0.5%

// 3. dropping buffStates must still match calcMainBase exactly
const bare = {...p}; delete bare.buffStates;
FFXI.damage.simulate(bare, 1000).mainBase === FFXI.damage.calcMainBase(bare).mainBase;
```

Method that has worked: sweep the pure functions across their ranges in both the
old and new build, join the results and compare a digest — they should be
**identical**. Then compare full sims at ~200k trials across several configs
(Adoulin on/off, hybrid on/off, crit, multi-hit, low-ATT/high-DEF, low hit rate).
`mainBase`/`fSTR`/`wsc`/`alpha` must match **exactly**; means inside ~0.5% is
sampling noise. Watch for integer-rounding artifacts in the DOM readouts — a
"33 vs 34" gap is usually a value sitting at x.5, not a divergence.

## Server assumptions

The live server runs `USE_ADOULIN_WEAPON_SKILL_CHANGES = false`, contradicting the
repo default of `true` in `settings/default/main.lua`, and there is no override
file in `settings/` — so reading the repo alone gives the wrong answer. Assume the
non-Adoulin branch: legacy WSC `alpha` applies (0.83 at L75, 0.85 at L76+), and
Tachi: Jinpu takes `ftpMod = {1.0, 1.0, 1.0}` (no TP scaling) with `str_wsc = 0.4`.

The page defaults already encode this — `#adoulinWS` ships unchecked and
`#wscWeight` defaults to `40`. Leave them that way; checking the box is for
answering "what would this look like on a stock server", not the live one.

## Known gaps (pre-existing, not bugs to "fix" silently)

- ATT is a manual input, not derived from STR. Note this means Hasso's STR bonus
  moves fSTR and WSC but **not** ATT, where on the server it would also feed the
  `strMultiplier` term.
- Weapon rank is hand-entered, not computed as `floor(weaponDmg / 9)`.
- Job points, merits and potency gear aren't modeled; they are what the manual
  potency overrides in the Buffs section are for. Warcry's Savagery TP bonus and
  Berserk's/Warcry's job-point *flat* ATT (`BERSERK_EFFECT * 2`,
  `WARCRY_EFFECT * 3`) are not applied — TP is a manual field, and the flat ATT
  would need a separate input from the percentage.
- The buff rotation assumes every ability is pressed off cooldown from a common
  start. It has no notion of a fight shorter than one cycle, of delaying Berserk to
  line it up with a WS, or of Hasso being cancelled for Seigan.
- Pre-level-50 subjob stat curve isn't ported; halved fallback, flagged in the UI.
- E-rank DEF below level 51 is interpolated from the A–D pattern, flagged in the UI.
- The hidden `#vit`/`#def`/`#tLvl` keep their page defaults (80 / 850 / 99) until a
  mob-panel field is touched; from then on every recalc re-derives them. A loaded
  preset restores its own saved values and suppresses that one derivation, so a
  preset saved before the mob panel was ever used keeps the numbers it was saved
  with.
- `../jinpu_calculator/` holds the unmaintained pre-refactor original
  (`ws_damage_calculator.html`) plus two older `- Copy` snapshots. Each carries its
  own copy of the engine, so they will drift from `shared/lib/damage.js` — treat
  them as reference only, and don't fix bugs there.
