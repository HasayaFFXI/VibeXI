-- VibeXI -- read-only combat event recorder.
--
-- Watches incoming action packets, resolves the ids in them against the
-- client's own entity table, and appends one JSON line per (action, target) to
-- a local file. That file is the only output.
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
-- This is enforced mechanically, not by discipline: dev/check-apis.py diffs
-- every external call in this directory against dev/ALLOWED_APIS.txt and
-- fails on anything unlisted. Run it before you commit.
-- ============================================================================
--
-- Conventions in this tree:
--   * Dot-calls only on our own modules -- every ':' call
--     you see is therefore an Ashita SDK call or a file handle.
--   * string.xxx(s, ...) rather than s:xxx(...), for the same reason.
--   * Nothing on the packet path may throw. It runs on the game's thread.

require('common')

local E        = require('vx_enums')
local Action   = require('vx_action')
local Entity   = require('vx_entity')
local Emit     = require('vx_emit')
local WS_NAMES = require('vx_ws_names')
local Config   = require('vx_config')

-- Horizon=1 in vibexi.ini, which is also what a missing file or line means.
local HORIZON = Config.horizon

addon.name    = 'VibeXI'
addon.author  = 'HasayaFFXI'
addon.version = '1.1.0'


-- ---------------------------------------------------------------- state

local S = {
    use      = 0,       -- monotonic id shared by every row of one action
    seq      = 0,       -- ordering within a single wall-clock second
    seq_at   = 0,
    owner    = nil,     -- our character name, for the filename
    dropped  = 0,       -- packets that failed to decode
    dupes    = 0,
}

-- Duplicate suppression. Ashita can hand the same packet to an addon twice
-- across chunk boundaries, which would double every number. The comparison is
-- done on Lua strings rather than with FFI over raw pointers, because FFI is a
-- foreign-function escape hatch this addon deliberately does not have.
--
-- The packet event table really does carry the chunk: Ashita exports
-- chunk_data, chunk_data_raw and chunk_size next to data, data_raw,
-- data_modified and data_modified_raw. Ashita's own addons use the non-_raw
-- members as Lua strings (struct.unpack over e.data) and the _raw ones as FFI
-- pointers (ffi.cast over e.data_modified_raw), which is the pairing this
-- relies on.
local last_chunk    = {}
local current_chunk = {}

--- Retire the previous chunk when this packet opens a new one.
---
--- EVERY PACKET, not just the action packets that are checked for duplicates.
--- A new chunk is recognised by its FIRST packet, and that is whatever the
--- server put first -- an entity update as often as an action. Seeing only the
--- action packets, a chunk that opens with anything else never rotates, the
--- previous chunk's packets are never compared against, and a re-delivered
--- action goes through twice.
---
--- The first two bytes are the packet's id and size, so comparing them turns
--- away nearly every packet that is not the head without building a string.
---
--- A chunk that is not a string fails OPEN -- no rotation, so no duplicate is
--- caught -- rather than raising on every packet and recording nothing at
--- all.
local function note_chunk(chunk, data)
    if type(chunk) ~= 'string' then return end
    local c1, c2 = string.byte(chunk, 1, 2)
    local d1, d2 = string.byte(data, 1, 2)
    if c1 ~= d1 or c2 ~= d2 then return end
    if string.sub(chunk, 1, #data) == data then
        last_chunk    = current_chunk
        current_chunk = {}
    end
end

--- Was this action packet already delivered in the previous chunk?
local function is_duplicate(data)
    -- Lua strings are interned, so each comparison is a pointer check.
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

-- id -> name, for every lookup that found one. The resource tables do not
-- change while the game runs, and each lookup marshals a whole resource record
-- out of the SDK. Misses are not kept, so a miss is asked again next time.
local ability_names = {}
local spell_names   = {}

local function ability_name(id)
    local n = ability_names[id]
    if n then return n end
    local rm = AshitaCore:GetResourceManager()
    if not rm then return nil end
    local a = rm:GetAbilityById(id)
    if a and a.Name and a.Name[1] and a.Name[1] ~= '' then
        n = a.Name[1]
        ability_names[id] = n
        return n
    end
    return nil
end

local function spell_name(id)
    local n = spell_names[id]
    if n then return n end
    local rm = AshitaCore:GetResourceManager()
    if not rm then return nil end
    local s = rm:GetSpellById(id)
    if s and s.Name and s.Name[1] and s.Name[1] ~= '' then
        n = s.Name[1]
        spell_names[id] = n
        return n
    end
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
        -- Shield Bash, the Jumps...). Weaponskill table first, then abilities --
        -- which only reaches an ability whose id no weaponskill has. The ones
        -- that share an id never get here as category 3 with Horizon on:
        -- record() has already turned them into abilities.
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
    -- has no lookup for them and the name tables are ~300KB, so the event
    -- carries the id and leaves naming to whatever reads it.
    return '#' .. tostring(param)
