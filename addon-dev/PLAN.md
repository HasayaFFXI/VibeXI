# Damage Meter — packet/memory addon

Branch: `DamageMeter-Addon`. Nothing is built yet; this file is the whole plan.

## Why

`damage_meter/` used to read the FFXI chat log as text. That worked, but the
ceiling was a hard one: the log did not say who was a player, did not say who
owned a pet, did not flag crits reliably, could not distinguish a weaponskill
from a job ability at the announcement, and wrote one damage line per AoE victim
with no grouping.

An Ashita addon reading incoming packets and the client's own memory has all of
that as ground truth. This addon is a **thin event emitter** feeding the existing
browser UI, which already knew how to aggregate.

**The chat-log reader is gone.** It was removed on 2026-09-04 once the addon was
approved (see Resolved, below) rather than being kept as a fallback: two sources
that disagree is worse than one that is right, and every "known gap" in the old
`damage_meter/CLAUDE.md` was a property of the log rather than of the meter.
`web/lib/parser.js` and the CP932 tailer went with it.

---

## THE CONSTRAINT — no outgoing anything

**The addon must never send a command, a packet, or any data to the game
server.** This outranks every other goal in this document. If a feature and this
rule conflict, the feature loses.

It is not enforced by discipline. It is enforced by the addon not containing the
capability. An Ashita addon can only reach the server through a small set of
named APIs, and all of them are closed by omission:

| Route | Closed by |
|---|---|
| `AshitaCore:GetChatManager():QueueCommand` | never copy `ashita/chat.lua` or `modules/report/` from Metrics |
| `packet_out` / outgoing packet injection | never register the event; there is no other route |
| `require('socket')` | not required at all — see Decision 1 |
| `require('ffi')` → `ffi.load('ws2_32')` | not required at all — see Decision 2 |

What remains is: read-only memory getters, an incoming-packet callback, and an
append to a local file. **No API capable of reaching the network is present in
the source.**

### Enforcement: allowlist, not denylist

A denylist only catches routes we thought of. Instead, assert the *complete* set
of external calls against a checked-in manifest:

```bash
grep -rhoE "AshitaCore:[A-Za-z]+|ashita\.[a-z_]+\.[a-z_]+|require\(['\"][a-z]+" addons/VibeXI/ | sort -u
```

Diff against `addon-dev/ALLOWED_APIS.txt`; fail on anything not listed. Adding an
API becomes a deliberate edit to that file, never an accident. Seed list is in
the appendix.

### The bridge is one-way by construction

The addon **appends to a local file**. The Python server tails it, exactly
as it already tails chat logs. Never open a socket from the addon, not even to
localhost — that would put network capability back in the process for a
convenience we do not need.

```
FFXI process             disk                     existing stack
[addon] ──write──▶ events.jsonl ──read──▶ [damage-meter.py] ──▶ [browser]
```

Nothing downstream can send anything back into the game regardless of what
happens to it. That is a property of the shape, not a promise in a comment.

---

## Reference material

**Metrics** by Metra — the gold-standard Ashita parser this work borrows from.

