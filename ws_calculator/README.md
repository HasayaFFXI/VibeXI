# WS Calculator

Shared-component app extracted from the single-file Jinpu weaponskill calculator.

## Layout

```
../shared-ui/
  css/ffxi-theme.css       THE design system, shared with ../damage_meter
  js/theme.js              window.FFXITheme - the same tokens, resolved for canvas
shared/
  css/theme.css            app-only rules: mob grid, buff table, presets, run bar, results
  data/mob-data.js         MOB_DATA - zones, mobsByZone, species (~390KB)
  data/job-data.js         JOB_NAMES, JOB_VIT_RANK, rankToLetter, populateJobSelect
  data/weapon-data.js      WEAPON_TYPES (pDIF cap + hit-rate cap), populateWeaponSelect
  lib/core.js              namespace bootstrap, clamp/randomInt/escapeHtml/resolve/num/minMax
  lib/buffs.js             food model + job-ability uptime/rotation model             (pure)
  lib/damage.js            THE ENGINE - fSTR, fTP, pDIF, alpha, mainBase, simulate  (pure)
  lib/mob-stats.js         getBaseToRank, getBaseDefEva, derive                       (pure)
  lib/chart.js             canvas setup, plot box, axes, scales, shared tooltip
  lib/presets.js           slot store + UI (localStorage, import/export, dirty tracking)
  components/mob-selector.js
  components/fstr-panel.js
  components/attack-panel.js
  components/histogram.js
pages/
  ws-calculator.html       the weaponskill calculator
```

## Conventions

**Classic scripts, one global namespace.** Everything assigns into `window.FFXI`.
Deliberately *not* ES modules: modules are blocked by CORS on `file://`, and
double-clicking a page in `pages/` has to keep working. There is no build step.

Load order matters — the shared theme, then `core.js`, then data, then lib, then
components:

```html
<script src="../../shared-ui/js/theme.js"></script>
<script src="../shared/lib/core.js"></script>
<script src="../shared/data/mob-data.js"></script>
...
```

**Styling is shared with the DPS meter.** The palette and every primitive
(`.card`, `.field`, `table.data`, `.chart-wrap`, buttons) live in
[`../shared-ui/`](../shared-ui); `shared/css/theme.css` holds only what is
specific to this app. Canvas colours are read from the same custom properties
through `FFXITheme`, so there is no second palette to keep in sync.

**The `lib/` math layer is pure.** `damage.js`, `mob-stats.js` and `buffs.js`
contain no DOM reads or writes and no element ids. This is the layer validated
against the server Lua, so it must be callable with a plain object from a console
or a test harness. Keep it that way — if a formula needs a value, it takes it as
an argument.

**Components own their DOM, not their inputs.** Each `components/*.js` exposes
`create(config)`. Config supplies a `mount` map of elements-or-ids plus callbacks
(`getInputs`, `onDerive`, `onApply`). Components never reach for another
component's ids or call another module's render function by name — the host page
wires those together. That is what makes them reusable on a second page.

**The page is live; nothing waits on a button.** The host listens for `input` and
`change` on the whole document and reruns everything — target derivation, both
panels, and the Monte Carlo sim — through a single `recalcAll()`. Components
expose `render()` / `derive()` and never wire a "recalculate" button of their own.
Keystrokes are debounced (~180 ms); discrete `change` events run immediately.

## Buffs by uptime

The calculator models Hasso, Berserk and Warcry as an **uptime percentage** — the
share of weaponskills that land with the buff up — defaulting to duration ÷ recast
from the server tables (Hasso 300s/60s → 100%, Berserk 180s/300s → 60%, Warcry
30s/300s → 10%).

Uptime is realised as a **rotation**, not independent coin flips: each ability is
pressed the moment its recast is up, and the weaponskill lands at a random phase of
the shared cycle. So abilities sharing a recast correlate — at default uptimes
Warcry is only ever up *inside* a Berserk window, never alone. The distribution
over buff states is enumerated in closed form rather than sampled, so it adds no
noise to the mean, and every state is listed in the UI with its probability and its
resulting damage.

Potency defaults derive from the job setup (main/sub job and their levels). Leave a
potency field blank to keep it derived; type a number to override it for job points,
merits or potency gear. Uptime 0 turns an ability off, and an ability whose job
requirement isn't met is disabled outright.

## Adding a page

Copy the head and script block from `pages/ws-calculator.html`, mount the
components you need, and supply the callbacks. Nothing in `shared/` or
`../shared-ui/` needs to change.

## Validating against the server

The engine is callable directly:

```js
FFXI.damage.simulate(readParams(), 200000)      // full Monte Carlo
FFXI.damage.calcMainBase(readParams())          // deterministic part only
FFXI.damage.calcFSTR_PC(str, vit, weaponRank)
FFXI.damage.calculateMeleePDIF(att, def, isCrit, lvlCorr, lvlDiff, cap, dlPlus, dlPct, critDmg)
```

`calcFSTR_PC`, `fTPInterp`, `calculateMeleePDIF`, `buffStates`, `buffPotency` and
`jobSetup` are also re-exported as page globals on the calculator so the existing
console workflow keeps working.

Compare means at ~200k trials; agreement inside ~0.5% is sampling noise. The
deterministic outputs (`mainBase`, `fSTR`, `wsc`, `alpha`) should match exactly.

For the buff layer, the invariant to hold onto is that each ability's marginal
probability across `buffStates()` equals its uptime field exactly, for any
combination of uptimes.

## Known gaps (carried over, not introduced here)

- ATT is a manual input, not derived from STR — so Hasso's STR bonus reaches fSTR
  and WSC but not ATT.
- Weapon rank is hand-entered rather than computed as `floor(weaponDmg / 9)`.
- Job points, merits and potency gear aren't modeled; the manual potency overrides
  in the Buffs section cover them. Warcry's Savagery TP bonus and the job-point flat
  ATT on Berserk/Warcry aren't applied.
- The pre-level-50 subjob stat curve isn't ported; the halved fallback is used and
  the result is flagged as approximate.
- E-rank DEF below level 51 is interpolated from the A–D pattern, and flagged.