end

-- ---------------------------------------------------------------- emit

--- Write one line per party member whose job changed since the last look.
---
--- Fed by Entity.refresh_party, which is the only thing in the addon that reads
--- the party table and therefore the only thing that can see a job at all. It
--- returns nil on the overwhelming majority of passes -- jobs change about once
--- a session -- so this runs a handful of times per file.
---
--- The job of a party member who never swings is still recorded, and that is the
--- point: the file describes the party, not only the characters who dealt damage.
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

--- Write a heal: one kind:"heal" line per resolved target result.
---
--- Every result on every target the client can resolve, its value as the
--- amount, whatever its message says. A target that does not resolve is
--- skipped. Proc trailers are not read: a heal has no skillchain or additional
--- effect to carry.
---
--- ONE `use` FOR THE WHOLE ACTION. A heal has no multi-attack dimension, so the
--- per-slot rule record() uses for swings does not apply: a Curaga on five
--- people is one cast.
---
--- One table for every heal line, for the same reason as `row` below: the
--- fields that are the action's are set once, and each line sets the rest.
local heal_row = {}

local function record_heal(act, actor, actor_kind, owner, pet_name, heal_name, via, now)
    local name = action_name(act.category, act.param, nil)
    -- Pet abilities have no resource lookup and come back as '#id'; name
    -- those from the same table that identified them.
    if string.sub(name, 1, 1) == '#' then name = heal_name end

    S.use = S.use + 1

    local h = heal_row
    h.t         = now
    h.use       = S.use
    h.via       = via
    h.actor     = actor.name
    h.actorKind = actor_kind
    h.action    = name
    h.actionId  = act.param
    h.owner     = owner
    h.pet       = pet_name

    for _, target in ipairs(act.targets) do
        local tgt = Entity.by_id(target.id)
        if tgt then
            local tgt_kind = Entity.kind(tgt)
            for _, res in ipairs(target.results) do
                h.seq        = next_seq(now)
                h.target     = tgt.name
                h.targetKind = tgt_kind
                h.hp         = res.value or 0
                h.msg        = res.message
                Emit.write_heal(h)
            end
        end
    end
end

--- The first result in the packet, or nil when no target carries one.
local function first_result(act)
    for _, target in ipairs(act.targets) do
        local res = target.results[1]
        if res then return res end
    end
    return nil
end

--- The `use` id for result slot `slot` of the action record() is writing,
--- minted on first sight. See ONE USE ID PER SWING in record().
local function use_for(use_by_slot, slot)
    local u = use_by_slot[slot]
    if not u then
        S.use = S.use + 1
        u = S.use
        use_by_slot[slot] = u
    end
    return u
end

-- The damage line being written, ONE TABLE REFILLED FOR EVERY LINE. Emit.write
-- encodes a line on the spot and keeps nothing, so a fresh table per line would
-- only be garbage for the game's thread to collect.
--
-- Every writer assigns every field, nil included -- that is how owner and pet
-- are cleared -- so nothing carries over from one line to the next. record()
-- sets the action's five (t, actor, actorKind, owner, pet) once, each target's
-- two (target, targetKind) at the top of its loop, and the other ten per line;
-- emit_reaction sets all seventeen itself.
local row = {}

