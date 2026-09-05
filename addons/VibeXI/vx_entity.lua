-- Reads the client's own entity and party tables.
--
-- This is the module that makes the packet data useful: the packet carries
-- numeric ids, and everything that turns an id into "Hasaya, a party member" or
-- "Goblin Pathfinder, a monster" happens here.
--
-- It is also what retires the chat parser's guesswork. `spawn_flags` states
-- outright whether an entity is a player, a monster or a pet, so the article
-- heuristic ("the Goblin..."), the fixed-point propagation over who-fights-whom,
-- and every `guess: true` event become unnecessary.
--
-- Every call in here is a getter. Nothing in this file can write to game memory
-- or to the network -- see addon-dev/ALLOWED_APIS.txt.

local E = require('vx_enums')

local M = {}

-- name -> true, for everyone currently in the party or alliance.
M.party = {}
M.party_stamp = 0
local PARTY_TTL = 3          -- seconds; matches what Metrics uses

-- name -> { main, main_lvl, sub, sub_lvl }, for everyone we have ever seen a
-- job for. Kept for the whole session rather than rebuilt per refresh: see
-- M.note_job for why a member who left is not forgotten.
M.jobs = {}

local MAX_ENTITY_INDEX = 2303

--- Entity index for a server id.
--- The low 11 bits of a server id are usually the index, so try that first and
--- only fall back to the linear scan when it does not match. `% 2048` is the
--- same as masking 0x7FF and avoids pulling in the bit library.
function M.index_by_id(id)
    local mm = AshitaCore:GetMemoryManager()
    if not mm then return 0 end
    local em = mm:GetEntity()
    if not em then return 0 end

    local guess = id % 2048
    if em:GetServerId(guess) == id then return guess end

    for i = 1, MAX_ENTITY_INDEX do
        if em:GetServerId(i) == id then return i end
    end
    return 0
end

--- Entity record for an index. Returns nil when the slot is empty, which the
--- callers treat as "skip this action" rather than as an error.
function M.by_index(index)
    if not index or index <= 0 then return nil end

    local mm = AshitaCore:GetMemoryManager()
    if not mm then return nil end
    local em = mm:GetEntity()
    if not em then return nil end

    local name = em:GetName(index)
    if not name or name == '' then return nil end

    return {
        name        = name,
        id          = em:GetServerId(index),
        index       = index,
        spawn_flags = em:GetSpawnFlags(index),
        pet_index   = em:GetPetTargetIndex(index),
        claim_id    = em:GetClaimStatus(index),
        hpp         = em:GetHPPercent(index),
    }
end

function M.by_id(id)
    return M.by_index(M.index_by_id(id))
end

--- Is a single spawn-flag bit set?
--- Arithmetic rather than bit.band for the same reason vx_bitreader is: it keeps
--- the addon's external surface one API smaller (see ALLOWED_APIS.txt), and the
--- masks here are single bits, so a divide-and-mod is exactly a band.
local function has(flags, mask)
    return math.floor(flags / mask) % 2 == 1
end

--- 'player' | 'pet' | 'mob' | 'npc' | 'other'
--- This is the field the UI's roster becomes a thin wrapper over.
---
--- ORDER MATTERS, because the bits overlap. Trusts and pets are both NPC|ALLY;
--- only the TRUST bit separates them, so it has to be tested first. Everything
--- ALLY that is not a trust (pets, adventuring fellows) is a pet.
function M.kind(ent)
    if not ent or not ent.spawn_flags then return 'other' end
    local f = ent.spawn_flags
    local SF = E.SpawnFlags

    if has(f, SF.TRUST)   then return 'player' end   -- trusts count as our damage
    if has(f, SF.ALLY)    then return 'pet' end
    if has(f, SF.MONSTER) then return 'mob' end
    if has(f, SF.PLAYER)  then return 'player' end
    if has(f, SF.NPC)     then return 'npc' end
    return 'other'
end