- Source (outside the repo, not in git):
  `C:\Users\thadl\OneDrive\Documents\Claude\resources\metrics\`
- Installed copy on this machine:
  `C:\Users\thadl\AppData\Roaming\HorizonXI-Launcher\HorizonXI\Game\addons\metrics`
- Its config/output dir:
  `...\HorizonXI\Game\config\addons\Metrics`

Audited: it is read-only against game state — no packet injection, no memory
writes, no network (LuaSocket present but only `gettime`). Its one outbound call
is `QueueCommand`, used solely by the Report tab to post parses to chat. We are
not taking that file.

**Upstream packet documentation** — `atom0s/XiPackets`, `world/server/0x0028`.
Describes the action packet in full; this is what to build the reader from.

---

## Decisions already made

Recorded so they don't get relitigated. Each has a reason.

**1. No LuaSocket.** Metrics uses it only for `Socket.gettime()` millisecond
timestamps, for attack-speed. Requiring it links a library whose purpose is
opening sockets. We lose nothing: `cumulative()` clamps its grid to
`Math.max(1000, …)`, `fmtClock` is HH:MM:SS, and DPS divides by whole seconds —
no surface in the UI resolves finer than 1s. Use `os.time()` plus a per-second
`seq` counter for ordering. Revisit only if attack-delay tracking is wanted, and
treat it as a constraint change, not a detail.

**2. No FFI.** Metrics uses it for `memcmp` in the duplicate-packet check, but
`ffi.cdef` can declare `connect()`/`send()` and `ffi.load` can pull in `ws2_32`.
Replace the dedup with a plain Lua string comparison of packet buffers — at FFXI
packet rates this is free. The addon then has zero foreign-function capability.

**3. File bridge, not a socket.** See above.

**4. Emit events, do not port the handlers.** `handlers/melee.lua` is 34KB
because it aggregates into Metrics' own `DB` tree — Metrics does that because
its UI is ImGui. Ours is a browser and `stats.js` already aggregates. Take the
message-ID *logic* from the handlers, not the code.

**5. One JSON line per (action, target, result), with the `use` id minted in Lua
per RESULT SLOT.** The packet hands over the target list directly, so `use` stops
being the 5-second `AOE_MS` heuristic and becomes ground truth — and
`stats.collapse()`, already written and tested, works unchanged.

The slot part is load-bearing and was got wrong first time round. A packet has
two dimensions and they mean different things: several *targets* with one result
each is an AoE (one use), while one target with several *results* is a
multi-attack round (two or three genuine swings). Minting one id for the whole
action folds the second case into the first, and because `collapse()` treats
`hit` as "any", a round that landed once and whiffed once then reports one hit
and no miss. On the test fixture that read as 93.7% accuracy against a true
89.7%. `use_for(slot)` in `record()` keys the id on the result's position, so
result 1 across every target is one use and result 2 is the next.

**6. ASCII-only JSON** (escape non-ASCII as `\uXXXX`). FFXI names are ASCII and
ASCII bytes survive the server's CP932 decode untouched, so the encoding path
needs no changes.

**7. Reimplement the bit reader from the XiPackets spec.** Not a style
preference — see Licensing below.

**8. Our own Lua uses dot-calls only** — `emit.write(x)`, never `emit:write(x)`.
This is what makes the allowlist sharp: the checker treats *every* `:method(`
call as an SDK call, so if our own code used colon-methods we would have to
allowlist our own names and the SDK surface would stop being legible at a
glance. Defining a colon-method on one of our tables blunts the check. Added in
Phase 0 as a consequence of how `check-apis.py` extracts tokens.

**9. Three events, not one.** The plan said "register `packet_in` only". The
allowlist permits `packet_in`, `load` and `unload` — the latter two are
lifecycle hooks for opening and flushing/closing the output file, and neither
carries any capability. `d3d_present`, `command` and `packet_out` remain
excluded; `packet_out` is denylisted outright.

---

**10. Skillchains come off the PROC trailer, and Metrics owns the id table.** A
skillchain is not a packet or a message of its own: it rides on the closing
weaponskill's result as `proc_message` / `proc_value`. `E.Skillchains` is
Metrics' `Res.WS.Skillchains` verbatim and `E.skillchain()` is its
`Res.WS.Get_Skillchain`.

The first cut of this derived the ids arithmetically instead, from the
LandSandBoat server's `action_result_t::recordSkillchain` (`287 + effect`
landed, `384 + effect` absorbed). That was the wrong call. It disagrees with
Metrics on Radiance and Umbra (302/303 vs 767/768) and invents an "absorbed"
outcome where Metrics simply maps 385/386 to Light and Darkness, and Metrics is
the parser with a track record against this server. Reading the server source is
not the same as knowing what this server emits; if the two ever have to be
reconciled that is a measurement against a live client.

**The table is gated on category 3.** Metrics calls `H.TP.Skillchain_Parse` from
`H.TP.Action` and nowhere else (`handlers/tp_action.lua:41`), and that gate is
what resolves 229 — 'DRG Jump Effect' on a weaponskill, `Message.ENSPELL` and
therefore an additional effect on anything else. A global lookup would file every
enspell proc as a skillchain. Note also that `proc_kind` cannot identify a chain
on its own: it is a variant (add-effect OR skillchain), so the message decides.

The trailer is read *outside* the branch that handles the main message, so a
chain is never lost because its weaponskill's own message was unrecognised. Both
a skillchain and an additional effect become their own event with their own
`use`, minted once per action — one weaponskill closes one chain however many
targets or swings it involved.

## Architecture

### Event contract

```json
{"t":1785000000,"seq":3,"use":41207,"kind":"ws","actor":"Hasaya","actorKind":"player",
 "action":"Tachi: Jinpu","actionId":32,"target":"Goblin Pathfinder","targetKind":"mob",
 "dmg":723,"hit":true,"crit":false,"burst":false,"msg":185}
```

`web/lib/source.js` reads it, and `stats.js` is unchanged from the chat-log era
apart from the fields it carries through `collapse()`.

Three things got strictly better:

- **The roster stopped guessing.** `spawn_flags` gives player/mob/pet outright.
  The article heuristic, the fixed-point propagation in `roster.rebuild` and
  every `guess: true` event are deleted. The manual override UI is kept.
- **Pets carry their owner** via `pet_index`, closing a documented gap.
- **Crits, multi-attack and shadows are real flags** rather than phrasings.

One thing to know about the clock: `t` is `os.time()`, whole seconds, with `seq`
ordering events inside one second (Decision 1). `source.js` scales it to
milliseconds once on the way in, because everything downstream is in ms.

### What to take from Metrics

| Take | Notes |
|---|---|
| `packets/_parser.lua` | the 0x028 walk — adapt, drop its O(2303) `get_actor_name` scan |
| `ashita/packets.lua` | `Build_Action`, `Build_Message`, dedup (reimplement without FFI) |
| `ashita/mob.lua` / `party.lua` / `player.lua` | memory getters, trimmed hard |
| `ashita/_enums.lua` | message IDs, spawn flags, animation IDs |
| `resources/*` | ID→name tables (spells, WS, abilities, avatars, pets) |
| `metrics.lua:174-207` | offense/defense/pet attribution — the valuable part |
| `handlers/*` | read for message-ID semantics only; do not port |

| Leave | Why |
|---|---|
| `ashita/chat.lua` | the only `QueueCommand` — **never copy this file** |
| `modules/report/` | its only consumer |
| `modules/`, `windows/`, `columns/` | ImGui UI we don't want |
| `database/` | `stats.js` already does this |
| `packets/_bitreader.lua` | rewrite from spec — licensing |

Estimated addon size: ~600 lines.

### Layout

`addons/VibeXI/` is the addon and nothing else — it is what gets copied into
Ashita, so only Lua the addon loads at runtime may live there. Everything that
builds, checks or documents the addon sits outside it and never ships.

```
addons/VibeXI/         copy this folder to …\HorizonXI\Game\addons\
  vibexi.lua           entry: addon meta, packet_in dispatcher
  vx_bitreader.lua     bit unpacking (written from XiPackets spec)
  vx_action.lua        0x028 / 0x029 → action tables
  vx_entity.lua        mob / party / player memory reads
  vx_enums.lua         packet ids, categories, spawn flags, message ids
  vx_emit.lua          event → ASCII JSON → append to file
  vx_ws_names.lua      generated weaponskill id → name table

addon-dev/             the addon's tooling and docs; never shipped
  PLAN.md              this file
  ALLOWED_APIS.txt     the enforcement manifest
  check-apis.py        the allowlist diff, wired to pre-commit
  check-lua.py         structural Lua check for a box with no interpreter
  gen-ws-names.py      regenerates addons/VibeXI/vx_ws_names.lua

.githooks/
  pre-commit           runs check-apis.py on every commit
```

The folder is `VibeXI` because that is the name the addon was approved under
(ticket addon-0032), and Ashita takes the folder name as the addon name.

---

## Phases

### Phase 0 — safety rails — **DONE**

Built:

- `addon-dev/ALLOWED_APIS.txt` — 51 tokens, 3 events, each grouped with why it is safe
- `addon-dev/check-apis.py` — three independent checks (allowlist / denylist / event names)
- `.githooks/pre-commit` — runs it on every commit

Install the hook once, from the repo root:

```bash
git config core.hooksPath .githooks
```

Run it by hand any time:

```bash
python addon-dev/check-apis.py
```

**Python, not sh** (this plan originally said `check-apis.sh`, and it was
PowerShell until 2026-09-04): one language across the repo's tooling, and it
runs without Git Bash. The hook itself is a two-line `sh` wrapper, because that
is what git invokes.

**Verified:**

| Case | Result |
|---|---|
| empty `src/` | PASS, exit 0 |
| `GetChatManager` + `QueueCommand` | FAIL — caught by denylist *and* allowlist |
| `require('socket')`, `require('ffi')`, `ffi.cdef` | FAIL |
| `ashita.events.register('packet_out', …)` | FAIL — denylist *and* event gate |
| unapproved SDK call (`:GetInventory`) | FAIL — allowlist only, which is the point |
| denylisted name inside a `--` comment | WARN, exit 0 |
| legitimate calls in the same file | no violations |
| real `git commit` of a bad file | **aborted, no commit created** |

The `:GetInventory` row is the one that matters. Nothing named it in advance and
it was still caught — that is the allowlist earning its keep over a denylist.

### Phase 1 — addon emits JSONL — **WRITTEN, NOT YET RUN**

Source is in `addons/VibeXI/`:

| File | Role |
|---|---|
| `vibexi.lua` | entry, dedup, dispatch, naming, `use` minting |
| `vx_bitreader.lua` | LSB-first bit reader, written from the XiPackets spec |
| `vx_action.lua` | 0x028 / 0x029 → Lua tables |
| `vx_entity.lua` | entity + party memory reads, kind/owner classification |
| `vx_emit.lua` | event → ASCII JSON → appended file |
| `vx_enums.lua` | packet ids, categories, spawn flags, message ids |
| `vx_ws_names.lua` | generated; `addon-dev/gen-ws-names.py` rebuilds it |

**Output path — `%LOCALAPPDATA%\VibeXI\events\<Character>_<YYYY.MM.DD>.jsonl`.**
Deliberately not `%TEMP%` (Storage Sense deletes it, and this file *is* the
persistence layer), not `%APPDATA%`/Roaming (profile sync on a hot file), not
anywhere under OneDrive (continuous cloud sync on a file appended several times
a second), and not the Ashita tree (which on this machine is itself under
Roaming). Rationale is repeated in `vx_emit.lua`'s header where someone editing
the path will actually see it.

**Verified without a game:**

- `check-apis.py` green — 7 files, 43 distinct external calls, all allowlisted
- the bit-reader **algorithm** round-trips a synthetic 0x028 built to the
  documented layout: 32-bit ids without sign corruption, nested
  multi-target/multi-result in order, correct field alignment across byte
  boundaries

**Checked against the Ashita v4 tree** at
`Claude\resources\Ashita-v4beta` (SDK headers, the LuaLS annotations under
`addons/libs/annotations/`, and ~110 shipped addons). What that settled:

| Was assumed | Verdict | Evidence |
|---|---|---|
| `p.chunk_data` is a Lua string | **correct** | `plugins/addons.dll` exports `chunk_data`/`chunk_data_raw`/`chunk_size` beside `data`/`data_raw`; shipped addons `struct.unpack` the non-`_raw` member and `ffi.cast` the `_raw` one |
| `GetIsZoning()` returns a number | **correct** | `uint32_t GetIsZoning(void)` in `plugins/sdk/Ashita.h`; `---@return number` in the annotations |
| `ashita.fs.create_dir` is non-recursive | **wrong, was harmless** | it aliases `create_directory`, documented as "Creates all missing folders within the path". Collapsed to one call |
| spawn flags are enum values, compare with `==` | **wrong, was a real bug** | `EntitySpawnFlags` in `plugins/sdk/ffxi/enums.h` is a bitfield; `chamcham`/`skeletonkey` both `bit.band` it. Metrics' 525/258/4366 are *composites*, and `==` misclassified anything with a combination nobody wrote down. `vx_entity.kind()` now tests bits |
| 0x028 layout (from XiPackets) | **correct** | matches Ashita's shipped `actionparse/parser.lua` field for field and width for width |
| `.Name[1]` is the English name | **correct** | the idiom across `recast`, `blucheck`, `itemwatch`, `craftmon` |

Still **NOT verified — there is no Lua runtime on this machine** (Ashita embeds
LuaJIT inside `Ashita.dll`; nothing standalone ships, and neither Python nor
Node is installed). So the Lua has still never executed:

1. **Lua syntax/runtime errors.** A structural checker (tokenizes out
   strings/comments, then balances block keywords and brackets) passes all 7
   files. It was calibrated on 108 shipped Ashita `.lua` files — zero false
   positives — and does catch a dropped `end` and a dropped paren. That rules
   out the common typo class, **not** a misspelled identifier or a bad
   expression, which will still surface on `/addon load VibeXI`.
2. **`GetAbilityById(id + 512)`** — the one number still inferred. `abils.dat`
   lives in the FFXI install, not the Ashita tree, so the table's segmentation
   cannot be read from source. Supporting: `recast` scans `GetAbilityById(0..2048)`
   for ~300 job abilities, so the table *is* segmented; `blusets` does the exact
   analogous `GetSpellById(id + 512)`. The probe line now looks the same id up
   **both** ways (`abilRaw` / `abilOffset`), so one session settles it.
3. **Spawn-flag composites on HorizonXI.** The bit meanings are now from
   Ashita's own header, so classification is robust to unexpected combinations;
   only the exact value of `selfSpawnFlags` (expected 525) is still a live check.
4. **Monster TP moves and pet abilities emit `#<id>`**, not names. Deliberate
   for Phase 1: the name tables are ~300KB and every event carries `actionId`,
   so naming can be added later without touching the event contract.

**Done when:** a fight produces a JSONL file whose events match what the chat
log says for the same fight — same actors, same damage totals — and
`check-apis.py` is still green.

**First-run checklist:**

Nothing gates this any more — HorizonXI approved the addon on 2026-09-04 (see
Resolved, below), so it can load on a real account rather than needing a test
one.

1. Copy the `addons/VibeXI/` folder into `…\HorizonXI\Game\addons\`, so the
   addon lands at `…\Game\addons\VibeXI\` with `vibexi.lua` inside it.
2. `/addon load VibeXI`
3. Check `%LOCALAPPDATA%\VibeXI\events\` for a file appearing.
4. Read line 1 of that file — the `"kind":"meta"` probe. Expect
   `"chunkData":"string"`, `"isZoningType":"number"`, `"selfSpawnFlags":525`,
   and `abilOffset` naming a job ability while `abilRaw` names a weaponskill.
   Anything else points straight at the assumption it belongs to.
5. Kill one mob. Compare the JSONL totals against the chat log for the same
   fight — **if every number is exactly double, it is the `chunk_data` dedup**.
6. Point the server at it — or just run it, since that path is the default:
   `python damage_meter/damage-meter.py --events-dir %LOCALAPPDATA%\VibeXI\events`

### Phase 2 — server serves it — **DONE**

- `--events-dir`, defaulting to `%LOCALAPPDATA%\VibeXI\events`
- the newest-file scan takes `*.jsonl`
- `/api/log` became `/api/events`; decoding is UTF-8, not CP932, because the
  addon escapes every non-ASCII byte

The tailer needed no structural change: it already returned raw lines and already
held back a partial trailing one, which is exactly what makes the addon's
flush-per-event safe — half a JSON object is not parseable.

### Phase 3 — client reads it — **DONE**

- `damage_meter/web/lib/source.js` replaces `parser.js`, which is deleted along
  with `tools/gen-test-log.py` and the CP932 fixture
- `roster` is a lookup over `actorKind`/`targetKind`; the article heuristic and
  the fixed point are gone, the manual override is kept
- `stats.js` is unchanged apart from the fields `collapse()` carries through
- Diagnostics swapped "unrecognised damage lines" for the addon's own meta lines
  (the startup probe, and one notice per unrecognised message id)
- `tools/gen-test-events.py` writes a synthetic event file, so the whole UI is
  still exercisable with no game running

**Verified against that fixture:** 709 events over 711 lines, no malformed lines;
collapse preserves total damage exactly; an AoE folds 3 rows into 1 use reading
`3 targets`; multi-attack swings do *not* fold; skillchains never share a use
with the weaponskill that closed them; an absorbed chain counts as a miss worth
zero; `Leaping Lizzy` classifies as a mob with no heuristic at all; the pet is
counted, the NPC and the unresolved `Unknown` target are not; the Skillchains
toggle and the manual roster override both work in the page.

### Phase 4 — the metrics that were previously impossible

Defense/mitigation, TP-at-weaponskill, real resist rates. Multi-attack rounds are
already observable — the swings are separate results — but nothing reports the
round shape yet (double/triple/quad rates), only the individual swings.
Also worth revisiting from the earlier Metrics comparison: active-time duration
(`AUTOPAUSE = 5`), rolling DPS (3s × 3 buckets), running accuracy over last N.

---

## Open questions

1. **Where the addon installs.** Presumably
   `...\HorizonXI\Game\addons\VibeXI\`, loaded with `/addon load VibeXI`.
   Confirm against how Metrics is registered.
2. **Output directory.** Ashita's `config/addons/<name>/` via
   `AshitaCore:GetInstallPath()` + `ashita.fs.create_dir`, matching what
   `file.lua` does. Needs to be somewhere `damage-meter.py` can be pointed at.
   Phase 1 as written chose `%LOCALAPPDATA%\VibeXI\events\` instead, for the
   reasons listed there; this question is really "does the server care where an
   approved addon writes", and the answer so far is no.

## Resolved

**HorizonXI addon policy — approved.**

| | |
|---|---|
| Approved by | **Aerec** |
| Date | **2026-09-04** |
| Ticket | **addon-0032** |

The server has signed off on this addon; it can be attached to a real account.

Two things follow, and neither is a licence to loosen anything:

- **The constraint does not move.** Approval is permission to run the addon as
  described — read-only, no outgoing anything, allowlist-enforced. Every rule
  under "THE CONSTRAINT" and the whole of `check-apis.py` stay exactly as they
  are. If a future feature would need an API outside `ALLOWED_APIS.txt`, that is
  a new conversation with the server, not a local edit.
- **The approval is recorded in the entry file.** Metrics carries
  `-- Horizon Approved Addon 0457` at the top of its entry file; the equivalent
  trace for this addon is the approver, date and ticket, and those three lines
  are now in the header of `src/vibexi.lua` so anyone reading the source — or
  any staff member asked about it — can follow it back to addon-0032 without
  reading this plan. If Horizon later issues a numbered marker of the Metrics
  form, add it there alongside them.

## Licensing — read before copying anything

Metrics is BSD-3-Clause, **but** `packets/_bitreader.lua` and
`packets/_parser.lua` carry the **Ashita Development Team's GPL-3.0** header.
Metrics vendors copyleft files into a BSD project. Copying those two makes this
component GPL-3.

For a personal tool that is fine. If VibeXI is ever published it is not. The bit
reader is ~40 lines of trivial shifting and XiPackets documents 0x0028 fully, so
writing it from the spec removes the question — hence Decision 7. Retain the BSD
notice for anything genuinely derived from Metrics' own files.

---

## Appendix A — allowlist seed

From the audit of what the acquisition core actually needs.

```
require('common')

ashita.events.register          -- 'packet_in' ONLY
ashita.fs.exists
ashita.fs.create_dir

AshitaCore:GetInstallPath
AshitaCore:GetMemoryManager
AshitaCore:GetResourceManager

:GetEntity   → GetName GetServerId GetType GetStatus GetSpawnFlags
               GetHPPercent GetClaimStatus GetTargetIndex GetPetTargetIndex
               GetLocalPositionX GetLocalPositionY GetLocalPositionZ
:GetParty    → GetMemberIsActive GetMemberName GetMemberServerId
               GetMemberTargetIndex GetMemberMainJob GetMemberMainJobLevel
               GetMemberSubJob GetMemberSubJobLevel GetMemberZone
:GetPlayer   → GetMainJob GetSubJob GetIsZoning GetPetTP
:GetResourceManager → GetAbilityById GetSpellById

GetEntity()            -- bare global
GetPlayerEntity()      -- bare global

io.open  -- "a" mode only, local path under the Ashita config dir
```

**Explicitly excluded and must never appear:** `GetChatManager`, `QueueCommand`,
`packet_out`, `add_outgoing_packet`, `require('socket')`, `require('ffi')`,
`ffi.load`, `ffi.cdef`, `os.execute`, `io.popen`, `ashita.memory.write*`,
`loadstring`.

## Appendix B — source pointers into Metrics

| What | Where |
|---|---|
| packet_in dispatcher, category routing | `metrics.lua:121-259` |
| offense/defense/pet attribution | `metrics.lua:174-207` |
| 0x028 bit layout | `packets/_parser.lua:51-119` |
| Windower field renaming | `ashita/packets.lua:16-85` |
| duplicate-packet check (FFI version) | `ashita/packets.lua:198-217` |
| entity struct from memory | `ashita/mob.lua:58-94` |
| pet → owner resolution | `ashita/mob.lua:170-185` |
| party table read | `ashita/party.lua:13-73` |
| message ID → outcome | `ashita/_enums.lua:78-125` |
| spawn flags | `ashita/_enums.lua:22-31` |
| no-damage message set | `handlers/melee.lua:170-178` |
| AoE: count attempts once | `handlers/tp_action.lua:28-60` |
| WS-vs-ability curated lists | `resources/weapon_skills_curated.lua` |

## Appendix C — our side

| What | Where |
|---|---|
| event shape, `use` id minting | `addons/VibeXI/vibexi.lua` (`record`) |
| reading it back | `damage_meter/web/lib/source.js` |
| `collapse`, aggregation | `damage_meter/web/lib/stats.js` |
| file tailer, `/api/events` | `damage_meter/damage-meter.py` |
| operational detail, gotchas | `damage_meter/CLAUDE.md` |
| test-event generator | `damage_meter/tools/gen-test-events.py` |
