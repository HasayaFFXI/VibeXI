# VibeXI for developers: the event format

VibeXI's output is meant to be read by your own tools: a parser, a live meter,
a spreadsheet import. This page describes what's in the files. You don't
need to change the addon to use them.

For installing the addon and its settings, see the [README](README.md).

## The files

- [JSON Lines](https://jsonlines.org/): one JSON object per line, `\n` line
  endings.
- One file per load of the addon, named `<Character>-<YYYYMMDDHHMMSS>.jsonl`
  (local time) in the `EventDir` folder (see
  [Settings](README.md#settings-vibexiini)).
- The file only grows. Lines are flushed as soon as each packet is processed,
  so you can tail the file live. While the game is running, the last line may be incomplete:
  only parse lines that end in a newline.
- Open it for reading with write sharing allowed (for example
  `FileShare.ReadWrite` in .NET), because the addon keeps it open.
- Output is pure ASCII. Any byte outside printable ASCII is written as a
  `\u00XX` escape of that byte. Character and monster names are ASCII in
  practice.

## What gets recorded

- Actions taken by you, your party and your alliance, including trusts, and by
  their pets.
- Damage your side deals back on an enemy's action: counters, retaliation and
  spikes.
- Monsters your party or alliance killed, and deaths of anyone in your party
  or alliance.
- Damage dealt **to** your party is not recorded. Nothing is recorded while
  zoning.

## Reading a line

Every line has a `kind`. Switch on it, and skip kinds and fields you don't
recognise, so your reader keeps working when new ones are added. Optional
fields are left out rather than written as `null`.

| `kind` | What the line is |
|---|---|
| `melee`, `ranged`, `ws`, `magic`, `ability`, `mobtp`, `pet` | One result of an action: a hit or a miss on one target. `mobtp` is a monster TP move used by a charmed or summoned pet; `pet` is a pet ability. |
| `skillchain` | A skillchain closed by a weaponskill. |
| `addl` | An additional effect riding on a hit: an enspell, a drain, a Sneak Attack proc. |
| `reaction` | Damage dealt back on an enemy's swing: a counter, retaliation or spikes. |
| `heal` | HP restored to one target. |
| `enfeeble` | An enfeeble on one monster: a Sleep, Slow, Stun, Dispel, Lullaby, Light Shot. |
| `death` | A monster your side killed, or a death in your party or alliance. |
| `job` | A party member's job, written when it's first seen or when it changes. |
| `meta` | Which version of VibeXI wrote the file, and when it loaded. Always the first line. |

## The meta line

The first line of every file, written once when the addon loads:

| Field | Type | Meaning |
|---|---|---|
| `t` | integer | When the addon loaded, in Unix time. If it was loaded at character select, this is earlier than the first event. |
| `version` | string | The version of VibeXI that wrote the file, `major.minor.patch`, for example `1.3.0`. |

Use it to check the file before you read it: stop, or warn, when `version` is
older than the version your tool was built for. Compare the three numbers one
by one, not as strings (`1.10.0` is newer than `1.9.0`). A file whose first
line isn't `meta` was written before the meta line was added, so it's older
than any version you'd check for.

If the addon is unloaded and loaded again within the same second, the second
load writes to the same file, so a second `meta` line can turn up partway
through. Everything after it comes from that second load.

## Action lines

Every kind in the first four rows of the table above uses these fields:

| Field | Type | Meaning |
|---|---|---|
| `t` | integer | When it happened, in Unix time (seconds, UTC). |
| `seq` | integer | Order within the same second. File order is also event order. |
| `use` | integer | Groups the rows of one swing or one use. See below. |
| `kind` | string | See the table above. |
| `actor` | string | Who did it. |
| `actorKind` | string | `player`, `pet`, `mob`, `npc` or `other`. Trusts are `player`. |
| `action` | string | The action's name, for example `Tachi: Gekko`, `Fire IV`, `Attack`. |
| `actionId` | integer | The game's id for the action. For `skillchain`, `addl` and `reaction`, the result message id instead. |
| `target` | string | Who it hit. `Unknown` if the target couldn't be identified. |
| `targetKind` | string | Same values as `actorKind`. |
| `targetId` | integer | Optional. Present when `targetKind` is `mob`: the monster's id, the same one death lines carry. |
| `dmg` | integer | Damage dealt. `0` on a miss. |
| `hit` | boolean | `true` if it landed, `false` for a miss, dodge, parry, shadow, resist and so on. |
| `crit` | boolean | `true` for a critical hit. |
| `burst` | boolean | `true` for a magic burst. |
| `msg` | integer | The game's result message id, if you need detail beyond `hit`. |
| `owner` | string | Optional. Present when the actor is a pet: its master's name. |
| `pet` | string | Optional. Present with `owner`: the pet's name. |

Things worth knowing:

- **Counting swings and uses.** All the rows of one swing share a `use`. An
  area-effect weaponskill or spell that hits three monsters is three rows with
  the same `use`. A double attack is two swings, so two different `use` values.
  Count distinct `use` values to count attempts; add up `dmg` for damage.
- A skillchain or additional effect gets its own `use`, apart from the swing
  that caused it, and its damage is separate from that swing's `dmg`.
- **Telling monsters apart.** Two monsters with the same name have different
  `targetId` values, so group by `targetId` to total damage per monster. It
  names a spawn point, not a single life: see `targetId` under
  [Death lines](#death-lines). On a `reaction` line the target is the monster
  that swung, so `targetId` is that monster's.
- Melee names are `Attack`, `Offhand Attack`, `Kick Attack` and `Throwing`.
  Ranged is `Ranged Attack`. Skillchains are named `Skillchain: <name>`, and
  additional effects `Additional Effect`. Reactions are `Counter`,
  `Retaliation` or `Spikes`.
- When the addon has no name for an action, `action` is `#` and the id, for
  example `#1234`. This happens mostly with monster TP moves.
- On HorizonXI with `Horizon=1`, abilities such as the Jumps arrive as
  `ability`, not `ws`.
- **Support is not damage.** A line whose `actorKind` and `targetKind` are both
  `player` or `pet` is one of your side acting on another: a buff (Protect,
  Haste, a song, a roll), a status removal (Erase, Poisona) or a pet's ward.
  It is written as an ordinary `magic`, `ability` or `pet` line with `dmg` 0.
  `hit` is `true` when it took effect and `false` when it had no effect
  (`msg` 75 or 323, for example). Keep these lines out of damage counts and
  accuracy; count distinct `use` values to count casts.
- **Only listed actions are written on your side.** An action is written
  against one of your side only if its id is in `vx_support_ids.lua`: spells,
  job abilities and pets' abilities that the game won't let anyone aim at an
  enemy, taken from the server's own tables by `dev/gen-support-ids.py`.
  Everything else aimed at your side is dropped: damage, a miss, and every
  status put **on** your side, whoever put it there. A monster's actions
  aren't recorded at all, and an enfeeble or an attack that one of your own
  aims at your side (while charmed, say) is dropped too.

## Heal lines

`kind` is `heal`. They have the same `t`, `seq`, `use`, `actor`, `actorKind`,
`action`, `actionId`, `target`, `targetKind`, `msg`, `owner` and `pet` fields as
action lines, plus:

| Field | Type | Meaning |
|---|---|---|
| `hp` | integer | HP restored. |
| `via` | string | `magic`, `ability` or `pet`. |

A heal has no `dmg`, `hit`, `crit` or `burst`, so it can never be counted as
damage by mistake. A Curaga on five people is five rows sharing one `use`.

## Enfeeble lines

`kind` is `enfeeble`. An action is an enfeeble when its id is in
`vx_enfeeble_ids.lua`: spells, job abilities and pets' abilities that can be
aimed at an enemy and deal no damage, taken from the server's own tables and
scripts by `dev/gen-enfeeble-ids.py`. Dia, Bio and Drain deal damage, so they
are action lines. An enfeeble is never written as an action line, whether it
landed or not.

They have the same `t`, `seq`, `use`, `actor`, `actorKind`, `action`,
`actionId`, `target`, `targetKind`, `targetId`, `msg`, `owner` and `pet` fields
as action lines, plus:

| Field | Type | Meaning |
|---|---|---|
| `hit` | boolean | `true` if it took effect, `false` if it was resisted, missed or had no effect (the monster was already asleep, or immune). |
| `via` | string | `magic`, `ability` or `pet`. |

An enfeeble has no `dmg`, `crit` or `burst`. A Sleepga on three monsters is
three rows sharing one `use`. Only enfeebles on monsters are written: one
aimed at your side is dropped.

## Death lines

`kind` is `death`. Look at `targetKind` to tell a monster's death from a
player's.

| Field | Type | Meaning |
|---|---|---|
| `t` | integer | When it happened, in Unix time. |
| `seq` | integer | Order within the same second. |
| `actor` | string | Optional. Who killed the monster, always one of your side. Left out when nothing landed the killing blow (`msg` 20), and always left out on a player's death. |
| `actorKind` | string | Optional, present with `actor`. Same values as on action lines. |
| `target` | string | Who died. |
| `targetKind` | string | `mob` or `player`. |
| `targetId` | integer | Monsters only. The monster's id. See below. |
| `msg` | integer | `6` a monster was defeated, `97` a player was defeated, `20` either one fell with no killer. |
| `owner`, `pet` | string | Optional. Present when a monster's killer was a pet: its master's name and its own. |

A death has no `dmg`, `hit` or `use`, so it is never counted as damage or as
an attempt. To count kills of a monster, count its death lines.

- **Which monster deaths are written.** Your party or alliance must have the
  monster's claim. If something landed the killing blow, it must be one of
  yours too: a party or alliance member, a trust, or a pet. A monster you
  claimed but someone else finished isn't written.
- **Which player deaths are written.** Anyone in your party or alliance,
  whatever killed them. The line says only who died: there is no `actor`,
  because what a monster does to your party isn't recorded, its killing blow
  included.
- **Trusts and pets aren't included.** The game doesn't announce their deaths.
- **Timing.** A monster's death line arrives about a second after the hit
  that killed it, so it can come after a few more lines about other things.
- **Range.** Like everything else, deaths are only seen within range of your
  character.
- **`targetId`** is the monster's id, the number other tools often show in
  hex (`17195010` is `0x1066002`). It contains the zone (`0x066`, zone 102)
  and the monster's slot in that zone (`0x002`). It names a **spawn point,
  not a single life of the monster**: a monster that respawns comes back with
  the same id, so two deaths with the same `targetId` are the same spawn
  dying twice. Use it to tell apart monsters that share a name, or to
  track one spawn. A few monsters created on the fly during events reuse ids
  from a pool, so their ids don't carry across sessions. Players have no
  `targetId`: their name already identifies them.

## Job lines

| Field | Type | Meaning |
|---|---|---|
| `t` | integer | When it was seen, in Unix time. |
| `actor` | string | The party member. |
| `main` | string | Main job abbreviation, for example `WAR`. `NON` if unknown. |
| `mainId` | integer | Main job id, matching `main` (`1` is WAR). |
| `mainLvl` | integer | Main job level. |
| `sub`, `subId`, `subLvl` | | The same for the support job. Left out when there isn't one. |

## Example

```jsonl
{"kind":"meta","t":1760570990,"version":"1.3.0"}
{"kind":"job","t":1760571002,"actor":"Hasaya","main":"SAM","mainId":12,"mainLvl":75,"sub":"WAR","subId":1,"subLvl":37}
{"t":1760571004,"seq":1,"use":1,"kind":"melee","actor":"Hasaya","actorKind":"player","action":"Attack","actionId":0,"target":"Tiger","targetKind":"mob","targetId":17195010,"dmg":112,"hit":true,"crit":false,"burst":false,"msg":1}
{"t":1760571004,"seq":2,"use":2,"kind":"melee","actor":"Hasaya","actorKind":"player","action":"Attack","actionId":0,"target":"Tiger","targetKind":"mob","targetId":17195010,"dmg":0,"hit":false,"crit":false,"burst":false,"msg":15}
{"t":1760571009,"seq":1,"use":3,"kind":"ws","actor":"Hasaya","actorKind":"player","action":"Tachi: Gekko","actionId":151,"target":"Tiger","targetKind":"mob","targetId":17195010,"dmg":1480,"hit":true,"crit":false,"burst":false,"msg":185}
{"t":1760571009,"seq":2,"use":4,"kind":"skillchain","actor":"Hasaya","actorKind":"player","action":"Skillchain: Fragmentation","actionId":291,"target":"Tiger","targetKind":"mob","targetId":17195010,"dmg":620,"hit":true,"crit":false,"burst":false,"msg":291}
{"kind":"enfeeble","t":1760571011,"seq":1,"use":5,"via":"magic","actor":"Olivia","actorKind":"player","action":"Sleepga","actionId":273,"target":"Tiger","targetKind":"mob","targetId":17195010,"hit":true,"msg":236}
{"kind":"death","t":1760571015,"seq":1,"actor":"Hasaya","actorKind":"player","target":"Tiger","targetKind":"mob","targetId":17195010,"msg":6}
```
