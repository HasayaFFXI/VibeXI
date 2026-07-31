-- VibeXI -- read-only combat event recorder for the VibeXI damage meter.
--
-- Watches incoming action packets, resolves the ids in them against the
-- client's own entity table, and appends one JSON line per (action, target) to
-- a local file. That file is the only output. See addon/PLAN.md.
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
-- This is enforced mechanically, not by discipline: addon/check-apis.ps1 diffs
-- every external call in this directory against addon/ALLOWED_APIS.txt and
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
addon.author  = 'VibeXI'
addon.version = '0.1.0'

-- ---------------------------------------------------------------- state

local S = {
    use      = 0,       -- monotonic id shared by every row of one action
    seq      = 0,       -- ordering within a single wall-clock second
    seq_at   = 0,
    owner    = nil,     -- our character name, for the filename
    dropped  = 0,       -- packets that failed to decode
    dupes    = 0,
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

    -- One use id for the whole action, however many targets it reached. This is
    -- the grouping the chat parser had to infer with a 5-second heuristic; here
    -- the packet states it outright, and stats.collapse() consumes it unchanged.
    S.use = S.use + 1
    local use = S.use

    local name_resolved = nil

    for _, target in ipairs(act.targets) do
        local tgt = Entity.by_id(target.id)
        local tgt_name = 'Unknown'
        local tgt_kind = 'other'
        if tgt then
            tgt_name = tgt.name
            tgt_kind = Entity.kind(tgt)
        end

        for _, res in ipairs(target.results) do
            if not name_resolved then
                name_resolved = action_name(act.category, act.param, res)
            end

            local no_damage = E.NoDamage[res.message] == true
            local dmg = res.value or 0
            if no_damage then dmg = 0 end

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
                hit        = not no_damage,
                crit       = E.Crit[res.message] == true,
                burst      = res.message == E.Message.BURST,
                msg        = res.message,
                owner      = owner,
                pet        = pet_name,
            })
        end
    end
end

--- Is this action worth recording? Anything where the party is on either side.
--- Unrelated fights happening nearby are ignored entirely.
local function involves_party(actor, act)
    if Entity.in_party(actor.name) then return true end

    local k = Entity.kind(actor)
    if k == 'pet' and Entity.pet_owner(actor) then return true end

    -- Defensive: a monster acting on one of ours. Tagged and emitted so the
    -- damage-taken views in Phase 4 have data; the UI drops non-player actors
    -- from its totals today, so this costs nothing now.
    for _, target in ipairs(act.targets) do
        local tgt = Entity.by_id(target.id)
        if tgt then
            if Entity.in_party(tgt.name) then return true end
            if Entity.kind(tgt) == 'pet' and Entity.pet_owner(tgt) then return true end
        end
    end
    return false
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
        Entity.refresh_party(now)

        local act = Action.parse(p.data)
        if not act then S.dropped = S.dropped + 1 return end

        local actor = Entity.by_id(act.actor_id)
        if not actor then return end

        if involves_party(actor, act) then
            record(act, actor, now)
        end
    end)

    -- ALWAYS false. True would block the packet from reaching the client.
    return false
end)
