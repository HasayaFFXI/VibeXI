-- VibeXI -- read-only combat event recorder for the VibeXI damage meter.
--
-- Watches incoming action packets, resolves the ids in them against the
-- client's own entity table, and appends one JSON line per (action, target) to
-- a local file. That file is the only output. See addon-dev/PLAN.md.
--
-- Horizon Approved Addon -- ticket addon-0032, approved by Aerec 2026-09-04.
-- Approval covers this addon as described below: read-only, no outgoing
-- anything. Widening that surface needs a new ticket, not a local edit.
--
-- ============================================================================
-- THIS ADDON NEVER SENDS ANYTHING TO THE GAME SERVER.
--
--   * No outgoing-packet subscription and no injection call.
--   * No chat manager, so no QueueCommand -- the only route to a command.
--   * No socket library, so no network capability of any kind.
--   * The packet_in callback ALWAYS returns false. Returning true would BLOCK
--     the packet from reaching the client, which is interference with the game
--     even though it is inbound. We observe; we never intervene.
--
-- This is enforced mechanically, not by discipline: addon-dev/check-apis.py diffs
-- every external call in this directory against addon-dev/ALLOWED_APIS.txt and
-- fails on anything unlisted. Run it before you commit.
-- ============================================================================
--
-- Conventions in this tree:
--   * Dot-calls only on our own modules (PLAN.md Decision 8) -- every ':' call
--     you see is therefore an Ashita SDK call or a file handle.
--   * string.xxx(s, ...) rather than s:xxx(...), for the same reason.
--   * Nothing on the packet path may throw. It runs on the game's thread.

require('common')

local E        = require('vx_enums')
local Action   = require('vx_action')
local Entity   = require('vx_entity')
local Emit     = require('vx_emit')
local WS_NAMES = require('vx_ws_names')

addon.name    = 'VibeXI'
addon.author  = 'HasayaFFXI'
addon.version = '0.2.1'

-- ---------------------------------------------------------------- state

local S = {
    use      = 0,       -- monotonic id shared by every row of one action
    seq      = 0,       -- ordering within a single wall-clock second
    seq_at   = 0,
    owner    = nil,     -- our character name, for the filename
    dropped  = 0,       -- packets that failed to decode
    dupes    = 0,
    unknown  = 0,       -- distinct message ids in neither allowlist
    probed   = false,   -- environment probe written; see probe()
}

-- Duplicate suppression. Ashita can hand the same packet to an addon twice
-- across chunk boundaries, which would double every number. Metrics solves this
-- with FFI memcmp over raw pointers; we do the same comparison on Lua strings,
-- because FFI is a foreign-function escape hatch this addon deliberately does
-- not have (PLAN.md Decision 2).
--
-- The packet event table really does carry the chunk: Ashita exports
-- chunk_data, chunk_data_raw and chunk_size next to data, data_raw,
-- data_modified and data_modified_raw. Ashita's own addons use the non-_raw
-- members as Lua strings (struct.unpack over e.data) and the _raw ones as FFI
-- pointers (ffi.cast over e.data_modified_raw), which is the pairing this
-- relies on. The probe reports the observed type of p.chunk_data anyway.
local last_chunk    = {}
local current_chunk = {}

