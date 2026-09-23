# penta-calculator — working notes

Penta Thrust damage simulator. One page, opened straight off disk. **No build
step, no bundler, no Node.** `README.md` is the human overview;
`PentaThrustDamageCalc.md` is the formula reference and the full input inventory;
this file is the operational detail.

## Paths

```
../../shared-ui/css/ffxi-theme.css   THE design system, shared with every app
../../shared-ui/js/theme.js          window.FFXITheme, the same tokens for <canvas>
../../shared-calc/                   THE engine, data and components — shared with ws-calculator
pages/penta-calculator.html          the whole app
PentaThrustDamageCalc.md             the derivation; cites server file:line for every formula
```

`../../shared-calc/README.md` documents the shared layer. **Nothing in this app
owns a formula** — the only JS here is page wiring, the DEX panel, and the
readouts.

## Read this first

`PentaThrustDamageCalc.md` §4 lists three things that catch people out, and all
three are load-bearing for how this page is built:

1. **The weaponskill cannot crit naturally.** `weaponskills.lua:162` gates the
   crit roll on `wsParams.critVaries`, which Penta Thrust does not define. The
   crit-rate field exists to model Mighty Strikes or Sneak Attack, and it ships
   at 0. Do not "fix" it to a dDEX-derived rate.
2. **fTP is flat 1.0 at every TP.** TP reaches damage *only* through
   `accVaries`. If a change makes `mainBase` move with TP, it is wrong.
3. **`atkVaries` 0.875 applies inside pDIF**, to ATT, before the ratio — not to
   the damage. The attack panel therefore plots `ATT x 0.875`, deliberately.

## Hard rules

Everything in `../ws-calculator/CLAUDE.md` §"Hard rules" applies here too —
classic scripts on `window.FFXI`, never ES modules; the load order; `lib/` stays
DOM-free; components never reach for each other's ids; the page is live with no
recalculate button. Those rules are about the shared layer, so they are the same
rules. Additions specific to this app:

- **The Weaponskill card is output, not input.** It renders from
  `FFXI.data.WS.penta_thrust` in `shared-calc/data/ws-data.js`. If a constant is
  wrong, fix it there against the Lua — do not turn the card back into form
  fields, or the page stops being a Penta Thrust calculator and becomes a second
  copy of ws-calculator.
- **Hit rate is derived, not typed.** `actorAcc()` -> `baseHitRate()` ->
  `buffStates()` -> the engine. The `#hitRateOverride` field short-circuits it and
  exists for "I already know the number I want to model"; it is not the normal
  path. The whole point of deriving is that DEX shows up in both places it
  belongs.
- **This page derives the target on first paint** (`pendingDerive = true` before
  the initial `recalcAll()`), unlike ws-calculator, which leaves its hidden
  `#vit`/`#def` at placeholders until a mob field is touched. With a derived hit
  rate on screen, a target that disagrees with the panel above it reads as a bug.
  The hidden defaults are kept in sync with the panel's own defaults anyway, so
  the first derive is a no-op — keep it that way if you change either.
- **`#eva` is a hidden field like `#vit`/`#def`.** A sweep over
  `input[type=number]` will miss all four.

## Gotchas

- **The preview pane caches `file://` and `localhost` scripts hard**, and neither
  `force: true` nor `?v=N` busts it. A shared `.js` edit can silently not land.
  Verify by re-evaluating the changed source in-page:
  ```js
  const src = await fetch('/shared-calc/lib/damage.js', {cache:'reload'}).then(r=>r.text());
  (0, eval)(src); recalcAll();
  ```
  Opening the page over `file://` in the pane is worse: it renders as a `data:`
  URL snapshot, so every relative `<script src>` fails and `FFXI` is undefined.
  Use the `calc-static` launch config (`python -m http.server 8740` at the repo
  root) and browse to `/apps/penta-calculator/pages/penta-calculator.html`.
- **Never read `shared-calc/data/mob-data.js` with the Read tool** — one 390KB
  line, ~250k tokens. Shell only.
- **The DEX panel runs a sim per sample point**, thirteen of them at 3000 trials
  each, on every recalc. That is the main cost of a keystroke on this page. If it
  gets sluggish, cut the trial count or widen the step — do not memoise against
  a stale parameter object.
- Chrome restores form state across reloads, so a "fresh" page can still hold
  values from earlier poking.

## Relationship to ws-calculator

The engine is shared and **must stay backward compatible**. Every Penta mechanic
is opt-in on a parameter that defaults to a no-op:

| Parameter | Absent means |
|---|---|
| `wscMods` / `stats` | fall back to the single-stat `wscStat` path |
| `atkVaries` | `atkMult = 1` |
| `accVaries` | `accBonus = 0` (via `tpFactor`, not `fTPInterp` — see below) |
| `daRate` / `taRate` / `qaRate` | no extra swings |
| `bonusWSmods` | 0 |

`fTPInterp` returns **1** with no table; `tpFactor` returns **0**. Both exist on
the server (`xi.weaponskills.fTP` and `xi.combat.physical.calculateTPfactor`) and
that difference is the entire reason both are kept — an additive accuracy bonus
must not default to 1.

After any engine change, re-run ws-calculator's own deterministic checks and
confirm they are **exactly** unchanged:

```js
FFXI.damage.calcMainBase(readParams())   // fSTR 8.5, wsc 44, alpha 0.83, mainBase 128
                                         // on ws-calculator's shipped defaults
```

## Known gaps

`PentaThrustDamageCalc.md` §5 and §6 are the authoritative list. The ones most
likely to bite:

- The weaponskill stopping early once accumulated damage exceeds the target's HP
  is **not** modelled, so results overstate damage against a target you overkill.
- `PIERCE_SDT` is not modelled, and polearm is a piercing weapon — on families
  with a large piercing modifier this is a big multiplier in either direction.
- `ALL_WSDMG_FIRST_HIT` is not modelled; only the all-hits bonus is.
- ATT is entered, not derived from STR, so a STR buff moves fSTR and WSC but not
  ATT. Inherited from ws-calculator.
- `accVaries` at 3000 TP is `60` here, but the era module overrides it to `50`
  and **both carry a `TODO` saying the number is unverified**. If the live server
  loads the era module, change it in `ws-data.js`.