--- Record a member's jobs, and say whether that is news.
---
--- Returns the record when something CHANGED and nil when it did not, which is
--- what keeps the emitter quiet: the party table is read every three seconds
--- and nobody wants a line in the file every three seconds saying the same
--- warrior is still a warrior.
---
--- A MAIN JOB OF ZERO NEVER OVERWRITES A REAL ONE. The party table reports 0
--- for a member who is zoning, out of range, or mid-update, and taking that at
--- face value would blank a character's job -- and with it their colour in the
--- meter -- every time they crossed a zone line. Metrics guards the same way
--- (Ashita.Party.Update_Job); the first real answer wins and only another real
--- answer replaces it.
function M.note_job(name, main, main_lvl, sub, sub_lvl)
    if not name or name == '' then return nil end

    main     = main     or 0
    main_lvl = main_lvl or 0
    sub      = sub      or 0
    sub_lvl  = sub_lvl  or 0

    local had = M.jobs[name]
    if had and main == 0 then return nil end
    if had and had.main == main and had.main_lvl == main_lvl
           and had.sub == sub and had.sub_lvl == sub_lvl then
        return nil
    end

    local rec = { name = name, main = main, main_lvl = main_lvl,
                  sub = sub, sub_lvl = sub_lvl }
    M.jobs[name] = rec
    return rec
end

--- Refresh the party/alliance name set. Cheap enough to call per action, but
--- TTL-gated because it walks 18 slots and touches the entity table for each.
---
--- Returns the members whose jobs changed on THIS pass, or nil. The party table
--- is the only place a job is readable, so reading it is already happening here
--- and the extra four getters per occupied slot ride along for free.
function M.refresh_party(now, force)
    if not force and (now - M.party_stamp) < PARTY_TTL then return nil end
    M.party_stamp = now

    local mm = AshitaCore:GetMemoryManager()
    if not mm then return nil end
    local pt = mm:GetParty()
    if not pt then return nil end

    local fresh = {}
    local changed = nil
    for slot = 0, 17 do
        if pt:GetMemberIsActive(slot) == 1 then
            local name = pt:GetMemberName(slot)
            if name and name ~= '' then
                fresh[name] = true
                local rec = M.note_job(name,
                    pt:GetMemberMainJob(slot),  pt:GetMemberMainJobLevel(slot),
                    pt:GetMemberSubJob(slot),   pt:GetMemberSubJobLevel(slot))
                if rec then
                    changed = changed or {}
                    changed[#changed + 1] = rec
                end
            end
        end
    end
    M.party = fresh
    return changed
end

function M.in_party(name)
    if not name then return false end
    return M.party[name] == true
end

--- The owner entity of a pet, or nil. The chat log could never answer this;
--- `pet_index` on each party member's entity record does.
function M.pet_owner(pet)
    if not pet or not pet.index then return nil end

    local mm = AshitaCore:GetMemoryManager()
    if not mm then return nil end
    local pt = mm:GetParty()
    if not pt then return nil end

    for slot = 0, 17 do
        if pt:GetMemberIsActive(slot) == 1 then
            local owner = M.by_index(pt:GetMemberTargetIndex(slot))
            if owner and owner.pet_index == pet.index then return owner end
        end
    end
    return nil
end

--- Our own character's name, used to name the output file.
function M.me()
    local p = GetPlayerEntity()
    if not p or not p.Name or p.Name == '' then return nil end
    return p.Name
end

--- Are we mid-zone? Actions during a zone are stale/duplicated, so we skip them.
---
--- `GetIsZoning()` returns a NUMBER -- confirmed twice over: `uint32_t
--- GetIsZoning(void)` in plugins/sdk/Ashita.h, and `---@return number` on
--- IPlayer:GetIsZoning in Ashita's own LuaLS annotations. So `~= 0` is the
--- correct test and the number branch below is the live one.
---
--- The type dispatch stays anyway, because the failure mode it guards is silent
--- and total. In Lua `false ~= 0` is TRUE (values of different types are never
--- equal), so a bare `return x ~= 0` against a boolean-returning build would
--- report "always zoning" and the addon would record nothing at all, with no
--- error to explain it. Everything unrecognised fails open to not-zoning.
function M.is_zoning()
    local mm = AshitaCore:GetMemoryManager()
    if not mm then return false end
    local pl = mm:GetPlayer()
    if not pl then return false end

    local z = pl:GetIsZoning()
    if z == nil then return false end
    if type(z) == 'boolean' then return z end
    if type(z) == 'number' then return z ~= 0 end
    return false
end

return M