--- Turn one decoded action packet into rows and write them. `actor_kind` and
--- `pet_owner` are what classify() already worked out for the actor.
local function record(act, actor, actor_kind, pet_owner, now)
    -- A JOB ABILITY SENT AS A WEAPONSKILL is recorded as the ability: named
    -- from the ability table and written as kind 'ability', exactly as if it
    -- had arrived on category 6. Decided once for the whole action, from its
    -- first result -- see E.WsAbilities for why not per result. Only the name
    -- and the kind follow `category`; anything that asks what the packet
    -- said (the heal check below) still reads act.category.
    local category = act.category
    if HORIZON and category == E.Category.WEAPONSKILL then
        local first = first_result(act)
        if first and E.ws_ability(act.param, first.message) then
            category = E.Category.ABILITY
        end
    end

    local kind = E.EmitCategory[category]
    if not kind then return end

    local owner, pet_name
    if pet_owner then
        owner    = pet_owner.name
        pet_name = actor.name
    end

    -- HEALING IS DECIDED BY THE ACTION'S ID, BEFORE ANY MESSAGE IS LOOKED AT, and
    -- a healing action never reaches the damage branch below (see the HEALING
    -- block in vx_enums.lua): a cure is healing whatever its result message
    -- says, and healing never counts as damage.
    local heal_name, via = E.healing(act.category, act.param)
    if heal_name then
        record_heal(act, actor, actor_kind, owner, pet_name, heal_name, via, now)
        return
    end

    -- ONE USE ID PER SWING, SHARED ACROSS THE TARGETS THAT SWING REACHED.
    --
    -- The two dimensions of an action packet mean different things and must not
    -- be collapsed together:
    --
    --   several TARGETS, one result each   an AoE. One use of the action; the
    --                                      packet states the target list.
    --   one target, several RESULTS        a multi-attack round. Genuinely two
    --                                      or three separate swings, each with
    --                                      its own hit-or-miss outcome.
    --
    -- So the id is keyed on the result's position, not on the action: result 1
    -- across every target is one use, result 2 across every target is the next.
    -- Sharing a single id for the whole action instead would fold a multi-attack
    -- round into one swing -- and to a reader that groups rows by `use` and
    -- treats `hit` as "any", a round where the first swing landed and the second
    -- whiffed would read as one hit and NO miss. The swing count is the
    -- denominator accuracy divides by, so that reads as a party that never
    -- misses.
    local use_by_slot = {}

    -- The proc trailers are per ACTION, not per swing: one weaponskill closes
    -- one skillchain however many targets or swings it involved. Minted lazily,
    -- so an action with no trailer costs no id.
    local sc_use, addl_use

    local name_resolved = nil

    row.t         = now
    row.actor     = actor.name
    row.actorKind = actor_kind
    row.owner     = owner
    row.pet       = pet_name

    for _, target in ipairs(act.targets) do
        local tgt = Entity.by_id(target.id)
        row.target     = 'Unknown'
        row.targetKind = 'other'
        if tgt then
            row.target     = tgt.name
            row.targetKind = Entity.kind(tgt)
        end

        for slot, res in ipairs(target.results) do
            local use = use_for(use_by_slot, slot)

            -- Three outcomes, and the default is DROP. A message in neither
            -- table is not a damage event -- a cure, a buff, an enfeeble, a
            -- status tick -- and its `value` field means something other than
            -- hit points, so emitting it would put a non-damage number into a
            -- field that is summed as damage. See vx_enums.E.Damage. Nothing
            -- is written about a dropped message either: the file holds only
            -- what the allowlists name.
            local is_damage  = E.Damage[res.message] == true
            local is_attempt = E.Attempt[res.message] == true

            if is_damage or is_attempt then
                -- Resolved on the first row we actually emit, not the first row
                -- we see, so an action whose every result was dropped costs no
                -- resource lookup.
                if not name_resolved then
                    name_resolved = action_name(category, act.param, res)
                end

                local dmg = 0
                if is_damage then dmg = res.value or 0 end

                row.seq      = next_seq(now)
                row.use      = use
                row.kind     = kind
                row.action   = name_resolved
                row.actionId = act.param
                row.dmg      = dmg
                row.hit      = is_damage
                row.crit     = E.Crit[res.message] == true
                row.burst    = res.message == E.Message.BURST
                row.msg      = res.message
                Emit.write(row)
            end

            -- The PROC trailer, which is a second event riding on this result.
            --
            -- Read INDEPENDENTLY of the branch above, never inside it: a
            -- skillchain must not be lost because its closing weaponskill's own
            -- message happened to be one we do not recognise. The trailer names
            -- its own outcome and carries its own damage in proc_value.
            if res.has_proc and res.proc_message and res.proc_message > 0 then
                -- The skillchain table is consulted ONLY on a weaponskill. The
                -- same proc message means different things by category: 229 is
                -- 'DRG Jump Effect' on a weaponskill and an ENSPELL on a melee
                -- swing, so reading the table globally would file every enspell
                -- proc in the game as a skillchain. See E.Skillchains.
                local sc_name = nil
                if kind == 'ws' then sc_name = E.skillchain(res.proc_message) end

                if sc_name then
                    -- A skillchain is its own row, so it needs its own `use`:
                    -- sharing the weaponskill's would let a reader that groups
                    -- by `use` fold the chain's damage into the weaponskill and
                    -- lose it as a separate action. Minted once per action, not once per
                    -- target, so an AoE weaponskill closing on three mobs is one
                    -- chain of summed damage -- the same rule the weaponskill
                    -- itself is under.
                    if not sc_use then
                        S.use = S.use + 1
                        sc_use = S.use
                    end
                    row.seq      = next_seq(now)
                    row.use      = sc_use
                    row.kind     = 'skillchain'
                    row.action   = 'Skillchain: ' .. sc_name
                    row.actionId = res.proc_message
                    -- proc_value, never res.value: the chain's damage is its
                    -- own and is not part of the weaponskill's.
                    row.dmg      = res.proc_value or 0
                    row.hit      = true
                    row.crit     = false
                    row.burst    = false
                    row.msg      = res.proc_message
                    Emit.write(row)

                elseif E.Damage[res.proc_message] == true then
                    -- An additional effect: an enspell, a Sneak Attack proc, an
                    -- HP drain. Real damage the actor dealt, in a row of its own
                    -- rather than folded into the swing. Its own `use` for the
                    -- same reason.
                    if not addl_use then
                        S.use = S.use + 1
                        addl_use = S.use
                    end
                    row.seq      = next_seq(now)
                    row.use      = addl_use
                    row.kind     = 'addl'
                    row.action   = 'Additional Effect'
                    row.actionId = res.proc_message
                    row.dmg      = res.proc_value or 0
                    row.hit      = true
                    row.crit     = false
                    row.burst    = false
                    row.msg      = res.proc_message
                    Emit.write(row)
                end
            end
        end
    end
