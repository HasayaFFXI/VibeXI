# Penta Thrust — damage calculation and every input

Traced from the LandSandBoat-derivative server at
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`, which lives outside
this repo. Every formula below cites the file and line it came from. Paths are
relative to that checkout.

The short version: Penta Thrust is a **five-hit polearm weaponskill with no fTP
scaling, no natural critical hits, a dual-stat WSC, a 12.5% attack penalty, and
an accuracy bonus that grows with TP**. Three of those five are unusual enough
that a generic weaponskill calculator gets Penta Thrust wrong.

---

## 1. What the weaponskill is

### The Lua definition

`scripts/actions/weaponskills/penta_thrust.lua` — the whole thing:

```lua
params.numHits   = 5
params.ftpMod    = { 1.0, 1.0, 1.0 }
params.str_wsc   = 0.2  params.dex_wsc = 0.2
params.accVaries = { 0, 30, 60 }            -- TODO: verify exact number
params.atkVaries = { 0.875, 0.875, 0.875 }
xi.weaponskills.doPhysicalWeaponskill(player, target, wsID, params, tp, action, primary, taChar)
```

Note the `TODO` on `accVaries` — that number is the server's own guess. The era
module (`modules/era/lua/actions/weaponskills/polearm.lua:89`) overrides the
3000 TP anchor to `50` and carries the same caveat. If the live server runs the
era module, the 3000 TP accuracy bonus is +50, not +60.

### The database row

`sql/weapon_skills.sql:157`:

```sql
INSERT INTO `weapon_skills` VALUES
  (116,'penta_thrust',0x01000000000001000000000100010000000000000000,8,150,0,125,2000,3,1,0,2,0,0,0,0);
```

| Column | Value | Meaning |
|---|---|---|
| `weaponskillid` | 116 | Also the index into the per-WS damage mod: `570 + 116 = 686` |
| `type` | 8 | Polearm |
| `skilllevel` | 150 | Polearm skill required |
| `element` | 0 | None — there is no magic component |
| `jobs` | mask | WAR, PLD, SAM, DRG |
| `primary_sc` | 2 | `COMPRESSION`, a Lv1 **Dark** property |
| `aoe` | 1 | Not AoE — `isAoE()` tests `m_AOE == 2` (`weapon_skill.cpp:49`) |

`primary_sc = 2` is why the Shadow Gorget and Shadow Belt apply: the gear fTP
bonus is gated on the weaponskill having a skillchain property whose element
matches the gear's (`physical_utilities.lua:476-501`).

---

## 2. The damage equation, in evaluation order

```
tp_eff  = min(spentTP + TP_BONUS, 3000)                      battleutils.cpp:5874

alpha   = 1                       if USE_ADOULIN_WEAPON_SKILL_CHANGES
        = 0.85                    if not, and level > 75     weaponskills.lua:304
        = 0.83                    if not, and level = 75

WSC     = floor(STR x (0.20 + WS_STR_BONUS/100))
        + floor(DEX x (0.20 + WS_DEX_BONUS/100))             physical_utilities.lua:424

mainBase = floor(D + fSTR + bonusWSmods + WSC x alpha)       weaponskills.lua:317

fTP      = fTP(tp, {1,1,1}) + gearFTP   = 1.0 + gearFTP      weaponskills.lua:320
         = 1.0                             for hits 2..5     weaponskills.lua:434

atkMod   = fTP(tp, {0.875,...})         = 0.875              weaponskills.lua:120
accMod   = fTP(tp, {0,30,60})           = 0 .. 60            weaponskills.lua:702

per hit, in this order: miss -> parry -> shadows -> crit -> pDIF -> block/guard
    wRatio  = floor(ATT x atkMod x flourish) / max(1, DEF)   physical_utilities.lua:646
              (+1.0 if critical)
    pDIF    = piecewise caps, a spike chance, a uniform roll,
              then x (1 + randInt(0,5)/100)                  physical_utilities.lua:704-761
    hitDmg  = mainBase x fTP x pDIF                          weaponskills.lua:177
    hitDmg  = physicalDmgTaken x (1 + PIERCE_SDT/10000)      weaponskills.lua:210
              then Phalanx, then Stoneskin

sum the hits, then once:
    final = sum x (100 + ALL_WSDMG_ALL_HITS + mod[686]) / 100   weaponskills.lua:643
          + firstHitDmg x ALL_WSDMG_FIRST_HIT / 100             weaponskills.lua:430
    final = floor(final) x WEAPON_SKILL_POWER                    weaponskills.lua:725
