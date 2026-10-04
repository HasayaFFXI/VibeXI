# Zerg — the event contract and the counting rules

What Zerg reads, what it counts, and why each rule is the way it is. The code
is in `src/Zerg.Core`; each rule names the function that holds it, and that
function's own comment has the detail. Read this before changing any number
Zerg prints.

Many rules exist so that a figure here agrees with **the reference parser**:
the in-game parser most players run beside Zerg. The addon copies that
parser's id tables (`../../addons/VibeXI/vx_enums.lua` names it and says which
table came from where). Agreeing with it is how a figure in Zerg gets checked,
so a rule marked "as the reference parser counts it" is copied, not designed,
and is changed only by measuring against the game.

## One source: the addon's event file

`addons/VibeXI` reads the game's own action packets and appends one JSON object
per line to `%LOCALAPPDATA%\VibeXI\events\<Character>_<date>.jsonl`. That file
is Zerg's only input. There is no chat-log reader and there must not be one:
everything a chat log makes a reader guess (who is a player, which lines are
one area attack, crits, misses against parries, a pet's owner) the packet
states outright, and a second source that disagrees with the first is worse
than one that is right.

Reading the file (`EventFiles`, `EventTail`):

- **The newest `*.jsonl` is followed.** A newer file appearing is a switch:
  state resets and the session goes idle, since a clock still running from
  another character or another day measures a session that is not this one.
- **Byte offsets, not line counts.** A read stops at the last newline and
  reports the next offset, so a half-written line waits for the next poll
  instead of being parsed as a broken one. The addon flushes per event; half a
  JSON object is not parseable.
- **The file is opened sharing read, write and delete.** The addon writes it
  from inside the game, on the game's thread, and must never be blocked.
- **The file is ASCII.** The addon escapes every byte from 0x7F up, so there
  is no encoding to negotiate. A mangled name is fixed in the addon.
- **A file that got shorter is read again from the top.**

## The event contract

One line is one `(action, target, result)` row:

```json
{"t":1785000000,"seq":3,"use":41207,"kind":"ws","actor":"Hasaya","actorKind":"player",
 "action":"Tachi: Jinpu","actionId":32,"target":"Goblin Pathfinder","targetKind":"mob",
 "dmg":723,"hit":true,"crit":false,"burst":false,"msg":185}
```

- `kind` is one of `melee ranged ws magic ability mobtp pet skillchain addl
  reaction`. `msg` is the game's raw message id, on every row, so new outcomes
  can be read later without changing the format. `owner` and `pet` appear only
  on a pet's own rows.
- **`t` is whole seconds**, with `seq` ordering rows inside one second (the
  addon has no finer clock). `EventReader` scales it to milliseconds exactly
  once; everything after it is in milliseconds.
- The generator `tools/gen-test-events.py` must stay field for field what
  `addons/VibeXI/vx_emit.lua` writes. Diff against `vx_emit.encode` when
  either changes.

Three kinds of line are not rows, and `EventReader` files each elsewhere:

- **`kind:"meta"`**: the addon's startup probe, and one notice per game message
  id it did not recognise. Skipped. An unrecognised id is still damage nobody
  is credited with, so when a total seems low, search the event file for
  `"kind":"meta"`; the fix is a new entry in `vx_enums.lua`.
- **`kind:"job"`**: one party member's jobs. It goes on the roster, never in
  the rows: a zero-damage row would land in every count.

  ```json
  {"kind":"job","t":1785000000,"actor":"Hasaya","main":"SAM","mainId":12,"mainLvl":75,
   "sub":"WAR","subId":1,"subLvl":37}
  ```

  - Written on change, not on a timer, so `EventReader.Reset` keeps the
    roster. A cleared job map would stay empty until somebody changed job.
  - Last write wins (a second job line is a real change). Spawn kinds are the
    opposite, first answer wins, because a spawn kind cannot change.
  - The `sub` trio is left out when there is no sub job.
  - It also marks the name as one of ours. For a member who never acts it is
    the only evidence there is.
- **`kind:"heal"`**: one per target of a heal, with `hp` and never `dmg` or
  `hit`, so a heal mistaken for a row would still add nothing.

  ```json
  {"kind":"heal","t":1785000000,"seq":2,"use":7,"via":"magic","actor":"Catpirate",
   "actorKind":"player","action":"Curaga II","actionId":8,"target":"Hasaya",
   "targetKind":"player","hp":190,"msg":367}
  ```

  Which actions are heals is the addon's decision, by action id, as the
  reference parser decides it. `via` is `magic`, `ability` or `pet`.

## `use` is per swing, not per action

The packet has two dimensions, and they mean different things:

- **Several targets, one result each**: an area attack. One use.
- **One target, several results**: a multi-attack round. Two or three real
  swings, each with its own outcome, each its own use.

The addon mints `use` from the result's position, so result 1 across every
target shares an id and result 2 takes the next. `Counting.Collapse` folds the
rows of one use together: damage sums, hit, crit and burst are "any", and a
use on several targets reports `"3 targets"`.

Folding the second case into the first makes a round that landed once and
missed once read as one hit and no miss. The swing count is what accuracy
divides by, so the symptom is a party that never misses.

**The invariant: collapsing moves counts, never damage.**

## Skillchains and additional effects

Both ride on the row that caused them, and each is written as a row of its own
with its own `use`. Sharing the weaponskill's id would fold the chain's damage
into the weaponskill and lose it as a row. A chain's action is
`Skillchain: <name>`. **Include Skillchains** off drops every `skillchain` row
in `Counting.Counted`; the SC columns then print a dash, not 0.

## Party damage only

`Counting.Counted` is the single place a row is dropped for a reason other
than time, and both `Counting.Filter` and `Counting.FirstCounted` go through
it. Two copies would let the clock start on a row that is then not drawn.

- **Monsters' rows are read and kept, and dropped on the actor side.** Anything
  not positively ours (`player` or `pet`) is treated as a monster, so an entity
  the addon could not resolve is left out rather than added. Under-counting a
  stranger beats crediting one. Monsters still appear as targets.
- **Pets are ours, credited to the owner** (`Counting.Credit`). A pet has no
  row, chip or colour of its own. The action keeps the pet's name as a prefix
  (`Fluffikins: Big Scissors`), since pet and master both swing an "Attack"
  and one average over both would describe neither. It copies, so the list
  stays true to the file.
- **Reaction damage counts, and arrives with its ends swapped.** A counter, a
  spikes proc and a Retaliation ride on a packet the monster is the actor of.
  The addon writes them with the reacting character as the actor, as
  `kind:"reaction"`.
- The addon no longer records a monster's own swings. Older files that have
  them still read correctly, because the drop is here.

## The per-character table

Every column but Job and DPS is one of the reference parser's columns, counted
its way, with percentages to one decimal.

| Column | What it is |
|---|---|
| Damage | everything counted: pet, and skillchains when the switch is on |
| Damage % | share of the party total |
| Accuracy | own melee and ranged swings that connected / attempted |
| WS Damage, WS Avg, WS %, WS Acc | own `ws` rows; Avg over weaponskills that dealt damage; Acc is dealt damage / used |
| SC Damage, SC % | the chains this character closed; a dash with the switch off |
| Pet Damage, Pet Acc | rows with an `owner`; the pet's melee swings |

- **Accuracy is `Counting.Connects`, not `Hit`.** `Hit` asks whether damage
  landed, and feeds totals, averages and histograms. `Connects` asks whether
  the swing got through: a swing into shadows (31) is a hit, a melee swing that
  healed the target (373) is a hit, a melee Perfect Dodge (32) is not an
  attempt, and a weaponskill connects only if it dealt damage. The Actions
  table's Hits and Miss columns are damage counts, so a shadowed swing is a
  Miss there and a hit in Accuracy. Two questions, two answers.
- **A pet's row is the owner's damage, never the owner's swing.** A pet's
  melee must not reach the owner's Accuracy, nor a pet's move WS.
- **Null means "nothing to measure" and prints as a dash.** A mage with no
  weaponskills has no WS Acc; 0% would say they missed every one.
- **Nobody at zero** in the bars, the table, the strip or the cumulative
  lines. Reaching the totals takes an action, not damage. The chips still list
  them, so they can be excluded, and the Actions table shows what they did.
- **One known difference from the reference parser:** it leaves melee
  additional effects out of its total and Zerg counts every `addl` row, so
  with enspells up Damage reads a little above it.

## The session clock

Every number is measured from the session's zero, and there is no other clock
(`Session`).

- **Start arms; the first counted row latches the zero**, at that row's own
  time. Pressing early costs nothing. A stopwatch that must be pressed on the
  frame of the first swing is always a second or two wrong, and every DPS is
  divided by that error.
- **Idle and armed both count nothing.** There is no zero to measure from.
- **What starts the clock is exactly what would be counted**: not a monster's
  swing, not a skillchain with the switch off, not an excluded character's
  attack. With everyone excluded the clock never starts, which is why the
  armed state is shown loudly.
- **A miss starts the clock.** Waiting for damage would put the misses before
  it outside the session and out of accuracy.
- **A heal does not.** Only a counted damage row latches; a second definition
  of "counted" would be needed otherwise.
- **The arming time is floored to the second**, since the wire clock is whole
  seconds: a swing in the same second as the press is counted, not dropped.
- **The latch is sticky.** A later filter change cannot re-date a running
  session. A filter change can latch an armed one, and that row is then the
  first thing counted, so it is the right zero.
- **Pause stops damage and clock together**, and a pause is subtracted, not
  skipped: two swings either side of a three-minute pause come out three
  minutes closer. Reading goes on during a pause; rows are dropped by their
  own timestamps, so poll latency cannot move one across the boundary.
- **The second button is Pause or Cancel, never both.** Cancel exists only
  while armed: it calls off a measurement that has not begun. A session with
  damage in it is ended by Start.
- **Start during a session re-arms**, and drops the rows read so far while
  keeping the read offset, the roster, the colour slots and the filters.
- **One denominator.** `Counting.Aggregate` divides the party's DPS and each
  character's by the session clock, so the column adds up to the party figure.
- **DPS decays between polls**: the total holds and the clock grows, so the
  Elapsed tile, the party DPS and each character's DPS are rewritten 4 times a
  second, together. A tile decaying past a frozen column reads as a bug.
- **The chart's left edge is the zero and its right edge is the clock**, paused
  included. A flat line out to the present is a falling DPS, drawn. The axis
  and the DPS beside it must always describe the same span.
- **The session is not saved.** A restart returns to idle; a clock restored
  from an earlier run would be measuring time nobody was fighting.

## Healing

`Healing.Filter` applies the session window and the exclusions as the damage
filter does, and credits a pet's heal to its owner.

- **Healing leaves out pet heals**; Pet Healing is a column of its own, as the
  reference parser has it.
- **Casts are one per `use`**, however many targets; Avg is total / casts.
- **There is no overcure, on purpose.** The packet does not carry the target's
  missing HP, and an estimate against the spell's best cast so far was tried
  and removed as not accurate enough. Do not bring it back without a real
  source for missing HP.
- **A cure on a full target healed 0; it did not miss.** The cumulative line
  and the histogram reuse the damage code on a row-shaped copy
  (`Healing.AsEvents`) where every heal is a hit.
- HPS ticks with DPS, over the same clock.

## Export, import and Compare

- **Export only while paused** (`ParseFile.Export`): a running clock would be
  out of date before the file was written.
- **The file is the addon's own records, replayed.** Rows and job lines are
  stored in the wire format and read back through `EventReader.FeedRecord`,
  the same door a live line uses. With them go what the records cannot state:
  the session, the spawn kinds and any manual override.
- **Only rows the session covers are written.** Filters are not: exclusions,
  the skillchain switch and Hide names are the viewer's.
- **An import is locked** (no Start, no Pause) and takes nothing from the live
  session, which goes on being read and measured underneath.
- **An import lives in the View section** and is counted only while that
  section is on screen. Damage and Healing are always the session; leaving
  View keeps the parse open for the way back.
- **Exports are `.zerg`, never `.jsonl`** (`ParseFile.CanSaveAs`): the newest
  `*.jsonl` in the events folder is what Zerg follows. Older `.json` exports
  still import; detection is by content.
- **The owner travels in the file**, so colour slot 0 and the one name Hide
  names keeps are the exporter's.
- **A compared run is an import measured by the same pipeline**
  (`Compare.Measure`), so a compared figure is the figure Zerg prints when
  that file is imported. Every change is B − A, every percentage that over A.
- **By Job re-actors onto the main job before the totals**, so a job's accuracy
  is over its combined swings. An unreported job is "Unknown job" and is never
  borrowed from the other run.
- **The shorter run's line ends where the run ends.** Carried flat it would
  claim it was still being measured.
- A parse with no heal lines at all prints dashes in Healing mode, not zeroes.

## Colours and names

- **A character is drawn in their job's colour.** The palette is the one
  players already read in game. It is not colourblind-separable (four jobs are
  reds), which is acceptable only because a colour never appears without the
  name and job beside it as text.
- **Two characters on one job are shaded** (`Shades.Step`), ordered by colour
  slot so a shade is stable all session.
- **No job on record, or a job with no colour, falls to the 18 slots**
  (`Cast`). The file's owner is pinned to slot 0 before anyone else is
  slotted; otherwise their hue depends on who swung first. Monsters get no
  slot.
- **Hide names is display only.** Every key stays the real name. If a total
  moves when it is switched, something has started keying on a drawn name.
  Aliases are ordered by slot (`SAM/WAR`, `SAM/WAR 2`), the owner keeps their
  name, a pet keeps its own, and the Job column is dropped, not blanked.

## Known gaps

- Reaction attempts that dealt nothing are not recorded, so a Counter or
  Retaliation row always reads 100% accuracy.
- Monster TP moves and pet abilities are named `#<id>`.
- A few job abilities share an id with a weaponskill (Jump among them) and are
  written by the addon as `kind:"ws"`, so they land in the WS columns. The fix
  belongs in the addon.
- Absorbed and "no effect" outcomes are misses, except in Accuracy, where a
  swing into shadows is a hit.
- Nothing reports double, triple or quad attack rates.
- MP drain, enfeebles and TP are not read. Every row carries its `msg`, so
  adding them is a change to the addon's tables, not to the format.