end

--- Is this action worth recording? ONLY if one of OURS is the actor.
---
--- This addon records damage the party dealt. Damage dealt TO the party is
--- explicitly not wanted, so a monster's own swings are dropped here rather
--- than written for a reader to filter out -- on a long pull those rows would
--- be a large fraction of the file.
---
--- "Ours" is party or alliance: Entity.refresh_party walks all 18 slots, and
--- trusts occupy party slots like anyone else. A pet qualifies only through
--- Entity.pet_owner, which searches those same members, so a passing
--- stranger's pet is not ours and is not recorded.
---
--- NOT the whole test, because it asks only about the packet's actor. Damage
--- our side dealt as a REACTION -- a counter, spikes, retaliation -- rides on a
--- packet the monster is the actor of, so `Action.has_reaction` gets a second
--- look at everything this returns false for. The same predicate then decides
--- it, asked of the entity that reacted rather than of the one that swung.
---
--- Returns the answer along with the entity's kind and, for a pet, its owner's
--- record -- the two things every caller needs next. The owner search is a
--- round of SDK calls per party member, so it is done once, here, and handed on.
local function classify(ent)
    local kind = Entity.kind(ent)
    local owner = nil
    if kind == 'pet' then owner = Entity.pet_owner(ent) end
    local ours = Entity.in_party(ent.name) or owner ~= nil
    return ours, kind, owner
