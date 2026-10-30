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
| `job` | A party member's job, written when it's first seen or when it changes. |
| `meta` | Information about the session, not an event. Most readers skip these. |

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
- Melee names are `Attack`, `Offhand Attack`, `Kick Attack` and `Throwing`.
  Ranged is `Ranged Attack`. Skillchains are named `Skillchain: <name>`, and
  additional effects `Additional Effect`. Reactions are `Counter`,
  `Retaliation` or `Spikes`.
- When the addon has no name for an action, `action` is `#` and the id, for
  example `#1234`. This happens mostly with monster TP moves.
- On HorizonXI with `Horizon=1`, abilities such as the Jumps arrive as
  `ability`, not `ws`. The session's `meta` line says which way it was read.

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

## Job lines

| Field | Type | Meaning |
|---|---|---|
| `t` | integer | When it was seen, in Unix time. |
| `actor` | string | The party member. |
| `main` | string | Main job abbreviation, for example `WAR`. `NON` if unknown. |
| `mainId` | integer | Main job id, matching `main` (`1` is WAR). |
| `mainLvl` | integer | Main job level. |
| `sub`, `subId`, `subLvl` | | The same for the support job. Left out when there isn't one. |

## Meta lines

The first line of every file is a `meta` line describing the session. The
fields you're most likely to want:

| Field | Meaning |
|---|---|
| `v` | The addon version that wrote the file. |
| `t` | When the line was written, in Unix time: the start of recording. |
| `selfName` | The character the file belongs to. |
| `horizon` | `1` or `0`: the `Horizon` setting the file was written under. |
| `path` | The file's own full path. |

It also carries diagnostic fields you can ignore. Later `meta` lines with an
`unknownMsg` field note a game message the addon saw but doesn't record; they
have no `t` and can be skipped.

## Example

```jsonl
{"kind":"meta","v":"1.0.0","t":1760571000,"selfName":"Hasaya","horizon":1,"path":"C:\\Users\\you\\AppData\\Local\\VibeXI\\events\\Hasaya-20261015193000.jsonl"}
{"kind":"job","t":1760571002,"actor":"Hasaya","main":"SAM","mainId":12,"mainLvl":75,"sub":"WAR","subId":1,"subLvl":37}
{"t":1760571004,"seq":1,"use":1,"kind":"melee","actor":"Hasaya","actorKind":"player","action":"Attack","actionId":0,"target":"Tiger","targetKind":"mob","dmg":112,"hit":true,"crit":false,"burst":false,"msg":1}
{"t":1760571004,"seq":2,"use":2,"kind":"melee","actor":"Hasaya","actorKind":"player","action":"Attack","actionId":0,"target":"Tiger","targetKind":"mob","dmg":0,"hit":false,"crit":false,"burst":false,"msg":15}
{"t":1760571009,"seq":1,"use":3,"kind":"ws","actor":"Hasaya","actorKind":"player","action":"Tachi: Gekko","actionId":151,"target":"Tiger","targetKind":"mob","dmg":1480,"hit":true,"crit":false,"burst":false,"msg":185}
{"t":1760571009,"seq":2,"use":4,"kind":"skillchain","actor":"Hasaya","actorKind":"player","action":"Skillchain: Fragmentation","actionId":291,"target":"Tiger","targetKind":"mob","dmg":620,"hit":true,"crit":false,"burst":false,"msg":291}
```

The `meta` line is shortened here; real ones carry more fields.
