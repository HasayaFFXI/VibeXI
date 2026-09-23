# Penta Thrust Calculator

Monte-Carlo damage simulator for **Penta Thrust**, the five-hit polearm
weaponskill. Open `pages/penta-calculator.html` straight off disk.

[`PentaThrustDamageCalc.md`](PentaThrustDamageCalc.md) is the reference: the full
damage equation, every input with the server file and line it comes from, and
what is deliberately not modelled. Read that before changing any formula here.

## Why this isn't just ws-calculator with the numbers changed

[`../ws-calculator`](../ws-calculator) was built around Tachi: Jinpu — two hits,
one STR weaponskill modifier, no TP-varying anything. Penta Thrust has three
mechanics that weaponskill has no representation for:

- **Dual-stat WSC**, 20% STR *and* 20% DEX, with each term floored separately.
- **`accVaries {0, 30, 60}`** — accuracy, not damage, scales with TP.
- **`atkVaries 0.875`** — a 12.5% attack penalty applied *inside* pDIF, so it
  moves the attack/defense ratio rather than scaling the damage.

All three now live in the shared engine at [`../../shared-calc`](../../shared-calc),
behind parameters that default to no-ops, so ws-calculator's numbers are
unchanged.

The fourth thing, and the reason the page is shaped the way it is: on a
two-handed weapon DEX is worth 0.75 accuracy per point, so on this weaponskill
**DEX is paid twice** — once into the WSC term and once into the hit rate that
decides how many of the five hits land. That is what the DEX curve panel is for,
and it is why this page derives its hit rate from ACC and EVA instead of taking
a hit-rate percentage the way ws-calculator does.

## Layout

```
PentaThrustDamageCalc.md    the derivation and the full input inventory
pages/penta-calculator.html the app
```

Everything else is shared: the design system in
[`../../shared-ui`](../../shared-ui) and the engine, data and components in
[`../../shared-calc`](../../shared-calc). This app is one HTML file and a
document.

## What the page shows

- **Weaponskill** — read-only. These are the `params` the server builds for
  weaponskill 116, rendered from `shared-calc/data/ws-data.js`, not fields you
  fill in. Alongside them, the three findings worth knowing: TP buys accuracy
  and not damage, the weaponskill cannot crit naturally, and the attack penalty
  is nonlinear.
- **Accuracy** — derived from polearm skill, DEX, gear and the gorget, then shown
  against the target's EVA. It tells you when you are over the 95% cap and how
  much accuracy is doing nothing.
- **DEX curve** — average damage against DEX, with the WSC term and the hit rate
  it implies in the same table.
- **Mob panel** — now derives AGI and EVA as well as VIT and DEF, because hit
  rate needs them.
- **Multi-attack** — DA/TA/QA do proc inside a weaponskill, capped at two procs
  and eight swings total. Defaults to zero.
- Food, buffs-by-uptime, presets, fSTR tiers, attack curve and the histogram all
  behave exactly as they do in ws-calculator.

The attack curve plots ATT **after** the 0.875 penalty, since that is the number
that actually enters the ratio.

## Defaults

A level 99 DRG/SAM: 424 polearm skill (the A+ cap at 99), STR 130, DEX 120,
ATT 900, against a level 99 C-rank target. `USE_ADOULIN_WEAPON_SKILL_CHANGES` is
unticked to match the live server, so alpha applies.

Berserk and Warcry show as unavailable on those defaults — DRG/SAM has no access
to them. Switch the subjob to WAR and they light up.

## Validating a change

```js
FFXI.damage.simulate(readParams(), 200000)   // full run
FFXI.damage.calcMainBase(readParams())       // deterministic part only
FFXI.damage.calcWSC({str:123, dex:98}, WS.wsc, {})   // dual-stat WSC
actorAcc(); baseHitRate(); dexCurve()        // the accuracy chain
```

The checks that matter for this page specifically:

```js
// fTP is flat, so mainBase must not move with TP -- only the hit rate may
[1000, 2000, 3000].map(tp => { $('tp').value = tp;
  const s = FFXI.damage.simulate(readParams(), 40000);
  return [s.mainBase, s.states[0].hitRate, Math.round(s.avgAll)]; });

// DEX must move BOTH mainBase and accuracy
[80, 140].map(d => { $('dex').value = d;
  return [FFXI.damage.calcMainBase(readParams()).mainBase, actorAcc()]; });
```