end

--- One reaction row: `defender` dealt `dmg` back to `attacker`.
local function emit_reaction(now, defender, d_kind, attacker, atk_kind,
                             owner, pet_name, name, message, dmg)
    -- A reaction that dealt nothing is not written. It would enter the
    -- accuracy denominator as a swing this actor never took, and a zero would
    -- drag the average of an action whose entire content is its damage. The
    -- reaction ATTEMPTS that would be the honest denominator are unwired on
    -- purpose -- see the 535/592/14 note in vx_enums.lua.
    if not dmg or dmg <= 0 then return end
    S.use = S.use + 1
    row.t          = now
    row.seq        = next_seq(now)
    row.use        = S.use
    row.kind       = 'reaction'
    row.actor      = defender.name
    row.actorKind  = d_kind
    row.action     = name
    row.actionId   = message
    row.target     = attacker.name
    row.targetKind = atk_kind
    row.dmg        = dmg
    row.hit        = true
    row.crit       = false
    row.burst      = false
    row.msg        = message
    row.owner      = owner
    row.pet        = pet_name
    Emit.write(row)
end

--- Emit what our side dealt back on somebody else's action.
---
--- THE INVERSION IS THE WHOLE FUNCTION. `attacker` is the packet's actor and
--- becomes the TARGET of every row written here; the result's target -- whoever
--- countered, retaliated, or had spikes up -- becomes the ACTOR. Read straight
--- instead, each of these credits a victim with their attacker's damage. See
--- the REACTION DAMAGE block in vx_enums.lua.
---
--- The classify() test is applied to the ENTITY THAT REACTED, which is what makes
--- this symmetric rather than a special case for monsters: when the swing is
--- ours and the spikes are the monster's, the inverted actor is a monster and
--- nothing is written, exactly as for any other damage the monsters dealt.
---
--- ONE `use` PER ROW, minted fresh. A reaction is one entity's own answer to
--- one swing: an AoE that lands on two party members with spikes up is two
--- events by two different actors, and a shared id would let a reader that
--- groups by `use` fold them into a single row under whichever came first.
local function record_reactions(act, attacker, atk_kind, now)
    for _, target in ipairs(act.targets) do
        local defender = Entity.by_id(target.id)
        local ours, d_kind, d_owner = false, nil, nil
        if defender then ours, d_kind, d_owner = classify(defender) end
        if ours then
            local owner, pet_name
            if d_owner then
                owner    = d_owner.name
                pet_name = defender.name
            end

            for _, res in ipairs(target.results) do
                -- Main slot: the counter or the retaliation that stopped this
                -- swing, with its damage in res.value.
                local reaction = E.Reaction[res.message]
                if reaction then
                    emit_reaction(now, defender, d_kind, attacker, atk_kind,
                                  owner, pet_name, reaction, res.message, res.value)
                end

                -- React (spike) trailer: fires in ADDITION to the swing, so it
                -- is read whatever the main message said, and its damage is in
                -- res.react_value rather than res.value.
                if res.has_react and res.react_message then
                    local spike = E.SpikeReaction[res.react_message]
                    if spike then
                        emit_reaction(now, defender, d_kind, attacker, atk_kind,
                                      owner, pet_name, spike, res.react_message,
                                      res.react_value)
                    end
                end
            end
        end
    end
