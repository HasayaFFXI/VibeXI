# Damage Meter — packet/memory addon

Branch: `DamageMeter-Addon`. Nothing is built yet; this file is the whole plan.

## Why

`damage_meter/` reads the FFXI chat log as text. That works, but the ceiling is
documented in `damage_meter/CLAUDE.md` under "Known gaps" and it is a hard one:
the log does not say who is a player, does not say who owns a pet, does not flag
crits reliably, cannot distinguish a weaponskill from a job ability at the
announcement, and writes one damage line per AoE victim with no grouping.

An Ashita addon reading incoming packets and the client's own memory has all of
that as ground truth. The plan is to build that addon as a **thin event
emitter** and feed the existing browser UI, which already knows how to
aggregate.

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
grep -rhoE "AshitaCore:[A-Za-z]+|ashita\.[a-z_]+\.[a-z_]+|require\(['\"][a-z]+" addon/src/ | sort -u
```

Diff against `addon/ALLOWED_APIS.txt`; fail on anything not listed. Adding an
API becomes a deliberate edit to that file, never an accident. Seed list is in
the appendix.

### The bridge is one-way by construction

The addon **appends to a local file**. The PowerShell server tails it, exactly
as it already tails chat logs. Never open a socket from the addon, not even to
localhost — that would put network capability back in the process for a
convenience we do not need.

```
FFXI process             disk                     existing stack
[addon] ──write──▶ events.jsonl ──read──▶ [damage-meter.ps1] ──▶ [browser]
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

**5. One JSON line per (action, target), all sharing a `use` id minted in Lua.**
The packet hands over the target list directly, so `use` stops being the
5-second `AOE_MS` heuristic and becomes ground truth — and `stats.collapse()`,
already written and tested, works unchanged.

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
Phase 0 as a consequence of how `check-apis.ps1` extracts tokens.

**9. Three events, not one.** The plan said "register `packet_in` only". The
allowlist permits `packet_in`, `load` and `unload` — the latter two are
lifecycle hooks for opening and flushing/closing the output file, and neither
carries any capability. `d3d_present`, `command` and `packet_out` remain
excluded; `packet_out` is denylisted outright.

---

## Architecture

### Event contract

```json
{"t":1785000000,"seq":3,"use":41207,"kind":"ws","actor":"Hasaya","actorKind":"player",
 "action":"Tachi: Jinpu","target":"Goblin Pathfinder","targetKind":"mob",
 "dmg":723,"hit":true,"crit":false,"burst":false}
```

Field-compatible with what `parser.js` emits today, so `stats.js` needs no
changes. New fields (`actorKind`, `targetKind`, `owner`) are additive.

Three things get strictly better for free:

- **The roster stops guessing.** `spawn_flags` gives player/mob/pet outright.
  The article heuristic, the fixed-point propagation in `roster.rebuild`, and
  every `guess: true` event become unnecessary. Keep the manual override UI;
  delete the inference.
- **Pets carry their owner** via `pet_index`, closing a documented gap.
- **Crits, multi-attack and shadows become real flags** rather than phrasings.

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

### Proposed layout

```
addon/
  PLAN.md              this file
  ALLOWED_APIS.txt     the enforcement manifest
  check-apis.sh        the allowlist diff, wired to pre-commit
  src/
    vibexi.lua         entry: addon meta, packet_in dispatcher
    bitreader.lua      bit unpacking (written from XiPackets spec)
    action.lua         0x028 → action table
    entity.lua         mob / party / player memory reads
    enums.lua          message IDs, spawn flags, animations
    emit.lua           event → ASCII JSON → append to file
    resources/         ID→name tables
```

---

## Phases

### Phase 0 — safety rails — **DONE**

Built:

- `addon/ALLOWED_APIS.txt` — 51 tokens, 3 events, each grouped with why it is safe
- `addon/check-apis.ps1` — three independent checks (allowlist / denylist / event names)
- `addon/hooks/pre-commit` — runs it on every commit

Install the hook once, from the repo root:

```bash
git config core.hooksPath addon/hooks
```

Run it by hand any time:

```bash
powershell -ExecutionPolicy Bypass -File addon/check-apis.ps1
```

**PowerShell, not sh** (this plan originally said `check-apis.sh`): the repo is
PowerShell-first everywhere else and this way it runs without Git Bash. The hook
itself is a two-line `sh` wrapper, because that is what git invokes.

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

Source is in `addon/src/`:

| File | Role |
|---|---|
| `vibexi.lua` | entry, dedup, dispatch, naming, `use` minting |
| `vx_bitreader.lua` | LSB-first bit reader, written from the XiPackets spec |
| `vx_action.lua` | 0x028 / 0x029 → Lua tables |
| `vx_entity.lua` | entity + party memory reads, kind/owner classification |
| `vx_emit.lua` | event → ASCII JSON → appended file |
| `vx_enums.lua` | packet ids, categories, spawn flags, message ids |
| `vx_ws_names.lua` | generated; `addon/tools/gen-ws-names.ps1` rebuilds it |

**Output path — `%LOCALAPPDATA%\VibeXI\events\<Character>_<YYYY.MM.DD>.jsonl`.**
Deliberately not `%TEMP%` (Storage Sense deletes it, and this file *is* the
persistence layer), not `%APPDATA%`/Roaming (profile sync on a hot file), not
anywhere under OneDrive (continuous cloud sync on a file appended several times
a second), and not the Ashita tree (which on this machine is itself under
Roaming). Rationale is repeated in `vx_emit.lua`'s header where someone editing
the path will actually see it.

**Verified without a game:**

- `check-apis.ps1` green — 7 files, 43 distinct external calls, all allowlisted
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
   expression, which will still surface on `/addon load vibexi`.
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
`check-apis.ps1` is still green.

**First-run checklist:**

1. Copy `addon/src/` to `…\HorizonXI\Game\addons\vibexi\`, with `vibexi.lua` as
   the entry point.
2. `/addon load vibexi`
3. Check `%LOCALAPPDATA%\VibeXI\events\` for a file appearing.
4. Read line 1 of that file — the `"kind":"meta"` probe. Expect
   `"chunkData":"string"`, `"isZoningType":"number"`, `"selfSpawnFlags":525`,
   and `abilOffset` naming a job ability while `abilRaw` names a weaponskill.
   Anything else points straight at the assumption it belongs to.
5. Kill one mob. Compare the JSONL totals against the chat log for the same
   fight — **if every number is exactly double, it is the `chunk_data` dedup**.
6. Point the server at it: `-LogDir %LOCALAPPDATA%\VibeXI\events`

### Phase 2 — server serves it

- Point `-LogDir` at the addon's output directory
- Let the newest-file scan accept `.jsonl` alongside `.log`

`/api/log` is already dumb and returns raw lines, and `Read-LogTail` already
holds back partial lines, so a mid-write flush is safe. This should be a very
small change.

**Done when:** `/api/log` returns JSONL lines with a correct `nextOffset`.

### Phase 3 — client picks a source

- New `damage_meter/web/lib/source-jsonl.js`: `JSON.parse` per line → event, DOM-free, same contract as `parser.js`
- `app.js` chooses by file extension
- **Keep `parser.js`** — it is how historical chat logs are read and it is the fallback
- `stats.js` untouched; `roster` keeps manual overrides, drops inference when `actorKind` is present

**Done when:** the UI renders from a live JSONL file, and a chat log still
renders exactly as it does today.

### Phase 4 — the metrics that were previously impossible

Multi-attack rounds, defense/mitigation, TP-at-weaponskill, real resist rates.
Also worth revisiting from the earlier Metrics comparison: active-time duration
(`AUTOPAUSE = 5`), rolling DPS (3s × 3 buckets), running accuracy over last N.

---

## Open questions

1. **HorizonXI addon policy.** Metrics carries a `-- Horizon Approved Addon 0457`
   marker; a custom addon does not. A read-only derivative of an approved parser
   is *probably* fine, but that is an inference, not a verified fact. **Ask in
   their Discord before attaching this to a real account.** Unresolved.
2. **Where the addon installs.** Presumably
   `...\HorizonXI\Game\addons\vibexi\`, loaded with `/addon load vibexi`.
   Confirm against how Metrics is registered.
3. **Output directory.** Ashita's `config/addons/<name>/` via
   `AshitaCore:GetInstallPath()` + `ashita.fs.create_dir`, matching what
   `file.lua` does. Needs to be somewhere `damage-meter.ps1` can be pointed at.

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
| event shape, `use` id minting | `damage_meter/web/lib/parser.js` |
| `collapse`, aggregation | `damage_meter/web/lib/stats.js` |
| log tailer, `/api/log`, CP932 | `damage_meter/damage-meter.ps1` |
| operational detail, gotchas | `damage_meter/CLAUDE.md` |
| test-log generator | `damage_meter/tools/gen-test-log.ps1` |