local function is_duplicate(p)
    local data = p.data
    if not data then return false end

    -- A packet identical to the head of its chunk is the first packet OF that
    -- chunk, which means a new chunk started and the previous one can retire.
    if p.chunk_data and string.sub(p.chunk_data, 1, #data) == data then
        last_chunk    = current_chunk
        current_chunk = {}
    end

    for i = 1, #last_chunk do
        if last_chunk[i] == data then return true end
    end

    current_chunk[#current_chunk + 1] = data
    -- Bound the buffer; a chunk never holds many packets and an unbounded table
    -- on the packet path is a slow leak.
    if #current_chunk > 64 then table.remove(current_chunk, 1) end

    return false
end

-- ---------------------------------------------------------------- naming

local function ability_name(id)
    local rm = AshitaCore:GetResourceManager()
    if not rm then return nil end
    local a = rm:GetAbilityById(id)
    if a and a.Name and a.Name[1] and a.Name[1] ~= '' then return a.Name[1] end
    return nil
end

local function spell_name(id)
    local rm = AshitaCore:GetResourceManager()
    if not rm then return nil end
    local s = rm:GetSpellById(id)
    if s and s.Name and s.Name[1] and s.Name[1] ~= '' then return s.Name[1] end
    return nil
end

local function melee_name(animation)
    if animation == E.Animation.MELEE_OFFHAND then return 'Offhand Attack' end
    if animation == E.Animation.MELEE_KICK
       or animation == E.Animation.MELEE_KICK2 then return 'Kick Attack' end
    if animation == E.Animation.DAKEN then return 'Throwing' end
    return 'Attack'
end

--- Resolve the display name for an action. Always paired with the raw id in the
--- event, so a lookup miss degrades to "#123" and loses nothing but readability.
local function action_name(category, param, first_result)
    if category == E.Category.MELEE then
        return melee_name(first_result and first_result.animation or 0)
    elseif category == E.Category.RANGED then
        return 'Ranged Attack'
    elseif category == E.Category.WEAPONSKILL then
        -- Category 3 also carries a handful of job abilities (Mug, Steal,
        -- Shield Bash, the Jumps...), which is exactly the ambiguity the chat
        -- log could never resolve. Weaponskill table first, then abilities.
        local n = WS_NAMES[param]
        if n then return n end
        n = ability_name(param + E.ABILITY_ID_OFFSET)
        if n then return n end
    elseif category == E.Category.MAGIC then
        local n = spell_name(param)
        if n then return n end
    elseif category == E.Category.ABILITY then
        local n = ability_name(param + E.ABILITY_ID_OFFSET)
        if n then return n end
    end
    -- Monster TP moves and pet abilities land here. Ashita's resource manager
    -- has no lookup for them and the name tables are ~300KB, so Phase 1 emits
    -- the id and leaves naming to a later pass.
    return '#' .. tostring(param)
end

-- ---------------------------------------------------------------- emit

--- One-off environment probe, written as the first line of every file.
---
--- This addon was written without a Lua runtime available to test it against,
--- so several SDK behaviours started out assumed rather than known. Most have
--- since been settled against the Ashita source tree; the probe stays because
--- confirming them live costs one line per file and turns "should work" into
--- "did work on this machine, this build, this server".
---
--- What each field answers:
---
---   chunkData      Duplicate suppression needs p.chunk_data to be a Lua string.
---                  Ashita's addon host does export chunk_data / chunk_data_raw
---                  alongside data / data_raw, and the non-_raw member of each
---                  pair is the string one. If this ever reads "nil", chunks
---                  never rotate, duplicates are never caught, and EVERY NUMBER
---                  IS DOUBLED -- so it is worth one field forever.
---   isZoningType   Expected "number" (Ashita.h returns uint32_t). A "boolean"
---                  here means vx_entity.is_zoning's fallback branch is load
---                  bearing rather than belt-and-braces.
---   selfSpawnFlags Expected 525 = PLAYER|PARTY|ALLIANCE|LOCALPLAYER. Read it as
---                  bits, not as a magic number; see vx_enums.SpawnFlags.
---   abilRaw/abilOffset
---                  The +512 question, asked as an experiment instead of an
---                  assertion. The SAME id is looked up both ways. A weaponskill
---                  name in abilRaw and a job ability in abilOffset means the
---                  table is segmented as assumed and E.ABILITY_ID_OFFSET is
---                  right. If the names come back swapped, the offset is wrong.
local function probe(p)
    local player = GetPlayerEntity()
    local mm = AshitaCore:GetMemoryManager()
    local zoning_type = 'nil'
    if mm and mm:GetPlayer() then
        zoning_type = type(mm:GetPlayer():GetIsZoning())
    end

    local self_flags = -1
    if player then
        local ent = Entity.by_id(player.ServerId)
        if ent and ent.spawn_flags then self_flags = ent.spawn_flags end
    end

    local ABIL_PROBE_ID = 5
    local abil_raw    = ability_name(ABIL_PROBE_ID) or 'MISS'
    local abil_offset = ability_name(ABIL_PROBE_ID + E.ABILITY_ID_OFFSET) or 'MISS'
    local spell_probe = spell_name(1) or 'MISS'
    local ws_probe    = WS_NAMES[1] or 'MISS'

    local parts = {
        '{"kind":"meta","v":"' .. Emit.escape(addon.version) .. '"',
        ',"t":' .. tostring(os.time()),
        ',"chunkData":"' .. Emit.escape(type(p.chunk_data)) .. '"',
        ',"injected":"' .. Emit.escape(type(p.injected)) .. '"',
        ',"dataLen":' .. tostring(#(p.data or '')),
        ',"isZoningType":"' .. Emit.escape(zoning_type) .. '"',
        ',"selfName":"' .. Emit.escape(Entity.me() or 'nil') .. '"',
        ',"selfSpawnFlags":' .. tostring(self_flags),
        ',"abilProbeId":' .. tostring(ABIL_PROBE_ID),
        ',"abilRaw":"'    .. Emit.escape(abil_raw) .. '"',
        ',"abilOffset":"' .. Emit.escape(abil_offset) .. '"',
        ',"spellLookup":"' .. Emit.escape(spell_probe) .. '"',
        ',"wsLookup":"'    .. Emit.escape(ws_probe) .. '"',
        ',"path":"' .. Emit.escape(Emit.path or 'nil') .. '"',
        '}',
    }
    Emit.write_raw(table.concat(parts))
end

--- Record a message id that is in neither allowlist, once per id per session.
---
--- The allowlists drop what they do not recognise, which is the right default
--- -- but silently, and a silent drop is indistinguishable from a bug. One meta
--- line per NEW id makes the omission visible and turns "should I add 431?"
--- into a question with evidence attached: the id, the category it arrived on,
--- and the action that produced it. Consumers already skip kind:"meta".
---
--- Once per id, so a buff-heavy party cannot flood the file.
local seen_unknown = {}

local function note_unknown(message, act)
    if seen_unknown[message] then return end
    seen_unknown[message] = true
    S.unknown = S.unknown + 1
    Emit.write_raw(
        '{"kind":"meta","unknownMsg":' .. string.format('%d', message) ..
        ',"category":' .. string.format('%d', act.category) ..
        ',"actionId":' .. string.format('%d', act.param) .. '}')
end

--- Write one line per party member whose job changed since the last look.
---
--- Fed by Entity.refresh_party, which is the only thing in the addon that reads
--- the party table and therefore the only thing that can see a job at all. It
--- returns nil on the overwhelming majority of passes -- jobs change about once
--- a session -- so this runs a handful of times per file.
---
--- The job of a party member who never swings is still recorded, and that is the
--- point: the meter lists the party, not only the characters who dealt damage.
local function record_jobs(changed, now)
    for i = 1, #changed do
        local m = changed[i]
        Emit.write_job({
            t       = now,
            actor   = m.name,
            main    = E.job(m.main),
            mainId  = m.main,
            mainLvl = m.main_lvl,
            sub     = E.job(m.sub),
            subId   = m.sub,
            subLvl  = m.sub_lvl,
        })
    end
end

local function next_seq(now)
    if now ~= S.seq_at then
        S.seq_at = now
        S.seq = 0
    end
    S.seq = S.seq + 1
    return S.seq
end

--- Turn one decoded action packet into rows and write them.
local function record(act, actor, now)
    local kind = E.EmitCategory[act.category]
    if not kind then return end

    local actor_kind = Entity.kind(actor)
    local owner, pet_name
    if actor_kind == 'pet' then
        local o = Entity.pet_owner(actor)
        if o then
            owner    = o.name
            pet_name = actor.name
        end
    end

    -- ONE USE ID PER SWING, SHARED ACROSS THE TARGETS THAT SWING REACHED.
    --
    -- The two dimensions of an action packet mean different things and must not
    -- be collapsed together:
    --
    --   several TARGETS, one result each   an AoE. One use of the action; the
    --                                      packet states the target list, which
    --                                      is the grouping the chat parser had
    --                                      to infer with a 5-second window.
    --   one target, several RESULTS        a multi-attack round. Genuinely two
    --                                      or three separate swings, each with
    --                                      its own hit-or-miss outcome.
    --
    -- So the id is keyed on the result's position, not on the action: result 1
    -- across every target is one use, result 2 across every target is the next.
    -- Sharing a single id for the whole action instead would fold a multi-attack
    -- round into one swing -- and since stats.collapse() treats `hit` as "any",
    -- a round where the first swing landed and the second whiffed would report
    -- one hit and NO miss. The swing count is the denominator accuracy divides
    -- by, so that reads as a party that never misses.
    local use_by_slot = {}

    local function use_for(slot)
        local u = use_by_slot[slot]
        if not u then
            S.use = S.use + 1
            u = S.use
            use_by_slot[slot] = u
        end
        return u
    end

    -- The proc trailers are per ACTION, not per swing: one weaponskill closes
    -- one skillchain however many targets or swings it involved. Minted lazily,
    -- so an action with no trailer costs no id.
    local sc_use, addl_use

    local name_resolved = nil

    for _, target in ipairs(act.targets) do
        local tgt = Entity.by_id(target.id)
        local tgt_name = 'Unknown'
        local tgt_kind = 'other'
        if tgt then
            tgt_name = tgt.name
            tgt_kind = Entity.kind(tgt)
        end

        for slot, res in ipairs(target.results) do
            local use = use_for(slot)

            -- Three outcomes, and the default is DROP. A message in neither
            -- table is not a damage event -- a cure, a buff, an enfeeble, a
            -- status tick -- and its `value` field means something other than
            -- hit points, so emitting it would put a non-damage number into a
            -- field the browser sums as damage. See vx_enums.E.Damage.
            local is_damage  = E.Damage[res.message] == true
            local is_attempt = E.Attempt[res.message] == true

            if is_damage or is_attempt then
                -- Resolved on the first row we actually emit, not the first row
                -- we see, so an action whose every result was dropped costs no
                -- resource lookup.
                if not name_resolved then
                    name_resolved = action_name(act.category, act.param, res)
                end

                local dmg = 0
                if is_damage then dmg = res.value or 0 end

                Emit.write({
                    t          = now,
                    seq        = next_seq(now),
                    use        = use,
                    kind       = kind,
                    actor      = actor.name,
                    actorKind  = actor_kind,
                    action     = name_resolved,
                    actionId   = act.param,
                    target     = tgt_name,
                    targetKind = tgt_kind,
                    dmg        = dmg,
                    hit        = is_damage,
                    crit       = E.Crit[res.message] == true,
                    burst      = res.message == E.Message.BURST,
                    msg        = res.message,
                    owner      = owner,
                    pet        = pet_name,
                })
            else
                note_unknown(res.message, act)
            end

            -- The PROC trailer, which is a second event riding on this result.
            --
            -- Read INDEPENDENTLY of the branch above, never inside it: a
            -- skillchain must not be lost because its closing weaponskill's own
            -- message happened to be one we do not recognise. The trailer names
            -- its own outcome and carries its own damage in proc_value.
            if res.has_proc and res.proc_message and res.proc_message > 0 then
                -- The skillchain table is consulted ONLY on a weaponskill,
                -- because that is the only context Metrics consults it in. The
                -- same proc message means different things by category: 229 is
                -- 'DRG Jump Effect' on a weaponskill and an ENSPELL on a melee
                -- swing, so reading the table globally would file every enspell
                -- proc in the game as a skillchain. See E.Skillchains.
                local sc_name = nil
                if kind == 'ws' then sc_name = E.skillchain(res.proc_message) end

                if sc_name then
                    -- A skillchain is its own row, so it needs its own `use`:
                    -- sharing the weaponskill's would let stats.collapse() fold
                    -- the chain's damage into the weaponskill and lose it as a
                    -- separate action. Minted once per action, not once per
                    -- target, so an AoE weaponskill closing on three mobs is one
                    -- chain of summed damage -- the same rule the weaponskill
                    -- itself is under.
                    if not sc_use then
                        S.use = S.use + 1
                        sc_use = S.use
                    end
                    Emit.write({
                        t          = now,
                        seq        = next_seq(now),
                        use        = sc_use,
                        kind       = 'skillchain',
                        actor      = actor.name,
                        actorKind  = actor_kind,
                        -- The same string the UI has always used, and what its
                        -- drill-down test matches on.
                        action     = 'Skillchain: ' .. sc_name,
                        actionId   = res.proc_message,
                        target     = tgt_name,
                        targetKind = tgt_kind,
                        -- proc_value, never res.value: the chain's damage is
                        -- its own and is not part of the weaponskill's. This is
                        -- Metrics' H.TP.Skillchain_Damage, which reads
                        -- add_effect_param for exactly the same reason.
                        dmg        = res.proc_value or 0,
                        hit        = true,
                        crit       = false,
                        burst      = false,
                        msg        = res.proc_message,
                        owner      = owner,
                        pet        = pet_name,
                    })

                elseif E.Damage[res.proc_message] == true then
                    -- An additional effect: an enspell, a Sneak Attack proc, an
                    -- HP drain. Real damage the actor dealt, in a row of its own
                    -- rather than folded into the swing, exactly as the chat
                    -- parser filed it. Its own `use` for the same reason.
                    if not addl_use then
                        S.use = S.use + 1
                        addl_use = S.use
                    end
                    Emit.write({
                        t          = now,
                        seq        = next_seq(now),
                        use        = addl_use,
                        kind       = 'addl',
                        actor      = actor.name,
                        actorKind  = actor_kind,
                        action     = 'Additional Effect',
                        actionId   = res.proc_message,
                        target     = tgt_name,
                        targetKind = tgt_kind,
                        dmg        = res.proc_value or 0,
                        hit        = true,
                        crit       = false,
                        burst      = false,
                        msg        = res.proc_message,
                        owner      = owner,
                        pet        = pet_name,
                    })

                else
                    note_unknown(res.proc_message, act)
                end
            end
        end
    end
end

--- Is this action worth recording? ONLY if one of OURS is the actor.
---
--- This meter measures damage the party dealt. Damage dealt TO the party is
--- explicitly not wanted, so a monster's own swings are dropped here rather
--- than written and then filtered out in the browser -- on a long pull those
--- rows were a large fraction of the file and nothing ever read one.
---
--- "Ours" is party or alliance: Entity.refresh_party walks all 18 slots, and
--- trusts occupy party slots like anyone else. A pet qualifies only through
--- Entity.pet_owner, which searches those same slots, so a passing stranger's
--- pet is not ours and is not recorded.
---
--- NOT the whole test, because it asks only about the packet's actor. Damage
--- our side dealt as a REACTION -- a counter, spikes, retaliation -- rides on a
--- packet the monster is the actor of, so `has_reaction` gets a second look at
--- everything this returns false for. The same predicate then decides it, asked
--- of the entity that reacted rather than of the one that swung.
local function is_ours(actor)
    if Entity.in_party(actor.name) then return true end
    if Entity.kind(actor) == 'pet' and Entity.pet_owner(actor) then return true end
    return false
end

--- Does this action carry a reaction at all?
---
--- A cheap pre-test, and it has to be: this runs for every monster action in
--- range, and the overwhelming majority carry no reaction. Integer lookups over
--- the parsed result blocks only -- not one entity lookup until something is
--- actually there to credit.
local function has_reaction(act)
    for _, target in ipairs(act.targets) do
        for _, res in ipairs(target.results) do
            if E.Reaction[res.message] then return true end
            if res.has_react and res.react_message
               and E.SpikeReaction[res.react_message] then return true end
        end
    end
    return false
end

--- Emit what our side dealt back on somebody else's action.
---
--- THE INVERSION IS THE WHOLE FUNCTION. `attacker` is the packet's actor and
--- becomes the TARGET of every row written here; the result's target -- whoever
--- countered, retaliated, or had spikes up -- becomes the ACTOR. Read straight
--- instead, each of these credits a victim with their attacker's damage. See
--- the REACTION DAMAGE block in vx_enums.lua.
---
--- The is_ours test is applied to the ENTITY THAT REACTED, which is what makes
--- this symmetric rather than a special case for monsters: when the swing is
--- ours and the spikes are the monster's, the inverted actor is a monster and
--- nothing is written, exactly as for any other damage the monsters dealt.
---
--- ONE `use` PER ROW, minted fresh. A reaction is one entity's own answer to
--- one swing: an AoE that lands on two party members with spikes up is two
--- events by two different actors, and a shared id would let stats.collapse()
--- fold them into a single row under whichever came first.
local function record_reactions(act, attacker, now)
    local atk_kind = Entity.kind(attacker)

    for _, target in ipairs(act.targets) do
        local defender = Entity.by_id(target.id)
        if defender and is_ours(defender) then
            local d_kind = Entity.kind(defender)
            local owner, pet_name
            if d_kind == 'pet' then
                local o = Entity.pet_owner(defender)
                if o then
                    owner    = o.name
                    pet_name = defender.name
                end
            end

            local function emit(name, message, dmg)
                -- A reaction that dealt nothing is not written. It would enter
                -- the accuracy denominator as a swing this actor never took,
                -- and a zero would drag the average of an action whose entire
                -- content is its damage. The reaction ATTEMPTS that would be
                -- the honest denominator are unwired on purpose -- see the
                -- 535/592/14 note in vx_enums.lua.
                if not dmg or dmg <= 0 then return end
                S.use = S.use + 1
                Emit.write({
                    t          = now,
                    seq        = next_seq(now),
                    use        = S.use,
                    kind       = 'reaction',
                    actor      = defender.name,
                    actorKind  = d_kind,
                    action     = name,
                    actionId   = message,
                    target     = attacker.name,
                    targetKind = atk_kind,
                    dmg        = dmg,
                    hit        = true,
                    crit       = false,
                    burst      = false,
                    msg        = message,
                    owner      = owner,
                    pet        = pet_name,
                })
            end

            for _, res in ipairs(target.results) do
                -- Main slot: the counter or the retaliation that stopped this
                -- swing, with its damage in res.value.
                local reaction = E.Reaction[res.message]
                if reaction then emit(reaction, res.message, res.value) end

                -- React (spike) trailer: fires in ADDITION to the swing, so it
                -- is read whatever the main message said, and its damage is in
                -- res.react_value rather than res.value.
                if res.has_react and res.react_message then
                    local spike = E.SpikeReaction[res.react_message]
                    if spike then emit(spike, res.react_message, res.react_value) end
                end
            end
        end
    end
end

-- ---------------------------------------------------------------- events

ashita.events.register('load', 'vibexi_load', function()
    S.owner = Entity.me()
    Emit.open(S.owner)
end)

ashita.events.register('unload', 'vibexi_unload', function()
    Emit.close()
end)

ashita.events.register('packet_in', 'vibexi_packet_in', function(p)
    -- Wrapped whole: a decode bug must never surface as a Lua error on the
    -- game's thread. A dropped packet is a missing row; a thrown error is a
    -- broken client.
    pcall(function()
        if not p or not p.data then return end
        if p.id ~= E.Packet.ACTION then return end
        if not p.injected and is_duplicate(p) then
            S.dupes = S.dupes + 1
            return
        end

        local now = os.time()

        -- Lazily (re)open: the character is not known at load time when the
        -- addon is loaded from the character select screen, and the file rolls
        -- over at midnight.
        if not Emit.is_open() then
            S.owner = Entity.me() or S.owner
            if S.owner then Emit.open(S.owner) end
            if not Emit.is_open() then return end
        end

        if not S.probed then
            S.probed = true
            probe(p)
        end

        if Entity.is_zoning() then return end

        local job_changes = Entity.refresh_party(now)
        if job_changes then record_jobs(job_changes, now) end

        local act = Action.parse(p.data)
        if not act then S.dropped = S.dropped + 1 return end

        local actor = Entity.by_id(act.actor_id)
        if not actor then return end

        if is_ours(actor) then
            record(act, actor, now)
        elseif has_reaction(act) then
            -- The one reason a monster's own packet is still looked at: the
            -- damage our side dealt back on it.
            record_reactions(act, actor, now)
        end
    end)

    -- ALWAYS false. True would block the packet from reaching the client.
    return false
end)