end

-- ---------------------------------------------------------------- events

ashita.events.register('load', 'vibexi_load', function()
    -- Only once the character is known: the file is named for them, and named
    -- once per load. Loaded from the character select screen, the first packet
    -- after login opens it instead (see packet_in).
    S.owner = Entity.me()
    if S.owner then Emit.open(S.owner) end
end)

ashita.events.register('unload', 'vibexi_unload', function()
    Emit.close()
end)

--- Everything packet_in does, as a named function so the callback can pcall it
--- directly: it runs for every packet the client receives, and a fresh closure
--- per packet is garbage the game's thread has to collect.
local function on_packet_in(p)
    if not p then return end
    local data = p.data
    if not data then return end
    local id = p.id

    -- Before any packet is turned away by id: see note_chunk.
    local injected = p.injected
    if not injected then note_chunk(p.chunk_data, data) end

    -- ROSTER UPDATES. 0x0C8 announces an alliance change, 0x0DD a party
    -- member change; between them they cover forming an alliance, merging a
    -- party into one, and anyone joining or leaving. Neither is read here --
    -- we only mark the cached roster stale, because the client's party
    -- memory does not update in the same breath as the packet that
    -- announced the change. Entity.refresh_party then re-reads on the next
    -- action.
    --
    -- Without this the roster is refreshed ONLY by its 3-second TTL, and
    -- only when an action packet arrives to consult it. An alliance that
    -- pulls the moment it is assembled therefore spends its first seconds
    -- with the other twelve members absent from the name set that classify()
    -- tests against, and every one of their swings is dropped.
    if id == E.Packet.ALLIANCE or id == E.Packet.PARTY then
        Entity.party_changed()
        return
    end

    if id ~= E.Packet.ACTION then return end
    if not injected and is_duplicate(data) then
        S.dupes = S.dupes + 1
        return
    end

    local now = os.time()

    -- Lazily (re)open: the character is not known at load time when the
    -- addon is loaded from the character select screen, and a failed write
    -- drops the handle. Either way it is this load's one file.
    if not Emit.is_open() then
        S.owner = Entity.me() or S.owner
        if S.owner then Emit.open(S.owner) end
        if not Emit.is_open() then return end
    end

    if Entity.is_zoning() then return end

    local job_changes = Entity.refresh_party(now)
    if job_changes then record_jobs(job_changes, now) end

    -- THE ACTOR FIRST, THE PACKET SECOND. Most action packets in a busy zone
    -- are a stranger's or a monster's, and all that is ever wanted from one of
    -- those is a reaction our side dealt back -- which a walk over the bits
    -- answers without building a table. So the actor is resolved from its id
    -- alone, and the packet is decoded in full only when there is something
    -- in it to write.
    local actor_id = Action.actor_id(data)
    if not actor_id then S.dropped = S.dropped + 1 return end

    local actor = Entity.by_id(actor_id)
    if not actor then return end

    local ours, actor_kind, pet_owner = classify(actor)
    -- The one reason a monster's own packet is still looked at: the damage
    -- our side dealt back on it.
    if not ours and not Action.has_reaction(data, E.Reaction, E.SpikeReaction) then
        return
    end

    local act = Action.parse(data)
    if not act then S.dropped = S.dropped + 1 return end

    if ours then
        record(act, actor, actor_kind, pet_owner, now)
    else
        record_reactions(act, actor, actor_kind, now)
    end
end

ashita.events.register('packet_in', 'vibexi_packet_in', function(p)
    -- Wrapped whole: a decode bug must never surface as a Lua error on the
    -- game's thread. A dropped packet is a missing row; a thrown error is a
    -- broken client.
    pcall(on_packet_in, p)

    -- Whatever this packet wrote goes to the file now, in one flush. Emit.flush
    -- cannot throw.
    Emit.flush()

    -- ALWAYS false. True would block the packet from reaching the client.
    return false
end)