```

### fSTR (players)

`physical_utilities.lua:293-420`. `statDiff = STR − target VIT`, clamped to
`[(7 + rank x 2) x -2, (14 + rank x 2) x 2]`, then a step table, then
**divided by 4** and clamped to `[-rank, rank + 8]` (lower cap is `-1` at rank 0).
The result is fractional; only `mainBase` is floored.

### Hit rate

`physical_hit_rate.lua:149-203`:

```
ACC     = GetAccFromSkill(polearm skill)                     battle_entity.cpp:1278
        + floor(DEX x 0.75)        two-handed multiplier     battle_entity.cpp:1482
        + TWOHAND_ACC + Mod::ACC + Accuracy merits
        + min(ACC x FOOD_ACCP/100, FOOD_ACC_CAP)

bonus   = ceil(gearFTP x 100) + WSACC + accMod               weaponskills.lua:659,697,703
        + 100 on hit 1 only                                  weaponskills.lua:706

if below the target's level:  ACC += (myLvl − targetLvl) x 4
hitRate = clamp((75 + (ACC + bonus − EVA) / 2) / 100, 0.20, 0.95)
```

`GetAccFromSkill` is 1:1 up to 200 skill, then `x0.9` to 400, `x0.8` to 600,
`x0.9` beyond — so 424 polearm skill is worth 399 accuracy, not 424.

The 0.95 cap is because polearm is two-handed (`physical_hit_rate.lua:51`).
The level term is a **penalty only** for a player: being above the target's
level grants nothing.

Target evasion, for a mob:
`EVA = f(level, evasion rank) + floor(AGI / 2)` — `mobutils.cpp:931` seeds the
base from the better of the mob's main/sub **evasion skill** rank, and
`battle_entity.cpp:1661` adds the AGI term. There is no `+8` constant, unlike DEF.

### ATT

`battle_entity.cpp:1128-1180`, then the percentage pass:

```
ATT  = 8 + Mod::ATT + STR x 1.0 + polearm skill        (x1.0 because two-handed)
ATT  = max(1, ATT + ATT x ATTP/100 + min(ATT x FOOD_ATTP/100, FOOD_ATT_CAP))
```

Ability ATT% and food ATT% come off the **same pre-percentage base and add** —
they do not compound — and only the food term is capped.

---

## 3. Every input

"Modelled" is whether `apps/penta-calculator` simulates it today.

### The weaponskill itself — fixed constants, not inputs

| Input | Value | Enters at | Source | Modelled |
|---|---|---|---|---|
| `numHits` | 5 | hit loop bound | penta_thrust.lua:20 | yes |
| `ftpMod` | `{1.0, 1.0, 1.0}` | fTP, hit 1 only | :21 | yes |
| `str_wsc` | 0.20 | WSC | :22 | yes |
| `dex_wsc` | 0.20 | WSC | :22 | yes |
| `accVaries` | `{0, 30, 60}` | `bonusAcc` | :23 | yes |
| `atkVaries` | `{0.875, 0.875, 0.875}` | ATT inside pDIF | :24 | yes |
| `critVaries` | absent | crit gate | — | yes (as 0) |
| `multiHitfTP` | absent | fTP after hit 1 | — | yes |
| `hybridWS` | absent | magic component | — | n/a |
| `ignoredDefense` | absent | target DEF | — | n/a |
| pDIF weapon cap | 3.75 (polearm) | `pDifFinalCap` | physical_utilities.lua:60 | yes |
| hit-rate cap | 0.95 (two-handed) | hit rate clamp | physical_hit_rate.lua:57 | yes |

### Weapon and actor stats

| Input | Enters at | Server symbol | Source | Modelled |
|---|---|---|---|---|
| Weapon damage **D** | `mainBase` | `getWeaponDmg()` | weaponskills.lua:1048 | yes |
| Weapon rank | fSTR clamps | `getWeaponDmgRank()` | physical_utilities.lua:294 | yes (manual) |
| **STR** | fSTR, WSC, ATT | `Mod::STR` | :255, :437 | yes |
| **DEX** | WSC **and** accuracy | `Mod::DEX` | :438, battle_entity.cpp:1484 | yes |
| **ATT** | pDIF ratio | `getStat(ATT, slot)` | physical_utilities.lua:646 | yes (manual) |
| Polearm skill | accuracy, and ATT | `GetSkill()` | battle_entity.cpp:1477, :1174 | yes (accuracy only) |
| Actor level | alpha, level correction | `getMainLvl()` | weaponskills.lua:306 | yes |
| Current **TP** | fTP, accVaries, atkVaries | `tp` | :320, :702, :120 | yes |

### Target

| Input | Enters at | Source | Modelled |
|---|---|---|---|
| **VIT** | fSTR `statDiff` | physical_utilities.lua:255 | yes (derived) |
| **DEF** | pDIF ratio | :630 | yes (derived) |
| **AGI** | EVA, and crit dDEX | battle_entity.cpp:1661 | yes (derived) |
| **EVA** | hit rate | physical_hit_rate.lua:187 | yes (derived) |
| Level | level correction, both kinds | :152, physical_utilities.lua:685 | yes |
| `PIERCE_SDT` | per-hit damage taken | weaponskills.lua:210 | **no** |
| `physicalDmgTaken` | per-hit damage taken | :202 | **no** |
| `CRIT_DEF_BONUS` | crit damage | physical_utilities.lua:765 | folded into the crit-damage field |
| Current HP | stops the WS early | :296, :463 | **no** |

### Rates

| Input | Enters at | Source | Modelled |
|---|---|---|---|
| Hit rate | per-hit miss roll | weaponskills.lua:131 | yes (derived, overridable) |
| Hit-1 accuracy bonus | `+100` ACC | :706 | yes |
| Crit rate | per-hit crit roll | :163 | yes — but see §4 |
| `CRIT_DMG_INCREASE` | pDIF multiplier, capped +100% | physical_utilities.lua:765 | yes |
| Double / Triple / Quad Attack | extra swings, max 2 procs | weaponskills.lua:59, :494 | yes |
| `MYTHIC_OCC_ATT_TWICE/THRICE` | extra swings, hit 1 only | :86-90 | **no** |

### Gear and buff modifiers

| Input | Enters at | Server mod | Source | Modelled |
|---|---|---|---|---|
| `WS_STR_BONUS` | WSC STR multiplier | 980 | physical_utilities.lua:437 | yes |
| `WS_DEX_BONUS` | WSC DEX multiplier | 957 | :438 | yes |
| Gorget / belt fTP | `bonusfTP`, **and** accuracy | per-element /256 | :495, weaponskills.lua:659 | yes, both |
| `WSACC` | `bonusAcc` | — | weaponskills.lua:697 | yes |
| `DAMAGE_LIMIT` | raises pDIF cap (flat/100) | 1080 | physical_utilities.lua:710 | yes |
| `DAMAGE_LIMITP` | raises pDIF cap (%) | 1081 | :711 | yes |
| `ALL_WSDMG_ALL_HITS` | final multiplier | 840 | weaponskills.lua:633 | yes |
| per-WS damage mod | final multiplier | 570+116 = **686** | :636 | yes (same field) |
| `ALL_WSDMG_FIRST_HIT` | added after the multiplier | 841 | :430, :644 | **no** |
| `bonusWSmods` | inside the `mainBase` floor | — | :317 | yes (as 0) |
| Food ATT% / cap, food STR | ATT, fSTR | `FOOD_ATTP` etc. | battle_entity.cpp:1509 | yes |
| Hasso / Berserk / Warcry | STR, ATT%, accuracy | — | job_utils/*.lua | yes (by uptime) |
| Building Flourish | `+40` ACC, `x1.25` ATT | — | physical_hit_rate.lua:95, physical_utilities.lua:640 | **no** |

### Server settings

| Setting | Effect | Source | Modelled |
|---|---|---|---|
| `USE_ADOULIN_WEAPON_SKILL_CHANGES` | alpha 1 vs the legacy curve | settings/default/main.lua:163 | yes (checkbox) |
| `WEAPON_SKILL_POWER` | flat final multiplier, 1.000 | :138 | no — it is 1.0 |
| `TWO_HANDED_DEX_ACCURACY_MULTIPLIER` | 0.75 | :149 | yes (hard-coded) |
| `TWO_HANDED_STR_ATTACK_MULTIPLIER` | 1.0 | :142 | n/a — ATT is entered directly |
| Level-corrected zone | pDIF and accuracy penalties | `isLevelCorrectedZone` | yes (checkbox) |

**The repo default for `USE_ADOULIN_WEAPON_SKILL_CHANGES` is `true`, but the live
server runs it `false`** and there is no override file in `settings/` — so reading
the repo alone gives the wrong answer. Assume alpha applies. Both calculator pages
ship the checkbox unticked to match.

---

## 4. Three results that surprise people

### Penta Thrust cannot critically hit on this server

`weaponskills.lua:162`:

```lua
criticalHit = (wsParams.critVaries and critChance <= calcParams.critRate) or
    calcParams.forcedFirstCrit or
    calcParams.mightyStrikesApplicable
