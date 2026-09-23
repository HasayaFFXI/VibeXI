# shared-calc

The calculation layer both weaponskill calculators run on. One engine, one set of
server-derived data tables, one set of components.

```
lib/core.js         namespace bootstrap + clamp/randomInt/escapeHtml/resolve/num/minMax
lib/damage.js       THE ENGINE — fSTR, fTP, WSC, alpha, pDIF, multi-attack, simulate   (pure)
lib/buffs.js        food model + job-ability uptime/rotation model                     (pure)
lib/mob-stats.js    VIT/DEF/AGI/EVA derivation, a mobutils.cpp port                    (pure)
lib/chart.js        canvas setup, plot box, axes, scales, shared tooltip
lib/presets.js      10-slot localStorage presets: store + UI + dirty tracking
data/mob-data.js    MOB_DATA: zones, mobsByZone, species. ~390KB, ONE LINE.
data/job-data.js    JOB_NAMES, JOB_VIT_RANK, JOB_AGI_RANK, JOB_EVA_SKILL_RANK, rank helpers
data/weapon-data.js WEAPON_TYPES (pDIF cap + hit-rate cap), populateWeaponSelect
data/ws-data.js     WS constants per weaponskill, accFromSkill, hitRateFromAccEva
components/         mob-selector.js  fstr-panel.js  attack-panel.js  histogram.js
css/calc.css        the calculator shell — mob grid, buff table, presets, run bar, results
```

This lives at the repo root, a sibling of `apps/` and of `shared-ui/`, and its
position is load-bearing: both calculator pages reach it by relative path,
`../../../shared-calc/...` from `apps/<app>/pages/`. Moving or renaming it breaks
both pages silently.

`shared-ui/` is the *design system*; `shared-calc/` is the *math and components*.
They are separate because the damage meter wants the first and has no use for the
second.

## Source of truth

Every formula here is a port of the LandSandBoat-derivative server kept **outside
this repo** at `C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`.
`apps/penta-calculator/PentaThrustDamageCalc.md` cites the exact file and line
for each one; start there rather than re-deriving from a wiki.

The live server runs `USE_ADOULIN_WEAPON_SKILL_CHANGES = false`, contradicting the
repo default of `true`, with no override file in `settings/` — so reading the repo
alone gives the wrong answer. Assume alpha applies. Both pages ship the checkbox
unticked.

## Conventions

**Classic scripts, one global namespace.** Everything assigns into `window.FFXI`.
Deliberately *not* ES modules: modules are CORS-blocked on `file://`, and
double-clicking a page has to keep working. There is no build step.

**Load order matters**, and is the same for every page:

```html
<script src="../../../shared-ui/js/theme.js"></script>   <!-- first: chart.js reads it at load -->
<script src="../../../shared-calc/lib/core.js"></script>  <!-- creates the namespace -->
<script src="../../../shared-calc/data/…"></script>
<script src="../../../shared-calc/lib/buffs.js"></script> <!-- before damage.js -->
<script src="../../../shared-calc/lib/damage.js"></script>
<script src="../../../shared-calc/lib/…"></script>
<script src="../../../shared-calc/components/…"></script>
```

**`lib/damage.js`, `lib/mob-stats.js` and `lib/buffs.js` are pure.** No
`document`, no element ids. They are what gets validated against the server, so
they must be callable with a plain object from a console or a harness. If a
formula needs a value, it takes it as an argument.

**Components own their DOM, not their inputs.** Each exposes `create(config)`
taking a `mount` map of elements-or-ids plus callbacks (`getInputs`, `onDerive`,
`onApply`). They never reference another component's ids or call another module's
render function by name. That is what makes them reusable on a second page — and
it is why `penta-calculator` needed no component rewrites, only extra mounts.

**Canvas colours come from `FFXITheme`, never a table in the JS.**
`FFXI.chart.COLORS` is a live view over the CSS custom properties.

## Extending the engine without breaking the other page

The engine serves two weaponskills with different shapes. Every mechanic one page
needs and the other doesn't is **opt-in on a parameter that defaults to a
no-op**:

| Parameter | Absent means |
|---|---|
| `wscMods` + `stats` | the single-stat `wscStat` / `wscWeight` path |
| `atkVaries` | `atkMult = 1` |
| `accVaries` | `accBonus = 0` |
| `daRate` / `taRate` / `qaRate` | no multi-attack swings |
| `bonusWSmods` | 0 |
| `buffStates` | one unbuffed state from `str` / `att` / `hitRate` |

`calculateMeleePDIF`'s positional signature is append-only, for the same reason:
the console-validation workflow calls it positionally.

`fTPInterp` returns **1** with no table, `tpFactor` returns **0**. The server has
both (`xi.weaponskills.fTP`, `xi.combat.physical.calculateTPfactor`) and that
single difference is why both are kept — an additive accuracy bonus must not
default to a multiplicative identity.

`FFXI.mobStats.derive()` returns `AGI`/`EVA` as `null` unless the AGI ranks are
supplied, and `mobSelector`'s AGI/evasion mounts are optional, so a page that
only needs VIT and DEF is unaffected.

## Validating a change

Serve the repo root and open a page over http — `file://` in the preview pane
renders as a `data:` URL snapshot where no relative script loads:

```bash
python -m http.server 8740
```

Then, in the page console:

```js
FFXI.damage.calcMainBase(readParams())      // deterministic part only
FFXI.damage.simulate(readParams(), 200000)  // full Monte Carlo
FFXI.damage.calcWSC({str:123, dex:98}, {str:0.2, dex:0.2}, {})
buffStates()                                // the exact rotation distribution
```

Deterministic outputs (`mainBase`, `fSTR`, `wsc`, `alpha`) must match **exactly**
across a change; simulated means inside ~0.5% is sampling noise. After any engine
edit, re-run ws-calculator's shipped defaults and confirm
`fSTR 8.5, wsc 44, alpha 0.83, mainBase 128` — if `mainBase` moved, a supposedly
opt-in parameter is not defaulting to a no-op.

The pane caches scripts hard. To be sure an edit landed:

```js
const src = await fetch('/shared-calc/lib/damage.js', {cache:'reload'}).then(r => r.text());
(0, eval)(src); recalcAll();
```

**Never read `data/mob-data.js` with the Read tool** — one 390KB line, ~250k
tokens. Move and inspect it with shell only.