```

The natural crit roll is gated on `wsParams.critVaries` existing, and Penta
Thrust does not define it. `calcParams.critRate` is likewise left at 0
(`:326`). So crit rate is not merely low — it is **unreachable**. Critical Hit
Rate gear does nothing for this weaponskill.

The only crits available are `forcedFirstCrit` (Sneak Attack, or Trick Attack
with the Assassin trait — first hit only, cleared at `:426`) and Mighty Strikes,
which forces every hit.

### fTP is flat, so TP buys accuracy rather than damage

`ftpMod = {1.0, 1.0, 1.0}` means the fTP interpolation returns 1.0 at every TP
level. Going from 1000 to 3000 TP cannot raise a hit's damage.

What it does buy is `accVaries`: +60 accuracy, which
`hitdiff = (acc − eva) / 2` turns into **+30 points of hit rate**. Measured in
the calculator against a target that leaves you at 49.5% hit rate, 1000 → 3000 TP
moves average damage from 929 to 1297 — a 40% gain, entirely from landing more of
the five hits. Against a target where you are already at the 95% cap, the same
change is worth exactly **zero**.

That makes "should I weaponskill at 1000 or 3000 TP" a question about the
accuracy cap, not about damage.

### The 0.875 attack penalty is not a flat 12.5% damage loss

`physical_utilities.lua:646`:

```lua
actorAttack = math.max(1, math.floor(actor:getStat(xi.mod.ATT, weaponSlot) * wsAttackMod * flourishBonus))
```

The multiplier is applied to ATT **inside** pDIF, before the attack/defense
ratio — so it does not scale the damage, it moves you along the pDIF cap curve.
That curve is piecewise (`wRatioCapPC`, `:510`), so the cost depends entirely on
where you sit:

```
ATT 1000 vs DEF 1000  ->  ratio 1.000  ->  pDIF bounds [0.711, 1.300]
ATT  875 vs DEF 1000  ->  ratio 0.875  ->  pDIF bounds [0.567, 1.175]
```

Near a breakpoint the penalty costs far more than 12.5%; deep inside a capped
region it costs far less.

---

## 5. What the calculator does not model

Each of these is real server behaviour left out of `apps/penta-calculator`, with
the line to start from.

- **The weaponskill stops early once it has done enough damage.** Every hit loop
  is guarded by `finaldmg < targetHp`, where `targetHp` includes Stoneskin
  (`weaponskills.lua:296-301`, `:463`). On an overkill the later hits never
  happen, so real average damage against a low-HP target is lower than simulated.
- **Shadows, parry, block and guard** (`:142-191`). Utsusemi/Blink absorb a hit
  outright. Note the server's own oddity: guard reduces `calcParams.pdif` *after*
  `hitDamage` has already been computed, so guarding a weaponskill hit costs
  nothing that hit.
- **`PIERCE_SDT` and `physicalDmgTaken`** (`:202-210`). Polearm is a piercing
  weapon, so the target's piercing-specific damage taken applies per hit — this
  can be a large multiplier either way on some families.
- **Sneak Attack's DEX addition** (`:405`). On THF main, SA adds
  `pdif x floor(DEX x (1 + SNEAK_ATK_DEX/100))` to the first hit, on top of
  forcing a crit and guaranteeing the hit.
- **`ALL_WSDMG_FIRST_HIT`** (`:430`), which is added *after* the all-hits
  multiplier rather than being part of it.
- **Building Flourish**, worth `+40` accuracy and a `x1.25` attack multiplier.
- **Mythic `OCC_ATT_TWICE/THRICE`**, which only proc on hit 1.
- **Phalanx and Stoneskin** (`:227-228`), applied per hit.
- **Skillchains.** Penta Thrust opens Compression; skillchain damage is resolved
  in C++ after the Lua returns (`char_entity.cpp:1724`).
- **Conserve TP and `WS_NO_DEPLETE`** (`weaponskill_state.cpp:94`) — these affect
  what TP you keep, not the damage of this weaponskill.
- **Angon**, and anything else that lowers the target's DEF, is only representable
  by lowering the DEF input by hand.

## 6. Two places the model is approximate rather than absent

- **ATT is entered, not derived from STR.** So a buff that raises STR moves fSTR
  and the WSC term but not ATT, where the server would also feed
  `STR x TWO_HANDED_STR_ATTACK_MULTIPLIER` into it. Carried over from
  `ws-calculator`.
- **The pDIF roll is continuous here, quantised on the server.**
  `physical_utilities.lua:754` rolls `randomInt(lower x 1000, upper x 1000) / 1000`,
  a 0.001 grid. The calculator draws a continuous uniform over the same interval.
  The difference is far below sampling noise at any useful trial count.
